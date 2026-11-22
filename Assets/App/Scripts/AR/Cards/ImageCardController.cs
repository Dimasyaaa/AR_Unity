using System.Collections.Generic;
using System.Linq;
using ArInventory.Local;
using ArInventory.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Показывает изображения содержимого коробки, загруженные из API или локальной базы.</summary>
public class ImageCardController : MonoBehaviour
{
    [Header("Область изображения")]
    [SerializeField] private float maxImageWidth = 500f;
    [SerializeField] private float maxImageHeight = 640f;

    private readonly Dictionary<int, Texture2D> boxPhotos = new();
    private readonly HashSet<int> availablePages = new();
    private RawImage rawImage;
    private RectTransform imageRect;
    private TextMeshProUGUI titleText;
    private Button previousButton;
    private Button nextButton;
    private Button closeButton;
    private LocalCabinetProfile localBox;
    private string qrCode;
    private string boxTitle;
    private int selectedPage;

    private void Start()
    {
        // Визуальные элементы находятся в префабе; скрипт только связывает события и данные.
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) { Debug.LogError("[ImageCard] Canvas не найден."); return; }
        var panel = canvas.transform.Find("Panel");
        if (panel == null) { Debug.LogError("[ImageCard] Панель не найдена."); return; }
        BindUI(panel);

        qrCode = GameDataManager.LastScannedQR;
        localBox = LocalDatabase.Instance != null
            ? LocalDatabase.Instance.FindCabinetByQr(qrCode)
            : null;
        boxTitle = localBox != null ? localBox.title : "Коробка";

        if (localBox != null)
        {
            AddLocalFallbackPhoto(1, localBox.reference_photo);
            foreach (LocalCabinetPhoto photo in LocalDatabase.Instance.GetCabinetPhotos(localBox.id))
                AddLocalFallbackPhoto(photo.page_number, photo.image_data);
            ShowFirstAvailablePage();
        }
        else if (titleText != null)
        {
            titleText.text = "Загружаем содержимое коробки…";
        }

        if (!string.IsNullOrWhiteSpace(qrCode))
            InventoryApiClient.GetBox(this, qrCode, OnBoxReceived, error =>
            {
                if (localBox == null)
                    Debug.Log("[ImageCard] Не удалось получить содержимое коробки: " + error);
            });
    }

    private void BindUI(Transform panel)
    {
        ARCardSpawnUtility.AddPanelDepth(panel.GetComponent<Image>());
        Transform sheet = panel.Find("Sheet");
        rawImage = sheet != null ? sheet.GetComponent<RawImage>() : null;
        imageRect = sheet as RectTransform;
        titleText = panel.Find("Title")?.GetComponent<TextMeshProUGUI>();

        Transform row = panel.Find("Row");
        previousButton = row != null
            ? (row.Find("Вид 1") ?? row.Find("Лист 1"))?.GetComponent<Button>()
            : null;
        nextButton = row != null
            ? (row.Find("Вид 2") ?? row.Find("Лист 2"))?.GetComponent<Button>()
            : null;
        closeButton = row != null ? row.Find("Закрыть")?.GetComponent<Button>() : null;

        if (previousButton != null) previousButton.onClick.AddListener(ShowPreviousPage);
        if (nextButton != null) nextButton.onClick.AddListener(ShowNextPage);
        if (closeButton != null) closeButton.onClick.AddListener(() => ARCardModeSelector.CloseCard(gameObject));
        SetButtonText(previousButton, "‹");
        SetButtonText(nextButton, "›");
        UpdatePageButtons();
    }

    private void OnBoxReceived(InventoryBoxResponse response)
    {
        // После успешного ответа API заменяет возможную локальную копию актуальными изображениями.
        ClearLoadedPhotos();
        boxTitle = response.title;
        InventoryBoxPhoto[] photos = response.photos ?? System.Array.Empty<InventoryBoxPhoto>();
        if (photos.Length == 0 && response.photoAvailable)
        {
            photos = new[]
            {
                new InventoryBoxPhoto
                {
                    pageNumber = 1,
                    photoUrl = response.photoUrl,
                    caption = "Содержимое коробки"
                }
            };
        }

        foreach (InventoryBoxPhoto photo in photos.Where(item => item.pageNumber > 0))
        {
            int page = photo.pageNumber;
            availablePages.Add(page);
            InventoryApiClient.GetBoxPhoto(this, qrCode, page, texture =>
            {
                ReplacePageTexture(page, texture);
                if (selectedPage == page || selectedPage == 0)
                    ShowBoxView(page);
            }, error => Debug.LogWarning($"[ImageCard] Не удалось загрузить фото {page}: {error}"));
        }

        if (!availablePages.Contains(selectedPage))
            selectedPage = availablePages.Count > 0 ? availablePages.Min() : 0;
        UpdatePageButtons();

        if (selectedPage > 0)
            ShowBoxView(selectedPage);
        else
        {
            if (rawImage != null)
            {
                rawImage.texture = null;
                rawImage.color = new Color(.20f, .23f, .29f, 1f);
            }
            if (titleText != null) titleText.text = boxTitle + "\nИзображения содержимого не добавлены";
        }
    }

    private void AddLocalFallbackPhoto(int pageNumber, byte[] bytes)
    {
        if (pageNumber < 1 || bytes == null || bytes.Length == 0) return;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes)) { Destroy(texture); return; }
        ReplacePageTexture(pageNumber, texture);
    }

    private void ReplacePageTexture(int pageNumber, Texture2D texture)
    {
        if (boxPhotos.TryGetValue(pageNumber, out Texture2D previous) && previous != null)
            Destroy(previous);
        boxPhotos[pageNumber] = texture;
        availablePages.Add(pageNumber);
        UpdatePageButtons();
    }

    private void ShowFirstAvailablePage()
    {
        selectedPage = availablePages.Count > 0 ? availablePages.Min() : 0;
        UpdatePageButtons();
        if (selectedPage > 0) ShowBoxView(selectedPage);
        else if (titleText != null) titleText.text = boxTitle + "\nИзображения содержимого не добавлены";
    }

    private void ShowPreviousPage()
    {
        int[] pages = availablePages.OrderBy(page => page).ToArray();
        int index = System.Array.IndexOf(pages, selectedPage);
        if (index > 0) ShowBoxView(pages[index - 1]);
    }

    private void ShowNextPage()
    {
        int[] pages = availablePages.OrderBy(page => page).ToArray();
        int index = System.Array.IndexOf(pages, selectedPage);
        if (index >= 0 && index + 1 < pages.Length) ShowBoxView(pages[index + 1]);
    }

    /// <summary>Голосовая команда листает виды вперёд или назад.</summary>
    public bool SelectPageFromVoice(int direction)
    {
        int previous = selectedPage;
        if (direction == 1) ShowNextPage();
        else ShowPreviousPage();
        return selectedPage != previous;
    }

    private void ShowBoxView(int pageNumber)
    {
        if (rawImage == null || imageRect == null || !availablePages.Contains(pageNumber)) return;
        selectedPage = pageNumber;
        UpdatePageButtons();
        boxPhotos.TryGetValue(pageNumber, out Texture2D texture);
        rawImage.color = texture != null ? Color.white : new Color(.20f, .23f, .29f, 1f);
        rawImage.texture = texture;
        if (texture != null) FitImage(texture);

        int[] pages = availablePages.OrderBy(page => page).ToArray();
        int ordinal = System.Array.IndexOf(pages, pageNumber) + 1;
        if (titleText != null)
            titleText.text = texture != null
                ? $"Коробка: {boxTitle} · Изображение {ordinal} из {pages.Length}"
                : $"{boxTitle}\nЗагружается изображение {ordinal}…";
    }

    private void UpdatePageButtons()
    {
        int[] pages = availablePages.OrderBy(page => page).ToArray();
        int index = System.Array.IndexOf(pages, selectedPage);
        bool hasMultiplePages = pages.Length > 1;
        if (previousButton != null)
        {
            previousButton.gameObject.SetActive(hasMultiplePages);
            previousButton.interactable = index > 0;
        }
        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(hasMultiplePages);
            nextButton.interactable = index >= 0 && index < pages.Length - 1;
        }
    }

    private static void SetButtonText(Button button, string value)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null) label.text = value;
    }

    private void ClearLoadedPhotos()
    {
        foreach (Texture2D texture in boxPhotos.Values)
            if (texture != null) Destroy(texture);
        boxPhotos.Clear();
        availablePages.Clear();
        selectedPage = 0;
    }

    private void FitImage(Texture texture)
    {
        // Сохраняем пропорции изображения в пределах заранее подготовленной области префаба.
        float aspect = (float)texture.width / texture.height;
        float width = maxImageWidth;
        float height = width / aspect;
        if (height > maxImageHeight) { height = maxImageHeight; width = height * aspect; }
        imageRect.sizeDelta = new Vector2(width, height);
    }

    private void OnDestroy()
    {
        if (previousButton != null) previousButton.onClick.RemoveListener(ShowPreviousPage);
        if (nextButton != null) nextButton.onClick.RemoveListener(ShowNextPage);
        ClearLoadedPhotos();
    }
}
