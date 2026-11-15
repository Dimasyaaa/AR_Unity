using TMPro;
using UnityEngine;

/// <summary>Builds a responsive login layout inside the device safe area.</summary>
[RequireComponent(typeof(RectTransform))]
public class FitToSafeArea : MonoBehaviour
{
    private RectTransform panel;
    private RectTransform canvas;
    private Vector2Int lastSize;
    private Rect lastSafeArea;
    private bool lastPortrait;

    private void Awake()
    {
        panel = (RectTransform)transform;
        canvas = panel.parent as RectTransform;
        Apply();
    }

    private void OnEnable() => Apply();

    private void Update()
    {
        if (lastSize.x != Screen.width || lastSize.y != Screen.height ||
            lastSafeArea != Screen.safeArea || lastPortrait != (Screen.height >= Screen.width))
            Apply();
    }

    private void Apply()
    {
        if (panel == null || canvas == null || Screen.width <= 0 || Screen.height <= 0 ||
            canvas.rect.width <= 0 || canvas.rect.height <= 0)
            return;

        Rect safe = Screen.safeArea;
        // The panel is the full-screen background. Safe margins are built into the child anchors.
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = Vector2.zero;
        panel.localScale = Vector3.one;
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;

        bool portrait = Screen.height >= Screen.width;

        // Use normalized anchors so every control stays inside its panel at any aspect ratio.
        float fieldLeft = portrait ? .08f : .20f;
        float fieldRight = portrait ? .92f : .80f;
        Place("TitleText", portrait ? .08f : .20f, .87f, portrait ? .82f : .80f, .96f);
        Place("DepartmentDropdown", fieldLeft, .72f, fieldRight, .82f);
        Place("FullNameInput", fieldLeft, .60f, fieldRight, .70f);
        Place("PasswordInput", fieldLeft, .48f, fieldRight, .58f);
        Place("LoginStatusText", fieldLeft, .28f, fieldRight, .38f);
        Place("LoginButton", fieldLeft, .10f, fieldRight, .22f);

        FitText(panel);
        Transform help = panel.Find("HelpButton");
        if (help == null) help = canvas.Find("HelpButton");
        if (help is RectTransform helpRect)
        {
            helpRect.SetParent(panel, false);
            helpRect.anchorMin = portrait ? new Vector2(.82f, .86f) : new Vector2(.86f, .84f);
            helpRect.anchorMax = portrait ? new Vector2(.94f, .93f) : new Vector2(.93f, .96f);
            helpRect.pivot = new Vector2(.5f, .5f);
            helpRect.offsetMin = Vector2.zero;
            helpRect.offsetMax = Vector2.zero;
            helpRect.SetAsLastSibling();
        }

        lastSize = new Vector2Int(Screen.width, Screen.height);
        lastSafeArea = safe;
        lastPortrait = portrait;
    }

    private void Place(string name, float left, float bottom, float right, float top)
    {
        Transform child = panel.Find(name);
        if (!(child is RectTransform rect)) return;
        rect.anchorMin = new Vector2(left, bottom);
        rect.anchorMax = new Vector2(right, top);
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
            text.fontSizeMax = 42;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        TMP_Dropdown dropdown = root.Find("DepartmentDropdown")?.GetComponent<TMP_Dropdown>();
        if (dropdown != null && dropdown.captionText != null)
        {
            dropdown.captionText.textWrappingMode = Screen.height >= Screen.width
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;
        }
    }
}
