using System;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

// Управляет процессом сканирования QR-кодов. Синглтон с глобальным доступом.
public class QRScanner : MonoBehaviour
{
    public static QRScanner Instance { get; private set; }

    // Событие успешного сканирования
    public event Action<string> OnQRScanned;

    private bool isScanning = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void StartScanning()
    {
        if (isScanning) return;
        isScanning = true;

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
        }
    }
#endif

    // Вызывается из нативного Android-кода после сканирования
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
            GameDataManager.LastScannedQR = result; // сохраняем глобально
            OnQRScanned?.Invoke(result);
        }
    }

    // Эмуляция скана в редакторе: сразу ваш реальный URL
    private void TestScan()
    {
        Debug.Log("[QR] TestScan fired");
        isScanning = false;
        string testUrl = "https://app.kolagmk.ru/dsq/93C1671C08844953074249C1F0CB239F";
        GameDataManager.LastScannedQR = testUrl;
        OnQRScanned?.Invoke(testUrl);
    }
}