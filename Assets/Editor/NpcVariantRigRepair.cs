#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Repairs the NPC Variant prefab family so characters with different skeleton
/// hierarchies can share the NPC state machine through Humanoid retargeting.
/// The original NPC controller and its users (Reed, Bhan, etc.) are untouched.
/// </summary>
[InitializeOnLoad]
public static class NpcVariantRigRepair
{
    private const string RepairSessionKey = "CivilCraft.NpcVariantRigRepair.v2";
    private const string SourceControllerPath = "Assets/Elements/Characters/NPC_ANIMATOR.controller";
    private const string VariantControllerPath = "Assets/Elements/Characters/NPC_VARIANTS_ANIMATOR.controller";
    private const string VariantAnimationFolder = "Assets/Elements/Characters/NPC Variant Animations";
    private const string UnriggedNpcModelPath = "Assets/Elements/Characters/NPC_2.fbx";
    private const string RiggedFemaleFallbackPath = "Assets/Elements/Characters/Char_Lola4.fbx";

    private static readonly string[] VariantModelPaths =
    {
        "Assets/Elements/Characters/NPC_1.fbx",
        "Assets/Elements/Characters/NPC_3.fbx",
        RiggedFemaleFallbackPath
    };

    private static readonly string[] VariantPrefabPaths =
    {
        "Assets/Elements/Characters/Prefabs/NPC Variant.prefab",
        "Assets/Elements/Characters/Prefabs/NPC Variant Variant.prefab",
        "Assets/Elements/Characters/Prefabs/NPC Variant Variant Variant.prefab",
        "Assets/Elements/Characters/Prefabs/NPC Variant 1.prefab",
        "Assets/Elements/Characters/Prefabs/NPC Variant 1 Variant.prefab",
        "Assets/Elements/Characters/Prefabs/NPC Variant 1 Variant Variant.prefab"
    };

    private static readonly AnimationSource[] AnimationSources =
    {
        new AnimationSource("breathingbhan", "Assets/Elements/Characters/Breathing Idle.fbx", true),
        new AnimationSource("walk", "Assets/Elements/Characters/Walking.fbx", true),
        new AnimationSource("talking", "Assets/Elements/Characters/Talking.fbx", true),
        new AnimationSource("Sprint", "Assets/Elements/Characters/Character_w_Clothes_v4@Sprint.fbx", true)
    };

    static NpcVariantRigRepair()
    {
        EditorApplication.delayCall += RunAutomaticRepair;
    }

    [MenuItem("Tools/Civil Craft/Repair NPC Variant Rigs")]
    public static void RepairFromMenu()
    {
        Repair();
    }

    private static void RunAutomaticRepair()
    {
        if (SessionState.GetBool(RepairSessionKey, false) ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        SessionState.SetBool(RepairSessionKey, true);
        Repair();
    }

    private static void Repair()
    {
        try
        {
            EnsureFolder(VariantAnimationFolder);

            foreach (string modelPath in VariantModelPaths)
            {
                ConfigureHumanoidImporter(modelPath, false);
            }

            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            foreach (AnimationSource source in AnimationSources)
            {
                string destination = VariantAnimationFolder + "/" + Path.GetFileName(source.Path);
                if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(destination))
                {
                    if (!AssetDatabase.CopyAsset(source.Path, destination))
                    {
                        throw new InvalidOperationException("Could not copy NPC animation: " + source.Path);
                    }
                }

                ConfigureHumanoidImporter(destination, source.Loop);
                AnimationClip clip = LoadImportedClip(destination);
                if (!clip)
                {
                    throw new InvalidOperationException("No animation clip was imported from: " + destination);
                }

                clips[source.StateName] = clip;
            }

            AnimatorController controller = CreateOrUpdateVariantController(clips);
            foreach (string prefabPath in VariantPrefabPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                    continue;
                RepairPrefab(prefabPath, controller);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[NPC Variant Rig Repair] Repaired all NPC Variant rigs, controllers, Avatars, and progression Animator references.");
        }
        catch (Exception exception)
        {
            SessionState.SetBool(RepairSessionKey, false);
            Debug.LogException(exception);
        }
    }

    private static void ConfigureHumanoidImporter(string assetPath, bool loop)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (!importer)
        {
            throw new InvalidOperationException("Missing model importer: " + assetPath);
        }

        bool changed = importer.animationType != ModelImporterAnimationType.Human ||
                       importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        if (importer.importAnimation)
        {
            ModelImporterClipAnimation[] clipAnimations = importer.clipAnimations;
            if (clipAnimations == null || clipAnimations.Length == 0)
            {
                clipAnimations = importer.defaultClipAnimations;
            }

            foreach (ModelImporterClipAnimation clip in clipAnimations)
            {
                if (clip.loopTime != loop || clip.loopPose != loop)
                {
                    clip.loopTime = loop;
                    clip.loopPose = loop;
                    changed = true;
                }
            }

            importer.clipAnimations = clipAnimations;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static AnimatorController CreateOrUpdateVariantController(
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(VariantControllerPath))
        {
            if (!AssetDatabase.CopyAsset(SourceControllerPath, VariantControllerPath))
            {
                throw new InvalidOperationException("Could not create the NPC Variant Animator Controller.");
            }
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(VariantControllerPath);
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            ReplaceStateMotions(layer.stateMachine, clips);
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void ReplaceStateMotions(
        AnimatorStateMachine stateMachine,
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        foreach (ChildAnimatorState childState in stateMachine.states)
        {
            if (clips.TryGetValue(childState.state.name, out AnimationClip clip))
            {
                childState.state.motion = clip;
                EditorUtility.SetDirty(childState.state);
            }
        }

        foreach (ChildAnimatorStateMachine childMachine in stateMachine.stateMachines)
        {
            ReplaceStateMotions(childMachine.stateMachine, clips);
        }
    }

    private static void RepairPrefab(string prefabPath, RuntimeAnimatorController controller)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            Animator visibleAnimator = animators.FirstOrDefault(IsVariantModelAnimator);
            if (visibleAnimator && GetSourceAssetPath(visibleAnimator.gameObject) == UnriggedNpcModelPath)
            {
                visibleAnimator = ReplaceUnriggedModel(visibleAnimator);
            }
            if (!visibleAnimator)
            {
                throw new InvalidOperationException("No model Animator found in prefab: " + prefabPath);
            }

            string modelPath = GetSourceAssetPath(visibleAnimator.gameObject);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isValid || !avatar.isHuman)
            {
                throw new InvalidOperationException(
                    "Unity could not build a valid Humanoid Avatar for " + modelPath +
                    ". Open that FBX's Rig Configuration to correct its bone mapping.");
            }

            visibleAnimator.runtimeAnimatorController = controller;
            visibleAnimator.avatar = avatar;
            visibleAnimator.applyRootMotion = false;
            EditorUtility.SetDirty(visibleAnimator);

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour)
                {
                    continue;
                }

                SerializedObject serialized = new SerializedObject(behaviour);
                SerializedProperty animatorProperty = serialized.FindProperty("animator");
                if (animatorProperty != null && animatorProperty.propertyType == SerializedPropertyType.ObjectReference &&
                    behaviour.GetType().Name == "NPCProgressionManager")
                {
                    animatorProperty.objectReferenceValue = visibleAnimator;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool IsVariantModelAnimator(Animator animator)
    {
        string path = GetSourceAssetPath(animator.gameObject);
        return VariantModelPaths.Contains(path) || path == UnriggedNpcModelPath;
    }

    private static Animator ReplaceUnriggedModel(Animator unriggedAnimator)
    {
        Transform oldTransform = unriggedAnimator.transform;
        Transform parent = oldTransform.parent;
        Vector3 localPosition = oldTransform.localPosition;
        Quaternion localRotation = oldTransform.localRotation;
        Vector3 localScale = oldTransform.localScale;

        GameObject fallbackAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RiggedFemaleFallbackPath);
        GameObject replacement = PrefabUtility.InstantiatePrefab(fallbackAsset, parent) as GameObject;
        if (!replacement)
        {
            throw new InvalidOperationException("Could not instantiate the rigged NPC_2 replacement.");
        }

        replacement.name = "NPC_2_Rigged";
        replacement.transform.localPosition = localPosition;
        replacement.transform.localRotation = localRotation;
        replacement.transform.localScale = localScale;
        UnityEngine.Object.DestroyImmediate(oldTransform.gameObject);

        Animator animator = replacement.GetComponent<Animator>();
        if (!animator)
        {
            animator = replacement.AddComponent<Animator>();
        }
        return animator;
    }

    private static string GetSourceAssetPath(GameObject instance)
    {
        UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
        return source ? AssetDatabase.GetAssetPath(source) : string.Empty;
    }

    private static AnimationClip LoadImportedClip(string assetPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[index]);
            }
            current = next;
        }
    }

    private readonly struct AnimationSource
    {
        public AnimationSource(string stateName, string path, bool loop)
        {
            StateName = stateName;
            Path = path;
            Loop = loop;
        }

        public string StateName { get; }
        public string Path { get; }
        public bool Loop { get; }
    }
}
#endif
