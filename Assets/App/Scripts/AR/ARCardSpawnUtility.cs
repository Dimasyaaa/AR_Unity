using System.Linq;
using UnityEngine;
using UnityEngine.XR.Templates.AR;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.UI;

/// <summary>Selects one of the card prefabs explicitly listed on the scene's ObjectSpawner.</summary>
public static class ARCardSpawnUtility
{
    public static void AddPanelDepth(Image panelImage)
    {
        if (panelImage == null)
            return;

        if (!panelImage.TryGetComponent(out Shadow shadow))
            shadow = panelImage.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(16f, -16f);
        shadow.useGraphicAlpha = true;

        // Keep card depth in prefab geometry. A runtime cube can end up in
        // front of the World Space Canvas when its facing direction changes.
    }

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

        if (FindExistingCard(cardPrefab) != null)
        {
            Debug.LogWarning($"[AR Cards] Карточка «{cardPrefab.name}» уже размещена. Сначала удали её, затем добавь заново.");
            return false;
        }

        spawner.SetSpawnObjectIndex(index);
        placementTrigger.enabled = true;
        HideObjectMenu();
        Debug.Log($"[AR Cards] Выбрана карточка «{cardPrefab.name}» ({index + 1}/{spawner.objectPrefabs.Count}). Коснитесь поверхности, чтобы разместить её.");
        return true;
    }

    public static void DisablePlacement()
    {
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
        if (prefab.GetComponent<CalculatorController>() != null)
            return Object.FindAnyObjectByType<CalculatorController>();
        if (prefab.TryGetComponent<ImageCardController>(out var imageCard))
            return Object.FindObjectsByType<ImageCardController>(FindObjectsInactive.Include)
                .FirstOrDefault(card => card.CabinetViewOnly == imageCard.CabinetViewOnly);
        if (prefab.GetComponent<InventoryChecklistController>() != null)
            return Object.FindAnyObjectByType<InventoryChecklistController>();
        if (prefab.GetComponent<WebCardController>() != null)
            return Object.FindAnyObjectByType<WebCardController>();

        return null;
    }
}
