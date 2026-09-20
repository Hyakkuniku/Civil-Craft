using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-time editor authoring for the simulation lesson panel. The resulting UI
/// is serialized into prefab/scene hierarchies; no runtime UI is generated.
/// </summary>
[InitializeOnLoad]
public static class SimulationLessonUIAuthoring
{
    private const string PresenterGuid = "86a69af76ec4471a939e90ba7287f209";
    private const int HierarchyAuthoringVersion = 6;

    private static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab"
    };

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    static SimulationLessonUIAuthoring()
    {
        EditorApplication.delayCall += AuthorMissingLessonUI;
    }

    [MenuItem("Tools/Civil Craft/Build Mode/Author Simulation Lesson UI")]
    public static void AuthorMissingLessonUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += AuthorMissingLessonUI;
            return;
        }

        int authoredCount = 0;
        foreach (string path in PrefabPaths)
        {
            if (!AssetNeedsWiring(path)) continue;
            authoredCount += WirePrefab(path);
        }

        foreach (string path in ScenePaths)
        {
            if (!AssetNeedsWiring(path)) continue;
            authoredCount += WireScene(path);
        }

        if (authoredCount > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Simulation Lesson] Authored the hierarchy panel for {authoredCount} build UI instance(s).");
        }
    }

    private static bool AssetNeedsWiring(string path)
    {
        if (!File.Exists(path)) return false;
        string yaml = File.ReadAllText(path);
        return yaml.Contains("b80a52e972006974caa6c608eb09b59e") &&
               (!yaml.Contains(PresenterGuid) ||
                !yaml.Contains($"hierarchyAuthoringVersion: {HierarchyAuthoringVersion}"));
    }

    private static int WirePrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        int count = 0;
        try
        {
            foreach (BuildUIController controller in root.GetComponentsInChildren<BuildUIController>(true))
            {
                if (EnsureLessonUI(controller)) count++;
            }

            if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return count;
    }

    private static int WireScene(string path)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedForAuthoring = !scene.IsValid() || !scene.isLoaded;
        if (openedForAuthoring) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        int count = 0;
        try
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BuildUIController controller in root.GetComponentsInChildren<BuildUIController>(true))
                {
                    if (EnsureLessonUI(controller)) count++;
                }
            }

            if (count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            if (openedForAuthoring && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
        return count;
    }

    private static bool EnsureLessonUI(BuildUIController controller)
    {
        if (controller == null) return false;

        Canvas canvas = ResolveBuildCanvas(controller);
        if (canvas == null)
        {
            Debug.LogWarning(
                $"[Simulation Lesson] Could not find the build-mode Canvas for '{controller.name}'. " +
                "The lesson panel was not authored because UI outside a Canvas cannot render.",
                controller);
            return false;
        }

        Transform uiParent = canvas.transform;
        Sprite panelSprite = FindPanelSprite(controller);
        TMP_FontAsset font = controller.actionLogText != null
            ? controller.actionLogText.font
            : TMP_Settings.defaultFontAsset;

        SimulationLessonPresenter presenter = controller.GetComponent<SimulationLessonPresenter>();
        RectTransform existingPanel = null;
        if (presenter != null)
        {
            SerializedObject serializedPresenter = new SerializedObject(presenter);
            existingPanel = serializedPresenter.FindProperty("lessonPanel")?.objectReferenceValue as RectTransform;
        }

        if (existingPanel != null)
        {
            bool changed = false;
            if (existingPanel.parent != uiParent)
            {
                existingPanel.SetParent(uiParent, false);
                existingPanel.SetAsLastSibling();
                changed = true;
            }


            changed |= EnsureLessonControls(
                uiParent,
                existingPanel,
                panelSprite,
                font,
                out Button menuButton,
                out Button closeButton);
            presenter.ConfigureControls(
                menuButton,
                closeButton,
                HierarchyAuthoringVersion);

            SerializedObject serializedPresenter = new SerializedObject(presenter);
            SerializedProperty version = serializedPresenter.FindProperty("hierarchyAuthoringVersion");
            if (version != null && version.intValue != HierarchyAuthoringVersion)
            {
                version.intValue = HierarchyAuthoringVersion;
                serializedPresenter.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(existingPanel);
                EditorUtility.SetDirty(presenter);
            }
            return changed;
        }

        if (presenter == null)
            presenter = controller.gameObject.AddComponent<SimulationLessonPresenter>();

        GameObject panelObject = new GameObject(
            "SimulationLessonPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(CanvasGroup),
            typeof(Outline));
        panelObject.transform.SetParent(uiParent, false);
        panelObject.transform.SetAsLastSibling();

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(32f, -205f);
        panelRect.sizeDelta = new Vector2(500f, 156f);

        Image borderImage = panelObject.GetComponent<Image>();
        borderImage.sprite = panelSprite;
        borderImage.type = panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        borderImage.color = new Color32(250, 246, 232, 255);
        borderImage.raycastTarget = false;

        Outline outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color32(10, 32, 92, 180);
        outline.effectDistance = new Vector2(0f, -4f);
        outline.useGraphicAlpha = true;

        CanvasGroup group = panelObject.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        GameObject fillObject = CreateImage(
            "Background",
            panelObject.transform,
            panelSprite,
            new Color32(13, 43, 116, 246));
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        Stretch(fillRect, 5f, 5f, 5f, 5f);

        TextMeshProUGUI title = CreateText(
            "Title",
            fillObject.transform,
            font,
            new Color32(244, 174, 41, 255),
            22f,
            TextAlignmentOptions.MidlineLeft);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(22f, -45f);
        titleRect.offsetMax = new Vector2(-22f, -12f);
        title.text = "STRUCTURAL LESSON";
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 1.5f;

        GameObject divider = CreateImage(
            "Divider",
            fillObject.transform,
            null,
            new Color32(224, 149, 0, 220));
        RectTransform dividerRect = divider.GetComponent<RectTransform>();
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.offsetMin = new Vector2(22f, -51f);
        dividerRect.offsetMax = new Vector2(-22f, -49f);

        TextMeshProUGUI message = CreateText(
            "Message",
            fillObject.transform,
            font,
            Color.white,
            22f,
            TextAlignmentOptions.TopLeft);
        RectTransform messageRect = message.rectTransform;
        messageRect.anchorMin = Vector2.zero;
        messageRect.anchorMax = Vector2.one;
        messageRect.offsetMin = new Vector2(22f, 15f);
        messageRect.offsetMax = new Vector2(-22f, -61f);
        message.enableAutoSizing = true;
        message.fontSizeMin = 17f;
        message.fontSizeMax = 22f;
        message.enableWordWrapping = true;
        message.overflowMode = TextOverflowModes.Ellipsis;
        message.text = "Simulation lesson text";

        presenter.Configure(panelRect, group, title, message, HierarchyAuthoringVersion);
        EnsureLessonControls(
            uiParent,
            panelRect,
            panelSprite,
            font,
            out Button newMenuButton,
            out Button newCloseButton);
        presenter.ConfigureControls(
            newMenuButton,
            newCloseButton,
            HierarchyAuthoringVersion);
        panelObject.SetActive(false);
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(presenter);
        return true;
    }

    private static bool EnsureLessonControls(
        Transform uiParent,
        RectTransform lessonPanel,
        Sprite panelSprite,
        TMP_FontAsset font,
        out Button menuButton,
        out Button closeButton)
    {
        bool changed = false;
        Transform existingMenu = FindDescendant(uiParent, "SimulationLessonMenuButton");
        Transform existingOptions = FindDescendant(uiParent, "SimulationLessonOptionsPanel");
        if (existingOptions != null)
        {
            Object.DestroyImmediate(existingOptions.gameObject);
            changed = true;
        }

        GameObject menuObject;
        if (existingMenu == null)
        {
            menuObject = new GameObject(
                "SimulationLessonMenuButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            menuObject.transform.SetParent(uiParent, false);
            menuObject.transform.SetAsLastSibling();
            changed = true;
        }
        else
        {
            menuObject = existingMenu.gameObject;
        }

        RectTransform menuRect = menuObject.GetComponent<RectTransform>();
        menuRect.anchorMin = new Vector2(0f, 1f);
        menuRect.anchorMax = new Vector2(0f, 1f);
        menuRect.pivot = new Vector2(0f, 1f);
        menuRect.anchoredPosition = new Vector2(32f, -205f);
        menuRect.sizeDelta = new Vector2(64f, 64f);

        Image menuImage = menuObject.GetComponent<Image>();
        menuImage.sprite = panelSprite;
        menuImage.type = panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        menuImage.color = new Color32(13, 43, 116, 246);
        menuImage.raycastTarget = true;
        menuButton = menuObject.GetComponent<Button>();
        menuButton.targetGraphic = menuImage;

        if (FindDescendant(menuObject.transform, "Burger Bar 1") == null)
        {
            for (int index = 0; index < 3; index++)
            {
                GameObject bar = CreateImage(
                    "Burger Bar " + (index + 1),
                    menuObject.transform,
                    null,
                    Color.white);
                RectTransform barRect = bar.GetComponent<RectTransform>();
                barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
                barRect.pivot = new Vector2(0.5f, 0.5f);
                barRect.anchoredPosition = new Vector2(0f, 12f - index * 12f);
                barRect.sizeDelta = new Vector2(30f, 4f);
            }
            changed = true;
        }

        Transform existingClose = FindDescendant(lessonPanel, "SimulationLessonCloseButton");
        GameObject closeObject;
        if (existingClose == null)
        {
            closeObject = new GameObject(
                "SimulationLessonCloseButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(Outline));
            closeObject.transform.SetParent(lessonPanel, false);
            changed = true;
        }
        else
        {
            closeObject = existingClose.gameObject;
        }
        closeObject.transform.SetAsLastSibling();
        closeObject.SetActive(true);

        RectTransform closeRect = closeObject.GetComponent<RectTransform>();
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(0.5f, 0.5f);
        closeRect.anchoredPosition = Vector2.zero;
        closeRect.sizeDelta = new Vector2(42f, 42f);

        Image closeImage = closeObject.GetComponent<Image>();
        Sprite circleSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        closeImage.sprite = circleSprite != null ? circleSprite : panelSprite;
        closeImage.type = Image.Type.Simple;
        closeImage.color = new Color32(250, 246, 232, 255);
        closeImage.raycastTarget = true;
        closeButton = closeObject.GetComponent<Button>();
        closeButton.targetGraphic = closeImage;

        Outline closeOutline = closeObject.GetComponent<Outline>();
        closeOutline.effectColor = new Color32(13, 43, 116, 230);
        closeOutline.effectDistance = new Vector2(2f, -2f);
        closeOutline.useGraphicAlpha = true;

        Transform existingCloseLabel = FindDescendant(closeObject.transform, "Close Icon");
        TextMeshProUGUI closeLabel;
        if (existingCloseLabel == null)
        {
            closeLabel = CreateText(
                "Close Icon",
                closeObject.transform,
                font,
                new Color32(13, 43, 116, 255),
                28f,
                TextAlignmentOptions.Center);
            changed = true;
        }
        else
        {
            closeLabel = existingCloseLabel.GetComponent<TextMeshProUGUI>();
        }
        Stretch(closeLabel.rectTransform, 1f, 1f, 1f, 3f);
        closeLabel.text = "×";
        closeLabel.fontStyle = FontStyles.Bold;
        closeLabel.raycastTarget = false;

        Transform title = FindDescendant(lessonPanel, "Title");
        if (title is RectTransform titleRect)
            titleRect.offsetMax = new Vector2(-70f, -12f);

        // These are presentation-only children and must remain enabled. Keeping
        // this repair here prevents a close-button layout edit from leaving the
        // frame visible while its background and text disappear.
        changed |= EnsureActive(lessonPanel, "Background");
        changed |= EnsureActive(lessonPanel, "Title");
        changed |= EnsureActive(lessonPanel, "Divider");
        changed |= EnsureActive(lessonPanel, "Message");

        menuObject.SetActive(false);
        return changed;
    }

    private static bool EnsureActive(Transform root, string objectName)
    {
        Transform target = FindDescendant(root, objectName);
        if (target == null || target.gameObject.activeSelf) return false;
        target.gameObject.SetActive(true);
        return true;
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        if (root == null) return null;
        foreach (Transform value in root.GetComponentsInChildren<Transform>(true))
            if (value != null && value.name == objectName) return value;
        return null;
    }

    private static Canvas ResolveBuildCanvas(BuildUIController controller)
    {
        if (controller.actionLogText != null)
        {
            Canvas canvas = controller.actionLogText.GetComponentInParent<Canvas>(true);
            if (canvas != null) return canvas;
        }

        if (controller.simulationControlsPanel != null)
        {
            Canvas canvas = controller.simulationControlsPanel.GetComponentInParent<Canvas>(true);
            if (canvas != null) return canvas;
        }

        if (controller.playSimulationButtonObject != null)
        {
            Canvas canvas = controller.playSimulationButtonObject.GetComponentInParent<Canvas>(true);
            if (canvas != null) return canvas;
        }

        Canvas childCanvas = controller.GetComponentInChildren<Canvas>(true);
        if (childCanvas != null) return childCanvas;

        foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (canvas != null && canvas.gameObject.scene == controller.gameObject.scene)
                return canvas;
        }
        return null;
    }

    private static Sprite FindPanelSprite(BuildUIController controller)
    {
        if (controller.simulationControlsPanel != null)
        {
            Image image = controller.simulationControlsPanel.GetComponent<Image>();
            if (image != null && image.sprite != null) return image.sprite;
        }

        Image[] images = controller.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image != null && image.sprite != null && image.type == Image.Type.Sliced)
                return image.sprite;
        }
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
    }

    private static GameObject CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        GameObject value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        value.transform.SetParent(parent, false);
        Image image = value.GetComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return value;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        TMP_FontAsset font,
        Color color,
        float size,
        TextAlignmentOptions alignment)
    {
        GameObject value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        value.transform.SetParent(parent, false);
        TextMeshProUGUI text = value.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.color = color;
        text.fontSize = size;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
