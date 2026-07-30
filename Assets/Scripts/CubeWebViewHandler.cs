using Gree.UnityWebView;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class CubeWebViewHandler : MonoBehaviour
{
    [Header("Settings")]
    public string url = "https://github.com";
    public float webViewDistance = 0.15f;

    [Header("References")]
    public GameObject cubeVisual;
    public Camera arCamera;

    private WebViewObject webViewObject;
    private GameObject webViewCanvas;
    private bool isWebViewActive = false;
    private XRGrabInteractable grabInteractable;

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

        // Скрываем визуальную модель куба, чтобы не мешала
        if (cubeVisual != null)
            cubeVisual.SetActive(false);
        else if (transform.childCount > 0)
            transform.GetChild(0).gameObject.SetActive(false);

        CreateWebViewCanvas();
    }

    void CloseWebView()
    {
        Debug.Log("Closing WebView...");
        isWebViewActive = false;

        // Возвращаем визуальную модель куба
        if (cubeVisual != null)
            cubeVisual.SetActive(true);
        else if (transform.childCount > 0)
            transform.GetChild(0).gameObject.SetActive(true);

        if (webViewCanvas != null)
        {
            Destroy(webViewCanvas);
            webViewCanvas = null;
        }

        webViewObject = null;
    }

    void CreateWebViewCanvas()
    {
        if (arCamera == null)
        {
            arCamera = Camera.main;
            if (arCamera == null)
            {
                Debug.LogError("No camera found!");
                return;
            }
        }

        // Создаем "фейковый" 3D-объект для коллайдера (чтобы система XR знала, что мы "держим" объект)
        webViewCanvas = new GameObject("WebViewCanvas");
        webViewCanvas.transform.SetParent(transform);
        webViewCanvas.transform.localPosition = Vector3.forward * webViewDistance;
        webViewCanvas.transform.localRotation = Quaternion.identity;
        webViewCanvas.transform.localScale = Vector3.one * 0.001f;

        // Добавляем коллайдер, чтобы XR Interactor мог "удерживать" этот объект
        var webViewCollider = webViewCanvas.AddComponent<BoxCollider>();
        webViewCollider.size = new Vector3(0.5f, 0.5f, 0.01f);
        webViewCollider.center = Vector3.zero;

#if UNITY_ANDROID || UNITY_IOS
        Debug.Log("=== BUILDING FOR MOBILE PLATFORM ===");

        try
        {
            webViewObject = webViewCanvas.AddComponent<WebViewObject>();

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

            // === ГЛАВНОЕ ИСПРАВЛЕНИЕ: Делаем аккуратное окно по центру экрана ===
            int marginX = Screen.width / 8;  // Отступ 12.5% слева и справа
            int marginY = Screen.height / 6; // Отступ ~16% сверху и снизу

            // SetMargins принимает: left, top, right, bottom
            webViewObject.SetMargins(marginX, marginY, marginX, marginY);
            webViewObject.SetVisibility(true);

            Debug.Log("Loading URL: " + url);
            webViewObject.LoadURL(url);

            Debug.Log("=== WebView init complete! ===");
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to initialize WebView: " + e.Message);
            Application.OpenURL(url);
        }
#else
        // Для Unity Editor просто открываем браузер
        Debug.LogWarning("Not on mobile - opening external browser");
        Application.OpenURL(url);
        
        // В редакторе сразу закрываем "вебвью", так как он не отобразится в 3D
        Invoke(nameof(CloseWebView), 0.5f);
#endif
    }

    void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnCubeSelected);
        }
    }
}