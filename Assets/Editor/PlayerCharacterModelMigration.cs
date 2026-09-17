#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-time, guarded migration for the scene-authored player visual.
/// It intentionally keeps the Player root and the previous visual intact so the
/// change can be undone or rolled back without losing gameplay components.
/// </summary>
public static class PlayerCharacterModelMigration
{
    private const string NewModelAssetPath =
        "Assets/Elements/Characters/Character_Cosmetics.fbx";
    private const string PlayerControllerPath =
        "Assets/Animations/Player/PlayerAnimator.controller";

    private static readonly string[] NewVisualNames =
    {
        "NewCharacterModel",
        "Character_Cosmetics"
    };

    [MenuItem("Tools/Civil Craft/Migrate New Character Model In Open Scene")]
    public static void MigrateOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Player Model Migration] Exit Play Mode before running the migration.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Player Model Migration] There is no loaded active scene to migrate.");
            return;
        }

        PlayerMotor[] players = Resources.FindObjectsOfTypeAll<PlayerMotor>()
            .Where(motor => motor != null && motor.gameObject.scene == scene)
            .ToArray();

        if (players.Length == 0)
        {
            Debug.LogError($"[Player Model Migration] No PlayerMotor was found in {scene.path}.");
            return;
        }

        int migrated = 0;
        foreach (PlayerMotor player in players)
        {
            if (MigratePlayer(player)) migrated++;
        }

        if (migrated == 0)
        {
            Debug.LogError(
                "[Player Model Migration] Nothing was migrated. Keep NewCharacterModel directly under " +
                "the Player root and check the preceding Console messages.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = players[0].gameObject;
        Debug.Log(
            $"[Player Model Migration] Migrated {migrated} player visual(s) in {scene.name}. " +
            "The old model is disabled as a rollback. Review the Player, then save the scene.",
            players[0]);
    }

    [MenuItem("Tools/Civil Craft/Migrate New Character Model In Open Scene", true)]
    private static bool ValidateMigrateOpenScene()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static bool MigratePlayer(PlayerMotor motor)
    {
        Transform playerRoot = motor.transform;
        Transform newVisual = FindDirectChild(playerRoot, NewVisualNames);
        Transform oldVisual = FindDirectChildBeginningWith(playerRoot, "Character_w_Clothes_v4");

        if (newVisual == null)
        {
            Debug.LogError(
                $"[Player Model Migration] {playerRoot.name} has no direct child named " +
                "NewCharacterModel or Character_Cosmetics.",
                playerRoot);
            return false;
        }

        if (oldVisual == newVisual)
        {
            Debug.LogError("[Player Model Migration] The old and new visuals resolve to the same object.", playerRoot);
            return false;
        }

        RuntimeAnimatorController controller = null;
        Animator oldAnimator = oldVisual != null ? oldVisual.GetComponent<Animator>() : null;
        if (oldAnimator != null) controller = oldAnimator.runtimeAnimatorController;
        if (controller == null)
            controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);

        // Both the existing player and Character_Cosmetics are Generic Mixamo
        // rigs. They intentionally have no Humanoid Avatar; animation and cargo
        // hand lookup use their matching mixamorig transform names.
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(NewModelAssetPath)
            .OfType<Avatar>()
            .FirstOrDefault(candidate => candidate != null && candidate.isValid);

        if (controller == null)
        {
            Debug.LogError(
                $"[Player Model Migration] Player Animator Controller was not found at {PlayerControllerPath}.",
                playerRoot);
            return false;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Migrate New Player Character Model");

        Undo.RecordObject(newVisual, "Align new player visual");
        bool newWasActive = newVisual.gameObject.activeSelf;
        if (!newWasActive) newVisual.gameObject.SetActive(true);
        AlignVisualWithPreviousModel(newVisual, oldVisual);

        // Generic clips bind by their exact transform paths. The player's clips
        // start at "mixamorig:Hips", so the Animator must live on the direct
        // parent of that transform (Base_Rig on Character_Cosmetics), not on the
        // outer visual/category root.
        Transform animationRoot = FindGenericAnimationRoot(newVisual);
        if (animationRoot == null)
        {
            Debug.LogError(
                "[Player Model Migration] Could not find the parent of mixamorig:Hips. " +
                "The Animator was not changed.",
                newVisual);
            if (!newWasActive) newVisual.gameObject.SetActive(false);
            Undo.CollapseUndoOperations(undoGroup);
            return false;
        }

        Animator misplacedAnimator = newVisual.GetComponent<Animator>();
        Animator newAnimator = animationRoot.GetComponent<Animator>();
        if (newAnimator == null)
            newAnimator = Undo.AddComponent<Animator>(animationRoot.gameObject);
        else
            Undo.RecordObject(newAnimator, "Configure new player Animator");

        newAnimator.avatar = avatar;
        newAnimator.runtimeAnimatorController = controller;
        newAnimator.applyRootMotion = false;
        newAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        newAnimator.updateMode = AnimatorUpdateMode.Normal;
        EditorUtility.SetDirty(newAnimator);

        if (!ValidateAnimator(newAnimator, newVisual))
        {
            if (!newWasActive) newVisual.gameObject.SetActive(false);
            Undo.CollapseUndoOperations(undoGroup);
            return false;
        }

        Undo.RecordObject(motor, "Assign new player Animator");
        motor.playerAnimator = newAnimator;
        EditorUtility.SetDirty(motor);
        PrefabUtility.RecordPrefabInstancePropertyModifications(motor);
        ReconnectPlayerCinematics(playerRoot, oldAnimator, misplacedAnimator, newAnimator);

        if (misplacedAnimator != null && misplacedAnimator != newAnimator)
            Undo.DestroyObjectImmediate(misplacedAnimator);

        GameObject newHardHat = FindDescendantByNormalizedName(newVisual, "BaseHardHat");
        PlayerCosmetics cosmetics = playerRoot.GetComponent<PlayerCosmetics>();
        if (cosmetics == null)
        {
            Debug.LogWarning(
                "[Player Model Migration] PlayerCosmetics is missing from the Player root. " +
                "Movement was migrated, but the hard hat could not be connected.",
                playerRoot);
        }
        else if (newHardHat == null)
        {
            Debug.LogWarning(
                "[Player Model Migration] Base_Hard Hat was not found under the new model. " +
                "The existing EngineeringHardHat reference was preserved.",
                newVisual);
        }
        else
        {
            ConnectEngineeringHardHat(cosmetics, newHardHat);
            Undo.RecordObject(newHardHat, "Set hard hat default state");
            newHardHat.SetActive(false);
            EditorUtility.SetDirty(newHardHat);
        }

        Undo.RecordObject(newVisual.gameObject, "Rename new player visual");
        newVisual.gameObject.name = "NewCharacterModel";
        EditorUtility.SetDirty(newVisual.gameObject);

        if (oldVisual != null)
        {
            Undo.RecordObject(oldVisual.gameObject, "Keep old player visual as rollback");
            if (!oldVisual.gameObject.name.EndsWith(" (Rollback)", StringComparison.Ordinal))
                oldVisual.gameObject.name += " (Rollback)";
            oldVisual.gameObject.SetActive(false);
            EditorUtility.SetDirty(oldVisual.gameObject);
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(newVisual);
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log(
            $"[Player Model Migration] {playerRoot.name}: new Generic Animator assigned to " +
            $"{animationRoot.name}, " +
            $"EngineeringHardHat connected={(newHardHat != null && cosmetics != null)}, " +
            $"old visual retained={(oldVisual != null)}.",
            playerRoot);
        return true;
    }

    private static void ReconnectPlayerCinematics(
        Transform playerRoot,
        Animator previousAnimator,
        Animator misplacedAnimator,
        Animator newAnimator)
    {
        foreach (CinematicDirector cinematic in Resources.FindObjectsOfTypeAll<CinematicDirector>())
        {
            if (cinematic == null || cinematic.gameObject.scene != playerRoot.gameObject.scene ||
                cinematic.playerActor != playerRoot)
                continue;
            if (cinematic.playerAnimator != null &&
                cinematic.playerAnimator != previousAnimator &&
                cinematic.playerAnimator != misplacedAnimator)
                continue;

            Undo.RecordObject(cinematic, "Reconnect cinematic player Animator");
            cinematic.playerAnimator = newAnimator;
            EditorUtility.SetDirty(cinematic);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cinematic);
        }
    }

    private static void AlignVisualWithPreviousModel(Transform newVisual, Transform oldVisual)
    {
        if (oldVisual == null)
        {
            Vector3 position = newVisual.localPosition;
            position.x = 0f;
            position.z = 0f;
            newVisual.localPosition = position;
            return;
        }

        Vector3 localPosition = newVisual.localPosition;
        localPosition.x = oldVisual.localPosition.x;
        localPosition.z = oldVisual.localPosition.z;
        newVisual.localPosition = localPosition;

        if (!TryGetVisualBounds(oldVisual, out Bounds oldBounds) ||
            !TryGetVisualBounds(newVisual, out Bounds newBounds))
            return;

        Vector3 worldPosition = newVisual.position;
        worldPosition.y += oldBounds.min.y - newBounds.min.y;
        newVisual.position = worldPosition;
    }

    private static bool TryGetVisualBounds(Transform visual, out Bounds result)
    {
        result = default;
        if (visual == null) return false;

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer != null && renderer.enabled)
            .ToArray();
        if (renderers.Length == 0) return false;

        result = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
        return true;
    }

    private static bool ValidateAnimator(Animator animator, Transform context)
    {
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips.Distinct())
        {
            string missingPath = AnimationUtility.GetCurveBindings(clip)
                .Select(binding => binding.path)
                .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)
                    .Select(binding => binding.path))
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .FirstOrDefault(path => animator.transform.Find(path) == null);

            if (missingPath == null) continue;
            Debug.LogError(
                $"[Player Model Migration] Animation clip {clip.name} cannot find path " +
                $"'{missingPath}' below Animator root {animator.transform.name}. " +
                "The old model was not disabled.",
                context);
            return false;
        }

        string[] requiredBones =
        {
            "Hips",
            "LeftHand",
            "RightHand",
            "LeftFoot",
            "RightFoot",
            "Head"
        };

        foreach (string bone in requiredBones)
        {
            if (FindGenericBone(animator.transform, bone) != null) continue;
            Debug.LogError(
                $"[Player Model Migration] Generic animation bone {bone} is missing. " +
                "The old model was not disabled.",
                context);
            return false;
        }

        string[] requiredParameters = { "Speed", "IsGrounded", "IsSprinting", "Jump" };
        foreach (string parameterName in requiredParameters)
        {
            if (animator.parameters.Any(parameter => parameter.name == parameterName)) continue;
            Debug.LogError(
                $"[Player Model Migration] Animator parameter {parameterName} is missing. " +
                "The old model was not disabled.",
                context);
            return false;
        }

        if (animator.GetLayerIndex("Carry Pose") < 0)
        {
            Debug.LogError(
                "[Player Model Migration] Animator layer Carry Pose is missing. " +
                "The old model was not disabled.",
                context);
            return false;
        }

        return true;
    }

    private static Transform FindGenericBone(Transform root, string expectedName)
    {
        foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
        {
            string candidate = bone.name;
            int namespaceEnd = candidate.LastIndexOf(':');
            if (namespaceEnd >= 0) candidate = candidate.Substring(namespaceEnd + 1);
            if (string.Equals(candidate, expectedName, StringComparison.OrdinalIgnoreCase)) return bone;
        }
        return null;
    }

    private static Transform FindGenericAnimationRoot(Transform visualRoot)
    {
        Transform hips = FindGenericBone(visualRoot, "Hips");
        if (hips == null || hips.parent == null || !hips.IsChildOf(visualRoot)) return null;
        return hips.parent;
    }

    private static void ConnectEngineeringHardHat(PlayerCosmetics cosmetics, GameObject hardHat)
    {
        Undo.RecordObject(cosmetics, "Connect new Engineering hard hat");
        SerializedObject serializedCosmetics = new SerializedObject(cosmetics);
        SerializedProperty hats = serializedCosmetics.FindProperty("hats");

        int targetIndex = -1;
        for (int i = 0; i < hats.arraySize; i++)
        {
            SerializedProperty entry = hats.GetArrayElementAtIndex(i);
            if (!string.Equals(
                    entry.FindPropertyRelative("cosmeticID").stringValue,
                    "EngineeringHardHat",
                    StringComparison.Ordinal))
                continue;

            targetIndex = i;
            break;
        }

        if (targetIndex < 0)
        {
            targetIndex = hats.arraySize;
            hats.InsertArrayElementAtIndex(targetIndex);
        }

        SerializedProperty target = hats.GetArrayElementAtIndex(targetIndex);
        target.FindPropertyRelative("cosmeticID").stringValue = "EngineeringHardHat";
        target.FindPropertyRelative("cosmeticModel").objectReferenceValue = hardHat;
        serializedCosmetics.ApplyModifiedProperties();
        EditorUtility.SetDirty(cosmetics);
        PrefabUtility.RecordPrefabInstancePropertyModifications(cosmetics);
    }

    private static Transform FindDirectChild(Transform parent, string[] names)
    {
        foreach (Transform child in parent)
        {
            foreach (string candidate in names)
            {
                if (string.Equals(child.name, candidate, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
        }
        return null;
    }

    private static Transform FindDirectChildBeginningWith(Transform parent, string namePrefix)
    {
        foreach (Transform child in parent)
        {
            if (child.name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) return child;
        }
        return null;
    }

    private static GameObject FindDescendantByNormalizedName(Transform root, string normalizedName)
    {
        foreach (Transform descendant in root.GetComponentsInChildren<Transform>(true))
        {
            string normalized = new string(descendant.name
                .Where(char.IsLetterOrDigit)
                .ToArray());
            if (string.Equals(normalized, normalizedName, StringComparison.OrdinalIgnoreCase))
                return descendant.gameObject;
        }
        return null;
    }
}
#endif
