using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Creates the editable uGUI hierarchies inside the three card prefabs.</summary>
public static class CardPrefabAuthoring
{
    private const string BodyMaterialPath = "Assets/App/Content/Cards/CardBody.mat";

    [MenuItem("AR Inventory/Создать или обновить UI трёх карточек")]
    private static void CreateAllCardUIs()
    {
        Create("Assets/App/Content/Cards/CalculatorCard.prefab", "CalculatorController");
        Create("Assets/App/Content/Cards/ProductInfoCard.prefab", "ImageCardController");
        Create("Assets/App/Content/Cards/WebCard.prefab", "WebCardController");
        AssetDatabase.SaveAssets();
        Debug.Log("[AR Cards] UI трёх карточек создан в Prefab Assets.");
    }

    [MenuItem("AR Inventory/Добавить объёмные корпуса трём карточкам")]
    private static void AddBodiesToAllCards()
    {
        var material = GetOrCreateBodyMaterial();
        AddBody("Assets/App/Content/Cards/CalculatorCard.prefab", material);
        AddBody("Assets/App/Content/Cards/ProductInfoCard.prefab", material);
        AddBody("Assets/App/Content/Cards/WebCard.prefab", material);
        AssetDatabase.SaveAssets();
        Debug.Log("[AR Cards] Объёмные корпуса добавлены в три Prefab.");
    }

    private static Material GetOrCreateBodyMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("Не найден подходящий шейдер для материала корпуса карточки.");

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, BodyMaterialPath);
        }

        var bodyColor = new Color(0.035f, 0.09f, 0.18f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", bodyColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", bodyColor);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.22f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.62f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AddBody(string path, Material material)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var backing = root.transform.Find("Card Body");
            if (backing == null)
            {
                backing = new GameObject("Card Body", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                backing.SetParent(root.transform, false);
            }

            backing.localPosition = new Vector3(0f, 0.2f, 0f);
            backing.localRotation = Quaternion.identity;
            backing.localScale = new Vector3(0.31f, 0.48f, 0.0375f);

            var meshFilter = backing.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (meshFilter.sharedMesh == null)
                throw new InvalidOperationException("Не удалось получить встроенную Cube Mesh.");
            backing.GetComponent<MeshRenderer>().sharedMaterial = material;

            var collider = root.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.size = new Vector3(0.31f, 0.48f, 0.0375f);
                collider.center = new Vector3(0f, 0.2f, 0f);
            }

            var canvas = root.GetComponentInChildren<Canvas>(true);
            if (canvas != null)
            {
                canvas.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var position = canvas.transform.localPosition;
                canvas.transform.localPosition = new Vector3(position.x, position.y, 0.020f);
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void Create(string path, string controllerName)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var canvas = root.GetComponentInChildren<Canvas>(true);
            var oldBacking = canvas != null ? canvas.transform.Find("Card 3D Backing") : null;
            if (oldBacking != null)
                UnityEngine.Object.DestroyImmediate(oldBacking.gameObject);

            if (canvas != null && canvas.transform.Find("Panel") != null)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return;
            }

            var controller = Array.Find(root.GetComponentsInChildren<MonoBehaviour>(true), item =>
                item != null && item.GetType().Name == controllerName);
            if (controller == null)
                throw new InvalidOperationException($"Не найден компонент {controllerName} в {path}");

            var method = controller.GetType().GetMethod("BuildUIInEditor", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException($"Не найден метод создания UI в {controllerName}");

            method.Invoke(controller, null);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
