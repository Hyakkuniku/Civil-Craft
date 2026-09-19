#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reconnects a scene-authored Character_Cosmetics hierarchy after its outfit
/// objects have been replaced.  It deliberately does not recreate any UI.
/// </summary>
[InitializeOnLoad]
public static class NewCharacterModelWiring
{
    private const string ControllerPath = "Assets/Animations/Player/PlayerAnimator.controller";
    private const string ModelPath = "Assets/Elements/Characters/Character_Cosmetics.fbx";
    private const string PreviewPrefabPath = "Assets/Resources/Loading/NewCharacterPreview.prefab";
    private const string GeneratedSkinFolder = "Assets/Generated/CharacterSkins";
    private const string SafetyVestRepairMarker = "CivilCraft.SafetyVestBodySkin.v1";
    private const string AutoRunKey = "CivilCraft.NewCharacterModelWiring.v6";

    static NewCharacterModelWiring()
    {
        EditorApplication.delayCall += AutoWireOnce;
    }

    [MenuItem("Tools/Civil Craft/Wire New Character Cosmetics And Rig")]
    public static void WireOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Character Wiring] Exit Play Mode before wiring the character.");
            return;
        }

        CharacterCustomizationSetup.EnsureDefinitions();
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<Avatar>()
            .FirstOrDefault(candidate => candidate != null && candidate.isValid);

        int players = 0;
        int mirrors = 0;
        int reboundRenderers = 0;
        int unresolvedRenderers = 0;
        int generatedSkins = 0;
        int repairedVests = 0;
        Transform gameplayPreviewSource = null;

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.IsValid() || !scene.isLoaded) continue;

            bool sceneChanged = false;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (PlayerMotor motor in sceneRoot.GetComponentsInChildren<PlayerMotor>(true))
                {
                    if (!TryFindVisual(motor.transform, out Transform visual)) continue;
                    if (WirePlayer(motor, visual, controller, avatar,
                            ref reboundRenderers, ref unresolvedRenderers, ref generatedSkins,
                            ref repairedVests))
                    {
                        if (gameplayPreviewSource == null) gameplayPreviewSource = visual;
                        players++;
                        sceneChanged = true;
                    }
                }

                foreach (PlayerCosmeticMirror mirror in
                         sceneRoot.GetComponentsInChildren<PlayerCosmeticMirror>(true))
                {
                    Transform visual = FindVisualForMirror(mirror.transform);
                    if (visual == null) continue;
                    CharacterCustomizationSetup.ConfigureBindings(mirror, visual);
                    ConnectLegacyHardHat(mirror, visual);
                    ApplyAuthoredDefaultVisibility(mirror.cosmeticBindings);
                    EditorUtility.SetDirty(mirror);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(mirror);
                    mirrors++;
                    sceneChanged = true;
                }
            }

            if (sceneChanged)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!string.IsNullOrWhiteSpace(scene.path))
                    EditorSceneManager.SaveScene(scene);
            }
        }

        if (gameplayPreviewSource != null)
            RebuildSharedPreviewPrefab(gameplayPreviewSource);
        else
            WireSharedPreviewPrefab();
        AssetDatabase.SaveAssets();

        string unresolvedMessage = unresolvedRenderers == 0
            ? "all skinned meshes use Base_Rig"
            : unresolvedRenderers + " skinned mesh renderer(s) still need artist bone data";
        Debug.Log(
            "[Character Wiring] Complete: " + players + " gameplay player(s), " + mirrors +
            " preview mirror(s), " + reboundRenderers + " renderer(s) rebound, " +
            generatedSkins + " static cosmetic mesh(es) skinned, " + repairedVests +
            " safety vest mesh(es) repaired; " +
            unresolvedMessage + ". Save any dirty open scene.");
    }

    [MenuItem("Tools/Civil Craft/Wire New Character Cosmetics And Rig", true)]
    private static bool CanWireOpenScenes()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    // Dedicated non-interactive entry point used by project maintenance and CI.
    public static void WireCanyonSceneBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/CanyonCrossing.unity", OpenSceneMode.Single);
        WireOpenScenes();
    }

    private static void AutoWireOnce()
    {
        if (SessionState.GetBool(AutoRunKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += AutoWireOnce;
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;
        bool containsNewPlayer = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PlayerMotor>(true))
            .Any(motor => TryFindVisual(motor.transform, out _));
        if (!containsNewPlayer) return;

        SessionState.SetBool(AutoRunKey, true);
        WireOpenScenes();
    }

    private static bool WirePlayer(
        PlayerMotor motor,
        Transform visual,
        RuntimeAnimatorController controller,
        Avatar avatar,
        ref int reboundRenderers,
        ref int unresolvedRenderers,
        ref int generatedSkins,
        ref int repairedVests)
    {
        Transform hips = FindGenericBone(visual, "Hips");
        Transform animationRoot = hips != null ? hips.parent : null;
        if (animationRoot == null)
        {
            Debug.LogError(
                "[Character Wiring] " + visual.name +
                " has no Base_Rig/mixamorig:Hips hierarchy. Animator was not changed.", visual);
            return false;
        }

        if (controller == null)
        {
            Debug.LogError("[Character Wiring] Missing PlayerAnimator.controller at " + ControllerPath, visual);
            return false;
        }

        Undo.RecordObject(visual.gameObject, "Wire new character model");
        visual.gameObject.SetActive(true);

        Animator animator = animationRoot.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(animationRoot.gameObject);
        else Undo.RecordObject(animator, "Configure new character Animator");

        animator.runtimeAnimatorController = controller;
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(animator);

        // There must be one Animator, at the direct parent of mixamorig:Hips.
        foreach (Animator misplaced in visual.GetComponentsInChildren<Animator>(true)
                     .Where(candidate => candidate != animator).ToArray())
            Undo.DestroyObjectImmediate(misplaced);

        generatedSkins += SkinStaticCosmetics(visual, animationRoot);
        repairedVests += RepairSafetyVestSkinning(visual, animationRoot);
        RebindSkinnedMeshes(visual, animationRoot, ref reboundRenderers, ref unresolvedRenderers);

        Undo.RecordObject(motor, "Assign new character Animator");
        motor.playerAnimator = animator;
        EditorUtility.SetDirty(motor);
        PrefabUtility.RecordPrefabInstancePropertyModifications(motor);

        PlayerCosmetics cosmetics = motor.GetComponent<PlayerCosmetics>();
        if (cosmetics == null) cosmetics = Undo.AddComponent<PlayerCosmetics>(motor.gameObject);
        CharacterCustomizationSetup.ConfigureBindings(cosmetics, visual);
        ConnectLegacyHardHat(cosmetics, visual);
        ApplyAuthoredDefaultVisibility(cosmetics.cosmeticBindings);
        EditorUtility.SetDirty(cosmetics);
        PrefabUtility.RecordPrefabInstancePropertyModifications(cosmetics);

        foreach (CinematicDirector cinematic in Resources.FindObjectsOfTypeAll<CinematicDirector>())
        {
            if (cinematic == null || cinematic.gameObject.scene != motor.gameObject.scene ||
                cinematic.playerActor != motor.transform)
                continue;
            Undo.RecordObject(cinematic, "Reconnect cinematic character Animator");
            cinematic.playerAnimator = animator;
            EditorUtility.SetDirty(cinematic);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cinematic);
        }

        ValidateBindings(cosmetics.cosmeticBindings, visual);
        return true;
    }

    private static void RebindSkinnedMeshes(
        Transform visual,
        Transform animationRoot,
        ref int reboundRenderers,
        ref int unresolvedRenderers)
    {
        Dictionary<string, Transform> rigBones = new Dictionary<string, Transform>(
            StringComparer.OrdinalIgnoreCase);
        foreach (Transform bone in animationRoot.GetComponentsInChildren<Transform>(true))
        {
            string key = BoneKey(bone.name);
            if (!rigBones.ContainsKey(key)) rigBones.Add(key, bone);
        }

        foreach (SkinnedMeshRenderer renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform[] currentBones = renderer.bones;
            if (currentBones == null || currentBones.Length == 0) continue;

            bool alreadyBound = currentBones.All(bone => bone != null && bone.IsChildOf(animationRoot)) &&
                                renderer.rootBone != null &&
                                renderer.rootBone.IsChildOf(animationRoot);
            if (alreadyBound) continue;

            Transform[] replacementBones = new Transform[currentBones.Length];
            bool resolved = true;
            for (int index = 0; index < currentBones.Length; index++)
            {
                Transform oldBone = currentBones[index];
                if (oldBone == null || !rigBones.TryGetValue(BoneKey(oldBone.name), out replacementBones[index]))
                {
                    resolved = false;
                    break;
                }
            }

            Transform replacementRoot = null;
            if (renderer.rootBone != null)
                rigBones.TryGetValue(BoneKey(renderer.rootBone.name), out replacementRoot);
            if (replacementRoot == null && rigBones.TryGetValue("hips", out Transform hips))
                replacementRoot = hips;

            if (!resolved || replacementRoot == null)
            {
                unresolvedRenderers++;
                Debug.LogError(
                    "[Character Wiring] Could not safely rebind " + renderer.name +
                    " because its source bone names are missing. Re-export that mesh with the Base_Rig skin.",
                    renderer);
                continue;
            }

            Undo.RecordObject(renderer, "Rebind cosmetic mesh to Base_Rig");
            renderer.bones = replacementBones;
            renderer.rootBone = replacementRoot;
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            reboundRenderers++;
        }
    }

    /// <summary>
    /// Boy_Cosmetics2 currently imports its wardrobe pieces as MeshRenderers,
    /// so simply assigning Base_Rig cannot animate them. Transfer nearby body
    /// weights for deforming clothes and rigidly skin headwear/hair to Head.
    /// The generated meshes are project assets; the source FBX stays untouched.
    /// </summary>
    private static int SkinStaticCosmetics(Transform visual, Transform animationRoot)
    {
        MeshFilter[] candidates = visual.GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => filter != null && filter.sharedMesh != null &&
                             filter.GetComponent<MeshRenderer>() != null &&
                             IsWardrobeMesh(filter.transform, visual))
            .ToArray();
        if (candidates.Length == 0) return 0;

        SkinnedMeshRenderer donor = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(renderer =>
                string.Equals(renderer.name.Trim(), "Body", StringComparison.OrdinalIgnoreCase));
        if (donor == null || donor.sharedMesh == null || donor.bones == null || donor.bones.Length == 0)
        {
            Debug.LogError(
                "[Character Wiring] Cannot skin the new static wardrobe meshes because the Body " +
                "SkinnedMeshRenderer is missing.", visual);
            return 0;
        }

        HashSet<string> temporarilyReadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            MakeReadable(donor.sharedMesh, temporarilyReadable);
            foreach (MeshFilter candidate in candidates)
                MakeReadable(candidate.sharedMesh, temporarilyReadable);

            // Reimporting read/write settings can replace sub-asset instances.
            donor = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .First(renderer =>
                    string.Equals(renderer.name.Trim(), "Body", StringComparison.OrdinalIgnoreCase));
            candidates = visual.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter != null && filter.sharedMesh != null &&
                                 filter.GetComponent<MeshRenderer>() != null &&
                                 IsWardrobeMesh(filter.transform, visual))
                .ToArray();

            EnsureFolder(GeneratedSkinFolder);
            Vector3[] donorVertices = donor.sharedMesh.vertices;
            BoneWeight[] donorWeights = donor.sharedMesh.boneWeights;
            if (donorVertices.Length == 0 || donorWeights.Length != donorVertices.Length)
            {
                Debug.LogError(
                    "[Character Wiring] Body mesh has no usable four-weight skin data.", donor);
                return 0;
            }

            SpatialVertexLookup bodyLookup = new SpatialVertexLookup(donorVertices, donor.sharedMesh.bounds);
            Transform head = FindGenericBone(animationRoot, "Head");
            int headBoneIndex = Array.IndexOf(donor.bones, head);
            int converted = 0;

            foreach (MeshFilter filter in candidates)
            {
                MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
                Material[] sourceMaterials = sourceRenderer.sharedMaterials;
                UnityEngine.Rendering.ShadowCastingMode sourceShadowCasting =
                    sourceRenderer.shadowCastingMode;
                bool sourceReceiveShadows = sourceRenderer.receiveShadows;
                UnityEngine.Rendering.LightProbeUsage sourceLightProbes = sourceRenderer.lightProbeUsage;
                UnityEngine.Rendering.ReflectionProbeUsage sourceReflectionProbes =
                    sourceRenderer.reflectionProbeUsage;
                Transform sourceProbeAnchor = sourceRenderer.probeAnchor;
                Mesh sourceMesh = filter.sharedMesh;
                Mesh generated = UnityEngine.Object.Instantiate(sourceMesh);
                generated.name = sourceMesh.name + "_CivilCraftSkinned";

                Vector3[] clothingVertices = generated.vertices;
                BoneWeight[] weights = new BoneWeight[clothingVertices.Length];
                bool rigidToHead = ShouldRigidlyFollowHead(filter.transform, visual);

                if (rigidToHead && headBoneIndex >= 0)
                {
                    BoneWeight headWeight = new BoneWeight { boneIndex0 = headBoneIndex, weight0 = 1f };
                    for (int index = 0; index < weights.Length; index++) weights[index] = headWeight;
                }
                else
                {
                    for (int index = 0; index < clothingVertices.Length; index++)
                    {
                        Vector3 world = filter.transform.TransformPoint(clothingVertices[index]);
                        Vector3 donorLocal = donor.transform.InverseTransformPoint(world);
                        int nearest = bodyLookup.FindNearest(donorLocal);
                        weights[index] = donorWeights[nearest];
                    }
                }

                Matrix4x4[] bindposes = new Matrix4x4[donor.bones.Length];
                for (int boneIndex = 0; boneIndex < donor.bones.Length; boneIndex++)
                    bindposes[boneIndex] = donor.bones[boneIndex].worldToLocalMatrix *
                                           filter.transform.localToWorldMatrix;
                generated.boneWeights = weights;
                generated.bindposes = bindposes;
                generated.RecalculateBounds();

                string meshPath = GeneratedMeshPath(sourceMesh, filter.name);
                Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (saved == null)
                {
                    AssetDatabase.CreateAsset(generated, meshPath);
                    saved = generated;
                }
                else
                {
                    EditorUtility.CopySerialized(generated, saved);
                    UnityEngine.Object.DestroyImmediate(generated);
                    EditorUtility.SetDirty(saved);
                }

                SkinnedMeshRenderer skinned = Undo.AddComponent<SkinnedMeshRenderer>(filter.gameObject);
                skinned.sharedMesh = saved;
                skinned.sharedMaterials = sourceMaterials;
                skinned.bones = donor.bones;
                skinned.rootBone = donor.rootBone;
                skinned.localBounds = saved.bounds;
                skinned.updateWhenOffscreen = donor.updateWhenOffscreen;
                skinned.shadowCastingMode = sourceShadowCasting;
                skinned.receiveShadows = sourceReceiveShadows;
                skinned.lightProbeUsage = sourceLightProbes;
                skinned.reflectionProbeUsage = sourceReflectionProbes;
                skinned.probeAnchor = sourceProbeAnchor;

                if (sourceRenderer != null) Undo.DestroyObjectImmediate(sourceRenderer);
                Undo.DestroyObjectImmediate(filter);
                EditorUtility.SetDirty(skinned);
                PrefabUtility.RecordPrefabInstancePropertyModifications(skinned);
                converted++;
            }

            AssetDatabase.SaveAssets();
            return converted;
        }
        finally
        {
            RestoreReadability(temporarilyReadable);
        }
    }

    /// <summary>
    /// Earlier migration passes treated every object under Accessories as
    /// headwear.  That made the safety vest follow the head instead of the
    /// torso, so it intersected or disappeared inside shirts during animation.
    /// Rebuild its weights from the animated body in place; the generated mesh
    /// asset is shared by gameplay, loading, Almanac, and customization.
    /// </summary>
    private static int RepairSafetyVestSkinning(Transform visual, Transform animationRoot)
    {
        SkinnedMeshRenderer donor = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(renderer =>
                string.Equals(renderer.name.Trim(), "Body", StringComparison.OrdinalIgnoreCase));
        if (donor == null || donor.sharedMesh == null || donor.bones == null || donor.bones.Length == 0)
            return 0;

        SkinnedMeshRenderer[] vests = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(renderer => renderer != null && renderer.sharedMesh != null &&
                               IsSafetyVest(renderer.transform))
            .ToArray();
        if (vests.Length == 0) return 0;

        // This is a one-time asset migration. Re-reading the FBX donor on every
        // domain reload toggled Read/Write and forced two complete model
        // reimports, which produced the empty-clip and bad-polygon warnings.
        //
        // The marker is the fast path. The weight check is deliberately kept as
        // a fallback because a checked-in native Mesh can be present before
        // Unity has refreshed its .meta importer data. In that case the vest is
        // already correct and must not cause the source FBX to be reimported.
        vests = vests
            .Where(vest => !IsSafetyVestRepairCurrent(vest, animationRoot))
            .ToArray();
        if (vests.Length == 0) return 0;

        HashSet<string> temporarilyReadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            MakeReadable(donor.sharedMesh, temporarilyReadable);

            // A model reimport can replace the donor sub-asset reference.
            donor = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer =>
                    string.Equals(renderer.name.Trim(), "Body", StringComparison.OrdinalIgnoreCase));
            if (donor == null || donor.sharedMesh == null) return 0;

            Vector3[] donorVertices = donor.sharedMesh.vertices;
            BoneWeight[] donorWeights = donor.sharedMesh.boneWeights;
            if (donorVertices.Length == 0 || donorWeights.Length != donorVertices.Length) return 0;
            SpatialVertexLookup lookup = new SpatialVertexLookup(donorVertices, donor.sharedMesh.bounds);
            int repaired = 0;

            foreach (SkinnedMeshRenderer vest in vests)
            {
                if (vest == null || vest.sharedMesh == null) continue;
                Mesh mesh = vest.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = new BoneWeight[vertices.Length];
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 world = vest.transform.TransformPoint(vertices[index]);
                    Vector3 donorLocal = donor.transform.InverseTransformPoint(world);
                    weights[index] = donorWeights[lookup.FindNearest(donorLocal)];
                }

                Matrix4x4[] bindposes = new Matrix4x4[donor.bones.Length];
                for (int boneIndex = 0; boneIndex < donor.bones.Length; boneIndex++)
                    bindposes[boneIndex] = donor.bones[boneIndex].worldToLocalMatrix *
                                           vest.transform.localToWorldMatrix;

                Undo.RecordObject(mesh, "Repair safety vest skinning");
                Undo.RecordObject(vest, "Bind safety vest to body rig");
                mesh.boneWeights = weights;
                mesh.bindposes = bindposes;
                mesh.RecalculateBounds();
                vest.bones = donor.bones;
                vest.rootBone = donor.rootBone;
                vest.localBounds = mesh.bounds;
                EditorUtility.SetDirty(mesh);
                EditorUtility.SetDirty(vest);
                PrefabUtility.RecordPrefabInstancePropertyModifications(vest);
                SetAssetMarker(mesh, SafetyVestRepairMarker);
                repaired++;
            }

            return repaired;
        }
        finally
        {
            RestoreReadability(temporarilyReadable);
        }
    }

    private static bool ShouldRigidlyFollowHead(Transform candidate, Transform visual)
    {
        if (IsUnderNamedAncestor(candidate, visual, "Hair")) return true;
        return IsUnderNamedAncestor(candidate, visual, "Accessories") &&
               !IsSafetyVest(candidate);
    }

    private static bool IsSafetyVest(Transform candidate)
    {
        return candidate != null &&
               candidate.name.IndexOf("Vest", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasAssetMarker(UnityEngine.Object asset, string marker)
    {
        if (asset == null || string.IsNullOrWhiteSpace(marker)) return false;
        string path = AssetDatabase.GetAssetPath(asset);
        AssetImporter importer = AssetImporter.GetAtPath(path);
        return importer != null && string.Equals(importer.userData, marker,
            StringComparison.Ordinal);
    }

    private static bool IsSafetyVestRepairCurrent(
        SkinnedMeshRenderer vest,
        Transform animationRoot)
    {
        if (vest == null || vest.sharedMesh == null) return false;
        if (HasAssetMarker(vest.sharedMesh, SafetyVestRepairMarker)) return true;

        // Only trust generated project meshes. Imported FBX sub-assets may have
        // a superficially similar bone list while still being rigidly weighted
        // to Head, which is the broken state this migration repairs.
        string path = AssetDatabase.GetAssetPath(vest.sharedMesh);
        if (string.IsNullOrEmpty(path) ||
            !path.Replace('\\', '/').StartsWith(
                GeneratedSkinFolder + "/", StringComparison.OrdinalIgnoreCase) ||
            !vest.sharedMesh.isReadable)
            return false;

        Transform[] bones = vest.bones;
        BoneWeight[] weights = vest.sharedMesh.boneWeights;
        Matrix4x4[] bindposes = vest.sharedMesh.bindposes;
        if (bones == null || bones.Length == 0 || weights == null || weights.Length == 0 ||
            bindposes == null || bindposes.Length != bones.Length)
            return false;

        Transform head = FindGenericBone(animationRoot, "Head");
        int headIndex = Array.IndexOf(bones, head);
        if (headIndex < 0) return false;

        // The old broken vest had every vertex assigned only to Head. A body-
        // skinned vest necessarily contains meaningful torso/non-head weights.
        foreach (BoneWeight weight in weights)
        {
            if ((weight.weight0 > 0.001f && weight.boneIndex0 != headIndex) ||
                (weight.weight1 > 0.001f && weight.boneIndex1 != headIndex) ||
                (weight.weight2 > 0.001f && weight.boneIndex2 != headIndex) ||
                (weight.weight3 > 0.001f && weight.boneIndex3 != headIndex))
                return true;
        }

        return false;
    }

    private static void SetAssetMarker(UnityEngine.Object asset, string marker)
    {
        if (asset == null || string.IsNullOrWhiteSpace(marker)) return;
        string path = AssetDatabase.GetAssetPath(asset);
        AssetImporter importer = AssetImporter.GetAtPath(path);
        if (importer == null || string.Equals(importer.userData, marker,
                StringComparison.Ordinal))
            return;
        importer.userData = marker;
        AssetDatabase.WriteImportSettingsIfDirty(path);
    }

    private static bool IsWardrobeMesh(Transform candidate, Transform visual)
    {
        if (candidate == null || visual == null) return false;
        if (IsUnderNamedAncestor(candidate, visual, "Hair") ||
            IsUnderNamedAncestor(candidate, visual, "Shirt") ||
            IsUnderNamedAncestor(candidate, visual, "Pants") ||
            IsUnderNamedAncestor(candidate, visual, "Shoes") ||
            IsUnderNamedAncestor(candidate, visual, "Accessories"))
            return true;

        string name = candidate.name.Trim();
        return string.Equals(name, "Base_Tshirt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Base_Pants", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderNamedAncestor(Transform candidate, Transform stopAt, string name)
    {
        for (Transform current = candidate.parent;
             current != null && current != stopAt.parent;
             current = current.parent)
        {
            if (string.Equals(current.name.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return true;
            if (current == stopAt) break;
        }
        return false;
    }

    private static void MakeReadable(Mesh mesh, ISet<string> changedPaths)
    {
        if (mesh == null || mesh.isReadable) return;
        string path = AssetDatabase.GetAssetPath(mesh);
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null || importer.isReadable) return;
        importer.isReadable = true;
        importer.SaveAndReimport();
        changedPaths.Add(path);
    }

    private static void RestoreReadability(IEnumerable<string> changedPaths)
    {
        foreach (string path in changedPaths)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || !importer.isReadable) continue;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
    }

    private static string GeneratedMeshPath(Mesh sourceMesh, string objectName)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
            sourceMesh, out string guid, out long localID);
        string safeName = new string((objectName + "_" + guid + "_" + localID)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
            .ToArray());
        return GeneratedSkinFolder + "/" + safeName + ".asset";
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(parent)) return;
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    private sealed class SpatialVertexLookup
    {
        private readonly Vector3[] vertices;
        private readonly Dictionary<Vector3Int, List<int>> cells =
            new Dictionary<Vector3Int, List<int>>();
        private readonly float cellSize;

        public SpatialVertexLookup(Vector3[] source, Bounds bounds)
        {
            vertices = source;
            cellSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) / 24f;
            if (cellSize < 0.0001f) cellSize = 0.05f;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3Int key = Cell(vertices[index]);
                if (!cells.TryGetValue(key, out List<int> values))
                {
                    values = new List<int>();
                    cells.Add(key, values);
                }
                values.Add(index);
            }
        }

        public int FindNearest(Vector3 point)
        {
            Vector3Int center = Cell(point);
            int bestIndex = -1;
            float bestDistance = float.PositiveInfinity;
            for (int radius = 0; radius <= 5 && bestIndex < 0; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                for (int y = -radius; y <= radius; y++)
                for (int z = -radius; z <= radius; z++)
                {
                    if (radius > 0 && Mathf.Abs(x) != radius && Mathf.Abs(y) != radius &&
                        Mathf.Abs(z) != radius)
                        continue;
                    if (!cells.TryGetValue(center + new Vector3Int(x, y, z), out List<int> values))
                        continue;
                    foreach (int index in values)
                    {
                        float distance = (vertices[index] - point).sqrMagnitude;
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        bestIndex = index;
                    }
                }
            }

            if (bestIndex >= 0) return bestIndex;
            for (int index = 0; index < vertices.Length; index++)
            {
                float distance = (vertices[index] - point).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestIndex = index;
            }
            return Mathf.Max(0, bestIndex);
        }

        private Vector3Int Cell(Vector3 point)
        {
            return new Vector3Int(
                Mathf.FloorToInt(point.x / cellSize),
                Mathf.FloorToInt(point.y / cellSize),
                Mathf.FloorToInt(point.z / cellSize));
        }
    }

    private static void WireSharedPreviewPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PreviewPrefabPath);
        if (root == null) return;
        try
        {
            PlayerCosmeticMirror mirror = root.GetComponent<PlayerCosmeticMirror>();
            if (mirror == null) mirror = root.AddComponent<PlayerCosmeticMirror>();
            CharacterCustomizationSetup.ConfigureBindings(mirror, root.transform);
            ConnectLegacyHardHat(mirror, root.transform);
            ApplyAuthoredDefaultVisibility(mirror.cosmeticBindings);
            EditorUtility.SetDirty(mirror);
            PrefabUtility.SaveAsPrefabAsset(root, PreviewPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Replaces only the model contents of the existing preview prefab. Keeping
    /// its root and component file IDs intact preserves every Almanac and
    /// customization prefab instance, camera reference, and 0.14 display scale.
    /// </summary>
    private static void RebuildSharedPreviewPrefab(Transform gameplayVisual)
    {
        if (gameplayVisual == null)
        {
            WireSharedPreviewPrefab();
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PreviewPrefabPath);
        if (root == null) return;
        try
        {
            for (int childIndex = root.transform.childCount - 1; childIndex >= 0; childIndex--)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(childIndex).gameObject);

            GameObject model = UnityEngine.Object.Instantiate(gameplayVisual.gameObject);
            model.name = "NewCharacterModel";
            model.transform.SetParent(root.transform, false);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            model.SetActive(true);

            foreach (PlayerCosmetics component in model.GetComponentsInChildren<PlayerCosmetics>(true))
                UnityEngine.Object.DestroyImmediate(component);
            foreach (PlayerCosmeticMirror component in model.GetComponentsInChildren<PlayerCosmeticMirror>(true))
                UnityEngine.Object.DestroyImmediate(component);

            PlayerCosmeticMirror mirror = root.GetComponent<PlayerCosmeticMirror>();
            if (mirror == null) mirror = root.AddComponent<PlayerCosmeticMirror>();
            CharacterCustomizationSetup.ConfigureBindings(mirror, root.transform);
            ConnectLegacyHardHat(mirror, root.transform);
            ApplyAuthoredDefaultVisibility(mirror.cosmeticBindings);
            EditorUtility.SetDirty(mirror);
            PrefabUtility.SaveAsPrefabAsset(root, PreviewPrefabPath);
            Debug.Log("[Character Wiring] Almanac/customization preview rebuilt from the gameplay NewCharacterModel.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConnectLegacyHardHat(PlayerCosmetics target, Transform visual)
    {
        GameObject hat = FindByTrimmedName(visual, "Base_Hard Hat")?.gameObject;
        if (hat == null) return;
        target.hats.RemoveAll(item => item == null ||
                                      string.Equals(item.cosmeticID, "EngineeringHardHat",
                                          StringComparison.OrdinalIgnoreCase));
        target.hats.Add(new CosmeticItem
        {
            cosmeticID = "EngineeringHardHat",
            cosmeticModel = hat
        });
    }

    private static void ConnectLegacyHardHat(PlayerCosmeticMirror target, Transform visual)
    {
        GameObject hat = FindByTrimmedName(visual, "Base_Hard Hat")?.gameObject;
        if (hat == null) return;
        target.hats.RemoveAll(item => item == null ||
                                      string.Equals(item.cosmeticID, "EngineeringHardHat",
                                          StringComparison.OrdinalIgnoreCase));
        target.hats.Add(new CosmeticItem
        {
            cosmeticID = "EngineeringHardHat",
            cosmeticModel = hat
        });
    }

    private static void ApplyAuthoredDefaultVisibility(IList<CosmeticModelBinding> bindings)
    {
        if (bindings == null) return;
        foreach (CosmeticModelBinding binding in bindings)
        {
            if (binding == null || binding.models == null) continue;
            foreach (GameObject model in binding.models)
                if (model != null) model.SetActive(binding.defaultWhenEmpty);
        }
    }

    private static void ValidateBindings(IList<CosmeticModelBinding> bindings, Transform visual)
    {
        string[] required =
        {
            "EngineeringHardHat", "Hair_Base", "Hair_1", "Hair_2", "Hair_4", "Hair_5",
            "Hair_6", "Hair_8", "Shirt_Base", "Shirt_Blouse", "Shirt_Polo", "Shirt_Tee1",
            "Shirt_Tee2", "Pants_Base", "Pants_Straight", "Pants_Cargo", "Pants_Rolled",
            "Pants_Skirt", "Shoes_Base", "Shoes_Sandals", "Shoes_Boots1", "Shoes_Boots2",
            "Shoes_Black"
        };

        List<string> missing = required.Where(id =>
                bindings == null || !bindings.Any(binding => binding != null &&
                    string.Equals(binding.cosmeticID, id, StringComparison.Ordinal) &&
                    binding.models != null && binding.models.Any(model => model != null)))
            .ToList();
        if (missing.Count > 0)
            Debug.LogError("[Character Wiring] Missing model objects for: " +
                           string.Join(", ", missing) + ".", visual);
    }

    private static bool TryFindVisual(Transform playerRoot, out Transform visual)
    {
        visual = null;
        foreach (Transform child in playerRoot)
        {
            if (!string.Equals(child.name, "NewCharacterModel", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(child.name, "Character_Cosmetics", StringComparison.OrdinalIgnoreCase))
                continue;
            visual = child;
            return true;
        }
        return false;
    }

    private static Transform FindVisualForMirror(Transform mirror)
    {
        Transform current = mirror;
        while (current != null)
        {
            if (FindGenericBone(current, "Hips") != null &&
                FindByTrimmedName(current, "Base_Tshirt") != null)
                return current;
            current = current.parent;
        }
        return mirror;
    }

    private static Transform FindGenericBone(Transform root, string expectedName)
    {
        if (root == null) return null;
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(BoneKey(candidate.name), expectedName,
                    StringComparison.OrdinalIgnoreCase))
                return candidate;
        return null;
    }

    private static Transform FindByTrimmedName(Transform root, string expectedName)
    {
        if (root == null) return null;
        string wanted = expectedName.Trim();
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string BoneKey(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        int namespaceEnd = name.LastIndexOf(':');
        return (namespaceEnd >= 0 ? name.Substring(namespaceEnd + 1) : name).Trim();
    }
}
#endif
