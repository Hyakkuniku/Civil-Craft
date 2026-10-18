#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Explicit migration of NEW Tutorial 1 only. No edits without a menu/request.</summary>
[InitializeOnLoad]
public static class BeamTutorialStoryAuthoring
{
    public const string RequestPath = "Temp/beam-tutorial-story-v2.request";
    public const string ReportPath = "Logs/beam-tutorial-story-v2.txt";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string House = "Assets/Scenes/BHAN HOUSE.unity";
    private static readonly FieldInfo PhasesField = typeof(NPCProgressionManager).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
    private static double nextPoll;

    static BeamTutorialStoryAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        string command = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        try
        {
            if (command == "apply") ApplySavedStory();
            else if (command == "validate") ValidateLoaded();
            else if (command == "configure-and-validate") { ConfigureAssessment(); ValidateLoaded(); }
            else if (command == "finish-close-house") FinishCanyonSetup();
            else throw new InvalidOperationException("Unknown tutorial migration request.");
        }
        catch (Exception error)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, "FAILED\n" + error);
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Civil Craft/Story/Beam Tutorial/1. Apply New Tutorial to Saved Scenes")]
    public static void ApplySavedStory()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before applying the tutorial update.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your current scene edits first. No scene was modified.");
        SceneSetup[] original = EditorSceneManager.GetSceneManagerSetup();
        StringBuilder report = new StringBuilder("NEW Tutorial 1 Beam Bridge\n");
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-BeamTutorial-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, Path.Combine(backup, "CanyonCrossing.unity"));
        File.Copy(House, Path.Combine(backup, "BHAN HOUSE.unity"));
        report.AppendLine("Recoverable pre-migration scenes: " + backup);
        bool authored = false;
        bool saved = false;
        try
        {
            Scene canyon = SceneManager.GetSceneByPath(Canyon);
            if (!canyon.IsValid() || !canyon.isLoaded) canyon = EditorSceneManager.OpenScene(Canyon, OpenSceneMode.Additive);
            Scene house = SceneManager.GetSceneByPath(House);
            if (!house.IsValid() || !house.isLoaded) house = EditorSceneManager.OpenScene(House, OpenSceneMode.Additive);
            // Validate BOTH scenes and all fixtures before any scene mutation/save.
            ValidateCanyonReferences(canyon);
            BeamTutorialHouseAuthoring.Preflight(canyon);
            BeamTutorialHouseAuthoring.Preflight(house);
            BeamTutorialBuildAuthoring.Preflight(canyon);
            ConfigureAssessment();
            report.AppendLine(BeamTutorialHouseAuthoring.ValidateDetachedFixtures());
            report.AppendLine(BeamTutorialBuildValidation.ValidateDetached());
            authored = true;
            ApplyCanyon(canyon, report);
            BeamTutorialHouseAuthoring.Apply(canyon, report);
            BeamTutorialBuildAuthoring.Apply(canyon);
            BeamTutorialHouseAuthoring.Apply(house, report);
            report.AppendLine("Updated first build instructions with real statistics, grid and hold-to-inspect gates.");
            ValidateCanyon(canyon, report);
            EditorSceneManager.MarkSceneDirty(canyon);
            if (!EditorSceneManager.SaveScene(canyon)) throw new IOException("Could not save updated CanyonCrossing.");
            if (house.isDirty && !EditorSceneManager.SaveScene(house)) throw new IOException("Could not save updated Bhan house.");
            saved = true;
        }
        finally
        {
            // Never discard a partially authored unsaved scene after a failure.
            // Preflight failures and successful saves restore the original setup.
            if (!authored || saved) EditorSceneManager.RestoreSceneManagerSetup(original);
            else Debug.LogWarning("Tutorial update stopped with unsaved edits retained for review. Backups: " + backup);
        }
        report.AppendLine("PASS: saved both tutorial scenes; preserved scene setup, all phase indexes/IDs, budget, rewards and later story.");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }

    [MenuItem("Tools/Civil Craft/Story/Beam Tutorial/2. Validate Loaded Canyon")]
    public static void ValidateLoaded()
    {
        StringBuilder report = new StringBuilder();
        AssetDatabase.ImportAsset("Assets/BridgeBuilder/Data/Quized/BeamBridgeQuiz.asset", ImportAssetOptions.ForceUpdate);
        ValidateCanyon(SceneManager.GetSceneByPath(Canyon), report);
        report.AppendLine(BeamTutorialHouseAuthoring.ValidateDetachedFixtures());
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }

    // Used to close the clean house scene opened additively by this migration
    // when recovering an interrupted run. Never closes a dirty/user-active scene.
    private static void FinishCanyonSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Unity in Edit Mode.");
        Scene canyon = SceneManager.GetSceneByPath(Canyon);
        Scene house = SceneManager.GetSceneByPath(House);
        ValidateCanyon(canyon, new StringBuilder());
        if (house.IsValid() && house.isLoaded)
        {
            if (house.isDirty || SceneManager.GetActiveScene() != canyon)
                throw new InvalidOperationException("The added house scene has unsaved edits or is active; it was left open for review.");
            if (!EditorSceneManager.CloseScene(house, true)) throw new IOException("Could not close the saved additive house scene.");
        }
        File.AppendAllText(ReportPath, "PASS: closed the migration's saved additive house scene; CanyonCrossing remains active for testing.\n");
    }

    private static T[] InScene<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static NPCProgressionManager Bhan(Scene scene) => InScene<NPCProgressionManager>(scene)
        .Single(npc => npc.ProgressionSaveId == "MainContractNPC");
    private static List<NPCProgressionPhase> Phases(NPCProgressionManager npc) => (List<NPCProgressionPhase>)PhasesField.GetValue(npc);
    private static NPCProgressionPhase Phase(List<NPCProgressionPhase> phases, string id) => phases.Single(p => p.phaseId == id);
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Missing tutorial asset: " + path);
    private static void ConfigureAssessment()
    {
        QuizData quiz = Load<QuizData>("Assets/BridgeBuilder/Data/Quized/BeamBridgeQuiz.asset");
        if (quiz.assessmentMode) return;
        Undo.RecordObject(quiz, "Use submitted assessment for Beam Bridge tutorial");
        quiz.assessmentMode = true;
        EditorUtility.SetDirty(quiz);
        AssetDatabase.SaveAssetIfDirty(quiz);
    }
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static NPCOptionalDialogueStep Say(params string[] lines) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.Dialogue, dialogue = new Dialogue { name = "Professor Bhan", sentences = lines } };
    private static NPCOptionalDialogueStep Material(BridgeMaterialSO material) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.MaterialUnlock, material = material, materialButtonLabel = "CONTINUE" };
    private static NPCOptionalDialogueStep Lesson(LessonData lesson) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = lesson };

    private static void ValidateCanyonReferences(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CanyonCrossing to validate the tutorial.");
        List<NPCProgressionPhase> phases = Phases(Bhan(scene));
        string[] expected = { "Greetings", "LiveLoad", "WorkBench", "Give Contract Build Location 1", "DeadLoad", "Beam", "Build Tutorial 2" };
        if (phases.Count < expected.Length || !expected.SequenceEqual(phases.Take(expected.Length).Select(p => p.phaseId)))
            throw new InvalidOperationException("Bhan's released phase order differs. Migration stopped; no index substitution is permitted.");
        if (Phase(phases, expected[3]).contract == null || Phase(phases, expected[3]).contract.ContractID != "TUT_CONTRACT1")
            throw new InvalidOperationException("First training contract reference is missing or changed.");
        if (Phase(phases, expected[5]).optionalDialogueSequence == null || Phase(phases, expected[5]).optionalDialogueSequence.Count < 2)
            throw new InvalidOperationException("Existing Tutorial 2 transition sequence is missing.");
        NPCProgressionPhase dead = Phase(phases, "DeadLoad");
        int calls = dead.onDialogueFinished.GetPersistentEventCount();
        bool originalCallbacks = calls == 2 &&
            dead.onDialogueFinished.GetPersistentMethodName(0) == "MoveToSelectedPhase" &&
            dead.onDialogueFinished.GetPersistentTarget(0) is NPCPhaseAction &&
            dead.onDialogueFinished.GetPersistentMethodName(1) == "ShowLesson" &&
            dead.onDialogueFinished.GetPersistentTarget(1) is LessonTrigger;
        bool alreadyMigrated = calls == 0 && dead.onDialogueFinishedPhaseMove != null &&
            dead.onDialogueFinishedPhaseMove.npc == Bhan(scene) && dead.onDialogueFinishedPhaseMove.phaseId == "Beam";
        if (!originalCallbacks && !alreadyMigrated)
            throw new InvalidOperationException("Dead Load has additional or changed callbacks. Migration stopped to preserve those edits.");
        Load<LessonData>("Assets/BridgeBuilder/Data/Lessons/Beam.asset");
        Load<LessonData>("Assets/BridgeBuilder/Data/Lessons/DeadLoad.asset");
        Load<QuizData>("Assets/BridgeBuilder/Data/Quized/BeamBridgeQuiz.asset");
        if (!InScene<BuildLocationLessonTrigger>(scene).Any(trigger => new SerializedObject(trigger)
            .FindProperty("buildLocation").objectReferenceValue == Phase(phases, expected[3]).targetBuildLocation))
            throw new InvalidOperationException("First bridge's old lesson trigger is missing; cannot safely migrate lesson timing.");
    }

    private static void ApplyCanyon(Scene scene, StringBuilder report)
    {
        NPCProgressionManager bhan = Bhan(scene);
        List<NPCProgressionPhase> phases = Phases(bhan);
        Undo.RecordObject(bhan, "Update Beam Bridge tutorial story");
        Phase(phases, "Greetings").phaseDialogue = Say(
            "There you are, {PlayerName}.", "Are you ready to learn the basics?",
            "Before we begin construction, there's something I want you to inspect.", "Come with me.").dialogue;
        Phase(phases, "LiveLoad").phaseDialogue = Say("Inspect the " + Em("vehicle") + ".").dialogue;

        BridgeMaterialSO road = Load<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Wood Road.asset");
        BridgeMaterialSO pier = Load<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Pier.asset");
        Phase(phases, "WorkBench").optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("Now that you've learned about live loads, let's head to the construction area.",
                "Here we are, {PlayerName}.", "This is where you'll build your first bridge.",
                "The " + Em("Blueprint") + " shows the area that needs to be connected.",
                "Before you begin, let's take a look at the " + Em("materials") + " available for this project."),
            Say("First, you'll be using a " + Em("Wood Road") + " for the bridge deck."), Material(road),
            Say("You'll also need " + Em("Wood Piers") + " to support the bridge."), Material(pier),
            Say("These are the " + Em("materials") + " available for this project.",
                "Use them carefully and make sure your bridge can safely carry the " + Em("required load") + ".",
                "But before I give you your first contract, there's something you should understand about the type of bridge you'll be constructing."),
            Say("For your first project, you'll be constructing a " + Em("Beam Bridge") + ".",
                "A beam bridge is one of the simplest types of bridges.",
                "It uses a horizontal structure to carry loads toward its supports.",
                "When a vehicle crosses, its weight pushes down on the bridge deck.",
                "The beam resists that load through bending and transfers the forces toward its supports.",
                "Those supports then carry the forces toward the ground.",
                "That's why a beam bridge needs both a properly supported roadway and structural members strong enough to carry the required load.",
                Em("Remember, the bridge must remain stable while the vehicle crosses") + "."),
            Lesson(Load<LessonData>("Assets/BridgeBuilder/Data/Lessons/Beam.asset"))
        };
        Phase(phases, "WorkBench").unlockFeatureIdAfterDialogue = string.Empty;
        NPCProgressionPhase dead = Phase(phases, "DeadLoad");
        dead.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.AlmanacReview, reviewStepId = "training-bridge-record-review" },
            Say("Keep that record with you, {PlayerName}.",
                "You've now seen how a beam bridge carries a vehicle across a gap.",
                "Every project you complete from this point forward will become part of your engineering journey.",
                "But before we move on, there's one more thing I want you to notice about the bridge you just built."),
            Say("Good work, {PlayerName}.", "Your bridge is standing, and it can carry the required " + Em("Live Load") + ".",
                "But look at your bridge now.", "The vehicle has already crossed, yet the bridge is still carrying weight.",
                "That's because the roadway and supports have weight of their own.",
                "This permanent weight is called " + Em("Dead Load") + ".",
                "Even when no vehicles or people are crossing, a bridge must support its own weight.",
                "And when a vehicle enters, the bridge must carry both its dead load and the additional live load."),
            Lesson(Load<LessonData>("Assets/BridgeBuilder/Data/Lessons/DeadLoad.asset")),
            Say("Before we move on, there's one more thing you need to complete.",
                "You've learned about " + Em("Live Load") + ", " + Em("Dead Load") + ", and the basics of a " + Em("Beam Bridge") + ".",
                "Now it's time to see how much you've learned.", "I've prepared a short " + Em("quiz") + " for you.",
                "But don't worry. You can review your " + Em("lessons in the Almanac") + " before starting.",
                "Once you begin the quiz, you won't be able to leave or access your lessons until you've submitted your answers.",
                "Take your time and review everything before you begin."),
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz,
                quiz = Load<QuizData>("Assets/BridgeBuilder/Data/Quized/BeamBridgeQuiz.asset"), quizStepId = "beam-bridge-engineering-assessment-v1" },
            Say("Now you know that a bridge carries both the loads placed on it and its own weight.",
                "Keep that in mind when designing your structures.", "Follow me.",
                "I have another " + Em("contract") + " waiting for you.")
        };
        dead.unlockFeatureIdAfterDialogue = string.Empty;
        dead.showFeatureCollectPopup = false;
        dead.repeatDialogue = false;
        // The previous event moved first and opened the lesson second. The ordered
        // reader and assessment now own that wait; phase travel happens last.
        dead.onDialogueFinished = new UnityEvent();
        dead.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = bhan, phaseId = "Beam" };

        NPCOptionalDialogueStep introduction = Phase(phases, "Beam").optionalDialogueSequence
            .First(step => step.stepType == NPCOptionalDialogueStep.StepType.Dialogue);
        string marker = "You've completed your first training project.";
        if (introduction.dialogue.sentences == null || !introduction.dialogue.sentences.Contains(marker))
            introduction.dialogue.sentences = new[]
            {
                marker,
                "You learned how to move through the construction area, use your camera, work with contracts, build a bridge, test it, and record your work in the Almanac.",
                "You also learned about your first bridge type, the " + Em("Beam Bridge") + ".",
                "And you've encountered two important engineering terms: " + Em("Live Load") + " and " + Em("Dead Load") + ".",
                "Remember them.", "From this point forward, I won't always tell you exactly how to build.",
                "Your projects will become increasingly challenging.",
                "You'll learn new structural concepts by facing new engineering problems.",
                "That is where real experience begins."
            }.Concat(introduction.dialogue.sentences ?? Array.Empty<string>()).ToArray();
        EditorUtility.SetDirty(bhan);

        BuildLocation first = Phase(phases, "Give Contract Build Location 1").targetBuildLocation;
        foreach (BuildLocationLessonTrigger trigger in InScene<BuildLocationLessonTrigger>(scene))
        {
            SerializedObject serialized = new SerializedObject(trigger);
            if (serialized.FindProperty("buildLocation").objectReferenceValue != first) continue;
            Undo.RecordObject(trigger, "Move Beam lesson before contract");
            serialized.FindProperty("listenForSavedBridge").boolValue = false;
            serialized.ApplyModifiedProperties();
            trigger.enabled = false; // Also guards explicit legacy post-save dispatch.
            EditorUtility.SetDirty(trigger);
        }
        foreach (LessonTrigger trigger in InScene<LessonTrigger>(scene))
        {
            SerializedObject serialized = new SerializedObject(trigger);
            if (serialized.FindProperty("advanceNPC").objectReferenceValue != bhan ||
                serialized.FindProperty("requiredNPCPhaseIndex").intValue != 1) continue;
            Undo.RecordObject(trigger, "Preserve one-time vehicle inspection gate");
            serialized.FindProperty("completionSaveKey").stringValue = "Bhan_LiveLoad_Inspection";
            serialized.FindProperty("requireAlmanacReviewBeforeAdvance").boolValue = true;
            serialized.ApplyModifiedProperties();
        }
        TutorialSequence firstReport = InScene<TutorialSequence>(scene).Single(s => s.lessonName == "Sequence_FirstLevelCompletion");
        Undo.RecordObject(firstReport, "Update Beam test report guidance");
        firstReport.tutorialSteps[0].message = Em("BRIDGE SUCCESSFULLY TESTED!") + " Your Beam Bridge safely carried the " + Em("200 kg vehicle") + " across the canyon. Your first bridge has passed the structural simulation.";
        firstReport.tutorialSteps[firstReport.tutorialSteps.Length - 1].message = "Choose " + Em("RETRY") + " to improve the design, or " + Em("SAVE & CONTINUE") + " to record your completed Training Beam Bridge in the Almanac. Your training contract will be ready to turn in.";
        EditorUtility.SetDirty(firstReport);
        report.AppendLine("Updated Bhan's existing phases in place: inspection > materials > Beam lesson > contract > review > Dead Load > Almanac assessment > existing Tutorial 2 transition.");
    }

    private static void ValidateCanyon(Scene scene, StringBuilder report)
    {
        ValidateCanyonReferences(scene);
        List<NPCProgressionPhase> phases = Phases(Bhan(scene));
        var work = Phase(phases, "WorkBench").optionalDialogueSequence;
        var dead = Phase(phases, "DeadLoad").optionalDialogueSequence;
        if (work.Last().stepType != NPCOptionalDialogueStep.StepType.LessonIntroduction ||
            work.Last().lesson != Load<LessonData>("Assets/BridgeBuilder/Data/Lessons/Beam.asset"))
            throw new InvalidOperationException("Beam Bridge introduction must finish before the contract offer.");
        if (dead.Count != 7 || dead[0].stepType != NPCOptionalDialogueStep.StepType.AlmanacReview ||
            dead[3].stepType != NPCOptionalDialogueStep.StepType.LessonIntroduction ||
            dead[5].stepType != NPCOptionalDialogueStep.StepType.Quiz || !dead[5].quiz.assessmentMode ||
            !dead[5].quiz.TryValidate(out string _) || dead[5].quiz.QuestionCount != 5)
            throw new InvalidOperationException("Post-build sequence is incomplete: " +
                "count=" + dead.Count + "; types=" + string.Join(",", dead.Select(step => step.stepType.ToString())) +
                "; quiz=" + (dead.Count > 5 && dead[5].quiz != null ?
                    dead[5].quiz.name + ", assessment=" + dead[5].quiz.assessmentMode + ", questions=" + dead[5].quiz.QuestionCount +
                    ", valid=" + dead[5].quiz.TryValidate(out string detail) + ", error=" + detail : "missing"));
        if (!string.IsNullOrWhiteSpace(Phase(phases, "DeadLoad").unlockFeatureIdAfterDialogue) ||
            Phase(phases, "DeadLoad").onDialogueFinishedPhaseMove.phaseId != "Beam")
            throw new InvalidOperationException("Assessment must have no feature reward and continue to the existing Beam phase only after submission.");
        ContractSO contract = Phase(phases, "Give Contract Build Location 1").contract;
        if (contract.liveLoadWeight != 200 || contract.ContractID != "TUT_CONTRACT1")
            throw new InvalidOperationException("Training vehicle must match the 200 kg contract without changing its identity.");
        report.AppendLine("PASS: legacy phase indexes 0–6 unchanged, Beam lesson precontract, review/lesson/5-question assessment ordering, no quiz reward, stable continuation.");
    }
}
#endif
