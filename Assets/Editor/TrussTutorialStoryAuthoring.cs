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
using UnityEngine.SceneManagement;

/// <summary>Explicit NEW Tutorial 2 migration. Never saves a scene or edits another story.</summary>
[InitializeOnLoad]
public static class TrussTutorialStoryAuthoring
{
    public const string RequestPath = "Temp/truss-tutorial-story-v1.request";
    public const string ReportPath = "Logs/truss-tutorial-story-v1.txt";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private const string QuizStepId = "truss-bridge-engineering-assessment-v1";
    private static readonly string[] PhaseIds = { "Greetings", "LiveLoad", "WorkBench", "Give Contract Build Location 1", "DeadLoad", "Beam", "Build Tutorial 2", "Follow", "City", "Shopkeeper", "Go to Shopkeeper", "CollectMap", "Farewell", "Find Reed", "Delete", "Delete" };
    private static readonly FieldInfo PhasesField = typeof(NPCProgressionManager).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
    private static double nextPoll;
    static TrussTutorialStoryAuthoring() { EditorApplication.update += Poll; }
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
            else throw new InvalidOperationException("Unknown Truss Tutorial request: " + command);
        }
        catch (Exception error)
        {
            WriteReport("FAILED\n" + error);
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Civil Craft/Story/Truss Tutorial/1. Apply to Saved Canyon (Do Not Save)")]
    public static void ApplySavedStory()
    {
        Scene scene = LoadedCanyon();
        if (scene.isDirty) throw new InvalidOperationException("Save your current CanyonCrossing edits first. No changes were applied or saved.");
        Preflight(scene);
        string[] untouched = Phases(Bhan(scene)).Select(JsonUtility.ToJson).ToArray();
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-TrussTutorial-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, Path.Combine(backup, "CanyonCrossing.unity"));
        int group = Undo.GetCurrentGroup();
        Undo.IncrementCurrentGroup();
        group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Update NEW Tutorial 2 Truss Bridge");
        try
        {
            ApplyCanyon(scene);
            TrussTutorialBuildAuthoring.Apply(scene);
            var phases = Phases(Bhan(scene));
            for (int i = 0; i < phases.Count; i++)
                if ((i < 5 || i > 7) && JsonUtility.ToJson(phases[i]) != untouched[i])
                    throw new InvalidOperationException("Unrelated phase changed: " + i + ". The migration was rolled back.");
            StringBuilder report = new StringBuilder("NEW Tutorial 2 - Truss Bridge\n");
            ValidateCanyon(scene, report);
            report.AppendLine(TrussTutorialBuildValidation.ValidateReadOnly(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            report.AppendLine("PASS: preserved all 16 phase IDs/indexes, unrelated phases, contract budget/rewards and authored bridge geometry.");
            report.AppendLine("No world route gate added, as requested. No other scene was opened, closed or saved.");
            report.AppendLine("Recoverable pre-migration Canyon: " + backup);
            report.AppendLine("NEEDS_SAVE: CanyonCrossing. Review, then save this scene in Unity.");
            WriteReport(report.ToString());
            Debug.Log(report.ToString());
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    [MenuItem("Tools/Civil Craft/Story/Truss Tutorial/2. Validate Loaded Canyon")]
    public static void ValidateLoaded()
    {
        Scene scene = LoadedCanyon();
        StringBuilder report = new StringBuilder("NEW Tutorial 2 validation\n");
        ValidateCanyon(scene, report);
        report.AppendLine(TrussTutorialBuildValidation.ValidateReadOnly(scene));
        report.AppendLine("NEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    public static void Preflight(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.path != Canyon) throw new InvalidOperationException("Open CanyonCrossing before applying Tutorial 2.");
        NPCProgressionManager bhan = Bhan(scene);
        List<NPCProgressionPhase> phases = Phases(bhan);
        if (!phases.Select(p => p.phaseId).SequenceEqual(PhaseIds)) throw new InvalidOperationException("Released Bhan phase IDs/order differ. No phases were substituted.");
        NPCProgressionPhase beam = phases[5], second = phases[6], follow = phases[7];
        ContractSO contract = Load<ContractSO>(Data + "Contract/CanyonCrossing/Bhan/Bhan Tutorial 2.asset");
        if (second.contract != contract || contract.ContractID != "TUT_CONTRACT2" || second.targetBuildLocation == null ||
            (second.targetBuildLocation.activeContract != null && second.targetBuildLocation.activeContract != contract) || second.targetBuildLocation.gameObject.scene != scene ||
            second.targetLocation != beam.targetLocation || beam.contract != null || follow.contract != null || follow.targetLocation != null)
            throw new InvalidOperationException("The audited Tutorial 2 NPC/build-site references differ. Review them before migration.");
        if (contract.liveLoadWeight != 400 || contract.budget != 75000 || contract.goldReward != 500 || contract.expReward != 150 || contract.autoCollectReward)
            throw new InvalidOperationException("Tutorial 2 must retain its identity, budget and rewards, and use the updated 400 kg load.");
        BridgeMaterialSO woodBeam = Load<BridgeMaterialSO>(Data + "Resources/Beam.asset");
        LessonData truss = Load<LessonData>(Data + "Lessons/Truss.asset");
        LessonData equilibrium = Load<LessonData>(Data + "Lessons/Equilibruim.asset");
        QuizData quiz = Load<QuizData>(Data + "Quized/TrussBridgeQuiz.asset");
        var introduction = beam.optionalDialogueSequence;
        bool legacyIntro = introduction != null && introduction.Count == 2 && introduction.All(step => step != null) &&
            introduction[0].stepType == NPCOptionalDialogueStep.StepType.Dialogue &&
            introduction[1].stepType == NPCOptionalDialogueStep.StepType.MaterialUnlock && introduction[1].material == woodBeam;
        bool updatedIntro = introduction != null && introduction.Count == 7 && introduction.All(step => step != null) &&
            introduction.Select(step => (int)step.stepType).SequenceEqual(new[] { 0, 0, 1, 0, 3, 0, 3 }) &&
            introduction[2].material == woodBeam && introduction[4].lesson == truss && introduction[6].lesson == equilibrium;
        if (!legacyIntro && !updatedIntro)
            throw new InvalidOperationException("The pre-contract transition is neither the audited legacy flow nor this migration.");
        if (follow.optionalDialogueSequence != null && follow.optionalDialogueSequence.Count != 0 &&
            !(follow.optionalDialogueSequence.Count == 3 && follow.optionalDialogueSequence.All(step => step != null) &&
              follow.optionalDialogueSequence.Select(step => (int)step.stepType).SequenceEqual(new[] { 0, 2, 0 }) &&
              follow.optionalDialogueSequence[1].quizStepId == QuizStepId && follow.optionalDialogueSequence[1].quiz == quiz))
            throw new InvalidOperationException("Follow contains custom ordered content. It was not replaced.");
        if (second.optionalDialogueSequence != null && second.optionalDialogueSequence.Count != 0)
            throw new InvalidOperationException("Tutorial 2 contract acceptance contains custom ordered content. It was not replaced.");
        if (beam.onDialogueFinished == null || beam.onDialogueFinished.GetPersistentEventCount() != 1 ||
            !(beam.onDialogueFinished.GetPersistentTarget(0) is NPCPhaseAction) || beam.onDialogueFinished.GetPersistentMethodName(0) != "MoveToSelectedPhase")
            throw new InvalidOperationException("The existing pre-contract continuation differs. It was not replaced.");
        var continuation = new SerializedObject(beam.onDialogueFinished.GetPersistentTarget(0));
        if (continuation.FindProperty("npc").objectReferenceValue != bhan || continuation.FindProperty("phaseId").stringValue != "Build Tutorial 2")
            throw new InvalidOperationException("The existing pre-contract callback no longer targets Build Tutorial 2.");
        if (beam.onDialogueFinished.GetPersistentListenerState(0) == UnityEngine.Events.UnityEventCallState.Off ||
            (beam.onMaterialIntroductionClosed != null && beam.onMaterialIntroductionClosed.GetPersistentEventCount() != 0) ||
            (beam.onMaterialIntroductionClosedPhaseMove != null && beam.onMaterialIntroductionClosedPhaseMove.npc != null))
            throw new InvalidOperationException("The introduction has disabled continuation or custom early material callbacks. Review it before migration.");
        if (follow.onDialogueFinished == null || follow.onDialogueFinished.GetPersistentEventCount() != 1 ||
            !(follow.onDialogueFinished.GetPersistentTarget(0) is TutorialSequence guide) || guide.lessonName != "FollowProfessorBhan" ||
            follow.onDialogueFinished.GetPersistentMethodName(0) != "StartTutorial" ||
            follow.onDialogueFinished.GetPersistentListenerState(0) == UnityEngine.Events.UnityEventCallState.Off ||
            follow.onDialogueFinishedPhaseMove == null || follow.onDialogueFinishedPhaseMove.npc != bhan || follow.onDialogueFinishedPhaseMove.phaseId != "City")
            throw new InvalidOperationException("Follow's existing guide/departure callbacks differ. They were not replaced.");
        if (!quiz.assessmentMode || quiz.QuestionCount != 5 || !quiz.TryValidate(out string error))
            throw new InvalidOperationException("Truss assessment must contain five valid questions in assessment mode.");
        BuildLocationLessonTrigger[] triggers = second.targetBuildLocation.GetComponents<BuildLocationLessonTrigger>();
        if (triggers.Length > 1) throw new InvalidOperationException("Duplicate second-site lesson triggers require manual review.");
        if (triggers.Length == 1)
        {
            var data = new SerializedObject(triggers[0]);
            if (data.FindProperty("buildLocation").objectReferenceValue != second.targetBuildLocation ||
                data.FindProperty("lesson").objectReferenceValue != equilibrium ||
                data.FindProperty("onLessonTriggered").FindPropertyRelative("m_PersistentCalls.m_Calls").arraySize != 0 ||
                data.FindProperty("onLessonSequenceFinished").FindPropertyRelative("m_PersistentCalls.m_Calls").arraySize != 0)
                throw new InvalidOperationException("The second-site lesson trigger has custom references/events. It was not overwritten.");
            SerializedProperty lessons = data.FindProperty("lessons");
            if (lessons.arraySize > 1 || (lessons.arraySize == 1 && lessons.GetArrayElementAtIndex(0).objectReferenceValue != equilibrium))
                throw new InvalidOperationException("The second-site lesson trigger contains a custom ordered review. It was not replaced.");
        }
        TrussTutorialBuildAuthoring.Preflight(scene);
    }

    private static void ApplyCanyon(Scene scene)
    {
        NPCProgressionManager bhan = Bhan(scene);
        var phases = Phases(bhan);
        Undo.RecordObject(bhan, "Update Truss tutorial story in existing phases");
        var beam = phases[5];
        beam.phaseDialogue = new Dialogue { name = "Professor Bhan", sentences = Array.Empty<string>() };
        beam.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("Well done, {PlayerName}.",
                "You've completed your first bridge using a simple " + Em("Beam Bridge") + ".",
                "You also learned that the bridge itself has weight, while the cart introduces a " + Em("Live Load") + ".",
                "Now, we're going to make the structure a little more interesting.",
                "This time, you'll learn how structural members can work together.",
                "Before we begin, there's a " + Em("new material") + " you'll need for this project."),
            Say("For this project, you'll have access to " + Em("Wood Beams") + ".",
                "Unlike the roadway, which provides the surface for crossing, these beams can be connected to form structural arrangements that help support the bridge.",
                "You'll use them to construct your first truss structure."),
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.MaterialUnlock,
                material = Load<BridgeMaterialSO>(Data + "Resources/Beam.asset"), materialButtonLabel = "CONTINUE" },
            Say("In your first tutorial, you built a beam bridge.", "Now, you'll be constructing another type of bridge.",
                "It's called a " + Em("Truss Bridge") + ".",
                "A truss bridge uses connected structural members, often arranged in triangular patterns, to carry loads toward its supports.",
                "These triangular arrangements help the structure maintain its shape when forces act on it.",
                "In an ideal truss, the members primarily carry pulling or pushing forces.",
                "You'll learn more about those forces in your later projects.",
                "For now, remember that " + Em("the arrangement and connections of the members help the bridge carry loads safely") + "."),
            Lesson(Load<LessonData>(Data + "Lessons/Truss.asset")),
            Say("There's another concept you need to understand before building this bridge.", "It's called " + Em("Equilibrium") + ".",
                "Equilibrium means that the forces and turning effects acting on a structure are balanced.",
                "For example, when a vehicle crosses a bridge, its weight pushes downward.",
                "The supports provide upward reactions that help balance the load.",
                "The connected truss members also carry forces throughout the structure.",
                "For a bridge to remain in equilibrium as the vehicle crosses, the forces and moments acting on it must balance.",
                "But remember, " + Em("equilibrium alone doesn't mean a bridge is strong enough") + ".",
                "Its structural members must also be capable of safely carrying the forces acting on them."),
            Lesson(Load<LessonData>(Data + "Lessons/Equilibruim.asset"))
        };
        beam.unlockFeatureIdAfterDialogue = string.Empty;
        beam.showFeatureCollectPopup = false;
        phases[6].phaseDialogue = new Dialogue { name = "Professor Bhan", sentences = Array.Empty<string>() };
        var follow = phases[7];
        follow.phaseDialogue = new Dialogue { name = "Professor Bhan", sentences = Array.Empty<string>() };
        follow.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("Before we move on, complete your " + Em("Truss Bridge quiz") + ".",
                "You can review your " + Em("lessons in the Almanac") + " before starting.",
                "Take your time. Once you begin, submit all your answers to finish the assessment."),
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz,
                quiz = Load<QuizData>(Data + "Quized/TrussBridgeQuiz.asset"), quizStepId = QuizStepId },
            Say("From here on, things will be a little different.", "You'll begin working on " + Em("actual construction projects") + ".",
                "And for your first project, I'll be guiding you one last time.", "Come with me.")
        };
        follow.playDialogueOnArrival = true;
        // Old saves can have recorded the previous short 'Follow me' dialogue.
        // They may still take the new assessment while in this same phase; later
        // City+ saves are never rewound. The existing final callback leaves Follow.
        follow.repeatDialogue = true;
        follow.unlockFeatureIdAfterDialogue = string.Empty;
        follow.showFeatureCollectPopup = false;
        EditorUtility.SetDirty(bhan);

        BuildLocation location = phases[6].targetBuildLocation;
        Undo.RecordObject(location, "Keep Tutorial 2 bridge when retrying");
        var site = new SerializedObject(location);
        site.FindProperty("restartGuidedTutorialOnFailure").boolValue = false;
        site.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(location);
        BuildLocationLessonTrigger trigger = location.GetComponent<BuildLocationLessonTrigger>();
        if (trigger == null) trigger = Undo.AddComponent<BuildLocationLessonTrigger>(location.gameObject);
        Undo.RecordObject(trigger, "Review Equilibrium after saving the Truss Bridge");
        var serialized = new SerializedObject(trigger);
        LessonData equilibrium = Load<LessonData>(Data + "Lessons/Equilibruim.asset");
        serialized.FindProperty("buildLocation").objectReferenceValue = location;
        serialized.FindProperty("lesson").objectReferenceValue = equilibrium;
        var list = serialized.FindProperty("lessons");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = equilibrium;
        serialized.FindProperty("listenForSavedBridge").boolValue = true;
        serialized.FindProperty("triggerOnlyOnce").boolValue = true;
        serialized.FindProperty("waitForGameplayPresentation").boolValue = true;
        serialized.ApplyModifiedProperties();
        trigger.enabled = true;
        EditorUtility.SetDirty(trigger);
        EditorUtility.SetDirty(location);
    }

    private static void ValidateCanyon(Scene scene, StringBuilder report)
    {
        Preflight(scene);
        var phases = Phases(Bhan(scene));
        var steps = phases[5].optionalDialogueSequence;
        if (steps.Count != 7 || !steps.Select(step => (int)step.stepType).SequenceEqual(new[] { 0, 0, 1, 0, 3, 0, 3 }) ||
            steps[4].lesson != Load<LessonData>(Data + "Lessons/Truss.asset") || steps[6].lesson != Load<LessonData>(Data + "Lessons/Equilibruim.asset"))
            throw new InvalidOperationException("Wood Beam, Truss and Equilibrium introductions must finish before the second contract.");
        var follow = phases[7];
        if (follow.optionalDialogueSequence.Count != 3 || !follow.optionalDialogueSequence.Select(step => (int)step.stepType).SequenceEqual(new[] { 0, 2, 0 }) ||
            follow.optionalDialogueSequence[1].quiz != Load<QuizData>(Data + "Quized/TrussBridgeQuiz.asset") ||
            !follow.repeatDialogue || !follow.playDialogueOnArrival || !string.IsNullOrEmpty(follow.unlockFeatureIdAfterDialogue) || follow.showFeatureCollectPopup)
            throw new InvalidOperationException("The assessment must precede departure, support legacy Follow saves, and grant no extra reward.");
        BuildLocation location = phases[6].targetBuildLocation;
        var trigger = new SerializedObject(location.GetComponent<BuildLocationLessonTrigger>());
        if (location.RestartGuidedTutorialOnFailure || !trigger.FindProperty("listenForSavedBridge").boolValue ||
            !trigger.FindProperty("waitForGameplayPresentation").boolValue)
            throw new InvalidOperationException("Tutorial 2 must preserve editing on failure and defer the saved-bridge Equilibrium review until gameplay resumes.");
        report.AppendLine("PASS: Wood Beam > Truss lesson > Equilibrium lesson > 400 kg contract > guided truss > save/review > turn-in > five-question assessment > existing City departure.");
        report.AppendLine("PASS: assessment accepts any submitted score, no extra rewards; pre-existing Follow saves can access it, City+ saves are not rewound.");
    }

    private static Scene LoadedCanyon()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Leave Unity in Edit Mode with imports finished and the prefab editor closed.");
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CanyonCrossing; this command never changes the loaded scene setup.");
        return scene;
    }
    private static NPCProgressionManager Bhan(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<NPCProgressionManager>(true)).Single(npc => npc.ProgressionSaveId == "MainContractNPC");
    private static List<NPCProgressionPhase> Phases(NPCProgressionManager npc) => (List<NPCProgressionPhase>)PhasesField.GetValue(npc);
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Missing Tutorial 2 asset: " + path);
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static NPCOptionalDialogueStep Say(params string[] sentences) => new NPCOptionalDialogueStep {
        stepType = NPCOptionalDialogueStep.StepType.Dialogue, dialogue = new Dialogue { name = "Professor Bhan", sentences = sentences } };
    private static NPCOptionalDialogueStep Lesson(LessonData lesson) => new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = lesson };
    private static void WriteReport(string text) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); }
}
#endif
