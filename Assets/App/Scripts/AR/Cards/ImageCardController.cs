using ArInventory.Local;
using ArInventory.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Third AR card: product sheets for regular QR codes and cabinet photo pages for inventory.</summary>
public class ImageCardController : MonoBehaviour
{
    [Header("Режим карточки")]
    [SerializeField] private bool cabinetViewOnly;
    public bool CabinetViewOnly => cabinetViewOnly;
    [Header("Область изображения")]
    [SerializeField] private float maxImageWidth = 500f;
    [SerializeField] private float maxImageHeight = 640f;
    [Header("Листы информации о продукции")]
    [SerializeField] private Texture2D sheet1;
    [SerializeField] private Texture2D sheet2;

    private readonly Texture2D[] cabinetPhotos = new Texture2D[2];
    private readonly bool[] availablePages = new bool[2];
    private RawImage rawImage;
    private RectTransform imageRect;
    private TextMeshProUGUI titleText;
    private Button pageOneButton;
    private Button pageTwoButton;
    private Button closeButton;
    private Texture2D[] sheets;
    private LocalCabinetProfile localCabinet;
    private string qrCode;
    private string cabinetTitle;
    private int selectedPage;
    private bool cabinetMode;

    private void Start()
    {
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { Debug.LogError("[ImageCard] Canvas не найден."); return; }
        var panel = canvas.transform.Find("Panel");
        if (panel == null) { Debug.LogError("[ImageCard] Панель не найдена."); return; }
        BindUI(panel);

        qrCode = GameDataManager.LastScannedQR;
        if (cabinetViewOnly)
        {
            localCabinet = LocalDatabase.Instance != null
                ? LocalDatabase.Instance.FindCabinetByQr(qrCode)
                : null;
            if (localCabinet != null)
            {
                cabinetMode = true;
                cabinetTitle = localCabinet.title;
                AddLocalFallbackPhoto(localCabinet.reference_photo);
                ShowCabinetPage(0);
            }
            else
            {
                cabinetMode = true;
                cabinetTitle = "Шкаф";
                if (titleText != null) titleText.text = "Загружаем данные шкафа…";
            }
        }
        else
        {
            sheets = new[]
            {
                sheet1 != null ? sheet1 : Resources.Load<Texture2D>("Images/product_sheet_1"),
                sheet2 != null ? sheet2 : Resources.Load<Texture2D>("Images/product_sheet_2")
            };
            ShowSheet(0);
        }

        if (cabinetViewOnly && !string.IsNullOrWhiteSpace(qrCode))
            InventoryApiClient.GetCabinet(this, qrCode, OnCabinetReceived, error =>
            {
                if (localCabinet == null) Debug.Log("[ImageCard] QR не относится к шкафу или API недоступен: " + error);
            });
    }

    private void BindUI(Transform panel)
    {
        ARCardSpawnUtility.AddPanelDepth(panel.GetComponent<Image>());
        var sheet = panel.Find("Sheet");
        rawImage = sheet != null ? sheet.GetComponent<RawImage>() : null;
        imageRect = sheet as RectTransform;
        titleText = panel.Find("Title")?.GetComponent<TextMeshProUGUI>();
        var row = panel.Find("Row");
        pageOneButton = row != null ? row.Find("Лист 1")?.GetComponent<Button>() : null;
        pageTwoButton = row != null ? row.Find("Лист 2")?.GetComponent<Button>() : null;
        closeButton = row != null ? row.Find("Закрыть")?.GetComponent<Button>() : null;
        if (pageOneButton != null) pageOneButton.onClick.AddListener(() => SelectPage(0));
        if (pageTwoButton != null) pageTwoButton.onClick.AddListener(() => SelectPage(1));
        if (closeButton != null) closeButton.onClick.AddListener(() => ARCardModeSelector.CloseCard(gameObject));
    }

    private void OnCabinetReceived(InventoryCabinetResponse response)
    {
        cabinetMode = true;
        cabinetTitle = response.title;
        if (titleText != null) titleText.text = "Шкаф: " + cabinetTitle;

        var photos = response.photos ?? System.Array.Empty<InventoryCabinetPhoto>();
        if (photos.Length == 0 && response.photoAvailable)
            photos = new[] { new InventoryCabinetPhoto { pageNumber = 1, photoUrl = response.photoUrl, caption = "Лист 1" } };

        foreach (var photo in photos)
        {
            int index = photo.pageNumber - 1;
            if (index < 0 || index >= cabinetPhotos.Length) continue;
            availablePages[index] = true;
            InventoryApiClient.GetPhoto(this, qrCode, photo.pageNumber, texture =>
            {
                if (cabinetPhotos[index] != null) Destroy(cabinetPhotos[index]);
                cabinetPhotos[index] = texture;
                if (index == selectedPage) ShowCabinetPage(index);
            }, error => Debug.LogWarning($"[ImageCard] Не удалось загрузить фото листа {photo.pageNumber}: {error}"));
        }

        UpdatePageButtons();
        if (availablePages[selectedPage]) ShowCabinetPage(selectedPage);
        else if (availablePages[0]) ShowCabinetPage(0);
        else
        {
            if (rawImage != null) { rawImage.texture = null; rawImage.color = new Color(.20f, .23f, .29f, 1f); }
            if (titleText != null) titleText.text = cabinetTitle + "\nФото шкафа не добавлено";
        }
    }

    private void AddLocalFallbackPhoto(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes)) { Destroy(texture); return; }
        cabinetPhotos[0] = texture;
        availablePages[0] = true;
        UpdatePageButtons();
    }

    private void SelectPage(int index)
    {
        if (cabinetMode) ShowCabinetPage(index);
        else ShowSheet(index);
    }

    private void ShowCabinetPage(int index)
    {
        if (!cabinetMode || rawImage == null || imageRect == null) return;
        index = Mathf.Clamp(index, 0, 1);
        if (!availablePages[index]) return;
        selectedPage = index;
        UpdatePageButtons();
        var texture = cabinetPhotos[index];
        rawImage.color = texture != null ? Color.white : new Color(.20f, .23f, .29f, 1f);
        rawImage.texture = texture;
        if (texture != null) FitImage(texture);
        if (titleText != null)
            titleText.text = texture != null
                ? $"Шкаф: {cabinetTitle} · Лист {index + 1} из 2"
                : $"{cabinetTitle}\nЗагружаем лист {index + 1}…";
    }

    private void ShowSheet(int index)
    {
        if (cabinetMode || sheets == null || rawImage == null || imageRect == null) return;
        int selected = Mathf.Clamp(index, 0, sheets.Length - 1);
        var texture = sheets[selected];
        if (texture == null) { Debug.LogWarning("[ImageCard] Лист продукции не найден: " + selected); return; }
        selectedPage = selected;
        rawImage.color = Color.white;
        rawImage.texture = texture;
        if (titleText != null) titleText.text = $"Информация о продукции · Лист {selected + 1}";
        FitImage(texture);
        UpdatePageButtons();
    }

    private void UpdatePageButtons()
    {
        if (pageOneButton != null) pageOneButton.gameObject.SetActive(cabinetMode ? availablePages[0] : sheets != null && sheets.Length > 0 && sheets[0] != null);
        if (pageTwoButton != null) pageTwoButton.gameObject.SetActive(cabinetMode ? availablePages[1] : sheets != null && sheets.Length > 1 && sheets[1] != null);
    }

    private void FitImage(Texture texture)
    {
        float aspect = (float)texture.width / texture.height;
        float width = maxImageWidth;
        float height = width / aspect;
        if (height > maxImageHeight) { height = maxImageHeight; width = height * aspect; }
        imageRect.sizeDelta = new Vector2(width, height);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < cabinetPhotos.Length; i++)
            if (cabinetPhotos[i] != null) Destroy(cabinetPhotos[i]);
    }
}
