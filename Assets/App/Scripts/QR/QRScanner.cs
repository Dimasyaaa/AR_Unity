using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using ArInventory.Local;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// Управляет процессом сканирования QR-кодов. Синглтон с глобальным доступом.
public class QRScanner : MonoBehaviour
{
    public static QRScanner Instance { get; private set; }

    // Событие успешного сканирования
    public event Action<string> OnQRScanned;
    // Пользователь закрыл сканер или запуск завершился ошибкой.
    public event Action OnScanCancelled;

    [Header("Тестирование в Unity Editor")]
    //[SerializeField] private string editorTestQrCode = "DEMO-CABINET-002";
    //[SerializeField] private string editorTestQrCode = "DEMO-CABINET";
    [SerializeField] private string editorTestQrCode = "test2";

    private bool isScanning = false;
    private bool scanningFromAR;
    private GameObject rescanCanvas;
    private Button rescanButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureScannerExistsInStandaloneARScene()
    {
        if (SceneManager.GetActiveScene().name == "SampleScene" && Instance == null)
            new GameObject("QRManager").AddComponent<QRScanner>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (SceneManager.GetActiveScene().name == "SampleScene")
            CreateARRescanButton();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "SampleScene")
            CreateARRescanButton();
        else if (rescanCanvas != null)
        {
            Destroy(rescanCanvas);
            rescanCanvas = null;
            rescanButton = null;
        }
    }

    public void StartScanning()
    {
        if (isScanning) return;
        isScanning = true;
        scanningFromAR = SceneManager.GetActiveScene().name == "SampleScene";
        if (rescanButton != null)
            rescanButton.interactable = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            LaunchNativeScanner();
        }
        else
        {
            PermissionCallbacks callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += OnPermissionGranted;
            callbacks.PermissionDenied += OnPermissionDenied;
            callbacks.PermissionDeniedAndDontAskAgain += OnPermissionDenied;
            Permission.RequestUserPermissions(new[] { Permission.Camera }, callbacks);
        }
#else
        Debug.Log("[QR] Editor mode - simulating scan");
        Invoke(nameof(TestScan), 2);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void OnPermissionGranted(string permission)
    {
        Debug.Log($"[QR] Permission granted: {permission}");
        LaunchNativeScanner();
    }

    private void OnPermissionDenied(string permission)
    {
        Debug.LogError($"[QR] Permission denied: {permission}");
        isScanning = false;
        scanningFromAR = false;
        UpdateRescanButton(true);
        OnScanCancelled?.Invoke();
    }

    private void LaunchNativeScanner()
    {
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = new AndroidJavaObject(
                "android.content.Intent",
                currentActivity,
                new AndroidJavaClass("com.unity.template.ar_mobile.QRScannerActivity")
            );
            currentActivity.Call("startActivity", intent);
            Debug.Log("[QR] Native scanner launched");
        }
        catch (Exception e)
        {
            Debug.LogError($"[QR] Failed to launch scanner: {e.Message}");
            isScanning = false;
            scanningFromAR = false;
            UpdateRescanButton(true);
            OnScanCancelled?.Invoke();
        }
    }
#endif

    // Вызывается из нативного Android-кода после сканирования
    public void OnNativeQRScanned(string result)
    {
        Debug.Log($"[QR] Scanned result: {result}");
        isScanning = false;

        if (result == "CANCELLED")
        {
            Debug.Log("[QR] Scanning cancelled by user");
            UpdateRescanButton(true);
            OnScanCancelled?.Invoke();
        }
        else if (result.StartsWith("ERROR:"))
        {
            Debug.LogError($"[QR] Error: {result.Substring(6)}");
            UpdateRescanButton(true);
            OnScanCancelled?.Invoke();
        }
        else
        {
            GameDataManager.LastScannedQR = result; // сохраняем глобально
            OnQRScanned?.Invoke(result);

            if (scanningFromAR)
            {
                SaveARScan(result);
                // Rebuild the AR scene so every card reads the newly scanned cabinet.
                SceneManager.LoadScene("SampleScene");
            }
        }

        scanningFromAR = false;
    }

    // Эмуляция QR шкафа в редакторе. На Android используется фактический результат сканера.
    private void TestScan()
    {
        Debug.Log("[QR] TestScan fired");
        OnNativeQRScanned(editorTestQrCode);
    }

    private void SaveARScan(string qrCode)
    {
        if (!SessionManager.IsLoggedIn)
            return;

        LocalClient client = LocalClient.GetOrCreate();
        client.SendScan(
            qrCode,
            response => Debug.Log($"[QR] Повторное сканирование сохранено: {response.objectName}"),
            error => Debug.LogWarning($"[QR] Сканирование не сохранено: {error}"));
    }

    private void CreateARRescanButton()
    {
        if (rescanCanvas != null)
            Destroy(rescanCanvas);

        rescanCanvas = new GameObject("AR QR Rescan UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = rescanCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;

        CanvasScaler scaler = rescanCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        rescanButton = CreateNavigationButton(
            "Rescan QR Button",
            "⌕  Сканировать другой QR-код",
            new Vector2(28, -28),
            StartScanning);

        CreateNavigationButton(
            "Main Menu Button",
            "⌂  Главное меню",
            new Vector2(484, -28),
            ReturnToMainMenu);
    }

    private Button CreateNavigationButton(string objectName, string labelText, Vector2 anchoredPosition, Action onClick)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(rescanCanvas.transform, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(440, 92);

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(0.035f, 0.12f, 0.22f, 0.94f);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => onClick());
        ColorBlock colors = button.colors;
        colors.normalColor = background.color;
        colors.highlightedColor = new Color(0.08f, 0.34f, 0.52f, 1f);
        colors.pressedColor = new Color(0.03f, 0.23f, 0.36f, 1f);
        button.colors = colors;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(14, 6);
        labelRect.offsetMax = new Vector2(-14, -6);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 31;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 20;
        label.fontSizeMax = 31;
        label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;

        return button;
    }

    private void ReturnToMainMenu()
    {
        CancelInvoke(nameof(TestScan));
        isScanning = false;
        scanningFromAR = false;
        SceneManager.LoadScene("StartScene");
    }

    private void UpdateRescanButton(bool interactable)
    {
        if (rescanButton != null)
            rescanButton.interactable = interactable;
    }
}
