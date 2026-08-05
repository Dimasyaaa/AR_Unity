using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Карточка-"сайт": лист с инфо о продукции + переключение листов.
public class ImageCardController : MonoBehaviour
{
    private static ImageCardController current;

    private RawImage rawImage;
    private RectTransform imageRect;
    private Texture2D[] sheets;

    [Header("Максимальная область под изображение")]
    [SerializeField] private float maxImageWidth = 500f;
    [SerializeField] private float maxImageHeight = 640f;

    [Header("Листы (или из Resources/Images)")]
    [SerializeField] private Texture2D sheet1;
    [SerializeField] private Texture2D sheet2;

    void Start()
    {
        if (current != null && current != this) { Destroy(gameObject); return; }
        current = this;

        if (sheet1 == null) sheet1 = Resources.Load<Texture2D>("Images/product_sheet_1");
        if (sheet2 == null) sheet2 = Resources.Load<Texture2D>("Images/product_sheet_2");
        sheets = new Texture2D[] { sheet1, sheet2 };

        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { Debug.LogError("[ImageCard] Canvas не найден!"); return; }
        BuildUI(canvas.transform);
        ShowSheet(0);
    }

    void OnDestroy() { if (current == this) current = null; }

    void BuildUI(Transform root)
    {
        var panel = Create("Panel", root);
        panel.AddComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        Stretch(panel.GetComponent<RectTransform>());

        // Заголовок сверху
        var title = Create("Title", panel.transform);
        var t = title.AddComponent<TextMeshProUGUI>();
        SetFont(t);
        t.text = "Информация о продукции";
        t.fontSize = 44;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0.5f, 1);
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = new Vector2(0, 80);

        // Изображение по центру (размер подгоняется под пропорции)
        var imgGo = Create("Sheet", panel.transform);
        rawImage = imgGo.AddComponent<RawImage>();
        imageRect = imgGo.GetComponent<RectTransform>();
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = new Vector2(0, 20);
        imageRect.sizeDelta = new Vector2(maxImageWidth, maxImageHeight);

        // Кнопки снизу
        var row = Create("Row", panel.transform);
        var rrt = row.AddComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0, 0); rrt.anchorMax = new Vector2(1, 0);
        rrt.pivot = new Vector2(0.5f, 0);
        rrt.anchoredPosition = new Vector2(0, 35);
        rrt.sizeDelta = new Vector2(0, 110);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 15;
        hlg.padding = new RectOffset(20, 20, 20, 0);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        MakeBtn(row.transform, "Лист 1", () => ShowSheet(0), new Color(0.10f, 0.55f, 0.90f));
        MakeBtn(row.transform, "Лист 2", () => ShowSheet(1), new Color(0.10f, 0.55f, 0.90f));
        MakeBtn(row.transform, "Закрыть", () => Destroy(gameObject), new Color(0.55f, 0.20f, 0.20f));
    }

    void ShowSheet(int i)
    {
        if (sheets == null || sheets.Length == 0 || rawImage == null) return;
        int idx = Mathf.Clamp(i, 0, sheets.Length - 1);
        var tex = sheets[idx];
        if (tex == null) { Debug.LogWarning("[ImageCard] Лист не найден: " + idx); return; }

        rawImage.texture = tex;

        // Сохраняем пропорции: вписываем в max-область без сжатия
        float aspect = (float)tex.width / tex.height;
        float w = maxImageWidth;
        float h = w / aspect;
        if (h > maxImageHeight) { h = maxImageHeight; w = h * aspect; }
        imageRect.sizeDelta = new Vector2(w, h);
    }

    // ===== вспомогательные =====
    GameObject Create(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    void SetFont(TextMeshProUGUI tmp)
    {
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }

    void MakeBtn(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
    {
        var go = Create(label, parent);
        go.AddComponent<Image>().color = color;
        go.AddComponent<Button>().onClick.AddListener(onClick);

        var tg = Create("Text", go.transform);
        var tmp = tg.AddComponent<TextMeshProUGUI>();
        SetFont(tmp);
        tmp.text = label;
        tmp.fontSize = 40;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        Stretch(tg.GetComponent<RectTransform>());
    }
}