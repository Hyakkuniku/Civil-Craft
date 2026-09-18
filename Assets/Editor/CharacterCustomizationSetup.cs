#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class CharacterCustomizationSetup
{
    // Keep the wardrobe source of truth beside the shop data.  The previous
    // Resources path no longer exists after the shop/category migration and
    // caused a setup rerun to create a second, disconnected set of assets.
    private const string DefinitionFolder = "Assets/BridgeBuilder/Data/Cosmetics";
    private const string PanelPrefabPath = "Assets/Prefabs/UI/CharacterCustomizationPanel.prefab";
    private const string PreviewPrefabPath = "Assets/Resources/Loading/NewCharacterPreview.prefab";
    private const string ManagersPrefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    private sealed class Spec
    {
        public string id;
        public string label;
        public CosmeticCategory category;
        public string[] modelNames;
        public bool defaultUnlocked;
        public bool defaultWhenEmpty;
    }

    private static readonly Spec[] Specs =
    {
        new Spec { id="Accessory_None", label="None", category=CosmeticCategory.Accessories, modelNames=new string[0], defaultUnlocked=true, defaultWhenEmpty=true },
        new Spec { id="EngineeringHardHat", label="Engineering Hard Hat", category=CosmeticCategory.Accessories, modelNames=new[]{"Base_Hard Hat"} },
        new Spec { id="Accessory_SmallCap", label="Small Cap", category=CosmeticCategory.Accessories, modelNames=new[]{"Accesories_Cap_Small"} },
        new Spec { id="Accessory_LargeCap", label="Large Cap", category=CosmeticCategory.Accessories, modelNames=new[]{"Accesories_Cap_Large"} },
        new Spec { id="Accessory_SafetyVest", label="Safety Vest", category=CosmeticCategory.Accessories, modelNames=new[]{"Accesories_Vest"} },

        new Spec { id="Hair_Base", label="Classic Hair", category=CosmeticCategory.Hair, modelNames=new[]{"Base_Hair.Front", "Base_Hair.Back"}, defaultUnlocked=true, defaultWhenEmpty=true },
        new Spec { id="Hair_1", label="Side Sweep", category=CosmeticCategory.Hair, modelNames=new[]{"Hair_1"} },
        new Spec { id="Hair_2", label="Short Layers", category=CosmeticCategory.Hair, modelNames=new[]{"Hair_2"} },
        new Spec { id="Hair_4", label="Bob Cut", category=CosmeticCategory.Hair, modelNames=new[]{"Hair_4"} },
        new Spec { id="Hair_5", label="Wavy Hair", category=CosmeticCategory.Hair, modelNames=new[]{"Hair_5"} },
        new Spec { id="Hair_6", label="Crew Cut", category=CosmeticCategory.Hair, modelNames=new[]{"Hair_6"} },
        new Spec { id="Hair_8", label="Curly Hair", category=CosmeticCategory.Hair, modelNames=new[]{"Hair_8"} },

        new Spec { id="Shirt_Base", label="Builder Tee", category=CosmeticCategory.Shirt, modelNames=new[]{"Base_Tshirt"}, defaultUnlocked=true, defaultWhenEmpty=true },
        new Spec { id="Shirt_Blouse", label="Blouse", category=CosmeticCategory.Shirt, modelNames=new[]{"Top_Blouse"} },
        new Spec { id="Shirt_Polo", label="Polo", category=CosmeticCategory.Shirt, modelNames=new[]{"Top_Polo"} },
        new Spec { id="Shirt_Tee1", label="Work Tee", category=CosmeticCategory.Shirt, modelNames=new[]{"Top_tshirt_1"} },
        new Spec { id="Shirt_Tee2", label="Pocket Tee", category=CosmeticCategory.Shirt, modelNames=new[]{"Top_tshirt_2"} },

        new Spec { id="Pants_Base", label="Builder Pants", category=CosmeticCategory.Pants, modelNames=new[]{"Base_Pants"}, defaultUnlocked=true, defaultWhenEmpty=true },
        new Spec { id="Pants_Straight", label="Straight Pants", category=CosmeticCategory.Pants, modelNames=new[]{"Bottom_Pants"} },
        new Spec { id="Pants_Cargo", label="Cargo Pants", category=CosmeticCategory.Pants, modelNames=new[]{"Bottom_CargoPants"} },
        new Spec { id="Pants_Rolled", label="Rolled Pants", category=CosmeticCategory.Pants, modelNames=new[]{"Bottom_RolledUp_Pants"} },
        new Spec { id="Pants_Skirt", label="Work Skirt", category=CosmeticCategory.Pants, modelNames=new[]{"Bottom_Skirt"} },

        new Spec { id="Shoes_Base", label="Builder Shoes", category=CosmeticCategory.Shoes, modelNames=new[]{"Base_SHOE"}, defaultUnlocked=true, defaultWhenEmpty=true },
        new Spec { id="Shoes_Sandals", label="Sandals", category=CosmeticCategory.Shoes, modelNames=new[]{"Shoes_Sandals"} },
        new Spec { id="Shoes_Boots1", label="Work Boots", category=CosmeticCategory.Shoes, modelNames=new[]{"Shoes_Boots_1"} },
        new Spec { id="Shoes_Boots2", label="Heavy Boots", category=CosmeticCategory.Shoes, modelNames=new[]{"Shoes_Boots_2"} },
        new Spec { id="Shoes_Black", label="Black Shoes", category=CosmeticCategory.Shoes, modelNames=new[]{"Shoes_Black Shoes"} }
    };

    private static readonly Color Paper = new Color32(246, 231, 198, 255);
    private static readonly Color Cream = new Color32(255, 246, 222, 255);
    private static readonly Color Brown = new Color32(111, 61, 31, 255);
    private static readonly Color Orange = new Color32(232, 133, 33, 255);
    private static readonly Color Muted = new Color32(205, 174, 128, 255);

    [MenuItem("GameObject/Civil Craft/Setup Character Customization", false, 42)]
    public static void SetupAll()
    {
        EnsureFolder(DefinitionFolder);
        EnsureFolder(Path.GetDirectoryName(PanelPrefabPath).Replace('\\', '/'));
        List<CosmeticDefinition> definitions = EnsureDefinitions();
        CreatePanelPrefab(definitions);
        ConfigurePreviewPrefab();
        ConfigureManagersPrefab();
        ConfigureScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CharacterCustomizationSetup] Wardrobe data, prefab, Almanac panels, player bindings, and preview bindings are ready.");
    }

    public static List<CosmeticDefinition> EnsureDefinitions()
    {
        Color[] cloth = { Color.clear, new Color32(75, 55, 44, 255), new Color32(48, 73, 107, 255), new Color32(188, 74, 50, 255), new Color32(232, 159, 40, 255), new Color32(92, 128, 83, 255) };
        Color[] hair = { Color.clear, new Color32(65, 43, 34, 255), new Color32(102, 59, 32, 255), new Color32(40, 33, 31, 255), new Color32(160, 94, 50, 255), new Color32(229, 186, 90, 255) };
        List<CosmeticDefinition> result = new List<CosmeticDefinition>();
        foreach (Spec spec in Specs)
        {
            string path = $"{DefinitionFolder}/{spec.id}.asset";
            CosmeticDefinition definition = AssetDatabase.LoadAssetAtPath<CosmeticDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<CosmeticDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.EditorConfigure(spec.id, spec.label, spec.category,
                string.Join("|", spec.modelNames), spec.defaultUnlocked,
                spec.category == CosmeticCategory.Hair ? hair : cloth);
            result.Add(definition);
        }
        return result;
    }

    private static void CreatePanelPrefab(List<CosmeticDefinition> definitions)
    {
        GameObject root = UIObject("CharacterCustomizationPanel", null);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        Image background = root.AddComponent<Image>();
        background.color = Paper;
        CanvasGroup customizationGroup = root.AddComponent<CanvasGroup>();
        customizationGroup.alpha = 0f;
        customizationGroup.interactable = false;
        customizationGroup.blocksRaycasts = false;

        CharacterCustomizationController controller = root.AddComponent<CharacterCustomizationController>();
        TMP_FontAsset font = FindFont();
        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        GameObject shade = Panel("Backdrop", root.transform, new Color32(72, 43, 27, 95), uiSprite);
        Stretch(shade.GetComponent<RectTransform>());
        shade.transform.SetAsFirstSibling();

        GameObject shell = Panel("WardrobeBook", root.transform, Paper, uiSprite);
        SetRect(shell.GetComponent<RectTransform>(), 0.5f, 0.5f, 0.5f, 0.5f, 0, 0, 1840, 980);

        TMP_Text title = Text("Title", shell.transform, "CUSTOMIZE YOUR BUILDER", 48, font, Brown, TextAlignmentOptions.Center);
        SetRect(title.rectTransform, 0.5f, 1, 0.5f, 1, 0, -45, 1100, 72);
        title.fontStyle = FontStyles.Bold;

        GameObject left = Panel("PhotoboothFrame", shell.transform, Cream, uiSprite);
        SetRect(left.GetComponent<RectTransform>(), 0, 0, 0, 0, 30, 145, 650, 730);
        RectTransform portraitAnchor = UIObject("CustomizationPortraitAnchor", left.transform)
            .GetComponent<RectTransform>();
        SetRect(portraitAnchor, 0.5f, 0.5f, 0.5f, 0.5f, 0, 22, 620, 418);

        TMP_Text rotateHint = Text("RotateHint", left.transform, "DRAG TO ROTATE", 21, font, Brown, TextAlignmentOptions.Center);
        SetRect(rotateHint.rectTransform, 0.5f, 0, 0.5f, 0, 0, 30, 300, 34);

        GameObject right = Panel("WardrobeOptions", shell.transform, new Color32(251, 239, 211, 255), uiSprite);
        SetRect(right.GetComponent<RectTransform>(), 1, 0, 1, 0, -30, 145, 1100, 730);

        TMP_Text categoryTitle = Text("CategoryTitle", right.transform, "ACCESSORIES", 30, font, Brown, TextAlignmentOptions.Center);
        SetRect(categoryTitle.rectTransform, 0.5f, 1, 0.5f, 1, 0, -150, 440, 40);

        List<CosmeticCategoryTabBinding> tabs = new List<CosmeticCategoryTabBinding>();
        string[] tabLabels = { "ACCESSORIES", "HAIR", "SHIRT", "PANTS", "SHOES" };
        for (int i = 0; i < tabLabels.Length; i++)
        {
            GameObject tabObject = Panel($"{tabLabels[i]}Tab", right.transform, Cream, uiSprite);
            SetRect(tabObject.GetComponent<RectTransform>(), 0, 1, 0, 1, 20 + i * 212, -56, 202, 86);
            Button tabButton = tabObject.AddComponent<Button>();
            tabButton.targetGraphic = tabObject.GetComponent<Image>();
            TMP_Text tabText = Text("Label", tabObject.transform, tabLabels[i], 19, font, Brown, TextAlignmentOptions.Center);
            Stretch(tabText.rectTransform, 8, 8, 8, 8);
            GameObject selected = Panel("Selected", tabObject.transform, Orange, uiSprite);
            Stretch(selected.GetComponent<RectTransform>(), 4, 4, 4, 4);
            selected.transform.SetAsFirstSibling();
            tabs.Add(new CosmeticCategoryTabBinding { category=(CosmeticCategory)i, button=tabButton, selectedVisual=selected });
        }

        GameObject grid = UIObject("CosmeticGrid", right.transform);
        SetRect(grid.GetComponent<RectTransform>(), 0.5f, 0.5f, 0.5f, 0.5f, 0, -2, 1020, 390);
        GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(245, 180);
        layout.spacing = new Vector2(13, 15);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 4;
        layout.childAlignment = TextAnchor.UpperCenter;

        List<CosmeticOptionButton> options = new List<CosmeticOptionButton>();
        for (int i = 0; i < 8; i++) options.Add(CreateOptionSlot(grid.transform, i + 1, font, uiSprite));

        TMP_Text colorLabel = Text("ColorLabel", right.transform, "COLOR", 24, font, Brown, TextAlignmentOptions.Center);
        SetRect(colorLabel.rectTransform, 0.5f, 0, 0.5f, 0, 0, 105, 180, 38);

        List<CosmeticColorButtonBinding> colors = new List<CosmeticColorButtonBinding>();
        for (int i = 0; i < 8; i++)
        {
            GameObject swatch = Panel($"Color_{i + 1}", right.transform, Color.white, uiSprite);
            SetRect(swatch.GetComponent<RectTransform>(), 0.5f, 0, 0.5f, 0, -294 + i * 84, 34, 62, 62);
            Button button = swatch.AddComponent<Button>();
            button.targetGraphic = swatch.GetComponent<Image>();
            GameObject selected = CreateBorder("SelectedOutline", swatch.transform, Color.white, 4f, 5f);
            if (i == 0)
            {
                TMP_Text defaultLabel = Text("DefaultLabel", swatch.transform, "D", 22, font, Brown, TextAlignmentOptions.Center);
                Stretch(defaultLabel.rectTransform);
                defaultLabel.fontStyle = FontStyles.Bold;
            }
            colors.Add(new CosmeticColorButtonBinding { button=button, colorImage=swatch.GetComponent<Image>(), selectedVisual=selected });
        }

        Button cancel = CreateActionButton(shell.transform, "CancelButton", "CANCEL", new Color32(250, 240, 215, 255), Brown, font, uiSprite);
        SetRect(cancel.GetComponent<RectTransform>(), 1, 0, 1, 0, -500, 35, 430, 82);
        Button save = CreateActionButton(shell.transform, "SaveLookButton", "SAVE LOOK", Orange, Color.white, font, uiSprite);
        SetRect(save.GetComponent<RectTransform>(), 1, 0, 1, 0, -35, 35, 430, 82);
        UnityEventTools.AddPersistentListener(cancel.onClick, controller.CancelCustomization);
        UnityEventTools.AddPersistentListener(save.onClick, controller.SaveLook);

        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("customizationPanel").objectReferenceValue = root;
        serialized.FindProperty("customizationPortraitAnchor").objectReferenceValue = portraitAnchor;
        serialized.FindProperty("customizationGroup").objectReferenceValue = customizationGroup;
        serialized.FindProperty("categoryTitle").objectReferenceValue = categoryTitle;
        AssignObjectList(serialized.FindProperty("cosmetics"), definitions.Cast<UnityEngine.Object>().ToList());
        AssignTabs(serialized.FindProperty("categoryTabs"), tabs);
        AssignObjectList(serialized.FindProperty("optionSlots"), options.Cast<UnityEngine.Object>().ToList());
        AssignColors(serialized.FindProperty("colorSlots"), colors);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);
        PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static CosmeticOptionButton CreateOptionSlot(Transform parent, int index, TMP_FontAsset font, Sprite sprite)
    {
        GameObject card = Panel($"CosmeticOption_{index}", parent, Cream, sprite);
        Button button = card.AddComponent<Button>();
        button.targetGraphic = card.GetComponent<Image>();
        CosmeticOptionButton option = card.AddComponent<CosmeticOptionButton>();

        Image icon = UIObject("Icon", card.transform).AddComponent<Image>();
        SetRect(icon.rectTransform, 0.5f, 1, 0.5f, 1, 0, -67, 105, 95);
        TMP_Text label = Text("Label", card.transform, "COSMETIC", 19, font, Brown, TextAlignmentOptions.Center);
        SetRect(label.rectTransform, 0.5f, 0, 0.5f, 0, 0, 27, 190, 55);

        GameObject selected = Panel("SelectedBorder", card.transform, new Color32(232, 133, 33, 70), sprite);
        Stretch(selected.GetComponent<RectTransform>(), 3, 3, 3, 3);
        selected.transform.SetAsFirstSibling();
        GameObject locked = Panel("Locked", card.transform, new Color32(78, 55, 42, 170), sprite);
        Stretch(locked.GetComponent<RectTransform>());
        TMP_Text lockText = Text("LockLabel", locked.transform, "LOCKED", 22, font, Color.white, TextAlignmentOptions.Center);
        Stretch(lockText.rectTransform);

        SerializedObject serialized = new SerializedObject(option);
        serialized.FindProperty("button").objectReferenceValue = button;
        serialized.FindProperty("icon").objectReferenceValue = icon;
        serialized.FindProperty("label").objectReferenceValue = label;
        serialized.FindProperty("selectedVisual").objectReferenceValue = selected;
        serialized.FindProperty("lockedVisual").objectReferenceValue = locked;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return option;
    }

    private static void ConfigurePreviewPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PreviewPrefabPath);
        if (root == null) return;
        PlayerCosmeticMirror mirror = root.GetComponent<PlayerCosmeticMirror>() ?? root.AddComponent<PlayerCosmeticMirror>();
        ConfigureBindings(mirror, root.transform);
        PrefabUtility.SaveAsPrefabAsset(root, PreviewPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static void ConfigureManagersPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ManagersPrefabPath);
        if (root == null) return;
        ConfigureHierarchy(root);
        PrefabUtility.SaveAsPrefabAsset(root, ManagersPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static void ConfigureScenes()
    {
        string previous = SceneManager.GetActiveScene().path;
        foreach (string path in ScenePaths)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects()) ConfigureHierarchy(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        if (!string.IsNullOrWhiteSpace(previous) && File.Exists(previous))
            EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
    }

    private static void ConfigureHierarchy(GameObject root)
    {
        foreach (PlayerCosmetics cosmetics in root.GetComponentsInChildren<PlayerCosmetics>(true))
        {
            Transform visual = Find(cosmetics.transform, "NewCharacterModel") ?? cosmetics.transform;
            ConfigureBindings(cosmetics, visual);
        }

        // Materialize this list before ConfigureAlmanac replaces the previous
        // panel; otherwise LINQ may continue iterating destroyed descendants.
        Transform[] canvases = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t != null && t.name == "AlmanacCanvas")
            .ToArray();
        foreach (Transform canvas in canvases)
            ConfigureAlmanac(canvas);
    }

    private static void ConfigureAlmanac(Transform canvas)
    {
        Transform existing = Find(canvas, "CharacterCustomizationPanel");
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        Transform oldProfileAnchor = Find(canvas, "ProfilePortraitAnchor");
        if (oldProfileAnchor != null) UnityEngine.Object.DestroyImmediate(oldProfileAnchor.gameObject);
        Transform oldTransitionLayer = Find(canvas, "CharacterCustomizationTransitionLayer");
        if (oldTransitionLayer != null) UnityEngine.Object.DestroyImmediate(oldTransitionLayer.gameObject);

        GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
        GameObject panel = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, canvas);
        panel.name = "CharacterCustomizationPanel";
        panel.SetActive(false);

        CharacterCustomizationController controller = panel.GetComponent<CharacterCustomizationController>();
        PlayerCosmeticMirror mirror = canvas.GetComponentInChildren<PlayerCosmeticMirror>(true);
        if (mirror == null)
        {
            Transform visual = Find(canvas, "NewCharacterModel (Almanac)") ?? Find(canvas, "NewCharacterModel");
            if (visual != null) mirror = visual.gameObject.AddComponent<PlayerCosmeticMirror>();
        }
        if (mirror != null) ConfigureBindings(mirror, mirror.transform);

        Transform almanacPanel = Find(canvas, "AlmanacPanel");
        AlmanacPortraitRotator sourceRotator = canvas.GetComponentsInChildren<AlmanacPortraitRotator>(true)
            .FirstOrDefault(item => !item.transform.IsChildOf(panel.transform));
        if (sourceRotator == null)
        {
            RenderTexture portraitTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(
                "Assets/Materials/Render/New Render Texture.renderTexture");
            RawImage profilePortrait = canvas.GetComponentsInChildren<RawImage>(true)
                .FirstOrDefault(image => image != null && image.texture == portraitTexture &&
                                         !image.transform.IsChildOf(panel.transform));
            if (profilePortrait != null)
            {
                sourceRotator = profilePortrait.GetComponent<AlmanacPortraitRotator>();
                if (sourceRotator == null)
                    sourceRotator = profilePortrait.gameObject.AddComponent<AlmanacPortraitRotator>();

                SerializedObject rotatorData = new SerializedObject(sourceRotator);
                rotatorData.FindProperty("rotationTarget").objectReferenceValue = mirror != null ? mirror.transform : null;
                Camera portraitCamera = mirror != null
                    ? mirror.GetComponentInChildren<Camera>(true)
                    : null;
                rotatorData.FindProperty("cameraToKeepFixed").objectReferenceValue =
                    portraitCamera != null ? portraitCamera.transform : null;
                rotatorData.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        RectTransform sharedPortrait = sourceRotator != null
            ? sourceRotator.transform as RectTransform
            : null;
        if (sourceRotator != null && sourceRotator.RotationTarget != null)
        {
            mirror = sourceRotator.RotationTarget.GetComponentInChildren<PlayerCosmeticMirror>(true);
            if (mirror != null) ConfigureBindings(mirror, mirror.transform);
        }
        RectTransform profileAnchor = null;
        if (sharedPortrait != null)
        {
            profileAnchor = UIObject("ProfilePortraitAnchor", sharedPortrait.parent)
                .GetComponent<RectTransform>();
            CopyRectTransform(sharedPortrait, profileAnchor);
            profileAnchor.SetSiblingIndex(sharedPortrait.GetSiblingIndex());
        }

        RectTransform transitionLayer = UIObject("CharacterCustomizationTransitionLayer", canvas)
            .GetComponent<RectTransform>();
        Stretch(transitionLayer);
        CanvasGroup transitionCanvasGroup = transitionLayer.gameObject.AddComponent<CanvasGroup>();
        transitionCanvasGroup.interactable = false;
        transitionCanvasGroup.blocksRaycasts = false;
        transitionLayer.SetAsLastSibling();
        transitionLayer.gameObject.SetActive(false);

        RectTransform customizationAnchor = Find(panel.transform, "CustomizationPortraitAnchor") as RectTransform;
        CanvasGroup customizationGroup = panel.GetComponent<CanvasGroup>();
        List<CanvasGroup> profileGroups = new List<CanvasGroup>();
        foreach (string profileName in new[] { "ProfilePageDesign", "ProfileStatsDesign" })
        {
            Transform profileRoot = Find(canvas, profileName);
            if (profileRoot == null) continue;
            CanvasGroup group = profileRoot.GetComponent<CanvasGroup>();
            if (group == null)
                group = profileRoot.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            if (!profileGroups.Contains(group)) profileGroups.Add(group);
        }

        SerializedObject controllerData = new SerializedObject(controller);
        controllerData.FindProperty("previewMirror").objectReferenceValue = mirror;
        controllerData.FindProperty("almanacPanel").objectReferenceValue = almanacPanel != null ? almanacPanel.gameObject : null;
        controllerData.FindProperty("sharedPortrait").objectReferenceValue = sharedPortrait;
        controllerData.FindProperty("profilePortraitAnchor").objectReferenceValue = profileAnchor;
        controllerData.FindProperty("customizationPortraitAnchor").objectReferenceValue = customizationAnchor;
        controllerData.FindProperty("transitionLayer").objectReferenceValue = transitionLayer;
        controllerData.FindProperty("customizationGroup").objectReferenceValue = customizationGroup;
        AssignObjectList(controllerData.FindProperty("profileGroups"), profileGroups.Cast<UnityEngine.Object>().ToList());
        controllerData.ApplyModifiedPropertiesWithoutUndo();
        if (!controller.ResolvePreviewMirror())
            Debug.LogError("[CharacterCustomizationSetup] Could not resolve the shared photobooth mirror.", controller);
        EditorUtility.SetDirty(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

        Button edit = Find(canvas, "Edit")?.GetComponent<Button>();
        if (edit == null) edit = CreateEditButton(canvas, sourceRotator != null ? sourceRotator.transform : almanacPanel);
        if (edit != null)
        {
            for (int i = edit.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                if (edit.onClick.GetPersistentTarget(i) is CharacterCustomizationController)
                    UnityEventTools.RemovePersistentListener(edit.onClick, i);
            }
            UnityEventTools.AddPersistentListener(edit.onClick, controller.OpenCustomization);
            EditorUtility.SetDirty(edit);
        }
    }

    private static Button CreateEditButton(Transform canvas, Transform near)
    {
        Transform parent = near != null ? near.parent : canvas;
        GameObject editObject = Panel("Edit", parent, Cream,
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"));
        SetRect(editObject.GetComponent<RectTransform>(), 1, 1, 1, 1, -54, -54, 92, 70);
        Button button = editObject.AddComponent<Button>();
        button.targetGraphic = editObject.GetComponent<Image>();
        TMP_Text label = Text("Label", editObject.transform, "EDIT", 20, FindFont(), Brown, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        return button;
    }

    public static void ConfigureBindings(PlayerCosmetics target, Transform root)
    {
        target.cosmeticBindings = BuildBindings(root);
        EditorUtility.SetDirty(target);
    }

    public static void ConfigureBindings(PlayerCosmeticMirror target, Transform root)
    {
        target.cosmeticBindings = BuildBindings(root);
        EditorUtility.SetDirty(target);
    }

    public static List<CosmeticModelBinding> BuildBindings(Transform root)
    {
        List<CosmeticModelBinding> bindings = new List<CosmeticModelBinding>();
        foreach (Spec spec in Specs)
        {
            CosmeticModelBinding binding = new CosmeticModelBinding
            {
                cosmeticID = spec.id,
                category = spec.category,
                defaultWhenEmpty = spec.defaultWhenEmpty,
                models = new List<GameObject>()
            };
            HashSet<GameObject> unique = new HashSet<GameObject>();
            foreach (string modelName in spec.modelNames)
            {
                Transform found = Find(root, modelName);
                if (found != null && unique.Add(found.gameObject)) binding.models.Add(found.gameObject);
            }
            bindings.Add(binding);
        }
        return bindings;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root == null) return null;
        string wanted = name.Trim();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name.Trim(), wanted, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    private static GameObject UIObject(string name, Transform parent)
    {
        GameObject value = new GameObject(name, typeof(RectTransform));
        if (parent != null) value.transform.SetParent(parent, false);
        return value;
    }

    private static GameObject Panel(string name, Transform parent, Color color, Sprite sprite)
    {
        GameObject value = UIObject(name, parent);
        Image image = value.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        return value;
    }

    private static GameObject CreateBorder(string name, Transform parent, Color color,
        float thickness, float expansion)
    {
        GameObject border = UIObject(name, parent);
        Stretch(border.GetComponent<RectTransform>(), -expansion, -expansion, -expansion, -expansion);

        CreateBorderSide("Top", border.transform, color,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, thickness));
        CreateBorderSide("Bottom", border.transform, color,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, thickness));
        CreateBorderSide("Left", border.transform, color,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f),
            new Vector2(thickness, 0f));
        CreateBorderSide("Right", border.transform, color,
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(thickness, 0f));
        return border;
    }

    private static void CreateBorderSide(string name, Transform parent, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        Image side = UIObject(name, parent).AddComponent<Image>();
        side.color = color;
        side.raycastTarget = false;
        RectTransform rect = side.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;
    }

    private static TMP_Text Text(string name, Transform parent, string text, float size,
        TMP_FontAsset font, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI value = UIObject(name, parent).AddComponent<TextMeshProUGUI>();
        value.text = text;
        value.fontSize = size;
        value.font = font;
        value.color = color;
        value.alignment = alignment;
        value.enableAutoSizing = true;
        value.fontSizeMin = Mathf.Max(12, size * 0.65f);
        value.fontSizeMax = size;
        value.raycastTarget = false;
        return value;
    }

    private static Button CreateActionButton(Transform parent, string name, string label,
        Color background, Color foreground, TMP_FontAsset font, Sprite sprite)
    {
        GameObject value = Panel(name, parent, background, sprite);
        Button button = value.AddComponent<Button>();
        button.targetGraphic = value.GetComponent<Image>();
        TMP_Text text = Text("Label", value.transform, label, 30, font, foreground, TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        Stretch(text.rectTransform, 10, 10, 8, 8);
        return button;
    }

    private static void SetRect(RectTransform rect, float anchorX, float anchorY,
        float pivotX, float pivotY, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(anchorX, anchorY);
        rect.pivot = new Vector2(pivotX, pivotY);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void CopyRectTransform(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
    }

    private static void Stretch(RectTransform rect, float left=0, float right=0, float bottom=0, float top=0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static TMP_FontAsset FindFont()
    {
        List<TMP_FontAsset> fonts = AssetDatabase.FindAssets("t:TMP_FontAsset")
            .Select(guid => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(font => font != null).ToList();
        return fonts.FirstOrDefault(font => font.name.IndexOf("bekind", StringComparison.OrdinalIgnoreCase) >= 0)
               ?? fonts.FirstOrDefault();
    }

    private static void AssignObjectList(SerializedProperty property, List<UnityEngine.Object> values)
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void AssignTabs(SerializedProperty property, List<CosmeticCategoryTabBinding> values)
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
        {
            SerializedProperty item = property.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("category").enumValueIndex = (int)values[i].category;
            item.FindPropertyRelative("button").objectReferenceValue = values[i].button;
            item.FindPropertyRelative("selectedVisual").objectReferenceValue = values[i].selectedVisual;
        }
    }

    private static void AssignColors(SerializedProperty property, List<CosmeticColorButtonBinding> values)
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
        {
            SerializedProperty item = property.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("button").objectReferenceValue = values[i].button;
            item.FindPropertyRelative("colorImage").objectReferenceValue = values[i].colorImage;
            item.FindPropertyRelative("selectedVisual").objectReferenceValue = values[i].selectedVisual;
        }
    }

    private static void EnsureFolder(string folder)
    {
        string normalized = folder.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(normalized)) return;
        string parent = Path.GetDirectoryName(normalized).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(normalized));
    }
}
#endif
