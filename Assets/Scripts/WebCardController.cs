using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Контроллер AR-карточки веб-страницы.
// Сам подтягивает данные из QRResolver в Start().
// Один экземпляр на сцене.
public class WebCardController : MonoBehaviour
{
    private static WebCardController current;

    private TextMeshProUGUI titleText;
    private TextMeshProUGUI urlText;
    private Button openBtn;
    private GameObject openGo;

    private string pageTitle = "QR-код";
    private string pageUrl;

    public bool IsPinned { get; set; }

    public void Initialize(bool pinned)
    {
        IsPinned = pinned;
    }

    private void Start()
    {
        if (current != null && current != this) { Destroy(gameObject); return; }
        current = this;

        // Автозаполнение из глобального LastScannedQR
        var r = QRResolver.Resolve(GameDataManager.LastScannedQR);
        pageTitle = r.title;
        pageUrl = r.url;

        if (!r.foundInDb && string.IsNullOrEmpty(pageUrl))
            pageTitle = "QR не найден в базе";

        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { Debug.LogError("[WebCard] Canvas не найден"); return; }
        BuildUI(canvas.transform);
    }

    private void OnDestroy() { if (current == this) current = null; }

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

        // Заголовок (название изделия или host)
        var titleGo = CreateUIObject("Title", panel.transform);
        titleText = titleGo.AddComponent<TextMeshProUGUI>();
        SetFont(titleText);
        titleText.text = pageTitle;
        titleText.fontSize = 56;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;

        // URL (или "без ссылки")
        var urlGo = CreateUIObject("URL", panel.transform);
        urlText = urlGo.AddComponent<TextMeshProUGUI>();
        SetFont(urlText);
        urlText.text = string.IsNullOrEmpty(pageUrl) ? "(без ссылки)" : pageUrl;
        urlText.fontSize = 28;
        urlText.alignment = TextAlignmentOptions.Center;
        urlText.color = new Color(0.5f, 0.7f, 1f);

        // Подсказка
        var hintGo = CreateUIObject("Hint", panel.transform);
        var hint = hintGo.AddComponent<TextMeshProUGUI>();
        SetFont(hint);
        hint.text = string.IsNullOrEmpty(pageUrl)
            ? "Для этого QR нет ссылки"
            : "Нажмите кнопку ниже, чтобы открыть страницу";
        hint.fontSize = 28;
        hint.alignment = TextAlignmentOptions.Center;
        hint.color = new Color(0.8f, 0.8f, 0.8f);

        // Кнопка "Открыть страницу"
        openGo = CreateUIObject("OpenButton", panel.transform);
        openGo.AddComponent<Image>().color = new Color(0.10f, 0.55f, 0.90f);
        openBtn = openGo.AddComponent<Button>();
        CreateTextChild(openGo.transform, "Открыть страницу", 44);
        openBtn.onClick.AddListener(OpenPage);

        // Если URL нет — кнопка неактивна
        if (string.IsNullOrEmpty(pageUrl))
        {
            openBtn.interactable = false;
            openGo.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);
        }

        // Кнопка "Закрыть"
        var closeGo = CreateUIObject("CloseButton", panel.transform);
        closeGo.AddComponent<Image>().color = new Color(0.55f, 0.20f, 0.20f);
        var closeBtn = closeGo.AddComponent<Button>();
        CreateTextChild(closeGo.transform, "Закрыть", 44);
        closeBtn.onClick.AddListener(() => Destroy(gameObject));
    }

    private void OpenPage()
    {
        if (string.IsNullOrEmpty(pageUrl)) return;
        WebOverlayController.GetOrCreate().Open(pageUrl, pageTitle);
    }

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
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }
}