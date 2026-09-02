#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Keeps Bhan House on the same UI presentation as Canyon Crossing while
/// preserving Bhan-specific gameplay objects, dialogue content, and references.
/// </summary>
public static class BhanHouseUISync
{
    private const string CanyonPath = "Assets/Scenes/CanyonCrossing.unity";
    private const string BhanPath = "Assets/Scenes/BHAN HOUSE.unity";

    [MenuItem("Tools/Civil Craft/Synchronize Bhan House UI From Canyon Crossing")]
    public static void SynchronizeFromMenu()
    {
        Synchronize(false);
    }

    public static void SynchronizeFromCommandLine()
    {
        Synchronize(true);
    }

    private static void Synchronize(bool commandLine)
    {
        // Re-run the existing shared generators first so both scenes receive the
        // latest settings, achievement panel, and absolute popup structure.
        MainMenuSettingsSetup.SetupFromMenu();
        AchievementPanelStyleSetup.StyleAchievementPanels();
        AchievementPopupSceneSetup.SetupAchievementPopupInScenes();

        Scene previousActive = SceneManager.GetActiveScene();
        Scene canyon = SceneManager.GetSceneByPath(CanyonPath);
        bool openedCanyon = !canyon.IsValid() || !canyon.isLoaded;
        if (openedCanyon)
            canyon = EditorSceneManager.OpenScene(CanyonPath, OpenSceneMode.Additive);

        Scene bhan = SceneManager.GetSceneByPath(BhanPath);
        bool openedBhan = !bhan.IsValid() || !bhan.isLoaded;
        if (openedBhan)
            bhan = EditorSceneManager.OpenScene(BhanPath, OpenSceneMode.Additive);

        SceneManager.SetActiveScene(canyon);
        ShopSceneSetup.SetupCurrentScene(false);
        EditorSceneManager.MarkSceneDirty(canyon);
        EditorSceneManager.SaveScene(canyon);

        SynchronizeDialogue(canyon, bhan);
        SynchronizeLessonSystem(canyon, bhan);

        // These panels already have scene-specific managers in Bhan House. Copy
        // presentation/layout only so their existing functional references remain.
        CopyVisualTree(FindObject(canyon, "AlmanacCanvas"), FindObject(bhan, "AlmanacCanvas"));
        CopyVisualTree(FindObject(canyon, "SettingsPanel"), FindObject(bhan, "SettingsPanel"));
        CopyVisualTree(FindObject(canyon, "AchievementsPanel"), FindObject(bhan, "AchievementsPanel"));

        SceneManager.SetActiveScene(bhan);
        ShopSceneSetup.SetupCurrentScene(false);

        EditorSceneManager.MarkSceneDirty(bhan);
        EditorSceneManager.SaveScene(bhan);
        AssetDatabase.SaveAssets();

        if (openedCanyon && canyon.IsValid() && canyon.isLoaded)
            EditorSceneManager.CloseScene(canyon, true);

        if (previousActive.IsValid() && previousActive.isLoaded)
            SceneManager.SetActiveScene(previousActive);
        else
            SceneManager.SetActiveScene(bhan);

        if (openedBhan && bhan.IsValid() && bhan.isLoaded &&
            previousActive.IsValid() && previousActive.path != BhanPath)
        {
            EditorSceneManager.CloseScene(bhan, true);
        }

        Debug.Log("[BhanHouseUISync] Dialogue, Lessons, Shop, Settings, Achievements, and Almanac presentation synchronized.");

        if (commandLine)
            EditorApplication.Exit(0);
    }

    private static void SynchronizeDialogue(Scene sourceScene, Scene targetScene)
    {
        DialogueManager sourceManager = FindComponent<DialogueManager>(sourceScene);
        DialogueManager targetManager = FindComponent<DialogueManager>(targetScene);
        if (sourceManager == null || targetManager == null)
        {
            Debug.LogWarning("[BhanHouseUISync] DialogueManager was not found in both scenes.");
            return;
        }

        SerializedObject sourceData = new SerializedObject(sourceManager);
        SerializedObject targetData = new SerializedObject(targetManager);
        GameObject sourceBox = sourceData.FindProperty("dialogueBox").objectReferenceValue as GameObject;
        GameObject oldTargetBox = targetData.FindProperty("dialogueBox").objectReferenceValue as GameObject;
        if (sourceBox == null || oldTargetBox == null)
        {
            Debug.LogWarning("[BhanHouseUISync] DialogueBox is missing in Canyon Crossing or Bhan House.");
            return;
        }

        Transform targetParent = oldTargetBox.transform.parent;
        int siblingIndex = oldTargetBox.transform.GetSiblingIndex();
        Object.DestroyImmediate(oldTargetBox);

        GameObject clonedBox = Object.Instantiate(sourceBox);
        clonedBox.name = "DialogueBox";
        clonedBox.transform.SetParent(null);
        SceneManager.MoveGameObjectToScene(clonedBox, targetScene);
        clonedBox.transform.SetParent(targetParent, false);
        clonedBox.transform.SetSiblingIndex(Mathf.Min(siblingIndex, targetParent.childCount - 1));
        CopyRectTransform(sourceBox.transform as RectTransform, clonedBox.transform as RectTransform);
        clonedBox.SetActive(false);

        string namePath = RelativePath(sourceBox.transform, sourceManager.nameText.transform);
        string dialoguePath = RelativePath(sourceBox.transform, sourceManager.dialogueText.transform);
        Transform clonedName = FindByRelativePath(clonedBox.transform, namePath);
        Transform clonedDialogue = FindByRelativePath(clonedBox.transform, dialoguePath);

        targetManager.nameText = clonedName != null ? clonedName.GetComponent<TextMeshProUGUI>() : null;
        targetManager.dialogueText = clonedDialogue != null ? clonedDialogue.GetComponent<TextMeshProUGUI>() : null;
        targetManager.animator = clonedBox.GetComponent<Animator>();

        // The Canyon dialogue buttons point to Canyon's DialogueManager, which
        // lives outside the cloned DialogueBox hierarchy. Remap those persistent
        // UnityEvent targets before saving so Bhan never receives a cross-scene
        // reference or a dead Continue button.
        RemapDialogueButtonTargets(clonedBox, sourceManager, targetManager);

        targetData.Update();
        targetData.FindProperty("dialogueBox").objectReferenceValue = clonedBox;
        targetData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(targetManager);
    }

    private static void RemapDialogueButtonTargets(
        GameObject clonedBox,
        DialogueManager sourceManager,
        DialogueManager targetManager)
    {
        foreach (Button button in clonedBox.GetComponentsInChildren<Button>(true))
        {
            SerializedObject buttonData = new SerializedObject(button);
            SerializedProperty calls = buttonData.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null) continue;

            bool changed = false;
            for (int i = 0; i < calls.arraySize; i++)
            {
                SerializedProperty target = calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Target");
                if (target == null || target.objectReferenceValue != sourceManager) continue;
                target.objectReferenceValue = targetManager;
                changed = true;
            }

            if (!changed) continue;
            buttonData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(button);
        }
    }

    private static void CloneOrRefreshRoot(Scene sourceScene, Scene targetScene, string rootName)
    {
        GameObject source = FindObject(sourceScene, rootName);
        if (source == null) return;

        GameObject existing = FindObject(targetScene, rootName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        GameObject clone = Object.Instantiate(source);
        clone.name = source.name;
        clone.transform.SetParent(null);
        SceneManager.MoveGameObjectToScene(clone, targetScene);
        clone.transform.position = source.transform.position;
        clone.transform.rotation = source.transform.rotation;
        clone.transform.localScale = source.transform.localScale;

        // Managers stay alive while their visible panel starts closed.
        LessonUIManager lessonManager = clone.GetComponentInChildren<LessonUIManager>(true);
        if (lessonManager != null && lessonManager.Panel != null)
            lessonManager.Panel.SetActive(false);
    }

    private static void SynchronizeLessonSystem(Scene sourceScene, Scene targetScene)
    {
        GameObject sourceCanvas = FindObject(sourceScene, "LessonCanvas");
        LessonUIManager sourceManager = FindComponent<LessonUIManager>(sourceScene);
        if (sourceCanvas == null || sourceManager == null)
        {
            Debug.LogWarning("[BhanHouseUISync] Canyon lesson canvas or manager is missing.");
            return;
        }

        GameObject existingCanvas = FindObject(targetScene, "LessonCanvas");
        if (existingCanvas != null)
            Object.DestroyImmediate(existingCanvas);

        LessonUIManager existingManager = FindComponent<LessonUIManager>(targetScene);
        if (existingManager != null)
            Object.DestroyImmediate(existingManager.gameObject);

        GameObject clonedCanvas = Object.Instantiate(sourceCanvas);
        clonedCanvas.name = "LessonCanvas";
        clonedCanvas.transform.SetParent(null);
        SceneManager.MoveGameObjectToScene(clonedCanvas, targetScene);
        clonedCanvas.transform.position = sourceCanvas.transform.position;
        clonedCanvas.transform.rotation = sourceCanvas.transform.rotation;
        // Canyon's legacy scene once stored this Canvas at zero scale. A Canvas
        // root must remain usable in Bhan even before LessonCanvasRepair runs.
        clonedCanvas.transform.localScale = Vector3.one;

        GameObject managerObject = new GameObject("LessonUIManager");
        SceneManager.MoveGameObjectToScene(managerObject, targetScene);
        LessonUIManager targetManager = managerObject.AddComponent<LessonUIManager>();

        SerializedObject sourceData = new SerializedObject(sourceManager);
        SerializedObject targetData = new SerializedObject(targetManager);
        string[] referenceFields =
        {
            "lessonPanel",
            "lessonTitleText",
            "lessonImage",
            "lessonDescriptionText",
            "closeButton",
            "descriptionScrollRect",
            "descriptionContent",
            "lessonCanvas"
        };

        foreach (string field in referenceFields)
        {
            SerializedProperty sourceProperty = sourceData.FindProperty(field);
            SerializedProperty targetProperty = targetData.FindProperty(field);
            if (sourceProperty == null || targetProperty == null) continue;
            targetProperty.objectReferenceValue = RemapLessonReference(
                sourceCanvas.transform,
                clonedCanvas.transform,
                sourceProperty.objectReferenceValue);
        }

        string[] booleanFields =
        {
            "usePanelCoordinator",
            "lockPlayerControls",
            "hideImageWhenMissing",
            "hideOtherCanvases"
        };
        foreach (string field in booleanFields)
        {
            SerializedProperty sourceProperty = sourceData.FindProperty(field);
            SerializedProperty targetProperty = targetData.FindProperty(field);
            if (sourceProperty != null && targetProperty != null)
                targetProperty.boolValue = sourceProperty.boolValue;
        }

        SerializedProperty keepVisible = targetData.FindProperty("canvasesToKeepVisible");
        if (keepVisible != null) keepVisible.arraySize = 0;
        targetData.ApplyModifiedPropertiesWithoutUndo();

        SerializedProperty panelProperty = targetData.FindProperty("lessonPanel");
        GameObject lessonPanel = panelProperty != null
            ? panelProperty.objectReferenceValue as GameObject
            : null;
        if (lessonPanel != null)
            lessonPanel.SetActive(false);

        EditorUtility.SetDirty(targetManager);
        EditorUtility.SetDirty(clonedCanvas);
    }

    private static Object RemapLessonReference(
        Transform sourceRoot,
        Transform targetRoot,
        Object sourceReference)
    {
        if (sourceReference == null) return null;

        Transform sourceTransform = null;
        if (sourceReference is GameObject sourceObject)
            sourceTransform = sourceObject.transform;
        else if (sourceReference is Component sourceComponent)
            sourceTransform = sourceComponent.transform;

        if (sourceTransform == null ||
            (sourceTransform != sourceRoot && !sourceTransform.IsChildOf(sourceRoot)))
            return null;

        string path = RelativePath(sourceRoot, sourceTransform);
        Transform targetTransform = FindByRelativePath(targetRoot, path);
        if (targetTransform == null) return null;

        if (sourceReference is GameObject)
            return targetTransform.gameObject;
        if (sourceReference is Component component)
            return targetTransform.GetComponent(component.GetType());
        return null;
    }

    private static void CopyVisualTree(GameObject source, GameObject target)
    {
        if (source == null || target == null) return;
        CopyVisuals(source, target);

        for (int i = 0; i < source.transform.childCount; i++)
        {
            Transform sourceChild = source.transform.GetChild(i);
            int occurrence = NameOccurrence(sourceChild);
            Transform targetChild = FindNamedOccurrence(target.transform, sourceChild.name, occurrence);
            if (targetChild != null)
                CopyVisualTree(sourceChild.gameObject, targetChild.gameObject);
        }
    }

    private static void CopyVisuals(GameObject source, GameObject target)
    {
        CopyRectTransform(source.transform as RectTransform, target.transform as RectTransform);

        Image sourceImage = source.GetComponent<Image>();
        Image targetImage = target.GetComponent<Image>();
        if (sourceImage != null && targetImage != null)
        {
            targetImage.sprite = sourceImage.sprite;
            targetImage.color = sourceImage.color;
            targetImage.type = sourceImage.type;
            targetImage.preserveAspect = sourceImage.preserveAspect;
            targetImage.fillCenter = sourceImage.fillCenter;
            targetImage.pixelsPerUnitMultiplier = sourceImage.pixelsPerUnitMultiplier;
        }

        TMP_Text sourceText = source.GetComponent<TMP_Text>();
        TMP_Text targetText = target.GetComponent<TMP_Text>();
        if (sourceText != null && targetText != null)
        {
            targetText.font = sourceText.font;
            targetText.fontSize = sourceText.fontSize;
            targetText.fontStyle = sourceText.fontStyle;
            targetText.color = sourceText.color;
            targetText.alignment = sourceText.alignment;
            targetText.enableWordWrapping = sourceText.enableWordWrapping;
            targetText.overflowMode = sourceText.overflowMode;
        }

        Outline sourceOutline = source.GetComponent<Outline>();
        Outline targetOutline = target.GetComponent<Outline>();
        if (sourceOutline != null && targetOutline != null)
        {
            targetOutline.effectColor = sourceOutline.effectColor;
            targetOutline.effectDistance = sourceOutline.effectDistance;
            targetOutline.useGraphicAlpha = sourceOutline.useGraphicAlpha;
        }

        LayoutElement sourceLayout = source.GetComponent<LayoutElement>();
        LayoutElement targetLayout = target.GetComponent<LayoutElement>();
        if (sourceLayout != null && targetLayout != null)
        {
            targetLayout.minWidth = sourceLayout.minWidth;
            targetLayout.minHeight = sourceLayout.minHeight;
            targetLayout.preferredWidth = sourceLayout.preferredWidth;
            targetLayout.preferredHeight = sourceLayout.preferredHeight;
            targetLayout.flexibleWidth = sourceLayout.flexibleWidth;
            targetLayout.flexibleHeight = sourceLayout.flexibleHeight;
        }

        Button sourceButton = source.GetComponent<Button>();
        Button targetButton = target.GetComponent<Button>();
        if (sourceButton != null && targetButton != null)
        {
            targetButton.transition = sourceButton.transition;
            targetButton.colors = sourceButton.colors;
        }
    }

    private static void CopyRectTransform(RectTransform source, RectTransform target)
    {
        if (source == null || target == null) return;
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static int NameOccurrence(Transform child)
    {
        int occurrence = 0;
        for (int i = 0; i < child.GetSiblingIndex(); i++)
        {
            if (child.parent.GetChild(i).name == child.name) occurrence++;
        }
        return occurrence;
    }

    private static Transform FindNamedOccurrence(Transform parent, string name, int occurrence)
    {
        int found = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name != name) continue;
            if (found++ == occurrence) return child;
        }
        return null;
    }

    private static string RelativePath(Transform root, Transform child)
    {
        if (root == null || child == null || (child != root && !child.IsChildOf(root))) return string.Empty;
        if (child == root) return string.Empty;

        string path = child.name;
        Transform current = child.parent;
        while (current != null && current != root)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    private static Transform FindByRelativePath(Transform root, string path)
    {
        if (root == null) return null;
        return string.IsNullOrEmpty(path) ? root : root.Find(path);
    }

    private static GameObject FindObject(Scene scene, string objectName)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindRecursive(root.transform, objectName);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static T FindComponent<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }

    private static Transform FindRecursive(Transform root, string objectName)
    {
        if (root.name == objectName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindRecursive(root.GetChild(i), objectName);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
