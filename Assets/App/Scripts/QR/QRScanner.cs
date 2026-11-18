using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using ArInventory.Local;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// Управляет процессом сканирования QR-кодов. Синглтон с глобальным доступом.
public class QRScanner : MonoBehaviour
{
    public static QRScanner Instance { get; private set; }
    public bool IsScanning => isScanning;

    // Событие успешного сканирования
    public event Action<string> OnQRScanned;
    // Пользователь закрыл сканер или запуск завершился ошибкой.
    public event Action OnScanCancelled;

    [Header("Тестирование в Unity Editor")]
    [SerializeField] private string editorTestQrCode = "test2";

    private bool isScanning = false;
    private bool scanningFromAR;

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
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void StartScanning()
    {
        if (isScanning) return;
        isScanning = true;
        scanningFromAR = SceneManager.GetActiveScene().name == "SampleScene";

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
        if (!isScanning) return;
        Debug.Log($"[QR] Permission granted: {permission}");
        LaunchNativeScanner();
    }

    private void OnPermissionDenied(string permission)
    {
        Debug.LogError($"[QR] Permission denied: {permission}");
        isScanning = false;
        scanningFromAR = false;
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
            OnScanCancelled?.Invoke();
        }
        else if (result.StartsWith("ERROR:"))
        {
            Debug.LogError($"[QR] Error: {result.Substring(6)}");
            OnScanCancelled?.Invoke();
        }
        else
        {
            GameDataManager.LastScannedQR = result; // сохраняем глобально
            OnQRScanned?.Invoke(result);

            if (scanningFromAR)
            {
                SaveARScan(result);
                // Перезагружаем AR-сцену, чтобы карточки прочитали данные нового шкафа.
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

    // Останавливает ожидающее тестовое сканирование перед уходом со сцены.
    public void CancelScanning()
    {
        CancelInvoke(nameof(TestScan));
        bool wasScanning = isScanning;
        isScanning = false;
        scanningFromAR = false;
        if (wasScanning)
            OnScanCancelled?.Invoke();
    }

    private void SaveARScan(string qrCode)
    {
        if (!SessionManager.IsLoggedIn)
            return;

        LocalClient client = LocalClient.GetOrCreate();
        client.SendScan(
            qrCode,
            response => Debug.Log($"[QR] Сканирование сохранено: {response.objectName}; повтор: {response.wasRepeated}"),
            error => Debug.LogWarning($"[QR] Сканирование не сохранено: {error}"));
    }

}
