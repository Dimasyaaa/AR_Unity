#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds a real, editable Unity Button to each primary AR card prefab.</summary>
[InitializeOnLoad]
public static class ARPanelPrefabButtons
{
    private static readonly string[] Prefabs =
    {
        "Assets/App/Content/Cards/CalculatorCard.prefab",
        "Assets/App/Content/Cards/CabinetViewCard.prefab",
        "Assets/App/Content/Cards/InventoryChecklistCard.prefab",
        "Assets/App/Content/Cards/WebCard.prefab",
        "Assets/App/Content/Cards/ProductInfoCard.prefab"
    };

    static ARPanelPrefabButtons()
    {
        EditorApplication.delayCall += EnsureButtonsExist;
    }

    [MenuItem("AR Inventory/Обновить кнопки закрепления AR-панелей")]
    public static void EnsureButtonsExist()
    {
        foreach (string path in Prefabs)
        {
            if (!File.Exists(path)) continue;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Canvas canvas = root.GetComponentInChildren<Canvas>(true);
                if (canvas == null || canvas.transform.Find("Закрепить") != null) continue;

                var buttonObject = new GameObject("Закрепить", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(ARPanelLockButton));
                var rect = (RectTransform)buttonObject.transform;
                rect.SetParent(canvas.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(148f, 68f);
                rect.anchoredPosition = new Vector2(-14f, -14f);

                var image = buttonObject.GetComponent<Image>();
                image.color = new Color(.08f, .48f, .78f, .96f);
                var button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.ColorTint;
                button.colors = new ColorBlock
                {
                    normalColor = Color.white, highlightedColor = new Color(.82f, .94f, 1f),
                    pressedColor = new Color(.65f, .82f, 1f), selectedColor = Color.white,
                    disabledColor = new Color(.5f, .5f, .5f, .5f), colorMultiplier = 1f, fadeDuration = .1f
                };

                var labelObject = new GameObject("Название", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                var labelRect = (RectTransform)labelObject.transform;
                labelRect.SetParent(rect, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(4f, 2f);
                labelRect.offsetMax = new Vector2(-4f, -2f);
                var label = labelObject.GetComponent<TextMeshProUGUI>();
                if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
                label.text = "Закрепить";
                label.fontSize = 20f;
                label.enableAutoSizing = true;
                label.fontSizeMin = 12f;
                label.fontSizeMax = 20f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.raycastTarget = false;

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }
}
#endif
