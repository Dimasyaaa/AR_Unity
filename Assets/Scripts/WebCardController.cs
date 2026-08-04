using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Контроллер AR-карточки веб-страницы.
// Вешается на префаб WebCardVariant. Показывает заголовок, URL и кнопки
// "Открыть страницу" (запускает оверлей) и "Закрыть" (уничтожает объект).
// Один экземпляр на сцене (защита от дубликатов, как у калькулятора).
public class WebCardController : MonoBehaviour
{
    private static WebCardController current;

    private TextMeshProUGUI titleText;
    private TextMeshProUGUI urlText;

    private string pageTitle = "Веб-страница";
    private string pageUrl = "https://www.google.com";

    public void SetData(string title, string url)
    {
        pageTitle = string.IsNullOrEmpty(title) ? "Веб-страница" : title;
        pageUrl = string.IsNullOrEmpty(url) ? "https://www.google.com" : url;

        if (titleText != null) titleText.text = pageTitle;
        if (urlText != null) urlText.text = pageUrl;
    }

    private void Start()
    {
        if (current != null && current != this)
        {
            Destroy(gameObject);
            return;
        }
        current = this;

        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[WebCard] Canvas не найден на объекте!");
            return;
        }
        BuildUI(canvas.transform);
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
    }

    // ==========================================
    // Построение интерфейса
    // ==========================================
    private void BuildUI(Transform canvasRoot)
    {
        var panel = CreateUIObject("Panel", canvasRoot);
        panel.AddComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        Stretch(panel.GetComponent<RectTransform>());

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(30, 30, 30, 30);
        vlg.spacing = 20;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Заголовок
        var titleGo = CreateUIObject("Title", panel.transform);
        titleText = titleGo.AddComponent<TextMeshProUGUI>();
        SetFont(titleText);
        titleText.text = pageTitle;
        titleText.fontSize = 56;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;

        // URL
        var urlGo = CreateUIObject("URL", panel.transform);
        urlText = urlGo.AddComponent<TextMeshProUGUI>();
        SetFont(urlText);
        urlText.text = pageUrl;
        urlText.fontSize = 32;
        urlText.alignment = TextAlignmentOptions.Center;
        urlText.color = new Color(0.5f, 0.7f, 1f);

        // Подсказка
        var hintGo = CreateUIObject("Hint", panel.transform);
        var hint = hintGo.AddComponent<TextMeshProUGUI>();
        SetFont(hint);
        hint.text = "Нажмите кнопку ниже, чтобы открыть страницу";
        hint.fontSize = 28;
        hint.alignment = TextAlignmentOptions.Center;
        hint.color = new Color(0.8f, 0.8f, 0.8f);

        // Кнопка "Открыть страницу"
        var openGo = CreateUIObject("OpenButton", panel.transform);
        openGo.AddComponent<Image>().color = new Color(0.10f, 0.55f, 0.90f);
        var openBtn = openGo.AddComponent<Button>();
        CreateTextChild(openGo.transform, "Открыть страницу", 44);
        openBtn.onClick.AddListener(OpenPage);

        // Кнопка "Закрыть"
        var closeGo = CreateUIObject("CloseButton", panel.transform);
        closeGo.AddComponent<Image>().color = new Color(0.55f, 0.20f, 0.20f);
        var closeBtn = closeGo.AddComponent<Button>();
        CreateTextChild(closeGo.transform, "Закрыть", 44);
        closeBtn.onClick.AddListener(() => Destroy(gameObject));
    }

    private void OpenPage()
    {
        WebOverlayController.GetOrCreate().Open(pageUrl, pageTitle);
    }

    // ==========================================
    // Вспомогательные методы
    // ==========================================
    private GameObject CreateUIObject(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private TextMeshProUGUI CreateTextChild(Transform parent, string text, float size)
    {
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(parent, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        SetFont(tmp);
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        Stretch(textGo.GetComponent<RectTransform>());
        return tmp;
    }

    private void SetFont(TextMeshProUGUI tmp)
    {
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
    }
}