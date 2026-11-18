using System.Linq;
using UnityEngine;
using UnityEngine.XR.Templates.AR;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.UI;

/// <summary>Выбирает карточку для размещения и управляет режимом штатного AR-спавнера.</summary>
public static class ARCardSpawnUtility
{
    private static ObjectSpawner activeSpawner;
    private static GameObject selectedPrefab;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartWithPlacementDisabled()
    {
        DisablePlacement();
    }

    /// <summary>Добавляет панели мягкую тень, сохраняя её объём в самом префабе.</summary>
    public static void AddPanelDepth(Image panelImage)
    {
        if (panelImage == null)
            return;

        if (!panelImage.TryGetComponent(out Shadow shadow))
            shadow = panelImage.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(16f, -16f);
        shadow.useGraphicAlpha = true;

        // Объём панели задаётся геометрией префаба: созданный здесь куб может
        // оказаться перед Canvas при изменении направления карточки.
    }

    /// <summary>Выбирает префаб и включает ожидание касания поверхности для размещения.</summary>
    public static bool SelectForPlacement(GameObject cardPrefab)
    {
        if (cardPrefab == null)
        {
            Debug.LogError("[AR Cards] Не назначен префаб карточки.");
            return false;
        }

        ObjectSpawner spawner = Object.FindAnyObjectByType<ObjectSpawner>();
        if (spawner == null)
        {
            Debug.LogError("[AR Cards] В сцене не найден ObjectSpawner.");
            return false;
        }

        Behaviour placementTrigger = FindPlacementTrigger(spawner);
        if (placementTrigger == null)
        {
            Debug.LogError("[AR Cards] На ObjectSpawner не найден ARInteractorSpawnTrigger.");
            return false;
        }

        int index = spawner.objectPrefabs.IndexOf(cardPrefab);
        if (index < 0)
        {
            Debug.LogError($"[AR Cards] Карточка «{cardPrefab.name}» не добавлена в список ObjectSpawner сцены SampleScene. Добавь её в Object Prefabs в Inspector.");
            return false;
        }

        if (IsCardAlreadyPresent(cardPrefab))
        {
            Debug.LogWarning($"[AR Cards] Карточка «{cardPrefab.name}» уже размещена. Сначала удали её, затем добавь заново.");
            return false;
        }

        if (activeSpawner != null) activeSpawner.objectSpawned -= OnObjectSpawned;
        activeSpawner = spawner;
        selectedPrefab = cardPrefab;
        spawner.objectSpawned += OnObjectSpawned;
        spawner.SetSpawnObjectIndex(index);
        var horizontalOnly = placementTrigger.GetType().GetProperty("requireHorizontalUpSurface");
        horizontalOnly?.SetValue(placementTrigger, false);
        placementTrigger.enabled = true;
        HideObjectMenu();
        Debug.Log($"[AR Cards] Выбрана карточка «{cardPrefab.name}» ({index + 1}/{spawner.objectPrefabs.Count}). Коснитесь поверхности, чтобы разместить её.");
        return true;
    }

    /// <summary>Останавливает режим размещения и скрывает меню карточек.</summary>
    public static void DisablePlacement()
    {
        if (activeSpawner != null) activeSpawner.objectSpawned -= OnObjectSpawned;
        activeSpawner = null;
        selectedPrefab = null;
        ObjectSpawner spawner = Object.FindAnyObjectByType<ObjectSpawner>();
        if (spawner == null) return;

        Behaviour placementTrigger = FindPlacementTrigger(spawner);
        if (placementTrigger != null)
            placementTrigger.enabled = false;

        HideObjectMenu();
    }

    private static void HideObjectMenu()
    {
        ARTemplateMenuManager menu = Object.FindAnyObjectByType<ARTemplateMenuManager>();
        if (menu != null)
            menu.HideMenu();
    }

    private static Behaviour FindPlacementTrigger(ObjectSpawner spawner)
    {
        foreach (Component component in spawner.GetComponents<Component>())
        {
            if (component != null && component.GetType().Name == "ARInteractorSpawnTrigger")
                return component as Behaviour;
        }

        return null;
    }

    private static Component FindExistingCard(GameObject prefab)
    {
        var placedMarkers = Object.FindObjectsByType<ARPlacedCardMarker>(FindObjectsInactive.Include);
        var marker = placedMarkers.FirstOrDefault(card => card.PrefabName == prefab.name);
        if (marker != null) return marker;

        if (prefab.GetComponentInChildren<CalculatorController>(true) != null)
            return Object.FindAnyObjectByType<CalculatorController>(FindObjectsInactive.Include);
        var imageCard = prefab.GetComponentInChildren<ImageCardController>(true);
        if (imageCard != null)
            return Object.FindAnyObjectByType<ImageCardController>(FindObjectsInactive.Include);
        if (prefab.GetComponentInChildren<InventoryChecklistController>(true) != null)
            return Object.FindAnyObjectByType<InventoryChecklistController>(FindObjectsInactive.Include);
        return null;
    }

    /// <summary>Проверяет, размещена ли карточка или уже открыта в режиме оверлея.</summary>
    public static bool IsCardAlreadyPresent(GameObject prefab) =>
        prefab != null && (FindExistingCard(prefab) != null || ARCardModeSelector.HasOverlay(prefab));

    private static void OnObjectSpawned(GameObject instance)
    {
        if (instance != null && selectedPrefab != null)
        {
            var marker = instance.GetComponent<ARPlacedCardMarker>() ?? instance.AddComponent<ARPlacedCardMarker>();
            marker.PrefabName = selectedPrefab.name;
        }
        DisablePlacement();
        Debug.Log("[AR Cards] Карточка размещена. Режим размещения выключен; повторное создание заблокировано.");
    }
}
