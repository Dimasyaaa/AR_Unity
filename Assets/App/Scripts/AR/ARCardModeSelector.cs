using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Показывает выбор между экранным окном и размещением AR-карточки в пространстве.</summary>
public static class ARCardModeSelector
{
    // Ссылка нужна, чтобы перед показом нового окна закрыть предыдущее.
    private static GameObject dialog;

    // Список открытых экранных карточек нужен для ограничения числа окон и поиска дубликатов.
    private static readonly List<GameObject> overlays = new();

    /// <summary>Проверяет, открыта ли экранная копия выбранной карточки.</summary>
    public static bool HasOverlay(GameObject prefab)
    {
        overlays.RemoveAll(item => item == null);
        return overlays.Any(item => item.name == prefab.name + " (Overlay)");
    }

    /// <summary>Показывает пользователю варианты отображения карточки.</summary>
    public static void Show(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("[AR Cards] Префаб карточки не назначен.");
            return;
        }

        // Объёмная модель остаётся AR-объектом и не открывается плоским оверлеем.
        if (prefab.GetComponentInChildren<BoxModelsCardController>(true) != null)
        {
            if (!ARCardSpawnUtility.IsCardAlreadyPresent(prefab))
                ARCardSpawnUtility.SelectForPlacement(prefab);
            return;
        }

        CloseDialog();
        dialog = new GameObject("ARCardModeDialog", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = dialog.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;
        ConfigureScaler(dialog.GetComponent<CanvasScaler>());

        var dim = NewRect("Dim", dialog.transform);
        var dimImage = dim.gameObject.AddComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, .68f);
        dimImage.raycastTarget = true;
        Stretch(dim);

        var panel = NewRect("Panel", dialog.transform);
        panel.sizeDelta = new Vector2(760f, 660f);
        panel.gameObject.AddComponent<Image>().color = new Color(.09f, .12f, .18f, .99f);
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 34, 34);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var title = NewText("Title", panel, $"Как показать: {GetDisplayName(prefab)}?", 42f, FontStyles.Bold);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;
        AddButton(panel, "Поверх экрана · оверлей", new Color(.08f, .48f, .78f), () =>
        {
            CloseDialog();
            ARCardSpawnUtility.DisablePlacement();
            ShowOverlay(prefab);
        });
        AddButton(panel, "В пространстве · AR-объект", new Color(.12f, .58f, .32f), () =>
        {
            CloseDialog();
            ARCardSpawnUtility.SelectForPlacement(prefab);
        });
        AddButton(panel, "Отмена", new Color(.30f, .34f, .41f), CloseDialog);
    }

    /// <summary>Создаёт экранное окно карточки и добавляет кнопки изменения масштаба.</summary>
    private static void ShowOverlay(GameObject prefab)
    {
        overlays.RemoveAll(item => item == null);
        if (ARCardSpawnUtility.IsCardAlreadyPresent(prefab))
        {
            Debug.LogWarning($"[AR Cards] Окно «{GetDisplayName(prefab)}» уже открыто. Повторно добавить его нельзя.");
            return;
        }
        if (overlays.Count >= 3)
        {
            Debug.LogWarning("[AR Cards] Уже открыты три окна оверлея. Закройте одно, чтобы открыть ещё.");
            return;
        }

        int windowIndex = overlays.Count;
        int sortingBase = 9000 + windowIndex * 10;
        Vector2[] positions = { Vector2.zero, new Vector2(44f, -44f), new Vector2(-44f, 44f) };
        float[] scales = { 1f, .82f, .68f };
        GameObject overlay = new GameObject("ARCardOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        overlays.Add(overlay);
        var rootCanvas = overlay.GetComponent<Canvas>();
        rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        rootCanvas.sortingOrder = sortingBase;
        ConfigureScaler(overlay.GetComponent<CanvasScaler>());

        var background = NewRect("Background", overlay.transform);
        var backgroundImage = background.gameObject.AddComponent<Image>();
        backgroundImage.color = new Color(.015f, .025f, .045f, 0f);
        backgroundImage.raycastTarget = false;
        Stretch(background);

        var card = UnityEngine.Object.Instantiate(prefab, overlay.transform, false);
        card.name = prefab.name + " (Overlay)";
        card.transform.localPosition = Vector3.zero;
        card.transform.localRotation = Quaternion.identity;
        card.transform.localScale = Vector3.one;

        foreach (var renderer in card.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (var collider in card.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in card.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        foreach (var behaviour in card.GetComponents<Behaviour>())
        {
            if (behaviour is InventoryChecklistController || behaviour is ImageCardController ||
                behaviour is CalculatorController) continue;
            behaviour.enabled = false;
        }

        var cardCanvas = card.GetComponentInChildren<Canvas>(true);
        if (cardCanvas == null)
        {
            Debug.LogError($"[AR Cards] У карточки «{prefab.name}» нет Canvas для оверлея.");
            overlays.Remove(overlay);
            UnityEngine.Object.Destroy(overlay);
            return;
        }

        cardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        cardCanvas.overrideSorting = true;
        cardCanvas.sortingOrder = sortingBase + 1;
        cardCanvas.worldCamera = null;
        ConfigureScaler(cardCanvas.GetComponent<CanvasScaler>());
        var cardRect = cardCanvas.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f);
        cardRect.pivot = new Vector2(.5f, .5f);
        cardRect.anchoredPosition = positions[windowIndex];
        cardRect.sizeDelta = new Vector2(Mathf.Min(920f, Screen.width - 32f), Mathf.Min(1500f, Screen.height - 160f));
        cardRect.localPosition = Vector3.zero;
        cardRect.localRotation = Quaternion.identity;
        cardRect.localScale = Vector3.one * scales[windowIndex];
        CreateScaleControls(overlay.transform, cardRect, positions[windowIndex], scales[windowIndex], sortingBase + 2);
    }

    private static void CreateScaleControls(Transform parent, RectTransform cardRect, Vector2 position, float scale, int sortingOrder)
    {
        var controlsObject = new GameObject("OverlayScaleControls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        controlsObject.transform.SetParent(parent, false);
        var controlsCanvas = controlsObject.GetComponent<Canvas>();
        controlsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        controlsCanvas.overrideSorting = true;
        controlsCanvas.sortingOrder = sortingOrder;
        ConfigureScaler(controlsObject.GetComponent<CanvasScaler>());

        var bar = NewRect("ScaleBar", controlsObject.transform);
        bar.anchorMin = bar.anchorMax = new Vector2(.5f, .5f);
        bar.sizeDelta = new Vector2(210f, 72f);
        bar.anchoredPosition = position + new Vector2(0f, cardRect.sizeDelta.y * scale * .5f + 48f);
        bar.gameObject.AddComponent<Image>().color = new Color(.08f, .11f, .16f, .94f);

        var label = NewText("ScaleLabel", bar, "Масштаб", 22f, FontStyles.Bold);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f); labelRect.anchorMax = new Vector2(.48f, 1f);
        labelRect.offsetMin = Vector2.zero; labelRect.offsetMax = Vector2.zero;
        AddScaleButton(bar, "−", .67f, () => SetScale(cardRect, Mathf.Max(.5f, cardRect.localScale.x - .1f), bar, position));
        AddScaleButton(bar, "+", .91f, () => SetScale(cardRect, Mathf.Min(1.15f, cardRect.localScale.x + .1f), bar, position));
    }

    private static void AddScaleButton(RectTransform parent, string label, float anchor, UnityEngine.Events.UnityAction action)
    {
        var rect = NewRect(label, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(anchor, .5f);
        rect.sizeDelta = new Vector2(54f, 54f);
        rect.anchoredPosition = Vector2.zero;
        rect.gameObject.AddComponent<Image>().color = new Color(.12f, .48f, .76f, 1f);
        rect.gameObject.AddComponent<Button>().onClick.AddListener(action);
        NewText("Label", rect, label, 30f, FontStyles.Bold).raycastTarget = false;
    }

    private static void SetScale(RectTransform card, float scale, RectTransform bar, Vector2 position)
    {
        card.localScale = Vector3.one * scale;
        bar.anchoredPosition = position + new Vector2(0f, card.sizeDelta.y * scale * .5f + 48f);
    }

    /// <summary>Закрывает карточку или всё экранное окно, которому она принадлежит.</summary>
    public static void CloseCard(GameObject card)
    {
        if (card == null) return;
        var parent = card.transform.parent;
        if (parent != null && parent.name == "ARCardOverlay")
        {
            var overlay = parent.gameObject;
            overlays.Remove(overlay);
            UnityEngine.Object.Destroy(overlay);
        }
        else UnityEngine.Object.Destroy(card);
    }

    /// <summary>Закрывает диалог выбора режима, если он сейчас открыт.</summary>
    internal static void CloseDialog()
    {
        if (dialog != null) UnityEngine.Object.Destroy(dialog);
        dialog = null;
    }

    private static void ConfigureScaler(CanvasScaler scaler)
    {
        if (scaler == null) return;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = .5f;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string value, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.enableAutoSizing = true;
        text.fontSizeMin = 20f;
        text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return text;
    }

    private static void AddButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction action)
    {
        var rect = NewRect(label, parent);
        rect.gameObject.AddComponent<Image>().color = color;
        rect.gameObject.AddComponent<Button>().onClick.AddListener(action);
        NewText("Label", rect, label, 34f, FontStyles.Bold);
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 112f;
    }

    /// <summary>Возвращает короткое название карточки для диалога выбора режима.</summary>
    private static string GetDisplayName(GameObject prefab)
    {
        if (prefab.GetComponent<InventoryChecklistController>() != null) return "инвентаризацию";
        if (prefab.GetComponent<ImageCardController>() != null) return "содержимое коробки";
        if (prefab.GetComponent<BoxModelsCardController>() != null) return "3D-модели коробки";
        if (prefab.GetComponent<CalculatorController>() != null) return "калькулятор";
        return prefab.name;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
