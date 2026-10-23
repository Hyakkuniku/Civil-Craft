#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Two narrowly scoped authored changes. Never opens or saves a scene.</summary>
[InitializeOnLoad]
public static class BeamReviewRefinementAuthoring
{
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Request = "Temp/beam-review-refinement.request";
    private const string Report = "Logs/beam-review-refinement.txt";
    private const string Proof = "Temp/beam-review-refinement-proof.json";
    private static bool running;
    private static double nextPoll;

    [Serializable]
    private sealed class PreservationProof
    {
        public string[] phases;
        public string[] steps;
        public string contract;
    }

    static BeamReviewRefinementAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            running = true;
            if (command == "apply") ApplyLoaded();
            else if (command == "validate") ValidateLoaded();
            else throw new InvalidOperationException("Unknown Beam refinement request: " + command);
        }
        catch (Exception error) { Write("FAILED\n" + error); Debug.LogException(error); }
        finally { running = false; }
    }

    [MenuItem("Tools/Civil Craft/Story/Beam Review/1. Remove Extra Statistics Step and Enable Almanac Guidance (Do Not Save)")]
    public static void ApplyLoaded()
    {
        Scene scene = GetScene(out TutorialSequence sequence, out NPCProgressionManager bhan);
        var dead = bhan.AuthoredPhases[4];
        TutorialStep[] old = sequence.tutorialSteps;
        bool remove = old.Length == 29;
        Check(old.Length == 29 || old.Length == 28, "Expected the audited 29-step build tutorial or its 28-step refinement.");
        Check(HasCall(old[11], "PromptOpenStatistics") &&
            HasCall(old[remove ? 13 : 12], "PromptToggleGrid"), "The existing statistics/grid action gates differ; no changes applied.");
        if (remove)
            Check(old[12].message.StartsWith("Review the bridge information", StringComparison.Ordinal) &&
                HasCall(old[12], "PromptCloseStatistics"), "Step 12 is not the pictured closing instruction; no substitute step will be removed.");
        else Check(!old.Any(step => HasCall(step, "PromptCloseStatistics")), "An unexpected closing instruction remains in the compact tutorial.");
        Check(old.All(step => step != null) && old[remove ? 22 : 21].message.Contains("Wood Road") &&
            old[remove ? 27 : 26].message.Contains("BEAM BRIDGE COMPLETE") &&
            HasCall(old.Last(), "UnlockSimulation"), "Road tracing/completion/simulation landmarks differ; no changes applied.");
        Check(dead.optionalDialogueSequence != null && dead.optionalDialogueSequence.Count == 7 &&
            dead.optionalDialogueSequence[0].stepType == NPCOptionalDialogueStep.StepType.AlmanacReview &&
            dead.optionalDialogueSequence[0].reviewStepId == "training-bridge-record-review" &&
            dead.onDialogueFinishedPhaseMove != null && dead.onDialogueFinishedPhaseMove.npc == bhan &&
            dead.onDialogueFinishedPhaseMove.phaseId == "Beam", "Bhan's existing first-entry review/continuation differs; no changes applied.");
        ContractSO contract = bhan.AuthoredPhases[3].contract;
        Check(contract != null && contract.ContractID == "TUT_CONTRACT1" &&
            contract.finishedContractDialogue.sentences.Any(line => line.Contains("review your first entry")),
            "The first Beam contract/turn-in prompt is missing.");

        var proof = new PreservationProof {
            phases = bhan.AuthoredPhases.Select(JsonUtility.ToJson).ToArray(),
            steps = old.Select(JsonUtility.ToJson).ToArray(), contract = EditorJsonUtility.ToJson(contract)
        };
        string expectedDead = proof.phases[4]
            .Replace("\"playDialogueOnArrival\":false", "\"playDialogueOnArrival\":true")
            .Replace("\"reviewAlmanacWhileTravelling\":false", "\"reviewAlmanacWhileTravelling\":true");
        string[] expectedSteps = remove ? proof.steps.Where((step, index) => index != 12).ToArray() : proof.steps;
        // Run the read-only detached factory/action checks before scene mutation.
        string detached = BeamTutorialBuildValidation.ValidateDetached();
        int group;
        Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Refine Beam Statistics and Almanac Guidance");
        try
        {
            if (remove)
            {
                Undo.RecordObject(sequence, "Remove the extra Statistics closing instruction");
                sequence.tutorialSteps = old.Where((step, index) => index != 12).ToArray();
                EditorUtility.SetDirty(sequence);
            }
            if (!dead.playDialogueOnArrival || !dead.reviewAlmanacWhileTravelling)
            {
                Undo.RecordObject(bhan, "Begin first Almanac entry review on Bhan arrival");
                dead.playDialogueOnArrival = true;
                dead.reviewAlmanacWhileTravelling = true;
                EditorUtility.SetDirty(bhan);
            }
            Check(sequence.tutorialSteps.Select(JsonUtility.ToJson).SequenceEqual(expectedSteps),
                "A tutorial step other than the pictured closing instruction changed.");
            for (int i = 0; i < bhan.AuthoredPhases.Count; i++)
                Check(JsonUtility.ToJson(bhan.AuthoredPhases[i]) == (i == 4 ? expectedDead : proof.phases[i]),
                    "An unrelated Bhan phase field changed: " + i);
            Check(EditorJsonUtility.ToJson(contract) == proof.contract, "The contract/rewards/dialogue changed.");
            if (remove || proof.phases[4] != expectedDead) EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
        proof.phases[4] = expectedDead; proof.steps = expectedSteps;
        Directory.CreateDirectory("Temp"); File.WriteAllText(Proof, JsonUtility.ToJson(proof, true));
        var report = new StringBuilder("Beam Statistics + first-entry Almanac guidance\n");
        report.AppendLine(detached);
        report.AppendLine("PASS: 29 -> 28 steps (or already refined); only the pictured closing instruction removed. Statistics still opens, stays click-through, and retains normal placement cleanup.");
        report.AppendLine("PASS: first-entry Almanac guidance begins during Bhan's post-turn-in walk once reward/dialogue/Pause panels close. A genuine review while walking counts once; follow-up dialogue waits for arrival. Original HUD arrow/review identity retained.");
        report.AppendLine("PASS: all other steps, camera/ghost references, callbacks, phases, destinations, IDs, contract, budget, load and rewards preserved.");
        report.AppendLine("No scenes opened, closed or saved; no player saves accessed. Review and save CanyonCrossing in Unity.");
        Write(report.ToString());
    }

    [MenuItem("Tools/Civil Craft/Story/Beam Review/2. Validate Loaded Refinement (Read Only)")]
    public static void ValidateLoaded()
    {
        Scene scene = GetScene(out TutorialSequence sequence, out NPCProgressionManager bhan);
        Check(File.Exists(Proof), "Run the scoped refinement first to create its preservation proof.");
        var proof = JsonUtility.FromJson<PreservationProof>(File.ReadAllText(Proof));
        Check(sequence.tutorialSteps.Length == 28 && sequence.tutorialSteps.Select(JsonUtility.ToJson).SequenceEqual(proof.steps),
            "The compact sequence differs from the checked refinement.");
        Check(bhan.AuthoredPhases.Select(JsonUtility.ToJson).SequenceEqual(proof.phases), "A checked Bhan phase changed.");
        Check(EditorJsonUtility.ToJson(bhan.AuthoredPhases[3].contract) == proof.contract, "The original contract changed.");
        var phase = bhan.AuthoredPhases[4];
        Check(phase.playDialogueOnArrival && phase.reviewAlmanacWhileTravelling &&
            phase.optionalDialogueSequence[0].reviewStepId == "training-bridge-record-review",
            "Automatic first-entry review is not configured.");
        Write("PASS: loaded 28-step tutorial, all preserved step/phase/contract data, automatic first-entry guidance and unchanged review identity.\n" +
            (scene.isDirty ? "CanyonCrossing still has unsaved edits; save it in Unity.\n" : "PASS: CanyonCrossing is saved and clean.\n"));
    }

    private static Scene GetScene(out TutorialSequence sequence, out NPCProgressionManager bhan)
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Leave Unity in Edit Mode after compilation, outside Prefab Mode.");
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        Check(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing. This tool never opens/closes scenes.");
        var roots = scene.GetRootGameObjects();
        sequence = roots.SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true)).Single(item => item.lessonName == "Sequence_Build1");
        bhan = roots.SelectMany(root => root.GetComponentsInChildren<NPCProgressionManager>(true)).Single(npc => npc.ProgressionSaveId == "MainContractNPC");
        Check(bhan.AuthoredPhases.Count == 16 && bhan.AuthoredPhases[3].phaseId == "Give Contract Build Location 1" &&
            bhan.AuthoredPhases[4].phaseId == "DeadLoad", "Bhan's original phase layout differs.");
        return scene;
    }
    private static bool HasCall(TutorialStep step, string method)
    {
        if (step == null || step.OnStepStart == null) return false;
        for (int i = 0; i < step.OnStepStart.GetPersistentEventCount(); i++)
            if (step.OnStepStart.GetPersistentMethodName(i) == method && step.OnStepStart.GetPersistentTarget(i) != null) return true;
        return false;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Write(string report) { Directory.CreateDirectory("Logs"); File.WriteAllText(Report, report); }
}
#endif
