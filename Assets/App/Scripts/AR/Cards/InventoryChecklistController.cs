using System;
using System.Collections.Generic;
using System.Linq;
using ArInventory.Local;
using ArInventory.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Управляет проверкой содержимого выбранного шкафа и отправкой её результата.
/// Постоянная разметка настраивается в префабе; строки списка создаются по данным шкафа.
/// </summary>
public sealed class InventoryChecklistController : MonoBehaviour
{
    public static InventoryChecklistController Active { get; private set; }
    public static bool Exists => Active != null;

    public IReadOnlyList<string> VoiceItemNames => itemNames.Values.ToList();
    public bool CanFinalize => !saving && !completed && statuses.Count > 0 &&
        statuses.Values.All(value => value == "present" || value == "missing");

    [Header("Интерфейс карточки")]
    [SerializeField] private TextMeshProUGUI message;
    [SerializeField] private RectTransform content;
    [SerializeField] private InventoryChecklistItemRow itemRowTemplate;
    [SerializeField] private Button finishButton;
    [SerializeField] private ScrollRect checklistScroll;

    private LocalCabinetProfile localCabinet;
    private InventoryCabinetResponse serverCabinet;
    private string qrCode;
    private DateTime startedAtUtc;
    private readonly Dictionary<long, string> statuses = new();
    private readonly Dictionary<long, string> itemNames = new();
    private readonly Dictionary<long, InventoryChecklistItemRow> itemRows = new();
    private bool saving;
    private bool completed;

    // При появлении карточки берём последний отсканированный QR и загружаем состав шкафа.
    private void Start()
    {
        Active = this;
        qrCode = GameDataManager.LastScannedQR;
        startedAtUtc = DateTime.UtcNow;
        localCabinet = LocalDatabase.Instance != null
            ? LocalDatabase.Instance.FindCabinetByQr(qrCode)
            : null;

        if (!HasRequiredUi())
            return;

        // Строка-шаблон хранится в префабе, но показывается только после копирования.
        itemRowTemplate.gameObject.SetActive(false);

        if (!string.IsNullOrWhiteSpace(qrCode))
        {
            SetMessage("Загружаем список предметов шкафа…");
            InventoryApiClient.GetCabinet(this, qrCode, OnCabinetReceived, OnApiLoadFailed);
        }
        else
        {
            OnApiLoadFailed("Сначала отсканируйте QR-код шкафа.");
        }
    }

    // Проверяем связи из Inspector до обращения к разметке, чтобы ошибка была понятной.
    private bool HasRequiredUi()
    {
        if (message != null && content != null && itemRowTemplate != null &&
            finishButton != null && checklistScroll != null)
            return true;

        Debug.LogError("[Инвентаризация] В InventoryChecklistCard не назначены поля интерфейса. Проверьте префаб карточки.");
        return false;
    }

    public void OnScrollUpPressed() => ScrollChecklist(.24f);
    public void OnScrollDownPressed() => ScrollChecklist(-.24f);
    public void OnClosePressed() => ARCardModeSelector.CloseCard(gameObject);
    public void OnFinishPressed() => OpenReport();
    public void ScrollChecklistByVoice(bool up) => ScrollChecklist(up ? .24f : -.24f);

    private void ScrollChecklist(float amount)
    {
        if (checklistScroll == null || checklistScroll.content == null || checklistScroll.viewport == null)
            return;

        Canvas.ForceUpdateCanvases();
        checklistScroll.verticalNormalizedPosition =
            Mathf.Clamp01(checklistScroll.verticalNormalizedPosition + amount);
    }

    // Сервер присылает состав шкафа; на его основе создаём строки из шаблона префаба.
    private void OnCabinetReceived(InventoryCabinetResponse response)
    {
        serverCabinet = response;
        var items = response.items ?? Array.Empty<InventoryCabinetItem>();
        ClearRows();

        foreach (var item in items)
        {
            statuses[item.id] = string.Empty;
            itemNames[item.id] = item.name;
            AddItem(item.id, item.name);
        }

        if (items.Length == 0)
            SetMessage("В админ-панели добавьте предметы в чек-лист этого шкафа.");
        else
            SetMessage($"Шкаф: {response.title} · предметов: {items.Length}");
    }

    // Если сеть недоступна, пытаемся продолжить с локальной копией чек-листа.
    private void OnApiLoadFailed(string error)
    {
        var fallbackItems = localCabinet != null && LocalDatabase.Instance != null
            ? LocalDatabase.Instance.GetCabinetChecklist(localCabinet.id)
            : new List<LocalCabinetChecklistItem>();

        if (fallbackItems.Count == 0)
        {
            SetMessage("Не удалось загрузить чек-лист. Проверьте сеть и адрес API.");
            Debug.LogWarning("[Инвентаризация] Не удалось получить список предметов: " + error);
            return;
        }

        ClearRows();
        foreach (var item in fallbackItems)
        {
            statuses[item.id] = string.Empty;
            itemNames[item.id] = item.item_name;
            AddItem(item.id, item.item_name);
        }

        SetMessage("Офлайн-режим · данные из локальной базы");
    }

    // Удаляем только созданные строки, оставляя шаблон и ScrollRect в префабе.
    private void ClearRows()
    {
        statuses.Clear();
        itemNames.Clear();
        itemRows.Clear();

        if (content == null || itemRowTemplate == null)
            return;

        for (int index = content.childCount - 1; index >= 0; index--)
        {
            Transform child = content.GetChild(index);
            if (child == itemRowTemplate.transform)
                continue;

            Destroy(child.gameObject);
        }
    }

    // Каждая строка получает собственный ID и возвращает выбранный ответ контроллеру.
    private void AddItem(long id, string name)
    {
        InventoryChecklistItemRow row = Instantiate(itemRowTemplate, content, false);
        row.name = "Item_" + id;
        row.gameObject.SetActive(true);
        row.Configure(id, name, SetStatus);
        itemRows[id] = row;
    }

    // Голосовая команда находит упомянутый предмет и меняет его ответ так же, как кнопка.
    public bool SetStatusByVoice(string phrase)
    {
        if (completed || saving || statuses.Count == 0 || string.IsNullOrWhiteSpace(phrase))
            return false;

        string command = phrase.ToLowerInvariant();
        bool present = command.Contains("есть") || command.Contains("имеется");
        bool missing = command.Contains("нет") || command.Contains("отсутствует");
        if (!present && !missing)
            return false;

        string name = command
            .Replace("есть", "")
            .Replace("имеется", "")
            .Replace("нет", "")
            .Replace("отсутствует", "")
            .Trim(' ', '.', ',', '!', '?');

        var match = itemNames
            .Select(pair => new
            {
                pair.Key,
                pair.Value,
                Score = TokenOverlap(pair.Value, name)
            })
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();

        if (match == null || match.Score == 0)
            return false;

        SetStatus(match.Key, present ? "present" : "missing");
        SetMessage($"{match.Value}: {(present ? "есть" : "нет")}");
        return true;
    }

    public void SaveByVoice() => OpenReport();

    private static int TokenOverlap(string item, string spoken)
    {
        return spoken
            .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Length > 1 &&
                item.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void SetStatus(long id, string value)
    {
        if (completed || saving || !statuses.ContainsKey(id))
            return;

        statuses[id] = value;
        if (itemRows.TryGetValue(id, out InventoryChecklistItemRow row))
            row.ShowStatus(value);
    }

    private void OpenReport()
    {
        if (saving || completed)
            return;

        if (!CanFinalize)
        {
            SetMessage("Отметьте каждый предмет: «Есть» или «Нет».");
            return;
        }

        InventoryReportCapture.Open(this);
    }

    // Сначала сохраняем результат локально, затем отправляем его; при ошибке API
    // отчёт остаётся в очереди и может уйти при следующей синхронизации.
    public void SubmitReport(List<byte[]> photos, byte[] voice, Action<bool, string> onDone,
        Action<string> onProgress)
    {
        if (!CanFinalize || photos == null || photos.Count == 0 || voice == null)
        {
            onDone(false, "Отметьте каждый предмет и добавьте фото и голосовое сообщение.");
            return;
        }

        if (!SessionManager.IsLoggedIn)
        {
            onDone(false, "Для отправки отчёта войдите в приложение.");
            return;
        }

        if (serverCabinet == null && localCabinet == null)
        {
            onDone(false, "Не удалось определить шкаф для отчёта. Загрузите его данные и повторите.");
            return;
        }

        string runId = Guid.NewGuid().ToString("D");
        var payload = new InventoryRunSyncDto
        {
            clientRunId = runId,
            userId = SessionManager.UserId,
            qrCode = serverCabinet?.qrCode ?? qrCode,
            startedAt = startedAtUtc.ToUniversalTime().ToString("o"),
            completedAt = DateTime.UtcNow.ToString("o"),
            items = statuses.Select(pair => new InventoryRunItemSyncDto
            {
                itemId = pair.Key,
                status = pair.Value,
                comment = ""
            }).ToList(),
            photoBase64 = Convert.ToBase64String(photos[0]),
            photosBase64 = photos.ConvertAll(Convert.ToBase64String).ToArray(),
            voiceBase64 = Convert.ToBase64String(voice)
        };

        try
        {
            LocalDatabase database = LocalDatabase.Instance;
            if (database == null || !database.IsReady)
                throw new InvalidOperationException("Локальная база ещё не готова.");

            long cabinetId = serverCabinet?.cabinetId ?? localCabinet.id;
            long qrCodeId = serverCabinet?.qrCodeId ?? localCabinet.qr_code_id;
            string cabinetTitle = serverCabinet?.title ?? localCabinet.title ?? qrCode;
            database.SaveInventoryRun(payload, cabinetId, qrCodeId, cabinetTitle, itemNames);
        }
        catch (Exception error)
        {
            Debug.LogWarning("[Отчёт инвентаризации] Не удалось сохранить: " + error.Message);
            onDone(false, "Не удалось сохранить отчёт на устройстве. Повторите отправку.");
            return;
        }

        completed = true;
        saving = true;
        finishButton.interactable = false;
        SetMessage("Отправляем отчёт на сервер…");
        onProgress("Отправляем отчёт с фото и голосовым сообщением…");

        InventoryApiClient.SubmitInventory(this, payload,
            result =>
            {
                saving = false;
                try
                {
                    LocalDatabase.Instance?.MarkInventoryRunSynced(runId);
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[Отчёт инвентаризации] " + error.Message);
                }

                const string messageText = "Отчёт с фото и голосовым сообщением доступен администратору.";
                SetMessage(messageText);
                onDone(true, messageText);
            },
            error =>
            {
                saving = false;
                const string messageText = "Отчёт и вложения сохранены на устройстве. Отправьте их через «Синхронизировать» в главном меню.";
                SetMessage(messageText);
                onDone(true, messageText);
                Debug.LogWarning("[Инвентаризация] Не удалось отправить проверку: " + error);
            });
    }

    private void SetMessage(string text)
    {
        if (message != null)
            message.text = text;
    }

    private void OnDestroy()
    {
        if (Active != this)
            return;

        Active = FindObjectsByType<InventoryChecklistController>(FindObjectsInactive.Include)
            .FirstOrDefault(controller => controller != null && controller != this);
    }
}
