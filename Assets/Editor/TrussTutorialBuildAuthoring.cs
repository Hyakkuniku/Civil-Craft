#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Explicit Tutorial 2 content migration. Never imports, opens, or saves a scene automatically.</summary>
public static class TrussTutorialBuildAuthoring
{
    internal const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    internal const string LessonId = "Sequence_Build2";
    internal const string RoadFeedback = "Good! The <b><#E09500>Wood Road</color></b> provides the surface for the vehicle to cross. Now let's add the structural members that will support it.";
    internal const string FirstSectionFeedback = "<b><#E09500>FIRST TRUSS SECTION COMPLETE.</color></b>\n\nExcellent! You've connected several <b><#E09500>Wood Beams</color></b> to form your first truss section. Remember what Professor Bhan explained earlier. The triangular arrangement helps the connected members maintain their structural shape. Now let's learn how to build more efficiently.";
    internal const string CompletionFeedback = "<b><#E09500>TRUSS STRUCTURE COMPLETE.</color></b>\n\nExcellent! You've built a <b><#E09500>Truss Bridge</color></b> and used the editing tools to duplicate its structure efficiently. Your bridge is now ready for testing. Remember what you learned about <b><#E09500>equilibrium</color></b>. Watch how the connected structural members and supports respond as the vehicle crosses.";
    private const string RoadGuid = "d96454d8483949a4fb084d742f9a0ac8";
    private const string BeamGuid = "d168cda963676074692e7020faf3c791";
    internal static readonly int[] LegacyActions = { 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
    internal static readonly int[] UpdatedActions = { 6, 7, 9, 10, 12, 13, 14, 15, 16, 18 };

    [MenuItem("Tools/Civil Craft/Tutorial 2/Update Truss Build Instructions (Do Not Save)")]
    private static void ApplyFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || scene.isDirty)
            throw new InvalidOperationException("Exit Play Mode and save CanyonCrossing before updating Tutorial 2. No scene was saved.");
        Apply(scene);
        Debug.Log("[Tutorial 2] Updated the existing truss build instructions. Review and save CanyonCrossing when ready; this command did not save it.");
    }

    public static void Preflight(Scene scene) => Prepare(scene, out _, out _, out _);

    public static void Apply(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Finish Play Mode and imports before applying Tutorial 2 build instructions.");
        Prepare(scene, out TutorialSequence sequence, out _, out TutorialStep[] updated);
        if (SameSteps(sequence.tutorialSteps, updated)) return;
        Undo.RecordObject(sequence, "Update Tutorial 2 truss build instructions");
        sequence.tutorialSteps = updated;
        EditorUtility.SetDirty(sequence);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    internal static void Prepare(Scene scene, out TutorialSequence sequence, out BuildTutorialDirector director, out TutorialStep[] updated)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            throw new InvalidOperationException("Tutorial 2 build authoring requires the loaded CanyonCrossing scene.");
        TutorialSequence[] matches = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true))
            .Where(item => string.Equals(item.lessonName, LessonId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected exactly one existing Sequence_Build2. No replacement tutorial will be created.");
        sequence = matches[0];
        if (sequence.requiredPreviousLesson != "Sequence_Build1" || sequence.nextSequence != null || sequence.autoStartNextSequence || sequence.playOnStart)
            throw new InvalidOperationException("Sequence_Build2 progression/chaining differs from the audited scene; review the change before migrating it.");
        BuildTutorialDirector[] directors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BuildTutorialDirector>(true)).ToArray();
        if (directors.Length != 1) throw new InvalidOperationException("Expected the existing shared BuildTutorialDirector; no substitute will be created.");
        director = directors[0];
        var directorData = new SerializedObject(director);
        if (directorData.FindProperty("selectionTutorialLesson").stringValue != LessonId)
            throw new InvalidOperationException("The shared director no longer owns Sequence_Build2's selection/copy/paste gates.");
        updated = BuildUpdatedSteps(sequence.tutorialSteps, director);
        AuditSceneBindings(sequence, director);
    }

    /// <summary>Returns new step objects; never changes source steps, persistent events, or their scene references.</summary>
    internal static TutorialStep[] BuildUpdatedSteps(TutorialStep[] source, BuildTutorialDirector director)
    {
        int[] map = ActionMap(source);
        if (director == null || source.Any(step => step == null)) throw new InvalidOperationException("Tutorial 2 has a missing step or director.");
        TutorialStep[] actions = map.Select(index => source[index]).ToArray();
        TutorialStepAction[] expected = { TutorialStepAction.None, TutorialStepAction.None, TutorialStepAction.None, TutorialStepAction.None,
            TutorialStepAction.SelectTool, TutorialStepAction.SelectBridge, TutorialStepAction.CopySelection,
            TutorialStepAction.PositionPastePreview, TutorialStepAction.PasteSelection, TutorialStepAction.None };
        for (int i = 0; i < actions.Length; i++)
        {
            if (actions[i].requiredAction != expected[i] || actions[i].showNextButton || actions[i].canSkip)
                throw new InvalidOperationException("Tutorial 2 action gates no longer match the audited step " + map[i] + ".");
        }
        RequireEvent(actions[0], director, "PromptMaterialClick");
        RequireEvent(actions[1], director, "PromptDrawBridgeForContainer");
        RequireEvent(actions[2], director, "PromptMaterialClick");
        RequireEvent(actions[3], director, "PromptDrawBridgeForContainer");
        RequireEvent(actions[7], director, "PromptPositionPasteAtGhost");
        RequireEvent(actions[9], director, "UnlockSimulation");
        for (int step = 0; step < actions.Length - 1; step++)
            for (int call = 0; actions[step].OnStepStart != null && call < actions[step].OnStepStart.GetPersistentEventCount(); call++)
                if (actions[step].OnStepStart.GetPersistentMethodName(call) == "UnlockSimulation")
                    throw new InvalidOperationException("Tutorial 2 unlocks simulation before its completion/Continue screen.");
        foreach (int i in new[] { 4, 6, 8, 9 })
            if (!actions[i].usePointer || actions[i].pointerTarget == null)
                throw new InvalidOperationException("Tutorial 2 is missing its existing tool/clipboard/Play pointer at step " + map[i] + ".");
        if (!actions[4].advanceOnClick || actions[6].advanceOnClick || actions[8].advanceOnClick || !actions[9].advanceOnClick ||
            actions[5].worldHighlightObject == null || actions[5].worldHighlightObject.GetComponent<TutorialDragSelectionAnim>() == null)
            throw new InvalidOperationException("Tutorial 2 selection/copy/paste success gates or its authored selection volume are missing.");

        TutorialStep[] revised = actions.Select(CopyStep).ToArray();
        revised[0].message = "Select the <b><#E09500>Wood Road</color></b> to begin.";
        revised[1].message = "Start by placing the <b><#E09500>Wood Road</color></b> between the <b><#E09500>two red anchors</color></b>.";
        revised[2].message = "Select the <b><#E09500>Wood Beam</color></b>. You'll use these members to create a <b><#E09500>triangular structural arrangement</color></b>.";
        revised[3].message = "Connect the <b><#E09500>Wood Beams</color></b> using the highlighted guide. Follow the guide to create a <b><#E09500>triangular arrangement</color></b>.";
        revised[4].message = "When a structure contains several members, rebuilding each one individually can take time. The <b><#E09500>Selection Tool</color></b> allows you to select multiple bridge components.";
        revised[5].message = "Select the members that form the <b><#E09500>completed truss section</color></b>.";
        revised[6].message = "You can duplicate selected components instead of rebuilding them individually. Press <b><#E09500>Copy</color></b> to create a duplicate of the selected truss section.";
        revised[7].message = "The copied components are ready to be placed. Use <b><#E09500>Paste</color></b> to position the copied truss section on the <b><#E09500>highlighted guide</color></b>.";
        revised[8].message = "Position the copied truss section on the highlighted guide, then press <b><#E09500>Paste</color></b> to confirm.";
        revised[9].message = "Press <b><#E09500>Play</color></b> to test your <b><#E09500>Truss Bridge</color></b>.";
        // Keep the existing opening explanation and real Erase/Move tool
        // introductions exactly as authored, including every UI/callback field.
        var result = new List<TutorialStep>(source.Take(6).Select(CopyStep));
        result.AddRange(revised);
        result.Insert(8, Feedback(RoadFeedback, director));
        result.Insert(11, Feedback(FirstSectionFeedback, director));
        result.Insert(17, Feedback(CompletionFeedback, director));
        return result.ToArray();
    }

    internal static int[] ActionMap(TutorialStep[] source)
    {
        if (source != null && source.Length == 16)
        {
            for (int i = 0; i < 6; i++)
                if (source[i] == null)
                    throw new InvalidOperationException("Tutorial 2 is missing an existing introduction; no tool lesson will be removed or substituted.");
            return LegacyActions;
        }
        if (source != null && source.Length == 19 && source[8] != null && source[11] != null && source[17] != null &&
            source[8].message == RoadFeedback && source[11].message == FirstSectionFeedback && source[17].message == CompletionFeedback)
            return UpdatedActions;
        throw new InvalidOperationException("Sequence_Build2 is neither the audited 16-step tutorial nor this migration's 19-step result. Existing Erase/Move introductions must be retained; no custom sequence was replaced.");
    }

    private static void AuditSceneBindings(TutorialSequence sequence, BuildTutorialDirector director)
    {
        int[] map = ActionMap(sequence.tutorialSteps);
        var serialized = new SerializedObject(sequence);
        BridgeMaterialSO road = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(AssetDatabase.GUIDToAssetPath(RoadGuid));
        BridgeMaterialSO beam = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(AssetDatabase.GUIDToAssetPath(BeamGuid));
        if (road == null || beam == null || ObjectArgument(serialized, map[0], director, "PromptMaterialClick") != road ||
            ObjectArgument(serialized, map[2], director, "PromptMaterialClick") != beam)
            throw new InvalidOperationException("Tutorial 2 is missing its authored Wood Road/Wood Beam material callbacks.");
        GameObject roadGhost = ObjectArgument(serialized, map[1], director, "PromptDrawBridgeForContainer") as GameObject;
        GameObject beamGhost = ObjectArgument(serialized, map[3], director, "PromptDrawBridgeForContainer") as GameObject;
        GameObject pasteGhost = ObjectArgument(serialized, map[7], director, "PromptPositionPasteAtGhost") as GameObject;
        RequireGhosts(sequence, roadGhost, 3, road, 3, beam, 0);
        RequireGhosts(sequence, beamGhost, 8, road, 0, beam, 8);
        RequireGhosts(sequence, pasteGhost, 11, road, 3, beam, 8);
        RequireActivation(serialized, map[1], roadGhost);
        RequireActivation(serialized, map[3], beamGhost);
        RequireAnchors(serialized, map[1], sequence.gameObject.scene);
        RequireAnchors(serialized, map[7], sequence.gameObject.scene);
        RequireAnchors(serialized, map[8], sequence.gameObject.scene);
        SerializedProperty playCalls = Calls(serialized, map[9]); int unlocks = 0;
        for (int i = 0; i < playCalls.arraySize; i++)
        {
            SerializedProperty call = playCalls.GetArrayElementAtIndex(i);
            if (call.FindPropertyRelative("m_Target").objectReferenceValue == director && call.FindPropertyRelative("m_MethodName").stringValue == "UnlockSimulation" &&
                call.FindPropertyRelative("m_Mode").intValue == 1 && call.FindPropertyRelative("m_CallState").intValue != 0) unlocks++;
        }
        if (unlocks != 1) throw new InvalidOperationException("Tutorial 2's existing simulation unlock callback is disabled or malformed.");
    }

    private static void RequireGhosts(TutorialSequence sequence, GameObject container, int count, BridgeMaterialSO road, int roads, BridgeMaterialSO beam, int beams)
    {
        if (container == null || !container.transform.IsChildOf(sequence.transform))
            throw new InvalidOperationException("Tutorial 2 is missing one of its existing child blueprint containers.");
        GhostSegment[] ghosts = container.GetComponentsInChildren<GhostSegment>(true);
        if (ghosts.Length != count || ghosts.Count(item => item.requiredMaterial == road) != roads || ghosts.Count(item => item.requiredMaterial == beam) != beams ||
            ghosts.Any(item => !Finite(item.startPos) || !Finite(item.endPos) || (item.endPos - item.startPos).sqrMagnitude <= .0001f))
            throw new InvalidOperationException("Tutorial 2's audited blueprint geometry/materials differ. No blueprint, anchor, or camera reference was replaced.");
    }

    private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
        !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

    private static SerializedProperty Calls(SerializedObject sequence, int step) => sequence.FindProperty("tutorialSteps").GetArrayElementAtIndex(step)
        .FindPropertyRelative("OnStepStart").FindPropertyRelative("m_PersistentCalls").FindPropertyRelative("m_Calls");

    private static UnityEngine.Object ObjectArgument(SerializedObject sequence, int step, UnityEngine.Object target, string method)
    {
        SerializedProperty calls = Calls(sequence, step);
        var matches = new List<UnityEngine.Object>();
        for (int i = 0; i < calls.arraySize; i++)
        {
            SerializedProperty call = calls.GetArrayElementAtIndex(i);
            if (call.FindPropertyRelative("m_Target").objectReferenceValue != target || call.FindPropertyRelative("m_MethodName").stringValue != method) continue;
            if (call.FindPropertyRelative("m_Mode").intValue != 2 || call.FindPropertyRelative("m_CallState").intValue == 0)
                throw new InvalidOperationException("Tutorial 2 callback is disabled or has the wrong argument mode: " + method);
            matches.Add(call.FindPropertyRelative("m_Arguments").FindPropertyRelative("m_ObjectArgument").objectReferenceValue);
        }
        if (matches.Count != 1 || matches[0] == null) throw new InvalidOperationException("Missing/ambiguous Tutorial 2 object callback: " + method);
        return matches[0];
    }

    private static void RequireActivation(SerializedObject sequence, int step, GameObject container)
    {
        SerializedProperty calls = Calls(sequence, step); int matches = 0;
        for (int i = 0; i < calls.arraySize; i++)
        {
            SerializedProperty call = calls.GetArrayElementAtIndex(i);
            if (call.FindPropertyRelative("m_Target").objectReferenceValue == container && call.FindPropertyRelative("m_MethodName").stringValue == "SetActive" &&
                call.FindPropertyRelative("m_Mode").intValue == 6 && call.FindPropertyRelative("m_CallState").intValue != 0 &&
                call.FindPropertyRelative("m_Arguments").FindPropertyRelative("m_BoolArgument").boolValue) matches++;
        }
        if (matches != 1) throw new InvalidOperationException("Tutorial 2 lost the existing blueprint activation callback.");
    }

    private static void RequireAnchors(SerializedObject sequence, int step, Scene scene)
    {
        SerializedProperty calls = Calls(sequence, step); var anchors = new HashSet<Transform>();
        for (int i = 0; i < calls.arraySize; i++)
        {
            SerializedProperty call = calls.GetArrayElementAtIndex(i);
            if (call.FindPropertyRelative("m_MethodName").stringValue != "HighlightAnchor") continue;
            Transform anchor = call.FindPropertyRelative("m_Arguments").FindPropertyRelative("m_ObjectArgument").objectReferenceValue as Transform;
            if (!(call.FindPropertyRelative("m_Target").objectReferenceValue is TutorialAnchorHighlighter) || anchor == null || anchor.gameObject.scene != scene ||
                call.FindPropertyRelative("m_Mode").intValue != 2 || call.FindPropertyRelative("m_CallState").intValue == 0 || !anchors.Add(anchor))
                throw new InvalidOperationException("Tutorial 2 contains a missing/duplicate anchor callback.");
        }
        if (anchors.Count != 2) throw new InvalidOperationException("Tutorial 2 requires its two existing anchor callbacks; no anchors were substituted.");
    }

    private static void RequireEvent(TutorialStep step, UnityEngine.Object target, string method)
    {
        int matches = 0;
        for (int i = 0; step.OnStepStart != null && i < step.OnStepStart.GetPersistentEventCount(); i++)
            if (step.OnStepStart.GetPersistentTarget(i) == target && step.OnStepStart.GetPersistentMethodName(i) == method) matches++;
        if (matches != 1) throw new InvalidOperationException("Tutorial 2 is missing the unique existing callback: " + method);
    }

    private static TutorialStep CopyStep(TutorialStep step) => new TutorialStep
    {
        message = step.message, screenPosition = step.screenPosition, showNextButton = step.showNextButton, canSkip = step.canSkip,
        lockLook = step.lockLook, lockJump = step.lockJump, lockRun = step.lockRun, stepWaypoints = step.stepWaypoints,
        worldHighlightObject = step.worldHighlightObject, usePointer = step.usePointer, pointerTarget = step.pointerTarget,
        pointerOffset = step.pointerOffset, pointerRotation = step.pointerRotation, advanceOnClick = step.advanceOnClick,
        requiredAction = step.requiredAction, OnStepStart = step.OnStepStart
    };

    private static TutorialStep Feedback(string message, BuildTutorialDirector director)
    {
        var step = new TutorialStep { message = message, showNextButton = true, canSkip = false, requiredAction = TutorialStepAction.None };
        UnityEventTools.AddPersistentListener(step.OnStepStart, director.LockAllUI);
        return step;
    }

    internal static bool SameSteps(TutorialStep[] a, TutorialStep[] b) => a != null && b != null && a.Length == b.Length &&
        a.Zip(b, (left, right) => JsonUtility.ToJson(left) == JsonUtility.ToJson(right)).All(same => same);
}
#endif
