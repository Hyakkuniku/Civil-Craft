#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Detached data checks using existing scene references without invoking events or editing/saving the scene.</summary>
public static class TrussTutorialBuildValidation
{
    [MenuItem("Tools/Civil Craft/Tutorial 2/Validate Truss Build Instructions (Read Only)")]
    private static void ValidateFromMenu() => Debug.Log(ValidateReadOnly(SceneManager.GetActiveScene()));

    public static string ValidateReadOnly(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Finish Play Mode/imports before validating Tutorial 2 build instructions.");
        TrussTutorialBuildAuthoring.Prepare(scene, out TutorialSequence sequence, out BuildTutorialDirector director, out TutorialStep[] updated);
        string sequenceBefore = EditorJsonUtility.ToJson(sequence);
        string directorBefore = EditorJsonUtility.ToJson(director);
        string geometryBefore = GeometrySnapshot(sequence);
        bool dirtyBefore = scene.isDirty;
        string lessonBefore = sequence.lessonName, previousBefore = sequence.requiredPreviousLesson;
        TutorialStep[] original = sequence.tutorialSteps;
        int[] sourceMap = TrussTutorialBuildAuthoring.ActionMap(original);
        int checks = 0;
        Action<bool, string> require = (condition, message) =>
        {
            if (!condition) throw new InvalidOperationException("Tutorial 2 validation failed: " + message);
            checks++;
        };
        require(updated.Length == 19, "new build instructions contain exactly 19 steps, retaining all six introductions");
        for (int introduction = 0; introduction < 6; introduction++)
        {
            require(!ReferenceEquals(original[introduction], updated[introduction]) &&
                JsonUtility.ToJson(original[introduction]) == JsonUtility.ToJson(updated[introduction]) &&
                ReferenceEquals(original[introduction].OnStepStart, updated[introduction].OnStepStart),
                "original introduction text, Erase/Move tool pointers, action gates, callbacks and UI fields are retained exactly: " + introduction);
        }
        for (int action = 0; action < sourceMap.Length; action++)
        {
            TutorialStep before = original[sourceMap[action]], after = updated[TrussTutorialBuildAuthoring.UpdatedActions[action]];
            require(!ReferenceEquals(before, after), "action step is copied, not edited in place: " + action);
            require(ReferenceEquals(before.OnStepStart, after.OnStepStart), "all existing persistent callbacks and argument references are retained: " + action);
            require(before.screenPosition == after.screenPosition && before.showNextButton == after.showNextButton && before.canSkip == after.canSkip &&
                before.lockLook == after.lockLook && before.lockJump == after.lockJump && before.lockRun == after.lockRun &&
                ReferenceEquals(before.stepWaypoints, after.stepWaypoints) && before.worldHighlightObject == after.worldHighlightObject &&
                before.usePointer == after.usePointer && before.pointerTarget == after.pointerTarget && before.pointerOffset == after.pointerOffset &&
                before.pointerRotation == after.pointerRotation && before.advanceOnClick == after.advanceOnClick && before.requiredAction == after.requiredAction,
                "non-copy UI, control, geometry, highlight and semantic-action fields are unchanged: " + action);
        }
        foreach (int index in new[] { 8, 11, 17 })
        {
            TutorialStep feedback = updated[index];
            require(feedback.showNextButton && !feedback.canSkip && !feedback.advanceOnClick && feedback.requiredAction == TutorialStepAction.None,
                "feedback requires Continue rather than an automatic action: " + index);
            require(feedback.OnStepStart.GetPersistentEventCount() == 1 && feedback.OnStepStart.GetPersistentTarget(0) == director &&
                feedback.OnStepStart.GetPersistentMethodName(0) == "LockAllUI", "feedback retains a locked build UI: " + index);
        }
        require(updated[12].requiredAction == TutorialStepAction.SelectTool && updated[13].requiredAction == TutorialStepAction.SelectBridge &&
            updated[14].requiredAction == TutorialStepAction.CopySelection && updated[15].requiredAction == TutorialStepAction.PositionPastePreview &&
            updated[16].requiredAction == TutorialStepAction.PasteSelection, "real selection, Copy, position and Paste action order is retained");
        require(!updated[14].advanceOnClick && !updated[16].advanceOnClick && !updated[14].showNextButton && !updated[16].showNextButton,
            "Copy/Paste cannot be bypassed by the prompt or raw button clicks");
        require(updated[18].OnStepStart.GetPersistentMethodName(0) == "UnlockSimulation" && updated[18].advanceOnClick && !updated[18].showNextButton,
            "simulation unlock occurs only after the completion Continue, followed by actual Play");
        TutorialStep[] repeated = TrussTutorialBuildAuthoring.BuildUpdatedSteps(updated, director);
        require(TrussTutorialBuildAuthoring.SameSteps(updated, repeated), "migration is idempotent for an already-updated 19-step tutorial");
        require(repeated.Length == 19 && repeated.Skip(6).Count(step => step.showNextButton) == 3, "repeated migration does not duplicate feedback or enable action skipping");

        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(null, director), require, "missing sequence");
        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(updated.Take(12).ToArray(), director), require, "unrecognized/custom sequence length");
        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(updated, null), require, "missing shared director");
        TutorialStep[] invalid = CloneSteps(updated); invalid[14].requiredAction = TutorialStepAction.None;
        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(invalid, director), require, "missing real Copy action");
        invalid = CloneSteps(updated); invalid[12].pointerTarget = null;
        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(invalid, director), require, "missing Selection tool pointer");
        invalid = CloneSteps(updated); invalid[13].worldHighlightObject = null;
        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(invalid, director), require, "missing authored selection volume");
        invalid = CloneSteps(updated); invalid[7].OnStepStart = new UnityEvent();
        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(invalid, director), require, "missing road blueprint callback");
        invalid = CloneSteps(updated); invalid[17].message = "Custom completion";
        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(invalid, director), require, "custom edited completion must not be overwritten");
        invalid = CloneSteps(updated); invalid[6].OnStepStart = new UnityEvent();
        BridgeMaterialSO road = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(AssetDatabase.GUIDToAssetPath("d96454d8483949a4fb084d742f9a0ac8"));
        UnityEventTools.AddObjectPersistentListener<BridgeMaterialSO>(invalid[6].OnStepStart, director.PromptMaterialClick, road);
        UnityEventTools.AddPersistentListener(invalid[6].OnStepStart, director.UnlockSimulation);
        Reject(() => TrussTutorialBuildAuthoring.BuildUpdatedSteps(invalid, director), require, "early simulation unlock");
        if (original.Length == 16)
        {
            invalid = CloneSteps(original); invalid[4].OnStepStart = new UnityEvent();
            UnityEventTools.AddPersistentListener(invalid[4].OnStepStart, director.LockAllUI);
            TutorialStep[] withCustomIntro = TrussTutorialBuildAuthoring.BuildUpdatedSteps(invalid, director);
            require(JsonUtility.ToJson(withCustomIntro[4]) == JsonUtility.ToJson(invalid[4]) &&
                ReferenceEquals(withCustomIntro[4].OnStepStart, invalid[4].OnStepStart), "custom callbacks in retained Erase introduction are preserved, not removed");
        }
        require(sequence.lessonName == lessonBefore && sequence.requiredPreviousLesson == previousBefore && lessonBefore == "Sequence_Build2" &&
            ReferenceEquals(sequence.tutorialSteps, original) && scene.isDirty == dirtyBefore, "released lesson/prerequisite IDs, source array and dirty state are unchanged");
        require(EditorJsonUtility.ToJson(sequence) == sequenceBefore && EditorJsonUtility.ToJson(director) == directorBefore && GeometrySnapshot(sequence) == geometryBefore,
            "source sequence, shared director, ghost endpoint/material/anchor/selection transforms and event references are unchanged");
        return "PASS: " + checks + " read-only Tutorial 2 build migration checks; old-16/new-19 guards, 19-step idempotence, all six original introductions (including Erase/Move), real action callbacks and blueprint geometry preserved. No events, scenes, player saves, or story callbacks were run.";
    }

    private static TutorialStep[] CloneSteps(TutorialStep[] source)
    {
        MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        return source.Select(step => (TutorialStep)clone.Invoke(step, null)).ToArray();
    }

    private static void Reject(Action action, Action<bool, string> require, string label)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        require(rejected, "refuse migration when " + label);
    }

    private static string GeometrySnapshot(TutorialSequence sequence)
    {
        var result = new StringBuilder();
        foreach (Transform item in sequence.GetComponentsInChildren<Transform>(true))
        {
            result.Append(item.GetInstanceID()).Append(':').Append(item.localPosition.ToString("R")).Append(':')
                .Append(item.localRotation.ToString("R")).Append(':').Append(item.localScale.ToString("R")).Append(':').Append(item.gameObject.activeSelf).Append(';');
        }
        foreach (GhostSegment ghost in sequence.GetComponentsInChildren<GhostSegment>(true)) result.Append(EditorJsonUtility.ToJson(ghost));
        foreach (TutorialStep step in sequence.tutorialSteps)
        {
            if (step.worldHighlightObject != null) result.Append(EditorJsonUtility.ToJson(step.worldHighlightObject.transform));
            if (step.pointerTarget != null) result.Append(EditorJsonUtility.ToJson(step.pointerTarget));
        }
        var serialized = new SerializedObject(sequence);
        SerializedProperty steps = serialized.FindProperty("tutorialSteps");
        for (int step = 0; step < steps.arraySize; step++)
        {
            SerializedProperty calls = steps.GetArrayElementAtIndex(step).FindPropertyRelative("OnStepStart")
                .FindPropertyRelative("m_PersistentCalls").FindPropertyRelative("m_Calls");
            for (int call = 0; call < calls.arraySize; call++)
            {
                UnityEngine.Object argument = calls.GetArrayElementAtIndex(call).FindPropertyRelative("m_Arguments")
                    .FindPropertyRelative("m_ObjectArgument").objectReferenceValue;
                if (argument is Transform anchor) result.Append(EditorJsonUtility.ToJson(anchor));
                else if (argument is GameObject container) result.Append(EditorJsonUtility.ToJson(container.transform));
            }
        }
        return result.ToString();
    }
}
#endif
