#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

[InitializeOnLoad]
internal static class FinalizeSampleSceneCardMenu
{
    private const string ScenePath = "Assets/App/Scenes/SampleScene.unity";
    private const string MigrationKey = "ARUnity.SampleScene.CardMenu.Finalized.v2";

    static FinalizeSampleSceneCardMenu()
    {
        if (!EditorPrefs.GetBool(MigrationKey, false))
            EditorApplication.delayCall += ApplyOnce;
    }

    private static void ApplyOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene sampleScene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !sampleScene.IsValid() || !sampleScene.isLoaded;
        try
        {
            if (openedHere)
                sampleScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            Transform objectMenu = FindInScene(sampleScene, "Object Menu");
            Transform content = objectMenu != null
                ? objectMenu.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Content")
                : null;
            if (content == null)
                throw new InvalidOperationException("Не найден Object Menu/Content в SampleScene.");

            Transform calculator = content.Cast<Transform>().FirstOrDefault(t => t.name == "Button (Calculator)");
            if (calculator == null || calculator.GetComponent<ARCardButtonHook>() == null)
                throw new InvalidOperationException("Не найдена кнопка калькулятора с ARCardButtonHook.");

            DestroyChild(content, "Button (Web)");
            DestroyChild(content, "Button (WebImg)");

            GameObject calculatorPrefab = LoadPrefab("CalculatorCard");
            GameObject cabinetPrefab = LoadPrefab("CabinetViewCard");
            GameObject inventoryPrefab = LoadPrefab("InventoryChecklistCard");
            Sprite calculatorIcon = LoadSprite("Calculator.png");
            Sprite cabinetIcon = LoadSprite("icon_view.png");
            Sprite inventoryIcon = LoadSprite("checklist.png");

            ConfigureButton(calculator.gameObject, "Калькулятор", calculatorPrefab, calculatorIcon);
            GameObject cabinetButton = CreateOrFindButton(content, calculator.gameObject, "Button (Cabinet)");
            GameObject inventoryButton = CreateOrFindButton(content, calculator.gameObject, "Button (Inventory)");
            ConfigureButton(cabinetButton, "Вид шкафа", cabinetPrefab, cabinetIcon);
            ConfigureButton(inventoryButton, "Инвентаризация", inventoryPrefab, inventoryIcon);

            var spawner = sampleScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ObjectSpawner>(true))
                .FirstOrDefault();
            if (spawner == null)
                throw new InvalidOperationException("В SampleScene не найден ObjectSpawner.");

            var serializedSpawner = new SerializedObject(spawner);
            var prefabList = serializedSpawner.FindProperty("m_ObjectPrefabs");
            if (prefabList == null || !prefabList.isArray)
                throw new InvalidOperationException("Не найден список Object Prefabs в ObjectSpawner.");

            GameObject[] cardPrefabs = { calculatorPrefab, cabinetPrefab, inventoryPrefab };
            prefabList.arraySize = cardPrefabs.Length;
            for (int i = 0; i < cardPrefabs.Length; i++)
                prefabList.GetArrayElementAtIndex(i).objectReferenceValue = cardPrefabs[i];
            serializedSpawner.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(sampleScene);
            if (!EditorSceneManager.SaveScene(sampleScene))
                throw new InvalidOperationException("Unity не сохранила SampleScene.");

            EditorPrefs.SetBool(MigrationKey, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[AR Inventory] В SampleScene оставлены три статические карточки: калькулятор, вид шкафа и инвентаризация. Web и ProductInfo префабы сохранены в Assets.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (openedHere && sampleScene.IsValid() && sampleScene.isLoaded)
                EditorSceneManager.CloseScene(sampleScene, true);
        }
    }

    private static Transform FindInScene(Scene scene, string objectName)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == objectName)
                return root.transform;
            var match = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == objectName);
            if (match != null)
                return match;
        }
        return null;
    }

    private static void DestroyChild(Transform parent, string childName)
    {
        var child = parent.Cast<Transform>().FirstOrDefault(t =>
            string.Equals(t.name.Trim(), childName, StringComparison.Ordinal));
        if (child != null)
            UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static GameObject CreateOrFindButton(Transform content, GameObject template, string buttonName)
    {
        var existing = content.Cast<Transform>().FirstOrDefault(t => t.name == buttonName);
        if (existing != null)
            return existing.gameObject;

        var clone = UnityEngine.Object.Instantiate(template, content, false);
        clone.name = buttonName;
        return clone;
    }

    private static void ConfigureButton(GameObject button, string label, GameObject cardPrefab, Sprite icon)
    {
        if (button.GetComponent<ARCardButtonHook>() == null)
            button.AddComponent<ARCardButtonHook>();

        var serializedButton = new SerializedObject(button.GetComponent<ARCardButtonHook>());
        serializedButton.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;
        serializedButton.ApplyModifiedPropertiesWithoutUndo();

        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = icon;
            image.preserveAspect = true;
            EditorUtility.SetDirty(image);
        }

        foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            text.text = label;
        EditorUtility.SetDirty(button);
    }

    private static GameObject LoadPrefab(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/App/Content/Cards/{name}.prefab");
        if (prefab == null)
            throw new InvalidOperationException($"Не найден префаб Assets/App/Content/Cards/{name}.prefab.");
        return prefab;
    }

    private static Sprite LoadSprite(string fileName)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/App/Content/UI/Icons/{fileName}");
        if (sprite == null)
            throw new InvalidOperationException($"Не найдена иконка Assets/App/Content/UI/Icons/{fileName}.");
        return sprite;
    }
}
#endif
