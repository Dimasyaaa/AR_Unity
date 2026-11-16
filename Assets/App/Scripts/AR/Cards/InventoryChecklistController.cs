using System;
using System.Collections.Generic;
using System.Linq;
using ArInventory.Local;
using ArInventory.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Checklist AR card: loads the cabinet list from the API and records the result.</summary>
public sealed class InventoryChecklistController : MonoBehaviour
{
    public static InventoryChecklistController Active { get; private set; }
    public static bool Exists => Active != null;
    public IReadOnlyList<string> VoiceItemNames => itemNames.Values.ToList();

    private LocalCabinetProfile localCabinet;
    private InventoryCabinetResponse serverCabinet;
    private string qrCode;
    private DateTime startedAtUtc;
    private readonly Dictionary<long, string> statuses = new();
    private readonly Dictionary<long, string> itemNames = new();
    private TextMeshProUGUI message;
    private Transform content;
    private bool building;
    private bool saving;

    private void Start()
    {
        Active = this;
        qrCode = GameDataManager.LastScannedQR;
        startedAtUtc = DateTime.UtcNow;
        localCabinet = LocalDatabase.Instance != null ? LocalDatabase.Instance.FindCabinetByQr(qrCode) : null;

        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { Debug.LogError("[Inventory] В карточке нет Canvas."); return; }
        var oldPanel = canvas.transform.Find("Panel");
        if (oldPanel != null) Destroy(oldPanel.gameObject);
        BuildShell(canvas.transform);

        if (!string.IsNullOrWhiteSpace(qrCode))
        {
            SetMessage("Загружаем список предметов шкафа…");
            InventoryApiClient.GetCabinet(this, qrCode, OnCabinetReceived, OnApiLoadFailed);
        }
        else OnApiLoadFailed("Сначала отсканируйте QR-код шкафа.");
    }

    private void BuildShell(Transform canvas)
    {
        var panel = New("Panel", canvas);
        var background = panel.AddComponent<Image>();
        background.color = new Color(.09f, .12f, .18f, .97f);
        ARCardSpawnUtility.AddPanelDepth(background);
        Stretch(panel.GetComponent<RectTransform>());
        AddText(panel.transform, "Title", "Инвентаризация", 34, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -12), new Vector2(0, 104), TextAlignmentOptions.Center);
        message = AddText(panel.transform, "Message", "Подключение к серверу…", 22, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -112), new Vector2(0, 48), TextAlignmentOptions.Center);

        var scrollObject = New("ChecklistScroll", panel.transform);
        // New() already creates a RectTransform. AddComponent would return null
        // because every GameObject can only have one Transform component.
        var scrollRect = scrollObject.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(.04f, .17f); scrollRect.anchorMax = new Vector2(.96f, .80f);
        scrollRect.offsetMin = Vector2.zero; scrollRect.offsetMax = Vector2.zero;
        scrollObject.AddComponent<Image>().color = new Color(.14f, .17f, .23f, 1);
        var scroll = scrollObject.AddComponent<ScrollRect>(); scroll.horizontal = false;

        var viewport = New("Viewport", scrollObject.transform);
        var viewportRect = viewport.GetComponent<RectTransform>(); Stretch(viewportRect);
        viewport.AddComponent<Image>().color = Color.white;
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = viewportRect;

        var contentObject = New("Content", viewport.transform);
        content = contentObject.transform;
        var contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(.5f, 1); contentRect.anchoredPosition = Vector2.zero; contentRect.sizeDelta = Vector2.zero;
        var layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12); layout.spacing = 8;
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        var fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;

        var close = AddButton(panel.transform, "Закрыть", new Color(.30f, .34f, .41f), () => ARCardModeSelector.CloseCard(gameObject));
        var closeRect = close.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(.05f, .025f); closeRect.anchorMax = new Vector2(.31f, .145f);
        closeRect.offsetMin = Vector2.zero; closeRect.offsetMax = Vector2.zero;

        var save = AddButton(panel.transform, "Отправить проверку", new Color(.05f, .62f, .28f), Save);
        var saveRect = save.GetComponent<RectTransform>();
        saveRect.anchorMin = new Vector2(.35f, .025f); saveRect.anchorMax = new Vector2(.95f, .145f);
        saveRect.offsetMin = Vector2.zero; saveRect.offsetMax = Vector2.zero;
    }

    private void OnCabinetReceived(InventoryCabinetResponse response)
    {
        serverCabinet = response;
        var items = response.items ?? Array.Empty<InventoryCabinetItem>();
        if (content == null) return;
        foreach (Transform child in content) Destroy(child.gameObject);
        statuses.Clear(); itemNames.Clear();
        foreach (var item in items)
        {
            statuses[item.id] = string.Empty;
            itemNames[item.id] = item.name;
            AddItem(item.id, item.name);
        }
        if (items.Length == 0) SetMessage("В админ-панели добавьте предметы в чек-лист этого шкафа.");
        else SetMessage($"Шкаф: {response.title} · предметов: {items.Length}");
    }

    private void OnApiLoadFailed(string error)
    {
        var fallbackItems = localCabinet != null && LocalDatabase.Instance != null
            ? LocalDatabase.Instance.GetCabinetChecklist(localCabinet.id)
            : new List<LocalCabinetChecklistItem>();
        if (fallbackItems.Count == 0)
        {
            SetMessage("Не удалось загрузить чек-лист. Проверьте сеть и адрес API.");
            Debug.LogWarning("[Inventory] API checklist unavailable: " + error);
            return;
        }

        foreach (Transform child in content) Destroy(child.gameObject);
        statuses.Clear(); itemNames.Clear();
        foreach (var item in fallbackItems)
        {
            statuses[item.id] = string.Empty;
            itemNames[item.id] = item.item_name;
            AddItem(item.id, item.item_name);
        }
        SetMessage("Офлайн-режим · данные из локальной базы");
    }

    private void AddItem(long id, string name)
    {
        var row = New("Item_" + id, content.transform);
        row.AddComponent<LayoutElement>().preferredHeight = 94;
        row.AddComponent<Image>().color = new Color(.20f, .24f, .32f, 1);
        var label = AddText(row.transform, "Name", name, 22, new Vector2(0, 0), new Vector2(.55f, 1), new Vector2(12, 0), new Vector2(-8, 0), TextAlignmentOptions.Left);
        label.enableAutoSizing = true; label.fontSizeMin = 15; label.fontSizeMax = 22;
        AddButton(row.transform, "Есть", new Color(.05f, .52f, .28f), () => SetStatus(id, "present"));
        SetButtonRect(row.transform.Find("Есть") as RectTransform, .57f, .77f);
        AddButton(row.transform, "Нет", new Color(.65f, .18f, .18f), () => SetStatus(id, "missing"));
        SetButtonRect(row.transform.Find("Нет") as RectTransform, .79f, .99f);
    }

    private static void SetButtonRect(RectTransform rect, float left, float right)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(left, .17f); rect.anchorMax = new Vector2(right, .83f);
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
    }

    public bool SetStatusByVoice(string phrase)
    {
        if (statuses.Count == 0 || string.IsNullOrWhiteSpace(phrase)) return false;
        string command = phrase.ToLowerInvariant();
        bool present = command.Contains("есть") || command.Contains("имеется");
        bool missing = command.Contains("нет") || command.Contains("отсутствует");
        if (!present && !missing) return false;
        string name = command.Replace("есть", "").Replace("имеется", "").Replace("нет", "").Replace("отсутствует", "").Trim(' ', '.', ',', '!', '?');
        var match = itemNames.Select(pair => new { pair.Key, pair.Value, Score = TokenOverlap(pair.Value, name) }).OrderByDescending(item => item.Score).FirstOrDefault();
        if (match == null || match.Score == 0) return false;
        SetStatus(match.Key, present ? "present" : "missing");
        SetMessage($"{match.Value}: {(present ? "есть" : "нет")}");
        return true;
    }

    public void SaveByVoice() => Save();

    private static int TokenOverlap(string item, string spoken) => spoken.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Count(token => token.Length > 1 && item.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

    private void SetStatus(long id, string value)
    {
        if (!statuses.ContainsKey(id)) return;
        statuses[id] = value;
        var row = content != null ? content.Find("Item_" + id) : null;
        if (row == null) return;
        var yes = row.Find("Есть")?.GetComponent<Image>();
        var no = row.Find("Нет")?.GetComponent<Image>();
        if (yes != null) yes.color = value == "present" ? new Color(.12f, .85f, .32f) : new Color(.05f, .52f, .28f);
        if (no != null) no.color = value == "missing" ? new Color(.95f, .22f, .20f) : new Color(.65f, .18f, .18f);
    }

    private void Save()
    {
        if (saving) return;
        if (statuses.Count == 0 || statuses.Values.Any(value => value != "present" && value != "missing"))
        { SetMessage("Отметьте каждый предмет: «Есть» или «Нет»."); return; }

        string runId = Guid.NewGuid().ToString("D");
        var payload = new InventoryRunSyncDto
        {
            clientRunId = runId,
            userId = SessionManager.UserId,
            qrCode = qrCode,
            startedAt = startedAtUtc.ToUniversalTime().ToString("o"),
            completedAt = DateTime.UtcNow.ToString("o"),
            items = statuses.Select(pair => new InventoryRunItemSyncDto { itemId = pair.Key, status = pair.Value, comment = "" }).ToList()
        };

        bool savedLocally = false;
        if (localCabinet != null && LocalDatabase.Instance != null)
        {
            try
            {
                var results = statuses.Select(pair => new LocalInventoryItemResult { itemId = pair.Key, itemName = itemNames[pair.Key], status = pair.Value }).ToList();
                LocalDatabase.Instance.SaveInventoryRun(localCabinet.id, qrCode, results, startedAtUtc, runId);
                savedLocally = true;
            }
            catch (Exception exception) { Debug.LogWarning("[Inventory] Локальная копия не создана: " + exception.Message); }
        }

        if (!SessionManager.IsLoggedIn)
        {
            SetMessage(savedLocally ? "Сохранено на устройстве. Войдите в систему для отправки на сервер." : "Войдите в систему и повторите сохранение.");
            return;
        }
        saving = true;
        SetMessage("Отправляем результат на сервер…");
        InventoryApiClient.SubmitInventory(this, payload,
            result =>
            {
                saving = false;
                if (savedLocally) LocalDatabase.Instance?.MarkInventoryRunSynced(runId);
                SetMessage("Проверка сохранена на сервере и доступна администратору.");
            },
            error =>
            {
                saving = false;
                SetMessage(savedLocally ? "Нет связи с сервером. Результат сохранён локально — синхронизируйте позже." : "Не отправлено: " + error);
                Debug.LogWarning("[Inventory] Не удалось отправить проверку: " + error);
            });
    }

    private void SetMessage(string text) { if (message != null) message.text = text; }

    private static GameObject New(string name, Transform parent)
    { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }

    private static TextMeshProUGUI AddText(Transform parent, string name, string text, float size, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions alignment)
    {
        var go = New(name, parent); var tmp = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text; tmp.fontSize = size; tmp.alignment = alignment; tmp.color = Color.white;
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
        return tmp;
    }

    private static Button AddButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction action)
    {
        var go = New(label, parent); go.AddComponent<Image>().color = color;
        var button = go.AddComponent<Button>(); button.onClick.AddListener(action);
        var text = AddText(go.transform, "Label", label, 20, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        text.enableAutoSizing = true; text.fontSizeMin = 13; text.fontSizeMax = 20;
        return button;
    }

    private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }

    private void OnDestroy()
    {
        if (Active != this) return;
        Active = UnityEngine.Object.FindObjectsByType<InventoryChecklistController>(FindObjectsInactive.Include)
            .FirstOrDefault(controller => controller != null && controller != this);
    }
}
