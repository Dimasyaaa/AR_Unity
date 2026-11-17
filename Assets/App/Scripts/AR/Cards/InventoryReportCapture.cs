using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class InventoryReportCapture : MonoBehaviour
{
    private const int MaxReportPhotos = 5;
    private const int MaxReportPhotoBytes = 12 * 1024 * 1024;
    public static InventoryReportCapture Active { get; private set; }

    private readonly Color primary = new Color(.06f, .40f, .69f);
    private readonly Color secondary = new Color(.17f, .23f, .31f);
    private InventoryChecklistController checklist;
    private Canvas canvas;
    private RectTransform safeArea;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private RectTransform cameraControls;
    private RectTransform reportControls;
    private TextMeshProUGUI cameraStatus;
    private TextMeshProUGUI reportStatus;
    private TextMeshProUGUI recordLabel;
    private UnityEngine.UI.Button shutter;
    private UnityEngine.UI.Button closeReport;
    private UnityEngine.UI.Button retake;
    private UnityEngine.UI.Button record;
    private UnityEngine.UI.Button play;
    private UnityEngine.UI.Button send;
    private RawImage preview;
    private AspectRatioFitter previewAspect;
    private Texture2D image;
    private AudioSource player;
    private AudioClip recording;
    private AudioClip voicePreview;
    private string recordingDevice;
    private float recordingStarted;
    private byte[] photoJpeg;
    private readonly List<byte[]> reportPhotos = new List<byte[]>();
    private byte[] voiceWav;
    private bool takingPhoto;
    private bool startingRecording;
    private bool microphoneReserved;
    private bool playingVoice;
    private bool sending;
    private bool submitted;
    private InventoryReportCameraFallback cameraFallback;

    public static void Open(InventoryChecklistController owner = null)
    {
        if (Active != null) return;
        VoiceHelpController.CloseIfOpen();
        ARCardModeSelector.CloseDialog();
        ARCardSpawnUtility.DisablePlacement();
        var root = new GameObject("InventoryReportCamera", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(InventoryReportCapture));
        Active = root.GetComponent<InventoryReportCapture>();
        Active.checklist = owner;
        Active.Build();
    }

    private void Build()
    {
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 12000;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(720, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var tapShield = NewRect("TapShield", transform);
        Place(tapShield, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        tapShield.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.clear;
        safeArea = NewRect("SafeArea", transform);
        UpdateSafeArea();

        cameraControls = NewRect("CameraControls", safeArea);
        Place(cameraControls, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        LabelBar(cameraControls, "Камера", new Vector2(0, 1), new Vector2(12, -56), new Vector2(-12, -12));
        var footer = Box("CameraFooter", cameraControls, new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(12, 12), new Vector2(-12, 118));
        cameraStatus = Label("CameraStatus", footer,
            "В кадр попадут все видимые элементы сцены.", 22, new Vector2(0, .52f), Vector2.one);
        AddButton(footer, "Назад", secondary, .02f, .35f, Close);
        shutter = AddButton(footer, "Сделать фото", primary, .37f, .98f, TakePhoto);

        reportControls = Box("ReportControls", safeArea, Vector2.zero, Vector2.one,
            new Vector2(12, 12), new Vector2(-12, -12));
        LabelBar(reportControls, "Завершение инвентаризации", new Vector2(0, 1),
            new Vector2(10, -50), new Vector2(-10, -8));
        var previewArea = NewRect("PhotoPreview", reportControls);
        Place(previewArea, new Vector2(.03f, .38f), new Vector2(.97f, 1),
            Vector2.zero, new Vector2(0, -56));
        var photoRect = NewRect("ScenePhoto", previewArea);
        preview = photoRect.gameObject.AddComponent<RawImage>();
        preview.raycastTarget = false;
        previewAspect = photoRect.gameObject.AddComponent<AspectRatioFitter>();
        previewAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        var statusRect = NewRect("ReportMessage", reportControls);
        Place(statusRect, new Vector2(.03f, .27f), new Vector2(.97f, .38f), Vector2.zero, Vector2.zero);
        reportStatus = Label("Status", statusRect, "", 22, Vector2.zero, Vector2.one);

        var voiceRow = NewRect("VoiceActions", reportControls);
        Place(voiceRow, new Vector2(0, .14f), new Vector2(1, .27f), Vector2.zero, Vector2.zero);
        var addPhoto = AddButton(voiceRow, "Добавить фото", primary, .02f, .32f, ShowCamera, .08f, .92f);
        record = AddButton(voiceRow, "Записать голос", primary, .34f, .66f, RecordOrStop, .08f, .92f);
        recordLabel = record.GetComponentInChildren<TextMeshProUGUI>();
        play = AddButton(voiceRow, "Прослушать", secondary, .68f, .98f, PlayVoice, .08f, .92f);
        var actions = NewRect("ReportActions", reportControls);
        Place(actions, Vector2.zero, new Vector2(1, .14f), Vector2.zero, Vector2.zero);
        closeReport = AddButton(actions, "Назад", secondary, .03f, .28f, Close, .12f, .90f);
        retake = AddButton(actions, "Переснять", secondary, .30f, .59f, ShowCamera, .12f, .90f);
        send = AddButton(actions, "Отправить отчёт", primary, .61f, .97f, Send, .12f, .90f);
        player = gameObject.AddComponent<AudioSource>();
        player.playOnAwake = false;
        cameraFallback = gameObject.AddComponent<InventoryReportCameraFallback>();
        ShowCamera();
    }

    private void Update()
    {
        UpdateSafeArea();
        if (cameraControls != null && cameraControls.gameObject.activeSelf && !takingPhoto)
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
        StopVoicePlayback();
        reportControls.gameObject.SetActive(false);
        cameraControls.gameObject.SetActive(true);
        cameraStatus.text = checklist != null
            ? "Поместите чек-лист и нужные карточки в кадр."
            : "В кадр попадут все видимые элементы сцены.";
        cameraFallback.Show(cameraStatus);
    }

    public void TakePhoto()
    {
        if (takingPhoto || !cameraControls.gameObject.activeSelf || !cameraFallback.CanCapture) return;
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
            photoJpeg = jpeg;
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
            photoJpeg = null;
            yield break;
        }

        cameraFallback.Hide();
        preview.texture = image;
        previewAspect.aspectRatio = image.width / (float)image.height;
        cameraControls.gameObject.SetActive(false);
        reportControls.gameObject.SetActive(true);
        reportStatus.text = saved + $"\nФото в отчёте: {reportPhotos.Count}. Добавьте ещё снимки или запишите голос.";
        UpdateReportButtons();
    }

    public void RecordOrStop()
    {
        if (!reportControls.gameObject.activeSelf || sending || submitted || startingRecording) return;
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
        if (voicePreview == null || IsRecording || sending || !reportControls.gameObject.activeSelf) return;
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
        if (sending || takingPhoto) return;
        Destroy(gameObject);
    }

    private void UpdateReportButtons()
    {
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
        var size = new Vector2Int(Screen.width, Screen.height);
        var area = Screen.safeArea;
        if (size == lastScreenSize && area == lastSafeArea) return;
        if (size.x <= 0 || size.y <= 0) return;
        lastScreenSize = size;
        lastSafeArea = area;
        Place(safeArea, new Vector2(area.xMin / size.x, area.yMin / size.y),
            new Vector2(area.xMax / size.x, area.yMax / size.y), Vector2.zero, Vector2.zero);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var item = new GameObject(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        return (RectTransform)item.transform;
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static RectTransform Box(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rect = NewRect(name, parent);
        Place(rect, min, max, offsetMin, offsetMax);
        rect.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.06f, .09f, .14f, .94f);
        return rect;
    }

    private static void LabelBar(Transform parent, string title, Vector2 anchor, Vector2 min, Vector2 max)
    {
        var bar = Box("Header", parent, anchor, new Vector2(1, anchor.y), min, max);
        Label("Title", bar, title, 26, Vector2.zero, Vector2.one).fontStyle = FontStyles.Bold;
    }

    private static TextMeshProUGUI Label(string name, Transform parent, string value, float size, Vector2 min, Vector2 max)
    {
        var rect = NewRect(name, parent);
        Place(rect, min, max, new Vector2(6, 0), new Vector2(-6, 0));
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = value;
        label.fontSize = size;
        label.enableAutoSizing = true;
        label.fontSizeMin = 13;
        label.fontSizeMax = size;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private static UnityEngine.UI.Button AddButton(Transform parent, string title, Color color, float left, float right,
        UnityEngine.Events.UnityAction onClick, float bottom = .10f, float top = .52f)
    {
        var rect = NewRect(title, parent);
        Place(rect, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);
        var background = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = color;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);
        Label("Label", rect, title, 24, Vector2.zero, Vector2.one);
        return button;
    }
}
