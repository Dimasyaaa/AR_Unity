using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Показывает изображение камеры на Android, если устройство не может запустить AR-сессию.
/// Сам экран отчёта не создаёт: получает готовое текстовое поле от контроллера отчёта.
/// </summary>
public sealed class InventoryReportCameraFallback : MonoBehaviour
{
    public bool CanCapture { get; private set; }

#if UNITY_ANDROID && !UNITY_EDITOR
    private TextMeshProUGUI status;
    private bool shown;
    private bool fallbackStarted;
    private bool frameReceived;
    private WebCamTexture feed;
    private Material material;
    private GameObject background;
    private Camera targetCamera;
    private float startedAt;
#endif

    public void Show(TextMeshProUGUI message)
    {
        // Разрешаем съёмку и при необходимости запускаем камеру вместо AR-сессии.
        CanCapture = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        status = message;
        shown = true;
        Update();
#endif
    }

    public void Hide()
    {
        // При закрытии экрана съёмки останавливаем камеру и освобождаем ресурсы.
        CanCapture = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        shown = false;
        ReleaseCamera();
#endif
    }

    private void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!shown) return;
        if (!fallbackStarted &&
            (ARSession.state == ARSessionState.Unsupported || ARSession.state == ARSessionState.NeedsInstall))
            StartFallback();

        if (feed == null) return;
        if (feed.didUpdateThisFrame && feed.width > 16 && feed.height > 16)
        {
            if (!frameReceived)
            {
                frameReceived = true;
                CanCapture = true;
                status.text = "Поместите нужные карточки в кадр.";
            }
            FitCameraFeed();
        }
        else if (!frameReceived && Time.realtimeSinceStartup - startedAt > 8f)
        {
            ReleaseCamera();
            fallbackStarted = true;
            status.text = "Камера не передаёт изображение. Откройте экран камеры заново.";
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void StartFallback()
    {
        // Запасной режим используется на Android после запроса разрешения на камеру.
        fallbackStarted = true;
        CanCapture = false;
        status.text = "Подключаем камеру…";
        try
        {
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => { if (this != null && shown) StartFallback(); };
                callbacks.PermissionDenied += _ => { if (this != null && shown) status.text = "Разрешите доступ к камере в настройках Android."; };
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, callbacks);
                return;
            }

            var device = WebCamTexture.devices.FirstOrDefault(candidate => !candidate.isFrontFacing);
            var shader = Resources.Load<Shader>("InventoryReportCameraBackground");
            targetCamera = Camera.main;
            if (string.IsNullOrEmpty(device.name) || shader == null || targetCamera == null)
                throw new InvalidOperationException("Камера недоступна.");

            feed = new WebCamTexture(device.name, 1280, 720, 30);
            material = new Material(shader);
            material.mainTexture = feed;
            background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "InventoryReportCameraBackground";
            background.transform.SetParent(targetCamera.transform, false);
            background.layer = targetCamera.gameObject.layer;
            Destroy(background.GetComponent<Collider>());
            var renderer = background.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.enabled = false;
            feed.Play();
            startedAt = Time.realtimeSinceStartup;
        }
        catch (Exception error)
        {
            ReleaseCamera();
            fallbackStarted = true;
            status.text = "Не удалось открыть камеру.";
            Debug.LogWarning("[Inventory report] " + error.Message);
        }
    }

    private void FitCameraFeed()
    {
        // Учитываем поворот кадра и подгоняем видеопоток под поле зрения камеры.
        int rotation = feed.videoRotationAngle;
        float cameraWidth = feed.width;
        float cameraHeight = feed.height;
        float depth = targetCamera.farClipPlane * .99f;
        float viewportHeight = targetCamera.orthographic ? 2 * targetCamera.orthographicSize :
            2 * depth * Mathf.Tan(targetCamera.fieldOfView * .5f * Mathf.Deg2Rad);
        float viewportWidth = viewportHeight * targetCamera.aspect;
        bool quarterTurn = rotation % 180 != 0;
        float scale = Mathf.Max(viewportWidth / (quarterTurn ? cameraHeight : cameraWidth),
            viewportHeight / (quarterTurn ? cameraWidth : cameraHeight));
        var screen = background.transform;
        screen.localPosition = new Vector3(0, 0, depth);
        screen.localRotation = Quaternion.Euler(0, 0, -rotation);
        screen.localScale = new Vector3(cameraWidth * scale, cameraHeight * scale, 1);
        material.mainTextureScale = new Vector2(1, feed.videoVerticallyMirrored ? -1 : 1);
        material.mainTextureOffset = new Vector2(0, feed.videoVerticallyMirrored ? 1 : 0);
        background.GetComponent<MeshRenderer>().enabled = true;
    }

    private void ReleaseCamera()
    {
        // Останавливаем поток и удаляем созданные для него Unity-ресурсы.
        if (feed != null) { feed.Stop(); Destroy(feed); feed = null; }
        if (background != null) { background.SetActive(false); Destroy(background); background = null; }
        if (material != null) { Destroy(material); material = null; }
        fallbackStarted = false;
        frameReceived = false;
    }
#endif

    private void OnDisable() => Hide();

#if UNITY_ANDROID && !UNITY_EDITOR
    private void OnApplicationPause(bool paused)
    {
        if (!shown) return;
        if (paused && feed != null)
        {
            ReleaseCamera();
            CanCapture = false;
        }
    }
#endif
}
