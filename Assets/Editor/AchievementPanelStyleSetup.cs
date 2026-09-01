#if UNITY_EDITOR
using TMPro;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AchievementPanelStyleSetup
{
    private const string ThemeVersion = "CivilCraft.AchievementTheme.v8";
    private const string RowPrefabPath =
        "Assets/Script/Player/Achievements/AchivementRow_Prefab.prefab";
    private const string FontPath =
        "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset";
    private const string AchievementFolder =
        "Assets/BridgeBuilder/Data/Achievements";
    private const string ManagersPrefabPath =
        "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";

    private static readonly string[] TargetScenes =
    {
        "Assets/Scenes/Main Menu.unity",
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    static AchievementPanelStyleSetup()
    {
        EditorApplication.delayCall += ApplyCurrentThemeOnce;
    }

    [MenuItem("Tools/Civil Craft/Style Achievement Panels")]
    public static void StyleAchievementPanels()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        List<AchievementSO> achievements = LoadAchievementDatabase();
        StyleRowPrefab(font);
        StyleManagersPrefab(achievements);

        foreach (string scenePath in TargetScenes)
            StyleScene(scenePath, font, achievements);

        AssetImporter rowImporter = AssetImporter.GetAtPath(RowPrefabPath);
        if (rowImporter != null)
        {
            rowImporter.userData = ThemeVersion;
            AssetDatabase.WriteImportSettingsIfDirty(RowPrefabPath);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[AchievementPanelStyleSetup] Achievement panels and row prefab are styled and wired.");
    }

    private static void StyleScene(
        string scenePath,
        TMP_FontAsset font,
        List<AchievementSO> achievements)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForSetup = !scene.IsValid() || !scene.isLoaded;
        if (openedForSetup)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        AchievementUIManager manager = FindManagerInScene(scene);
        if (manager == null || manager.achievementPanel == null)
        {
            Debug.LogWarning($"[AchievementPanelStyleSetup] No configured AchievementUIManager in {scenePath}.");
            if (openedForSetup) EditorSceneManager.CloseScene(scene, true);
            return;
        }

        Transform scroll = FindDescendant(manager.achievementPanel.transform, "Scroll View");
        if (scroll == null)
        {
            Debug.LogWarning($"[AchievementPanelStyleSetup] Scroll View not found in {scenePath}.");
            if (openedForSetup) EditorSceneManager.CloseScene(scene, true);
            return;
        }

        Transform controlsParent = scroll.parent;
        Transform existingBar = controlsParent.Find("AchievementFilterBar");
        GameObject filterBar = existingBar != null
            ? existingBar.gameObject
            : CreateUIObject("AchievementFilterBar", controlsParent, typeof(Image), typeof(Outline), typeof(ToggleGroup));

        ConfigureFilterBar(filterBar, font, out Toggle all, out Toggle complete,
            out Toggle incomplete, out TMP_Text count);

        RectTransform scrollRect = scroll as RectTransform;
        if (scrollRect != null)
        {
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(50f, 35f);
            scrollRect.offsetMax = new Vector2(-50f, -165f);
        }

        SerializedObject serializedManager = new SerializedObject(manager);
        serializedManager.FindProperty("allToggle").objectReferenceValue = all;
        serializedManager.FindProperty("completeToggle").objectReferenceValue = complete;
        serializedManager.FindProperty("incompleteToggle").objectReferenceValue = incomplete;
        serializedManager.FindProperty("unlockedCountText").objectReferenceValue = count;
        serializedManager.ApplyModifiedPropertiesWithoutUndo();

        manager.allAchievements = new List<AchievementSO>(achievements);

        PlayerDataManager dataManager = FindPlayerDataManagerInScene(scene);
        if (dataManager != null)
        {
            dataManager.allGameAchievements = new List<AchievementSO>(achievements);
            EditorUtility.SetDirty(dataManager);
        }

        manager.ApplyReferencePanelLayout();

        ApplyFontToPanel(manager.achievementPanel, font);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        if (openedForSetup)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static void ConfigureFilterBar(
        GameObject filterBar,
        TMP_FontAsset font,
        out Toggle all,
        out Toggle complete,
        out Toggle incomplete,
        out TMP_Text count)
    {
        RectTransform barRect = filterBar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.anchoredPosition = new Vector2(0f, -72f);
        barRect.sizeDelta = new Vector2(-100f, 76f);

        Image barImage = filterBar.GetComponent<Image>();
        barImage.color = new Color(0.73f, 0.67f, 0.55f, 0.97f);
        barImage.raycastTarget = false;

        Outline outline = filterBar.GetComponent<Outline>();
        outline.effectColor = new Color(0.30f, 0.20f, 0.14f, 0.65f);
        outline.effectDistance = new Vector2(2f, -2f);

        ToggleGroup group = filterBar.GetComponent<ToggleGroup>();
        group.allowSwitchOff = false;

        all = GetOrCreateToggle(filterBar.transform, "AllFilter", "All", group, font,
            new Vector2(18f, -10f), 170f);
        complete = GetOrCreateToggle(filterBar.transform, "CompleteFilter", "Complete", group, font,
            new Vector2(200f, -10f), 220f);
        incomplete = GetOrCreateToggle(filterBar.transform, "IncompleteFilter", "Incomplete", group, font,
            new Vector2(432f, -10f), 250f);

        all.SetIsOnWithoutNotify(true);
        complete.SetIsOnWithoutNotify(false);
        incomplete.SetIsOnWithoutNotify(false);

        Transform existingCount = filterBar.transform.Find("UnlockedCount");
        GameObject countObject = existingCount != null
            ? existingCount.gameObject
            : CreateUIObject("UnlockedCount", filterBar.transform, typeof(TextMeshProUGUI));
        count = countObject.GetComponent<TMP_Text>();
        count.text = "0/0 Unlocked";
        count.font = font;
        count.fontSize = 28f;
        count.fontStyle = FontStyles.Bold;
        count.color = new Color(0.20f, 0.13f, 0.09f, 1f);
        count.alignment = TextAlignmentOptions.MidlineRight;
        count.raycastTarget = false;

        RectTransform countRect = countObject.GetComponent<RectTransform>();
        countRect.anchorMin = new Vector2(1f, 0f);
        countRect.anchorMax = new Vector2(1f, 1f);
        countRect.pivot = new Vector2(1f, 0.5f);
        countRect.anchoredPosition = new Vector2(-24f, 0f);
        countRect.sizeDelta = new Vector2(300f, 0f);
    }

    private static Toggle GetOrCreateToggle(
        Transform parent,
        string objectName,
        string label,
        ToggleGroup group,
        TMP_FontAsset font,
        Vector2 topLeft,
        float width)
    {
        Transform existing = parent.Find(objectName);
        GameObject owner = existing != null
            ? existing.gameObject
            : CreateUIObject(objectName, parent, typeof(Image), typeof(Toggle));

        RectTransform rect = owner.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeft;
        rect.sizeDelta = new Vector2(width, 56f);

        Image background = owner.GetComponent<Image>();
        background.color = new Color(0.93f, 0.88f, 0.73f, 1f);
        background.raycastTarget = true;

        Toggle toggle = owner.GetComponent<Toggle>();
        toggle.enabled = true;
        toggle.interactable = true;
        toggle.group = group;
        toggle.targetGraphic = background;
        toggle.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = toggle.colors;
        colors.normalColor = new Color(0.93f, 0.88f, 0.73f, 1f);
        colors.highlightedColor = new Color(0.98f, 0.76f, 0.35f, 1f);
        colors.selectedColor = new Color(0.90f, 0.61f, 0.20f, 1f);
        colors.pressedColor = new Color(0.77f, 0.48f, 0.14f, 1f);
        toggle.colors = colors;

        Transform existingMark = FindDescendant(owner.transform, "Checkmark");
        GameObject markObject = existingMark != null
            ? existingMark.gameObject
            : CreateUIObject("Checkmark", owner.transform, typeof(Image));
        Image mark = markObject.GetComponent<Image>();
        mark.color = new Color(0.87f, 0.57f, 0.16f, 1f);
        mark.raycastTarget = false;
        RectTransform markRect = markObject.GetComponent<RectTransform>();
        markRect.anchorMin = markRect.anchorMax = new Vector2(0f, 0.5f);
        markRect.pivot = new Vector2(0f, 0.5f);
        markRect.anchoredPosition = new Vector2(13f, 0f);
        markRect.sizeDelta = new Vector2(34f, 34f);
        toggle.graphic = mark;

        Transform existingLabel = owner.transform.Find("Label");
        GameObject labelObject = existingLabel != null
            ? existingLabel.gameObject
            : CreateUIObject("Label", owner.transform, typeof(TextMeshProUGUI));
        TMP_Text text = labelObject.GetComponent<TMP_Text>();
        text.text = label;
        text.font = font;
        text.fontSize = 26f;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(0.20f, 0.13f, 0.09f, 1f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        RectTransform textRect = labelObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(56f, 0f);
        textRect.offsetMax = new Vector2(-8f, 0f);

        return toggle;
    }

    private static void StyleRowPrefab(TMP_FontAsset font)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RowPrefabPath);
        if (root == null) return;

        try
        {
            AchievementRowUI rowUI = root.GetComponent<AchievementRowUI>();
            if (rowUI != null) rowUI.ApplyReferenceLayout();

            Image background = root.GetComponent<Image>();
            if (background != null)
                background.color = new Color(1f, 0.965f, 0.84f, 0.96f);

            Outline outline = root.GetComponent<Outline>();
            if (outline == null) outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.24f, 0.15f, 0.50f);
            outline.effectDistance = new Vector2(2f, -2f);

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (font != null) text.font = font;
                text.color = new Color(0.20f, 0.13f, 0.09f, 1f);
            }

            Transform iconTransform = FindDescendant(root.transform, "Icon");
            if (iconTransform != null)
            {
                Image icon = iconTransform.GetComponent<Image>();
                if (icon != null) icon.preserveAspect = true;
            }

            Transform progressBackground = FindDescendant(root.transform, "ProgressBar_BG");
            if (progressBackground != null)
            {
                Image image = progressBackground.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color(0.52f, 0.42f, 0.27f, 1f);
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                }
            }

            Transform progressFill = FindDescendant(root.transform, "Progressbar_Fill");
            if (progressFill != null)
            {
                Image image = progressFill.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color(0.88f, 0.62f, 0.18f, 1f);
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, RowPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ApplyCurrentThemeOnce()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += ApplyCurrentThemeOnce;
            return;
        }

        AssetImporter rowImporter = AssetImporter.GetAtPath(RowPrefabPath);
        if (rowImporter != null && rowImporter.userData == ThemeVersion) return;
        StyleAchievementPanels();
    }

    private static void ApplyFontToPanel(GameObject panel, TMP_FontAsset font)
    {
        if (font == null) return;
        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
            text.font = font;
    }

    private static AchievementUIManager FindManagerInScene(Scene scene)
    {
        foreach (AchievementUIManager manager in Resources.FindObjectsOfTypeAll<AchievementUIManager>())
        {
            if (manager.gameObject.scene == scene)
                return manager;
        }
        return null;
    }

    private static PlayerDataManager FindPlayerDataManagerInScene(Scene scene)
    {
        foreach (PlayerDataManager manager in Resources.FindObjectsOfTypeAll<PlayerDataManager>())
        {
            if (manager.gameObject.scene == scene)
                return manager;
        }
        return null;
    }

    private static List<AchievementSO> LoadAchievementDatabase()
    {
        List<AchievementSO> achievements = new List<AchievementSO>();
        string[] guids = AssetDatabase.FindAssets("t:AchievementSO", new[] { AchievementFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AchievementSO achievement = AssetDatabase.LoadAssetAtPath<AchievementSO>(path);
            if (achievement != null)
                achievements.Add(achievement);
        }

        achievements.Sort((left, right) =>
            string.CompareOrdinal(left.achievementID, right.achievementID));
        return achievements;
    }

    private static void StyleManagersPrefab(List<AchievementSO> achievements)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ManagersPrefabPath);
        if (root == null) return;

        try
        {
            PlayerDataManager manager = root.GetComponentInChildren<PlayerDataManager>(true);
            if (manager != null)
            {
                manager.allGameAchievements = new List<AchievementSO>(achievements);
                EditorUtility.SetDirty(manager);
                PrefabUtility.SaveAsPrefabAsset(root, ManagersPrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        if (root.name == objectName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDescendant(root.GetChild(i), objectName);
            if (result != null) return result;
        }
        return null;
    }

    private static GameObject CreateUIObject(
        string objectName,
        Transform parent,
        params System.Type[] componentTypes)
    {
        GameObject result = new GameObject(objectName, typeof(RectTransform));
        result.layer = 5;
        result.transform.SetParent(parent, false);
        foreach (System.Type type in componentTypes)
        {
            if (type != typeof(RectTransform)) result.AddComponent(type);
        }
        return result;
    }
}
#endif
