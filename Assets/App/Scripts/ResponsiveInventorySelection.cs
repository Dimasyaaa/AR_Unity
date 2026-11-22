using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Раскладывает готовые элементы сцены выбора инвентаризации с учётом ориентации и safe area.
/// Сам интерфейс остаётся в сцене; компонент меняет только якоря и параметры отображения текста.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class ResponsiveInventorySelection : MonoBehaviour
{
    private RectTransform canvasRect;
    private Vector2Int lastScreenSize;
    private Vector2Int lastCanvasSize;
    private Rect lastSafeArea;
    private bool lastPortrait;

    private void Awake()
    {
        canvasRect = (RectTransform)transform;
        ApplyLayout();
    }

    private void OnEnable() => ApplyLayout();

    private void Update()
    {
        if (canvasRect == null)
            return;

        Vector2Int canvasSize = new Vector2Int(
            Mathf.RoundToInt(canvasRect.rect.width),
            Mathf.RoundToInt(canvasRect.rect.height));
        bool portrait = canvasSize.y >= canvasSize.x;

        if (lastScreenSize.x != Screen.width || lastScreenSize.y != Screen.height ||
            lastCanvasSize != canvasSize || lastSafeArea != Screen.safeArea || lastPortrait != portrait)
            ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (canvasRect == null || canvasRect.rect.width <= 0 || canvasRect.rect.height <= 0 ||
            Screen.width <= 0 || Screen.height <= 0)
            return;

        Rect safeArea = Screen.safeArea;
        Vector2 safeMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
        Vector2 safeMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
        bool portrait = canvasRect.rect.height >= canvasRect.rect.width;

        // Фон покрывает экран, остальное размещается внутри безопасной зоны устройства.
        SetRect("Panel", 0f, 1f, 0f, 1f, Vector2.zero, Vector2.one);

        if (portrait)
        {
            SetRect("ScreenTitle", .06f, .94f, .89f, .97f, safeMin, safeMax);
            SetRect("StatusText", .07f, .93f, .81f, .875f, safeMin, safeMax);
            SetRect("AssignmentScroll", .045f, .955f, .415f, .79f, safeMin, safeMax);
            SetRect("HistoryScroll", .045f, .955f, .185f, .395f, safeMin, safeMax);
            SetRect("StartInventoryButton", .07f, .93f, .095f, .16f, safeMin, safeMax);
            SetRect("BackToMenuButton", .07f, .93f, .025f, .09f, safeMin, safeMax);
        }
        else
        {
            SetRect("ScreenTitle", .08f, .92f, .89f, .97f, safeMin, safeMax);
            SetRect("StatusText", .08f, .92f, .81f, .875f, safeMin, safeMax);
            SetRect("AssignmentScroll", .045f, .575f, .22f, .79f, safeMin, safeMax);
            SetRect("HistoryScroll", .605f, .955f, .22f, .79f, safeMin, safeMax);
            SetRect("StartInventoryButton", .09f, .485f, .075f, .16f, safeMin, safeMax);
            SetRect("BackToMenuButton", .515f, .91f, .075f, .16f, safeMin, safeMax);
        }

        ConfigureText("ScreenTitle", 26f, 48f, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
        ConfigureText("StatusText", 14f, 24f, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
        ConfigureText("AssignmentText", 14f, 25f, TextAlignmentOptions.TopLeft, TextOverflowModes.Overflow);
        ConfigureText("HistoryText", 14f, 23f, TextAlignmentOptions.TopLeft, TextOverflowModes.Overflow);
        ConfigureText("StartInventoryButton", 16f, 30f, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
        ConfigureText("BackToMenuButton", 16f, 30f, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

        // Прокручиваемые тексты расширяются по высоте и сохраняют общий верхний край.
        ConfigureScrollContent("AssignmentScroll", "AssignmentText");
        ConfigureScrollContent("HistoryScroll", "HistoryText");

        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        lastCanvasSize = new Vector2Int(
            Mathf.RoundToInt(canvasRect.rect.width), Mathf.RoundToInt(canvasRect.rect.height));
        lastSafeArea = safeArea;
        lastPortrait = portrait;
    }

    private void SetRect(string objectName, float left, float right, float bottom, float top,
        Vector2 safeMin, Vector2 safeMax)
    {
        Transform item = FindChild(transform, objectName);
        if (!(item is RectTransform rect))
            return;

        rect.anchorMin = new Vector2(
            Mathf.Lerp(safeMin.x, safeMax.x, left),
            Mathf.Lerp(safeMin.y, safeMax.y, bottom));
        rect.anchorMax = new Vector2(
            Mathf.Lerp(safeMin.x, safeMax.x, right),
            Mathf.Lerp(safeMin.y, safeMax.y, top));
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void ConfigureScrollContent(string scrollName, string contentName)
    {
        Transform scrollTransform = FindChild(transform, scrollName);
        Transform contentTransform = FindChild(transform, contentName);
        if (scrollTransform == null || !(contentTransform is RectTransform content))
            return;

        ScrollRect scroll = scrollTransform.GetComponent<ScrollRect>();
        if (scroll == null)
            return;

        scroll.viewport = scrollTransform as RectTransform;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;

        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(.5f, 1f);
        content.anchoredPosition = new Vector2(0f, -14f);
        content.sizeDelta = new Vector2(-32f, 0f);
    }

    private void ConfigureText(string objectName, float minSize, float maxSize,
        TextAlignmentOptions alignment, TextOverflowModes overflow)
    {
        Transform item = FindChild(transform, objectName);
        if (item == null)
            return;

        foreach (TMP_Text text in item.GetComponentsInChildren<TMP_Text>(true))
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = minSize;
            text.fontSizeMax = maxSize;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = overflow;
            text.raycastTarget = false;
        }
    }

    private static Transform FindChild(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child != root && child.name == objectName)
                return child;
        return null;
    }
}
