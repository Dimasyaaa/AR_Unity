using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Управляет фотографиями, голосовым комментарием и отправкой итогового отчёта инвентаризации.
/// </summary>
public sealed class InventoryReportCapture : MonoBehaviour
{
    private const int MaxReportPhotos = 5;
    private const int MaxReportPhotoBytes = 12 * 1024 * 1024;
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
    private AudioSource player;
    private AudioClip recording;
    private AudioClip voicePreview;
    private string recordingDevice;
    private float recordingStarted;
    private readonly List<byte[]> reportPhotos = new List<byte[]>();
    private byte[] voiceWav;
    private bool takingPhoto;
    private bool startingRecording;
    private bool microphoneReserved;
    private bool playingVoice;
    private bool sending;
    private bool submitted;
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
        addPhoto = voiceActions != null ? voiceActions.Find("AddPhotoButton")?.GetComponent<Button>() : null;
        record = voiceActions != null ? voiceActions.Find("RecordButton")?.GetComponent<Button>() : null;
        play = voiceActions != null ? voiceActions.Find("PlayButton")?.GetComponent<Button>() : null;
        closeReport = reportActions != null ? reportActions.Find("CloseReportButton")?.GetComponent<Button>() : null;
        retake = reportActions != null ? reportActions.Find("RetakeButton")?.GetComponent<Button>() : null;
        send = reportActions != null ? reportActions.Find("SendButton")?.GetComponent<Button>() : null;
        preview = photoPreview != null ? photoPreview.GetComponent<RawImage>() : null;
        previewAspect = photoPreview != null ? photoPreview.GetComponent<AspectRatioFitter>() : null;
        recordLabel = record != null ? record.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        player = GetComponent<AudioSource>();
        cameraFallback = GetComponent<InventoryReportCameraFallback>();

        bool ready = canvas != null && cameraControls != null && reportControls != null &&
            cameraStatus != null && cameraBack != null && shutter != null && reportStatus != null &&
            addPhoto != null && record != null && play != null && recordLabel != null &&
            closeReport != null && retake != null && send != null && preview != null &&
            previewAspect != null && player != null && cameraFallback != null;
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
        record.onClick.AddListener(RecordOrStop);
        play.onClick.AddListener(PlayVoice);
        closeReport.onClick.AddListener(Close);
        retake.onClick.AddListener(ShowCamera);
        send.onClick.AddListener(Send);
    }

    private void Update()
    {
        UpdateSafeArea();
        if (cameraControls != null && cameraControls.activeSelf && !takingPhoto)
            shutter.interactable = cameraFallback.CanCapture;
        if (playingVoice && !player.isPlaying) StopVoicePlayback();
        if (recording == null) return;

        int frames = Microphone.GetPosition(recordingDevice);
        float elapsed = Time.realtimeSinceStartup - recordingStarted;
        if (frames >= recording.frequency * InventoryReportVoice.MaxSeconds)
        {
            StopRecording(Mathf.Min(frames, recording.frequency * InventoryReportVoice.MaxSeconds));
        }
        else if (elapsed >= 4 && frames <= 0)
        {
            AbortRecording();
            reportStatus.text = "Микрофон не начал запись. Попробуйте ещё раз.";
            UpdateReportButtons();
        }
        else
            reportStatus.text = $"Идёт запись · {Mathf.FloorToInt(frames / (float)recording.frequency)} / 60 сек.";
    }

    private void ShowCamera()
    {
        // В кадр попадёт весь экран приложения: чек-лист и размещённые AR-карточки.
        StopVoicePlayback();
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
        if (checklist != null && reportPhotos.Count >= MaxReportPhotos)
        {
            cameraStatus.text = $"В отчёт можно добавить не более {MaxReportPhotos} фотографий.";
            return;
        }
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
        reportStatus.text = saved + $"\nФото в отчёте: {reportPhotos.Count}. Добавьте ещё снимки или запишите голос.";
        UpdateReportButtons();
    }

    public void RecordOrStop()
    {
        // Повторное нажатие на кнопку завершает текущую запись.
        if (!reportControls.activeSelf || sending || submitted || startingRecording) return;
        if (recording != null)
        {
            StopRecording(Microphone.GetPosition(recordingDevice));
            return;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => { if (this != null) RecordOrStop(); };
            callbacks.PermissionDenied += _ => { if (this != null) reportStatus.text = "Разрешите доступ к микрофону в настройках Android."; };
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            return;
        }
#endif
        startingRecording = true;
        StopVoicePlayback();
        SetMicrophoneReserved(true);
        UpdateReportButtons();
        StartCoroutine(StartRecording());
    }

    private IEnumerator StartRecording()
    {
        // Даём Android время выдать микрофон после запроса разрешения.
        reportStatus.text = "Подключаем микрофон…";
        yield return new WaitForSecondsRealtime(.35f);
        if (!startingRecording) yield break;
        startingRecording = false;
        try
        {
            var devices = Microphone.devices;
            if (devices.Length == 0) throw new InvalidOperationException("Микрофон не найден.");
            recordingDevice = devices[0];
            Microphone.GetDeviceCaps(recordingDevice, out int minRate, out int maxRate);
            int rate = maxRate > 0 ? Mathf.Clamp(InventoryReportVoice.Rate, minRate, maxRate) : InventoryReportVoice.Rate;
            recording = Microphone.Start(recordingDevice, false, InventoryReportVoice.MaxSeconds + 1, rate);
            if (recording == null) throw new InvalidOperationException("Микрофон не начал запись.");
            recordingStarted = Time.realtimeSinceStartup;
            reportStatus.text = "Идёт запись…";
        }
        catch (Exception error)
        {
            AbortRecording();
            reportStatus.text = error.Message;
        }
        UpdateReportButtons();
    }

    private void StopRecording(int frames)
    {
        // Завершаем запись, проверяем её длительность и кодируем WAV для API.
        try
        {
            Microphone.End(recordingDevice);
            if (frames < recording.frequency / 4)
                throw new InvalidOperationException("Запись слишком короткая. Попробуйте ещё раз.");
            var encoded = InventoryReportVoice.Encode(recording, Mathf.Min(frames, recording.samples), out var clip);
            if (voicePreview != null) Destroy(voicePreview);
            voicePreview = clip;
            voiceWav = encoded;
            reportStatus.text = $"Голосовое сообщение готово · {clip.length:0.#} сек.";
        }
        catch (Exception error) { reportStatus.text = error.Message; }
        finally
        {
            Destroy(recording);
            recording = null;
            SetMicrophoneReserved(false);
            UpdateReportButtons();
        }
    }

    private void AbortRecording()
    {
        startingRecording = false;
        if (recording != null)
        {
            Microphone.End(recordingDevice);
            Destroy(recording);
            recording = null;
        }
        SetMicrophoneReserved(false);
    }

    private void SetMicrophoneReserved(bool reserved)
    {
        if (microphoneReserved == reserved) return;
        microphoneReserved = reserved;
        VoiceController.Instance?.ReserveMicrophone(reserved);
    }

    public void PlayVoice()
    {
        // Проигрывание использует готовую копию записи и временно резервирует микрофон.
        if (voicePreview == null || IsRecording || sending || !reportControls.activeSelf) return;
        if (playingVoice) { StopVoicePlayback(); return; }
        SetMicrophoneReserved(true);
        playingVoice = true;
        player.clip = voicePreview;
        player.Play();
    }

    private void StopVoicePlayback()
    {
        if (!playingVoice) return;
        playingVoice = false;
        player.Stop();
        SetMicrophoneReserved(false);
    }

    public void Send()
    {
        // Отправка требует хотя бы одного фото, голосового комментария и активного чек-листа.
        if (sending || submitted || IsRecording || reportPhotos.Count == 0 || voiceWav == null || checklist == null)
            return;
        StopVoicePlayback();
        sending = true;
        UpdateReportButtons();
        checklist.SubmitReport(reportPhotos, voiceWav, (savedLocally, message) =>
        {
            if (this == null) return;
            sending = false;
            submitted = savedLocally;
            reportStatus.text = message;
            UpdateReportButtons();
        }, message => reportStatus.text = message);
    }

    private bool IsRecording => startingRecording || recording != null;

    public void Close()
    {
        // Не закрываем экран, пока снимок или отправка ещё выполняются.
        if (sending || takingPhoto) return;
        Destroy(gameObject);
    }

    private void UpdateReportButtons()
    {
        // Доступность кнопок зависит от текущего этапа заполнения отчёта.
        record.interactable = !startingRecording && !sending && !submitted;
        recordLabel.text = recording != null ? "Стоп" : voiceWav != null ? "Перезаписать" : "Записать голос";
        closeReport.interactable = !sending;
        retake.interactable = !IsRecording && !sending && !submitted;
        play.interactable = voiceWav != null && !IsRecording && !sending;
        send.interactable = !IsRecording && !sending && !submitted && reportPhotos.Count > 0 && voiceWav != null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused) return;
        StopVoicePlayback();
        if (!IsRecording) return;
        AbortRecording();
        reportStatus.text = "Запись остановлена. Можно записать сообщение заново.";
        UpdateReportButtons();
    }

    private void OnDestroy()
    {
        AbortRecording();
        if (image != null) Destroy(image);
        if (player != null) player.Stop();
        if (voicePreview != null) Destroy(voicePreview);
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

}
