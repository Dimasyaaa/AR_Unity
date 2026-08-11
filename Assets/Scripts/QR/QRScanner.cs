using System;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// Управляет процессом сканирования QR-кодов. Реализован как синглтон для глобального доступа.
public class QRScanner : MonoBehaviour
{
    public static QRScanner Instance { get; private set; }

    // Событие, вызываемое при успешном сканировании QR-кода
    public event Action<string> OnQRScanned;

    private bool isScanning = false;

    void Awake()
    {
        // Реализация паттерна Singleton. Если экземпляр уже есть, удаляем дубликат.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Сохраняем объект при переходе между сценами
        DontDestroyOnLoad(gameObject);
    }

    public void StartScanning()
    {
        if (isScanning) return;
        isScanning = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        // На Android проверяем наличие прав на использование камеры
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            LaunchNativeScanner();
        }
        else
        {
            // Если прав нет, запрашиваем их с обработчиками результатов
            PermissionCallbacks callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += OnPermissionGranted;
            callbacks.PermissionDenied += OnPermissionDenied;
            callbacks.PermissionDeniedAndDontAskAgain += OnPermissionDenied;
            
            Permission.RequestUserPermissions(new[] { Permission.Camera }, callbacks);
        }
#else
        // В редакторе Unity имитируем сканирование через 2 секунды
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
    }

    // Запуск нативной Android-активности для сканирования через Java-интероп
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
        }
    }
#endif

    // Этот метод вызывается из нативного Android-кода после сканирования
    public void OnNativeQRScanned(string result)
    {
        Debug.Log($"[QR] Scanned result: {result}");
        isScanning = false;

        if (result.StartsWith("ERROR:"))
        {
            Debug.LogError($"[QR] Error: {result.Substring(6)}");
        }
        else
        {
            GameDataManager.LastScannedQR = result; 
            OnQRScanned?.Invoke(result);
        }
    }

    // Имитация успешного сканирования для тестирования в редакторе Unity
    private void TestScan()
    {
        isScanning = false;
        GameDataManager.LastScannedQR = "TEST_QR_001";
    }
}