#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Creates the leaderboard once as editable objects in Mode Selection.</summary>
[InitializeOnLoad]
public static class LeaderboardPanelAuthoring
{
    private const string ScenePath = "Assets/Scenes/Mode Selection.unity";
    private const string PanelName = "Leaderboard Panel";
    private const string FontPath =
        "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset";
    private const string ContractFolder = "Assets/BridgeBuilder/Data/Contract";

    private static readonly Color32 Brown = new Color32(75, 50, 36, 255);
    private static readonly Color32 Cream = new Color32(255, 249, 238, 255);
    private static readonly Color32 Amber = new Color32(239, 166, 49, 255);
    private static readonly Color32 SoftAmber = new Color32(255, 231, 176, 255);
    private static TMP_FontAsset font;
    private static Sprite rounded;

    static LeaderboardPanelAuthoring()
    {
        EditorApplication.delayCall += TryAuthorOnce;
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryAuthorOnce;
        };
    }

    [MenuItem("Tools/Civil Craft/Author Mode Selection Leaderboard")]
    public static void AuthorFromMenu() => AuthorScene();

    private static void TryAuthorOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating) return;
        AuthorScene();
    }

    private static void AuthorScene()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        if (!scene.IsValid() || !scene.isLoaded) return;

        bool wasDirty = scene.isDirty;
        try
        {
            Canvas canvas = FindInScene<Canvas>(scene, c => c.renderMode != RenderMode.WorldSpace);
            if (canvas == null) throw new InvalidOperationException("Mode Selection canvas not found.");
            Transform existingPanel = canvas.transform.Find(PanelName);
            if (existingPanel != null)
            {
                // Migrate only the exact generated v0 panel once. Later user edits
                // are intentionally left alone on future domain reloads.
                LeaderboardPanelUI existingController = canvas.GetComponent<LeaderboardPanelUI>();
                if (existingController != null && UpgradeGeneratedPanel(scene, existingPanel,
                    existingController))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!wasDirty || opened) EditorSceneManager.SaveScene(scene);
                }
                return;
            }

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) ?? TMP_Settings.defaultFontAsset;
            Transform existingCard = FindDescendant(canvas.transform, "Session Choice Card");
            rounded = existingCard != null ? existingCard.GetComponent<Image>()?.sprite : null;

            RectTransform canvasRect = canvas.transform as RectTransform;
            LeaderboardPanelUI controller = canvas.GetComponent<LeaderboardPanelUI>();
            if (controller == null) controller = canvas.gameObject.AddComponent<LeaderboardPanelUI>();

            RectTransform buttonSafeArea = CreateRect("Leaderboard Button Safe Area", canvasRect);
            Stretch(buttonSafeArea, 0f, 0f, 0f, 0f);
            buttonSafeArea.gameObject.AddComponent<CompletionSafeArea>();
            Button open = CreateButton(buttonSafeArea, "Open Leaderboards Button",
                "LEADERBOARDS", new Vector2(1f, 1f), new Vector2(-36f, -116f),
                new Vector2(230f, 68f), Amber, 26f);
            RectTransform openRect = (RectTransform)open.transform;
            openRect.pivot = new Vector2(1f, 1f);

            RectTransform overlay = CreateRect(PanelName, canvasRect);
            Stretch(overlay, 0f, 0f, 0f, 0f);
            Image dim = overlay.gameObject.AddComponent<Image>();
            dim.color = new Color(0.07f, 0.045f, 0.035f, 0.70f);
            dim.raycastTarget = true;
            overlay.gameObject.AddComponent<CanvasGroup>();

            RectTransform panelSafeArea = CreateRect("Leaderboard Panel Safe Area", overlay);
            Stretch(panelSafeArea, 0f, 0f, 0f, 0f);
            panelSafeArea.gameObject.AddComponent<CompletionSafeArea>();
            RectTransform card = CreateRect("Leaderboard Card", panelSafeArea);
            card.anchorMin = new Vector2(0.18f, 0.06f);
            card.anchorMax = new Vector2(0.82f, 0.94f);
            card.offsetMin = Vector2.zero;
            card.offsetMax = Vector2.zero;
            AddImage(card, Brown, true);

            RectTransform inner = CreateRect("Cream Surface", card);
            Stretch(inner, 7f, 7f, 7f, 7f);
            AddImage(inner, Cream, false);

            RectTransform titleTab = CreateRect("Leaderboard Title Tab", card);
            Place(titleTab, new Vector2(0.5f, 1f), new Vector2(0f, 17f),
                new Vector2(550f, 95f));
            AddImage(titleTab, Brown, false);
            RectTransform titleFill = CreateRect("Title Fill", titleTab);
            Stretch(titleFill, 5f, 5f, 5f, 5f);
            AddImage(titleFill, SoftAmber, false);
            CreateTextAnchored(titleFill, "Title", "LEADERBOARDS", 53f, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one);

            Button close = CreateButton(card, "Close Leaderboards Button", "×",
                new Vector2(1f, 1f), new Vector2(-25f, -27f),
                new Vector2(75f, 72f), SoftAmber, 48f);
            ((RectTransform)close.transform).pivot = Vector2.one;

            TMP_Dropdown dropdown = CreateContractDropdown(card);
            ToggleGroup group = card.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            Toggle efficient = CreateRankingToggle(card, "Most Efficient Toggle",
                "Most Efficient", new Vector2(-280f, -205f), group, true);
            Toggle strongest = CreateRankingToggle(card, "Strongest Toggle",
                "Strongest", new Vector2(210f, -205f), group, false);

            TMP_Text description = CreateTextPlaced(card, "Ranking Description",
                "Lowest cost among successful bridges", 31f,
                TextAlignmentOptions.Center, new Vector2(0f, -275f), new Vector2(970f, 46f));
            description.color = new Color32(112, 85, 68, 255);

            RectTransform header = CreateRect("Column Header", card);
            Place(header, new Vector2(0.5f, 1f), new Vector2(0f, -338f),
                new Vector2(1040f, 58f));
            AddImage(header, new Color32(235, 204, 166, 255), false);
            CreateCell(header, "Rank Header", "#", 0f, .11f, 34f);
            CreateCell(header, "Builder Header", "BUILDER", .12f, .53f, 34f);
            CreateCell(header, "Cost Header", "COST", .55f, .75f, 34f);
            CreateCell(header, "Stress Header", "PEAK STRESS", .76f, .99f, 34f);

            RectTransform viewport = CreateRect("Leaderboard Viewport", card);
            viewport.anchorMin = new Vector2(.075f, .22f);
            viewport.anchorMax = new Vector2(.915f, .61f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect("Top 15 Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 5f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LeaderboardRowUI[] rows = new LeaderboardRowUI[15];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = CreateRow(content, i);

            RectTransform scrollbarRect = CreateRect("Leaderboard Scrollbar", card);
            scrollbarRect.anchorMin = new Vector2(.922f, .22f);
            scrollbarRect.anchorMax = new Vector2(.936f, .61f);
            scrollbarRect.offsetMin = Vector2.zero;
            scrollbarRect.offsetMax = Vector2.zero;
            Image scrollbarTrack = AddImage(scrollbarRect, new Color32(219, 195, 173, 255), false);
            RectTransform sliding = CreateRect("Sliding Area", scrollbarRect);
            Stretch(sliding, 0f, 0f, 0f, 0f);
            RectTransform handle = CreateRect("Handle", sliding);
            Stretch(handle, 1f, 1f, 1f, 1f);
            Image handleImage = AddImage(handle, Brown, false);
            Scrollbar scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.value = 1f;

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            TMP_Text status = CreateTextPlaced(card, "Leaderboard Status", string.Empty,
                36f, TextAlignmentOptions.Center, new Vector2(0f, -550f),
                new Vector2(960f, 110f));
            status.raycastTarget = false;

            TMP_Text personalBest = CreateTextPlaced(card, "Personal Best",
                "YOUR BEST  —  No successful saved bridge yet", 32f,
                TextAlignmentOptions.Center, new Vector2(0f, -790f),
                new Vector2(1020f, 52f));
            CreateTextPlaced(card, "Top 15 Footer", "TOP 15  •  SCROLL TO VIEW ALL",
                29f, TextAlignmentOptions.Center, new Vector2(0f, -845f),
                new Vector2(800f, 40f));

            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("authoredLayoutVersion").intValue = 4;
            Set(serialized, "panel", overlay.gameObject);
            Set(serialized, "openButton", open);
            Set(serialized, "closeButton", close);
            Set(serialized, "contractDropdown", dropdown);
            Set(serialized, "efficientToggle", efficient);
            Set(serialized, "strongestToggle", strongest);
            Set(serialized, "descriptionText", description);
            Set(serialized, "statusText", status);
            Set(serialized, "personalBestText", personalBest);
            Set(serialized, "leaderboardScroll", scroll);
            SerializedProperty rowArray = serialized.FindProperty("rows");
            rowArray.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
                rowArray.GetArrayElementAtIndex(i).objectReferenceValue = rows[i];

            SerializedProperty catalog = serialized.FindProperty("contracts");
            string[] contractGuids = AssetDatabase.FindAssets("t:ContractSO", new[] { ContractFolder });
            List<ContractSO> contractAssets = new List<ContractSO>();
            foreach (string guid in contractGuids)
            {
                ContractSO contract = AssetDatabase.LoadAssetAtPath<ContractSO>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (contract != null && !contract.hideFromLeaderboard)
                    contractAssets.Add(contract);
            }
            contractAssets.Sort((a, b) => string.Compare(a.name, b.name,
                StringComparison.OrdinalIgnoreCase));
            catalog.arraySize = contractAssets.Count;
            for (int i = 0; i < contractAssets.Count; i++)
                catalog.GetArrayElementAtIndex(i).objectReferenceValue = contractAssets[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SetAuthoredDropdownOptions(dropdown, contractAssets);

            overlay.SetAsLastSibling();
            overlay.gameObject.SetActive(false);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty || opened)
                EditorSceneManager.SaveScene(scene);
            else
                Debug.LogWarning("[Leaderboard] Mode Selection had unsaved edits. The panel is authored in memory; save the scene to keep it.");
            Debug.Log("[Leaderboard] Authored the top-15 leaderboard panel in Mode Selection.");
        }
        catch (Exception exception)
        {
            Debug.LogError("[Leaderboard] Scene authoring failed: " + exception);
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static bool UpgradeGeneratedPanel(
        Scene scene, Transform panel, LeaderboardPanelUI controller)
    {
        SerializedObject serialized = new SerializedObject(controller);
        SerializedProperty version = serialized.FindProperty("authoredLayoutVersion");
        if (version == null || version.intValue >= 4) return false;

        foreach (string toggleName in new[] { "Most Efficient Toggle", "Strongest Toggle" })
        {
            Transform toggleTransform = FindDescendant(panel, toggleName);
            Transform mark = toggleTransform != null ? FindDescendant(toggleTransform, "Mark") : null;
            Toggle toggle = toggleTransform != null ? toggleTransform.GetComponent<Toggle>() : null;
            if (mark == null || toggle == null) continue;
            // The old root had no raycast graphic, so tapping its text did nothing.
            if (toggleTransform.GetComponent<Image>() == null)
            {
                Image hitArea = toggleTransform.gameObject.AddComponent<Image>();
                hitArea.color = new Color(1f, 1f, 1f, 0f);
                hitArea.raycastTarget = true;
            }
            // The selection tint must not also drive the checkbox image:
            // LeaderboardToggleStyle owns its orange/cream state exclusively.
            toggle.transition = Selectable.Transition.None;
            TMP_Text unsupportedGlyph = mark.GetComponent<TMP_Text>();
            if (unsupportedGlyph == null || unsupportedGlyph.text != "✓") continue;
            UnityEngine.Object.DestroyImmediate(unsupportedGlyph);
            Image checkmark = mark.gameObject.AddComponent<Image>();
            checkmark.sprite = FindCheckmarkSprite(scene);
            checkmark.color = Color.white;
            checkmark.preserveAspect = true;
            checkmark.raycastTarget = false;
            toggle.graphic = checkmark;
            EditorUtility.SetDirty(toggle);
        }

        foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label.GetComponentInParent<LeaderboardRowUI>() != null)
                label.fontSize = 34f;
            else
            {
                switch (label.name)
                {
                    case "Title": label.fontSize = 53f; break;
                    case "Ranking Description": label.fontSize = 31f; break;
                    case "Leaderboard Status": label.fontSize = 36f; break;
                    case "Personal Best": label.fontSize = 32f; break;
                    case "Top 15 Footer": label.fontSize = 29f; break;
                    case "Rank Header":
                    case "Builder Header":
                    case "Cost Header":
                    case "Stress Header": label.fontSize = 34f; break;
                }
            }
            EditorUtility.SetDirty(label);
        }

        foreach (string toggleName in new[] { "Most Efficient Toggle", "Strongest Toggle" })
        {
            Transform toggle = FindDescendant(panel, toggleName);
            TMP_Text label = toggle != null ? toggle.Find("Label")?.GetComponent<TMP_Text>() : null;
            if (label != null) label.fontSize = 39f;
        }
        TMP_Dropdown dropdown = panel.GetComponentInChildren<TMP_Dropdown>(true);
        if (dropdown != null)
        {
            if (dropdown.captionText != null) dropdown.captionText.fontSize = 37f;
            if (dropdown.itemText != null) dropdown.itemText.fontSize = 31f;
            List<ContractSO> authoredContracts = new List<ContractSO>();
            SerializedProperty catalog = serialized.FindProperty("contracts");
            for (int i = 0; catalog != null && i < catalog.arraySize; i++)
            {
                ContractSO contract = catalog.GetArrayElementAtIndex(i).objectReferenceValue as ContractSO;
                if (contract != null && !contract.hideFromLeaderboard)
                    authoredContracts.Add(contract);
            }
            SetAuthoredDropdownOptions(dropdown, authoredContracts);
        }

        version.intValue = 4;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
        Debug.Log("[Leaderboard] Updated generated checkbox artwork and mobile text sizing.");
        return true;
    }

    private static void SetAuthoredDropdownOptions(TMP_Dropdown dropdown,
        List<ContractSO> contractAssets)
    {
        if (dropdown == null) return;
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        foreach (ContractSO contract in contractAssets)
            options.Add(new TMP_Dropdown.OptionData(contract.name));
        if (options.Count == 0)
            options.Add(new TMP_Dropdown.OptionData("No contracts available"));
        dropdown.options = options;
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
        EditorUtility.SetDirty(dropdown);
    }

    private static Sprite FindCheckmarkSprite(Scene scene)
    {
        Image existing = FindInScene<Image>(scene,
            image => image.name == "Checkmark" && image.sprite != null);
        return existing != null ? existing.sprite :
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
    }

    private static TMP_Dropdown CreateContractDropdown(RectTransform card)
    {
        TMP_Dropdown existing = FindInScene<TMP_Dropdown>(card.gameObject.scene,
            item => item.name == "QualityDropdown");
        if (existing == null)
            throw new InvalidOperationException("The existing QualityDropdown template is missing.");
        TMP_Dropdown dropdown = UnityEngine.Object.Instantiate(existing, card);
        dropdown.name = "Contract Dropdown";
        RectTransform rect = (RectTransform)dropdown.transform;
        rect.localScale = Vector3.one;
        Place(rect, new Vector2(.5f, 1f), new Vector2(0f, -100f),
            new Vector2(830f, 76f));
        LayoutElement element = dropdown.GetComponent<LayoutElement>();
        if (element != null) UnityEngine.Object.DestroyImmediate(element);
        Image image = dropdown.GetComponent<Image>();
        if (image != null) image.color = Cream;
        if (dropdown.captionText != null)
        {
            dropdown.captionText.font = font;
            dropdown.captionText.fontSize = 37f;
            dropdown.captionText.color = Brown;
            dropdown.captionText.text = "Choose a contract";
        }
        if (dropdown.itemText != null)
        {
            dropdown.itemText.font = font;
            dropdown.itemText.fontSize = 31f;
            dropdown.itemText.color = Brown;
        }
        dropdown.options = new List<TMP_Dropdown.OptionData> {
            new TMP_Dropdown.OptionData("Choose a contract")
        };
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private static Toggle CreateRankingToggle(RectTransform card, string name, string label,
        Vector2 position, ToggleGroup group, bool selected)
    {
        RectTransform root = CreateRect(name, card);
        Place(root, new Vector2(.5f, 1f), position, new Vector2(430f, 72f));
        Image hitArea = root.gameObject.AddComponent<Image>();
        hitArea.color = new Color(1f, 1f, 1f, 0f);
        hitArea.raycastTarget = true;
        RectTransform box = CreateRect("Checkbox", root);
        Place(box, new Vector2(0f, .5f), new Vector2(34f, 0f), new Vector2(61f, 61f));
        Image background = AddImage(box, selected ? Amber : Cream, true);
        RectTransform mark = CreateRect("Checkmark", box);
        Stretch(mark, 2f, 2f, 2f, 2f);
        Image check = mark.gameObject.AddComponent<Image>();
        check.sprite = FindCheckmarkSprite(card.gameObject.scene);
        check.color = Color.white;
        check.preserveAspect = true;
        check.raycastTarget = false;
        CreateTextAnchored(root, "Label", label, 39f, TextAlignmentOptions.MidlineLeft,
            new Vector2(.24f, 0f), new Vector2(.98f, 1f));
        Toggle toggle = root.gameObject.AddComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.targetGraphic = background;
        toggle.graphic = check;
        toggle.group = group;
        toggle.SetIsOnWithoutNotify(selected);
        LeaderboardToggleStyle style = root.gameObject.AddComponent<LeaderboardToggleStyle>();
        SerializedObject styleSettings = new SerializedObject(style);
        Set(styleSettings, "checkbox", background);
        styleSettings.ApplyModifiedPropertiesWithoutUndo();
        return toggle;
    }

    private static LeaderboardRowUI CreateRow(RectTransform content, int index)
    {
        RectTransform row = CreateRect("Leaderboard Row " + (index + 1).ToString("00"), content);
        row.sizeDelta = new Vector2(0f, 65f);
        LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 65f;
        Image image = AddImage(row, index < 3 ? SoftAmber : Cream, false);
        TMP_Text rank = CreateCell(row, "Rank", (index + 1).ToString(), 0f, .11f, 34f);
        TMP_Text builder = CreateCell(row, "Builder", index == 2 ? "You" : "Builder",
            .12f, .53f, 34f);
        TMP_Text cost = CreateCell(row, "Cost", "—", .55f, .75f, 34f);
        TMP_Text stress = CreateCell(row, "Peak Stress", "—", .76f, .99f, 34f);
        LeaderboardRowUI view = row.gameObject.AddComponent<LeaderboardRowUI>();
        SerializedObject serialized = new SerializedObject(view);
        Set(serialized, "rankText", rank);
        Set(serialized, "builderText", builder);
        Set(serialized, "costText", cost);
        Set(serialized, "stressText", stress);
        Set(serialized, "background", image);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    private static TMP_Text CreateCell(RectTransform parent, string name, string value,
        float minX, float maxX, float size)
    {
        return CreateTextAnchored(parent, name, value, size, TextAlignmentOptions.MidlineLeft,
            new Vector2(minX, 0f), new Vector2(maxX, 1f));
    }

    private static Button CreateButton(RectTransform parent, string name, string text,
        Vector2 anchor, Vector2 position, Vector2 size, Color fill, float fontSize)
    {
        RectTransform rect = CreateRect(name, parent);
        Place(rect, anchor, position, size);
        Image image = AddImage(rect, fill, true);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        CreateTextAnchored(rect, "Label", text, fontSize, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one);
        return button;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = 5;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static Image AddImage(RectTransform rect, Color color, bool raycast)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = rounded;
        image.type = rounded != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static TMP_Text CreateTextAnchored(RectTransform parent, string name, string value,
        float size, TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = Brown;
        text.alignment = alignment;
        text.text = value;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static TMP_Text CreateTextPlaced(RectTransform parent, string name, string value,
        float size, TextAlignmentOptions alignment, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = CreateRect(name, parent);
        Place(rect, new Vector2(.5f, 1f), position, dimensions);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = Brown;
        text.alignment = alignment;
        text.text = value;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static void Stretch(RectTransform rect, float left, float bottom,
        float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void Place(RectTransform rect, Vector2 anchor,
        Vector2 position, Vector2 dimensions)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
    }

    private static void Set(SerializedObject serialized, string property, UnityEngine.Object value)
    {
        serialized.FindProperty(property).objectReferenceValue = value;
    }

    private static T FindInScene<T>(Scene scene, Predicate<T> match = null) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true))
                if (match == null || match(component)) return component;
        return null;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDescendant(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
