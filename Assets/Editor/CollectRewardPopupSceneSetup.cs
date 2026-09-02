#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CollectRewardPopupSceneSetup
{
    private const string SessionKey = "CivilCraft.CollectRewardPopupSceneSetup.v1";
    private const string BhanScenePath = "Assets/Scenes/BHAN HOUSE.unity";
    private const string CanyonScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string PrefabPath = "Assets/Prefabs/UI/CollectRewardSystem.prefab";

    static CollectRewardPopupSceneSetup()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
    }

    [MenuItem("Tools/Civil Craft/Setup Shared Collect Reward Popup")]
    public static void SetupFromMenu()
    {
        SetupSharedPopup();
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryAutomaticSetup;
    }

    private static void TryAutomaticSetup()
    {
        if (SessionState.GetBool(SessionKey, false) ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        if (SetupSharedPopup())
            SessionState.SetBool(SessionKey, true);
    }

    private static bool SetupSharedPopup()
    {
        Scene previousActive = SceneManager.GetActiveScene();
        Scene bhanScene = OpenSceneIfNeeded(BhanScenePath, out bool openedBhan);
        Scene canyonScene = OpenSceneIfNeeded(CanyonScenePath, out bool openedCanyon);

        try
        {
            ItemUnlockUI sourceController = FindSceneComponent<ItemUnlockUI>(bhanScene);
            if (sourceController == null || sourceController.popupPanel == null)
            {
                Debug.LogError(
                    "[CollectRewardPopupSceneSetup] Bhan House's manually designed ItemUnlockUI/ItemUnlockPanel was not found.");
                return false;
            }

            GameObject bhanCanvas = FindSceneObject(bhanScene, "MainCanvas");
            if (bhanCanvas == null)
            {
                Debug.LogError("[CollectRewardPopupSceneSetup] MainCanvas is missing in Bhan House.");
                return false;
            }

            List<string> hiddenUIPaths = CollectRelativeHidePaths(
                sourceController,
                bhanCanvas.transform);
            GameObject prefab = CreatePrefabFromBhanDesign(sourceController);
            if (prefab == null) return false;

            InstallInScene(bhanScene, prefab, hiddenUIPaths);
            InstallInScene(canyonScene, prefab, hiddenUIPaths);

            EditorSceneManager.MarkSceneDirty(bhanScene);
            EditorSceneManager.MarkSceneDirty(canyonScene);
            EditorSceneManager.SaveScene(bhanScene);
            EditorSceneManager.SaveScene(canyonScene);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[CollectRewardPopupSceneSetup] Bhan House Collect panel is now a shared prefab in Bhan House and Canyon Crossing.");
            return true;
        }
        finally
        {
            if (openedCanyon && canyonScene.IsValid() && canyonScene.isLoaded &&
                (!previousActive.IsValid() || previousActive.path != CanyonScenePath))
            {
                EditorSceneManager.CloseScene(canyonScene, true);
            }

            if (openedBhan && bhanScene.IsValid() && bhanScene.isLoaded &&
                (!previousActive.IsValid() || previousActive.path != BhanScenePath))
            {
                EditorSceneManager.CloseScene(bhanScene, true);
            }

            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
        }
    }

    private static GameObject CreatePrefabFromBhanDesign(ItemUnlockUI source)
    {
        GameObject root = new GameObject("CollectRewardSystem", typeof(RectTransform));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.localScale = Vector3.one;

        GameObject panelClone = Object.Instantiate(source.popupPanel);
        panelClone.name = "ItemUnlockPanel";
        panelClone.transform.SetParent(root.transform, false);

        string titlePath = RelativePath(
            source.popupPanel.transform,
            source.itemNameText != null ? source.itemNameText.transform : null);
        string iconPath = RelativePath(
            source.popupPanel.transform,
            source.itemIconImage != null ? source.itemIconImage.transform : null);
        string collectPath = RelativePath(
            source.popupPanel.transform,
            source.collectButton != null ? source.collectButton.transform : null);

        ItemUnlockUI controller = root.AddComponent<ItemUnlockUI>();
        controller.popupPanel = panelClone;
        controller.itemNameText = FindRelative(panelClone.transform, titlePath)
            ?.GetComponent<TMPro.TextMeshProUGUI>();
        controller.itemIconImage = FindRelative(panelClone.transform, iconPath)
            ?.GetComponent<UnityEngine.UI.Image>();
        controller.collectButton = FindRelative(panelClone.transform, collectPath)
            ?.GetComponent<UnityEngine.UI.Button>();
        controller.uiElementsToHide.Clear();

        if (controller.itemNameText == null || controller.itemIconImage == null ||
            controller.collectButton == null)
        {
            Debug.LogError(
                "[CollectRewardPopupSceneSetup] Could not remap one or more controls inside Bhan House's Collect panel.");
            Object.DestroyImmediate(root);
            return null;
        }

        panelClone.SetActive(false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void InstallInScene(
        Scene scene,
        GameObject prefab,
        List<string> hiddenUIPaths)
    {
        if (!scene.IsValid() || !scene.isLoaded || prefab == null) return;

        GameObject mainCanvas = FindSceneObject(scene, "MainCanvas");
        if (mainCanvas == null)
        {
            Debug.LogWarning(
                $"[CollectRewardPopupSceneSetup] MainCanvas is missing in '{scene.name}'.");
            return;
        }

        ItemUnlockUI[] oldControllers = FindSceneComponents<ItemUnlockUI>(scene);
        foreach (ItemUnlockUI oldController in oldControllers)
        {
            if (oldController == null) continue;
            GameObject oldPanel = oldController.popupPanel;
            GameObject oldOwner = oldController.gameObject;

            if (oldPanel != null && oldPanel != oldOwner &&
                oldPanel.scene == scene)
            {
                Object.DestroyImmediate(oldPanel);
            }

            if (oldOwner != null && oldOwner.scene == scene)
                Object.DestroyImmediate(oldOwner);
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
        if (instance == null) return;

        instance.name = "CollectRewardSystem";
        instance.transform.SetParent(mainCanvas.transform, false);
        instance.transform.SetAsLastSibling();

        RectTransform rect = instance.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        ItemUnlockUI controller = instance.GetComponent<ItemUnlockUI>();
        if (controller != null)
        {
            controller.uiElementsToHide.Clear();
            foreach (string relativePath in hiddenUIPaths)
            {
                Transform target = FindRelative(mainCanvas.transform, relativePath);
                if (target != null && target.gameObject != instance)
                    controller.uiElementsToHide.Add(target.gameObject);
            }

            if (controller.popupPanel != null)
                controller.popupPanel.SetActive(false);
            EditorUtility.SetDirty(controller);
        }
    }

    private static List<string> CollectRelativeHidePaths(
        ItemUnlockUI source,
        Transform sourceCanvas)
    {
        List<string> paths = new List<string>();
        if (source == null || sourceCanvas == null || source.uiElementsToHide == null)
            return paths;

        foreach (GameObject target in source.uiElementsToHide)
        {
            if (target == null || !target.transform.IsChildOf(sourceCanvas)) continue;
            string path = RelativePath(sourceCanvas, target.transform);
            if (!string.IsNullOrEmpty(path) && !paths.Contains(path)) paths.Add(path);
        }

        return paths;
    }

    private static Scene OpenSceneIfNeeded(string scenePath, out bool opened)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        opened = !scene.IsValid() || !scene.isLoaded;
        return opened
            ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive)
            : scene;
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        T[] components = FindSceneComponents<T>(scene);
        return components.Length > 0 ? components[0] : null;
    }

    private static T[] FindSceneComponents<T>(Scene scene) where T : Component
    {
        List<T> results = new List<T>();
        if (!scene.IsValid() || !scene.isLoaded) return results.ToArray();

        foreach (GameObject root in scene.GetRootGameObjects())
            results.AddRange(root.GetComponentsInChildren<T>(true));
        return results.ToArray();
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindRecursive(root.transform, objectName);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static Transform FindRecursive(Transform root, string objectName)
    {
        if (root == null) return null;
        if (root.name == objectName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindRecursive(root.GetChild(i), objectName);
            if (found != null) return found;
        }
        return null;
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (root == null || target == null || target == root) return string.Empty;
        List<string> names = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Add(current.name);
            current = current.parent;
        }
        if (current != root) return string.Empty;
        names.Reverse();
        return string.Join("/", names);
    }

    private static Transform FindRelative(Transform root, string relativePath)
    {
        if (root == null) return null;
        if (string.IsNullOrEmpty(relativePath)) return root;
        return root.Find(relativePath);
    }
}
#endif
