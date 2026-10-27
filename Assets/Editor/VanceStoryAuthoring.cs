#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scoped Vance dialogue authoring; keeps the existing cargo task, callbacks and saved phase indices.</summary>
[InitializeOnLoad]
public static class VanceStoryAuthoring
{
    public const string RequestPath = "Temp/vance-story-v1.request";
    public const string ReportPath = "Logs/vance-story-v1.txt";
    public const string QuizStepId = "vance-tension-compression-assessment-v1";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private static readonly string[] PhaseIds = { "MeetingVance", "The Supply Route", "The Supply Route Part Two",
        "Cargo and Live-Load Introduction", "Project Briefing", "GoodByeForNow", "SideQuestStart" };
    private static double nextPoll;
    static VanceStoryAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
        try
        {
            if (command == "apply") Apply();
            else if (command == "validate") Validate();
            else if (command == "inspect") Inspect();
            else throw new InvalidOperationException("Unknown Vance story request: " + command);
        }
        catch (Exception error) { Report("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Vance/1. Apply Dialogue, Lesson and Quiz (Do Not Save Scene)")]
    public static void Apply()
    {
        Scene scene = Loaded(); NPCProgressionManager vance = Vance(scene); Preflight(scene, vance);
        if (IsApplied(vance)) { Validate(); return; }
        ContractSO contract = Contract();
        Require(!EditorUtility.IsDirty(contract), "Vance's main contract has unsaved asset edits. Save that asset first; nothing was applied.");
        bool initiallyDirty = scene.isDirty;
        string sidePhase = JsonUtility.ToJson(vance.AuthoredPhases[6]);
        string sideAsset = File.ReadAllText(AssetDatabase.GetAssetPath(SideContract()));
        string protectedContract = ProtectedContract(contract);
        var protectedState = CaptureProtectedState(scene, vance);
        string backup = "Temp/VanceStory/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, backup + "/CanyonCrossing.unity");
        File.Copy(AssetDatabase.GetAssetPath(contract), backup + "/Vance Main Contract.asset");
        File.Copy(AssetDatabase.GetAssetPath(SideContract()), backup + "/Vance Side Realignment.asset");
        File.WriteAllText(backup + "/Vance.loaded-before.json", EditorJsonUtility.ToJson(vance));
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Update Vance Tension and Compression story");
        try
        {
            Undo.RecordObject(vance, "Update Vance dialogue without changing cargo phase indices");
            var phases = vance.AuthoredPhases;
            phases[0].phaseDialogue = Dialogue("You must be the Engineer who opened the new route to the factory.",
                "I'm " + Em("Vance") + ". I coordinate some of the transport projects around Canyon Crossing.",
                "That crossing helped, but there's another part of the route that could work better too.",
                "Come with me. It'll make more sense when you see it.");
            phases[1].phaseDialogue = Dialogue("Take a look at the path through the canyon.");
            phases[1].repeatDialogue = true; // A cancelled camera tour must remain retryable.
            phases[2].phaseDialogue = Dialogue();
            phases[2].optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                Say("Vehicles from the factory can already reach the other side.",
                    "The issue is that the road takes them all the way around the canyon.",
                    "They end up traveling much farther than they need to.",
                    "A bridge here would give them a much more direct route.",
                    "This crossing will connect the factory road to the supply point on the middle rock shelf.",
                    "From there, another crossing is needed to reach the community.",
                    "But before you start, there's something important you should understand about how the different parts of a bridge handle forces."),
                Say("You've already seen how a bridge can bend under weight.",
                    "But bending isn't the only thing happening inside a structure.",
                    "When a vehicle crosses a bridge, some structural members are pulled, while others are pushed together.",
                    "When a member is being pulled or stretched, it experiences " + Em("Tension") + ".",
                    "When a member is being pushed or squeezed, it experiences " + Em("Compression") + ".",
                    "For example, in a truss bridge, some members carry tension while others carry compression.",
                    "Both are important because they help the structure carry the vehicle's weight toward its supports.",
                    "Our test includes the cargo on the truck, so its weight also acts on the bridge.",
                    "Understanding these forces will help you design a bridge that can safely handle the loaded vehicle."),
                LessonStep()
            };
            phases[2].playDialogueOnArrival = true;
            phases[2].repeatDialogue = true;
            // Arrival used to skip this entire conversation. Only finishing all ordered steps may move to cargo.
            phases[2].onNPCArrivedPhaseMove = new NPCPhaseCall();
            phases[3].phaseDialogue = Dialogue("Before I offer you the contract, we need to prepare the test load.",
                "We are not testing this bridge with an empty vehicle.",
                "The supplies are meant for the community, and their weight must be included in the test.",
                "Load all " + Em("four marked cargo items") + " into the truck.",
                "The vehicle and its cargo form the " + Em("combined live load") + " your bridge must carry.",
                "Once all four are loaded, come back to me for the contract.",
                "Your bridge must carry the complete load safely from the factory road to the middle supply point.");
            // Phase3's existing completed-dialogue cargo unlock and loading tutorial are deliberately unchanged.
            phases[5].phaseDialogue = Dialogue();
            phases[5].playDialogueOnArrival = true;
            phases[5].repeatDialogue = true;
            phases[5].optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                LessonStep(),
                Say("You can review " + Em("Tension & Compression") + " in your " + Em("Almanac") + ".",
                    "When you're ready, open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + "."),
                new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = Quiz(), quizStepId = QuizStepId,
                    quizWaitingReminder = "Review Tension & Compression in your Almanac, then open the Quiz tab and press Start Quiz when you're ready." },
                Say("The supplies can reach the middle point now, but the delivery route still needs one more crossing.",
                    Em("Silas") + " handles the projects beyond this point. Meet him near the next ravine to continue the route to the community.",
                    "I'm sure we'll cross paths again sometime, {PlayerName}.")
            };
            // Keep phase5's existing Silas SpawnNPC and SideQuestStart move after the final ordered step.
            EditorUtility.SetDirty(vance);
            Undo.RecordObject(contract, "Update Vance main contract wording and verified span");
            contract.bridgeSpan = 80;
            contract.jobDescription = "Connect the factory road to the middle supply point with an 80-meter bridge. Load all four marked cargo items before testing, and carry the complete vehicle-and-cargo load safely across.";
            contract.offerDialogue = Dialogue("Now that you understand tension and compression, let's put that knowledge to use.",
                "We need a bridge that gives vehicles a shorter route through the canyon.",
                "This bridge will connect the factory road to the supply point on the middle rock shelf.",
                "The crossing is " + Em("80 meters") + " wide.",
                "The four cargo items you've loaded are part of the test's combined vehicle-and-cargo live load.",
                Em("As that weight moves across, different parts of the bridge may experience tension or compression") + ".",
                "Pay attention to how the structural members behave during the test.",
                "Watch how they work together to support the loaded vehicle.", "If you're ready, I can give you the contract.");
            contract.continueOfferDialogue = Dialogue();
            contract.reminderDialogue = Dialogue("Take your time with the design, {PlayerName}.",
                "Keep all four required cargo items on the truck for the test.",
                "Remember what we discussed about tension and compression.",
                "Keep an eye on how the different parts of the bridge respond when the loaded vehicle crosses.");
            contract.finishedContractDialogue = Dialogue("Now that's a shortcut.",
                "The loaded vehicle can reach the middle supply point instead of taking the long way around the canyon.",
                "That should save everyone using this first part of the route a lot of travel time.",
                "And you've seen how tension and compression affect different parts of a bridge.",
                "Well done, {PlayerName}.", "Let's review what you've learned before you continue the delivery route.");
            EditorUtility.SetDirty(contract);
            foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene)) RegisterLesson(tab);
            ValidateScene(scene, vance);
            Require(JsonUtility.ToJson(phases[6]) == sidePhase && File.ReadAllText(AssetDatabase.GetAssetPath(SideContract())) == sideAsset,
                "Vance's side phase or side-contract asset changed. Update rolled back.");
            Require(ProtectedContract(contract) == protectedContract, "Main contract changed outside dialogue/description and confirmed span. Update rolled back.");
            foreach (var pair in protectedState)
                Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
                    "An unrelated NPC, cargo item, slot, truck, tutorial, camera, build site or transform changed. Update rolled back.");
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssetIfDirty(contract); // Save only this initially-clean asset, never unrelated assets/scenes.
            Undo.CollapseUndoOperations(group);
            Report("PASS: existing tour > situation > Tension & Compression lesson > cargo explanation > load four items > existing contract.\n" +
                "PASS: turn-in > lesson review > Almanac assessment > Silas handoff > unchanged realignment side contract.\n" +
                "PASS: all seven saved phase IDs/indexes, phase3 cargo unlock, tutorial callbacks, four cargo items/slots, truck and simulation guards preserved.\n" +
                "PASS: main budget, empty vehicle weight, individual cargo masses, rewards, permissions and side contract preserved.\n" +
                "SPAN_CONFIRMED: metadata corrected from placeholder3m to80m; measured site and reference are80m. Geometry unchanged.\n" +
                "Existing side-contract saves are not rewound for this new quiz. No hints, new callouts or ambient traffic added.\n" +
                "BACKUP: " + backup + "\nSCENE_WAS_DIRTY: " + initiallyDirty + "\nNEEDS_SAVE: CanyonCrossing. No scene was saved.");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Vance/2. Validate Loaded Canyon")]
    public static void Validate()
    {
        Scene scene = Loaded(); ValidateScene(scene, Vance(scene));
        Report("PASS: Vance's lesson, quiz, cargo-loading order and side-contract handoff; confirmed80m main span.\nNEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
    }
    [MenuItem("Tools/Civil Craft/Story/Vance/3. Inspect (Read Only)")]
    public static void Inspect()
    {
        Scene scene = Loaded(); NPCProgressionManager vance = Vance(scene); Preflight(scene, vance);
        Report("PASS: Vance's seven original phases, existing cargo settings and measured80m site verified.\nSCENE_DIRTY: " + scene.isDirty + "\nAlready updated: " + IsApplied(vance));
    }
    private static void Preflight(Scene scene, NPCProgressionManager vance)
    {
        var phases = vance.AuthoredPhases;
        Require(phases.Select(p => p?.phaseId).SequenceEqual(PhaseIds), "Vance's phase order differs. Custom phases were not overwritten.");
        ContractSO contract = Contract();
        Require(contract.ContractID == "VancesContract" && contract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle && contract.allowVehicleCargo &&
            contract.minimumLoadedCargo == 4 && contract.cargoUnlockProgressionId == "VanceNPC" &&
            contract.cargoUnlockAfterDialoguePhaseIndex == 3 && contract.cargoLoadedNPCPhaseId == "Project Briefing",
            "Vance's existing four-cargo loading/unlock settings differ. Nothing was applied.");
        Require(phases[4].contract == contract && phases[4].targetBuildLocation != null && phases[6].contract == SideContract() &&
            phases[6].targetBuildLocation != null && phases[6].teleportToTargetLocation, "Vance's main or realignment side site differs.");
        BuildLocation site = phases[4].targetBuildLocation;
        Require(site.startingAnchors != null && site.startingAnchors.Any() && site.endingAnchors != null && site.endingAnchors.Any(), "Vance's anchor pairs are missing.");
        Vector3 start = site.startingAnchors.OrderByDescending(p => p.transform.position.y).First().transform.position;
        Vector3 end = site.endingAnchors.OrderByDescending(p => p.transform.position.y).First().transform.position;
        Require(Mathf.Abs(Vector3.Distance(start, end) - 80f) < .1f && (contract.bridgeSpan == 3 || contract.bridgeSpan == 80),
            "Vance's authored span differs from the audited80m site. No geometry or custom span was overwritten.");
        Require(phases[2].onDialogueFinishedPhaseMove.npc == vance && phases[2].onDialogueFinishedPhaseMove.phaseId == PhaseIds[3], "Vance's lesson-to-cargo handoff differs.");
        var cargoGuide = phases[3].onDialogueFinished.GetPersistentEventCount() > 0 ? phases[3].onDialogueFinished.GetPersistentTarget(0) as TutorialSequence : null;
        Require(phases[3].onDialogueFinished.GetPersistentEventCount() == 1 && cargoGuide != null &&
            cargoGuide.lessonName == "LoadCargoVance" && phases[3].onDialogueFinished.GetPersistentMethodName(0) == "StartTutorial", "Vance's existing loading tutorial callback differs.");
        Require(phases[4].onNPCArrived.GetPersistentEventCount() == 2 && phases[4].onNPCArrived.GetPersistentTarget(0) == cargoGuide &&
            phases[4].onNPCArrived.GetPersistentMethodName(0) == "Skip" && phases[4].onNPCArrived.GetPersistentTarget(1) is TutorialSequence &&
            phases[4].onNPCArrived.GetPersistentMethodName(1) == "StartTutorial", "Vance's original post-loading guidance differs.");
        Require(phases[5].onDialogueFinishedPhaseMove.npc == vance && phases[5].onDialogueFinishedPhaseMove.phaseId == "SideQuestStart" &&
            phases[5].onDialogueFinished.GetPersistentEventCount() == 1 && phases[5].onDialogueFinished.GetPersistentTarget(0) is NPCSpawnerCondition silas &&
            silas.visibilitySaveId == "SilasNPC" && phases[5].onDialogueFinished.GetPersistentMethodName(0) == "SpawnNPC", "Vance's existing Silas/side handoff differs.");
        Require(All<AlmanacLessonTab>(scene).Any() && All<LessonUIManager>(scene).Any() && Quiz().assessmentMode && Quiz().QuestionCount == 5 && Quiz().TryValidate(out _) &&
            Lesson().Id == "civil-craft-tension-compression-lesson-v1", "Existing lesson/Almanac UI or the new lesson/quiz is missing or invalid.");
        if (!IsApplied(vance))
        {
            Require(phases[2].optionalDialogueSequence.Count == 0 && phases[5].optionalDialogueSequence.Count == 0,
                "Vance already has custom learning steps. They were not overwritten.");
            Require(phases[2].onNPCArrivedPhaseMove.npc == vance && phases[2].onNPCArrivedPhaseMove.phaseId == PhaseIds[3],
                "Vance's original arrival jump differs; custom callbacks were not changed.");
        }
    }
    private static void ValidateScene(Scene scene, NPCProgressionManager vance)
    {
        Preflight(scene, vance); var phases = vance.AuthoredPhases;
        var intro = phases[2].optionalDialogueSequence; var review = phases[5].optionalDialogueSequence;
        Require(intro.Count == 3 && intro[0].stepType == NPCOptionalDialogueStep.StepType.Dialogue && intro[1].stepType == NPCOptionalDialogueStep.StepType.Dialogue &&
            intro[2].stepType == NPCOptionalDialogueStep.StepType.LessonIntroduction && intro[2].lesson == Lesson() &&
            phases[2].playDialogueOnArrival && phases[2].repeatDialogue && phases[2].onNPCArrivedPhaseMove.npc == null,
            "Vance must finish the situation/forces/lesson sequence before cargo is introduced.");
        Require(review.Count == 4 && review[0].stepType == NPCOptionalDialogueStep.StepType.LessonIntroduction && review[0].lesson == Lesson() &&
            review[1].stepType == NPCOptionalDialogueStep.StepType.Dialogue && review[2].stepType == NPCOptionalDialogueStep.StepType.Quiz &&
            review[2].quiz == Quiz() && review[2].quizStepId == QuizStepId && !string.IsNullOrWhiteSpace(review[2].quizWaitingReminder) &&
            review[3].stepType == NPCOptionalDialogueStep.StepType.Dialogue && phases[5].playDialogueOnArrival && phases[5].repeatDialogue,
            "Vance's post-turn-in lesson/assessment/Silas handoff order differs.");
        Require(Contract().bridgeSpan == 80 && Contract().offerDialogue.sentences.Length == 9 && Contract().reminderDialogue.sentences.Length == 4 &&
            Contract().finishedContractDialogue.sentences.Length == 6 && phases[3].phaseDialogue.sentences.Any(line => line.Contains("four marked cargo items")),
            "Vance's confirmed span or cargo-aware dialogue has not been applied.");
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene)) Require(tab.Lessons.Count(value => value == Lesson()) == 1, "Tension & Compression must appear once in the lesson archive.");
    }
    private static Dictionary<UnityEngine.Object, string> CaptureProtectedState(Scene scene, NPCProgressionManager vance)
    {
        var result = new Dictionary<UnityEngine.Object, string>();
        IEnumerable<Component> objects = All<NPCProgressionManager>(scene).Cast<Component>().Concat(All<NPCSpawnerCondition>(scene))
            .Concat(All<CinematicDirector>(scene)).Concat(All<Camera>(scene)).Concat(All<BuildLocation>(scene))
            .Concat(All<TutorialSequence>(scene)).Concat(All<CargoItem>(scene)).Concat(All<VehicleCargoSlot>(scene))
            .Concat(All<LiveLoadVehicle>(scene)).Concat(All<Transform>(scene));
        foreach (Component item in objects) if (item != vance && !result.ContainsKey(item)) result[item] = EditorJsonUtility.ToJson(item);
        return result;
    }
    private static string ProtectedContract(ContractSO contract)
    {
        ContractSO copy = UnityEngine.Object.Instantiate(contract); copy.name = contract.name;
        copy.bridgeSpan = 80; // The one intentional settings correction, proven against the actual anchors.
        copy.jobDescription = string.Empty; copy.offerDialogue = copy.continueOfferDialogue = copy.reminderDialogue = copy.finishedContractDialogue = null;
        string text = EditorJsonUtility.ToJson(copy); UnityEngine.Object.DestroyImmediate(copy); return text;
    }
    private static void RegisterLesson(AlmanacLessonTab tab)
    {
        if (tab.Lessons.Contains(Lesson())) return;
        Undo.RecordObject(tab, "Register Tension and Compression lesson"); var data = new SerializedObject(tab); var list = data.FindProperty("allLessons");
        int index = list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = Lesson();
        data.ApplyModifiedProperties(); PrefabUtility.RecordPrefabInstancePropertyModifications(tab);
    }
    private static bool IsApplied(NPCProgressionManager vance) => vance.AuthoredPhases[5].optionalDialogueSequence.Any(step => step != null && step.quizStepId == QuizStepId);
    private static Scene Loaded()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Stop Play Mode and finish importing in Edit Mode.");
        Scene scene = SceneManager.GetSceneByPath(Canyon); Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing; no scenes are opened or closed by this tool."); return scene;
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static NPCProgressionManager Vance(Scene scene) => All<NPCProgressionManager>(scene).Single(value => value.ProgressionSaveId == "VanceNPC");
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Data + path) ?? throw new InvalidOperationException("Missing Vance asset: " + path);
    private static ContractSO Contract() => Load<ContractSO>("Contract/CanyonCrossing/Vance/Vance's Contract.asset");
    private static ContractSO SideContract() => Load<ContractSO>("Contract/CanyonCrossing/Vance/Vance Side Realignment.asset");
    private static LessonData Lesson() => Load<LessonData>("Lessons/TensionCompression.asset");
    private static QuizData Quiz() => Load<QuizData>("Quized/TensionCompressionQuiz.asset");
    private static Dialogue Dialogue(params string[] lines) => new Dialogue { name = "Vance", sentences = lines };
    private static NPCOptionalDialogueStep Say(params string[] lines) => new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Dialogue, dialogue = Dialogue(lines) };
    private static NPCOptionalDialogueStep LessonStep() => new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = Lesson() };
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Report(string text) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); Debug.Log(text); }
}
#endif
