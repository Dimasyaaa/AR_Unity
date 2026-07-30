using Gree.UnityWebView;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Основной скрипт взаимодействия с AR-объектом. Отвечает за открытие веб-страницы 
// на основе отсканированного QR-кода и управление UI-оверлеем для перемещения окна.
public class CubeWebViewHandler : MonoBehaviour
{
    [Header("Settings")]
    // Дефолтный URL. Будет перезаписан в Start(), если найден результат сканирования QR
    public string url = "https://www.wikipedia.org";
    public float webViewDistance = 0.15f;

    [Header("References")]
    public GameObject cubeVisual;
    public Camera arCamera;

    private WebViewObject webViewObject;
    private GameObject webViewTrigger;
    private bool isWebViewActive = false;
    private XRGrabInteractable grabInteractable;

    // Элементы программного UI
    private GameObject uiCanvas;

    // Независимые отступы для точного контроля размера и позиции окна WebView
    private int marginLeft;
    private int marginTop;
    private int marginRight;
    private int marginBottom;

    // Шаг перемещения окна при нажатии на кнопки (150 пикселей для быстрой реакции)
    private int moveStep = 150;

    void Start()
    {
        // Читаем данные из QR-сканера, если они были сохранены
        if (!string.IsNullOrEmpty(GameDataManager.LastScannedQR))
        {
            string scannedData = GameDataManager.LastScannedQR.Trim();
            Debug.Log("Найден отсканированный QR: " + scannedData);

            // Проверяем, является ли отсканированный текст прямой ссылкой
            if (scannedData.StartsWith("http://") || scannedData.StartsWith("https://"))
            {
                url = scannedData;
                Debug.Log("Установлен URL из QR-кода: " + url);
            }
            else
            {
                // Если это текст или ID, формируем ссылку на поиск в Википедии, 
                // чтобы экран не был пустым и не выдавал ошибку браузера
                url = "https://ru.wikipedia.org/wiki/" + scannedData.Replace(" ", "_");
                Debug.Log("QR не является ссылкой. Выполняем поиск: " + url);
            }
        }
        else
        {
            Debug.Log("QR-код не найден, используется тестовый URL: " + url);
        }

        // Инициализация компонента взаимодействия XR
        grabInteractable = GetComponent<XRGrabInteractable>();
        if (grabInteractable == null)
            grabInteractable = GetComponentInChildren<XRGrabInteractable>();

        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnCubeSelected);
        }

        Debug.Log("CubeWebViewHandler initialized. Platform: " + Application.platform);
    }

    // Обработчик события взятия/выбора объекта в XR
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

        // Скрываем визуальную модель куба, чтобы она не мешала обзору
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

        // Возвращаем видимость визуальной модели куба
        if (cubeVisual != null)
            cubeVisual.SetActive(true);
        else if (transform.childCount > 0)
            transform.GetChild(0).gameObject.SetActive(true);

        // Очистка и уничтожение созданных объектов для освобождения памяти
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

        // Создаем невидимый объект-триггер для системы XR Interaction Toolkit
        webViewTrigger = new GameObject("WebViewTrigger");
        webViewTrigger.transform.SetParent(transform);
        webViewTrigger.transform.localPosition = Vector3.forward * webViewDistance;
        webViewTrigger.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.1f, 0.1f);

#if UNITY_ANDROID || UNITY_IOS
        try
        {
            webViewObject = webViewTrigger.AddComponent<WebViewObject>();

            // Инициализация нативного WebView с прозрачным фоном для AR-эффекта
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

            // Расчет размеров и позиции окна в зависимости от ориентации экрана
            bool isPortrait = Screen.height > Screen.width;

            if (isPortrait)
            {
                // Вертикальный режим: компактное окно (1/4 ширины, 1/5 высоты) в левом верхнем углу
                int targetWidth = Screen.width / 4;
                int targetHeight = Screen.height / 5;

                marginLeft = 20;
                marginTop = 220; // Отступ сверху, чтобы не перекрывать панель кнопок
                marginRight = Screen.width - marginLeft - targetWidth;
                marginBottom = Screen.height - marginTop - targetHeight;
            }
            else
            {
                // Горизонтальный режим: окно занимает правую часть экрана (~35% ширины, ~40% высоты)
                marginLeft = (int)(Screen.width * 0.6f);
                marginTop = (int)(Screen.height * 0.15f);
                marginRight = (int)(Screen.width * 0.05f);

                int targetHeight = (int)(Screen.height * 0.4f);
                marginBottom = Screen.height - marginTop - targetHeight;
            }

            UpdateWebViewMargins();
            webViewObject.SetVisibility(true);

            Debug.Log($"WebView initialized. Portrait: {isPortrait}");
            Debug.Log($"Loading URL: " + url);
            webViewObject.LoadURL(url);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to initialize WebView: " + e.Message);
            Application.OpenURL(url);
        }
#else
        // Фоллбэк для редактора Unity: открытие ссылки в системном браузере
        Debug.LogWarning("Editor mode - opening external browser");
        Application.OpenURL(url);
        Invoke(nameof(CloseWebView), 0.5f);
#endif
    }

    // Программное создание UI-панели управления поверх WebView
    void CreateUI()
    {
        uiCanvas = new GameObject("WebViewUI_Canvas");
        Canvas canvas = uiCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999; // Максимальный приоритет отрисовки

        CanvasScaler scaler = uiCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        uiCanvas.AddComponent<GraphicRaycaster>();

        // Создаем верхнюю панель для кнопок
        GameObject buttonPanel = new GameObject("ButtonPanel");
        buttonPanel.transform.SetParent(uiCanvas.transform, false);
        RectTransform panelRect = buttonPanel.AddComponent<RectTransform>();

        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.sizeDelta = new Vector2(0, 200);
        panelRect.anchoredPosition = new Vector2(0, -100);

        Image panelImage = buttonPanel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f); // Темный полупрозрачный фон

        // Параметры для размещения кнопок в ряд
        float buttonSize = 120f;
        float spacing = 160f;
        float startX = -320f;

        CreateButton(buttonPanel, "←", startX, 0, buttonSize, OnMoveLeft);
        CreateButton(buttonPanel, "→", startX + spacing, 0, buttonSize, OnMoveRight);
        CreateButton(buttonPanel, "↑", startX + spacing * 2, 0, buttonSize, OnMoveUp);
        CreateButton(buttonPanel, "↓", startX + spacing * 3, 0, buttonSize, OnMoveDown);
        CreateButton(buttonPanel, "X", startX + spacing * 4, 0, buttonSize, CloseWebView, Color.red);
    }

    // Вспомогательный метод для создания отдельной UI-кнопки
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

    // Применение текущих значений отступов к нативному объекту WebView
    void UpdateWebViewMargins()
    {
        if (webViewObject != null)
        {
            webViewObject.SetMargins(marginLeft, marginTop, marginRight, marginBottom);
        }
    }

    // Методы управления перемещением окна путем изменения парных отступов
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
        // Ограничение сверху (200), чтобы окно не зашло под панель кнопок
        if (marginTop > 200) { marginTop -= moveStep; marginBottom += moveStep; UpdateWebViewMargins(); }
    }

    void OnMoveDown()
    {
        if (marginBottom > 0) { marginTop += moveStep; marginBottom -= moveStep; UpdateWebViewMargins(); }
    }

    void OnDestroy()
    {
        // Очистка подписок на события для предотвращения утечек памяти и ошибок
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnCubeSelected);
        }
    }
}