#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class MainMenuSettingsSetup
{
    private const string ScenePath = "Assets/Scenes/Main Menu.unity";
    private const string GeneratedRootName = "SettingsContentRoot";
    private const string BekindFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset";

    private static readonly Color Ink = Hex("4B3022");
    private static readonly Color Cream = Hex("F7F0DE");
    private static readonly Color CreamAlternate = Hex("EEE3CC");
    private static readonly Color Gold = Hex("D69A3E");
    private static readonly Color Taupe = Hex("B8AA93");
    private static readonly Color Track = Hex("6B4934");

    static MainMenuSettingsSetup()
    {
        EditorApplication.delayCall += CloseStrayMainMenuScene;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            CloseStrayMainMenuScene();

    }

    private static void CloseStrayMainMenuScene()
    {
        if (EditorApplication.isPlaying)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        Scene mainMenuScene = SceneManager.GetSceneByPath(ScenePath);
        if (!activeScene.IsValid() || activeScene.path == ScenePath ||
            !mainMenuScene.IsValid() || !mainMenuScene.isLoaded)
        {
            return;
        }

        // The settings generator may temporarily open Main Menu additively. Never
        // let that utility scene leak into Play Mode over CanyonCrossing/BHAN HOUSE.
        if (mainMenuScene.isDirty)
        {
            Debug.LogWarning(
                "Main Menu is loaded additively and has unsaved edits. Save or close it before entering Play Mode; " +
                "the active gameplay scene was left unchanged.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.isPlaying = false;
            return;
        }

        SceneManager.SetActiveScene(activeScene);
        EditorSceneManager.CloseScene(mainMenuScene, true);
    }

    [MenuItem("Tools/Civil Craft/Setup Mobile Settings In All Scenes")]
    public static void SetupFromMenu()
    {
        SetupSceneAsset(true);
    }

    private static bool SetupSceneAsset(bool logWhenAlreadyConfigured)
    {
        Scene previousActive = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedAdditively = !scene.IsValid() || !scene.isLoaded;

        if (openedAdditively)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        bool changed = EnsureSetup(scene);
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Created and wired the mobile Main Menu settings tabs.");
        }
        else if (logWhenAlreadyConfigured)
        {
            Debug.Log("Main Menu settings are already configured.");
        }

        GameObject sourcePanel = FindGameObject(scene, "SettingsPanel");
        SettingsManager sourceManager = FindInScene<SettingsManager>(scene);
        int integratedScenes = IntegrateIntoBuildScenes(sourcePanel, sourceManager);
        if (integratedScenes > 0)
            Debug.Log($"Integrated the mobile settings panel into {integratedScenes} additional scene(s).");

        if (previousActive.IsValid() && previousActive.isLoaded)
            SceneManager.SetActiveScene(previousActive);

        bool mainMenuWasUtilityScene = previousActive.IsValid() && previousActive.path != ScenePath;
        if ((openedAdditively || mainMenuWasUtilityScene) && scene.IsValid() && scene.isLoaded && !scene.isDirty)
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        return true;
    }

    private static bool EnsureSetup(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return false;

        GameObject settingsPanel = FindGameObject(scene, "SettingsPanel");
        SettingsManager manager = FindInScene<SettingsManager>(scene);
        if (settingsPanel == null || manager == null)
        {
            Debug.LogError("Main Menu settings setup requires SettingsPanel and SettingsManager.");
            return false;
        }

        Transform container = FindChildRecursive(settingsPanel.transform, "ContainerBG");
        if (container == null)
        {
            Debug.LogError("SettingsPanel is missing its existing ContainerBG artwork.", settingsPanel);
            return false;
        }

        Transform existing = FindChildRecursive(container, GeneratedRootName);
        if (existing != null)
        {
            // V1 contained a desktop-only fullscreen row. Rebuild only our generated
            // content, leaving the user's frame, colors, sprites, and close artwork intact.
            if (FindChildRecursive(existing, "FullscreenRow") != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }
            else
            {
                bool fontChanged = ApplyBekindFont(settingsPanel);
                WireManagerFromPanel(manager, settingsPanel, scene);
                settingsPanel.SetActive(false);
                return fontChanged;
            }
        }

        RectTransform root = CreateRect(container, GeneratedRootName);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(86f, 70f);
        root.offsetMax = new Vector2(-86f, -150f);
        root.SetSiblingIndex(Mathf.Min(1, container.childCount - 1));

        SettingsTabController tabs = root.gameObject.AddComponent<SettingsTabController>();

        RectTransform tabsRow = CreateRect(root, "TabsRow");
        SetTopStrip(tabsRow, 72f);
        HorizontalLayoutGroup tabLayout = tabsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 12f;
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = true;
        tabLayout.childForceExpandHeight = true;

        string[] tabNames = { "GRAPHICS", "AUDIO", "GAMEPLAY", "ACCOUNT" };
        Button[] tabButtons = new Button[tabNames.Length];
        for (int i = 0; i < tabNames.Length; i++)
            tabButtons[i] = CreateButton(tabsRow, tabNames[i] + "TabButton", tabNames[i], Taupe, 25f);

        RectTransform pageBackground = CreateRect(root, "PageBackground");
        pageBackground.anchorMin = Vector2.zero;
        pageBackground.anchorMax = Vector2.one;
        pageBackground.offsetMin = new Vector2(0f, 88f);
        pageBackground.offsetMax = new Vector2(0f, -84f);
        Image pageImage = pageBackground.gameObject.AddComponent<Image>();
        pageImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        pageImage.type = Image.Type.Sliced;
        pageImage.color = new Color(Cream.r, Cream.g, Cream.b, 0.97f);

        RectTransform graphicsPage = CreatePage(pageBackground, "GraphicsPage");
        TMP_Dropdown quality = CreateDropdown(
            CreateSettingRow(graphicsPage, "QualityRow", "Quality Preset", false),
            "QualityDropdown",
            new List<string>(QualitySettings.names));
        Toggle shadows = CreateToggle(
            CreateSettingRow(graphicsPage, "ShadowsRow", "Detailed Shadows", true),
            "ShadowsToggle");
        TMP_Dropdown frameRate = CreateDropdown(
            CreateSettingRow(graphicsPage, "FrameRateRow", "Framerate Cap", false),
            "FrameRateDropdown",
            new List<string> { "30 FPS", "60 FPS", "90 FPS", "120 FPS" });

        RectTransform audioPage = CreatePage(pageBackground, "AudioPage");
        Toggle mute = CreateToggle(
            CreateSettingRow(audioPage, "MuteRow", "Mute All Audio", false),
            "GlobalMuteToggle");
        Slider master = CreateValueSlider(
            CreateSettingRow(audioPage, "MasterVolumeRow", "Master Volume", true),
            "MasterVolumeSlider", out TextMeshProUGUI masterValue);
        Slider music = CreateValueSlider(
            CreateSettingRow(audioPage, "MusicVolumeRow", "Music Volume", false),
            "MusicVolumeSlider", out TextMeshProUGUI musicValue);
        Slider sfx = CreateValueSlider(
            CreateSettingRow(audioPage, "SFXVolumeRow", "Sound Effects", true),
            "SFXVolumeSlider", out TextMeshProUGUI sfxValue);

        RectTransform gameplayPage = CreatePage(pageBackground, "GameplayPage");
        Toggle haptics = CreateToggle(
            CreateSettingRow(gameplayPage, "HapticsRow", "Haptic Feedback", false),
            "HapticsToggle");
        Slider sensitivity = CreateValueSlider(
            CreateSettingRow(gameplayPage, "CameraSensitivityRow", "Touch Camera Sensitivity", true),
            "CameraSensitivitySlider", out TextMeshProUGUI sensitivityValue, 0.5f, 2f);
        Toggle invertLook = CreateToggle(
            CreateSettingRow(gameplayPage, "InvertLookRow", "Invert Vertical Camera", false),
            "InvertLookYToggle");

        RectTransform accountPage = CreatePage(pageBackground, "AccountPage");
        TextMeshProUGUI accountStatus = CreateText(
            accountPage, "AccountStatusText",
            "Not signed in\nSign in to use your PlayFab account.", 30f, FontStyles.Normal);
        accountStatus.alignment = TextAlignmentOptions.Center;
        accountStatus.color = Ink;
        LayoutElement statusLayout = accountStatus.gameObject.AddComponent<LayoutElement>();
        statusLayout.preferredHeight = 190f;
        statusLayout.flexibleWidth = 1f;

        RectTransform accountActions = CreateRect(accountPage, "AccountActions");
        HorizontalLayoutGroup accountLayout = accountActions.gameObject.AddComponent<HorizontalLayoutGroup>();
        accountLayout.padding = new RectOffset(100, 100, 10, 10);
        accountLayout.spacing = 30f;
        accountLayout.childAlignment = TextAnchor.MiddleCenter;
        accountLayout.childControlWidth = true;
        accountLayout.childControlHeight = true;
        accountLayout.childForceExpandWidth = true;
        accountLayout.childForceExpandHeight = true;
        LayoutElement accountActionsLayout = accountActions.gameObject.AddComponent<LayoutElement>();
        accountActionsLayout.preferredHeight = 82f;
        Button accountAction = CreateButton(accountActions, "AccountActionButton", "SIGN IN", Gold, 25f);
        Button logout = CreateButton(accountActions, "LogoutButton", "LOG OUT", Taupe, 25f);
        TextMeshProUGUI accountActionText = accountAction.GetComponentInChildren<TextMeshProUGUI>();

        RectTransform footer = CreateRect(root, "Footer");
        footer.anchorMin = new Vector2(0f, 0f);
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = Vector2.zero;
        footer.sizeDelta = new Vector2(0f, 70f);
        HorizontalLayoutGroup footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 36f;
        footerLayout.childAlignment = TextAnchor.MiddleCenter;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = true;
        footerLayout.childForceExpandHeight = true;
        Button restore = CreateButton(footer, "RestoreDefaultsButton", "RESTORE COZY DEFAULTS", Taupe, 24f);
        Button apply = CreateButton(footer, "ApplySettingsButton", "APPLY & CLOSE", Gold, 24f);

        GameObject[] pages =
        {
            graphicsPage.gameObject,
            audioPage.gameObject,
            gameplayPage.gameObject,
            accountPage.gameObject
        };
        tabs.tabPanels = pages;
        tabs.tabButtons = tabButtons;
        tabs.activeTabColor = Gold;
        tabs.inactiveTabColor = Taupe;
        for (int i = 0; i < pages.Length; i++) pages[i].SetActive(i == 0);

        PlayFabAuthManager auth = FindInScene<PlayFabAuthManager>(scene);
        SerializedObject serializedManager = new SerializedObject(manager);
        Assign(serializedManager, "settingsPanel", settingsPanel);
        Assign(serializedManager, "globalMuteToggle", mute);
        Assign(serializedManager, "masterVolumeSlider", master);
        Assign(serializedManager, "musicVolumeSlider", music);
        Assign(serializedManager, "sfxVolumeSlider", sfx);
        Assign(serializedManager, "masterVolumeText", masterValue);
        Assign(serializedManager, "musicVolumeText", musicValue);
        Assign(serializedManager, "sfxVolumeText", sfxValue);
        Assign(serializedManager, "qualityDropdown", quality);
        Assign(serializedManager, "shadowsToggle", shadows);
        Assign(serializedManager, "frameRateDropdown", frameRate);
        Assign(serializedManager, "hapticsToggle", haptics);
        Assign(serializedManager, "cameraSensitivitySlider", sensitivity);
        Assign(serializedManager, "cameraSensitivityText", sensitivityValue);
        Assign(serializedManager, "invertLookYToggle", invertLook);
        Assign(serializedManager, "authManager", auth);
        Assign(serializedManager, "accountStatusText", accountStatus);
        Assign(serializedManager, "accountActionButtonText", accountActionText);
        Assign(serializedManager, "accountActionButton", accountAction);
        Assign(serializedManager, "logoutButton", logout);
        serializedManager.ApplyModifiedPropertiesWithoutUndo();

        Button existingClose = FindChildRecursive(container, "CloseButton")?.GetComponent<Button>();
        WireButton(existingClose, manager.CloseSettings);
        WireButton(restore, manager.RestoreCozyDefaults);
        WireButton(apply, manager.ApplyAndClose);

        SetLayerRecursively(root.gameObject, settingsPanel.layer);
        ApplyBekindFont(settingsPanel);
        settingsPanel.SetActive(false);
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(tabs);
        return true;
    }

    private static int IntegrateIntoBuildScenes(
        GameObject sourcePanel,
        SettingsManager sourceManager)
    {
        if (sourcePanel == null || sourceManager == null)
            return 0;

        int changedCount = 0;
        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (string.IsNullOrWhiteSpace(buildScene.path) || buildScene.path == ScenePath)
                continue;

            if (!System.IO.File.Exists(buildScene.path))
            {
                Debug.LogWarning($"Skipping missing build scene while integrating settings: {buildScene.path}");
                continue;
            }

            Scene targetScene = SceneManager.GetSceneByPath(buildScene.path);
            bool wasLoaded = targetScene.IsValid() && targetScene.isLoaded;
            bool wasDirty = wasLoaded && targetScene.isDirty;
            if (!wasLoaded)
                targetScene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);

            bool changed = EnsureSceneIntegration(targetScene, sourcePanel, sourceManager);
            if (changed)
            {
                changedCount++;
                EditorSceneManager.MarkSceneDirty(targetScene);

                // Never silently save unrelated edits in a scene the user already
                // had open and dirty. The settings changes remain in that scene for
                // the user to review and save normally.
                if (!wasLoaded || !wasDirty)
                    EditorSceneManager.SaveScene(targetScene);
                else
                    Debug.Log($"Mobile settings were added to {targetScene.name}; the scene was already dirty, so it was left unsaved.");
            }

            if (!wasLoaded)
                EditorSceneManager.CloseScene(targetScene, true);
        }

        return changedCount;
    }

    private static bool EnsureSceneIntegration(
        Scene scene,
        GameObject sourcePanel,
        SettingsManager sourceManager)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        bool changed = false;
        GameObject legacyPanel = FindGameObject(scene, "SettingsPanel");
        GameObject mobilePanel = FindGameObject(scene, "MobileSettingsPanel");
        if (mobilePanel == null && legacyPanel != null &&
            FindChildRecursive(legacyPanel.transform, GeneratedRootName) != null)
        {
            mobilePanel = legacyPanel;
        }

        Canvas canvas = FindBestCanvas(scene);
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject(
                "MobileSettingsCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1500;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            changed = true;
        }

        if (mobilePanel == null)
        {
            mobilePanel = Object.Instantiate(sourcePanel);
            mobilePanel.name = "MobileSettingsPanel";
            SceneManager.MoveGameObjectToScene(mobilePanel, scene);
            mobilePanel.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = mobilePanel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
                panelRect.localScale = Vector3.one;
            }
            mobilePanel.SetActive(false);
            changed = true;
        }

        SettingsManager manager = FindInScene<SettingsManager>(scene);
        if (manager == null)
        {
            GameObject managerObject = new GameObject("SettingsManager", typeof(SettingsManager));
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            manager = managerObject.GetComponent<SettingsManager>();
            changed = true;
        }

        if (manager.mainAudioMixer == null && sourceManager.mainAudioMixer != null)
        {
            manager.mainAudioMixer = sourceManager.mainAudioMixer;
            changed = true;
        }

        WireManagerFromPanel(manager, mobilePanel, scene);
        changed |= ApplyBekindFont(mobilePanel);

        if (legacyPanel != null && legacyPanel != mobilePanel && legacyPanel.activeSelf)
        {
            legacyPanel.SetActive(false);
            changed = true;
        }

        PauseManager pauseManager = FindInScene<PauseManager>(scene);
        if (pauseManager != null && pauseManager.settingsPanel != mobilePanel)
        {
            SerializedObject serializedPause = new SerializedObject(pauseManager);
            Assign(serializedPause, "settingsPanel", mobilePanel);
            serializedPause.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pauseManager);
            changed = true;
        }

        Button settingsButton = FindNamedButton(scene, "btn_Settings", "SettingsButton", "MobileSettingsButton");
        if (settingsButton == null)
        {
            settingsButton = CreateButton(canvas.transform, "MobileSettingsButton", "SETTINGS", Gold, 23f);
            RectTransform buttonRect = settingsButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 1f);
            buttonRect.anchoredPosition = new Vector2(-36f, -36f);
            buttonRect.sizeDelta = new Vector2(210f, 68f);
            SetLayerRecursively(settingsButton.gameObject, canvas.gameObject.layer);
            changed = true;
        }

        WireSettingsOpenButton(settingsButton, manager, legacyPanel);
        mobilePanel.transform.SetAsLastSibling();
        mobilePanel.SetActive(false);
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(mobilePanel);
        return changed;
    }

    private static void WireManagerFromPanel(SettingsManager manager, GameObject panel, Scene scene)
    {
        if (manager == null || panel == null)
            return;

        SerializedObject serializedManager = new SerializedObject(manager);
        Assign(serializedManager, "settingsPanel", panel);
        Assign(serializedManager, "globalMuteToggle", FindPanelComponent<Toggle>(panel, "GlobalMuteToggle"));
        Assign(serializedManager, "masterVolumeSlider", FindPanelComponent<Slider>(panel, "MasterVolumeSlider"));
        Assign(serializedManager, "musicVolumeSlider", FindPanelComponent<Slider>(panel, "MusicVolumeSlider"));
        Assign(serializedManager, "sfxVolumeSlider", FindPanelComponent<Slider>(panel, "SFXVolumeSlider"));
        Assign(serializedManager, "masterVolumeText", FindPanelComponent<TMP_Text>(panel, "MasterVolumeSliderGroup", "Value"));
        Assign(serializedManager, "musicVolumeText", FindPanelComponent<TMP_Text>(panel, "MusicVolumeSliderGroup", "Value"));
        Assign(serializedManager, "sfxVolumeText", FindPanelComponent<TMP_Text>(panel, "SFXVolumeSliderGroup", "Value"));
        Assign(serializedManager, "qualityDropdown", FindPanelComponent<TMP_Dropdown>(panel, "QualityDropdown"));
        Assign(serializedManager, "shadowsToggle", FindPanelComponent<Toggle>(panel, "ShadowsToggle"));
        Assign(serializedManager, "frameRateDropdown", FindPanelComponent<TMP_Dropdown>(panel, "FrameRateDropdown"));
        Assign(serializedManager, "hapticsToggle", FindPanelComponent<Toggle>(panel, "HapticsToggle"));
        Assign(serializedManager, "cameraSensitivitySlider", FindPanelComponent<Slider>(panel, "CameraSensitivitySlider"));
        Assign(serializedManager, "cameraSensitivityText", FindPanelComponent<TMP_Text>(panel, "CameraSensitivitySliderGroup", "Value"));
        Assign(serializedManager, "invertLookYToggle", FindPanelComponent<Toggle>(panel, "InvertLookYToggle"));
        Assign(serializedManager, "authManager", FindInScene<PlayFabAuthManager>(scene));
        Assign(serializedManager, "accountStatusText", FindPanelComponent<TMP_Text>(panel, "AccountStatusText"));

        Button accountAction = FindPanelComponent<Button>(panel, "AccountActionButton");
        Assign(serializedManager, "accountActionButton", accountAction);
        Assign(serializedManager, "accountActionButtonText",
            accountAction != null ? accountAction.GetComponentInChildren<TMP_Text>(true) : null);
        Assign(serializedManager, "logoutButton", FindPanelComponent<Button>(panel, "LogoutButton"));
        serializedManager.ApplyModifiedPropertiesWithoutUndo();

        WireButton(FindPanelComponent<Button>(panel, "CloseButton"), manager.CloseSettings);
        WireButton(FindPanelComponent<Button>(panel, "RestoreDefaultsButton"), manager.RestoreCozyDefaults);
        WireButton(FindPanelComponent<Button>(panel, "ApplySettingsButton"), manager.ApplyAndClose);
    }

    private static bool ApplyBekindFont(GameObject root)
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BekindFontPath);
        if (root == null || font == null)
            return false;

        bool changed = false;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == font)
                continue;
            text.font = font;
            EditorUtility.SetDirty(text);
            changed = true;
        }
        return changed;
    }

    private static Canvas FindBestCanvas(Scene scene)
    {
        Canvas best = null;
        int bestScore = int.MinValue;
        foreach (Canvas candidate in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (candidate == null || candidate.gameObject.scene != scene || !candidate.isRootCanvas)
                continue;

            int score = candidate.renderMode == RenderMode.ScreenSpaceOverlay ? 100 : 0;
            string lowerName = candidate.name.ToLowerInvariant();
            if (lowerName.Contains("main")) score += 40;
            if (lowerName.Contains("ui")) score += 20;
            if (candidate.gameObject.activeInHierarchy) score += 10;
            if (score <= bestScore) continue;
            best = candidate;
            bestScore = score;
        }
        return best;
    }

    private static Button FindNamedButton(Scene scene, params string[] names)
    {
        foreach (Button button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button == null || button.gameObject.scene != scene)
                continue;
            foreach (string name in names)
            {
                if (button.name == name)
                    return button;
            }
        }
        return null;
    }

    private static void WireSettingsOpenButton(Button button, SettingsManager manager, GameObject legacyPanel)
    {
        if (button == null || manager == null)
            return;

        bool alreadyWired = false;
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            Object target = button.onClick.GetPersistentTarget(i);
            string method = button.onClick.GetPersistentMethodName(i);
            if (target == manager && (method == nameof(SettingsManager.OpenSettings) ||
                                      method == nameof(SettingsManager.ToggleSettings)))
            {
                alreadyWired = true;
                continue;
            }

            if (target is SettingsManager || target == legacyPanel)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
        }

        if (!alreadyWired)
            UnityEventTools.AddPersistentListener(button.onClick, manager.OpenSettings);
        EditorUtility.SetDirty(button);
    }

    private static T FindPanelComponent<T>(GameObject panel, string objectName) where T : Component
    {
        Transform target = FindChildRecursive(panel.transform, objectName);
        return target != null ? target.GetComponent<T>() : null;
    }

    private static T FindPanelComponent<T>(
        GameObject panel,
        string parentName,
        string childName) where T : Component
    {
        Transform parent = FindChildRecursive(panel.transform, parentName);
        Transform target = parent != null ? FindChildRecursive(parent, childName) : null;
        return target != null ? target.GetComponent<T>() : null;
    }

    private static RectTransform CreatePage(Transform parent, string name)
    {
        RectTransform page = CreateRect(parent, name);
        Stretch(page, 16f);
        VerticalLayoutGroup layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(26, 26, 28, 28);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return page;
    }

    private static RectTransform CreateSettingRow(Transform parent, string name, string label, bool alternate)
    {
        RectTransform row = CreateRect(parent, name);
        Image background = row.gameObject.AddComponent<Image>();
        background.color = alternate ? CreamAlternate : new Color(Cream.r, Cream.g, Cream.b, 0.55f);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(30, 30, 8, 8);
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = 76f;
        rowLayout.flexibleWidth = 1f;

        TextMeshProUGUI text = CreateText(row, "Label", label, 27f, FontStyles.Normal);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = Ink;
        LayoutElement textLayout = text.gameObject.AddComponent<LayoutElement>();
        textLayout.preferredWidth = 650f;
        textLayout.flexibleWidth = 1f;
        textLayout.preferredHeight = 54f;
        return row;
    }

    private static Button CreateButton(Transform parent, string name, string label, Color color, float fontSize)
    {
        RectTransform root = CreateRect(parent, name);
        Image image = root.gameObject.AddComponent<Image>();
        image.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;
        image.color = color;
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        TextMeshProUGUI text = CreateText(root, "Label", label, fontSize, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        text.color = Ink;
        Stretch(text.rectTransform, 8f);
        LayoutElement element = root.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 60f;
        element.flexibleWidth = 1f;
        return button;
    }

    private static Toggle CreateToggle(Transform parent, string name)
    {
        RectTransform root = CreateRect(parent, name);
        root.sizeDelta = new Vector2(54f, 54f);
        LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = 54f;
        layout.preferredHeight = 54f;

        Image background = root.gameObject.AddComponent<Image>();
        background.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        background.type = Image.Type.Sliced;
        background.color = Cream;
        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = Ink;
        outline.effectDistance = new Vector2(2f, -2f);

        RectTransform checkRect = CreateRect(root, "Checkmark");
        Stretch(checkRect, 10f);
        Image check = checkRect.gameObject.AddComponent<Image>();
        check.sprite = BuiltinSprite("UI/Skin/Checkmark.psd");
        check.color = Gold;

        Toggle toggle = root.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = check;
        return toggle;
    }

    private static Slider CreateValueSlider(
        Transform parent,
        string name,
        out TextMeshProUGUI valueText,
        float min = 0f,
        float max = 1f)
    {
        RectTransform group = CreateRect(parent, name + "Group");
        LayoutElement groupLayout = group.gameObject.AddComponent<LayoutElement>();
        groupLayout.preferredWidth = 520f;
        groupLayout.preferredHeight = 54f;
        HorizontalLayoutGroup horizontal = group.gameObject.AddComponent<HorizontalLayoutGroup>();
        horizontal.spacing = 18f;
        horizontal.childAlignment = TextAnchor.MiddleCenter;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = false;
        horizontal.childForceExpandHeight = false;

        RectTransform root = CreateRect(group, name);
        LayoutElement sliderLayout = root.gameObject.AddComponent<LayoutElement>();
        sliderLayout.preferredWidth = 410f;
        sliderLayout.preferredHeight = 42f;
        Slider slider = root.gameObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = max;

        RectTransform backgroundRect = CreateRect(root, "Background");
        backgroundRect.anchorMin = new Vector2(0f, 0.5f);
        backgroundRect.anchorMax = new Vector2(1f, 0.5f);
        backgroundRect.sizeDelta = new Vector2(0f, 12f);
        Image background = backgroundRect.gameObject.AddComponent<Image>();
        background.sprite = BuiltinSprite("UI/Skin/Background.psd");
        background.type = Image.Type.Sliced;
        background.color = Track;

        RectTransform fillArea = CreateRect(root, "Fill Area");
        Stretch(fillArea, 10f, 10f, 15f, 15f);
        RectTransform fillRect = CreateRect(fillArea, "Fill");
        Stretch(fillRect);
        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        fill.type = Image.Type.Sliced;
        fill.color = Gold;

        RectTransform handleArea = CreateRect(root, "Handle Slide Area");
        Stretch(handleArea, 12f, 12f, 8f, 8f);
        RectTransform handleRect = CreateRect(handleArea, "Handle");
        handleRect.sizeDelta = new Vector2(34f, 34f);
        Image handle = handleRect.gameObject.AddComponent<Image>();
        handle.sprite = BuiltinSprite("UI/Skin/Knob.psd");
        handle.color = Gold;

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle;

        valueText = CreateText(group, "Value", "100%", 24f, FontStyles.Bold);
        valueText.alignment = TextAlignmentOptions.Center;
        valueText.color = Ink;
        LayoutElement valueLayout = valueText.gameObject.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = 88f;
        valueLayout.preferredHeight = 50f;
        return slider;
    }

    private static TMP_Dropdown CreateDropdown(Transform parent, string name, List<string> options)
    {
        RectTransform root = CreateRect(parent, name);
        LayoutElement rootLayout = root.gameObject.AddComponent<LayoutElement>();
        rootLayout.preferredWidth = 430f;
        rootLayout.preferredHeight = 56f;
        Image image = root.gameObject.AddComponent<Image>();
        image.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;
        image.color = CreamAlternate;
        TMP_Dropdown dropdown = root.gameObject.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = image;

        TextMeshProUGUI caption = CreateText(root, "Label", "Select...", 24f, FontStyles.Normal);
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.color = Ink;
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = new Vector2(18f, 4f);
        caption.rectTransform.offsetMax = new Vector2(-52f, -4f);

        TextMeshProUGUI arrow = CreateText(root, "Arrow", "▼", 22f, FontStyles.Bold);
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.color = Ink;
        arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
        arrow.rectTransform.sizeDelta = new Vector2(42f, 42f);

        RectTransform template = CreateRect(root, "Template");
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0.5f, 1f);
        template.anchoredPosition = new Vector2(0f, -3f);
        template.sizeDelta = new Vector2(0f, 220f);
        Image templateImage = template.gameObject.AddComponent<Image>();
        templateImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
        templateImage.type = Image.Type.Sliced;
        templateImage.color = Cream;
        Canvas popupCanvas = template.gameObject.AddComponent<Canvas>();
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingOrder = 20000;
        template.gameObject.AddComponent<GraphicRaycaster>();
        ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        RectTransform viewport = CreateRect(template, "Viewport");
        Stretch(viewport, 4f);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = Color.white;
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = CreateRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, 42f);

        RectTransform item = CreateRect(content, "Item");
        item.anchorMin = new Vector2(0f, 0.5f);
        item.anchorMax = new Vector2(1f, 0.5f);
        item.sizeDelta = new Vector2(0f, 42f);
        Image itemBackground = item.gameObject.AddComponent<Image>();
        itemBackground.color = CreamAlternate;
        Toggle itemToggle = item.gameObject.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;

        RectTransform checkRect = CreateRect(item, "Item Checkmark");
        checkRect.anchorMin = checkRect.anchorMax = new Vector2(0f, 0.5f);
        checkRect.anchoredPosition = new Vector2(18f, 0f);
        checkRect.sizeDelta = new Vector2(22f, 22f);
        Image check = checkRect.gameObject.AddComponent<Image>();
        check.sprite = BuiltinSprite("UI/Skin/Checkmark.psd");
        check.color = Gold;
        itemToggle.graphic = check;

        TextMeshProUGUI itemLabel = CreateText(item, "Item Label", "Option", 22f, FontStyles.Normal);
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        itemLabel.color = Ink;
        itemLabel.rectTransform.anchorMin = Vector2.zero;
        itemLabel.rectTransform.anchorMax = Vector2.one;
        itemLabel.rectTransform.offsetMin = new Vector2(42f, 2f);
        itemLabel.rectTransform.offsetMax = new Vector2(-8f, -2f);

        scroll.viewport = viewport;
        scroll.content = content;
        dropdown.template = template;
        dropdown.captionText = caption;
        dropdown.itemText = itemLabel;
        dropdown.ClearOptions();
        if (options.Count == 0) options.Add("Default");
        dropdown.AddOptions(options);
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
        template.gameObject.SetActive(false);
        return dropdown;
    }

    private static TextMeshProUGUI CreateText(
        Transform parent, string name, string value, float size, FontStyles style)
    {
        RectTransform root = CreateRect(parent, name);
        TextMeshProUGUI text = root.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BekindFontPath)
            ?? TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject created = new GameObject(name, typeof(RectTransform));
        RectTransform rect = created.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void SetTopStrip(RectTransform rect, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, height);
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void Assign(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.objectReferenceValue = value;
    }

    private static void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, action);
        EditorUtility.SetDirty(button);
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (T component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component != null && component.gameObject.scene == scene) return component;
        }
        return null;
    }

    private static GameObject FindGameObject(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindChildRecursive(root.transform, name);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static Transform FindChildRecursive(Transform current, string name)
    {
        if (current.name == name) return current;
        for (int i = 0; i < current.childCount; i++)
        {
            Transform result = FindChildRecursive(current.GetChild(i), name);
            if (result != null) return result;
        }
        return null;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    private static Sprite BuiltinSprite(string path)
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
#endif
