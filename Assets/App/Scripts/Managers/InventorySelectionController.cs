using System;
using System.Collections.Generic;
using System.Text;
using ArInventory.Local;
using ArInventory.Network;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Показывает сотруднику назначение и историю перед началом сканирования коробок.</summary>
public sealed class InventorySelectionController : MonoBehaviour
{
    private const string StartAfterSelectionKey = "ArInventory.StartAfterSelection";
    private const int VisibleHistoryEntries = 5;

    [Header("Компоненты должны быть размещены в сцене Unity")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text assignmentText;
    [SerializeField] private TMP_Text historyText;
    [SerializeField] private Button startButton;
    [SerializeField] private Button backButton;

    private InventoryAssignmentResponse activeAssignment;
    private bool requestInProgress;
    private string latestStatus = string.Empty;

    /// <summary>Экран выбора, доступный голосовому маршрутизатору.</summary>
    public static InventorySelectionController Instance { get; private set; }

    /// <summary>Показывает, что в строке состояния сейчас выведена голосовая справка.</summary>
    public bool IsVoiceHelpVisible { get; private set; }

    private void Awake()
    {
        Instance = this;

        // Имена объектов — договорённость сцены, визуальные элементы создаются в редакторе Unity.
        statusText ??= FindComponent<TMP_Text>("StatusText");
        assignmentText ??= FindComponent<TMP_Text>("AssignmentText");
        historyText ??= FindComponent<TMP_Text>("HistoryText");
        startButton ??= FindComponent<Button>("StartInventoryButton");
        backButton ??= FindComponent<Button>("BackToMenuButton");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        LocalClient.GetOrCreate();
        if (startButton != null) startButton.onClick.AddListener(StartAssignedInventory);
        if (backButton != null) backButton.onClick.AddListener(ReturnToMenu);

        if (!SessionManager.IsLoggedIn)
        {
            SetStatus("Сначала войдите в систему.");
            SetStartAvailable(false);
            return;
        }

        SetStatus("Загружаем назначение и историю…");
        requestInProgress = true;
        InventoryApiClient.GetAssignmentOverview(this, SessionManager.UserId, ShowOverview, ShowOfflineData);
    }

    private void ShowOverview(InventoryAssignmentOverviewResponse overview)
    {
        requestInProgress = false;
        activeAssignment = overview?.activeAssignment;

        LocalDatabase database = LocalDatabase.Instance;
        if (database != null && database.IsReady && activeAssignment != null)
        {
            var localBatch = database.SaveAssignedInventoryBatch(
                activeAssignment.clientBatchId,
                SessionManager.UserId,
                Array.ConvertAll(activeAssignment.boxes, box => box.qrCode));
            if (localBatch == null)
                SetStatus("Назначение загружено, но не удалось сохранить его для работы без сети.");
            else
                SetStatus("Данные синхронизированы с сервером.");
        }
        else if (activeAssignment == null)
            SetStatus("Новых назначений пока нет.");

        assignmentText.text = activeAssignment == null
            ? "<b>Активное назначение</b>\n\nАдминистратор пока не назначил вам инвентаризацию коробок."
            : FormatAssignment(activeAssignment);
        historyText.text = FormatHistory(overview?.history);
        SetStartAvailable(activeAssignment != null);
    }

    private void ShowOfflineData(string error)
    {
        requestInProgress = false;
        LocalDatabase database = LocalDatabase.Instance;
        List<LocalInventoryBatch> completedBatches = database != null && database.IsReady
            ? database.GetCompletedInventoryBatches(SessionManager.UserId, VisibleHistoryEntries)
            : new List<LocalInventoryBatch>();
        if (historyText != null)
            historyText.text = FormatLocalHistory(completedBatches, database);

        LocalInventoryBatch cached = database != null && database.IsReady
            ? database.GetActiveInventoryBatch(SessionManager.UserId)
            : null;

        if (cached != null && !string.IsNullOrWhiteSpace(cached.assigned_qr_codes))
        {
            string[] qrCodes = cached.assigned_qr_codes.Split(new[] { '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            activeAssignment = new InventoryAssignmentResponse
            {
                success = true,
                clientBatchId = cached.client_batch_id,
                expectedBoxCount = cached.expected_box_count,
                status = cached.status,
                boxes = Array.ConvertAll(qrCodes, code => new InventoryAssignedBox { qrCode = code.Trim(), title = code.Trim() })
            };
            assignmentText.text = FormatAssignment(activeAssignment);
            SetStatus("Сервер недоступен. Используется сохранённое назначение.");
            SetStartAvailable(true);
            return;
        }

        LocalInventoryBatch latest = database != null && database.IsReady
            ? database.GetLatestInventoryBatch(SessionManager.UserId)
            : null;
        assignmentText.text = "<b>Активное назначение</b>\n\nАдминистратор пока не назначил вам инвентаризацию коробок.";
        if (historyText != null && completedBatches.Count == 0 && latest != null && latest.status == "completed")
            historyText.text = FormatLocalHistory(new List<LocalInventoryBatch> { latest }, database);
        SetStatus("Нет подключения к серверу и сохранённого активного назначения.");
        SetStartAvailable(false);
        Debug.LogWarning("[Выбор инвентаризации] Не удалось загрузить данные: " + error);
    }

    /// <summary>Показывает локальную историю, не выдавая её за полную серверную историю.</summary>
    private static string FormatLocalHistory(List<LocalInventoryBatch> batches, LocalDatabase database)
    {
        if (batches == null || batches.Count == 0)
            return "<b>Завершённые инвентаризации</b>\n\nСервер недоступен. На этом устройстве нет сохранённых завершённых проверок.";

        var text = new StringBuilder("<b>Завершённые проверки на этом устройстве</b>\n\n");
        for (int index = 0; index < batches.Count; index++)
        {
            LocalInventoryBatch batch = batches[index];
            string timestamp = string.IsNullOrWhiteSpace(batch.completed_at)
                ? batch.started_at
                : batch.completed_at;
            DateTime.TryParse(timestamp, out DateTime completed);
            string date = completed == default
                ? "Дата не указана"
                : completed.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            int completedBoxes = database != null ? database.GetCompletedBoxCount(batch.client_batch_id) : 0;
            text.AppendLine($"<color=#1676B8><b>{index + 1:00}</b></color>  {date}");
            text.AppendLine($"     Коробки: {completedBoxes} из {batch.expected_box_count}\n");
        }

        return text.ToString();
    }

    private void StartAssignedInventory()
    {
        if (requestInProgress || activeAssignment == null) return;
        PlayerPrefs.SetInt(StartAfterSelectionKey, 1);
        SceneManager.LoadScene("StartScene");
    }

    private void ReturnToMenu()
    {
        SceneManager.LoadScene("StartScene");
    }

    private void SetStartAvailable(bool available)
    {
        if (startButton != null) startButton.interactable = available && !requestInProgress;
    }

    private void SetStatus(string message)
    {
        latestStatus = message ?? string.Empty;
        IsVoiceHelpVisible = false;
        if (statusText != null) statusText.text = message;
    }

    /// <summary>Показывает команды для этого экрана, если в сцене нет отдельного окна помощи.</summary>
    public void ShowVoiceHelp()
    {
        if (statusText == null)
            return;

        IsVoiceHelpVisible = true;
        statusText.text = "Голосом: «начать инвентаризацию», «вернуться в меню», " +
                          "«прокрути список вверх/вниз». Скажите «закрой подсказки», чтобы скрыть этот текст.";
    }

    /// <summary>Возвращает строку состояния после закрытия голосовой справки.</summary>
    public void HideVoiceHelp()
    {
        if (!IsVoiceHelpVisible)
            return;

        IsVoiceHelpVisible = false;
        if (statusText != null)
            statusText.text = latestStatus;
    }

    private static string FormatAssignment(InventoryAssignmentResponse assignment)
    {
        var text = new StringBuilder();
        text.AppendLine("<b>Активное назначение</b>");
        text.AppendLine($"Проверить коробок: {assignment.expectedBoxCount}");
        text.AppendLine();
        for (int index = 0; index < assignment.boxes.Length; index++)
        {
            InventoryAssignedBox box = assignment.boxes[index];
            string title = string.IsNullOrWhiteSpace(box.title) ? box.qrCode : box.title;
            text.AppendLine($"<color=#1676B8><b>{index + 1}.</b></color> {Escape(title)}  <color=#7B8CA3>QR: {Escape(box.qrCode)}</color>");
        }
        return text.ToString();
    }

    private static string FormatHistory(InventoryAssignmentHistory[] history)
    {
        if (history == null || history.Length == 0)
            return "<b>Завершённые инвентаризации</b>\n\nПока нет завершённых запусков.";

        var text = new StringBuilder("<b>Завершённые инвентаризации</b>\n\n");
        // Сервер отдаёт последние записи первыми; показываем пять, чтобы история оставалась читаемой.
        int visibleCount = Mathf.Min(history.Length, VisibleHistoryEntries);
        for (int index = 0; index < visibleCount; index++)
        {
            InventoryAssignmentHistory entry = history[index];
            DateTime.TryParse(entry.completedAt, out DateTime completed);
            string date = completed == default ? "Дата не указана" : completed.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            text.AppendLine($"<color=#1676B8><b>{index + 1:00}</b></color>  {date}");
            text.AppendLine($"     Коробки: {entry.completedBoxCount} из {entry.expectedBoxCount}\n");
        }

        if (history.Length > visibleCount)
            text.AppendLine($"Показаны последние {visibleCount} из {history.Length} записей.");

        return text.ToString();
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private T FindComponent<T>(string objectName) where T : Component
    {
        foreach (T component in GetComponentsInChildren<T>(true))
            if (component.gameObject.name == objectName)
                return component;
        return null;
    }
}
