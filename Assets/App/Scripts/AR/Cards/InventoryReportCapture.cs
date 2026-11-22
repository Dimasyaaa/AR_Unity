using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ArInventory.Local;

/// <summary>
/// Управляет фотографиями, текстовой заметкой и отправкой итогового отчёта инвентаризации.
/// </summary>
public sealed class InventoryReportCapture : MonoBehaviour
{
    private const int MaxReportPhotoBytes = 12 * 1024 * 1024;
    [SerializeField, Range(2f, 3f)] private float autoReturnDelaySeconds = 3f;
    public static InventoryReportCapture Active { get; private set; }

    private InventoryChecklistController checklist;
    private Canvas canvas;
    private RectTransform safeArea;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private GameObject cameraControls;
    private GameObject reportControls;
    private TextMeshProUGUI cameraStatus;
    private TextMeshProUGUI reportStatus;
    private TextMeshProUGUI recordLabel;
    private TMP_InputField noteInput;
    private Button cameraBack;
    private Button shutter;
    private Button addPhoto;
    private Button closeReport;
    private Button retake;
    private Button record;
    private Button play;
    private Button send;
    private RawImage preview;
    private AspectRatioFitter previewAspect;
    private Texture2D image;
    private readonly List<byte[]> reportPhotos = new List<byte[]>();
    private readonly Queue<DictationResult> dictationResults = new Queue<DictationResult>();
    private readonly object dictationLock = new object();
    private AndroidJavaObject dictationRecognizer;
    private AndroidJavaObject dictationIntent;
    private bool takingPhoto;
    private bool dictationActive;
    private bool dictationMicrophoneReserved;
    private bool sending;
    private bool submitted;
    private bool autoReturnPending;
    private bool configured;
    private InventoryReportCameraFallback cameraFallback;

    private void Awake()
    {
        BindSceneObjects();
    }

    public static void Open(InventoryChecklistController owner = null)
    {
        // Перед показом отчёта закрываем подсказки и режим установки AR-карточки.
        if (Active != null) return;
        VoiceHelpController.CloseIfOpen();
        ARCardModeSelector.CloseDialog();
        ARCardSpawnUtility.DisablePlacement();
        InventoryReportLauncher.OpenReport(owner);
    }

    /// <summary>Подготавливает экземпляр префаба перед показом экрана камеры.</summary>
    internal bool Initialize(InventoryChecklistController owner)
    {
        if (!configured) return false;
        checklist = owner;
        Active = this;
        cameraControls.SetActive(true);
        reportControls.SetActive(false);
        cameraFallback.Hide();
        UpdateSafeArea();
        ShowCamera();
        UpdateReportButtons();
        return true;
    }

    private void BindSceneObjects()
    {
        canvas = GetComponent<Canvas>();
        safeArea = transform.Find("SafeArea") as RectTransform;
        if (safeArea == null)
        {
            Debug.LogError("[Отчёт] В префабе InventoryReportCard нет объекта SafeArea.");
            enabled = false;
            return;
        }

        Transform cameraPanel = safeArea.Find("CameraControls");
        Transform cameraFooter = cameraPanel != null ? cameraPanel.Find("CameraFooter") : null;
        Transform reportPanel = safeArea.Find("ReportControls");
        Transform voiceActions = reportPanel != null ? reportPanel.Find("VoiceActions") : null;
        Transform reportActions = reportPanel != null ? reportPanel.Find("ReportActions") : null;
        Transform photoPreview = reportPanel != null ? reportPanel.Find("PhotoPreview/ScenePhoto") : null;

        cameraControls = cameraPanel != null ? cameraPanel.gameObject : null;
        reportControls = reportPanel != null ? reportPanel.gameObject : null;
        cameraStatus = cameraFooter != null ? cameraFooter.Find("CameraStatus")?.GetComponent<TextMeshProUGUI>() : null;
        cameraBack = cameraFooter != null ? cameraFooter.Find("CameraBackButton")?.GetComponent<Button>() : null;
        shutter = cameraFooter != null ? cameraFooter.Find("ShutterButton")?.GetComponent<Button>() : null;
        reportStatus = reportPanel != null ? reportPanel.Find("ReportStatus")?.GetComponent<TextMeshProUGUI>() : null;
        noteInput = reportPanel != null ? reportPanel.Find("VoiceActions/NoteInput")?.GetComponent<TMP_InputField>() : null;
        addPhoto = voiceActions != null ? voiceActions.Find("AddPhotoButton")?.GetComponent<Button>() : null;
        record = voiceActions != null ? voiceActions.Find("RecordButton")?.GetComponent<Button>() : null;
        play = voiceActions != null ? voiceActions.Find("PlayButton")?.GetComponent<Button>() : null;
        closeReport = reportActions != null ? reportActions.Find("CloseReportButton")?.GetComponent<Button>() : null;
        retake = reportActions != null ? reportActions.Find("RetakeButton")?.GetComponent<Button>() : null;
        send = reportActions != null ? reportActions.Find("SendButton")?.GetComponent<Button>() : null;
        preview = photoPreview != null ? photoPreview.GetComponent<RawImage>() : null;
        previewAspect = photoPreview != null ? photoPreview.GetComponent<AspectRatioFitter>() : null;
        recordLabel = record != null ? record.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        cameraFallback = GetComponent<InventoryReportCameraFallback>();

        bool ready = canvas != null && cameraControls != null && reportControls != null &&
            cameraStatus != null && cameraBack != null && shutter != null && reportStatus != null &&
            addPhoto != null && record != null && play != null && recordLabel != null && noteInput != null &&
            closeReport != null && retake != null && send != null && preview != null &&
            previewAspect != null && cameraFallback != null;
        if (!ready)
        {
            Debug.LogError("[Отчёт] В префабе InventoryReportCard не хватает элементов или компонентов. Проверьте имена дочерних объектов.");
            enabled = false;
            return;
        }

        configured = true;
        cameraBack.onClick.AddListener(Close);
        shutter.onClick.AddListener(TakePhoto);
        addPhoto.onClick.AddListener(ShowCamera);
        record.onClick.AddListener(StartDictation);
        play.onClick.AddListener(ClearNote);
        noteInput.onValueChanged.AddListener(_ => UpdateReportButtons());
        closeReport.onClick.AddListener(Close);
        retake.onClick.AddListener(ShowCamera);
        send.onClick.AddListener(Send);
    }

    private void Update()
    {
        UpdateSafeArea();
        if (cameraControls != null && cameraControls.activeSelf && !takingPhoto)
            shutter.interactable = cameraFallback.CanCapture;
        PollDictationResults();
    }

    private void ShowCamera()
    {
        // В кадр попадёт весь экран приложения: чек-лист и размещённые AR-карточки.
        if (dictationActive)
            CancelDictation("Надиктовка остановлена.");
        reportControls.SetActive(false);
        cameraControls.SetActive(true);
        cameraStatus.text = checklist != null
            ? "Поместите чек-лист и нужные карточки в кадр."
            : "В кадр попадут все видимые элементы сцены.";
        cameraFallback.Show(cameraStatus);
    }

    public void TakePhoto()
    {
        // Не запускаем несколько снимков одновременно и проверяем доступность камеры.
        if (takingPhoto || !cameraControls.activeSelf || !cameraFallback.CanCapture) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        using var version = new AndroidJavaClass("android.os.Build$VERSION");
        if (version.GetStatic<int>("SDK_INT") < 29 &&
            !UnityEngine.Android.Permission.HasUserAuthorizedPermission(InventoryReportPhoto.LegacyStoragePermission))
        {
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => { if (this != null) TakePhoto(); };
            callbacks.PermissionDenied += _ => { if (this != null) cameraStatus.text = "Разрешите доступ к хранилищу для сохранения фото."; };
            UnityEngine.Android.Permission.RequestUserPermission(InventoryReportPhoto.LegacyStoragePermission, callbacks);
            return;
        }
#endif
        takingPhoto = true;
        StartCoroutine(CaptureFrame());
    }

    private IEnumerator CaptureFrame()
    {
        // На один кадр скрываем экран камеры, чтобы элементы управления не попали в фото.
        canvas.enabled = false;
        yield return new WaitForEndOfFrame();
        string saved = null;
        try
        {
            var captured = InventoryReportPhoto.Capture(out var jpeg);
            try { saved = InventoryReportPhoto.SaveToGallery(jpeg); }
            catch
            {
                Destroy(captured);
                throw;
            }
            if (image != null) Destroy(image);
            image = captured;
            if (checklist != null)
            {
                int totalBytes = jpeg.Length;
                foreach (byte[] savedPhoto in reportPhotos) totalBytes += savedPhoto.Length;
                if (totalBytes > MaxReportPhotoBytes)
                    throw new InvalidOperationException("Общий размер фотографий превышает 12 МБ. Переснимите кадр.");
                reportPhotos.Add(jpeg);
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning("[Inventory report] " + error.Message);
            cameraStatus.text = "Не удалось сохранить фото. Попробуйте ещё раз.";
        }
        finally
        {
            canvas.enabled = true;
            takingPhoto = false;
        }
        if (saved == null) yield break;

        if (checklist == null)
        {
            cameraStatus.text = saved;
            Destroy(image);
            image = null;
            yield break;
        }

        cameraFallback.Hide();
        preview.texture = image;
        previewAspect.aspectRatio = image.width / (float)image.height;
        cameraControls.SetActive(false);
        reportControls.SetActive(true);
        // Полный путь к файлу полезен для диагностики, но на экране отчёта занимает место.
        // Пользователю показываем короткий статус, а фото остаётся сохранённым в галерее и отчёте.
        reportStatus.text = $"Фото сохранено. В отчёте снимков: {reportPhotos.Count}.\nМожно добавить ещё или надиктовать заметку.";
        UpdateReportButtons();
    }

    public void StartDictation()
    {
        // Android распознаёт короткую фразу и сразу помещает её текст в заметку отчёта.
        if (!reportControls.activeSelf || sending || submitted || dictationActive) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => { if (this != null) StartDictation(); };
            callbacks.PermissionDenied += _ => { if (this != null) reportStatus.text = "Разрешите доступ к микрофону в настройках Android."; };
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            return;
        }
        dictationActive = true;
        dictationMicrophoneReserved = true;
        VoiceController.Instance?.ReserveMicrophone(true);
        reportStatus.text = "Слушаю заметку… Скажите фразу после сигнала распознавания.";
        UpdateReportButtons();
        RunOnAndroidUiThread(CreateAndStartDictation);
#else
        reportStatus.text = "Надиктовка доступна в Android. В редакторе введите заметку в поле вручную.";
#endif
    }

    public void ClearNote()
    {
        if (sending || submitted || dictationActive) return;
        noteInput.text = string.Empty;
        reportStatus.text = "Текстовая заметка очищена.";
        UpdateReportButtons();
    }

    private void CancelDictation(string message)
    {
        if (!dictationActive) return;
        dictationActive = false;
        StopDictationRecognizer();
        if (dictationMicrophoneReserved)
        {
            dictationMicrophoneReserved = false;
            VoiceController.Instance?.ReserveMicrophone(false);
        }
        reportStatus.text = message;
        UpdateReportButtons();
    }

    private void CreateAndStartDictation()
    {
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var recognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
            using var androidVersion = new AndroidJavaClass("android.os.Build$VERSION");

            int apiLevel = androidVersion.GetStatic<int>("SDK_INT");
            bool onDeviceAvailable = apiLevel >= 31 &&
                recognizerClass.CallStatic<bool>("isOnDeviceRecognitionAvailable", activity);
            dictationRecognizer = onDeviceAvailable
                ? recognizerClass.CallStatic<AndroidJavaObject>("createOnDeviceSpeechRecognizer", activity)
                : recognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);
            dictationRecognizer.Call("setRecognitionListener", new DictationListener(this));

            dictationIntent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH");
            dictationIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE_MODEL", "free_form");
            dictationIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE", "ru-RU");
            dictationIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PREFER_OFFLINE", true);
            dictationIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PARTIAL_RESULTS", false);
            dictationIntent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.MAX_RESULTS", 1);
            dictationRecognizer.Call("startListening", dictationIntent);
        }
        catch (Exception error)
        {
            QueueDictationResult(string.Empty, "Не удалось запустить распознавание речи: " + error.Message);
        }
    }

    private void RunOnAndroidUiThread(Action action)
    {
        using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        activity.Call("runOnUiThread", new AndroidRunnable(action));
    }

    private void QueueDictationResult(string text, string error)
    {
        lock (dictationLock)
            dictationResults.Enqueue(new DictationResult(text, error));
    }

    private void PollDictationResults()
    {
        DictationResult result;
        lock (dictationLock)
        {
            if (dictationResults.Count == 0) return;
            result = dictationResults.Dequeue();
        }

        if (!dictationActive) return;
        dictationActive = false;
        if (!string.IsNullOrWhiteSpace(result.text))
        {
            string existing = noteInput.text.Trim();
            noteInput.text = string.IsNullOrEmpty(existing) ? result.text.Trim() : existing + " " + result.text.Trim();
            reportStatus.text = "Речь распознана и добавлена в текстовую заметку.";
        }
        else
            reportStatus.text = string.IsNullOrWhiteSpace(result.error) ? "Не удалось распознать фразу. Попробуйте ещё раз." : result.error;

        StopDictationRecognizer();
        if (dictationMicrophoneReserved)
        {
            dictationMicrophoneReserved = false;
            VoiceController.Instance?.ReserveMicrophone(false);
        }
        UpdateReportButtons();
    }

    private void StopDictationRecognizer()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            RunOnAndroidUiThread(() =>
            {
                try { dictationRecognizer?.Call("cancel"); } catch { }
                try { dictationRecognizer?.Call("destroy"); } catch { }
                dictationRecognizer?.Dispose();
                dictationRecognizer = null;
                dictationIntent?.Dispose();
                dictationIntent = null;
            });
        }
        catch (Exception error) { Debug.LogWarning("[Отчёт] Не удалось закрыть распознаватель: " + error.Message); }
#endif
    }

    public void Send()
    {
        // Для отправки нужны завершённый чек-лист и фото; текстовую заметку можно не добавлять.
        if (sending || submitted || dictationActive || reportPhotos.Count == 0 || checklist == null)
            return;
        sending = true;
        UpdateReportButtons();
        checklist.SubmitReport(reportPhotos, noteInput.text.Trim(), (savedLocally, message) =>
        {
            if (this == null) return;
            sending = false;
            submitted = savedLocally;
            reportStatus.text = message;
            UpdateReportButtons();
            if (savedLocally)
                StartCoroutine(ReturnToNextAssignedBox(message));
        }, message => reportStatus.text = message);
    }

    /// <summary>После сохранения отчёта возвращает сотрудника к следующему назначенному QR-коду.</summary>
    private IEnumerator ReturnToNextAssignedBox(string savedMessage)
    {
        autoReturnPending = true;
        UpdateReportButtons();
        reportStatus.text = (string.IsNullOrWhiteSpace(savedMessage) ? "Отчёт сохранён." : savedMessage) +
                            $"\nЧерез {autoReturnDelaySeconds:0.#} сек. откроется сканер следующей коробки.";

        yield return new WaitForSecondsRealtime(Mathf.Clamp(autoReturnDelaySeconds, 2f, 3f));

        LocalDatabase database = LocalDatabase.Instance;
        LocalInventoryBatch batch = database != null && database.IsReady
            ? database.GetActiveInventoryBatch(SessionManager.UserId)
            : null;
        bool hasNextAssignedBox = batch != null &&
            database.GetCompletedBoxCount(batch.client_batch_id) < batch.expected_box_count;

        if (hasNextAssignedBox)
        {
            QRNavigationController navigation = FindAnyObjectByType<QRNavigationController>(FindObjectsInactive.Include);
            if (navigation != null)
            {
                navigation.ContinueAfterReport(gameObject);
                yield break;
            }

            Destroy(gameObject);
            MainMenuManager.ResumeAssignedInventoryScan();
            yield break;
        }

        // Последняя коробка завершена: главное меню покажет сотруднику сводную таблицу.
        Destroy(gameObject);
        SceneManager.LoadScene("StartScene");
    }

    public void Close()
    {
        // Не закрываем экран, пока снимок или отправка ещё выполняются.
        if (sending || takingPhoto) return;
        Destroy(gameObject);
    }

    private void UpdateReportButtons()
    {
        // Доступность кнопок зависит от текущего этапа заполнения отчёта.
        record.interactable = !dictationActive && !sending && !submitted;
        recordLabel.text = dictationActive ? "Слушаю…" : "Надиктовать";
        addPhoto.interactable = !sending && !submitted && !autoReturnPending;
        closeReport.interactable = !sending && !autoReturnPending;
        retake.interactable = !dictationActive && !sending && !submitted;
        play.interactable = !string.IsNullOrWhiteSpace(noteInput.text) && !dictationActive && !sending && !submitted;
        send.interactable = !dictationActive && !sending && !submitted && reportPhotos.Count > 0;
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused) return;
        CancelDictation("Надиктовка остановлена при сворачивании приложения.");
    }

    private void OnDestroy()
    {
        dictationActive = false;
        StopDictationRecognizer();
        if (dictationMicrophoneReserved)
        {
            dictationMicrophoneReserved = false;
            VoiceController.Instance?.ReserveMicrophone(false);
        }
        if (image != null) Destroy(image);
        if (Active == this) Active = null;
    }

    private void UpdateSafeArea()
    {
        // Пересчитываем безопасную область после смены ориентации или размера экрана.
        var size = new Vector2Int(Screen.width, Screen.height);
        var area = Screen.safeArea;
        if (size == lastScreenSize && area == lastSafeArea) return;
        if (size.x <= 0 || size.y <= 0) return;
        lastScreenSize = size;
        lastSafeArea = area;
        safeArea.anchorMin = new Vector2(area.xMin / size.x, area.yMin / size.y);
        safeArea.anchorMax = new Vector2(area.xMax / size.x, area.yMax / size.y);
        safeArea.offsetMin = Vector2.zero;
        safeArea.offsetMax = Vector2.zero;
    }

    /// <summary>Результат распознавания передаётся из потока Android в Update Unity.</summary>
    private readonly struct DictationResult
    {
        public readonly string text;
        public readonly string error;
        public DictationResult(string text, string error) { this.text = text; this.error = error; }
    }

    /// <summary>Адаптер Android RecognitionListener: обновления интерфейса выполняются позже на потоке Unity.</summary>
    private sealed class DictationListener : AndroidJavaProxy
    {
        private readonly InventoryReportCapture owner;
        public DictationListener(InventoryReportCapture owner) : base("android.speech.RecognitionListener") => this.owner = owner;
        public void onReadyForSpeech(AndroidJavaObject parameters) { }
        public void onBeginningOfSpeech() { }
        public void onRmsChanged(float value) { }
        public void onBufferReceived(byte[] buffer) { }
        public void onEndOfSpeech() { }
        public void onError(int errorCode) => owner?.QueueDictationResult(string.Empty, DescribeSpeechError(errorCode));
        public void onResults(AndroidJavaObject results)
        {
            try
            {
                using var recognized = results.Call<AndroidJavaObject>("getStringArrayList", "results_recognition");
                string text = recognized != null && recognized.Call<int>("size") > 0
                    ? recognized.Call<string>("get", 0)
                    : string.Empty;
                owner?.QueueDictationResult(text, string.Empty);
            }
            catch (Exception error) { owner?.QueueDictationResult(string.Empty, error.Message); }
        }
        public void onPartialResults(AndroidJavaObject results) { }
        public void onEvent(int eventType, AndroidJavaObject parameters) { }
    }

    /// <summary>Выполняет Android API, которым требуется основной поток Activity.</summary>
    private sealed class AndroidRunnable : AndroidJavaProxy
    {
        private readonly Action action;
        public AndroidRunnable(Action action) : base("java.lang.Runnable") => this.action = action;
        public void run() => action?.Invoke();
    }

    private static string DescribeSpeechError(int errorCode)
    {
        switch (errorCode)
        {
            case 1: return "Служба распознавания речи недоступна.";
            case 2: return "Нет подключения к службе распознавания. Проверьте сеть или офлайн-пакет языка.";
            case 3: return "Не удалось открыть микрофон.";
            case 6: return "Не удалось распознать фразу. Попробуйте ещё раз.";
            case 7: return "Речь не распознана. Повторите фразу.";
            case 9: return "Не выдано разрешение на использование микрофона.";
            default: return $"Распознавание завершилось с ошибкой {errorCode}. Попробуйте ещё раз.";
        }
    }

}
