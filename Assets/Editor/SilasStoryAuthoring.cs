#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Updates Silas's two main contracts while keeping saved phases and the existing multiplayer reward.</summary>
[InitializeOnLoad]
public static class SilasStoryAuthoring
{
    public const string RequestPath = "Temp/silas-story-v1.request";
    public const string ReportPath = "Logs/silas-story-v1.txt";
    public const string DistributionQuizStepId = "silas-load-distribution-assessment-v1";
    public const string TransferQuizStepId = "silas-load-transfer-assessment-v1";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private static readonly string[] PhaseIds = { "Meeting Silas", "The Final Supply Gap ", "AfterCinematic", "Project Briefing",
        "SilasLoadTransfer", "ExplainSituation2", "ExplainSituationContinuation", "MATERIAL INTRODUCTION", "ProjectBriefing2", "ProjectBriefing3", "Multiplayer" };
    private static double nextPoll;
    static SilasStoryAuthoring() { EditorApplication.update += Poll; }
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
            else throw new InvalidOperationException("Unknown Silas story request: " + command);
        }
        catch (Exception error) { Report("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Silas/1. Apply Both Main Stories (Do Not Save Scene)")]
    public static void Apply()
    {
        Scene scene = Loaded(); NPCProgressionManager silas = Silas(scene); Preflight(scene, silas);
        if (IsApplied(silas)) { Validate(); return; }
        ContractSO distribution = DistributionContract(), transfer = TransferContract();
        Require(!EditorUtility.IsDirty(distribution) && !EditorUtility.IsDirty(transfer),
            "A Silas contract has unsaved asset edits. Save those assets first; nothing was applied.");
        var phases = silas.AuthoredPhases;
        BuildLocation firstSite = phases[3].targetBuildLocation, secondSite = phases[8].targetBuildLocation;
        string firstSiteBefore = WithoutSimulationLesson(firstSite), secondSiteBefore = WithoutSimulationLesson(secondSite);
        string firstContractBefore = ProtectedContract(distribution), secondContractBefore = ProtectedContract(transfer);
        string multiplayerBefore = PhaseWithoutDialogue(phases[10]);
        var protectedState = CaptureProtectedState(scene, silas, firstSite, secondSite);
        bool initiallyDirty = scene.isDirty;
        string backup = "Temp/SilasStory/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup); File.Copy(Canyon, backup + "/CanyonCrossing.unity");
        File.Copy(AssetDatabase.GetAssetPath(distribution), backup + "/SilasContract.asset");
        File.Copy(AssetDatabase.GetAssetPath(transfer), backup + "/MainContractSilas.asset");
        File.WriteAllText(backup + "/Silas.loaded-before.json", EditorJsonUtility.ToJson(silas));
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Update Silas main stories and lessons");
        try
        {
            Undo.RecordObject(silas, "Update Silas dialogue without changing saved phase indices");
            phases[0].phaseDialogue = Dialogue("So, you're the Engineer who's been working with Reed and Vance.",
                "I'm " + Em("Silas") + ". I handle some of the more demanding projects farther into Canyon Crossing.",
                "I've seen what your crossings have done for the route.",
                "The factory is connected, and Vance's supplies can reach the middle point now.",
                "But that route still doesn't reach the community across the next gap.", "There's another site ahead. Come with me.");
            phases[1].phaseDialogue = Dialogue("Take a look across the canyon.");
            phases[1].repeatDialogue = true;
            phases[2].phaseDialogue = Dialogue();
            phases[2].optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                Say("There's a community on the other side of the canyon.",
                    "They can already reach Central Canyonville, but the current route takes them a long way around.",
                    "The crossings you've built have brought Vance's supplies to the middle point.",
                    "A bridge here would finish the direct delivery connection to the community. We'll test the route with a jeep before supplies continue across.",
                    "This crossing is wider than the previous ones, so you'll need to think carefully about how the bridge supports the weight of passing vehicles.",
                    "Before we begin, there's another structural concept you should understand."),
                Say("You've already learned how bridges bend under weight and how their members experience tension and compression.",
                    "Now, let's look at how that weight is shared throughout the structure.",
                    "When a vehicle crosses a bridge, its weight doesn't affect only the section directly beneath it.",
                    "The load is carried by different structural members and distributed toward the bridge's supports.",
                    "This is called " + Em("Load Distribution") + ".",
                    "Depending on the bridge's design, some members and supports may carry more load than others.",
                    "Adding or positioning supports can change how the load is distributed throughout the structure.",
                    "That's why it's important to consider the entire bridge, not just the section where the vehicle is located."),
                LessonStep(DistributionLesson())
            };
            phases[2].playDialogueOnArrival = true; phases[2].repeatDialogue = true;
            phases[4].phaseDialogue = Dialogue();
            phases[4].optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                LessonStep(DistributionLesson()), QuizIntroduction("Load Distribution"),
                QuizStep(DistributionQuiz(), DistributionQuizStepId, "Load Distribution"),
                Say("You've done more than build a few separate crossings.",
                    "The factory is connected, the route through the canyon is shorter, and the community has access to it now.",
                    "But Canyon Crossing doesn't end there.", "There's one more gap ahead.",
                    "Beyond it is the road leading toward " + Em("Town River") + ".", "Come with me.")
            };
            phases[4].playDialogueOnArrival = true; phases[4].repeatDialogue = true;
            phases[5].phaseDialogue = Dialogue("Take a look ahead."); phases[5].repeatDialogue = true;
            phases[6].phaseDialogue = Dialogue("That road leads out of " + Em("Canyon Crossing") + ".",
                Em("Town River") + " is beyond it.",
                "There's already another way there, but this would give the region a much more direct connection.",
                "You'll notice " + Em("there are old wooden towers standing on both sides of the crossing") + ".",
                "They were built for an earlier attempt to connect this passage, but the crossing itself was never completed.",
                "They're still standing, and you may be able to make use of them in your design.",
                "Finish this crossing, and the route you've built will finally lead beyond the canyon.");
            phases[6].repeatDialogue = true;
            phases[7].phaseDialogue = Dialogue();
            phases[7].optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                Say("Before we begin, there's another material available for this project.", "You'll now have access to " + Em("Rope") + "."),
                new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.MaterialUnlock, material = Rope(), materialButtonLabel = "CONTINUE" },
                Say("You can also make use of the " + Em("existing wooden towers at the crossing") + ".",
                    "They can serve as support points for your design.", "These give you another way to support parts of the bridge across the gap.",
                    "Use the available materials and support points carefully.", "But before you start building, there's another important concept you need to understand."),
                Say("You've already learned how a bridge distributes weight among its structural members and supports.",
                    "Now, let's look at how that weight travels through the structure.", "This is called " + Em("Load Transfer") + ".",
                    "When a vehicle crosses a bridge, its weight must be carried through the connected structural components until it reaches the supports and eventually the ground.",
                    "For example, in a suspension bridge, the deck carries the vehicle's weight to the hangers.",
                    "The hangers transfer the load to the main cables, which carry tension.",
                    "The cables pass over the towers and are secured at the anchor points.",
                    "The towers carry compressive forces toward the ground, while the anchor points resist the cable's pull.",
                    "This is why the connections between different parts of a bridge are important.",
                    Em("Every component has a role in carrying and transferring the load safely") + "."),
                LessonStep(TransferLesson())
            };
            phases[7].playDialogueOnArrival = true; phases[7].repeatDialogue = true; phases[7].introduceMaterialOnlyOnce = false;
            phases[9].phaseDialogue = Dialogue();
            phases[9].optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                LessonStep(TransferLesson()), QuizIntroduction("Load Transfer"), QuizStep(TransferQuiz(), TransferQuizStepId, "Load Transfer"),
                Say("Your route through Canyon Crossing is complete, {PlayerName}. You can head toward " + Em("Town River") + " when you're ready.",
                    "Before you go, there's another way to put those building skills to use: building alongside a friend.")
            };
            phases[9].playDialogueOnArrival = true; phases[9].repeatDialogue = true;
            // Retain this phase's original feature, icon, Collect popup and callbacks exactly.
            phases[10].phaseDialogue = Dialogue("The community is connected and the eastern passage is open, {PlayerName}. You've learned how a bridge carries its loads.",
                "Now you can put those skills to use alongside a friend. I'm unlocking " + Em("Multiplayer") + " for you!",
                "In your world, open Multiplayer and choose Turn On Multiplayer to create a private room.",
                "Share the room code with a friend. They can choose Multiplayer in Mode Selection and enter your code after unlocking it too.",
                "Explore together and take on bridge-building challenges. I'll be cheering you both on!");
            EditorUtility.SetDirty(silas);

            Undo.RecordObjects(new UnityEngine.Object[] { distribution, transfer }, "Update both main Silas contracts");
            distribution.bridgeSpan = 100; transfer.bridgeSpan = 100;
            distribution.jobDescription = "Finish the direct route from Vance's middle supply point to the community. Build a safe 100-meter crossing and observe how its members and supports distribute the test vehicle's load.";
            distribution.offerDialogue = Dialogue("Now that you understand load distribution, let's put that knowledge to use.",
                "We need a bridge that connects the community to the main route.", "The crossing is " + Em("100 meters") + " wide.",
                "A jeep will test this route before Vance's supplies continue to the community.",
                "There's a rock formation partway across, but it sits much lower than the roads on either side.",
                Em("You'll have to decide how to use the available support points across the site") + ".",
                "Remember, the way you arrange the bridge and its supports affects how the vehicle's weight is distributed.",
                Em("Watch how the whole structure responds when the jeep crosses") + ".", "If you're ready, take the contract.");
            distribution.continueOfferDialogue = Dialogue();
            distribution.reminderDialogue = Dialogue("Don't rush the design, {PlayerName}.", "Remember what we discussed about load distribution.",
                "Think carefully about where you place the supports, and keep an eye on how the whole bridge responds during the test.");
            distribution.finishedContractDialogue = Dialogue("The community has a direct connection to the route now. Vance's supplies can continue from the middle point.",
                "They won't have to take the long way around anymore.", "That was a wider crossing than your earlier projects.",
                "You also got to see how the bridge's design and support arrangement affect the way it carries weight.",
                "You handled it well, {PlayerName}.", "I've got a feeling this won't be the last project we handle together.");
            transfer.jobDescription = "Open the eastern passage toward Town River with a safe 100-meter crossing. Use the available rope, towers and anchor points as your design requires, and keep a continuous load path to the supports.";
            transfer.offerDialogue = Dialogue("Now that you understand load transfer, let's put that knowledge to use.",
                "This crossing is different from the ones you've handled before.", "The gap is " + Em("100 meters") + " wide.",
                "A jeep will use this route during the test.", "The site already has wooden towers, and you now have rope available for your design.",
                Em("The towers, rope, and anchor points give you another way to support parts of the bridge across the canyon") + ".",
                "You don't have to build the same way you did on your earlier projects.", "Remember what we discussed about load transfer.",
                Em("Pay attention to how the vehicle's weight travels through the connected parts of your bridge and toward its supports") + ".",
                "Think carefully about how you connect the different components.", "If you're ready, take the contract.");
            transfer.continueOfferDialogue = Dialogue();
            transfer.reminderDialogue = Dialogue("You've got the towers, anchor points, and rope available at the site.",
                Em("Think about how you can use them to support the structure across the gap") + ".",
                "Remember, the load needs a continuous path through the structure and toward its supports.", "How you connect everything is up to you.");
            transfer.finishedContractDialogue = Dialogue("That's it.", "The eastern passage is open.",
                "You started by connecting Central Canyonville to the factory.",
                "Then you improved the route through the canyon and brought it all the way to the community.",
                "This last crossing asked you to use the site differently from the ones before it.",
                "You also got to see how the different parts of a bridge work together to carry and transfer weight.",
                Em("Remember those towers and how they helped support a crossing over a wider gap") + ".",
                "You may encounter structures like that again.", "Now the route leads toward " + Em("Town River") + ".",
                "Head there when you're ready.", "Good work, {PlayerName}.", "If our paths cross again, I may still have something for you.");
            EditorUtility.SetDirty(distribution); EditorUtility.SetDirty(transfer);
            Undo.RecordObjects(new UnityEngine.Object[] { firstSite, secondSite }, "Assign both Silas simulation lessons");
            firstSite.simulationLesson = DistributionSimulation(); secondSite.simulationLesson = TransferSimulation();
            foreach (BuildLocation site in new[] { firstSite, secondSite })
            { PrefabUtility.RecordPrefabInstancePropertyModifications(site); EditorUtility.SetDirty(site); }
            foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene)) { RegisterLesson(tab, DistributionLesson()); RegisterLesson(tab, TransferLesson()); }
            ValidateScene(scene, silas);
            Require(PhaseWithoutDialogue(phases[10]) == multiplayerBefore, "Multiplayer feature/Collect configuration changed. Update rolled back.");
            Require(ProtectedContract(distribution) == firstContractBefore && ProtectedContract(transfer) == secondContractBefore,
                "A main contract changed outside wording and the confirmed span. Update rolled back.");
            Require(WithoutSimulationLesson(firstSite) == firstSiteBefore && WithoutSimulationLesson(secondSite) == secondSiteBefore,
                "A Silas site changed outside its simulation lesson. Update rolled back.");
            foreach (var pair in protectedState) Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
                "An unrelated NPC, tutorial, vehicle, cargo, cinematic, camera, site or authored transform changed. Update rolled back.");
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssetIfDirty(distribution); AssetDatabase.SaveAssetIfDirty(transfer);
            Undo.CollapseUndoOperations(group);
            Report("PASS: both Silas contracts remain main story contracts; all eleven phase IDs/indexes preserved.\n" +
                "PASS: Load Distribution introduction/review/quiz precedes the second main project; Rope introduction precedes Load Transfer lesson/contract.\n" +
                "PASS: Load Transfer review/quiz precedes the existing finale cinematic and multiplayer dialogue/Collect reward.\n" +
                "PASS: matching simulation lessons assigned only to the two existing Silas sites; both confirmed100m spans recorded.\n" +
                "PASS: weights, budgets, rewards, permissions, region/feature callbacks, material identity, other NPCs and geometry preserved.\n" +
                "No optional third contract, new region gate, network calls, extra quiz rewards or unsupported hints/callouts added.\n" +
                "BACKUP: " + backup + "\nSCENE_WAS_DIRTY: " + initiallyDirty + "\nNEEDS_SAVE: CanyonCrossing. No scene was saved.");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Silas/2. Validate Loaded Canyon")]
    public static void Validate()
    {
        Scene scene = Loaded(); ValidateScene(scene, Silas(scene));
        Report("PASS: both Silas main stories, lessons, quizzes, simulation lessons and original multiplayer reward are wired.\nNEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
    }
    [MenuItem("Tools/Civil Craft/Story/Silas/3. Inspect (Read Only)")]
    public static void Inspect()
    {
        Scene scene = Loaded(); NPCProgressionManager silas = Silas(scene); Preflight(scene, silas);
        Report("PASS: Silas's eleven stable phases, two main contracts, measured100m sites, Rope identity and multiplayer reward verified.\nSCENE_DIRTY: " + scene.isDirty + "\nAlready updated: " + IsApplied(silas));
    }
    private static void Preflight(Scene scene, NPCProgressionManager silas)
    {
        var phases = silas.AuthoredPhases;
        Require(phases.Select(p => p?.phaseId).SequenceEqual(PhaseIds), "Silas's phase IDs/order differ. Custom phases were not overwritten.");
        Require(phases[3].contract == DistributionContract() && phases[8].contract == TransferContract() &&
            phases[3].targetBuildLocation != null && phases[8].targetBuildLocation != null && phases[3].targetBuildLocation != phases[8].targetBuildLocation,
            "Silas's two existing main contract/site assignments differ.");
        foreach (ContractSO contract in new[] { DistributionContract(), TransferContract() })
            Require(contract.isStoryModeContract && contract.countsTowardMapAchievements && contract.contractMap == ContractSO.ContractMap.CanyonCrossing &&
                (contract.bridgeSpan == 30 || contract.bridgeSpan == 100), "Both existing Silas contracts must retain their main-story/map classification and audited span.");
        Require(DistributionContract().ContractID == "SilasMainContract" && TransferContract().ContractID == "MainContractSilas", "Silas contract save identities differ.");
        foreach (BuildLocation site in new[] { phases[3].targetBuildLocation, phases[8].targetBuildLocation })
        {
            Require(site.startingAnchors != null && site.startingAnchors.Any() && site.endingAnchors != null && site.endingAnchors.Any(), "Silas site anchor pairs are missing.");
            // Index zero is each site's authored road endpoint. The other
            // anchors belong to the lower rock or elevated wooden towers.
            // Choosing the highest anchor would measure a support-to-road diagonal.
            Require(site.startingAnchors[0] != null && site.endingAnchors[0] != null, "Silas's primary road anchors are missing.");
            Vector3 start = site.startingAnchors[0].transform.position;
            Vector3 end = site.endingAnchors[0].transform.position;
            Require(Mathf.Abs(Vector3.Distance(start, end) - 100f) < .1f,
                "Silas site '" + site.name + "' span is " + Vector3.Distance(start, end).ToString("0.###") +
                "m: " + start.ToString("F3") + " -> " + end.ToString("F3") + ". Expected100m. No geometry was overwritten.");
        }
        foreach (int index in new[] { 0, 2, 4, 6, 7 })
            Require(phases[index].onDialogueFinishedPhaseMove.npc == silas && phases[index].onDialogueFinishedPhaseMove.phaseId == PhaseIds[index + 1],
                "Silas's original dialogue handoff differs at phase " + PhaseIds[index]);
        Require(phases[4].targetLocation != null && phases[4].teleportToTargetLocation, "Silas's original second-project meeting pose differs.");
        Require(CinematicRoute(phases[1].onDialogueFinished, silas, PhaseIds[2]) && CinematicRoute(phases[5].onDialogueFinished, silas, PhaseIds[6]) &&
            CinematicRoute(phases[9].onDialogueFinished, silas, PhaseIds[10]), "A Silas cinematic or its original next-phase callback differs.");
        NPCProgressionPhase multiplayer = phases[10];
        Require(multiplayer.contract == null && multiplayer.unlockFeatureIdAfterDialogue == "multiplayer" && multiplayer.featureUnlockDisplayName == "Multiplayer" &&
            multiplayer.featureUnlockIcon != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(multiplayer.featureUnlockIcon)) == "366072043fdddde49a26ca9e066e7602" &&
            multiplayer.showFeatureCollectPopup, "Silas's original multiplayer feature/icon/Collect reward differs.");
        Require(Rope() != null && DistributionLesson().Id == "civil-craft-load-distribution-lesson-v1" && TransferLesson().Id == "civil-craft-load-transfer-lesson-v1" &&
            DistributionQuiz().assessmentMode && TransferQuiz().assessmentMode && DistributionQuiz().QuestionCount == 5 && TransferQuiz().QuestionCount == 5 &&
            DistributionQuiz().TryValidate(out _) && TransferQuiz().TryValidate(out _) && DistributionSimulation() != null && TransferSimulation() != null &&
            All<AlmanacLessonTab>(scene).Any() && All<LessonUIManager>(scene).Any(), "Silas learning assets or existing reader UI are missing/invalid.");
        if (!IsApplied(silas))
        {
            Require(phases[2].optionalDialogueSequence.Count == 0 && phases[4].optionalDialogueSequence.Count == 0 && phases[9].optionalDialogueSequence.Count == 0,
                "Silas already has custom learning sequences. They were not overwritten.");
            var material = phases[7].optionalDialogueSequence;
            Require(material.Count == 2 && material[0].stepType == NPCOptionalDialogueStep.StepType.MaterialUnlock && material[0].material == Rope() &&
                material[1].stepType == NPCOptionalDialogueStep.StepType.Dialogue, "Silas's original Rope introduction differs.");
            Require((phases[3].targetBuildLocation.simulationLesson == null || phases[3].targetBuildLocation.simulationLesson == DistributionSimulation()) &&
                (phases[8].targetBuildLocation.simulationLesson == null || phases[8].targetBuildLocation.simulationLesson == TransferSimulation()),
                "A Silas site has a custom simulation lesson. It was not overwritten.");
        }
    }
    private static void ValidateScene(Scene scene, NPCProgressionManager silas)
    {
        Preflight(scene, silas); var phases = silas.AuthoredPhases;
        Require(StepTypes(phases[2], 0, 0, 3) && phases[2].optionalDialogueSequence[2].lesson == DistributionLesson() && phases[2].playDialogueOnArrival && phases[2].repeatDialogue,
            "Load Distribution must finish before the first main briefing.");
        Require(StepTypes(phases[4], 3, 0, 2, 0) && phases[4].optionalDialogueSequence[0].lesson == DistributionLesson() &&
            IsQuiz(phases[4].optionalDialogueSequence[2], DistributionQuiz(), DistributionQuizStepId) && phases[4].playDialogueOnArrival && phases[4].repeatDialogue,
            "The first review/assessment must finish before the second main introduction.");
        Require(StepTypes(phases[7], 0, 1, 0, 0, 3) && phases[7].optionalDialogueSequence[1].material == Rope() &&
            phases[7].optionalDialogueSequence[1].materialButtonLabel == "CONTINUE" && phases[7].optionalDialogueSequence[4].lesson == TransferLesson() &&
            phases[7].playDialogueOnArrival && phases[7].repeatDialogue && !phases[7].introduceMaterialOnlyOnce,
            "Rope dialogue/unlock and the Load Transfer lesson must finish before the second main briefing.");
        Require(StepTypes(phases[9], 3, 0, 2, 0) && phases[9].optionalDialogueSequence[0].lesson == TransferLesson() &&
            IsQuiz(phases[9].optionalDialogueSequence[2], TransferQuiz(), TransferQuizStepId) && phases[9].playDialogueOnArrival && phases[9].repeatDialogue,
            "The second review/assessment must finish before the finale and multiplayer introduction.");
        Require(DistributionContract().bridgeSpan == 100 && TransferContract().bridgeSpan == 100 &&
            DistributionContract().offerDialogue.sentences.Length == 9 && DistributionContract().reminderDialogue.sentences.Length == 3 && DistributionContract().finishedContractDialogue.sentences.Length == 6 &&
            TransferContract().offerDialogue.sentences.Length == 11 && TransferContract().continueOfferDialogue.sentences.Length == 0 &&
            TransferContract().reminderDialogue.sentences.Length == 4 && TransferContract().finishedContractDialogue.sentences.Length == 12,
            "Silas's confirmed spans or main contract dialogue have not been applied.");
        Require(phases[3].targetBuildLocation.simulationLesson == DistributionSimulation() && phases[8].targetBuildLocation.simulationLesson == TransferSimulation(),
            "Both main sites need their matching simulation lessons.");
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene))
            Require(tab.Lessons.Count(value => value == DistributionLesson()) == 1 && tab.Lessons.Count(value => value == TransferLesson()) == 1,
                "Both Silas lessons must appear once in the Almanac archive.");
    }
    private static bool CinematicRoute(UnityEvent callback, NPCProgressionManager npc, string phaseId)
    {
        if (callback == null || callback.GetPersistentEventCount() != 1 || !(callback.GetPersistentTarget(0) is CinematicDirector movie) ||
            callback.GetPersistentMethodName(0) != "PlayCinematic" || callback.GetPersistentListenerState(0) == UnityEventCallState.Off ||
            movie.OnCinematicFinished == null || movie.OnCinematicFinished.GetPersistentEventCount() != 1 ||
            !(movie.OnCinematicFinished.GetPersistentTarget(0) is NPCPhaseAction action) || movie.OnCinematicFinished.GetPersistentMethodName(0) != "MoveToSelectedPhase" ||
            movie.OnCinematicFinished.GetPersistentListenerState(0) == UnityEventCallState.Off || action.npc != npc) return false;
        return new SerializedObject(action).FindProperty("phaseId").stringValue == phaseId;
    }
    private static bool StepTypes(NPCProgressionPhase phase, params int[] expected) => phase.optionalDialogueSequence != null &&
        phase.optionalDialogueSequence.Select(step => step == null ? -1 : (int)step.stepType).SequenceEqual(expected);
    private static bool IsQuiz(NPCOptionalDialogueStep step, QuizData quiz, string id) => step.quiz == quiz && step.quizStepId == id && !string.IsNullOrWhiteSpace(step.quizWaitingReminder);
    private static Dictionary<UnityEngine.Object, string> CaptureProtectedState(Scene scene, NPCProgressionManager silas, BuildLocation first, BuildLocation second)
    {
        var result = new Dictionary<UnityEngine.Object, string>();
        IEnumerable<Component> items = All<NPCProgressionManager>(scene).Cast<Component>().Concat(All<NPCSpawnerCondition>(scene))
            .Concat(All<CinematicDirector>(scene)).Concat(All<Camera>(scene)).Concat(All<BuildLocation>(scene)).Concat(All<TutorialSequence>(scene))
            .Concat(All<CargoItem>(scene)).Concat(All<VehicleCargoSlot>(scene)).Concat(All<LiveLoadVehicle>(scene)).Concat(All<Transform>(scene));
        foreach (Component item in items) if (item != silas && item != first && item != second && !result.ContainsKey(item)) result[item] = EditorJsonUtility.ToJson(item);
        return result;
    }
    private static string PhaseWithoutDialogue(NPCProgressionPhase phase) => JsonUtility.ToJson(phase).Replace("\"phaseDialogue\":" + JsonUtility.ToJson(phase.phaseDialogue), "\"phaseDialogue\":null");
    private static string ProtectedContract(ContractSO contract)
    {
        ContractSO copy = UnityEngine.Object.Instantiate(contract); copy.name = contract.name; copy.bridgeSpan = 100;
        copy.jobDescription = string.Empty; copy.offerDialogue = copy.continueOfferDialogue = copy.reminderDialogue = copy.finishedContractDialogue = null;
        string json = EditorJsonUtility.ToJson(copy); UnityEngine.Object.DestroyImmediate(copy); return json;
    }
    private static string WithoutSimulationLesson(BuildLocation site) => System.Text.RegularExpressions.Regex.Replace(EditorJsonUtility.ToJson(site),
        "\"simulationLesson\"\\s*:\\s*\\{[^}]*\\}", "\"simulationLesson\":null");
    private static void RegisterLesson(AlmanacLessonTab tab, LessonData lesson)
    {
        if (tab.Lessons.Contains(lesson)) return;
        Undo.RecordObject(tab, "Register Silas lesson in Almanac"); var data = new SerializedObject(tab); var list = data.FindProperty("allLessons");
        int index = list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = lesson;
        data.ApplyModifiedProperties(); PrefabUtility.RecordPrefabInstancePropertyModifications(tab);
    }
    private static bool IsApplied(NPCProgressionManager silas) => silas.AuthoredPhases[4].optionalDialogueSequence.Any(step => step != null && step.quizStepId == DistributionQuizStepId);
    private static Scene Loaded()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && PrefabStageUtility.GetCurrentPrefabStage() == null,
            "Stop Play Mode and finish importing in Edit Mode.");
        Scene scene = SceneManager.GetSceneByPath(Canyon); Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing; this tool never opens or closes scenes."); return scene;
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static NPCProgressionManager Silas(Scene scene) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == "SilasNPC");
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Data + path) ?? throw new InvalidOperationException("Missing Silas asset: " + path);
    private static ContractSO DistributionContract() => Load<ContractSO>("Contract/CanyonCrossing/Silas/SilasContract.asset");
    private static ContractSO TransferContract() => Load<ContractSO>("Contract/CanyonCrossing/Silas/MainContractSilas.asset");
    private static LessonData DistributionLesson() => Load<LessonData>("Lessons/LoadDistribution.asset");
    private static LessonData TransferLesson() => Load<LessonData>("Lessons/LoadTransfer.asset");
    private static QuizData DistributionQuiz() => Load<QuizData>("Quized/LoadDistributionQuiz.asset");
    private static QuizData TransferQuiz() => Load<QuizData>("Quized/LoadTransferQuiz.asset");
    private static SimulationLessonDefinition DistributionSimulation() => Load<SimulationLessonDefinition>("Simulation Lessons/Load Distribution Lesson.asset");
    private static SimulationLessonDefinition TransferSimulation() => Load<SimulationLessonDefinition>("Simulation Lessons/Load Transfer Lesson.asset");
    private static BridgeMaterialSO Rope() => AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(AssetDatabase.GUIDToAssetPath("4524a3fc876c6b640a735e6808fe1271"));
    private static Dialogue Dialogue(params string[] lines) => new Dialogue { name = "Silas", sentences = lines };
    private static NPCOptionalDialogueStep Say(params string[] lines) => new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Dialogue, dialogue = Dialogue(lines) };
    private static NPCOptionalDialogueStep LessonStep(LessonData lesson) => new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = lesson };
    private static NPCOptionalDialogueStep QuizIntroduction(string subject) => Say("You can review " + Em(subject) + " in your " + Em("Almanac") + ".",
        "When you're ready, open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + ".");
    private static NPCOptionalDialogueStep QuizStep(QuizData quiz, string id, string subject) => new NPCOptionalDialogueStep {
        stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = quiz, quizStepId = id,
        quizWaitingReminder = "Review " + subject + " in your Almanac, then open the Quiz tab and press Start Quiz when you're ready." };
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Report(string text) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); Debug.Log(text); }
}
#endif
