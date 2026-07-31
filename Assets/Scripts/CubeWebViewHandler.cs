using Gree.UnityWebView;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class CubeWebViewHandler : MonoBehaviour
{
    [Header("Settings")]
    public string url = "https://www.google.com";
    public float webViewDistance = 0.15f;

    [Header("References")]
    [Tooltip("Визуальная модель куба ")]
    public GameObject cubeVisual;

    [Tooltip("AR камера для правильного позиционирования WebView")]
    public Camera arCamera;

    private WebViewObject webViewObject;
    private GameObject webViewTrigger;
    private bool isWebViewActive = false;
    private XRGrabInteractable grabInteractable;

    // Элементы программного UI
    private GameObject uiCanvas;
    private GameObject loadingPanel;
    private Text loadingText;

    private int marginLeft;
    private int marginTop;
    private int marginRight;
    private int marginBottom;
    private int moveStep = 150;

    // Масштабирование окна (в процентах от базового размера)
    private float currentZoom = 1.0f;
    private float zoomStep = 0.4f; // Шаг масштабирования 30%
    private float minZoom = 0.5f;   // Минимальный масштаб 50%
    private float maxZoom = 5.0f;   // Максимальный масштаб 200%

    void Start()
    {
        Debug.Log("=== CubeWebViewHandler Start ===");
        Debug.Log("Текущее значение LastScannedQR: '" + GameDataManager.LastScannedQR + "'");

        if (!string.IsNullOrEmpty(GameDataManager.LastScannedQR))
        {
            string scannedData = GameDataManager.LastScannedQR.Trim();
            Debug.Log("=== Найден отсканированный QR-код ===");
            Debug.Log("Данные: '" + scannedData + "'");

            if (scannedData.StartsWith("http://") || scannedData.StartsWith("https://"))
            {
                url = scannedData;
                Debug.Log("Это прямая ссылка. Будет открыт URL: " + url);
            }
            else
            {
                url = "https://www.google.com";
                Debug.Log("Это не ссылка. Будет открыта главная страница Google.");
            }

            GameDataManager.LastScannedQR = null;
            Debug.Log("Данные очищены из GameDataManager");
        }
        else
        {
            Debug.Log("=== QR-код не найден или пуст ===");
            Debug.Log("Используется дефолтный URL: " + url);
        }

        Debug.Log("Итоговый URL для загрузки: " + url);

        grabInteractable = GetComponent<XRGrabInteractable>();
        if (grabInteractable == null)
            grabInteractable = GetComponentInChildren<XRGrabInteractable>();

        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnCubeSelected);
        }

        if (arCamera == null)
        {
            arCamera = Camera.main;
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
        currentZoom = 1.0f; // Сбрасываем масштаб при открытии

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
        if (arCamera == null)
        {
            arCamera = Camera.main;
            if (arCamera == null)
            {
                Debug.LogError("CRITICAL: No camera found! Cannot create WebView.");
                return;
            }
        }

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
                ld: (msg) =>
                {
                    Debug.Log($"WebView Loaded: {msg}");
                    if (loadingPanel != null)
                    {
                        loadingPanel.SetActive(false);
                        Debug.Log("Loading panel hidden.");
                    }
                },
                started: (msg) =>
                {
                    Debug.Log($"WebView Started: {msg}");
                    if (loadingText != null)
                    {
                        loadingText.text = "Загрузка содержимого...\nПожалуйста, подождите.";
                    }
                },
                enableWKWebView: true,
                zoom: false,
                transparent: true
            );

            CalculateInitialMargins();
            UpdateWebViewMargins();
            webViewObject.SetVisibility(true);

            Debug.Log($"Loading URL: " + url);
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

    // Расчет начальных отступов на основе ориентации экрана
    void CalculateInitialMargins()
    {
        bool isPortrait = Screen.height > Screen.width;

        if (isPortrait)
        {
            // Вертикальный режим: компактное окно
            int targetWidth = (int)(Screen.width / 2f * currentZoom);    // 4
            int targetHeight = (int)(Screen.height / 2.5f * currentZoom); //5

            // Центрируем окно
            marginLeft = (Screen.width - targetWidth) / 2;
            marginTop = 220; // Отступ сверху для кнопок
            marginRight = Screen.width - marginLeft - targetWidth;
            marginBottom = Screen.height - marginTop - targetHeight;
        }
        else
        {
            // Горизонтальный режим: окно справа
            marginLeft = (int)(Screen.width * 0.6f);
            marginTop = (int)(Screen.height * 0.15f * currentZoom);
            marginRight = (int)(Screen.width * 0.05f);

            int targetHeight = (int)(Screen.height * 0.4f * currentZoom);
            marginBottom = Screen.height - marginTop - targetHeight;
        }

        Debug.Log($"Margins calculated. Zoom: {currentZoom:F2}, L:{marginLeft}, T:{marginTop}, R:{marginRight}, B:{marginBottom}");
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

        // Панель загрузки
        loadingPanel = new GameObject("LoadingPanel");
        loadingPanel.transform.SetParent(uiCanvas.transform, false);

        RectTransform loadRect = loadingPanel.AddComponent<RectTransform>();
        loadRect.anchorMin = Vector2.zero;
        loadRect.anchorMax = Vector2.one;
        loadRect.sizeDelta = Vector2.zero;
        loadRect.anchoredPosition = Vector2.zero;

        Image loadBg = loadingPanel.AddComponent<Image>();
        loadBg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        GameObject textObj = new GameObject("LoadingText");
        textObj.transform.SetParent(loadingPanel.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        loadingText = textObj.AddComponent<Text>();
        loadingText.text = "Инициализация браузера...\nПожалуйста, подождите.";
        loadingText.fontSize = 48;
        loadingText.color = Color.white;
        loadingText.alignment = TextAnchor.MiddleCenter;
        loadingText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Панель кнопок управления 
        GameObject buttonPanel = new GameObject("ButtonPanel");
        buttonPanel.transform.SetParent(uiCanvas.transform, false);
        RectTransform panelRect = buttonPanel.AddComponent<RectTransform>();

        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.sizeDelta = new Vector2(0, 250);
        panelRect.anchoredPosition = new Vector2(0, -100);

        Image panelImage = buttonPanel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);

        // Размещаем 7 кнопок:
        float buttonSize = 180f; 
        float spacing = 100f;
        float totalWidth = buttonSize * 7 + spacing * 6;
        float startX = -totalWidth / 2f + buttonSize / 2f;

        CreateButton(buttonPanel, "←", startX, 0, buttonSize, OnMoveLeft, new Color(0.3f, 0.5f, 0.8f));
        CreateButton(buttonPanel, "→", startX + buttonSize + spacing, 0, buttonSize, OnMoveRight, new Color(0.3f, 0.5f, 0.8f));
        CreateButton(buttonPanel, "↑", startX + (buttonSize + spacing) * 2, 0, buttonSize, OnMoveUp, new Color(0.3f, 0.5f, 0.8f));
        CreateButton(buttonPanel, "↓", startX + (buttonSize + spacing) * 3, 0, buttonSize, OnMoveDown, new Color(0.3f, 0.5f, 0.8f));
        CreateButton(buttonPanel, "+", startX + (buttonSize + spacing) * 4, 0, buttonSize, OnZoomIn, new Color(0.2f, 0.7f, 0.3f));
        CreateButton(buttonPanel, "-", startX + (buttonSize + spacing) * 5, 0, buttonSize, OnZoomOut, new Color(0.8f, 0.5f, 0.2f));
        CreateButton(buttonPanel, "X", startX + (buttonSize + spacing) * 6, 0, buttonSize, CloseWebView, Color.red);
    }

    void CreateButton(GameObject parent, string text, float xPos, float yPos, float size, UnityEngine.Events.UnityAction onClick, Color bgColor)
    {
        GameObject btnObj = new GameObject($"Button_{text}");
        btnObj.transform.SetParent(parent.transform, false);

        RectTransform btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(size, size);
        btnRect.anchorMin = new Vector2(0.5f, 0.5f);
        btnRect.anchorMax = new Vector2(0.5f, 0.5f);
        btnRect.anchoredPosition = new Vector2(xPos, yPos);

        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(size, size);

        Text textComponent = textObj.AddComponent<Text>();
        textComponent.text = text;
        textComponent.fontSize = 100;
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

    // МЕТОДЫ ПЕРЕМЕЩЕНИЯ
    void OnMoveLeft()
    {
        if (marginLeft > 0) { marginLeft -= moveStep; marginRight += moveStep; UpdateWebViewMargins(); }
    }

    void OnMoveRight()
    {
        if (marginRight > 0) { marginLeft += moveStep; marginRight -= moveStep; UpdateWebViewMargins(); }
    }

    void OnMoveUp()
    {
        if (marginTop > 200) { marginTop -= moveStep; marginBottom += moveStep; UpdateWebViewMargins(); }
    }

    void OnMoveDown()
    {
        if (marginBottom > 0) { marginTop += moveStep; marginBottom -= moveStep; UpdateWebViewMargins(); }
    }

    // МЕТОДЫ МАСШТАБИРОВАНИЯ
    void OnZoomIn()
    {
        if (currentZoom < maxZoom)
        {
            currentZoom = Mathf.Min(maxZoom, currentZoom + zoomStep);
            ApplyZoom();
            Debug.Log($"Zoom In: {currentZoom:F2}");
        }
    }

    void OnZoomOut()
    {
        if (currentZoom > minZoom)
        {
            currentZoom = Mathf.Max(minZoom, currentZoom - zoomStep);
            ApplyZoom();
            Debug.Log($"Zoom Out: {currentZoom:F2}");
        }
    }

    // Применение текущего масштаба к окну
    void ApplyZoom()
    {
        bool isPortrait = Screen.height > Screen.width;

        if (isPortrait)
        {
            // В портретном режиме пересчитываем размеры на основе масштаба
            int baseWidth = Screen.width / 2;
            int baseHeight = Screen.height / 2;

            int targetWidth = (int)(baseWidth * currentZoom);
            int targetHeight = (int)(baseHeight * currentZoom);

            // Сохраняем центр окна
            int centerX = marginLeft + (Screen.width - marginLeft - marginRight) / 2;
            int centerY = marginTop + (Screen.height - marginTop - marginBottom) / 2;

            // Пересчитываем отступы для сохранения позиции центра
            marginLeft = centerX - targetWidth / 2;
            marginRight = Screen.width - marginLeft - targetWidth;
            marginTop = Mathf.Max(220, centerY - targetHeight / 2); // Не даем залезть на кнопки
            marginBottom = Screen.height - marginTop - targetHeight;
        }
        else
        {
            // В ландшафтном режиме
            int baseWidth = (int)(Screen.width * 0.35f);
            int baseHeight = (int)(Screen.height * 0.4f);

            int targetWidth = (int)(baseWidth * currentZoom);
            int targetHeight = (int)(baseHeight * currentZoom);

            int centerX = marginLeft + (Screen.width - marginLeft - marginRight) / 2;
            int centerY = marginTop + (Screen.height - marginTop - marginBottom) / 2;

            marginLeft = centerX - targetWidth / 2;
            marginRight = Screen.width - marginLeft - targetWidth;
            marginTop = Mathf.Max(220, centerY - targetHeight / 2);
            marginBottom = Screen.height - marginTop - targetHeight;
        }

        UpdateWebViewMargins();
    }

    void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnCubeSelected);
        }
    }
}