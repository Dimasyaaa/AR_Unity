using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Arranges the StartScene menu for portrait and landscape screens.</summary>
public class ResponsiveMainMenu : MonoBehaviour
{
    private RectTransform canvasRect;
    private Vector2Int lastSize;
    private Vector2Int lastCanvasSize;
    private Rect lastSafeArea;
    private bool lastPortrait;

    private void Awake()
    {
        canvasRect = GetComponent<RectTransform>();
        Layout();
    }

    private void Update()
    {
        Vector2Int canvasSize = canvasRect == null
            ? Vector2Int.zero
            : new Vector2Int(Mathf.RoundToInt(canvasRect.rect.width), Mathf.RoundToInt(canvasRect.rect.height));
        bool portrait = canvasSize.y >= canvasSize.x;
        if (lastSize.x != Screen.width || lastSize.y != Screen.height ||
            lastSafeArea != Screen.safeArea || lastPortrait != portrait || lastCanvasSize != canvasSize)
            Layout();
    }

    private void Layout()
    {
        if (canvasRect == null || canvasRect.rect.width <= 0 || canvasRect.rect.height <= 0 ||
            Screen.width <= 0 || Screen.height <= 0) return;

        Rect safe = Screen.safeArea;
        Vector2 safeMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        Vector2 safeMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        // In the Editor Game view the selected aspect ratio can differ from
        // Screen.width/height, so use the actual Canvas dimensions.
        bool portrait = canvasRect.rect.height >= canvasRect.rect.width;

        // These visual groups make the hierarchy easier to understand, but their
        // children still use Canvas-wide anchors. Keep the groups canvas-sized.
        StretchGroup("Header");
        StretchGroup("Menu Buttons");

        Set("BackGround", 0f, 1f, 0f, 1f, Vector2.zero, Vector2.one);

        float center = .5f;
        float logoWidth = portrait ? .62f : .38f;
        float logoHeight = portrait ? .16f : .20f;
        float logoTop = .975f;
        Set("Logo", center - logoWidth * .5f, center + logoWidth * .5f,
            logoTop - logoHeight, logoTop, safeMin, safeMax);

        float titleTop = portrait ? .79f : .75f;
        float titleHeight = portrait ? .08f : .10f;
        Set("TitleText", .08f, .92f, titleTop - titleHeight, titleTop, safeMin, safeMax);

        float infoTop = portrait ? .66f : .58f;
        float infoHeight = portrait ? .055f : .07f;
        if (portrait)
        {
            Set("UserInfoText", .08f, .92f, infoTop - infoHeight, infoTop, safeMin, safeMax);
            Set("SyncStatusText", .08f, .92f, infoTop - infoHeight * 2f, infoTop - infoHeight, safeMin, safeMax);
        }
        else
        {
            Set("UserInfoText", .06f, .47f, infoTop - infoHeight, infoTop, safeMin, safeMax);
            Set("SyncStatusText", .53f, .94f, infoTop - infoHeight, infoTop, safeMin, safeMax);
        }

        float buttonTop = portrait ? .49f : .44f;
        float buttonBottom = portrait ? .06f : .13f;
        float buttonLeft = .08f;
        float buttonRight = .92f;
        string[] names = { "ScanButton", "InstructionsButton", "SyncButton", "ExitButton" };
        if (portrait)
        {
            float row = (buttonTop - buttonBottom) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                float rowTop = buttonTop - row * i;
                float rowBottom = buttonTop - row * (i + 1);
                Set(names[i], buttonLeft, buttonRight,
                    rowBottom + .009f, rowTop - .009f, safeMin, safeMax);
            }
        }
        else
        {
            float col = (buttonRight - buttonLeft) * .5f;
            float row = (buttonTop - buttonBottom) * .5f;
            for (int i = 0; i < names.Length; i++)
            {
                int column = i % 2;
                int rowIndex = i / 2;
                float cellTop = buttonTop - row * rowIndex;
                float cellBottom = buttonTop - row * (rowIndex + 1);
                Set(names[i], buttonLeft + col * column + .008f,
                    buttonLeft + col * (column + 1) - .008f,
                    cellBottom + .012f, cellTop - .012f, safeMin, safeMax);
            }
        }

        Set("HelpButton", .91f, .97f, titleTop - titleHeight * .1f,
            titleTop + titleHeight * .8f, safeMin, safeMax);
        Set("WifiButton", .08f, .49f, .025f, .085f, safeMin, safeMax);
        Set("UsbButton", .51f, .92f, .025f, .085f, safeMin, safeMax);

        FitText(transform);
        SetTextWrapping("UserInfoText");
        SetTextWrapping("SyncStatusText");
        Transform logo = FindChildRecursive(transform, "Logo");
        if (logo != null && logo.TryGetComponent<Image>(out var image)) image.preserveAspect = true;

        lastSize = new Vector2Int(Screen.width, Screen.height);
        lastCanvasSize = new Vector2Int(
            Mathf.RoundToInt(canvasRect.rect.width), Mathf.RoundToInt(canvasRect.rect.height));
        lastSafeArea = safe;
        lastPortrait = portrait;
    }

    private void Set(string objectName, float left, float right, float bottom, float top,
        Vector2 safeMin, Vector2 safeMax)
    {
        Transform child = FindChildRecursive(transform, objectName);
        if (!(child is RectTransform rect)) return;
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

    private static void FitText(Transform root)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = 18;
            text.fontSizeMax = Mathf.Min(text.fontSize, 42);
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    private void SetTextWrapping(string objectName)
    {
        Transform item = FindChildRecursive(transform, objectName);
        if (item == null) return;
        foreach (TMP_Text text in item.GetComponentsInChildren<TMP_Text>(true))
            text.textWrappingMode = TextWrappingModes.Normal;
    }

    private void StretchGroup(string objectName)
    {
        Transform group = FindChildRecursive(transform, objectName);
        if (!(group is RectTransform rect)) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localPosition = Vector3.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static Transform FindChildRecursive(Transform root, string objectName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == objectName) return child;
            Transform nested = FindChildRecursive(child, objectName);
            if (nested != null) return nested;
        }
        return null;
    }
}
