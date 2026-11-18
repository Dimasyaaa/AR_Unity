using TMPro;
using UnityEngine;

/// <summary>
/// Перестраивает форму входа при изменении размера экрана, safeArea или ориентации.
/// Компонент находится на фоновой панели Canvas; поля формы ищутся по именам из LoginScene.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FitToSafeArea : MonoBehaviour
{
    // Ссылки и последние размеры нужны, чтобы не искать объекты заново каждый кадр.
    private RectTransform panel;
    private RectTransform canvas;
    private Vector2Int lastSize;
    private Rect lastSafeArea;
    private bool lastPortrait;

    private void Awake()
    {
        // Панель является фоном формы, а её родитель — Canvas.
        panel = (RectTransform)transform;
        canvas = panel.parent as RectTransform;
        Apply();
    }

    // Повторно применяем раскладку, если объект выключали и включали.
    private void OnEnable() => Apply();

    private void Update()
    {
        // Пересчитываем положение элементов только при изменении экрана.
        if (lastSize.x != Screen.width || lastSize.y != Screen.height ||
            lastSafeArea != Screen.safeArea || lastPortrait != (Screen.height >= Screen.width))
            Apply();
    }

    private void Apply()
    {
        // Пока Canvas или экран имеют нулевой размер, якоря вычислить нельзя.
        if (panel == null || canvas == null || Screen.width <= 0 || Screen.height <= 0 ||
            canvas.rect.width <= 0 || canvas.rect.height <= 0)
            return;

        Rect safe = Screen.safeArea;
        // Фоновая панель занимает весь экран; форма внутри неё раскладывается относительными якорями.
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = Vector2.zero;
        panel.localScale = Vector3.one;
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;

        bool portrait = Screen.height >= Screen.width;

        // Поля формы имеют разные отступы в книжной и альбомной ориентациях.
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
            // Кнопка помощи может быть создана рядом с панелью, поэтому переносим её внутрь
            // и располагаем в правом верхнем углу формы.
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
        // Поиск по имени позволяет оставить все визуальные элементы в самой сцене.
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
        // Ограничиваем размер текста и показываем многоточие вместо выхода за границы.
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
            // В портретном режиме длинное название отдела можно переносить на следующую строку.
            dropdown.captionText.textWrappingMode = Screen.height >= Screen.width
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;
        }
    }
}
