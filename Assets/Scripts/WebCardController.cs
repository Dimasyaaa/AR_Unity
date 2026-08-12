using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.Generic;

// Контроллер WebCard с превью: скриншот страницы → OG-картинка → placeholder.
public class WebCardController : MonoBehaviour
{
    private static WebCardController current;

    // Простой кэш: URL → текстура (чтобы не грузить повторно)
    private static Dictionary<string, Texture2D> previewCache = new Dictionary<string, Texture2D>();

    private TextMeshProUGUI titleText;
    private TextMeshProUGUI urlText;
    private TextMeshProUGUI statusText;
    private Button openFullBtn;
    private Button refreshBtn;
    private RawImage previewImage;
    private Image previewBg;

    private string pageTitle = "QR-код";
    private string pageUrl;

    public bool IsPinned { get; set; }

    public void Initialize(bool pinned) { IsPinned = pinned; }

    private void Start()
    {
        if (current != null && current != this) { Destroy(gameObject); return; }
        current = this;

        var r = QRResolver.Resolve(GameDataManager.LastScannedQR);
        pageTitle = r.title;
        pageUrl = r.url;

        if (!r.foundInDb && string.IsNullOrEmpty(pageUrl))
            pageTitle = "QR не найден в базе";

        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { Debug.LogError("[WebCard] Canvas не найден"); return; }
        BuildUI(canvas.transform);

        if (!string.IsNullOrEmpty(pageUrl))
            StartCoroutine(LoadPreview(pageUrl));
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
    }

    // ==========================================
    // Загрузка превью: скриншот страницы → OG → placeholder
    // ==========================================
    private IEnumerator LoadPreview(string url)
    {
        // Проверяем кэш
        if (previewCache.TryGetValue(url, out Texture2D cached))
        {
            Debug.Log($"[WebCard] Превью из кэша для {url}");
            ShowPreview(cached);
            yield break;
        }

        if (statusText != null) statusText.text = "Загрузка превью...";

        Texture2D tex = null;

        // 1) Скриншот страницы через thum.io (бесплатно, без ключа)
        Debug.Log($"[WebCard] Пробую скриншот thum.io для {url}");
        yield return StartCoroutine(TryLoadImage(ThumbIoUrl(url), t => tex = t));

        // 2) Скриншот через WordPress mShots
        if (tex == null)
        {
            Debug.Log($"[WebCard] Пробую скриншот mshots для {url}");
            yield return StartCoroutine(TryLoadImage(MShotsUrl(url), t => tex = t));
        }

        // 3) OG-картинка сайта
        if (tex == null)
        {
            Debug.Log($"[WebCard] Пробую og:image для {url}");
            yield return StartCoroutine(TryLoadOGImage(url, t => tex = t));
        }

        if (tex != null)
        {
            previewCache[url] = tex;
            ShowPreview(tex);
        }
        else
        {
            if (statusText != null) statusText.text = "Превью недоступно";
            Debug.LogWarning($"[WebCard] Не удалось получить превью для {url}");
        }
    }

    // Высокое разрешение 1200px + crop/900 = только верх страницы (текст крупнее) + noanimate = стоп-кадр
    private static string ThumbIoUrl(string url)
    {
        return "https://image.thum.io/get/width/1600/crop/1000/noanimate/" + url;
    }

    private static string MShotsUrl(string url)
    {
        return "https://s.wordpress.com/mshots/v1/" + UnityWebRequest.EscapeURL(url) + "?w=1600";
    }

    private IEnumerator TryLoadImage(string imgUrl, System.Action<Texture2D> callback)
    {
        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(imgUrl))
        {
            req.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            req.timeout = 20;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                // readable=true + mipChain для качественной фильтрации при уменьшении
                var tex = DownloadHandlerTexture.GetContent(req);
                if (tex != null)
                {
                    tex.filterMode = FilterMode.Trilinear;
                    tex.anisoLevel = 4;
                }

                // защита от сервисных заглушек 1x1 и мелких ошибок
                if (tex != null && tex.width > 50 && tex.height > 50)
                {
                    Debug.Log($"[WebCard] Скриншот загружен: {tex.width}x{tex.height}");
                    callback(tex);
                    yield break;
                }
                else
                {
                    Debug.Log($"[WebCard] Получена заглушка {tex?.width}x{tex?.height}, пробуем следующий источник");
                }
            }
            else
            {
                Debug.LogWarning($"[WebCard] Ошибка загрузки {imgUrl}: {req.error}");
            }
            callback(null);
        }
    }

    private IEnumerator TryLoadOGImage(string url, System.Action<Texture2D> callback)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[WebCard] Ошибка загрузки {url}: {www.error}");
                callback(null);
                yield break;
            }

            string html = www.downloadHandler.text;
            string ogImage = ExtractOGImage(html);

            if (string.IsNullOrEmpty(ogImage))
            {
                Debug.Log($"[WebCard] og:image не найден в HTML {url}");
                callback(null);
                yield break;
            }

            if (ogImage.StartsWith("/"))
            {
                var uri = new System.Uri(url);
                ogImage = uri.Scheme + "://" + uri.Host + ogImage;
            }

            Debug.Log($"[WebCard] Загружаем og:image: {ogImage}");

            using (UnityWebRequest imgReq = UnityWebRequestTexture.GetTexture(ogImage))
            {
                imgReq.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                yield return imgReq.SendWebRequest();

                if (imgReq.result == UnityWebRequest.Result.Success)
                {
                    var tex = DownloadHandlerTexture.GetContent(imgReq);
                    if (tex != null)
                    {
                        tex.filterMode = FilterMode.Trilinear;
                        tex.anisoLevel = 4;
                    }
                    Debug.Log($"[WebCard] OG-картинка загружена: {tex.width}x{tex.height}");
                    callback(tex);
                }
                else
                {
                    Debug.LogWarning($"[WebCard] Ошибка загрузки картинки: {imgReq.error}");
                    callback(null);
                }
            }
        }
    }

    private string ExtractOGImage(string html)
    {
        var match = Regex.Match(html, @"<meta\s+[^>]*property=[""']og:image[""'][^>]*content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(html, @"<meta\s+[^>]*content=[""']([^""']+)[""'][^>]*property=[""']og:image[""']", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(html, @"<meta\s+[^>]*name=[""']twitter:image[""'][^>]*content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(html, @"<meta\s+[^>]*property=[""']og:image:url[""'][^>]*content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        return null;
    }

    private void ShowPreview(Texture2D texture)
    {
        if (previewImage == null || texture == null) return;

        previewImage.texture = texture;
        previewImage.color = Color.white;
        previewImage.raycastTarget = false;
        if (statusText != null) statusText.text = "Превью загружено";
        Debug.Log($"[WebCard] ShowPreview: текстура {texture.width}x{texture.height} показана");
    }

    private void ShowPlaceholder()
    {
        if (previewImage != null) previewImage.color = new Color(1, 1, 1, 0);
    }

    // ==========================================
    // UI карточки
    // ==========================================
    private void BuildUI(Transform canvasRoot)
    {
        var panel = CreateUIObject("Panel", canvasRoot);
        panel.AddComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        Stretch(panel.GetComponent<RectTransform>());

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(25, 25, 25, 25);
        vlg.spacing = 20;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Заголовок
        var titleGo = CreateUIObject("Title", panel.transform);
        titleText = titleGo.AddComponent<TextMeshProUGUI>();
        SetFont(titleText);
        titleText.text = pageTitle;
        titleText.fontSize = 48;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleGo.AddComponent<LayoutElement>().preferredHeight = 70;

        // === ПРЕВЬЮ САЙТА (внутри карточки) ===
        var previewGo = CreateUIObject("Preview", panel.transform);
        previewBg = previewGo.AddComponent<Image>();
        previewBg.color = new Color(0.18f, 0.20f, 0.25f, 0.9f);
        var previewLayout = previewGo.AddComponent<LayoutElement>();
        previewLayout.preferredHeight = 620;

        // Placeholder текст
        var placeholderGo = CreateUIObject("Placeholder", previewGo.transform);
        var placeholderText = placeholderGo.AddComponent<TextMeshProUGUI>();
        SetFont(placeholderText);
        placeholderText.text = "Превью\nнедоступно";
        placeholderText.fontSize = 48;
        placeholderText.alignment = TextAlignmentOptions.Center;
        placeholderText.color = new Color(0.5f, 0.5f, 0.5f);
        var phRt = placeholderGo.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
        phRt.offsetMin = Vector2.zero; phRt.offsetMax = Vector2.zero;

        // RawImage для скриншота (поверх фона, изначально прозрачная)
        var rawGo = CreateUIObject("RawImage", previewGo.transform);
        previewImage = rawGo.AddComponent<RawImage>();
        previewImage.color = new Color(1, 1, 1, 0);
        var rawRt = rawGo.GetComponent<RectTransform>();
        rawRt.anchorMin = Vector2.zero; rawRt.anchorMax = Vector2.one;
        rawRt.offsetMin = new Vector2(4, 4);
        rawRt.offsetMax = new Vector2(-4, -4);

        // Статус загрузки
        var statusGo = CreateUIObject("Status", previewGo.transform);
        statusText = statusGo.AddComponent<TextMeshProUGUI>();
        SetFont(statusText);
        statusText.text = string.IsNullOrEmpty(pageUrl)
            ? "Для этого QR нет ссылки"
            : "Загрузка превью...";
        statusText.fontSize = 20;
        statusText.alignment = TextAlignmentOptions.Bottom;
        statusText.color = new Color(1f, 1f, 1f, 0.9f);
        var stRt = statusGo.GetComponent<RectTransform>();
        stRt.anchorMin = new Vector2(0, 0); stRt.anchorMax = new Vector2(1, 0);
        stRt.sizeDelta = new Vector2(0, 40);
        stRt.anchoredPosition = Vector2.zero;

        // === Кнопки ===
        var buttonsGo = CreateUIObject("Buttons", panel.transform);
        var hl = buttonsGo.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 15;
        hl.childControlWidth = true;
        hl.childControlHeight = true;
        buttonsGo.AddComponent<LayoutElement>().preferredHeight = 80;

        // "Обновить превью"
        var refGo = CreateUIObject("RefreshBtn", buttonsGo.transform);
        refGo.AddComponent<Image>().color = new Color(0.35f, 0.40f, 0.50f);
        refreshBtn = refGo.AddComponent<Button>();
        CreateTextChild(refGo.transform, "Обновить", 32);
        refreshBtn.onClick.AddListener(() =>
        {
            if (!string.IsNullOrEmpty(pageUrl))
            {
                previewCache.Remove(pageUrl);
                StartCoroutine(LoadPreview(pageUrl));
            }
        });

        // "Открыть полноэкранный"
        var openFullGo = CreateUIObject("OpenBtn", buttonsGo.transform);
        openFullGo.AddComponent<Image>().color = new Color(0.10f, 0.55f, 0.90f);
        openFullBtn = openFullGo.AddComponent<Button>();
        CreateTextChild(openFullGo.transform, "Открыть", 32);
        openFullBtn.onClick.AddListener(OpenFullPage);

        // "Закрыть"
        var closeGo = CreateUIObject("CloseBtn", buttonsGo.transform);
        closeGo.AddComponent<Image>().color = new Color(0.55f, 0.20f, 0.20f);
        var closeBtn = closeGo.AddComponent<Button>();
        CreateTextChild(closeGo.transform, "Закрыть", 32);
        closeBtn.onClick.AddListener(() => Destroy(gameObject));

        if (string.IsNullOrEmpty(pageUrl))
        {
            openFullBtn.interactable = false;
            refreshBtn.interactable = false;
            openFullGo.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);
            refGo.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);
        }

        ShowPlaceholder();
    }

    private void OpenFullPage()
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