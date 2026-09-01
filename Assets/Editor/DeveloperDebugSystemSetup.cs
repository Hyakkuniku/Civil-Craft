#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class DeveloperDebugSystemSetup
{
    private const string AutoSetupSessionKey = "CivilCraft.DeveloperDebugSystemSetup.V7";

    static DeveloperDebugSystemSetup()
    {
        EditorApplication.delayCall += TryAutoSetupCanyonCrossing;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryAutoSetupCanyonCrossing;
    }

    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.name == "CanyonCrossing")
            EditorApplication.delayCall += TryAutoSetupCanyonCrossing;
    }

    [MenuItem("Tools/Civil Craft/Setup Developer Debug Menu")]
    public static void SetupActiveSceneFromMenu()
    {
        SetupActiveScene(true);
    }

    /// <summary>Batch-mode entry point used by project tooling.</summary>
    public static void SetupCanyonCrossingAsset()
    {
        const string scenePath = "Assets/Scenes/CanyonCrossing.unity";
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
            throw new System.InvalidOperationException($"Could not open {scenePath}.");

        SetupActiveScene(true);
    }

    private static void TryAutoSetupCanyonCrossing()
    {
        if (SessionState.GetBool(AutoSetupSessionKey, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.name != "CanyonCrossing") return;

        SessionState.SetBool(AutoSetupSessionKey, true);

        DeveloperDebugManager existingManager = FindSceneComponent<DeveloperDebugManager>(scene);
        if (existingManager != null)
        {
            SerializedObject serializedManager = new SerializedObject(existingManager);
            SerializedProperty debugWindow = serializedManager.FindProperty("debugWindow");
            if (debugWindow != null && debugWindow.objectReferenceValue != null &&
                FindRecursive(existingManager.transform, "AutoCompleteTutorialButton") != null &&
                FindRecursive(existingManager.transform, "CompleteAllTutorialsButton") != null &&
                FindRecursive(existingManager.transform, "UnlockContractButton") != null &&
                FindRecursive(existingManager.transform, "UnlockAchievementButton") != null &&
                FindRecursive(existingManager.transform, "UnlockAllAchievementsButton") != null &&
                FindRecursive(existingManager.transform, "AddCoinsButton") != null &&
                DropdownTemplatesAreConfigured(existingManager))
                return;
        }

        SetupActiveScene(true);
    }

    private static void SetupActiveScene(bool saveScene)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogWarning("[DeveloperDebugSetup] Open a scene before running setup.");
            return;
        }

        DeveloperDebugManager manager = FindSceneComponent<DeveloperDebugManager>(scene);
        GameObject systemRoot = manager != null ? manager.gameObject : FindRoot(scene, "DeveloperDebugSystem");
        if (systemRoot == null)
        {
            systemRoot = new GameObject("DeveloperDebugSystem");
            SceneManager.MoveGameObjectToScene(systemRoot, scene);
        }

        systemRoot.name = "DeveloperDebugSystem";
        systemRoot.transform.SetParent(null, true);
        systemRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        systemRoot.transform.localScale = Vector3.one;

        manager = GetOrAdd<DeveloperDebugManager>(systemRoot);

        RectTransform canvasRect = GetOrCreateRect(systemRoot.transform, "DebugCanvas");
        Stretch(canvasRect);
        canvasRect.localScale = Vector3.one;
        Canvas canvas = GetOrAdd<Canvas>(canvasRect.gameObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = GetOrAdd<CanvasScaler>(canvasRect.gameObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        GetOrAdd<GraphicRaycaster>(canvasRect.gameObject);

        RectTransform window = GetOrCreateRect(canvasRect, "DebugWindow");
        Stretch(window);
        window.localScale = Vector3.one;
        CanvasGroup windowGroup = GetOrAdd<CanvasGroup>(window.gameObject);
        windowGroup.alpha = 1f;
        windowGroup.interactable = true;
        windowGroup.blocksRaycasts = true;

        RectTransform blocker = GetOrCreateRect(window, "ScreenBlocker");
        Stretch(blocker);
        Image blockerImage = GetOrAdd<Image>(blocker.gameObject);
        blockerImage.color = new Color(0.015f, 0.02f, 0.035f, 0.84f);
        blockerImage.raycastTarget = true;

        RectTransform panel = GetOrCreateRect(blocker, "DebugPanel");
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(1050f, 930f);
        panel.localScale = Vector3.one;
        Image panelImage = GetOrAdd<Image>(panel.gameObject);
        panelImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.055f, 0.075f, 0.11f, 0.99f);
        Outline outline = GetOrAdd<Outline>(panel.gameObject);
        outline.effectColor = new Color(0.18f, 0.62f, 0.82f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI title = GetOrCreateText(panel, "TitleText", "DEVELOPER DEBUG MENU", 42f, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.55f, 0.9f, 1f, 1f);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -12f), new Vector2(0f, 74f), new Vector2(0.5f, 1f));

        TextMeshProUGUI status = GetOrCreateText(panel, "StatusText", "F12 or ~ toggles this menu", 23f, FontStyles.Normal);
        status.alignment = TextAlignmentOptions.Left;
        status.color = new Color(0.72f, 0.82f, 0.9f, 1f);
        SetRect(status.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(34f, 24f), new Vector2(-250f, 54f), new Vector2(0f, 0f));

        RectTransform scrollRectTransform = GetOrCreateRect(panel, "ScrollView");
        scrollRectTransform.anchorMin = Vector2.zero;
        scrollRectTransform.anchorMax = Vector2.one;
        scrollRectTransform.offsetMin = new Vector2(30f, 92f);
        scrollRectTransform.offsetMax = new Vector2(-30f, -90f);
        Image scrollBackground = GetOrAdd<Image>(scrollRectTransform.gameObject);
        scrollBackground.sprite = BuiltinSprite("UI/Skin/Background.psd");
        scrollBackground.type = Image.Type.Sliced;
        scrollBackground.color = new Color(0.025f, 0.035f, 0.055f, 0.8f);
        ScrollRect scrollRect = GetOrAdd<ScrollRect>(scrollRectTransform.gameObject);
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 35f;

        RectTransform viewport = GetOrCreateRect(scrollRectTransform, "Viewport");
        Stretch(viewport, 10f, 10f, 10f, 10f);
        Image viewportImage = GetOrAdd<Image>(viewport.gameObject);
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
        viewportImage.raycastTarget = true;
        GetOrAdd<RectMask2D>(viewport.gameObject);

        RectTransform content = GetOrCreateRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup vertical = GetOrAdd<VerticalLayoutGroup>(content.gameObject);
        vertical.padding = new RectOffset(18, 18, 18, 18);
        vertical.spacing = 14f;
        vertical.childAlignment = TextAnchor.UpperCenter;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        ContentSizeFitter fitter = GetOrAdd<ContentSizeFitter>(content.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.viewport = viewport;
        scrollRect.content = content;

        TMP_Dropdown sceneDropdown = CreateSelectorRow(content, "SceneRow", "Scene", "LOAD SCENE", out Button loadScene);
        TMP_Dropdown tutorialDropdown = CreateSelectorRow(content, "TutorialRow", "Tutorial", "FORCE START", out Button startTutorial);
        RectTransform tutorialActions = EnsureRow(content, "TutorialActionsRow", 64f);
        TextMeshProUGUI tutorialActionsLabel = GetOrCreateText(tutorialActions, "Label", "Tutorial Actions", 24f, FontStyles.Bold);
        AddLayout(tutorialActionsLabel.gameObject, 190f, 54f, 0f);
        Button completeTutorial = GetOrCreateButton(
            tutorialActions, "AutoCompleteTutorialButton", "COMPLETE ACTIVE / SELECTED", 360f);
        Button completeAllTutorials = GetOrCreateButton(
            tutorialActions, "CompleteAllTutorialsButton", "COMPLETE ALL TUTORIALS", 370f);
        TMP_Dropdown locationDropdown = CreateSelectorRow(content, "BuildLocationRow", "Build Location", "TELEPORT PLAYER", out Button teleportPlayer);
        RectTransform contractActions = EnsureRow(content, "ContractActionsRow", 64f);
        TextMeshProUGUI contractActionsLabel = GetOrCreateText(contractActions, "Label", "Contract Actions", 24f, FontStyles.Bold);
        AddLayout(contractActionsLabel.gameObject, 190f, 54f, 0f);
        Button unlockContract = GetOrCreateButton(
            contractActions, "UnlockContractButton", "UNLOCK CONTRACT / BUILD LOCATION", 742f);
        TMP_Dropdown achievementDropdown = CreateSelectorRow(
            content, "AchievementRow", "Achievement", "UNLOCK SELECTED", out Button unlockAchievement);
        unlockAchievement.gameObject.name = "UnlockAchievementButton";
        RectTransform achievementActions = EnsureRow(content, "AchievementActionsRow", 64f);
        TextMeshProUGUI achievementActionsLabel = GetOrCreateText(
            achievementActions, "Label", "Achievement Actions", 24f, FontStyles.Bold);
        AddLayout(achievementActionsLabel.gameObject, 190f, 54f, 0f);
        Button unlockAllAchievements = GetOrCreateButton(
            achievementActions, "UnlockAllAchievementsButton", "UNLOCK ALL ACHIEVEMENTS", 742f);
        TMP_Dropdown phaseDropdown = CreateSelectorRow(content, "NPCPhaseRow", "NPC Phase", "WARP NPC", out Button teleportNpc);

        RectTransform coinsRow = EnsureRow(content, "CoinsRow", 64f);
        TextMeshProUGUI coinsLabel = GetOrCreateText(coinsRow, "Label", "Add Coins", 24f, FontStyles.Bold);
        AddLayout(coinsLabel.gameObject, 190f, 54f, 0f);
        TMP_Dropdown coinAmountDropdown = GetOrCreateDropdown(coinsRow, "CoinAmountDropdown");
        coinAmountDropdown.ClearOptions();
        coinAmountDropdown.AddOptions(new System.Collections.Generic.List<string>
        {
            "₱1,000",
            "₱10,000",
            "₱100,000",
            "₱1,000,000"
        });
        coinAmountDropdown.SetValueWithoutNotify(1);
        coinAmountDropdown.RefreshShownValue();
        AddLayout(coinAmountDropdown.gameObject, 520f, 54f, 1f);
        Button addCoins = GetOrCreateButton(coinsRow, "AddCoinsButton", "ADD COINS", 220f);

        RectTransform timeRow = EnsureRow(content, "TimeScaleRow", 64f);
        TextMeshProUGUI timeLabel = GetOrCreateText(timeRow, "TimeScaleText", "Time Scale: 1.00x", 24f, FontStyles.Bold);
        AddLayout(timeLabel.gameObject, 220f, 54f, 0f);
        Slider timeSlider = GetOrCreateSlider(timeRow, "TimeScaleSlider");
        timeSlider.minValue = 0f;
        timeSlider.maxValue = 5f;
        timeSlider.value = 1f;
        AddLayout(timeSlider.gameObject, 480f, 42f, 1f);
        Button resetTime = GetOrCreateButton(timeRow, "ResetTimeButton", "RESET", 160f);

        RectTransform toggleRow = EnsureRow(content, "InvincibleRow", 64f);
        TextMeshProUGUI godLabel = GetOrCreateText(toggleRow, "InvincibleLabel", "Bridge Testing", 24f, FontStyles.Bold);
        AddLayout(godLabel.gameObject, 220f, 54f, 0f);
        Toggle invincibleToggle = GetOrCreateToggle(toggleRow, "InvincibleBridgeToggle", "Invincible Bridge / God Mode");
        AddLayout(invincibleToggle.gameObject, 650f, 54f, 1f);

        RectTransform actionRow = EnsureRow(content, "ActionsRow", 70f);
        Button autoComplete = GetOrCreateButton(actionRow, "AutoCompleteButton", "AUTO-COMPLETE CONTRACT", 300f);
        Button refreshLists = GetOrCreateButton(actionRow, "RefreshListsButton", "REFRESH LISTS", 280f);
        Button clearSave = GetOrCreateButton(actionRow, "ClearSaveButton", "CLEAR SAVE DATA", 300f, true);

        TextMeshProUGUI hint = GetOrCreateText(content, "SafetyHint",
            "Clear Save requires two clicks within four seconds. Auto-complete still uses the normal bridge save validation.",
            19f, FontStyles.Italic);
        hint.alignment = TextAlignmentOptions.Center;
        hint.color = new Color(0.72f, 0.76f, 0.82f, 1f);
        AddLayout(hint.gameObject, -1f, 55f, 1f);

        Button close = GetOrCreateButton(panel, "CloseButton", "CLOSE", 190f);
        RectTransform closeRect = close.GetComponent<RectTransform>();
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 0f);
        closeRect.anchoredPosition = new Vector2(-30f, 22f);
        closeRect.sizeDelta = new Vector2(190f, 54f);

        WireButton(loadScene, manager.LoadSelectedScene);
        WireButton(startTutorial, manager.ForceStartSelectedTutorial);
        WireButton(completeTutorial, manager.AutoCompleteSelectedTutorial);
        WireButton(completeAllTutorials, manager.CompleteAllTutorials);
        WireButton(teleportPlayer, manager.TeleportPlayerToSelectedBuildLocation);
        WireButton(unlockContract, manager.UnlockSelectedBuildLocationContract);
        WireButton(unlockAchievement, manager.UnlockSelectedAchievement);
        WireButton(unlockAllAchievements, manager.UnlockAllAchievements);
        WireButton(teleportNpc, manager.TeleportNPCToSelectedPhase);
        WireButton(addCoins, manager.AddSelectedCoins);
        WireButton(resetTime, manager.ResetTimeScale);
        WireButton(autoComplete, manager.AutoCompleteCurrentContract);
        WireButton(refreshLists, manager.RefreshDropdownValues);
        WireButton(clearSave, manager.ClearAllLocalSaveData);
        WireButton(close, manager.CloseMenu);

        SerializedObject serializedManager = new SerializedObject(manager);
        Assign(serializedManager, "debugCanvas", canvas);
        Assign(serializedManager, "debugWindow", window.gameObject);
        Assign(serializedManager, "statusText", status);
        Assign(serializedManager, "timeScaleText", timeLabel);
        Assign(serializedManager, "sceneDropdown", sceneDropdown);
        Assign(serializedManager, "tutorialDropdown", tutorialDropdown);
        Assign(serializedManager, "buildLocationDropdown", locationDropdown);
        Assign(serializedManager, "npcPhaseDropdown", phaseDropdown);
        Assign(serializedManager, "achievementDropdown", achievementDropdown);
        Assign(serializedManager, "coinAmountDropdown", coinAmountDropdown);
        Assign(serializedManager, "timeScaleSlider", timeSlider);
        Assign(serializedManager, "invincibleBridgeToggle", invincibleToggle);
        serializedManager.ApplyModifiedPropertiesWithoutUndo();

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0) SetLayerRecursively(canvasRect.gameObject, uiLayer);
        window.gameObject.SetActive(true);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = systemRoot;
        Debug.Log($"[DeveloperDebugSetup] Developer debug menu created and wired in '{scene.name}'.", systemRoot);
    }

    private static TMP_Dropdown CreateSelectorRow(
        RectTransform content,
        string rowName,
        string labelText,
        string buttonText,
        out Button actionButton)
    {
        RectTransform row = EnsureRow(content, rowName, 64f);
        TextMeshProUGUI label = GetOrCreateText(row, "Label", labelText, 24f, FontStyles.Bold);
        AddLayout(label.gameObject, 190f, 54f, 0f);
        TMP_Dropdown dropdown = GetOrCreateDropdown(row, rowName.Replace("Row", "Dropdown"));
        AddLayout(dropdown.gameObject, 520f, 50f, 1f);
        actionButton = GetOrCreateButton(row, rowName.Replace("Row", "Button"), buttonText, 210f);
        return dropdown;
    }

    private static RectTransform EnsureRow(RectTransform parent, string name, float height)
    {
        RectTransform row = GetOrCreateRect(parent, name);
        HorizontalLayoutGroup layout = GetOrAdd<HorizontalLayoutGroup>(row.gameObject);
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        AddLayout(row.gameObject, -1f, height, 1f);
        return row;
    }

    private static TMP_Dropdown GetOrCreateDropdown(Transform parent, string name)
    {
        RectTransform root = GetOrCreateRect(parent, name);
        Image image = GetOrAdd<Image>(root.gameObject);
        image.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;
        image.color = new Color(0.1f, 0.14f, 0.2f, 1f);
        TMP_Dropdown dropdown = GetOrAdd<TMP_Dropdown>(root.gameObject);
        dropdown.targetGraphic = image;

        TextMeshProUGUI caption = GetOrCreateText(root, "Label", "Select...", 21f, FontStyles.Normal);
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = new Vector2(16f, 4f);
        caption.rectTransform.offsetMax = new Vector2(-48f, -4f);

        TextMeshProUGUI arrow = GetOrCreateText(root, "Arrow", "▼", 20f, FontStyles.Bold);
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.rectTransform.anchorMin = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchoredPosition = new Vector2(-10f, 0f);
        arrow.rectTransform.sizeDelta = new Vector2(36f, 36f);

        RectTransform template = GetOrCreateRect(root, "Template");
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0.5f, 1f);
        template.anchoredPosition = new Vector2(0f, -2f);
        template.sizeDelta = new Vector2(0f, 230f);
        Image templateImage = GetOrAdd<Image>(template.gameObject);
        templateImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        templateImage.type = Image.Type.Sliced;
        templateImage.color = new Color(0.065f, 0.085f, 0.13f, 1f);
        // TMP creates the visible popup by cloning this inactive template. Giving
        // the clone its own override-sorting canvas lets it escape the parent
        // debug ScrollView's RectMask2D and keeps it above the other debug UI.
        Canvas templateCanvas = GetOrAdd<Canvas>(template.gameObject);
        templateCanvas.overrideSorting = true;
        templateCanvas.sortingOrder = 32010;
        GetOrAdd<GraphicRaycaster>(template.gameObject);
        CanvasGroup templateGroup = GetOrAdd<CanvasGroup>(template.gameObject);
        templateGroup.alpha = 1f;
        templateGroup.interactable = true;
        templateGroup.blocksRaycasts = true;
        ScrollRect templateScroll = GetOrAdd<ScrollRect>(template.gameObject);
        templateScroll.horizontal = false;
        templateScroll.vertical = true;

        RectTransform viewport = GetOrCreateRect(template, "Viewport");
        Stretch(viewport, 4f, 4f, 4f, 4f);
        Image viewportImage = GetOrAdd<Image>(viewport.gameObject);
        viewportImage.color = Color.white;
        GetOrAdd<RectMask2D>(viewport.gameObject);

        RectTransform content = GetOrCreateRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, 36f);

        RectTransform item = GetOrCreateRect(content, "Item");
        item.anchorMin = new Vector2(0f, 0.5f);
        item.anchorMax = new Vector2(1f, 0.5f);
        item.sizeDelta = new Vector2(0f, 36f);
        Image itemBackground = GetOrAdd<Image>(item.gameObject);
        itemBackground.color = new Color(0.11f, 0.16f, 0.23f, 1f);
        Toggle toggle = GetOrAdd<Toggle>(item.gameObject);
        toggle.targetGraphic = itemBackground;

        RectTransform checkmarkRect = GetOrCreateRect(item, "Item Checkmark");
        checkmarkRect.anchorMin = checkmarkRect.anchorMax = new Vector2(0f, 0.5f);
        checkmarkRect.anchoredPosition = new Vector2(16f, 0f);
        checkmarkRect.sizeDelta = new Vector2(20f, 20f);
        Image checkmark = GetOrAdd<Image>(checkmarkRect.gameObject);
        checkmark.sprite = BuiltinSprite("UI/Skin/Checkmark.psd");
        checkmark.color = new Color(0.35f, 0.85f, 1f, 1f);
        toggle.graphic = checkmark;

        TextMeshProUGUI itemLabel = GetOrCreateText(item, "Item Label", "Option", 20f, FontStyles.Normal);
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        itemLabel.rectTransform.anchorMin = Vector2.zero;
        itemLabel.rectTransform.anchorMax = Vector2.one;
        itemLabel.rectTransform.offsetMin = new Vector2(36f, 2f);
        itemLabel.rectTransform.offsetMax = new Vector2(-8f, -2f);

        templateScroll.viewport = viewport;
        templateScroll.content = content;
        dropdown.template = template;
        dropdown.captionText = caption;
        dropdown.itemText = itemLabel;
        dropdown.ClearOptions();
        dropdown.options.Add(new TMP_Dropdown.OptionData("Select..."));
        template.gameObject.SetActive(false);
        return dropdown;
    }

    private static Slider GetOrCreateSlider(Transform parent, string name)
    {
        RectTransform root = GetOrCreateRect(parent, name);
        Slider slider = GetOrAdd<Slider>(root.gameObject);

        RectTransform background = GetOrCreateRect(root, "Background");
        background.anchorMin = new Vector2(0f, 0.35f);
        background.anchorMax = new Vector2(1f, 0.65f);
        background.offsetMin = new Vector2(8f, 0f);
        background.offsetMax = new Vector2(-8f, 0f);
        Image bgImage = GetOrAdd<Image>(background.gameObject);
        bgImage.color = new Color(0.09f, 0.12f, 0.17f, 1f);

        RectTransform fillArea = GetOrCreateRect(root, "Fill Area");
        Stretch(fillArea, 12f, 12f, 8f, 8f);
        RectTransform fill = GetOrCreateRect(fillArea, "Fill");
        Stretch(fill);
        Image fillImage = GetOrAdd<Image>(fill.gameObject);
        fillImage.color = new Color(0.16f, 0.7f, 0.95f, 1f);

        RectTransform handleArea = GetOrCreateRect(root, "Handle Slide Area");
        Stretch(handleArea, 12f, 12f, 0f, 0f);
        RectTransform handle = GetOrCreateRect(handleArea, "Handle");
        handle.sizeDelta = new Vector2(28f, 42f);
        Image handleImage = GetOrAdd<Image>(handle.gameObject);
        handleImage.sprite = BuiltinSprite("UI/Skin/Knob.psd");
        handleImage.color = new Color(0.65f, 0.92f, 1f, 1f);

        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static Toggle GetOrCreateToggle(Transform parent, string name, string labelText)
    {
        RectTransform root = GetOrCreateRect(parent, name);
        Toggle toggle = GetOrAdd<Toggle>(root.gameObject);

        RectTransform background = GetOrCreateRect(root, "Background");
        background.anchorMin = background.anchorMax = new Vector2(0f, 0.5f);
        background.anchoredPosition = new Vector2(24f, 0f);
        background.sizeDelta = new Vector2(38f, 38f);
        Image backgroundImage = GetOrAdd<Image>(background.gameObject);
        backgroundImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        backgroundImage.type = Image.Type.Sliced;
        backgroundImage.color = new Color(0.1f, 0.14f, 0.2f, 1f);

        RectTransform checkRect = GetOrCreateRect(background, "Checkmark");
        Stretch(checkRect, 7f, 7f, 7f, 7f);
        Image checkmark = GetOrAdd<Image>(checkRect.gameObject);
        checkmark.sprite = BuiltinSprite("UI/Skin/Checkmark.psd");
        checkmark.color = new Color(0.3f, 0.9f, 1f, 1f);

        TextMeshProUGUI label = GetOrCreateText(root, "Label", labelText, 23f, FontStyles.Normal);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(55f, 0f);
        label.rectTransform.offsetMax = Vector2.zero;

        toggle.targetGraphic = backgroundImage;
        toggle.graphic = checkmark;
        return toggle;
    }

    private static Button GetOrCreateButton(
        Transform parent,
        string name,
        string text,
        float preferredWidth,
        bool danger = false)
    {
        RectTransform root = GetOrCreateRect(parent, name);
        Image image = GetOrAdd<Image>(root.gameObject);
        image.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;
        image.color = danger
            ? new Color(0.55f, 0.12f, 0.15f, 1f)
            : new Color(0.08f, 0.42f, 0.58f, 1f);

        Button button = GetOrAdd<Button>(root.gameObject);
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.72f, 0.8f, 0.88f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        TextMeshProUGUI label = GetOrCreateText(root, "Text", text, 20f, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Center;
        Stretch(label.rectTransform, 8f, 8f, 4f, 4f);
        AddLayout(root.gameObject, preferredWidth, 52f, 0f);
        return button;
    }

    private static TextMeshProUGUI GetOrCreateText(
        Transform parent,
        string name,
        string text,
        float size,
        FontStyles style)
    {
        RectTransform rect = GetOrCreateRect(parent, name);
        TextMeshProUGUI label = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
        if (label.font == null) label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = Color.white;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform GetOrCreateRect(Transform parent, string name)
    {
        Transform existing = FindDirectChild(parent, name);
        if (existing != null)
        {
            existing.name = name;
            RectTransform existingRect = existing as RectTransform;
            if (existingRect != null) return existingRect;
        }

        GameObject created = new GameObject(name, typeof(RectTransform));
        RectTransform rect = created.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static Transform FindDirectChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.Trim() == name.Trim()) return child;
        }
        return null;
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform match = FindRecursive(root.transform, name);
            if (match != null) return match.gameObject;
        }
        return null;
    }

    private static Transform FindRecursive(Transform current, string name)
    {
        if (current.name.Trim() == name.Trim()) return current;
        for (int i = 0; i < current.childCount; i++)
        {
            Transform result = FindRecursive(current.GetChild(i), name);
            if (result != null) return result;
        }
        return null;
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        foreach (T component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component != null && component.gameObject.scene == scene) return component;
        }
        return null;
    }

    private static bool DropdownTemplatesAreConfigured(DeveloperDebugManager manager)
    {
        if (manager == null) return false;

        TMP_Dropdown[] dropdowns = manager.GetComponentsInChildren<TMP_Dropdown>(true);
        if (dropdowns.Length == 0) return false;

        foreach (TMP_Dropdown dropdown in dropdowns)
        {
            if (dropdown == null || dropdown.template == null) return false;

            GameObject template = dropdown.template.gameObject;
            Canvas canvas = template.GetComponent<Canvas>();
            CanvasGroup group = template.GetComponent<CanvasGroup>();
            if (canvas == null || !canvas.overrideSorting ||
                template.GetComponent<GraphicRaycaster>() == null ||
                group == null || !group.interactable || !group.blocksRaycasts)
                return false;
        }

        return true;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null) return;
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void AddLayout(GameObject target, float width, float height, float flexibleWidth)
    {
        LayoutElement layout = GetOrAdd<LayoutElement>(target);
        if (width >= 0f) layout.preferredWidth = width;
        layout.preferredHeight = height;
        layout.flexibleWidth = flexibleWidth;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float bottom = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 position,
        Vector2 size,
        Vector2 pivot)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static Sprite BuiltinSprite(string path)
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
    }

    private static void Assign(SerializedObject target, string propertyName, Object value)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property != null) property.objectReferenceValue = value;
    }

    private static void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, action);
        EditorUtility.SetDirty(button);
    }
}
#endif
