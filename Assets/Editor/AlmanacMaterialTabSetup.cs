#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AlmanacMaterialTabSetup
{
    private const string SessionKey = "CivilCraft.AlmanacMaterialTabSetup.v3";
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    static AlmanacMaterialTabSetup()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryAutomaticSetup;
        };
    }

    [MenuItem("Tools/Civil Craft/Setup Almanac Material Tabs")]
    public static void SetupFromMenu()
    {
        SetupAllScenes();
    }

    private static void TryAutomaticSetup()
    {
        if (SessionState.GetBool(SessionKey, false) ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (SetupAllScenes()) SessionState.SetBool(SessionKey, true);
    }

    private static bool SetupAllScenes()
    {
        bool success = true;
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();

        try
        {
            // Reload from disk deliberately. This prevents stale in-memory scene
            // layout data from an earlier setup attempt being serialized again.
            foreach (string scenePath in ScenePaths)
            {
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                success &= SetupScene(scene);
            }
        }
        finally
        {
            if (previousSetup != null && previousSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
        AssetDatabase.SaveAssets();

        if (success)
            Debug.Log("[AlmanacMaterialTabSetup] Material archive tabs are installed in Canyon Crossing and Bhan House.");
        return success;
    }

    private static bool SetupScene(Scene scene)
    {
        AlmanacManager manager = FindSceneComponent<AlmanacManager>(scene);
        if (manager == null)
        {
            Debug.LogWarning($"[AlmanacMaterialTabSetup] AlmanacManager is missing in '{scene.name}'.");
            return false;
        }

        AlmanacCategory lesson = manager.categories.Find(category =>
            category != null && category.tabType == AlmanacTabType.Lessons);
        AlmanacCategory existing = manager.categories.Find(category =>
            category != null && category.tabType == AlmanacTabType.Materials);
        if (existing != null)
        {
            if (existing.tabButton != null)
            {
                PositionAfter(
                    existing.tabButton.transform as RectTransform,
                    lesson != null && lesson.tabButton != null
                        ? lesson.tabButton.transform as RectTransform
                        : null);
                existing.tabButton.gameObject.SetActive(true);
            }
            EnsureSeparateMaterialParent(existing, lesson);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return true;
        }

        if (lesson == null || lesson.tabButton == null ||
            lesson.leftPageZone == null || lesson.rightPageZone == null)
        {
            Debug.LogWarning($"[AlmanacMaterialTabSetup] The Lessons tab is incomplete in '{scene.name}'.");
            return false;
        }

        string alertPath = lesson.tabAlertIcon != null
            ? RelativePath(lesson.tabButton.transform, lesson.tabAlertIcon.transform)
            : string.Empty;
        Button tabButton = CloneTabButton(lesson);
        Transform leftZone = CloneZone(lesson.leftPageZone, "MaterialsLeftPageZone");
        Transform rightZone = CloneZone(lesson.rightPageZone, "MaterialsRightPageZone");
        ConvertLessonArchive(leftZone);
        ConvertLessonArchive(rightZone);

        GameObject alert = null;
        if (lesson.tabAlertIcon != null)
        {
            Transform alertTransform = FindRelative(tabButton.transform, alertPath);
            alert = alertTransform != null ? alertTransform.gameObject : null;
        }
        if (alert != null) alert.SetActive(false);

        AlmanacCategory materialCategory = new AlmanacCategory
        {
            categoryName = "Materials",
            tabType = AlmanacTabType.Materials,
            visibilityMode = TabVisibility.Normal,
            tabButton = tabButton,
            inactiveSprite = lesson.inactiveSprite,
            activeSprite = lesson.activeSprite,
            tabAlertIcon = alert,
            leftPageZone = leftZone,
            rightPageZone = rightZone
        };
        manager.categories.Add(materialCategory);

        tabButton.gameObject.SetActive(true);
        leftZone.gameObject.SetActive(true);
        rightZone.gameObject.SetActive(true);
        EnsureSeparateMaterialParent(materialCategory, lesson);
        PositionAfter(tabButton.transform as RectTransform, lesson.tabButton.transform as RectTransform);

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }

    private static Button CloneTabButton(AlmanacCategory lesson)
    {
        GameObject clone = UnityEngine.Object.Instantiate(
            lesson.tabButton.gameObject, lesson.tabButton.transform.parent);
        clone.name = "MaterialsTabButton";
        clone.transform.SetSiblingIndex(lesson.tabButton.transform.GetSiblingIndex() + 1);

        Button button = clone.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        ReplaceVisibleText(clone.transform, "Lessons", "Materials");
        ReplaceNames(clone.transform);
        return button;
    }

    private static Transform CloneZone(Transform source, string name)
    {
        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, source.parent);
        clone.name = name;
        clone.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);
        ReplaceNames(clone.transform);
        ReplaceVisibleText(clone.transform, "Lessons", "Materials");
        ReplaceVisibleText(clone.transform, "Lesson", "Material");
        return clone.transform;
    }

    private static void ConvertLessonArchive(Transform root)
    {
        AlmanacLessonTab[] lessonTabs = root.GetComponentsInChildren<AlmanacLessonTab>(true);
        foreach (AlmanacLessonTab lessonTab in lessonTabs)
        {
            SerializedObject source = new SerializedObject(lessonTab);
            AlmanacLessonButton oldTemplate = source.FindProperty("lessonButtonPrefab")
                ?.objectReferenceValue as AlmanacLessonButton;
            AlmanacMaterialButton newTemplate = null;

            if (oldTemplate != null)
            {
                newTemplate = oldTemplate.gameObject.GetComponent<AlmanacMaterialButton>();
                if (newTemplate == null)
                    newTemplate = oldTemplate.gameObject.AddComponent<AlmanacMaterialButton>();
                CopyButtonWiring(oldTemplate, newTemplate);
                UnityEngine.Object.DestroyImmediate(oldTemplate);
            }

            AlmanacMaterialTab materialTab = lessonTab.gameObject.GetComponent<AlmanacMaterialTab>();
            if (materialTab == null)
                materialTab = lessonTab.gameObject.AddComponent<AlmanacMaterialTab>();

            SerializedObject target = new SerializedObject(materialTab);
            CopyObjectReference(source, target, "buttonContainer");
            CopyObjectReference(source, target, "emptyStateRoot");
            CopyObjectReference(source, target, "emptyStateText");
            target.FindProperty("materialButtonPrefab").objectReferenceValue = newTemplate;
            target.FindProperty("showLockedMaterials").boolValue = false;
            target.FindProperty("sortAlphabetically").boolValue = true;
            target.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(materialTab);
            UnityEngine.Object.DestroyImmediate(lessonTab);
        }
    }

    private static void CopyButtonWiring(AlmanacLessonButton sourceComponent, AlmanacMaterialButton targetComponent)
    {
        SerializedObject source = new SerializedObject(sourceComponent);
        SerializedObject target = new SerializedObject(targetComponent);
        string[] objectFields =
        {
            "button", "titleText", "thumbnailImage", "backgroundImage", "lockedOverlay", "lockedLabel"
        };
        foreach (string field in objectFields) CopyObjectReference(source, target, field);

        target.FindProperty("unlockedColor").colorValue = source.FindProperty("unlockedColor").colorValue;
        target.FindProperty("lockedColor").colorValue = source.FindProperty("lockedColor").colorValue;
        target.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(targetComponent);
    }

    private static void CopyObjectReference(SerializedObject source, SerializedObject target, string propertyName)
    {
        SerializedProperty sourceProperty = source.FindProperty(propertyName);
        SerializedProperty targetProperty = target.FindProperty(propertyName);
        if (sourceProperty != null && targetProperty != null)
            targetProperty.objectReferenceValue = sourceProperty.objectReferenceValue;
    }

    private static void PositionAfter(RectTransform target, RectTransform source)
    {
        if (target == null || source == null) return;
        target.sizeDelta = source.sizeDelta;
        Vector2 position = source.anchoredPosition;
        float step = Mathf.Abs(source.sizeDelta.x);
        if (step < 1f) step = 160f;
        position.x += step;
        target.anchoredPosition = position;
        EditorUtility.SetDirty(target);
    }

    private static void EnsureSeparateMaterialParent(
        AlmanacCategory material,
        AlmanacCategory lesson)
    {
        if (material == null || material.leftPageZone == null || material.rightPageZone == null)
            return;

        Transform lessonParent = lesson != null && lesson.leftPageZone != null
            ? lesson.leftPageZone.parent
            : null;
        Transform materialParent = material.leftPageZone.parent;

        // A previous setup placed both archives under EducationalTab. Give the
        // material pages their own always-active container instead.
        bool sharesLessonParent = lessonParent != null &&
                                  (materialParent == lessonParent ||
                                   material.rightPageZone.parent == lessonParent);
        if (sharesLessonParent)
        {
            Transform existingRoot = lessonParent.parent != null
                ? lessonParent.parent.Find("MaterialsArchiveTab")
                : null;

            RectTransform archiveRoot;
            if (existingRoot != null)
            {
                archiveRoot = existingRoot as RectTransform;
            }
            else
            {
                GameObject rootObject = new GameObject("MaterialsArchiveTab", typeof(RectTransform));
                rootObject.layer = lessonParent.gameObject.layer;
                archiveRoot = rootObject.GetComponent<RectTransform>();
                archiveRoot.SetParent(lessonParent.parent, false);
                CopyRectTransform(lessonParent as RectTransform, archiveRoot);
                archiveRoot.SetSiblingIndex(lessonParent.GetSiblingIndex() + 1);
            }

            material.leftPageZone.SetParent(archiveRoot, false);
            material.rightPageZone.SetParent(archiveRoot, false);
            materialParent = archiveRoot;
        }

        if (materialParent != null) materialParent.gameObject.SetActive(true);
        material.leftPageZone.gameObject.SetActive(true);
        material.rightPageZone.gameObject.SetActive(true);
        EditorUtility.SetDirty(material.leftPageZone);
        EditorUtility.SetDirty(material.rightPageZone);
    }

    private static void CopyRectTransform(RectTransform source, RectTransform target)
    {
        if (source == null || target == null) return;
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.pivot = source.pivot;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static void ReplaceNames(Transform root)
    {
        foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
            item.name = item.name.Replace("Lessons", "Materials").Replace("Lesson", "Material");
    }

    private static void ReplaceVisibleText(Transform root, string from, string to)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (string.IsNullOrEmpty(text.text) || text.text.IndexOf(from, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            text.text = ReplaceIgnoreCase(text.text, from, to);
            EditorUtility.SetDirty(text);
        }
    }

    private static string ReplaceIgnoreCase(string source, string oldValue, string newValue)
    {
        int index = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        return index < 0
            ? source
            : source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (root == null || target == null) return string.Empty;
        if (root == target) return string.Empty;

        string path = target.name;
        Transform cursor = target.parent;
        while (cursor != null && cursor != root)
        {
            path = cursor.name + "/" + path;
            cursor = cursor.parent;
        }
        return cursor == root ? path : string.Empty;
    }

    private static Transform FindRelative(Transform root, string path)
    {
        if (root == null) return null;
        return string.IsNullOrEmpty(path) ? root : root.Find(path);
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }
}
#endif
