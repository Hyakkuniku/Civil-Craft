#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Scoped, Undo-supported statistics explanations; no full story migration.</summary>
[InitializeOnLoad]
public static class BridgeStatisticsTutorialAuthoring
{
    private const string Request = "Temp/bridge-statistics-tutorial.request";
    private const string Report = "Logs/bridge-statistics-tutorial.txt";
    private static bool busy;
    static BridgeStatisticsTutorialAuthoring() { EditorApplication.update += Poll; }
    private static bool HasCall(TutorialStep step, string name) => step?.OnStepStart != null &&
        Enumerable.Range(0, step.OnStepStart.GetPersistentEventCount()).Any(i =>
            step.OnStepStart.GetPersistentTarget(i) is BuildTutorialDirector && step.OnStepStart.GetPersistentMethodName(i) == name);
    public static TutorialStep[] WithoutExplanationSteps(TutorialStep[] source)
    {
        if (source == null) return null;
        foreach (var step in source.Where(s => HasCall(s, nameof(BuildTutorialDirector.ExplainBridgeStatistics))))
            Require(TutorialInstructionText.BridgeStatisticsExplanations.Contains(step.message), "A statistics explanation was customized. Review it before reauthoring.");
        return source.Where(s => !HasCall(s, nameof(BuildTutorialDirector.ExplainBridgeStatistics))).ToArray();
    }
    public static TutorialStep[] AddExplanationSteps(TutorialStep[] source, BuildTutorialDirector director, BuildUIController controls)
    {
        TutorialStep[] baseSteps = WithoutExplanationSteps(source);
        Require(baseSteps != null && director != null && controls != null, "Missing tutorial steps or controls.");
        int[] opens = Enumerable.Range(0, baseSteps.Length).Where(i => HasCall(baseSteps[i], nameof(BuildTutorialDirector.PromptOpenStatistics))).ToArray();
        Require(opens.Length == 1, "Expected one real Open Statistics action.");
        var targets = new[] { controls.targetCargoWeightText, controls.totalLengthText, controls.membersCountText,
            controls.deadLoadText, controls.estimatedCapacityText, controls.efficiencyRatioText };
        var pages = TutorialInstructionText.BridgeStatisticsExplanations.Select((message, i) =>
        {
            var step = new TutorialStep { message = message, screenPosition = TutorialPosition.Center, showNextButton = true,
                canSkip = baseSteps[opens[0]].canSkip, lockLook = baseSteps[opens[0]].lockLook,
                lockJump = baseSteps[opens[0]].lockJump, lockRun = baseSteps[opens[0]].lockRun,
                usePointer = targets[i] != null, pointerTarget = targets[i] != null ? targets[i].rectTransform : null,
                // Approach from outside the readout instead of covering its neighboring rows.
                pointerOffset = new Vector2(-280, 0), pointerRotation = -90, advanceOnClick = false,
                requiredAction = TutorialStepAction.None, OnStepStart = new UnityEvent() };
            UnityEventTools.AddPersistentListener(step.OnStepStart, director.ExplainBridgeStatistics);
            return step;
        });
        return baseSteps.Take(opens[0] + 1).Concat(pages).Concat(baseSteps.Skip(opens[0] + 1)).ToArray();
    }
    private static void Poll()
    {
        if (busy || !File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request); busy = true;
        try { if (command == "apply") Apply(); else if (command == "validate") Validate(); else throw new InvalidOperationException("Use apply or validate."); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
        finally { busy = false; }
    }
    private static void Find(out Scene scene, out TutorialSequence sequence, out BuildTutorialDirector director, out BuildUIController controls)
    {
        scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.IsValid() && scene.isLoaded && !EditorApplication.isPlayingOrWillChangePlaymode, "Open CanyonCrossing in Edit Mode.");
        var roots = scene.GetRootGameObjects();
        sequence = roots.SelectMany(r => r.GetComponentsInChildren<TutorialSequence>(true)).Single(s => s.lessonName == "Sequence_Build1");
        director = sequence.GetComponent<BuildTutorialDirector>();
        controls = roots.SelectMany(r => r.GetComponentsInChildren<BuildUIController>(true)).Single();
    }
    [MenuItem("Tools/Civil Craft/Tutorials/Bridge Statistics/Apply Explanations (Do Not Save)")]
    public static void Apply()
    {
        Find(out Scene scene, out TutorialSequence sequence, out BuildTutorialDirector director, out BuildUIController controls);
        TutorialStep[] before = sequence.tutorialSteps;
        var baseSteps = WithoutExplanationSteps(before);
        Require(baseSteps.Length == 28, "Expected the current 28 logical Beam steps.");
        var stats = baseSteps.Single(s => HasCall(s, nameof(BuildTutorialDirector.PromptOpenStatistics)));
        Require(TutorialInstructionText.Compact(stats.message) == TutorialInstructionText.BridgeStatisticsPrompt, "The authored statistics introduction differs; review it manually.");
        var serializedControls = new SerializedObject(controls);
        var safetyVisibility = serializedControls.FindProperty("showEstimatedSafetyReadout");
        Require(safetyVisibility != null, "Refresh Unity to load the hidden legacy safety-row setting.");
        var targets = new[] { controls.targetCargoWeightText, controls.totalLengthText, controls.membersCountText,
            controls.deadLoadText, controls.estimatedCapacityText, controls.efficiencyRatioText };
        Require(controls.statsPanel != null && targets.All(t => t != null && t.transform.IsChildOf(controls.statsPanel.transform)),
            "Bind all six existing statistic rows before applying; no scene changes made.");
        string[] protectedSteps = baseSteps.Where(s => s != stats).Select(JsonUtility.ToJson).ToArray();
        var pages = AddExplanationSteps(before, director, controls);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Explain Bridge Statistics and hide Estimated Safety");
        try
        {
        Undo.RecordObject(sequence, "Explain Bridge Statistics");
        stats.message = TutorialInstructionText.BridgeStatisticsPrompt;
        sequence.tutorialSteps = pages;
        Require(WithoutExplanationSteps(pages).Where(s => s != stats).Select(JsonUtility.ToJson).SequenceEqual(protectedSteps), "An existing tutorial action changed.");
        PrefabUtility.RecordPrefabInstancePropertyModifications(sequence);
        safetyVisibility.boolValue = false;
        serializedControls.ApplyModifiedProperties();
        if (controls.factorOfSafetyText != null)
        {
            Undo.RecordObject(controls.factorOfSafetyText.gameObject, "Hide legacy estimated-safety row");
            controls.factorOfSafetyText.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controls.factorOfSafetyText.gameObject);
        }
        EditorUtility.SetDirty(sequence); EditorSceneManager.MarkSceneDirty(scene);
        Validate();
        Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }
    [MenuItem("Tools/Civil Craft/Tutorials/Bridge Statistics/Validate (Read Only)")]
    public static void Validate()
    {
        Find(out _, out TutorialSequence sequence, out _, out BuildUIController controls);
        var pages = sequence.tutorialSteps.Where(s => HasCall(s, nameof(BuildTutorialDirector.ExplainBridgeStatistics))).ToArray();
        Require(pages.Select(p => p.message).SequenceEqual(TutorialInstructionText.BridgeStatisticsExplanations), "Missing or reordered explanations.");
        int open = Array.FindIndex(sequence.tutorialSteps, step => HasCall(step, nameof(BuildTutorialDirector.PromptOpenStatistics)));
        Require(open >= 0 && sequence.tutorialSteps.Skip(open + 1).Take(pages.Length).SequenceEqual(pages) &&
            HasCall(sequence.tutorialSteps.ElementAtOrDefault(open + 1 + pages.Length), nameof(BuildTutorialDirector.PromptToggleGrid)),
            "Explanations must immediately follow Open Statistics, before the existing Grid action.");
        Require(WithoutExplanationSteps(sequence.tutorialSteps).Length == 28, "A non-explanation step changed.");
        Require(!new SerializedObject(controls).FindProperty("showEstimatedSafetyReadout").boolValue &&
            (controls.factorOfSafetyText == null || !controls.factorOfSafetyText.gameObject.activeSelf), "Estimated Safety should remain hidden.");
        var targets = new[] { controls.targetCargoWeightText, controls.totalLengthText, controls.membersCountText,
            controls.deadLoadText, controls.estimatedCapacityText, controls.efficiencyRatioText };
        for (int i = 0; i < pages.Length; i++)
        {
            TutorialStep page = pages[i];
            Require(page.showNextButton && !page.advanceOnClick && page.requiredAction == TutorialStepAction.None &&
                page.usePointer && targets[i] != null && page.pointerTarget == targets[i].rectTransform &&
                page.pointerTarget.IsChildOf(controls.statsPanel.transform), "Explanation navigation/pointer binding is incomplete.");
        }
        Require(!sequence.tutorialSteps.Any(s => s.message.IndexOf("factor of safety", StringComparison.OrdinalIgnoreCase) >= 0), "An authored FoS mention remains.");
        var report = new StringBuilder("PASS: "); report.Append(pages.Length).AppendLine(" individually explained readouts, real open gate retained, existing 28 logical instructions and callbacks preserved.");
        report.AppendLine("PASS: Continue pages point to the actual rows, lock construction and keep Statistics open. No statistics-close step was added. No scene was saved.");
        File.WriteAllText(Report, report.ToString());
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
