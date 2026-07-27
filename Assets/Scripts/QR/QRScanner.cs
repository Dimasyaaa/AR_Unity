using System;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class QRScanner : MonoBehaviour
{
    public static QRScanner Instance { get; private set; }
    public event Action<string> OnQRScanned;
    private bool isScanning = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
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
            // Создаём callbacks для обработки разрешений
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
            OnQRScanned?.Invoke(result);
        }
    }

    private void TestScan()
    {
        isScanning = false;
        OnQRScanned?.Invoke("TEST_QR_001");
    }
}