using Gree.UnityWebView;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class CubeWebViewHandler : MonoBehaviour
{
    [Header("Settings")]
    public string url = "https://www.wikipedia.org";
    public float webViewDistance = 0.15f;

    [Header("References")]
    public GameObject cubeVisual;
    public Camera arCamera;

    private WebViewObject webViewObject;
    private GameObject webViewTrigger;
    private bool isWebViewActive = false;
    private XRGrabInteractable grabInteractable;

    // UI элементы
    private GameObject uiCanvas;
    private int marginLeft;
    private int marginTop;
    private int marginRight;
    private int marginBottom;
    private int moveStep = 150;     // СКОРОСТЬ ПЕРЕМЕЩЕНИЯ

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        if (grabInteractable == null)
            grabInteractable = GetComponentInChildren<XRGrabInteractable>();

        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnCubeSelected);
        }

        Debug.Log("CubeWebViewHandler initialized. Platform: " + Application.platform);
    }

    void OnCubeSelected(SelectEnterEventArgs args)
    {
        Debug.Log("Cube selected! Active: " + isWebViewActive);
        if (isWebViewActive)
            CloseWebView();
        else
            OpenWebView();
    }

    void OpenWebView()
    {
        Debug.Log("Opening WebView...");
        isWebViewActive = true;

        if (cubeVisual != null)
            cubeVisual.SetActive(false);
        else if (transform.childCount > 0)
            transform.GetChild(0).gameObject.SetActive(false);

        CreateWebViewCanvas();
        CreateUI();
    }

    void CloseWebView()
    {
        Debug.Log("Closing WebView...");
        isWebViewActive = false;

        if (cubeVisual != null)
            cubeVisual.SetActive(true);
        else if (transform.childCount > 0)
            transform.GetChild(0).gameObject.SetActive(true);

        if (webViewTrigger != null)
        {
            Destroy(webViewTrigger);
            webViewTrigger = null;
        }

        if (webViewObject != null)
        {
            Destroy(webViewObject);
            webViewObject = null;
        }

        if (uiCanvas != null)
        {
            Destroy(uiCanvas);
            uiCanvas = null;
        }
    }

    void CreateWebViewCanvas()
    {
        if (arCamera == null) arCamera = Camera.main;

        webViewTrigger = new GameObject("WebViewTrigger");
        webViewTrigger.transform.SetParent(transform);
        webViewTrigger.transform.localPosition = Vector3.forward * webViewDistance;
        webViewTrigger.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.1f, 0.1f);

#if UNITY_ANDROID || UNITY_IOS
        try
        {
            webViewObject = webViewTrigger.AddComponent<WebViewObject>();

            webViewObject.Init(
                cb: (msg) => Debug.Log($"WebView JS: {msg}"),
                err: (msg) => Debug.LogError($"WebView Error: {msg}"),
                httpErr: (msg) => Debug.LogError($"WebView HTTP Error: {msg}"),
                ld: (msg) => Debug.Log($"WebView Loaded: {msg}"),
                started: (msg) => Debug.Log($"WebView Started: {msg}"),
                enableWKWebView: true,
                zoom: false,
                transparent: true
            );

            bool isPortrait = Screen.height > Screen.width;

            if (isPortrait)
            {
                // ВЕРТИКАЛЬНЫЙ РЕЖИМ: ОЧЕНЬ КОМПАКТНЫЙ (1/4 ширины, 1/5 высоты)
                int targetWidth = Screen.width / 4;
                int targetHeight = Screen.height / 5;

                marginLeft = 20; // Отступ от левого края
                marginTop = 220; // Отступ сверху (под панелью кнопок)
                marginRight = Screen.width - marginLeft - targetWidth;
                marginBottom = Screen.height - marginTop - targetHeight;
            }
            else
            {
                // ГОРИЗОНТАЛЬНЫЙ РЕЖИМ: КОМПАКТНЫЙ БЛОК СПРАВА (~35% ширины, ~40% высоты)
                marginLeft = (int)(Screen.width * 0.6f);   // Начинается с 60% ширины экрана
                marginTop = (int)(Screen.height * 0.15f);  // Отступ сверху 15%
                marginRight = (int)(Screen.width * 0.05f); // Отступ справа 5%

                // Высота будет ~40% экрана
                int targetHeight = (int)(Screen.height * 0.4f);
                marginBottom = Screen.height - marginTop - targetHeight;
            }

            UpdateWebViewMargins();
            webViewObject.SetVisibility(true);

            Debug.Log($"WebView initialized. Portrait: {isPortrait}");
            Debug.Log($"Margins: L={marginLeft}, T={marginTop}, R={marginRight}, B={marginBottom}");
            Debug.Log("Loading URL: " + url);
            webViewObject.LoadURL(url);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to initialize WebView: " + e.Message);
            Application.OpenURL(url);
        }
#else
        Debug.LogWarning("Editor mode - opening external browser");
        Application.OpenURL(url);
        Invoke(nameof(CloseWebView), 0.5f);
#endif
    }

    void CreateUI()
    {
        uiCanvas = new GameObject("WebViewUI_Canvas");
        Canvas canvas = uiCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = uiCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        uiCanvas.AddComponent<GraphicRaycaster>();

        // Панель кнопок всегда сверху
        GameObject buttonPanel = new GameObject("ButtonPanel");
        buttonPanel.transform.SetParent(uiCanvas.transform, false);
        RectTransform panelRect = buttonPanel.AddComponent<RectTransform>();

        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.sizeDelta = new Vector2(0, 200);
        panelRect.anchoredPosition = new Vector2(0, -100);

        Image panelImage = buttonPanel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);

        float buttonSize = 120f;
        float spacing = 160f;
        float startX = -320f;

        CreateButton(buttonPanel, "←", startX, 0, buttonSize, OnMoveLeft);
        CreateButton(buttonPanel, "→", startX + spacing, 0, buttonSize, OnMoveRight);
        CreateButton(buttonPanel, "↑", startX + spacing * 2, 0, buttonSize, OnMoveUp);
        CreateButton(buttonPanel, "↓", startX + spacing * 3, 0, buttonSize, OnMoveDown);
        CreateButton(buttonPanel, "X", startX + spacing * 4, 0, buttonSize, CloseWebView, Color.red);
    }

    void CreateButton(GameObject parent, string text, float xPos, float yPos, float size, UnityEngine.Events.UnityAction onClick, Color? bgColor = null)
    {
        GameObject btnObj = new GameObject($"Button_{text}");
        btnObj.transform.SetParent(parent.transform, false);

        RectTransform btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(size, size);
        btnRect.anchorMin = new Vector2(0.5f, 0.5f);
        btnRect.anchorMax = new Vector2(0.5f, 0.5f);
        btnRect.anchoredPosition = new Vector2(xPos, yPos);

        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = bgColor ?? new Color(0.4f, 0.4f, 0.4f, 0.9f);

        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(size, size);

        Text textComponent = textObj.AddComponent<Text>();
        textComponent.text = text;
        textComponent.fontSize = 50;
        textComponent.color = Color.white;
        textComponent.alignment = TextAnchor.MiddleCenter;
        textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void UpdateWebViewMargins()
    {
        if (webViewObject != null)
        {
            webViewObject.SetMargins(marginLeft, marginTop, marginRight, marginBottom);
        }
    }

    void OnMoveLeft()
    {
        if (marginLeft > 0)
        {
            marginLeft = Mathf.Max(0, marginLeft - moveStep);
            marginRight += moveStep;
            UpdateWebViewMargins();
        }
    }

    void OnMoveRight()
    {
        if (marginRight > 0)
        {
            marginLeft += moveStep;
            marginRight = Mathf.Max(0, marginRight - moveStep);
            UpdateWebViewMargins();
        }
    }

    void OnMoveUp()
    {
        if (marginTop > 200)
        {
            marginTop = Mathf.Max(200, marginTop - moveStep);
            marginBottom += moveStep;
            UpdateWebViewMargins();
        }
    }

    void OnMoveDown()
    {
        if (marginBottom > 0)
        {
            marginTop += moveStep;
            marginBottom = Mathf.Max(0, marginBottom - moveStep);
            UpdateWebViewMargins();
        }
    }

    void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnCubeSelected);
        }
    }
}

//using UnityEngine;
//using UnityEngine.UI;
//using UnityEngine.XR.Interaction.Toolkit;
//using Gree.UnityWebView;
//using UnityEngine.XR.Interaction.Toolkit.Interactables;

//public class CubeWebViewHandler : MonoBehaviour
//{
//    [Header("Settings")]
//    public string url = "https://www.wikipedia.org";
//    public float webViewDistance = 0.15f;

//    [Header("References")]
//    public GameObject cubeVisual;
//    public Camera arCamera;

//    private WebViewObject webViewObject;
//    private GameObject webViewTrigger;
//    private bool isWebViewActive = false;
//    private XRGrabInteractable grabInteractable;


//    // UI элементы
//    private GameObject uiCanvas;

//    // Позиция и размер WebView (в пикселях)
//    private int webViewTop;    // Отступ сверху (где начинаются кнопки)
//    private int webViewBottom; // Отступ снизу
//    private int webViewLeft;   // Отступ слева
//    private int webViewRight;  // Отступ справа

//    private int moveStep = 30; // Шаг перемещения

//    void Start()
//    {
//        grabInteractable = GetComponent<XRGrabInteractable>();
//        if (grabInteractable == null)
//            grabInteractable = GetComponentInChildren<XRGrabInteractable>();

//        if (grabInteractable != null)
//        {
//            grabInteractable.selectEntered.AddListener(OnCubeSelected);
//        }

//        Debug.Log("CubeWebViewHandler initialized. Platform: " + Application.platform);
//    }

//    void OnCubeSelected(SelectEnterEventArgs args)
//    {
//        Debug.Log("Cube selected! Active: " + isWebViewActive);
//        if (isWebViewActive)
//            CloseWebView();
//        else
//            OpenWebView();
//    }

//    void OpenWebView()
//    {
//        Debug.Log("Opening WebView...");
//        isWebViewActive = true;

//        if (cubeVisual != null)
//            cubeVisual.SetActive(false);
//        else if (transform.childCount > 0)
//            transform.GetChild(0).gameObject.SetActive(false);

//        CreateWebViewCanvas();
//        CreateUI();
//    }

//    void CloseWebView()
//    {
//        Debug.Log("Closing WebView...");
//        isWebViewActive = false;

//        if (cubeVisual != null)
//            cubeVisual.SetActive(true);
//        else if (transform.childCount > 0)
//            transform.GetChild(0).gameObject.SetActive(true);

//        if (webViewTrigger != null)
//        {
//            Destroy(webViewTrigger);
//            webViewTrigger = null;
//        }

//        if (webViewObject != null)
//        {
//            Destroy(webViewObject);
//            webViewObject = null;
//        }

//        if (uiCanvas != null)
//        {
//            Destroy(uiCanvas);
//            uiCanvas = null;
//        }
//    }

//    void CreateWebViewCanvas()
//    {
//        if (arCamera == null) arCamera = Camera.main;

//        webViewTrigger = new GameObject("WebViewTrigger");
//        webViewTrigger.transform.SetParent(transform);
//        webViewTrigger.transform.localPosition = Vector3.forward * webViewDistance;
//        webViewTrigger.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.1f, 0.1f);

//#if UNITY_ANDROID || UNITY_IOS
//        try
//        {
//            webViewObject = webViewTrigger.AddComponent<WebViewObject>();

//            webViewObject.Init(
//                cb: (msg) => Debug.Log($"WebView JS: {msg}"),
//                err: (msg) => Debug.LogError($"WebView Error: {msg}"),
//                httpErr: (msg) => Debug.LogError($"WebView HTTP Error: {msg}"),
//                ld: (msg) => Debug.Log($"WebView Loaded: {msg}"),
//                started: (msg) => Debug.Log($"WebView Started: {msg}"),
//                enableWKWebView: true,
//                zoom: false,
//                transparent: true
//            );

//            // === КЛЮЧЕВОЕ: WebView занимает только НИЖНЮЮ часть экрана ===
//            // Верхние 25% экрана оставляем для кнопок Unity UI
//            webViewTop = (int)(Screen.height * 0.25f);  // 25% сверху — зона кнопок
//            webViewBottom = 20;                          // 20px снизу
//            webViewLeft = 10;                            // 10px слева
//            webViewRight = 10;                           // 10px справа

//            webViewObject.SetMargins(webViewLeft, webViewTop, webViewRight, webViewBottom);
//            webViewObject.SetVisibility(true);

//            Debug.Log($"WebView margins: L={webViewLeft}, T={webViewTop}, R={webViewRight}, B={webViewBottom}");
//            Debug.Log($"Screen size: {Screen.width}x{Screen.height}");
//            Debug.Log("Loading URL: " + url);
//            webViewObject.LoadURL(url);
//        }
//        catch (System.Exception e)
//        {
//            Debug.LogError("Failed to initialize WebView: " + e.Message);
//            Application.OpenURL(url);
//        }
//#else
//        Debug.LogWarning("Editor mode - opening external browser");
//        Application.OpenURL(url);
//        Invoke(nameof(CloseWebView), 0.5f);
//#endif
//    }

//    void CreateUI()
//    {
//        // Создаем Canvas в режиме ScreenSpaceOverlay — рендерится поверх Unity
//        uiCanvas = new GameObject("WebViewUI_Canvas");
//        Canvas canvas = uiCanvas.AddComponent<Canvas>();
//        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
//        canvas.sortingOrder = 100;

//        CanvasScaler scaler = uiCanvas.AddComponent<CanvasScaler>();
//        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
//        scaler.referenceResolution = new Vector2(1080, 1920); // Вертикальный телефон
//        scaler.matchWidthOrHeight = 0.5f;

//        uiCanvas.AddComponent<GraphicRaycaster>();

//        // === ПАНЕЛЬ КНОПОК В ВЕРХНЕЙ ЧАСТИ ЭКРАНА ===
//        // Эта зона НЕ перекрывается WebView, поэтому кнопки будут видны и кликабельны
//        GameObject buttonPanel = new GameObject("ButtonPanel");
//        buttonPanel.transform.SetParent(uiCanvas.transform, false);
//        RectTransform panelRect = buttonPanel.AddComponent<RectTransform>();

//        // Панель занимает верхние 25% экрана
//        panelRect.anchorMin = new Vector2(0, 0.75f); // От 75% высоты
//        panelRect.anchorMax = new Vector2(1, 1);     // До верха
//        panelRect.sizeDelta = Vector2.zero;

//        Image panelImage = buttonPanel.AddComponent<Image>();
//        panelImage.color = new Color(0, 0, 0, 0.8f); // Темный фон

//        // Заголовок
//        GameObject titleObj = new GameObject("Title");
//        titleObj.transform.SetParent(buttonPanel.transform, false);
//        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
//        titleRect.anchorMin = new Vector2(0, 0.7f);
//        titleRect.anchorMax = new Vector2(1, 1);
//        titleRect.sizeDelta = Vector2.zero;

//        Text titleText = titleObj.AddComponent<Text>();
//        titleText.text = "AR Web Viewer";
//        titleText.fontSize = 24;
//        titleText.color = Color.white;
//        titleText.alignment = TextAnchor.MiddleCenter;
//        titleText.font = GetDefaultFont();

//        // Контейнер для кнопок
//        GameObject buttonsContainer = new GameObject("ButtonsContainer");
//        buttonsContainer.transform.SetParent(buttonPanel.transform, false);
//        RectTransform containerRect = buttonsContainer.AddComponent<RectTransform>();
//        containerRect.anchorMin = new Vector2(0, 0);
//        containerRect.anchorMax = new Vector2(1, 0.7f);
//        containerRect.sizeDelta = Vector2.zero;

//        // Создаем кнопки в ряд
//        float buttonWidth = 140f;
//        float spacing = 20f;
//        float totalWidth = buttonWidth * 5 + spacing * 4;
//        float startX = -totalWidth / 2f + buttonWidth / 2f;

//        CreateButton(buttonsContainer, "←", startX, OnMoveLeft, new Color(0.3f, 0.5f, 0.8f));
//        CreateButton(buttonsContainer, "→", startX + buttonWidth + spacing, OnMoveRight, new Color(0.3f, 0.5f, 0.8f));
//        CreateButton(buttonsContainer, "↑", startX + (buttonWidth + spacing) * 2, OnMoveUp, new Color(0.3f, 0.5f, 0.8f));
//        CreateButton(buttonsContainer, "↓", startX + (buttonWidth + spacing) * 3, OnMoveDown, new Color(0.3f, 0.5f, 0.8f));
//        CreateButton(buttonsContainer, "✕", startX + (buttonWidth + spacing) * 4, CloseWebView, new Color(0.8f, 0.2f, 0.2f));
//    }

//    Font GetDefaultFont()
//    {
//        // Пытаемся получить стандартный шрифт
//        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
//        if (f == null)
//        {
//            Debug.LogWarning("LegacyRuntime.ttf not found, using Arial fallback");
//            // Fallback — создаем через стандартный путь
//            return Font.CreateDynamicFontFromOSFont("Arial", 16);
//        }
//        return f;
//    }

//    void CreateButton(GameObject parent, string text, float xPos, UnityEngine.Events.UnityAction onClick, Color bgColor)
//    {
//        GameObject btnObj = new GameObject($"Button_{text}");
//        btnObj.transform.SetParent(parent.transform, false);
//        RectTransform btnRect = btnObj.AddComponent<RectTransform>();
//        btnRect.sizeDelta = new Vector2(140, 90);
//        btnRect.anchorMin = new Vector2(0.5f, 0.5f);
//        btnRect.anchorMax = new Vector2(0.5f, 0.5f);
//        btnRect.anchoredPosition = new Vector2(xPos, 0);

//        Image btnImage = btnObj.AddComponent<Image>();
//        btnImage.color = bgColor;

//        Button btn = btnObj.AddComponent<Button>();
//        btn.onClick.AddListener(onClick);

//        // Цветовая схема кнопки
//        ColorBlock colors = btn.colors;
//        colors.highlightedColor = bgColor * 1.2f;
//        colors.pressedColor = bgColor * 0.8f;
//        btn.colors = colors;

//        // Текст
//        GameObject textObj = new GameObject("Text");
//        textObj.transform.SetParent(btnObj.transform, false);
//        RectTransform textRect = textObj.AddComponent<RectTransform>();
//        textRect.sizeDelta = new Vector2(140, 90);

//        Text textComponent = textObj.AddComponent<Text>();
//        textComponent.text = text;
//        textComponent.fontSize = 36;
//        textComponent.color = Color.white;
//        textComponent.alignment = TextAnchor.MiddleCenter;
//        textComponent.font = GetDefaultFont();

//        Debug.Log($"Button '{text}' created at x={xPos}");
//    }

//    void UpdateWebViewMargins()
//    {
//        if (webViewObject != null)
//        {
//            webViewObject.SetMargins(webViewLeft, webViewTop, webViewRight, webViewBottom);
//        }
//    }

//    void OnMoveLeft()
//    {
//        webViewLeft = Mathf.Max(0, webViewLeft - moveStep);
//        webViewRight = Mathf.Max(0, webViewRight + moveStep);
//        UpdateWebViewMargins();
//        Debug.Log($"Move Left: L={webViewLeft}, R={webViewRight}");
//    }

//    void OnMoveRight()
//    {
//        webViewRight = Mathf.Max(0, webViewRight - moveStep);
//        webViewLeft = Mathf.Max(0, webViewLeft + moveStep);
//        UpdateWebViewMargins();
//        Debug.Log($"Move Right: L={webViewLeft}, R={webViewRight}");
//    }

//    void OnMoveUp()
//    {
//        // Двигаем вверх = уменьшаем верхний отступ (но не меньше высоты панели кнопок)
//        int minTop = (int)(Screen.height * 0.25f);
//        webViewTop = Mathf.Max(minTop, webViewTop - moveStep);
//        webViewBottom = Mathf.Max(0, webViewBottom + moveStep);
//        UpdateWebViewMargins();
//        Debug.Log($"Move Up: T={webViewTop}, B={webViewBottom}");
//    }

//    void OnMoveDown()
//    {
//        webViewBottom = Mathf.Max(0, webViewBottom - moveStep);
//        webViewTop = Mathf.Min(Screen.height - 50, webViewTop + moveStep);
//        UpdateWebViewMargins();
//        Debug.Log($"Move Down: T={webViewTop}, B={webViewBottom}");
//    }

//    void OnDestroy()
//    {
//        if (grabInteractable != null)
//        {
//            grabInteractable.selectEntered.RemoveListener(OnCubeSelected);
//        }
//    }
//}