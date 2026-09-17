#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds one visual-only prefab from Character_Cosmetics and uses it for the
/// loading screen fallback and every Almanac photobooth display.
/// </summary>
public static class PlayerPreviewModelMigration
{
    private const string NewModelAssetPath =
        "Assets/Elements/Characters/Character_Cosmetics.fbx";
    private const string PlayerControllerPath =
        "Assets/Animations/Player/PlayerAnimator.controller";
    private const string PreviewPrefabPath =
        "Assets/Resources/Loading/NewCharacterPreview.prefab";
    private const string LoadingAssetsPath =
        "Assets/Resources/Loading/LoadingScreenAssets.asset";
    private const string ManagersPrefabPath =
        "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private const string AlmanacRenderTexturePath =
        "Assets/Materials/Render/New Render Texture.renderTexture";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    private static readonly string[] VariantCategoryNames =
    {
        "Hair", "Shirt", "Pants", "Shoes", "Accessories"
    };

    private static readonly string[] VariantNamePrefixes =
    {
        "Hair_", "Top_", "Bottom_", "Shoes_", "Accesories_", "Accessories_"
    };

    [MenuItem("Tools/Civil Craft/Migrate Loading and Almanac To New Character")]
    public static void MigrateAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Player Preview Migration] Exit Play Mode before migrating previews.");
            return;
        }

        GameObject previewPrefab = BuildSharedPreviewPrefab();
        if (previewPrefab == null) return;

        AssignLoadingFallback(previewPrefab);
        int migratedDisplays = MigrateManagersPrefab(previewPrefab);
        foreach (string scenePath in ScenePaths)
            migratedDisplays += MigrateScene(scenePath, previewPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[Player Preview Migration] New loading fallback assigned and " +
            $"{migratedDisplays} Almanac display(s) migrated to Character_Cosmetics.");
    }

    private static GameObject BuildSharedPreviewPrefab()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NewModelAssetPath);
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
        if (modelAsset == null || controller == null)
        {
            Debug.LogError(
                "[Player Preview Migration] Character_Cosmetics or PlayerAnimator.controller is missing.");
            return null;
        }

        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene temporaryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(modelAsset, temporaryScene) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[Player Preview Migration] Could not instantiate Character_Cosmetics.");
                return null;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(instance))
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }

            instance.name = "NewCharacterPreview";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            ConfigurePreview(instance, controller);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, PreviewPrefabPath);
            if (saved == null)
                Debug.LogError($"[Player Preview Migration] Could not save {PreviewPrefabPath}.");
            return saved;
        }
        finally
        {
            EditorSceneManager.CloseScene(temporaryScene, true);
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);
        }
    }

    private static void ConfigurePreview(GameObject root, RuntimeAnimatorController controller)
    {
        foreach (string categoryName in VariantCategoryNames)
        {
            Transform category = FindDescendant(root.transform, categoryName);
            if (category == null) continue;
            category.gameObject.SetActive(true);
            foreach (Transform variant in category)
                variant.gameObject.SetActive(false);
        }

        // The imported FBX itself stores variants as flat siblings, while the
        // gameplay scene organizes them into category containers. Support both
        // layouts so the fallback/photobooth never renders every outfit at once.
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate == root.transform) continue;
            if (VariantNamePrefixes.Any(prefix =>
                    candidate.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                candidate.gameObject.SetActive(false);
        }

        GameObject hardHat = FindDescendantByNormalizedName(root.transform, "BaseHardHat")?.gameObject;
        if (hardHat != null) hardHat.SetActive(false);

        Transform hips = FindGenericBone(root.transform, "Hips");
        Transform animationRoot = hips != null ? hips.parent : null;
        if (animationRoot == null)
            throw new InvalidOperationException("Character_Cosmetics has no mixamorig:Hips parent.");

        foreach (Animator candidate in root.GetComponentsInChildren<Animator>(true))
            if (candidate.transform != animationRoot) UnityEngine.Object.DestroyImmediate(candidate);

        Animator animator = animationRoot.GetComponent<Animator>();
        if (animator == null) animator = animationRoot.gameObject.AddComponent<Animator>();
        animator.avatar = null;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        PlayerCosmeticMirror mirror = root.GetComponent<PlayerCosmeticMirror>();
        if (mirror == null) mirror = root.AddComponent<PlayerCosmeticMirror>();
        mirror.hats.Clear();
        if (hardHat != null)
        {
            mirror.hats.Add(new CosmeticItem
            {
                cosmeticID = "EngineeringHardHat",
                cosmeticModel = hardHat
            });
        }

        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(mirror);
        EditorUtility.SetDirty(root);
    }

    private static void AssignLoadingFallback(GameObject previewPrefab)
    {
        LoadingScreenAssets loadingAssets =
            AssetDatabase.LoadAssetAtPath<LoadingScreenAssets>(LoadingAssetsPath);
        if (loadingAssets == null)
        {
            Debug.LogError($"[Player Preview Migration] Loading assets are missing at {LoadingAssetsPath}.");
            return;
        }

        loadingAssets.fallbackPlayerPrefab = previewPrefab;
        EditorUtility.SetDirty(loadingAssets);
    }

    private static int MigrateManagersPrefab(GameObject previewPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ManagersPrefabPath);
        if (root == null) return 0;

        try
        {
            int count = ReplaceLegacyDisplays(root.scene, previewPrefab);
            count += RepairExistingDisplays(root.scene);
            if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, ManagersPrefabPath);
            return count;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int MigrateScene(string scenePath, GameObject previewPrefab)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForMigration = !scene.IsValid() || !scene.isLoaded;
        if (openedForMigration)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        int count = ReplaceLegacyDisplays(scene, previewPrefab);
        count += RepairExistingDisplays(scene);
        if (count > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (openedForMigration) EditorSceneManager.CloseScene(scene, true);
        return count;
    }

    private static int ReplaceLegacyDisplays(Scene scene, GameObject previewPrefab)
    {
        GameObject[] legacyDisplays = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(candidate =>
                candidate != null &&
                candidate.name.StartsWith("Character_w_Clothes_v4", StringComparison.Ordinal) &&
                IsUnderNamedAncestor(candidate, "PHOTOBOOTH"))
            .Select(candidate => candidate.gameObject)
            .Distinct()
            .ToArray();

        int count = 0;
        foreach (GameObject legacyDisplay in legacyDisplays)
        {
            Transform legacyTransform = legacyDisplay.transform;
            Transform parent = legacyTransform.parent;
            int siblingIndex = legacyTransform.GetSiblingIndex();
            Vector3 localPosition = legacyTransform.localPosition;
            Quaternion localRotation = legacyTransform.localRotation;
            Vector3 localScale = legacyTransform.localScale;
            bool active = legacyDisplay.activeSelf;
            Transform legacyCamera = legacyDisplay.GetComponentInChildren<Camera>(true)?.transform;

            GameObject replacement = PrefabUtility.InstantiatePrefab(previewPrefab, scene) as GameObject;
            if (replacement == null) continue;
            replacement.name = "NewCharacterModel (Almanac)";
            replacement.transform.SetParent(parent, false);
            replacement.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            replacement.transform.localScale = localScale;
            replacement.transform.SetSiblingIndex(siblingIndex);
            replacement.SetActive(active);

            // The original Almanac camera and light were children of the old
            // display model. Preserve that rig before destroying the model.
            if (legacyCamera != null)
                legacyCamera.SetParent(replacement.transform, false);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (AlmanacPortraitRotator rotator in
                         root.GetComponentsInChildren<AlmanacPortraitRotator>(true))
                {
                    SerializedObject serializedRotator = new SerializedObject(rotator);
                    SerializedProperty target = serializedRotator.FindProperty("rotationTarget");
                    if (target == null || target.objectReferenceValue != legacyTransform) continue;
                    target.objectReferenceValue = replacement.transform;
                    serializedRotator.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(rotator);
                }
            }

            UnityEngine.Object.DestroyImmediate(legacyDisplay);
            count++;
        }

        return count;
    }

    private static int RepairExistingDisplays(Scene scene)
    {
        GameObject[] displays = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PlayerCosmeticMirror>(true))
            .Where(mirror =>
                mirror != null &&
                IsUnderNamedAncestor(mirror.transform, "PHOTOBOOTH"))
            .Select(mirror => mirror.gameObject)
            .Distinct()
            .ToArray();

        int changes = 0;
        foreach (GameObject display in displays)
            if (EnsureAlmanacCamera(scene, display)) changes++;
        return changes;
    }

    private static bool EnsureAlmanacCamera(Scene scene, GameObject display)
    {
        bool changed = false;
        Camera portraitCamera = display.GetComponentInChildren<Camera>(true);
        if (portraitCamera == null)
        {
            GameObject cameraObject = new GameObject("UICamera");
            cameraObject.transform.SetParent(display.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 10.32f, 15.72f);
            cameraObject.transform.localRotation =
                new Quaternion(0f, -0.9979004f, 0.06476738f, 0f);

            portraitCamera = cameraObject.AddComponent<Camera>();
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            portraitCamera.nearClipPlane = 0.3f;
            portraitCamera.farClipPlane = 1000f;
            portraitCamera.fieldOfView = 60f;
            portraitCamera.cullingMask = ~0;
            portraitCamera.allowHDR = true;
            portraitCamera.allowMSAA = true;
            cameraObject.AddComponent<UniversalAdditionalCameraData>();

            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(cameraObject.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0f, -48.501343f);
            lightObject.transform.localRotation =
                new Quaternion(-0.011099398f, 0.00007672631f, -0.006912079f, 0.9999146f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.62f, 0.7f, 0.82f, 1f);
            light.intensity = 0.32f;
            light.shadows = LightShadows.None;
            lightObject.AddComponent<UniversalAdditionalLightData>();
            changed = true;
        }

        RenderTexture portraitTexture =
            AssetDatabase.LoadAssetAtPath<RenderTexture>(AlmanacRenderTexturePath);
        if (portraitCamera.targetTexture != portraitTexture)
        {
            portraitCamera.targetTexture = portraitTexture;
            EditorUtility.SetDirty(portraitCamera);
            changed = true;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (AlmanacPortraitRotator rotator in
                     root.GetComponentsInChildren<AlmanacPortraitRotator>(true))
            {
                SerializedObject serializedRotator = new SerializedObject(rotator);
                SerializedProperty target = serializedRotator.FindProperty("rotationTarget");
                if (target == null || target.objectReferenceValue != display.transform) continue;

                SerializedProperty fixedCamera = serializedRotator.FindProperty("cameraToKeepFixed");
                if (fixedCamera != null && fixedCamera.objectReferenceValue != portraitCamera.transform)
                {
                    fixedCamera.objectReferenceValue = portraitCamera.transform;
                    serializedRotator.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(rotator);
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool IsUnderNamedAncestor(Transform candidate, string ancestorName)
    {
        for (Transform current = candidate.parent; current != null; current = current.parent)
        {
            if (string.Equals(current.name, ancestorName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static Transform FindDescendant(Transform root, string exactName)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.name, exactName, StringComparison.OrdinalIgnoreCase));
    }

    private static Transform FindDescendantByNormalizedName(Transform root, string normalizedName)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate =>
            {
                string normalized = new string(candidate.name.Where(char.IsLetterOrDigit).ToArray());
                return string.Equals(normalized, normalizedName, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static Transform FindGenericBone(Transform root, string expectedName)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate =>
            {
                string candidateName = candidate.name;
                int namespaceEnd = candidateName.LastIndexOf(':');
                if (namespaceEnd >= 0) candidateName = candidateName.Substring(namespaceEnd + 1);
                return string.Equals(candidateName, expectedName, StringComparison.OrdinalIgnoreCase);
            });
    }
}
#endif
