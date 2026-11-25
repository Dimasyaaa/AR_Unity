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
    // Содержимое QR отклонено правилами назначенной инвентаризации.
    public event Action<string> OnScanRejected;

    [Header("Тестирование в Unity Editor")]
    [SerializeField] private string editorTestQrCode = "test_box_01";
    [SerializeField] private string[] editorTestQrCodes =
    {
        "test_box_01",
        "test_box_02",
        "test_box_03",
        "test_box_04",
        "test_box_05"
    };
    [SerializeField, Min(0)] private int editorTestQrCodeIndex;
    [SerializeField] private bool editorAdvanceQrAfterSuccessfulScan = true;
    [SerializeField, Min(0.1f)] private float editorScanDelaySeconds = 0.75f;

    private bool isScanning = false;
    private bool scanningFromAR;

#if UNITY_EDITOR
    private void Update()
    {
        // Клавиши удобны в Game View: можно выбрать отдельную коробку до сканирования.
        if (Input.GetKeyDown(KeyCode.F6)) ShiftEditorTestQrCode(-1);
        if (Input.GetKeyDown(KeyCode.F7)) ShiftEditorTestQrCode(1);
    }
#endif

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
        Debug.Log($"[QR] Режим Editor: через {editorScanDelaySeconds:0.##} с будет проверен QR «{GetEditorTestQrCode()}».");
        Invoke(nameof(TestScan), Mathf.Max(0.1f, editorScanDelaySeconds));
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
        ProcessScannedQr(result);
    }

    // Общая обработка результата нужна и нативному сканеру, и симуляции в редакторе.
    private bool ProcessScannedQr(string result)
    {
        Debug.Log($"[QR] Scanned result: {result}");
        isScanning = false;

        if (string.IsNullOrWhiteSpace(result))
        {
            Debug.LogError("[QR] Получена пустая строка QR-кода.");
            OnScanCancelled?.Invoke();
            scanningFromAR = false;
            return false;
        }

        if (result == "CANCELLED")
        {
            Debug.Log("[QR] Scanning cancelled by user");
            OnScanCancelled?.Invoke();
            scanningFromAR = false;
            return false;
        }
        else if (result.StartsWith("ERROR:"))
        {
            Debug.LogError($"[QR] Error: {result.Substring(6)}");
            OnScanCancelled?.Invoke();
            scanningFromAR = false;
            return false;
        }
        else
        {
            if (scanningFromAR)
            {
                string rejectionMessage = GetArScanRejection(result);
                if (!string.IsNullOrWhiteSpace(rejectionMessage))
                    return RejectScannedQr(rejectionMessage);
            }

            GameDataManager.LastScannedQR = result; // сохраняем глобально
            OnQRScanned?.Invoke(result);

            if (scanningFromAR)
            {
                SaveARScan(result);
                // Перезагружаем AR-сцену, чтобы карточки прочитали данные новой коробки.
                SceneManager.LoadScene("SampleScene");
            }
        }

        scanningFromAR = false;
        return true;
    }

    /// <summary>Проверяет, что QR-код относится к текущему назначению сотрудника.</summary>
    private static string GetArScanRejection(string qrCode)
    {
        if (!SessionManager.IsLoggedIn)
        {
#if UNITY_EDITOR
            // В Editor сохраняется прямой запуск SampleScene для отладки карточек.
            return null;
#else
            return "Сначала войдите в систему, чтобы сканировать назначенные коробки.";
#endif
        }

        LocalDatabase database = LocalDatabase.Instance;
        if (database == null || !database.IsReady)
        {
#if UNITY_EDITOR
            return null;
#else
            return "Назначение коробок ещё не загружено. Вернитесь в меню и попробуйте снова.";
#endif
        }

        LocalInventoryBatch batch = database.GetActiveInventoryBatch(SessionManager.UserId);
        if (batch == null)
        {
#if UNITY_EDITOR
            return null;
#else
            return "Нет активной описи. Вернитесь в меню и проверьте её в панели администратора.";
#endif
        }

        if (!database.IsQrAssignedToBatch(batch.client_batch_id, qrCode))
            return $"QR «{qrCode}» не входит в активную опись. Отсканируйте QR-код из неё.";

        if (database.HasCompletedBox(batch.client_batch_id, qrCode))
            return $"Коробка «{qrCode}» уже проверена. Отсканируйте следующую назначенную коробку.";

        return null;
    }

    /// <summary>Завершает попытку неверного QR и передаёт экрану понятную причину.</summary>
    private bool RejectScannedQr(string message)
    {
        Debug.LogWarning("[QR] " + message);
        OnScanRejected?.Invoke(message);
        OnScanCancelled?.Invoke();
        scanningFromAR = false;
        return false;
    }

    // Эмуляция QR-кода в редакторе. На Android используется результат камеры.
    private void TestScan()
    {
        string qrCode = GetEditorTestQrCode();
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            Debug.LogError("[QR] Не задан тестовый QR-код для Unity Editor.");
            ProcessScannedQr("ERROR: не задан тестовый QR-код");
            return;
        }

        Debug.Log($"[QR] Game Mode: имитация сканирования «{qrCode}».");
        bool accepted = ProcessScannedQr(qrCode);
#if UNITY_EDITOR
        if (accepted && editorAdvanceQrAfterSuccessfulScan)
            ShiftEditorTestQrCode(1, false);
#endif
    }

    private string GetEditorTestQrCode()
    {
        if (editorTestQrCodes != null && editorTestQrCodes.Length > 0)
        {
            int index = Mathf.Clamp(editorTestQrCodeIndex, 0, editorTestQrCodes.Length - 1);
            string selectedCode = editorTestQrCodes[index]?.Trim();
            if (!string.IsNullOrWhiteSpace(selectedCode))
                return selectedCode;
        }

        return editorTestQrCode?.Trim() ?? string.Empty;
    }

#if UNITY_EDITOR
    /// <summary>Для нового прогона выбирает первую назначенную коробку, которую ещё не проверили.</summary>
    public void SelectNextUncompletedEditorQr(long userId)
    {
        if (editorTestQrCodes == null || editorTestQrCodes.Length == 0)
            return;

        LocalDatabase database = LocalDatabase.Instance;
        if (database == null || !database.IsReady)
            return;

        LocalInventoryBatch batch = database.GetActiveInventoryBatch(userId);
        if (batch == null)
            return;

        for (int i = 0; i < editorTestQrCodes.Length; i++)
        {
            string qrCode = editorTestQrCodes[i]?.Trim();
            if (string.IsNullOrWhiteSpace(qrCode))
                continue;

            bool assigned = database.IsQrAssignedToBatch(batch.client_batch_id, qrCode);
            bool completed = database.HasCompletedBox(batch.client_batch_id, qrCode);
            if (assigned && !completed)
            {
                editorTestQrCodeIndex = i;
                Debug.Log($"[QR] Тест Unity выбрал следующую коробку: «{qrCode}».");
                return;
            }
        }
    }

    private void ShiftEditorTestQrCode(int direction, bool logSelection = true)
    {
        if (editorTestQrCodes == null || editorTestQrCodes.Length == 0)
        {
            Debug.LogWarning("[QR] Заполните список тестовых QR-кодов в компоненте QRScanner.");
            return;
        }

        int count = editorTestQrCodes.Length;
        editorTestQrCodeIndex = (editorTestQrCodeIndex + direction % count + count) % count;
        if (logSelection)
            Debug.Log($"[QR] Выбрана тестовая коробка «{GetEditorTestQrCode()}». F6 — предыдущая, F7 — следующая.");
    }
#endif

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
