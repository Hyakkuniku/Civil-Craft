using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Idempotent one-click setup for the existing Main Menu scene. It creates only
/// scene UI objects; no prefab is required. The setup also runs when Main Menu is
/// opened if the dropdown has not been created yet.
/// </summary>
[InitializeOnLoad]
public static class MainMenuDropdownSetup
{
    private const string ScenePath = "Assets/Scenes/Main Menu.unity";
    private const string PanelName = "MainMenuDropdownPanel";

    static MainMenuDropdownSetup()
    {
        EditorSceneManager.sceneOpened -= HandleSceneOpened;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
        EditorApplication.delayCall += TrySetupActiveMainMenu;
    }

    [MenuItem("Tools/Civil Craft/Setup Main Menu Dropdown")]
    public static void SetupFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        if (EnsureSetup(scene))
            EditorSceneManager.SaveScene(scene);
        else
            Debug.Log("Main Menu dropdown is already configured.");
    }

    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path != ScenePath) return;

        bool changed = EnsureSetup(scene);
        SaveGeneratedSetupIfNeeded(scene, changed);
    }

    private static void TrySetupActiveMainMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath) return;
        bool changed = EnsureSetup(scene);
        SaveGeneratedSetupIfNeeded(scene, changed);
    }

    private static void SaveGeneratedSetupIfNeeded(Scene scene, bool changed)
    {
        // Persist a newly generated in-memory hierarchy once, but do not auto-save
        // unrelated Main Menu edits on later script recompiles.
        bool setupExistsInScene = FindInScene<MainMenuUIController>(scene) != null &&
                                  FindGameObject(scene, PanelName) != null;
        bool setupExistsOnDisk = File.Exists(scene.path) &&
                                 File.ReadAllText(scene.path).Contains(PanelName);

        if ((changed || (setupExistsInScene && !setupExistsOnDisk)) && scene.isDirty &&
            !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Saved the animated Main Menu dropdown into the Main Menu scene.");
        }
    }

    private static bool EnsureSetup(Scene scene)
    {
        if (!scene.IsValid() || scene.path != ScenePath) return false;
        MainMenuUIController existingController = FindInScene<MainMenuUIController>(scene);
        GameObject existingPanel = FindGameObject(scene, PanelName);
        if (existingController != null && existingPanel != null)
        {
            // The user may have restyled or restructured the generated hierarchy.
            // Once it exists, never rewrite its RectTransforms, graphics, layout,
            // buttons, or serialized references automatically.
            return false;
        }

        GameObject playObject = FindGameObject(scene, "btn_Play");
        if (playObject == null)
        {
            Debug.LogError("Main Menu setup could not find the existing 'btn_Play' object.");
            return false;
        }

        Button playButton = playObject.GetComponent<Button>();
        RectTransform panelParent = playObject.transform.parent as RectTransform;
        if (playButton == null || panelParent == null)
        {
            Debug.LogError("btn_Play must have a Button and a RectTransform parent.", playObject);
            return false;
        }

        MainMenuUIController controller = panelParent.GetComponent<MainMenuUIController>();
        if (controller == null)
            controller = Undo.AddComponent<MainMenuUIController>(panelParent.gameObject);

        RectTransform panel = CreatePanel(panelParent);
        CanvasGroup panelGroup = panel.GetComponent<CanvasGroup>();

        GameObject settingsTemplate = FindGameObject(scene, "btn_Settings");
        GameObject quitTemplate = FindGameObject(scene, "btn_Quit");
        GameObject template = settingsTemplate != null
            ? settingsTemplate
            : quitTemplate != null ? quitTemplate : playObject;

        Button modeButton = CreateButton(template, panel, "ModeSelectionButton", "Mode Selection");
        Button loginButton = CreateButton(template, panel, "LoginButton", "Login");
        Button settingsButton = CreateButton(template, panel, "SettingsButton", "Settings");
        Button quitButton = CreateButton(template, panel, "QuitButton", "Quit");
        Button backButton = CreateButton(template, panel, "BackButton", "Back");

        SetPersistentClick(playButton, controller.OnClickToPlay);
        SetPersistentClick(modeButton, controller.OnModeSelectionClicked);
        SetPersistentClick(loginButton, controller.OnLoginClicked);
        SetPersistentClick(settingsButton, controller.OnSettingsClicked);
        SetPersistentClick(quitButton, controller.OnQuitClicked);
        SetPersistentClick(backButton, controller.HideMenuPanel);

        // The original corner controls remain in the hierarchy for easy recovery,
        // but their dropdown replacements are now the visible controls.
        if (settingsTemplate != null) settingsTemplate.SetActive(false);
        if (quitTemplate != null) quitTemplate.SetActive(false);

        SceneController sceneController = FindInScene<SceneController>(scene);
        SettingsManager settingsManager = FindInScene<SettingsManager>(scene);
        PlayFabAuthManager authManager = FindInScene<PlayFabAuthManager>(scene);

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("dropdownPanel").objectReferenceValue = panel;
        serializedController.FindProperty("dropdownCanvasGroup").objectReferenceValue = panelGroup;
        serializedController.FindProperty("initialPlayButton").objectReferenceValue = playObject;
        serializedController.FindProperty("onScreenPosition").vector2Value = Vector2.zero;
        serializedController.FindProperty("offScreenPosition").vector2Value = new Vector2(0f, 700f);
        serializedController.FindProperty("calculateOffScreenPositionAutomatically").boolValue = true;
        serializedController.FindProperty("offScreenPadding").floatValue = 50f;
        serializedController.FindProperty("slideDuration").floatValue = 0.45f;
        serializedController.FindProperty("fadeWithSlide").boolValue = true;
        serializedController.FindProperty("startHidden").boolValue = true;
        serializedController.FindProperty("sceneController").objectReferenceValue = sceneController;
        serializedController.FindProperty("settingsManager").objectReferenceValue = settingsManager;
        serializedController.FindProperty("authManager").objectReferenceValue = authManager;
        serializedController.FindProperty("authCanvas").objectReferenceValue =
            authManager != null ? authManager.authCanvas : FindGameObject(scene, "AuthCanvas");
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        AssignSupportingUI(controller, panelParent, panel.gameObject);

        panel.anchoredPosition = new Vector2(0f, 700f);
        panelGroup.alpha = 0f;
        panelGroup.interactable = false;
        panelGroup.blocksRaycasts = false;
        panel.SetAsLastSibling();

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Created the animated Main Menu dropdown. Save the Main Menu scene to keep it.", panel);
        return true;
    }

    private static bool EnsureEnhancements(
        Scene scene,
        MainMenuUIController controller,
        RectTransform panel)
    {
        if (panel == null) return false;
        bool changed = false;

        Vector2 expandedPanelSize = new Vector2(620f, 628f);
        if (panel.sizeDelta != expandedPanelSize)
        {
            panel.sizeDelta = expandedPanelSize;
            changed = true;
        }

        panel.gameObject.layer = panel.parent.gameObject.layer;
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        if (layout != null &&
            (layout.spacing != 28f || layout.padding.left != 48 || layout.padding.top != 48))
        {
            layout.padding = new RectOffset(48, 48, 48, 48);
            layout.spacing = 28f;
            changed = true;
        }

        Button modeButton = FindDirectChild(panel, "ModeSelectionButton")?.GetComponent<Button>();
        Button loginButton = FindDirectChild(panel, "LoginButton")?.GetComponent<Button>();
        Button settingsButton = FindDirectChild(panel, "SettingsButton")?.GetComponent<Button>();
        Button quitButton = FindDirectChild(panel, "QuitButton")?.GetComponent<Button>();
        Button backButton = FindDirectChild(panel, "BackButton")?.GetComponent<Button>();

        if (loginButton == null)
        {
            GameObject template = settingsButton != null
                ? settingsButton.gameObject
                : modeButton != null ? modeButton.gameObject : FindGameObject(scene, "btn_Play");
            loginButton = CreateButton(template, panel, "LoginButton", "Login");
            SetPersistentClick(loginButton, controller.OnLoginClicked);
            changed = true;
        }

        Button[] orderedButtons = { modeButton, loginButton, settingsButton, quitButton, backButton };
        for (int index = 0; index < orderedButtons.Length; index++)
        {
            Button button = orderedButtons[index];
            if (button == null) continue;
            button.transform.SetSiblingIndex(index);
            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect != null) buttonRect.sizeDelta = new Vector2(500f, 84f);
            LayoutElement element = button.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.minHeight = 76f;
                element.preferredHeight = 84f;
            }
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        PlayFabAuthManager authManager = FindInScene<PlayFabAuthManager>(scene);
        GameObject authCanvas = authManager != null
            ? authManager.authCanvas
            : FindGameObject(scene, "AuthCanvas");
        SerializedObject serializedController = new SerializedObject(controller);
        SerializedProperty authCanvasProperty = serializedController.FindProperty("authCanvas");
        if (authCanvasProperty.objectReferenceValue != authCanvas)
        {
            authCanvasProperty.objectReferenceValue = authCanvas;
            changed = true;
        }
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        if (AssignSupportingUI(controller, panel.parent as RectTransform, panel.gameObject))
            changed = true;

        if (changed)
        {
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Updated the Main Menu dropdown with Login and clean-screen behavior.", panel);
        }

        return changed;
    }

    private static RectTransform CreatePanel(RectTransform parent)
    {
        GameObject panelObject = new GameObject(
            PanelName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(CanvasGroup),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        Undo.RegisterCreatedObjectUndo(panelObject, "Create Main Menu Dropdown");

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(620f, 628f);
        panelObject.layer = parent.gameObject.layer;

        Image background = panelObject.GetComponent<Image>();
        background.color = new Color(0.035f, 0.055f, 0.08f, 0.94f);

        VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(48, 48, 48, 48);
        layout.spacing = 28f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panelObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rect;
    }

    private static Button CreateButton(
        GameObject template,
        RectTransform parent,
        string objectName,
        string label)
    {
        GameObject buttonObject = Object.Instantiate(template, parent, false);
        Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {label} Button");
        buttonObject.name = objectName;
        buttonObject.SetActive(true);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(500f, 84f);

        LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
        if (layoutElement == null) layoutElement = Undo.AddComponent<LayoutElement>(buttonObject);
        layoutElement.minHeight = 76f;
        layoutElement.preferredHeight = 84f;

        TMP_Text text = buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = label;

        Button button = buttonObject.GetComponent<Button>();
        if (button == null) button = Undo.AddComponent<Button>(buttonObject);
        return button;
    }

    private static void SetPersistentClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        while (button.onClick.GetPersistentEventCount() > 0)
            UnityEventTools.RemovePersistentListener(button.onClick, 0);
        button.onClick.RemoveAllListeners();
        UnityEventTools.AddPersistentListener(button.onClick, action);
        EditorUtility.SetDirty(button);
    }

    private static bool AssignSupportingUI(
        MainMenuUIController controller,
        RectTransform panelParent,
        GameObject dropdownPanel)
    {
        if (panelParent == null) return false;

        GameObject[] supportingObjects = new GameObject[panelParent.childCount - 1];
        int writeIndex = 0;
        for (int index = 0; index < panelParent.childCount; index++)
        {
            GameObject child = panelParent.GetChild(index).gameObject;
            if (child == dropdownPanel) continue;
            supportingObjects[writeIndex++] = child;
        }

        SerializedObject serializedController = new SerializedObject(controller);
        SerializedProperty property = serializedController.FindProperty("uiToHideWhileDropdownOpen");
        bool changed = property.arraySize != writeIndex;
        if (!changed)
        {
            for (int index = 0; index < writeIndex; index++)
            {
                if (property.GetArrayElementAtIndex(index).objectReferenceValue != supportingObjects[index])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed) return false;
        property.arraySize = writeIndex;
        for (int index = 0; index < writeIndex; index++)
            property.GetArrayElementAtIndex(index).objectReferenceValue = supportingObjects[index];
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child.name == childName) return child;
        }
        return null;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (T component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component.gameObject.scene == scene) return component;
        }
        return null;
    }

    private static GameObject FindGameObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in transforms)
            {
                if (candidate.name == objectName) return candidate.gameObject;
            }
        }
        return null;
    }
}
