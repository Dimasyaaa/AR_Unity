using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Вешается на кнопку «Помощь» в каждой сцене.
// Открывает большую панель со списком голосовых команд текущей сцены.
[RequireComponent(typeof(Button))]
public class VoiceHelpController : MonoBehaviour
{
    public static VoiceHelpController Instance { get; private set; }

    private GameObject panelRoot;

    // В вертикальной ориентации всё в 2 раза крупнее — иначе не читается
    private static float M => Screen.height > Screen.width ? 2f : 1f;

    void Awake()
    {
        Instance = this;
        GetComponent<Button>().onClick.AddListener(Toggle);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static bool IsOpen => Instance != null && Instance.panelRoot != null;

    public static void CloseIfOpen()
    {
        if (IsOpen) Instance.Close();
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public void Open() => BuildPanel();

    public void Close()
    {
        if (panelRoot != null)
        {
            Destroy(panelRoot);
            panelRoot = null;
        }
    }

    // ==========================================
    // Панель помощи
    // ==========================================
    private void BuildPanel()
    {
        Close();

        panelRoot = new GameObject("VoiceHelpPanel");
        var canvas = panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9600;
        var scaler = panelRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        panelRoot.AddComponent<GraphicRaycaster>(); // пока панель открыта — клики только в неё

        // Затемнение фона
        var dim = CreateUI("Dim", panelRoot.transform);
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        Stretch(dim.GetComponent<RectTransform>());

        // Центральная панель
        var box = CreateUI("Box", panelRoot.transform);
        box.AddComponent<Image>().color = new Color(0.08f, 0.10f, 0.15f, 0.97f);
        var boxRt = box.GetComponent<RectTransform>();
        boxRt.anchorMin = new Vector2(0.5f, 0.5f);
        boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(1400, 920 * M);

        var vlg = box.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)(50 * M), (int)(50 * M), (int)(30 * M), (int)(30 * M));
        vlg.spacing = 20 * M;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Заголовок
        var titleGo = CreateUI("Title", box.transform);
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        SetFont(title);
        title.text = "ГОЛОСОВОЕ УПРАВЛЕНИЕ — КОМАНДЫ";
        title.fontSize = 46 * M;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.5f, 0.86f, 1f);
        titleGo.AddComponent<LayoutElement>().preferredHeight = 70 * M;

        // Список команд
        var contentGo = CreateUI("Commands", box.transform);
        var content = contentGo.AddComponent<TextMeshProUGUI>();
        SetFont(content);
        content.text = GetCommandsText();
        content.fontSize = 30 * M;
        content.alignment = TextAlignmentOptions.Left;
        content.color = Color.white;
        content.enableWordWrapping = true;
        var le = contentGo.AddComponent<LayoutElement>();
        le.flexibleHeight = 1;

        // Кнопка «Закрыть»
        var closeGo = CreateUI("CloseBtn", box.transform);
        closeGo.AddComponent<Image>().color = new Color(0.55f, 0.20f, 0.20f);
        var closeBtn = closeGo.AddComponent<Button>();
        var ctGo = CreateUI("Text", closeGo.transform);
        var ct = ctGo.AddComponent<TextMeshProUGUI>();
        SetFont(ct);
        ct.text = "Закрыть";
        ct.fontSize = 36 * M;
        ct.alignment = TextAlignmentOptions.Center;
        ct.color = Color.white;
        Stretch(ctGo.GetComponent<RectTransform>());
        closeBtn.onClick.AddListener(Close);
        closeGo.AddComponent<LayoutElement>().preferredHeight = 80 * M;
    }

    private string GetCommandsText()
    {
        string scene = SceneManager.GetActiveScene().name;

        if (scene == "LoginScene")
            return
                "<b><color=#7FDBFF>АВТОРИЗАЦИЯ</color></b>\n" +
                "   «фио» — перейти к полю имени\n" +
                "   «пароль» — перейти к полю пароля\n" +
                "   «отдел» — открыть список отделов\n" +
                "   «вверх» / «вниз» — листать список\n" +
                "   «выбрать» — закрыть список\n" +
                "   «войти» — войти в систему\n\n" +
                "<color=#FFD466>Команды понимаются и по-английски: name / password / department / up / down / login.</color>";

        if (scene == "StartScene")
            return
                "<b><color=#7FDBFF>ГЛАВНОЕ МЕНЮ</color></b>\n" +
                "   «сканировать» — запустить сканер QR\n" +
                "   «инструкция» — открыть инструкцию\n" +
                "   «синхронизация» — синхронизировать данные\n" +
                "   «вай-фай» / «usb» — настройки подключения\n" +
                "   «закрыть» — закрыть открытую панель\n" +
                "   «выход» — выход из приложения\n\n" +
                "<color=#FFD466>Команды понимаются и по-английски: scan / instruction / sync / wifi / usb / close / exit.</color>";

        return
            "<b><color=#7FDBFF>AR-СЦЕНА</color></b>\n" +
            "   «далее» — закрыть приветствие      «меню» — открыть меню объектов\n" +
            "   «настройки» — открыть настройки    «отмена» — закрыть меню\n" +
            "   «подсказки» / «плоскости» / «дебаг» — пункты настроек\n" +
            "   «удалить» — удалить объекты\n" +
            "   «калькулятор» — создать калькулятор\n" +
            "   «веб» → затем «объект» или «оверлей» — веб-страница\n" +
            "   «карточка» — карточка изображения\n" +
            "   «положить» / «отпустить» — закрепить / отпустить\n" +
            "   «закрыть» — закрыть всё      «назад» — в главное меню\n" +
            "   «помощь» — открыть/закрыть эту панель\n\n" +
            "<color=#FFD466>Команды понимаются и по-английски: calculator / web / card / close / back / help…</color>";
    }

    // ==========================================
    // Вспомогательные
    // ==========================================
    private GameObject CreateUI(string name, Transform parent)
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

    private void SetFont(TextMeshProUGUI tmp)
    {
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
    }
}