#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Creates editable help UI objects in the three app scenes and wires their controllers.</summary>
internal static class VoiceHelpSceneAuthoring
{
    private const string PrefabPath = "Assets/App/Content/UI/Prefabs/VoiceHelpUI.prefab";
    private static readonly string[] ScenePaths =
    {
        "Assets/App/Scenes/LoginScene.unity",
        "Assets/App/Scenes/StartScene.unity",
        "Assets/App/Scenes/SampleScene.unity"
    };

    [MenuItem("AR Inventory/Voice/Настроить подсказки в сценах")]
    private static void ConfigureScenes()
    {
        try
        {
            EnsureFolder("Assets/App/Content/UI");
            EnsureFolder("Assets/App/Content/UI/Prefabs");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                prefab = CreatePrefab();
            else
                UpdatePrefabLayout();

            Scene originalActiveScene = SceneManager.GetActiveScene();
            foreach (string scenePath in ScenePaths)
                ConfigureScene(scenePath, prefab);

            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                SceneManager.SetActiveScene(originalActiveScene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Voice Help] Сценовые панели помощи обновлены. Их можно редактировать в Canvas каждой сцены и в Assets/App/Content/UI/Prefabs/VoiceHelpUI.prefab.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static GameObject CreatePrefab()
    {
        var root = new GameObject("VoiceHelpUI", typeof(RectTransform));
        SetRect(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        GameObject panel = CreateObject("VoiceHelpPanel", root.transform);
        panel.SetActive(false);
        SetRect(panel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        GameObject dimmer = CreateImage("Dimmer", panel.transform, new Color(.015f, .025f, .045f, .78f));
        SetRect(dimmer.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        GameObject card = CreateImage("PanelCard", panel.transform, new Color(.055f, .085f, .13f, .99f));
        SetRect(card.GetComponent<RectTransform>(), new Vector2(.035f, .11f), new Vector2(.965f, .965f), Vector2.zero, Vector2.zero);
        var cardImage = card.GetComponent<Image>();
        Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (rounded != null)
        {
            cardImage.sprite = rounded;
            cardImage.type = Image.Type.Sliced;
        }

        GameObject accent = CreateImage("Accent", card.transform, new Color(.1f, .68f, .95f, 1f));
        SetRect(accent.GetComponent<RectTransform>(), new Vector2(.03f, .975f), new Vector2(.97f, .985f), Vector2.zero, Vector2.zero);

        TextMeshProUGUI title = CreateText("Title", card.transform, "Голосовое управление", 42, FontStyles.Bold, TextAlignmentOptions.Center);
        SetRect(title.rectTransform, new Vector2(.06f, .885f), new Vector2(.94f, .965f), Vector2.zero, Vector2.zero);
        title.color = new Color(.58f, .88f, 1f, 1f);
        title.enableAutoSizing = true;
        title.fontSizeMin = 25;
        title.fontSizeMax = 42;

        TextMeshProUGUI subtitle = CreateText("Subtitle", card.transform, "Команды для текущего экрана", 24, FontStyles.Normal, TextAlignmentOptions.Center);
        SetRect(subtitle.rectTransform, new Vector2(.07f, .825f), new Vector2(.93f, .885f), Vector2.zero, Vector2.zero);
        subtitle.color = new Color(.76f, .83f, .91f, 1f);
        subtitle.enableAutoSizing = true;
        subtitle.fontSizeMin = 18;
        subtitle.fontSizeMax = 24;

        GameObject scrollObject = CreateObject("CommandsScroll", card.transform);
        SetRect(scrollObject.GetComponent<RectTransform>(), new Vector2(.065f, .12f), new Vector2(.935f, .82f), Vector2.zero, Vector2.zero);
        var scroll = scrollObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;

        GameObject viewport = CreateImage("Viewport", scrollObject.transform, new Color(1f, 1f, 1f, .01f));
        SetRect(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scroll.viewport = viewport.GetComponent<RectTransform>();

        GameObject content = CreateObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        SetRect(contentRect, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        contentRect.pivot = new Vector2(.5f, 1f);
        content.AddComponent<VerticalLayoutGroup>().childControlHeight = true;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 3;
        layout.padding = new RectOffset(8, 8, 6, 6);
        var contentFitter = content.AddComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;

        TextMeshProUGUI commands = CreateText("CommandsText", content.transform, "", 24, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        commands.textWrappingMode = TextWrappingModes.Normal;
        commands.enableAutoSizing = true;
        commands.fontSizeMin = 16;
        commands.fontSizeMax = 24;
        commands.color = Color.white;
        commands.raycastTarget = false;
        commands.rectTransform.anchorMin = new Vector2(0, 1);
        commands.rectTransform.anchorMax = new Vector2(1, 1);
        commands.rectTransform.pivot = new Vector2(.5f, 1);
        commands.rectTransform.sizeDelta = Vector2.zero;
        commands.rectTransform.anchoredPosition = Vector2.zero;
        var commandLayout = commands.gameObject.AddComponent<LayoutElement>();
        commandLayout.flexibleWidth = 1;

        GameObject close = CreateImage("CloseButton", card.transform, new Color(.12f, .38f, .62f, 1f));
        SetRect(close.GetComponent<RectTransform>(), new Vector2(.34f, .025f), new Vector2(.66f, .095f), Vector2.zero, Vector2.zero);
        var closeButton = close.AddComponent<Button>();
        closeButton.targetGraphic = close.GetComponent<Image>();
        TextMeshProUGUI closeText = CreateText("Label", close.transform, "Закрыть", 27, FontStyles.Bold, TextAlignmentOptions.Center);
        SetRect(closeText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        closeText.color = Color.white;
        closeText.enableAutoSizing = true;
        closeText.fontSizeMin = 18;
        closeText.fontSizeMax = 27;

        GameObject toast = CreateImage("VoiceStatusToast", root.transform, new Color(.035f, .065f, .11f, .97f));
        toast.SetActive(false);
        SetRect(toast.GetComponent<RectTransform>(), new Vector2(.1f, .015f), new Vector2(.9f, .075f), Vector2.zero, Vector2.zero);
        TextMeshProUGUI status = CreateText("StatusText", toast.transform, "", 23, FontStyles.Bold, TextAlignmentOptions.Center);
        SetRect(status.rectTransform, new Vector2(.025f, .08f), new Vector2(.975f, .92f), Vector2.zero, Vector2.zero);
        status.color = Color.white;
        status.enableAutoSizing = true;
        status.fontSizeMin = 14;
        status.fontSizeMax = 20;
        status.raycastTarget = false;
        toast.GetComponent<Image>().raycastTarget = false;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (saved == null)
            throw new InvalidOperationException("Не удалось создать VoiceHelpUI.prefab.");
        return saved;
    }

    private static void UpdatePrefabLayout()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform card = root.transform.Find("VoiceHelpPanel/PanelCard");
            Transform panel = root.transform.Find("VoiceHelpPanel");
            Transform toast = root.transform.Find("VoiceStatusToast");
            if (panel == null || card == null || toast == null)
                throw new InvalidOperationException("В VoiceHelpUI.prefab не найдены карточка подсказки или статусная панель.");

            // The prefab is active so its status toast can be shown, but the full-screen
            // dimmer belongs to the closed panel and must never intercept scene input on load.
            root.SetActive(true);
            panel.gameObject.SetActive(false);
            toast.gameObject.SetActive(false);

            SetRect(card.GetComponent<RectTransform>(), new Vector2(.035f, .11f), new Vector2(.965f, .965f), Vector2.zero, Vector2.zero);
            Transform scroll = root.transform.Find("VoiceHelpPanel/PanelCard/CommandsScroll");
            Transform close = root.transform.Find("VoiceHelpPanel/PanelCard/CloseButton");
            Transform content = root.transform.Find("VoiceHelpPanel/PanelCard/CommandsScroll/Viewport/Content");
            Transform commands = content != null ? content.Find("CommandsText") : null;
            if (scroll == null || close == null || content == null || commands == null)
                throw new InvalidOperationException("В VoiceHelpUI.prefab не найдены элементы области команд.");

            SetRect(scroll.GetComponent<RectTransform>(), new Vector2(.065f, .12f), new Vector2(.935f, .82f), Vector2.zero, Vector2.zero);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(.34f, .025f), new Vector2(.66f, .095f), Vector2.zero, Vector2.zero);
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = 3;
                layout.padding = new RectOffset(8, 8, 6, 6);
            }
            TextMeshProUGUI commandText = commands.GetComponent<TextMeshProUGUI>();
            if (commandText != null)
            {
                commandText.fontSize = 24;
                commandText.fontSizeMin = 16;
                commandText.fontSizeMax = 24;
            }
            SetRect(toast.GetComponent<RectTransform>(), new Vector2(.1f, .015f), new Vector2(.9f, .075f), Vector2.zero, Vector2.zero);
            TextMeshProUGUI status = toast.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
            if (status != null)
            {
                status.fontSizeMin = 14;
                status.fontSizeMax = 20;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureScene(string scenePath, GameObject prefab)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        try
        {
            if (openedHere)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            Canvas canvas = FindInScene(scene).OfType<Canvas>()
                .Where(item => item != null && item.gameObject.name == "Canvas")
                .FirstOrDefault() ?? FindInScene(scene).OfType<Canvas>().FirstOrDefault();
            if (canvas == null)
                throw new InvalidOperationException($"В сцене {scene.name} не найден Canvas.");

            VoiceHelpController controller = FindInScene(scene).OfType<VoiceHelpController>().FirstOrDefault();
            if (controller == null)
                throw new InvalidOperationException($"В сцене {scene.name} не найден HelpButton с VoiceHelpController.");

            Transform helpUi = canvas.transform.Find("VoiceHelpUI");
            GameObject instance = helpUi != null ? helpUi.gameObject : PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Не удалось добавить панель помощи в {scene.name}.");
            instance.name = "VoiceHelpUI";
            instance.transform.SetParent(canvas.transform, false);
            instance.transform.SetAsLastSibling();
            instance.SetActive(true);
            SetRect(instance.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Transform panel = instance.transform.Find("VoiceHelpPanel");
            Transform commands = panel != null ? panel.Find("PanelCard/CommandsScroll/Viewport/Content/CommandsText") : null;
            Transform close = panel != null ? panel.Find("PanelCard/CloseButton") : null;
            Transform toast = instance.transform.Find("VoiceStatusToast");
            Transform status = toast != null ? toast.Find("StatusText") : null;
            if (panel == null || commands == null || close == null || toast == null || status == null)
                throw new InvalidOperationException($"Неполная структура VoiceHelpUI в {scene.name}.");

            // Keep only the help button and hidden toast active at startup. The panel's
            // full-screen dimmer uses raycasts intentionally while help is open.
            panel.gameObject.SetActive(false);
            toast.gameObject.SetActive(false);

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            serialized.FindProperty("commandsText").objectReferenceValue = commands.GetComponent<TextMeshProUGUI>();
            serialized.FindProperty("closeButton").objectReferenceValue = close.GetComponent<Button>();
            serialized.FindProperty("statusToast").objectReferenceValue = toast.gameObject;
            serialized.FindProperty("statusText").objectReferenceValue = status.GetComponent<TextMeshProUGUI>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (scene.name != "StartScene")
                PositionHelpButton(controller);

            EnsureVoiceRouter(scene);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(instance);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Unity не сохранила сцену {scene.name}.");
        }
        finally
        {
            if (openedHere && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void PositionHelpButton(VoiceHelpController controller)
    {
        RectTransform button = controller.GetComponent<RectTransform>();
        Canvas canvas = controller.GetComponentInParent<Canvas>();
        if (button == null || canvas == null) return;

        button.anchorMin = button.anchorMax = new Vector2(.975f, .965f);
        button.pivot = new Vector2(1f, 1f);
        button.anchoredPosition = new Vector2(-22f, -24f);
        float size = canvas.renderMode == RenderMode.WorldSpace ? 180f : 86f;
        button.sizeDelta = new Vector2(size, size);
        EditorUtility.SetDirty(button);
    }

    private static void EnsureVoiceRouter(Scene scene)
    {
        if (FindInScene(scene).OfType<VoiceRouter>().Any())
            return;

        var system = new GameObject("VoiceSystem");
        SceneManager.MoveGameObjectToScene(system, scene);
        system.AddComponent<VoiceRouter>();
        EditorUtility.SetDirty(system);
    }

    private static System.Collections.Generic.IEnumerable<Component> FindInScene(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Component>(true))
            .Where(component => component != null);
    }

    private static GameObject CreateObject(string name, Transform parent)
    {
        var result = new GameObject(name, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result;
    }

    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        GameObject result = CreateObject(name, parent);
        Image image = result.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = name == "Dimmer" || name == "PanelCard" || name == "CloseButton";
        Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (rounded != null && (name == "PanelCard" || name == "CloseButton" || name == "VoiceStatusToast"))
        {
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
        }
        return result;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float size, FontStyles style, TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateObject(name, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
