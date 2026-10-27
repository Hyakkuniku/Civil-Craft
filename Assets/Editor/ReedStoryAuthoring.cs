#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Updates only the authored ReedNPC scene instance; never changes its shared prefab or saves scenes.</summary>
[InitializeOnLoad]
public static class ReedStoryAuthoring
{
    public const string RequestPath = "Temp/reed-story-v1.request";
    public const string ReportPath = "Logs/reed-story-v1.txt";
    public const string QuizStepId = "reed-bending-assessment-v1";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private static readonly string[] PhaseIds = { "MeetingReed", "ExplainSituation", "Reed Contract Give", "ReedFareWell", "SideContract" };
    private static double nextPoll;
    static ReedStoryAuthoring() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        string command = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        try
        {
            if (command == "apply") Apply();
            else if (command == "validate") Validate();
            else if (command == "inspect") Inspect();
            else throw new InvalidOperationException("Unknown Reed command: " + command);
        }
        catch (Exception error) { Report("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Reed/1. Apply Dialogue, Lesson and Quiz (Do Not Save Scene)")]
    public static void Apply()
    {
        Scene scene = Loaded();
        NPCProgressionManager reed = Reed(scene);
        Preflight(scene, reed);
        if (reed.GetComponent<ReedStoryIntroduction>() != null) { Validate(); return; }
        ContractSO contract = MainContract();
        Require(!EditorUtility.IsDirty(contract), "Reed's main contract has unsaved asset edits. Save that asset first; nothing was applied.");
        bool initiallyDirty = scene.isDirty;
        string sidePhase = JsonUtility.ToJson(reed.AuthoredPhases[4]);
        string sideFile = File.ReadAllText(AssetDatabase.GetAssetPath(SideContract()));
        string mainProtected = ProtectedContract(contract);
        var protectedObjects = new Dictionary<UnityEngine.Object, string>();
        foreach (Component item in All<NPCProgressionManager>(scene).Cast<Component>()
            .Concat(All<NPCSpawnerCondition>(scene)).Concat(All<CinematicDirector>(scene))
            .Concat(All<Camera>(scene)).Concat(All<BuildLocation>(scene)))
            if (item != reed) protectedObjects[item] = EditorJsonUtility.ToJson(item);
        string backup = "Temp/ReedStory/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, backup + "/CanyonCrossing.unity");
        File.Copy(AssetDatabase.GetAssetPath(contract), backup + "/Reed Main Contract.asset");
        File.Copy(AssetDatabase.GetAssetPath(SideContract()), backup + "/Reed Side Inspection.asset");
        File.WriteAllText(backup + "/Reed.loaded-before.json", EditorJsonUtility.ToJson(reed));
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Update Reed Bending dialogue and quiz");
        try
        {
            Undo.RecordObject(reed, "Update Reed dialogue without reordering phases");
            var phases = reed.AuthoredPhases;
            phases[0].phaseDialogue = Say("You must be the Engineer that Professor Bhan mentioned.",
                "I'm " + Em("Reed") + ".", "I handle some of the local projects around Canyon Crossing.",
                "I heard you finished your first project back in " + Em("Central Canyonville") + ".",
                "Good timing. There's a crossing nearby that could use your help.", "Come with me. I'll show you.");
            var helper = Undo.AddComponent<ReedStoryIntroduction>(reed.gameObject);
            ConfigureHelper(scene, reed, helper);
            phases[1].phaseDialogue = Say("See that factory across the canyon?");
            phases[1].repeatDialogue = true; // Interrupted cinematics/readers must remain retryable.
            phases[1].onDialogueFinished = new UnityEvent();
            UnityEventTools.AddPersistentListener(phases[1].onDialogueFinished, helper.BeginIntroduction);
            phases[1].onDialogueFinishedPhaseMove = new NPCPhaseCall();
            phases[2].playDialogueOnArrival = true;
            phases[3].phaseDialogue = Say();
            phases[3].playDialogueOnArrival = true;
            phases[3].repeatDialogue = true;
            phases[3].optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = Lesson() },
                new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Dialogue,
                    dialogue = Say("You've seen how " + Em("bending") + " affects a bridge under load.",
                        "You can review the Bending lesson in your " + Em("Almanac") + ".",
                        "When you're ready, open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + ".") },
                new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = Quiz(), quizStepId = QuizStepId,
                    quizWaitingReminder = "Review Bending in your Almanac, then open the Quiz tab and press Start Quiz when you're ready." }
            };
            // The existing Vance SpawnNPC event and SideContract phase move stay on phase3.
            // Ordered steps invoke them only after the submitted quiz's Continue button.
            PrefabUtility.RecordPrefabInstancePropertyModifications(reed);
            EditorUtility.SetDirty(reed);
            Undo.RecordObject(contract, "Update Reed main contract wording only");
            contract.jobDescription = "Connect the factory to Central Canyonville. Build a safe crossing for the test pickup and observe how the structure responds to bending under its weight.";
            contract.offerDialogue = Say("Now that you understand bending, let's put that knowledge to use.",
                "We need a bridge that can connect the factory to Central Canyonville.",
                "The crossing is " + Em("60 meters") + " wide.",
                "A pickup truck will use this route during the test.",
                "Your job is to construct a bridge that can safely carry the pickup across.",
                "Remember what we discussed about bending.",
                Em("Pay attention to how the bridge responds under the vehicle's weight") + ".",
                Em("Watch the structure closely when you run the test") + ".", "If you're ready, I've got the contract.");
            contract.continueOfferDialogue = Say();
            contract.reminderDialogue = Say("The crossing's yours, {PlayerName}.",
                "Remember what we discussed about bending.", "Keep an eye on how the bridge responds when you run the test.");
            contract.finishedContractDialogue = Say("That's exactly what the crossing needed.",
                "The factory finally has a direct connection to Central Canyonville.",
                "Getting supplies into the city should be much easier now.", "You handled your first project out here well.",
                "And now you've seen how bending affects a bridge under load.", "Nice work, {PlayerName}.",
                "I'm sure we'll meet again some other time.");
            EditorUtility.SetDirty(contract);
            foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene)) RegisterLesson(tab);
            ValidateScene(scene, reed);
            Require(JsonUtility.ToJson(phases[4]) == sidePhase && File.ReadAllText(AssetDatabase.GetAssetPath(SideContract())) == sideFile,
                "Reed's side contract or side phase changed. Update rolled back.");
            Require(ProtectedContract(contract) == mainProtected, "Main contract settings changed outside its wording. Update rolled back.");
            foreach (var pair in protectedObjects)
                Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value, "An unrelated NPC, gate, cinematic, camera or build site changed. Update rolled back.");
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssetIfDirty(contract); // This one initially-clean asset only; never SaveAssets.
            Undo.CollapseUndoOperations(group);
            Report("PASS: Reed introduction > existing cinematic > situation > Bending dialogue/lesson > existing main contract.\n" +
                "PASS: turn-in > Bending review > Almanac assessment > existing Vance unlock and unchanged side contract.\n" +
                "PASS: all five phase IDs/indexes, side phase, inspection tutorial, geometry, cameras and other NPCs preserved.\n" +
                "PASS: original main budget, live-load weight, rewards and material/tool permissions preserved; span matches the confirmed 60m site.\n" +
                "Existing side-contract saves are not rewound for this new quiz.\n" +
                "SPAN_CONFIRMED: main contract and briefing use 60m, matching the authored site. No geometry or live-load weight changed.\n" +
                "BACKUP: " + backup + "\nSCENE_WAS_DIRTY: " + initiallyDirty + "\nNEEDS_SAVE: CanyonCrossing. No scene was saved.");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Reed/2. Validate Loaded Canyon")]
    public static void Validate()
    {
        Scene scene = Loaded();
        ValidateScene(scene, Reed(scene));
        Report("PASS: Reed authored dialogue, cinematic/lesson ordering, five-question assessment and side-contract continuation.\nNEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
    }

    [MenuItem("Tools/Civil Craft/Story/Reed/3. Inspect (Read Only)")]
    public static void Inspect()
    {
        Scene scene = Loaded(); var reed = Reed(scene); Preflight(scene, reed);
        Report("PASS: Reed scene instance and original five phase IDs verified.\nSCENE_DIRTY: " + scene.isDirty +
            "\nAlready updated: " + (reed.GetComponent<ReedStoryIntroduction>() != null));
    }

    private static void Preflight(Scene scene, NPCProgressionManager reed)
    {
        Require(reed.AuthoredPhases.Select(p => p?.phaseId).SequenceEqual(PhaseIds), "Reed's phase order differs; custom phases were not overwritten.");
        Require(reed.AuthoredPhases[2].contract == MainContract() && reed.AuthoredPhases[4].contract == SideContract() &&
            reed.AuthoredPhases[2].targetBuildLocation != null && reed.AuthoredPhases[4].targetBuildLocation != null,
            "Reed's existing main/side contract or build-site assignment differs.");
        Require(MainContract().bridgeSpan == 60, "Reed's main contract span must match the confirmed 60m site.");
        Require(reed.AuthoredPhases[3].onDialogueFinishedPhaseMove.npc == reed &&
            reed.AuthoredPhases[3].onDialogueFinishedPhaseMove.phaseId == "SideContract", "Reed's post-main side-contract handoff differs.");
        Require(reed.AuthoredPhases[3].onDialogueFinished.GetPersistentEventCount() == 1 &&
            reed.AuthoredPhases[3].onDialogueFinished.GetPersistentTarget(0) is NPCSpawnerCondition vance &&
            vance.visibilitySaveId == "VanceNPC" && reed.AuthoredPhases[3].onDialogueFinished.GetPersistentMethodName(0) == "SpawnNPC",
            "Reed's existing Vance unlock differs.");
        Require(Quiz().assessmentMode && Quiz().QuestionCount == 5 && Quiz().TryValidate(out _) && Lesson().Id == "civil-craft-bending-lesson-v1",
            "Bending lesson/quiz assets are missing or invalid.");
        Require(All<AlmanacLessonTab>(scene).Any() && All<LessonUIManager>(scene).Any(), "Existing Almanac/lesson UI is missing.");
        var movie = Movie(scene);
        Require(movie.shots != null && movie.shots.Count > 0 && movie.cinematicCamera != null &&
            movie.OnCinematicFinished.GetPersistentEventCount() == 0, "Reed's existing cinematic has changed; no callbacks were overwritten.");
        if (reed.GetComponent<ReedStoryIntroduction>() == null)
        {
            UnityEvent finish = reed.AuthoredPhases[1].onDialogueFinished;
            Require(finish != null && finish.GetPersistentEventCount() == 2 && finish.GetPersistentTarget(0) == movie &&
                finish.GetPersistentMethodName(0) == "PlayCinematic" && finish.GetPersistentTarget(1) is NPCPhaseAction &&
                finish.GetPersistentMethodName(1) == "MoveToSelectedPhase", "Reed's situation callbacks differ; custom events were not replaced.");
            Require(reed.AuthoredPhases[3].optionalDialogueSequence == null || reed.AuthoredPhases[3].optionalDialogueSequence.Count == 0,
                "Reed's farewell already has a custom ordered flow; it was not overwritten.");
        }
    }

    private static void ConfigureHelper(Scene scene, NPCProgressionManager reed, ReedStoryIntroduction helper)
    {
        var data = new SerializedObject(helper);
        data.FindProperty("npc").objectReferenceValue = reed;
        data.FindProperty("contractGiver").objectReferenceValue = reed.GetComponent<NPCContractGiver>();
        data.FindProperty("cinematic").objectReferenceValue = Movie(scene);
        data.FindProperty("dialogueManager").objectReferenceValue = All<DialogueManager>(scene).Single();
        data.FindProperty("npcAnimator").objectReferenceValue = reed.GetComponentInChildren<Animator>(true);
        data.FindProperty("bendingLesson").objectReferenceValue = Lesson();
        SetDialogue(data.FindProperty("situationDialogue"), Say("That's one of the supply facilities serving Canyon Crossing.",
            "A lot of the materials used around here come through that facility.", "The problem is getting them into Central Canyonville.",
            "The factory isn't actually far from the city, but the canyon separates the two roads.",
            "It makes supplying Central Canyonville harder than it should be.", "A direct crossing here would solve that.",
            "But before we start, there's something important you should understand about how bridges behave under weight."));
        SetDialogue(data.FindProperty("bendingDialogue"), Say("When a vehicle crosses a bridge, its weight pushes down on the structure.",
            "That weight can cause parts of the bridge to bend.", "This is called " + Em("Bending") + ".",
            "For a bridge supported at both ends, bending is often greatest near the middle when the load is positioned there.",
            "In that downward bend, the upper part of the beam experiences compression, while the lower part experiences tension.",
            "Understanding this helps you identify where the bridge might need more structural support."));
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(helper);
    }

    private static void ValidateScene(Scene scene, NPCProgressionManager reed)
    {
        Preflight(scene, reed);
        var helper = reed.GetComponent<ReedStoryIntroduction>();
        Require(helper != null, "Reed introduction helper is not applied.");
        var data = new SerializedObject(helper);
        Require(data.FindProperty("npc").objectReferenceValue == reed && data.FindProperty("contractGiver").objectReferenceValue == reed.GetComponent<NPCContractGiver>() &&
            data.FindProperty("cinematic").objectReferenceValue == Movie(scene) && data.FindProperty("bendingLesson").objectReferenceValue == Lesson() &&
            data.FindProperty("dialogueManager").objectReferenceValue != null && data.FindProperty("npcAnimator").objectReferenceValue != null,
            "Reed helper has missing/stale references.");
        var phase = reed.AuthoredPhases[1];
        Require(phase.repeatDialogue && phase.onDialogueFinished.GetPersistentEventCount() == 1 &&
            phase.onDialogueFinished.GetPersistentTarget(0) == helper && phase.onDialogueFinished.GetPersistentMethodName(0) == "BeginIntroduction" &&
            phase.onDialogueFinishedPhaseMove.npc == null, "Reed introduction must wait for its cinematic and lesson, and allow cancellation retry.");
        Require(reed.AuthoredPhases[2].playDialogueOnArrival, "Reed's normal contract briefing must follow the lesson.");
        var steps = reed.AuthoredPhases[3].optionalDialogueSequence;
        Require(steps.Count == 3 && steps[0].stepType == NPCOptionalDialogueStep.StepType.LessonIntroduction && steps[0].lesson == Lesson() &&
            steps[1].stepType == NPCOptionalDialogueStep.StepType.Dialogue && steps[2].stepType == NPCOptionalDialogueStep.StepType.Quiz &&
            steps[2].quiz == Quiz() && steps[2].quizStepId == QuizStepId && !string.IsNullOrWhiteSpace(steps[2].quizWaitingReminder),
            "Reed post-turn-in review/assessment order differs.");
        foreach (var tab in All<AlmanacLessonTab>(scene)) Require(tab.Lessons.Count(lesson => lesson == Lesson()) == 1, "Bending must appear once in the Almanac archive.");
        Require(MainContract().offerDialogue.sentences.Length == 9 &&
            MainContract().offerDialogue.sentences.Contains("The crossing is " + Em("60 meters") + " wide.") &&
            MainContract().reminderDialogue.sentences.Length == 3 &&
            MainContract().finishedContractDialogue.sentences.Length == 7, "Reed's main contract wording has not been updated.");
    }

    private static void RegisterLesson(AlmanacLessonTab tab)
    {
        if (tab.Lessons.Contains(Lesson())) return;
        Undo.RecordObject(tab, "Register Bending lesson in Almanac");
        var data = new SerializedObject(tab); var list = data.FindProperty("allLessons");
        int index = list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = Lesson();
        data.ApplyModifiedProperties(); PrefabUtility.RecordPrefabInstancePropertyModifications(tab);
    }
    private static string ProtectedContract(ContractSO contract)
    {
        var copy = UnityEngine.Object.Instantiate(contract);
        copy.name = contract.name; copy.jobDescription = string.Empty;
        copy.offerDialogue = copy.continueOfferDialogue = copy.reminderDialogue = copy.finishedContractDialogue = null;
        string result = EditorJsonUtility.ToJson(copy); UnityEngine.Object.DestroyImmediate(copy); return result;
    }
    private static void SetDialogue(SerializedProperty property, Dialogue value)
    {
        property.FindPropertyRelative("name").stringValue = value.name;
        var list = property.FindPropertyRelative("sentences"); list.arraySize = value.sentences.Length;
        for (int i = 0; i < list.arraySize; i++) list.GetArrayElementAtIndex(i).stringValue = value.sentences[i];
    }
    private static Scene Loaded()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Stop Play Mode and finish importing in Edit Mode.");
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing; this tool never opens or closes scenes."); return scene;
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static NPCProgressionManager Reed(Scene scene) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == "ReedNPC");
    private static CinematicDirector Movie(Scene scene) => All<CinematicDirector>(scene).Single(movie => movie.cinematicID == "ReedCutscene");
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Data + path) ?? throw new InvalidOperationException("Missing Reed asset: " + path);
    private static ContractSO MainContract() => Load<ContractSO>("Contract/CanyonCrossing/Reed/Reed's Contract.asset");
    private static ContractSO SideContract() => Load<ContractSO>("Contract/CanyonCrossing/Reed/Reed Side Inspection.asset");
    private static LessonData Lesson() => Load<LessonData>("Lessons/Bending.asset");
    private static QuizData Quiz() => Load<QuizData>("Quized/BendingQuiz.asset");
    private static Dialogue Say(params string[] lines) => new Dialogue { name = "Reed", sentences = lines };
    private static string Em(string value) => "<b><#E09500>" + value + "</color></b>";
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Report(string value) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, value); Debug.Log(value); }
}
#endif
