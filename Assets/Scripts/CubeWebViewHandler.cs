using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using Gree.UnityWebView;

public class CubeWebViewHandler : MonoBehaviour
{
    [Header("Settings")]
    public string url = "https://github.com";
    public float webViewDistance = 1.5f;
    public int webViewWidth = 512;   
    public int webViewHeight = 384;  

    [Header("References")]
    public GameObject cubeVisual;
    public Camera arCamera;

    private WebViewObject webViewObject;
    private GameObject webViewCanvas;
    private bool isWebViewActive = false;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;

    void Start()
    {
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grabInteractable == null)
            grabInteractable = GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

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
    }

    void CloseWebView()
    {
        Debug.Log("Closing WebView...");
        isWebViewActive = false;

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
        webViewCanvas = new GameObject("WebViewCanvas");
        Canvas canvas = webViewCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = arCamera;

        webViewCanvas.transform.position = transform.position + transform.forward * webViewDistance;
        webViewCanvas.transform.rotation = arCamera.transform.rotation;
        webViewCanvas.transform.localScale = Vector3.one * 0.001f;

        webViewCanvas.AddComponent<CanvasScaler>();
        webViewCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        RectTransform rectTransform = webViewCanvas.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(webViewWidth, webViewHeight);

        Debug.Log("Current platform: " + Application.platform);
        Debug.Log("Is mobile platform: " + (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer));

#if UNITY_ANDROID || UNITY_IOS
    Debug.Log("Building for MOBILE platform - initializing native WebView");
    
    try 
    {
        webViewObject = webViewCanvas.AddComponent<WebViewObject>();
        
        Debug.Log("WebViewObject created, initializing...");
        
        webViewObject.Init(
            cb: (msg) => Debug.Log($"WebView JS Callback: {msg}"),
            err: (msg) => Debug.LogError($"WebView Error: {msg}"),
            httpErr: (msg) => Debug.LogError($"WebView HTTP Error: {msg}"),
            ld: (msg) => 
            {
                Debug.Log($"WebView Loaded: {msg}");
                Debug.Log("WebView progress: " + webViewObject.Progress() + "%");
            },
            started: (msg) => Debug.Log($"WebView Started: {msg}"),
            enableWKWebView: true,
            zoom: false
        );

        webViewObject.SetMargins(0, 0, 0, 0);
        webViewObject.SetVisibility(true);
        
        // bitmapRefreshCycle НЕ доступен на Android/iOS - удалили эту строку
        
        Debug.Log("Loading URL: " + url);
        webViewObject.LoadURL(url);
        
        Debug.Log("WebView initialization complete!");
    }
    catch (System.Exception e)
    {
        Debug.LogError("Failed to initialize WebView: " + e.Message);
        Debug.LogError("Stack trace: " + e.StackTrace);
        Application.OpenURL(url);
    }
    
#else
        Debug.LogWarning("NOT on mobile platform! Opening in external browser.");
        Application.OpenURL(url);
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