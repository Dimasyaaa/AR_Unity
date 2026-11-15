using UnityEngine;
using UnityEngine.UI;
using Gree.UnityWebView;

// Singleton, открывающий нативный WebView как оверлей поверх экрана.
// Используется и текущим кубом (через CubeWebViewHandler при желании),
// и новой AR-карточкой.
public class WebOverlayController : MonoBehaviour
{
    public static WebOverlayController Instance { get; private set; }

    private WebViewObject webViewObject;
    private GameObject webViewTrigger;
    private GameObject uiCanvas;
    private GameObject loadingPanel;
    private Text loadingText;

    private int marginLeft, marginTop, marginRight, marginBottom;
    private float currentZoom = 1.0f;
    private const float zoomStep = 0.4f;
    private const float minZoom = 0.5f;
    private const float maxZoom = 5.0f;
    private const int moveStep = 150;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static WebOverlayController GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("WebOverlayController");
        go.AddComponent<WebOverlayController>();
        return Instance;
    }

    public void Open(string url, string title = "")
    {
        // Если уже открыт — закрываем и открываем заново с новым URL
        Close();

        if (string.IsNullOrEmpty(url)) url = "https://www.google.com";

        currentZoom = 1.0f;
        CreateWebViewCanvas(url);
        CreateUI(title);
    }

    public void Close()
    {
        if (webViewTrigger != null) { Destroy(webViewTrigger); webViewTrigger = null; }
        if (uiCanvas != null) { Destroy(uiCanvas); uiCanvas = null; }
        webViewObject = null;
        loadingPanel = null;
        loadingText = null;
    }

    private void CreateWebViewCanvas(string url)
    {
        webViewTrigger = new GameObject("WebViewTrigger");
        webViewTrigger.transform.SetParent(transform);
        webViewTrigger.transform.localPosition = Vector3.zero;
        webViewTrigger.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.1f, 0.1f);

#if UNITY_ANDROID || UNITY_IOS
        try
        {
            webViewObject = webViewTrigger.AddComponent<WebViewObject>();
            webViewObject.Init(
                cb: msg => Debug.Log($"[WebView] {msg}"),
                err: msg => Debug.LogError($"[WebView Error] {msg}"),
                httpErr: msg => Debug.LogError($"[WebView HTTP] {msg}"),
                ld: msg =>
                {
                    if (loadingPanel != null) loadingPanel.SetActive(false);
                },
                started: msg =>
                {
                    if (loadingText != null)
                        loadingText.text = "Загрузка содержимого...\nПожалуйста, подождите.";
                },
                enableWKWebView: true,
                zoom: false,
                transparent: true
            );

            CalculateMargins();
            UpdateMargins();
            webViewObject.SetVisibility(true);
            webViewObject.LoadURL(url);
        }
        catch (System.Exception e)
        {
            Debug.LogError("WebView init failed: " + e.Message);
            Application.OpenURL(url);
        }
#else
        Application.OpenURL(url);
        Invoke(nameof(Close), 0.5f);
#endif
    }

    private void CalculateMargins()
    {
        bool isPortrait = Screen.height > Screen.width;
        if (isPortrait)
        {
            int w = (int)(Screen.width / 2f * currentZoom);
            int h = (int)(Screen.height / 2.5f * currentZoom);
            marginLeft = (Screen.width - w) / 2;
            marginTop = 220;
            marginRight = Screen.width - marginLeft - w;
            marginBottom = Screen.height - marginTop - h;
        }
        else
        {
            marginLeft = (int)(Screen.width * 0.6f);
            marginTop = (int)(Screen.height * 0.15f * currentZoom);
            marginRight = (int)(Screen.width * 0.05f);
            int h = (int)(Screen.height * 0.4f * currentZoom);
            marginBottom = Screen.height - marginTop - h;
        }
    }

    private void UpdateMargins()
    {
        if (webViewObject != null)
            webViewObject.SetMargins(marginLeft, marginTop, marginRight, marginBottom);
    }

    private void CreateUI(string title)
    {
        uiCanvas = new GameObject("WebOverlayUI");
        var canvas = uiCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        var scaler = uiCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        uiCanvas.AddComponent<GraphicRaycaster>();

        // Панель загрузки
        loadingPanel = new GameObject("LoadingPanel");
        loadingPanel.transform.SetParent(uiCanvas.transform, false);
        var lpRect = loadingPanel.AddComponent<RectTransform>();
        lpRect.anchorMin = Vector2.zero; lpRect.anchorMax = Vector2.one;
        lpRect.sizeDelta = Vector2.zero; lpRect.anchoredPosition = Vector2.zero;
        loadingPanel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        var tgo = new GameObject("LoadingText");
        tgo.transform.SetParent(loadingPanel.transform, false);
        var tRect = tgo.AddComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one;
        tRect.sizeDelta = Vector2.zero;
        loadingText = tgo.AddComponent<Text>();
        loadingText.text = "Инициализация браузера...\nПожалуйста, подождите.";
        loadingText.fontSize = 48;
        loadingText.color = Color.white;
        loadingText.alignment = TextAnchor.MiddleCenter;
        loadingText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Верхняя панель с заголовком и кнопкой X
        var top = new GameObject("TopBar");
        top.transform.SetParent(uiCanvas.transform, false);
        var topRect = top.AddComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0, 1);
        topRect.anchorMax = new Vector2(1, 1);
        topRect.sizeDelta = new Vector2(0, 140);
        topRect.anchoredPosition = new Vector2(0, -70);
        top.AddComponent<Image>().color = new Color(0, 0, 0, 0.8f);

        var titleLabel = new GameObject("Title");
        titleLabel.transform.SetParent(top.transform, false);
        var tlRect = titleLabel.AddComponent<RectTransform>();
        tlRect.anchorMin = new Vector2(0, 0);
        tlRect.anchorMax = new Vector2(1, 1);
        tlRect.offsetMin = new Vector2(30, 0);
        tlRect.offsetMax = new Vector2(-150, 0);
        var tl = titleLabel.AddComponent<Text>();
        tl.text = string.IsNullOrEmpty(title) ? "Веб-страница" : title;
        tl.fontSize = 40;
        tl.color = Color.white;
        tl.alignment = TextAnchor.MiddleLeft;
        tl.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Кнопки управления (нижняя панель)
        var buttonPanel = new GameObject("ButtonPanel");
        buttonPanel.transform.SetParent(uiCanvas.transform, false);
        var bpRect = buttonPanel.AddComponent<RectTransform>();
        bpRect.anchorMin = new Vector2(0, 1);
        bpRect.anchorMax = new Vector2(1, 1);
        bpRect.sizeDelta = new Vector2(0, 250);
        bpRect.anchoredPosition = new Vector2(0, -100 - 140);
        buttonPanel.AddComponent<Image>().color = new Color(0, 0, 0, 0.8f);

        float bs = 180f, spacing = 100f;
        float totalW = bs * 7 + spacing * 6;
        float startX = -totalW / 2f + bs / 2f;

        CreateCtrlBtn(buttonPanel, "←", startX + (bs + spacing) * 0, OnMoveLeft, new Color(0.3f, 0.5f, 0.8f));
        CreateCtrlBtn(buttonPanel, "→", startX + (bs + spacing) * 1, OnMoveRight, new Color(0.3f, 0.5f, 0.8f));
        CreateCtrlBtn(buttonPanel, "↑", startX + (bs + spacing) * 2, OnMoveUp, new Color(0.3f, 0.5f, 0.8f));
        CreateCtrlBtn(buttonPanel, "↓", startX + (bs + spacing) * 3, OnMoveDown, new Color(0.3f, 0.5f, 0.8f));
        CreateCtrlBtn(buttonPanel, "+", startX + (bs + spacing) * 4, OnZoomIn, new Color(0.2f, 0.7f, 0.3f));
        CreateCtrlBtn(buttonPanel, "-", startX + (bs + spacing) * 5, OnZoomOut, new Color(0.8f, 0.5f, 0.2f));
        CreateCtrlBtn(buttonPanel, "X", startX + (bs + spacing) * 6, Close, Color.red, 120);
    }

    private void CreateCtrlBtn(GameObject parent, string label, float x,
        UnityEngine.Events.UnityAction onClick, Color color, float size = 180f)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent.transform, false);
        var r = go.AddComponent<RectTransform>();
        r.sizeDelta = new Vector2(size, size);
        r.anchorMin = new Vector2(0.5f, 0.5f);
        r.anchorMax = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(x, 0);
        go.AddComponent<Image>().color = color;
        go.AddComponent<Button>().onClick.AddListener(onClick);

        var tg = new GameObject("Text");
        tg.transform.SetParent(go.transform, false);
        var tr = tg.AddComponent<RectTransform>();
        tr.sizeDelta = new Vector2(size, size);
        var t = tg.AddComponent<Text>();
        t.text = label;
        t.fontSize = 100;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void OnMoveLeft() { if (marginLeft > 0) { marginLeft -= moveStep; marginRight += moveStep; UpdateMargins(); } }
    private void OnMoveRight() { if (marginRight > 0) { marginLeft += moveStep; marginRight -= moveStep; UpdateMargins(); } }
    private void OnMoveUp() { if (marginTop > 200) { marginTop -= moveStep; marginBottom += moveStep; UpdateMargins(); } }
    private void OnMoveDown() { if (marginBottom > 0) { marginTop += moveStep; marginBottom -= moveStep; UpdateMargins(); } }
    private void OnZoomIn() { if (currentZoom < maxZoom) { currentZoom = Mathf.Min(maxZoom, currentZoom + zoomStep); ApplyZoom(); } }
    private void OnZoomOut() { if (currentZoom > minZoom) { currentZoom = Mathf.Max(minZoom, currentZoom - zoomStep); ApplyZoom(); } }

    private void ApplyZoom()
    {
        bool isPortrait = Screen.height > Screen.width;
        if (isPortrait)
        {
            int bw = Screen.width / 2, bh = Screen.height / 2;
            int tw = (int)(bw * currentZoom), th = (int)(bh * currentZoom);
            int cx = marginLeft + (Screen.width - marginLeft - marginRight) / 2;
            int cy = marginTop + (Screen.height - marginTop - marginBottom) / 2;
            marginLeft = cx - tw / 2;
            marginRight = Screen.width - marginLeft - tw;
            marginTop = Mathf.Max(220 + 140, cy - th / 2);
            marginBottom = Screen.height - marginTop - th;
        }
        else
        {
            int bw = (int)(Screen.width * 0.35f), bh = (int)(Screen.height * 0.4f);
            int tw = (int)(bw * currentZoom), th = (int)(bh * currentZoom);
            int cx = marginLeft + (Screen.width - marginLeft - marginRight) / 2;
            int cy = marginTop + (Screen.height - marginTop - marginBottom) / 2;
            marginLeft = cx - tw / 2;
            marginRight = Screen.width - marginLeft - tw;
            marginTop = Mathf.Max(220 + 140, cy - th / 2);
            marginBottom = Screen.height - marginTop - th;
        }
        UpdateMargins();
    }

    private void OnDestroy() { Close(); }
}