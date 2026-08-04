using ArInventory.Local;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Вешается на кнопку "Button (Web)".
// Показывает диалог выбора "Оверлей / AR-объект".
// - Оверлей: сразу открывает WebView с меню управления, без объекта.
// - AR-объект: ставит индекс карточки и включает тап-спавн (как калькулятор).
[RequireComponent(typeof(Button))]
public class WebButtonHook : MonoBehaviour
{
    [SerializeField] private float spawnDistance = 1.0f;

    // Индекс WebCardVariant в списке ObjectSpawner (у тебя это 0)
    [SerializeField] private int webCardIndex = 0;

    private UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ObjectSpawner objectSpawner;
    private Behaviour spawnTrigger; // ARInteractorSpawnTrigger (на том же объекте)
    private GameObject dialogCanvas;

    void Start()
    {
        var button = GetComponent<Button>();

        objectSpawner = FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ObjectSpawner>();
        if (objectSpawner != null)
        {
            foreach (var comp in objectSpawner.GetComponents<Component>())
            {
                if (comp.GetType().Name == "ARInteractorSpawnTrigger")
                    spawnTrigger = comp as Behaviour;
            }
        }

        // Отключаем persistent-слушатели из Inspector, чтобы кнопка сама не спавнила
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(ShowChoiceDialog);
    }

    private void ShowChoiceDialog()
    {
        if (dialogCanvas != null) { Destroy(dialogCanvas); dialogCanvas = null; }

        dialogCanvas = new GameObject("WebChoiceDialog");
        var canvas = dialogCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;
        var scaler = dialogCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        dialogCanvas.AddComponent<GraphicRaycaster>();

        var bg = new GameObject("BG");
        bg.transform.SetParent(dialogCanvas.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.6f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        bg.AddComponent<Button>().onClick.AddListener(CloseDialog);

        var panel = new GameObject("Panel");
        panel.transform.SetParent(dialogCanvas.transform, false);
        panel.AddComponent<Image>().color = new Color(0.15f, 0.17f, 0.22f, 0.98f);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(720, 520);

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(40, 40, 40, 40);
        vlg.spacing = 30;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(panel.transform, false);
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        title.text = "Как отобразить веб-страницу?";
        title.fontSize = 52;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) title.font = TMP_Settings.defaultFontAsset;
        titleGo.AddComponent<LayoutElement>().preferredHeight = 120;

        var ovBtnGo = new GameObject("OverlayBtn");
        ovBtnGo.transform.SetParent(panel.transform, false);
        ovBtnGo.AddComponent<Image>().color = new Color(0.10f, 0.55f, 0.90f);
        ovBtnGo.AddComponent<Button>().onClick.AddListener(OnOverlayChosen);
        MakeTextChild(ovBtnGo, "Как оверлей", 44);
        ovBtnGo.AddComponent<LayoutElement>().preferredHeight = 100;

        var arBtnGo = new GameObject("ARObjectBtn");
        arBtnGo.transform.SetParent(panel.transform, false);
        arBtnGo.AddComponent<Image>().color = new Color(0.20f, 0.70f, 0.40f);
        arBtnGo.AddComponent<Button>().onClick.AddListener(OnARObjectChosen);
        MakeTextChild(arBtnGo, "Как AR-объект", 44);
        arBtnGo.AddComponent<LayoutElement>().preferredHeight = 100;
    }

    private TextMeshProUGUI MakeTextChild(GameObject parent, string text, float size)
    {
        var tg = new GameObject("Text");
        tg.transform.SetParent(parent.transform, false);
        var tmp = tg.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        var rt = tg.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return tmp;
    }

    private void CloseDialog()
    {
        if (dialogCanvas != null) { Destroy(dialogCanvas); dialogCanvas = null; }
    }

    // Режим "Оверлей": сразу WebView с меню управления, без объекта
    private void OnOverlayChosen()
    {
        CloseDialog();

        // Не даём спавнеру создавать объект по тапу
        if (spawnTrigger != null)
            spawnTrigger.enabled = false;

        string qrCode = GameDataManager.LastScannedQR ?? "";
        WebOverlayController.GetOrCreate().Open(ResolveUrl(qrCode), GetTitleForQR(qrCode));

        Debug.Log("[WebHook] Режим Оверлей: открыт WebView с меню управления");
    }

    // Режим "AR-объект": карточка через ObjectSpawner (тап по поверхности), как калькулятор
    private void OnARObjectChosen()
    {
        CloseDialog();

        // Защита от дубликатов
        if (FindObjectOfType<WebCardController>() != null)
        {
            Debug.Log("[WebHook] Карточка уже на сцене — не создаём вторую");
            return;
        }

        if (objectSpawner == null)
        {
            Debug.LogError("[WebHook] ObjectSpawner не найден!");
            return;
        }

        // Выбираем префаб карточки (индекс 0) и разрешаем тап-спавн
        objectSpawner.SetSpawnObjectIndex(webCardIndex);
        if (spawnTrigger != null)
            spawnTrigger.enabled = true;

        Debug.Log("[WebHook] Режим AR-объект: тапни по поверхности, чтобы поставить карточку");
    }

    private string GetTitleForQR(string qrCode)
    {
        if (string.IsNullOrEmpty(qrCode)) return "Веб-страница";
        if (LocalDatabase.Instance == null || !LocalDatabase.Instance.IsReady)
            return "Веб-страница";

        var qr = LocalDatabase.Instance.Connection.Table<LocalQrCode>().ToList()
            .FirstOrDefault(q => q.code == qrCode);
        if (qr != null && !string.IsNullOrEmpty(qr.object_name))
            return qr.object_name;

        if (qrCode.StartsWith("http://") || qrCode.StartsWith("https://"))
        {
            try { return new System.Uri(qrCode).Host; } catch { return "Веб-страница"; }
        }
        return "Веб-страница";
    }

    private string ResolveUrl(string qrCode)
    {
        if (!string.IsNullOrEmpty(qrCode) &&
            (qrCode.StartsWith("http://") || qrCode.StartsWith("https://")))
            return qrCode;
        return "https://www.google.com";
    }
}