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

/// <summary>Explicit, undoable Marsh migration. Never saves or switches scenes.</summary>
[InitializeOnLoad]
public static class MarshStoryAuthoring
{
    public const string RequestPath = "Temp/marsh-story-v1.request";
    public const string ReportPath = "Logs/marsh-story-v1.txt";
    public const string ReviewPhaseId = "MarshReview";
    public const string QuizStepId = "marsh-arch-bridge-assessment-v1";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private static readonly string[] OriginalPhaseIds =
        { "MeetingMarsh", "ExplainSituation", "Material Introduction", "ProjectBriefing" };
    private static readonly string[] UpdatedPhaseIds =
        { "MeetingMarsh", "ExplainSituation", "Material Introduction", "ProjectBriefing", ReviewPhaseId, "Resolved" };
    private static readonly FieldInfo PhasesField = typeof(NPCProgressionManager)
        .GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
    private static double nextPoll;

    static MarshStoryAuthoring() { EditorApplication.update += Poll; }

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
            else throw new InvalidOperationException("Unknown Marsh story request: " + command);
        }
        catch (Exception error)
        {
            WriteReport("FAILED\n" + error);
            Debug.LogException(error);
        }
    }

    [MenuItem("Tools/Civil Craft/Story/Marsh/1. Apply to Saved Canyon (Do Not Save)")]
    public static void ApplySavedStory()
    {
        Scene scene = LoadedCanyon();
        if (scene.isDirty)
            throw new InvalidOperationException("Save your current CanyonCrossing edits first. No scene changes were applied or saved.");
        Preflight(scene);
        NPCProgressionManager marsh = Marsh(scene);
        Dictionary<UnityEngine.Object, string> protectedState = CaptureProtectedState(scene, marsh);
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-MarshStory-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, Path.Combine(backup, "CanyonCrossing.unity"));
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Update Marsh Arch Bridge Story");
        try
        {
            ApplyCanyon(scene, marsh);
            StringBuilder report = new StringBuilder("Marsh - Across Rivermere\n");
            ValidateCanyon(scene, report);
            foreach (var entry in protectedState)
                Require(entry.Key != null && EditorJsonUtility.ToJson(entry.Key) == entry.Value,
                    "An unrelated NPC, camera, cinematic, build location or authored bridge transform changed. Migration rolled back.");
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            report.AppendLine("PASS: all four existing Marsh phase IDs/indexes preserved; new review and Resolved phases appended.");
            report.AppendLine("PASS: unrelated NPC progression, cameras/cinematic shots and build-site geometry preserved.");
            report.AppendLine("No new arch-building physics, hints, simulation callouts, side quests or ambient traffic system added.");
            report.AppendLine("Recoverable pre-migration scene: " + backup);
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

    [MenuItem("Tools/Civil Craft/Story/Marsh/2. Validate Loaded Canyon")]
    public static void ValidateLoaded()
    {
        Scene scene = LoadedCanyon();
        StringBuilder report = new StringBuilder("Marsh saved/loaded story validation\n");
        ValidateCanyon(scene, report);
        report.AppendLine("NEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Preflight(Scene scene)
    {
        NPCProgressionManager marsh = Marsh(scene);
        List<NPCProgressionPhase> phases = Phases(marsh);
        string[] ids = phases.Select(phase => phase?.phaseId).ToArray();
        Require(ids.SequenceEqual(OriginalPhaseIds) || ids.SequenceEqual(UpdatedPhaseIds),
            "Marsh's phase IDs/order differ from the audited setup. Custom phases were not overwritten.");
        ContractSO contract = Contract();
        Require(contract.ContractID == "MarshID" && contract.budget == 325000 && contract.liveLoadWeight == 3000 &&
            contract.bridgeSpan == 80 && contract.goldReward == 500 && contract.expReward == 100 && !contract.autoCollectReward,
            "Marsh's existing contract identity/budget/load/span/rewards differ. Review before migrating.");
        Require(phases[0].targetLocation == null && phases[1].targetLocation != null &&
            phases[1].targetLocation == phases[2].targetLocation && phases[2].targetLocation == phases[3].targetLocation &&
            phases.Take(3).All(phase => phase.contract == null) && phases[3].contract == contract &&
            phases[3].targetBuildLocation != null && phases[3].targetBuildLocation.gameObject.scene == scene,
            "Marsh's original spawn, site marker or build-location references differ.");
        Require(phases[0].onDialogueFinishedPhaseMove.npc == marsh &&
            phases[0].onDialogueFinishedPhaseMove.phaseId == "ExplainSituation" &&
            phases[2].onDialogueFinishedPhaseMove.npc == marsh &&
            phases[2].onDialogueFinishedPhaseMove.phaseId == "ProjectBriefing",
            "Marsh's meeting/material continuation links differ.");
        CinematicDirector cinematic = SituationCinematic(phases[1]);
        Require(cinematic != null && cinematic.cinematicID == "MarshCutScene" && !cinematic.playOnStart && !cinematic.playOnlyOnce &&
            cinematic.shots.Count == 2 && cinematic.shots.All(shot => shot.cameraStartPoint != null && shot.cameraEndPoint != null),
            "Marsh's existing situation cinematic is missing or has changed. No camera substitutes were created.");
        Require(cinematic.OnCinematicFinished.GetPersistentEventCount() == 1 &&
            cinematic.OnCinematicFinished.GetPersistentTarget(0) is NPCPhaseAction action && action.npc == marsh &&
            new SerializedObject(action).FindProperty("phaseId").stringValue == "Material Introduction" &&
            cinematic.OnCinematicFinished.GetPersistentMethodName(0) == "MoveToSelectedPhase",
            "Marsh's cinematic must continue to the existing Material Introduction phase.");
        Require(All<AlmanacLessonTab>(scene).Any(), "No existing Almanac lesson archive found.");
        Require(All<LessonUIManager>(scene).Any() && All<ItemUnlockUI>(scene).Any(),
            "The existing lesson/material popup UI must be available.");
        QuizData quiz = Quiz();
        Require(quiz.assessmentMode && quiz.QuestionCount == 5 && quiz.TryValidate(out _),
            "Marsh's Arch Bridge assessment must contain five valid questions and accept any score.");
        Load<LessonData>(Data + "Lessons/ArchBridge.asset");
        Beam(); Road(); Pier();
        NPCProgressionManager ford = Ford(scene);
        NPCSpawnerCondition gate = ford.GetComponent<NPCSpawnerCondition>();
        Require(gate == null || IsFordGate(gate, ford.gameObject, contract),
            "Ford already has custom spawn rules. They were not replaced.");
    }

    private static void ApplyCanyon(Scene scene, NPCProgressionManager marsh)
    {
        Undo.RecordObject(marsh, "Update Marsh phases");
        var phases = Phases(marsh);
        phases[0].phaseDialogue = Dialogue(
            "You must be the engineer who came from Canyon Crossing!",
            "I'm " + Em("Marsh") + ". I help coordinate with some of the community infrastructure projects here in Rivermere.",
            "Professor Bhan mentioned you might be coming this way. It's good to finally meet you.",
            "We have a district that could use a better connection to the city.",
            "Why don't you come along? I'd like to show you the site.");
        phases[0].showExclamationMark = true;
        phases[0].repeatDialogue = true;
        phases[1].phaseDialogue = Dialogue("See that residential district across the waterway?");
        phases[1].showExclamationMark = true;
        phases[1].repeatDialogue = true;

        var introduction = phases[2];
        introduction.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("The residents can reach " + Em("Central Rivermere") + ", but they have to travel through another district first.",
                "It's quite a detour, making the trip longer than it needs to be.",
                "A bridge here would give them a much more convenient route.",
                "There's just one thing we need to consider. " + Em("Boats also use this waterway") + ", so we can't block their passage.",
                "We'll need a design that works for both the vehicles above and the boats below."),
            Say("Back in Canyon Crossing, you worked mostly with wooden materials.",
                "Here in Town River, we'll be using concrete for many of our bridges.",
                "Let's start with the " + Em("Concrete Beam") + ". It's a rigid component that can help support your structure."),
            Material(Beam()),
            Say("You'll also have access to " + Em("Concrete Road") + " to form the roadway where vehicles and residents can cross.",
                "Just remember, the road needs proper structural support beneath it."),
            Material(Road()),
            Say("You'll also have access to the " + Em("Concrete Pier") + ". A pier is a vertical support that helps carry the weight of a bridge and transfer loads down to the ground or foundation.",
                "Depending on your design, a pier can provide an additional support point beneath the bridge. Just remember that its placement and connection to the structure matter."),
            Material(Pier()),
            Say("You've already worked with beam and truss bridges. Now I'd like to introduce you to another type.",
                "It's called an " + Em("Arch Bridge") + ".",
                "An arch uses its curved shape to carry loads mainly through compression, transferring forces toward the supports at both ends.",
                "Those supports, called " + Em("Abutments") + ", must resist the outward push created by the arch.",
                "The curved structure can also help leave space beneath the roadway, which makes it worth considering for this crossing."),
            Lesson()
        };
        introduction.playDialogueOnArrival = true;
        introduction.repeatDialogue = true;
        introduction.showExclamationMark = true;
        introduction.materialIntroductionButtonLabel = "CONTINUE";
        // An interrupted introduction must be replayable on a later tap/visit.
        introduction.introduceMaterialOnlyOnce = false;
        phases[3].showExclamationMark = true;

        if (phases.Count == OriginalPhaseIds.Length)
        {
            phases.Add(new NPCProgressionPhase { phaseId = ReviewPhaseId, targetLocation = phases[3].targetLocation });
            phases.Add(new NPCProgressionPhase { phaseId = "Resolved", targetLocation = phases[3].targetLocation });
        }
        NPCProgressionPhase review = phases[4];
        review.phaseDialogue = Dialogue();
        review.dialoguePrompt = "Talk to Marsh";
        review.playDialogueOnArrival = true;
        review.repeatDialogue = true;
        review.showExclamationMark = true;
        review.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Lesson(),
            Say("You can review the " + Em("Arch Bridge lesson in your Almanac") + " before starting your quiz.",
                "When you're ready, open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + "."),
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz,
                quiz = Quiz(), quizStepId = QuizStepId,
                quizWaitingReminder = "Take your time reviewing the Arch Bridge lesson in the Almanac. Open the Quiz tab and press Start Quiz when you're ready." }
        };
        review.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = marsh, phaseId = "Resolved" };
        NPCProgressionPhase resolved = phases[5];
        resolved.phaseDialogue = Dialogue("The residents won't have to take that long detour anymore. Thanks for your help, {PlayerName}.");
        resolved.dialoguePrompt = "Talk to Marsh";
        resolved.playDialogueOnArrival = false;
        resolved.repeatDialogue = true;
        resolved.showExclamationMark = false;
        foreach (NPCProgressionPhase phase in phases)
        {
            phase.unlockFeatureIdAfterDialogue = string.Empty;
            phase.featureUnlockDisplayName = string.Empty;
            phase.showFeatureCollectPopup = false;
        }
        EditorUtility.SetDirty(marsh);

        MarshPostContractContinuation continuation = marsh.GetComponent<MarshPostContractContinuation>() ??
            Undo.AddComponent<MarshPostContractContinuation>(marsh.gameObject);
        Undo.RecordObject(continuation, "Configure Marsh legacy continuation");
        SerializedObject continuationData = new SerializedObject(continuation);
        continuationData.FindProperty("npc").objectReferenceValue = marsh;
        continuationData.FindProperty("contract").objectReferenceValue = Contract();
        continuationData.FindProperty("reviewPhaseId").stringValue = ReviewPhaseId;
        continuationData.ApplyModifiedProperties();

        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene))
        {
            SerializedObject serialized = new SerializedObject(tab);
            SerializedProperty list = serialized.FindProperty("allLessons");
            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == ArchLesson()) present = true;
            if (present) continue;
            Undo.RecordObject(tab, "Register Arch Bridge lesson");
            int index = list.arraySize++;
            list.GetArrayElementAtIndex(index).objectReferenceValue = ArchLesson();
            serialized.ApplyModifiedProperties();
        }

        NPCProgressionManager ford = Ford(scene);
        if (ford.GetComponent<NPCSpawnerCondition>() == null)
        {
            NPCSpawnerCondition gate = Undo.AddComponent<NPCSpawnerCondition>(ford.gameObject);
            gate.useMultipleConditions = true;
            gate.matchMode = NPCSpawnerCondition.MatchMode.All;
            gate.conditions = new List<NPCSpawnerCondition.Condition>
            {
                new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.ContractCompleted, contract = Contract() },
                new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.NPCAtPhase, id = "MarshNPC", phaseId = "Resolved" }
            };
            gate.hideWhenMatched = false;
            gate.waitForManualSpawn = false;
            gate.persistManualOverride = false;
            gate.updateAutomatically = true;
            gate.npcRoot = ford.gameObject;
            EditorUtility.SetDirty(gate);
        }
    }

    private static void ValidateCanyon(Scene scene, StringBuilder report)
    {
        Preflight(scene);
        NPCProgressionManager marsh = Marsh(scene);
        var phases = Phases(marsh);
        Require(phases.Select(phase => phase.phaseId).SequenceEqual(UpdatedPhaseIds), "Marsh review/idle phases have not been appended.");
        Require(phases[0].phaseDialogue.sentences.Length == 5 && phases[1].phaseDialogue.sentences.Length == 1,
            "Marsh meeting/situation dialogue does not match the updated reference.");
        Require(phases.All(phase => string.IsNullOrEmpty(phase.unlockFeatureIdAfterDialogue) && !phase.showFeatureCollectPopup),
            "Marsh must not grant unintended default feature rewards.");
        var sequence = phases[2].optionalDialogueSequence;
        Require(sequence.Select(step => (int)step.stepType).SequenceEqual(new[] { 0, 0, 1, 0, 1, 0, 1, 0, 3 }) &&
            sequence[2].material == Beam() && sequence[4].material == Road() && sequence[6].material == Pier() &&
            sequence[8].lesson == ArchLesson(), "Marsh's supported material/lesson order differs.");
        Require(phases[2].playDialogueOnArrival && phases[3].contract == Contract(),
            "The learning introduction must finish before the existing contract offer.");
        var review = phases[4];
        Require(review.contract == null && review.targetLocation == phases[3].targetLocation && review.playDialogueOnArrival &&
            review.optionalDialogueSequence.Select(step => (int)step.stepType).SequenceEqual(new[] { 3, 0, 2 }) &&
            review.optionalDialogueSequence[0].lesson == ArchLesson() && review.optionalDialogueSequence[2].quiz == Quiz() &&
            review.optionalDialogueSequence[2].quizStepId == QuizStepId &&
            review.onDialogueFinishedPhaseMove.npc == marsh && review.onDialogueFinishedPhaseMove.phaseId == "Resolved",
            "Post-turn-in review/assessment must finish before resolving Marsh.");
        Require(phases[5].contract == null && !phases[5].showExclamationMark &&
            !phases[5].playDialogueOnArrival && phases[5].repeatDialogue,
            "Resolved must be a terminal, repeatable idle phase without another reward.");
        ContractSO contract = Contract();
        Require(contract.contractMap == ContractSO.ContractMap.TownRiver &&
            contract.allowedMaterials.Any(item => item.material == Pier()) && !contract.hiddenMaterials.Contains(Pier()) &&
            contract.offerDialogue.sentences.Length == 5 && contract.reminderDialogue.sentences.Length == 1 &&
            contract.finishedContractDialogue.sentences.Length == 4,
            "Marsh contract text, Town River classification or supported Pier allowance differs.");
        MarshPostContractContinuation continuation = marsh.GetComponent<MarshPostContractContinuation>();
        Require(continuation != null, "Missing legacy turned-in save continuation.");
        SerializedObject saved = new SerializedObject(continuation);
        Require(saved.FindProperty("npc").objectReferenceValue == marsh &&
            saved.FindProperty("contract").objectReferenceValue == contract &&
            saved.FindProperty("reviewPhaseId").stringValue == ReviewPhaseId, "Legacy continuation has stale references.");
        NPCProgressionManager ford = Ford(scene);
        Require(IsFordGate(ford.GetComponent<NPCSpawnerCondition>(), ford.gameObject, contract),
            "Ford must wait for Marsh's completed contract and terminal review/quiz phase.");
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene))
        {
            SerializedProperty list = new SerializedObject(tab).FindProperty("allLessons");
            int matches = 0;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == ArchLesson()) matches++;
            Require(matches == 1, "Arch Bridge must be registered exactly once in each existing Almanac lesson list.");
        }
        report.AppendLine("PASS: meeting > existing walk/cinematic > Concrete Beam/Road/Pier introductions > Arch Bridge lesson > contract.");
        report.AppendLine("PASS: existing Collect > Arch Bridge review > five-question assessment (any score/no extra rewards) > Resolved/Ford visibility.");
        report.AppendLine("PASS: contract MarshID, budget 325000, load 3000, span 80, 500 coins/100 XP unchanged.");
    }

    private static bool IsFordGate(NPCSpawnerCondition gate, GameObject ford, ContractSO contract) =>
        gate != null && gate.enabled && gate.npcRoot == ford && gate.useMultipleConditions &&
        gate.matchMode == NPCSpawnerCondition.MatchMode.All && !gate.hideWhenMatched && !gate.waitForManualSpawn &&
        !gate.persistManualOverride && !gate.useDespawnConditions && gate.updateAutomatically &&
        gate.conditions != null && gate.conditions.Count == 2 && gate.conditions.All(rule => rule != null && rule.mustBeTrue) &&
        gate.conditions.Any(rule => rule.kind == NPCSpawnerCondition.ConditionKind.ContractCompleted && rule.contract == contract) &&
        gate.conditions.Any(rule => rule.kind == NPCSpawnerCondition.ConditionKind.NPCAtPhase && rule.id == "MarshNPC" && rule.phaseId == "Resolved");

    private static CinematicDirector SituationCinematic(NPCProgressionPhase phase)
    {
        Require(phase.onDialogueFinished != null && phase.onDialogueFinished.GetPersistentEventCount() == 1 &&
            phase.onDialogueFinished.GetPersistentMethodName(0) == "PlayCinematic" &&
            phase.onDialogueFinished.GetPersistentListenerState(0) != UnityEngine.Events.UnityEventCallState.Off,
            "Marsh's existing situation cinematic event differs.");
        return phase.onDialogueFinished.GetPersistentTarget(0) as CinematicDirector;
    }

    private static Dictionary<UnityEngine.Object, string> CaptureProtectedState(Scene scene, NPCProgressionManager marsh)
    {
        var result = new Dictionary<UnityEngine.Object, string>();
        foreach (NPCProgressionManager npc in All<NPCProgressionManager>(scene))
            if (npc != marsh) result.Add(npc, EditorJsonUtility.ToJson(npc));
        foreach (CinematicDirector director in All<CinematicDirector>(scene)) result.Add(director, EditorJsonUtility.ToJson(director));
        foreach (Camera camera in All<Camera>(scene)) result.Add(camera, EditorJsonUtility.ToJson(camera));
        foreach (BuildLocation location in All<BuildLocation>(scene)) result.Add(location, EditorJsonUtility.ToJson(location));
        Transform site = Phases(marsh)[3].targetBuildLocation.transform;
        while (site.parent != null && site.name != "MarshMainContract") site = site.parent;
        Require(site.name == "MarshMainContract", "Cannot locate Marsh's audited authored build hierarchy.");
        foreach (Transform point in site.GetComponentsInChildren<Transform>(true)) result.Add(point, EditorJsonUtility.ToJson(point));
        return result;
    }

    private static Scene LoadedCanyon()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Leave Unity in Edit Mode with imports finished and the prefab editor closed.");
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing. This tool never changes the loaded scene setup.");
        return scene;
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static NPCProgressionManager Marsh(Scene scene) => All<NPCProgressionManager>(scene)
        .Single(npc => npc.ProgressionSaveId == "MarshNPC");
    private static NPCProgressionManager Ford(Scene scene) => All<NPCProgressionManager>(scene)
        .Single(npc => npc.ProgressionSaveId == "FordNPC");
    private static List<NPCProgressionPhase> Phases(NPCProgressionManager npc) => (List<NPCProgressionPhase>)PhasesField.GetValue(npc);
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Missing Marsh story asset: " + path);
    private static ContractSO Contract() => Load<ContractSO>(Data + "Contract/TownRiver/Marsh Contract.asset");
    private static BridgeMaterialSO Beam() => Load<BridgeMaterialSO>(Data + "Resources/ConcreteBeam.asset");
    private static BridgeMaterialSO Road() => Load<BridgeMaterialSO>(Data + "Resources/Concrete Road.asset");
    private static BridgeMaterialSO Pier() => Load<BridgeMaterialSO>(Data + "Resources/Concrete Pier.asset");
    private static LessonData ArchLesson() => Load<LessonData>(Data + "Lessons/ArchBridge.asset");
    private static QuizData Quiz() => Load<QuizData>(Data + "Quized/ArchBridgeQuiz.asset");
    private static Dialogue Dialogue(params string[] sentences) => new Dialogue { name = "Marsh", sentences = sentences };
    private static NPCOptionalDialogueStep Say(params string[] sentences) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.Dialogue, dialogue = Dialogue(sentences) };
    private static NPCOptionalDialogueStep Material(BridgeMaterialSO material) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.MaterialUnlock, material = material, materialButtonLabel = "CONTINUE" };
    private static NPCOptionalDialogueStep Lesson() => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = ArchLesson() };
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void WriteReport(string text) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); }
}
#endif
