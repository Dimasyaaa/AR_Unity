using System;
using System.Collections.Generic;
using System.Linq;
using ArInventory.Local;
using ArInventory.Network;
using Siccity.GLTFUtility;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Показывает 3D-содержимое коробки, найденной по последнему QR-коду.</summary>
public sealed class BoxModelsCardController : MonoBehaviour
{
    [Header("Связи, назначаемые в префабе")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private RawImage modelBackground;
    [SerializeField] private Button closeButton;
    [Header("Размер тестовых фигур в локальных единицах Canvas")]
    [SerializeField, Min(10f)] private float primitiveSize = 54f;
    [SerializeField, Min(40f)] private float itemSpacing = 142f;

    private readonly List<GameObject> generatedObjects = new();
    private string qrCode;
    private InventoryBoxResponse loadedBox;
    private LocalCabinetProfile localBox;
    private int previewCount;

    private void Start()
    {
        if (modelRoot == null)
        {
            Debug.LogError("[Модели коробки] В префабе не назначена область modelRoot.");
            return;
        }

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
        if (modelBackground != null)
        {
            modelBackground.texture = null;
            modelBackground.color = new Color(.12f, .15f, .21f, 1f);
            modelBackground.raycastTarget = false;
        }

        qrCode = GameDataManager.LastScannedQR;
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            SetTitle("Сначала отсканируйте QR-код коробки");
            return;
        }

        localBox = LocalDatabase.Instance != null
            ? LocalDatabase.Instance.FindCabinetByQr(qrCode)
            : null;

        SetTitle("Загружаем 3D-содержимое…");
        InventoryApiClient.GetBox(this, qrCode, OnBoxLoaded, OnLoadFailed);
    }

    private void OnBoxLoaded(InventoryBoxResponse box)
    {
        loadedBox = box;
        if (box.modelAvailable)
        {
            SetTitle($"3D-модель · {box.title}");
            InventoryApiClient.GetBoxModel(this, qrCode, bytes => ImportModel(bytes), error =>
            {
                Debug.LogWarning("[Модели коробки] Не удалось загрузить GLB: " + error);
                LocalBoxModel localModel = localBox != null && LocalDatabase.Instance != null
                    ? LocalDatabase.Instance.GetBoxModel(localBox.id)
                    : null;
                if (localModel != null && localModel.model_data != null && localModel.model_data.Length > 0)
                {
                    SetTitle($"Загружаем локальную 3D-модель · {box.title}");
                    ImportModel(localModel.model_data);
                }
                else
                {
                    CreatePrimitivePreview(box);
                }
            });
            return;
        }

        CreatePrimitivePreview(box);
    }

    private void ImportModel(byte[] data)
    {
        if (data == null || data.Length < 12)
        {
            SetTitle("GLB-файл пустой или повреждён");
            return;
        }

        try
        {
            Importer.ImportGLBAsync(data, new ImportSettings(), (model, animations) =>
            {
                if (model == null)
                {
                    if (loadedBox != null) CreatePrimitivePreview(loadedBox);
                    else SetTitle("Не удалось прочитать GLB-модель");
                    return;
                }

                model.name = "Загруженная GLB-модель";
                model.transform.SetParent(modelRoot, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                NormalizeModel(model);
                generatedObjects.Add(model);
                SetTitle("3D-модель загружена");
            });
        }
        catch (Exception error)
        {
            Debug.LogWarning("[Модели коробки] Ошибка чтения GLB: " + error.Message);
            if (loadedBox != null) CreatePrimitivePreview(loadedBox);
            else SetTitle("Не удалось прочитать GLB-модель");
        }
    }

    private void CreatePrimitivePreview(InventoryBoxResponse box)
    {
        ClearGeneratedObjects();
        InventoryBoxItem[] items = box.items ?? Array.Empty<InventoryBoxItem>();
        if (items.Length == 0)
        {
            SetTitle($"Для коробки «{box.title}» нет позиций");
            return;
        }

        const int maxDisplayed = 20;
        previewCount = items.Sum(item => Mathf.Clamp(item.expectedQuantity, 1, 4));
        previewCount = Mathf.Clamp(previewCount, 1, maxDisplayed);
        int total = 0;
        foreach (InventoryBoxItem item in items)
        {
            int count = Mathf.Clamp(item.expectedQuantity, 1, 4);
            for (int index = 0; index < count && total < maxDisplayed; index++, total++)
                CreatePrimitive(item, total);
            if (total >= maxDisplayed) break;
        }

        SetTitle($"Тестовые 3D-фигуры · {box.title}");
    }

    private void CreatePrimitive(InventoryBoxItem item, int index)
    {
        PrimitiveType type = item.modelType switch
        {
            "sphere" => PrimitiveType.Sphere,
            "cylinder" => PrimitiveType.Cylinder,
            "capsule" => PrimitiveType.Capsule,
            "cone" => PrimitiveType.Cylinder,
            _ => PrimitiveType.Cube
        };

        GameObject model = GameObject.CreatePrimitive(type);
        model.name = $"{item.name} · {index + 1}";
        model.transform.SetParent(modelRoot, false);
        model.transform.localScale = Vector3.one * primitiveSize;
        if (item.modelType == "cone")
            model.transform.localScale = new Vector3(primitiveSize, primitiveSize * 1.25f, primitiveSize);
        int columns = Mathf.Min(4, Mathf.Max(1, previewCount));
        int rows = Mathf.Max(1, Mathf.CeilToInt((float)previewCount / columns));
        int displayed = Mathf.Clamp(index, 0, previewCount - 1);
        int column = displayed % columns;
        int row = displayed / columns;
        float x = (column - (columns - 1) * .5f) * itemSpacing;
        float y = ((rows - 1) * .5f - row) * itemSpacing;
        model.transform.localPosition = new Vector3(x, y, 24f);

        var collider = model.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Renderer renderer = model.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
            renderer.material.color = ColorFor(item.modelType);
        generatedObjects.Add(model);
    }

    private static Color ColorFor(string modelType) => modelType switch
    {
        "sphere" => new Color(.15f, .75f, .93f),
        "cylinder" => new Color(.22f, .78f, .44f),
        "cone" => new Color(1f, .67f, .2f),
        "capsule" => new Color(.75f, .45f, .94f),
        _ => new Color(.35f, .62f, 1f)
    };

    private void NormalizeModel(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);

        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largest > .001f)
            model.transform.localScale *= .34f / largest;
        model.transform.position += modelRoot.position - bounds.center;
    }

    private void OnLoadFailed(string error)
    {
        Debug.LogWarning("[Модели коробки] " + error);
        if (localBox == null || LocalDatabase.Instance == null)
        {
            SetTitle("Не удалось загрузить данные коробки");
            return;
        }

        var localItems = LocalDatabase.Instance.GetCabinetChecklist(localBox.id);
        loadedBox = new InventoryBoxResponse
        {
            success = true,
            boxId = localBox.id,
            qrCode = qrCode,
            title = localBox.title,
            modelAvailable = LocalDatabase.Instance.GetBoxModel(localBox.id) != null,
            items = localItems.Select(item => new InventoryBoxItem
            {
                id = item.id,
                name = item.item_name,
                description = item.description,
                sortOrder = item.sort_order,
                expectedQuantity = item.expected_quantity,
                modelType = item.model_type
            }).ToArray()
        };

        LocalBoxModel cachedModel = LocalDatabase.Instance.GetBoxModel(localBox.id);
        if (cachedModel != null && cachedModel.model_data != null && cachedModel.model_data.Length > 0)
        {
            SetTitle($"Загружаем локальную 3D-модель · {localBox.title}");
            ImportModel(cachedModel.model_data);
        }
        else
        {
            CreatePrimitivePreview(loadedBox);
        }
    }

    private void SetTitle(string value)
    {
        if (titleText != null) titleText.text = value;
    }

    private void ClearGeneratedObjects()
    {
        foreach (GameObject generated in generatedObjects)
            if (generated != null) Destroy(generated);
        generatedObjects.Clear();
    }

    private void Close() => ARCardModeSelector.CloseCard(gameObject);

    private void OnDestroy()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        ClearGeneratedObjects();
    }
}
