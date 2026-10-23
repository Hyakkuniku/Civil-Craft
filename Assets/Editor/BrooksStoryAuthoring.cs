#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Explicit, undoable Brooks story authoring. Never saves or switches scenes.</summary>
[InitializeOnLoad]
public static class BrooksStoryAuthoring
{
    public const string RequestPath = "Temp/brooks-story-v1.request";
    public const string ReportPath = "Logs/brooks-story-v1.txt";
    public const string ReviewPhaseId = "BrooksReview";
    public const string QuizStepId = "brooks-moving-load-assessment-v1";
    public const string StoryRootName = "Brooks Story";
    public const string CinematicId = "BrooksMarketConnection-v1";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private const string FollowLessonId = "Story/Brooks/FollowToSite-v1";
    private static readonly string[] PhaseIds =
        { "MeetingBrooks", "ExplainSituation", "LessonIntroduction", "ProjectBriefing", ReviewPhaseId, "Resolved" };
    private static readonly FieldInfo PhasesField = typeof(NPCProgressionManager)
        .GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
    private static double nextPoll;

    static BrooksStoryAuthoring() { EditorApplication.update += Poll; }

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
            else if (command == "inspect") InspectLoaded();
            else throw new InvalidOperationException("Unknown Brooks story request: " + command);
        }
        catch (Exception error) { WriteReport("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Brooks/1. Apply to Saved Canyon (Do Not Save)")]
    public static void ApplySavedStory()
    {
        Scene scene = LoadedCanyon();
        Require(!scene.isDirty, "Save your current CanyonCrossing edits first. No scene changes were applied or saved.");
        Preflight(scene);
        NPCProgressionManager brooks = NPC(scene, "BrooksNpc");
        if (Phases(brooks).Count != 0)
        {
            ValidateLoaded();
            return; // Repeated applies are read-only; never regenerate authored camera points or custom edits.
        }
        BuildLocation location = Location(scene);
        Vector3 start = Upper(location.startingAnchors).position;
        Vector3 end = Upper(location.endingAnchors).position;
        Vector3 direction = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 sitePosition = SafeSitePosition(brooks, location, start - direction * 7f + side * 4f);
        var protectedState = CaptureProtectedState(scene, brooks, location);
        string workbenchState = WorkbenchState(location);
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-BrooksStory-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, Path.Combine(backup, "CanyonCrossing.unity"));
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Author Brooks Moving Load Story");
        try
        {
            StringBuilder report = new StringBuilder("Brooks - Market Connection\n");
            ApplyCanyon(scene, brooks, location, sitePosition, start, end, direction, side, report);
            ValidateCanyon(scene, report);
            foreach (var entry in protectedState)
                Require(entry.Key != null && EditorJsonUtility.ToJson(entry.Key) == entry.Value,
                    "An unrelated NPC, existing gate, camera, cinematic, vehicle or authored transform changed. Migration rolled back.");
            Require(WorkbenchState(location) == workbenchState,
                "The Brooks workbench changed outside its active-contract assignment. Migration rolled back.");
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            report.AppendLine("PASS: Marsh and Ford progression/gates, all existing cameras, vehicle routes and bridge geometry preserved.");
            report.AppendLine("Budget/load remain editable placeholders chosen by the author; no balancing values were invented.");
            report.AppendLine("No dual-vehicle sequencing, hints, simulation callouts, automatic traffic, side quests or new structural physics added.");
            report.AppendLine("Recoverable pre-migration scene: " + backup);
            report.AppendLine("NEEDS_SAVE: CanyonCrossing. Review, then save this scene in Unity.");
            WriteReport(report.ToString());
            Debug.Log(report.ToString());
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Brooks/2. Validate Loaded Canyon")]
    public static void ValidateLoaded()
    {
        Scene scene = LoadedCanyon();
        StringBuilder report = new StringBuilder("Brooks saved/loaded story validation\n");
        ValidateCanyon(scene, report);
        report.AppendLine("NEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    [MenuItem("Tools/Civil Craft/Story/Brooks/3. Inspect Existing Site (Read Only)")]
    public static void InspectLoaded()
    {
        Scene scene = LoadedCanyon();
        Preflight(scene);
        NPCProgressionManager brooks = NPC(scene, "BrooksNpc");
        BuildLocation location = Location(scene);
        Vector3 start = Upper(location.startingAnchors).position, end = Upper(location.endingAnchors).position;
        Vector3 direction = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
        Vector3 desired = start - direction * 7f + Vector3.Cross(Vector3.up, direction) * 4f;
        var report = new StringBuilder("Brooks site inspection (read only)\n");
        report.AppendLine("Upper anchors: " + start + " -> " + end + "; span " + Vector3.Distance(start, end));
        Vector3 sitePosition = SafeSitePosition(brooks, location, desired);
        report.AppendLine("Safe NPC marker: " + sitePosition);
        NavMeshAgent agent = brooks.GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        Transform authoredSpawn = new SerializedObject(brooks).FindProperty("authoredSpawnLocation").objectReferenceValue as Transform;
        Vector3 spawnPosition = authoredSpawn != null ? authoredSpawn.position : brooks.transform.position;
        if (!NavMesh.SamplePosition(spawnPosition, out NavMeshHit spawnHit, 3f, filter) ||
            Vector3.ProjectOnPlane(spawnHit.position - spawnPosition, Vector3.up).magnitude > 3f ||
            Mathf.Abs(spawnHit.position.y - spawnPosition.y) > 2f)
        {
            report.AppendLine("ROUTE_UNAVAILABLE: authored Brooks spawn has no valid NavMesh sample within 3 m horizontally/2 m vertically. No bake, teleport or route substitution performed.");
        }
        else
        {
            NavMeshPath route = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(spawnHit.position, sitePosition, filter, route);
            float routeLength = 0;
            for (int i = 1; i < route.corners.Length; i++) routeLength += Vector3.Distance(route.corners[i - 1], route.corners[i]);
            report.AppendLine("Brooks authored-spawn route: sample " + spawnHit.position + "; calculated " + calculated +
                "; status " + route.status + "; corners " + route.corners.Length + "; length " + routeLength.ToString("0.00") + " m.");
            if (!calculated || route.status != NavMeshPathStatus.PathComplete)
                report.AppendLine("ROUTE_REVIEW_REQUIRED: the current NavMesh does not prove a complete Brooks walk. No auto-bake, wider sampling or teleport was attempted.");
        }
        report.AppendLine("The existing single delivery vehicle is preserved; dual taxi/cargo tests and an obsolete crossing/barriers are not implemented.");
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Preflight(Scene scene)
    {
        NPCProgressionManager brooks = NPC(scene, "BrooksNpc");
        var phases = Phases(brooks);
        Require(phases != null && (phases.Count == 0 || phases.Select(p => p?.phaseId).SequenceEqual(PhaseIds)),
            "Brooks has custom phase IDs/order. They were not overwritten.");
        ContractSO contract = Contract();
        Require(contract.ContractID == "BrooksMainContract" && contract.contractMap == ContractSO.ContractMap.TownRiver &&
            contract.budget > 0 && !float.IsNaN(contract.budget) && !float.IsInfinity(contract.budget) &&
            contract.liveLoadWeight > 0 && !float.IsNaN(contract.liveLoadWeight) && !float.IsInfinity(contract.liveLoadWeight) &&
            Mathf.Approximately(contract.bridgeSpan, 58.09374f) && contract.goldReward == 500 && contract.expReward == 100 &&
            !contract.autoCollectReward && contract.hideFromLeaderboard && !contract.countsTowardMapAchievements,
            "Brooks contract identity, span, rewards or unpublished flags differ; budget/load may be edited to positive values.");
        Require(contract.featureUnlockRewards == null || contract.featureUnlockRewards.Count == 0,
            "Brooks must not grant an unrequested feature reward.");
        Require(contract.offerDialogue != null && contract.offerDialogue.sentences.Length == 4 &&
            contract.reminderDialogue != null && contract.reminderDialogue.sentences.Length == 1 &&
            contract.finishedContractDialogue != null && contract.finishedContractDialogue.sentences.Length == 3,
            "Brooks contract dialogue differs from the supplied briefing/reminder/turn-in reference.");
        BuildLocation location = Location(scene);
        Require(location.startingAnchors.Count == 1 && location.endingAnchors.Count == 1 &&
            location.startingAnchors.All(p => p != null) && location.endingAnchors.All(p => p != null) &&
            Mathf.Abs(Vector3.Distance(Upper(location.startingAnchors).position, Upper(location.endingAnchors).position) - 58.09374f) < .2f,
            "Brooks's audited bank anchors or 58.09-metre site span differ.");
        Require(location.activeContract == null || location.activeContract == contract || location.activeContract.name == "NewContract",
            "Brooks's workbench has a different authored contract. It was not replaced.");
        Require(All<LiveLoadVehicle>(scene).Count(v => v.assignedContract == contract) == 1 &&
            All<FinishLineTrigger>(scene).Count(t => t.assignedContract == contract) == 1,
            "The existing Brooks truck and finish trigger must already reference Brooks's contract exactly once.");
        CinematicDirector template = CinematicTemplate(scene);
        Require(template.cinematicCamera != null && template.playerActor != null && template.playerAnimator != null,
            "Marsh's existing cinematic camera/actor references are unavailable; no replacement camera was created.");
        Require(All<LessonUIManager>(scene).Any() && All<AlmanacLessonTab>(scene).Any(), "Existing lesson and Almanac UI must be present.");
        LessonData lesson = LessonAsset();
        QuizData quiz = Quiz();
        Require(lesson != null && quiz.assessmentMode && quiz.QuestionCount == 5 && quiz.TryValidate(out _),
            "Moving Load requires a valid five-question assessment accepting any submitted score.");
        Require(IsGate(brooks.GetComponent<NPCSpawnerCondition>(), brooks.gameObject, FordContract(), "FordNPC"),
            "Brooks's existing Ford contract/review gate differs. It was not replaced.");
        NPCProgressionManager ronan = NPC(scene, "RonanNpc");
        NPCSpawnerCondition gate = ronan.GetComponent<NPCSpawnerCondition>();
        Require(gate == null || IsGate(gate, ronan.gameObject, contract, "BrooksNpc"),
            "Ronan has custom spawn conditions. They were not replaced.");
        GameObject root = StoryRoot(scene);
        Require(phases.Count != 0 || root == null,
            "A Brooks Story hierarchy already exists without the expected phases. Review it before migrating.");
    }

    private static void ApplyCanyon(Scene scene, NPCProgressionManager brooks, BuildLocation location,
        Vector3 sitePosition, Vector3 start, Vector3 end, Vector3 direction, Vector3 side, StringBuilder report)
    {
        GameObject root = NewObject(scene, StoryRootName, null);
        Transform spawn = Marker(scene, root.transform, "Spawn", brooks.transform.position, brooks.transform.rotation);
        Transform site = Marker(scene, root.transform, "Site", sitePosition, Quaternion.LookRotation(direction, Vector3.up));
        GameObject followRoot = NewObject(scene, "Follow Brooks Guide", root.transform);
        TutorialSequence guide = Undo.AddComponent<TutorialSequence>(followRoot);
        guide.lessonName = FollowLessonId;
        guide.requiredPreviousLesson = string.Empty;
        guide.playOnStart = false;
        guide.nextSequence = null;
        guide.autoStartNextSequence = false;
        guide.tutorialSteps = new[] { new TutorialStep { message = "Follow Brooks to the market crossing.",
            screenPosition = TutorialPosition.Left, showNextButton = false, canSkip = true,
            stepWaypoints = new List<GuiderWaypoint> { new GuiderWaypoint { target = site, advancesTutorial = true } } } };
        EditorUtility.SetDirty(guide);

        GameObject movieRoot = NewObject(scene, "Market Connection Cinematic", root.transform);
        CinematicDirector movie = Undo.AddComponent<CinematicDirector>(movieRoot);
        CinematicDirector template = CinematicTemplate(scene);
        movie.cinematicID = CinematicId;
        movie.playOnlyOnce = false;
        movie.playOnStart = false;
        movie.cinematicCamera = template.cinematicCamera;
        movie.playerActor = template.playerActor;
        movie.playerAnimator = template.playerAnimator;
        movie.cameraTransitionDuration = template.cameraTransitionDuration;
        movie.cameraReturnDuration = template.cameraReturnDuration;
        movie.animatorWalkParameter = template.animatorWalkParameter;
        movie.walkValue = template.walkValue;
        movie.animatorGroundedParameter = template.animatorGroundedParameter;
        movie.hudElementsToHide = new List<GameObject>(template.hudElementsToHide ?? new List<GameObject>());
        movie.contractToDisplay = Contract();
        movie.OnCinematicStarted = new UnityEvent();
        movie.OnCinematicFinished = new UnityEvent();
        Vector3 middle = (start + end) * .5f;
        movie.shots = new List<CinematicShot>
        {
            Shot(scene, movieRoot.transform, "Market connection", middle + side * 45f + Vector3.up * 27f - direction * 18f,
                middle + side * 39f + Vector3.up * 23f + direction * 10f, end + direction * 12f),
            Shot(scene, movieRoot.transform, "Existing delivery approaches", start - direction * 28f + side * 30f + Vector3.up * 16f,
                start - direction * 12f + side * 27f + Vector3.up * 14f, start - direction * 10f),
            Shot(scene, movieRoot.transform, "Bridge site and banks", middle + side * 75f + Vector3.up * 43f,
                middle + side * 65f + Vector3.up * 37f, middle + Vector3.up * 2f)
        };
        report.AppendLine("Three broad market/site/approach shots use existing scenery; no obsolete crossing or barriers were invented.");
        EditorUtility.SetDirty(movie);

        var meeting = Phase("MeetingBrooks", null, false);
        meeting.phaseDialogue = Dialogue("Oh, hello! You must be {PlayerName}!",
            "I'm Brooks. I work with the merchants and businesses here in Rivermere.",
            "We've been hoping to improve one of the routes to our market district.",
            "Would you mind taking a look? I think you'll see why it matters.");
        UnityEventTools.AddPersistentListener(meeting.onDialogueFinished, guide.StartTutorial);
        meeting.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = brooks, phaseId = "ExplainSituation" };
        var explain = Phase("ExplainSituation", site, false);
        explain.phaseDialogue = Dialogue("See the market across the river?");
        UnityEventTools.AddPersistentListener(explain.onNPCInteracted, guide.Skip);
        UnityEventTools.AddPersistentListener(explain.onDialogueFinished, movie.PlayCinematic);
        var introduction = Phase("LessonIntroduction", site, true);
        introduction.dialoguePrompt = "Continue Conversation";
        introduction.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("It's one of the busiest places around here, but reaching it isn't very convenient.",
                "Customers and delivery vehicles have to take a longer route to get there.",
                "A new crossing would make visiting the market much easier for everyone.",
                "And since both taxis and cargo vehicles will be using it, we'll need a design that can handle different loads."),
            Say("There's something important to consider when vehicles travel across a bridge.",
                "Their weight doesn't stay in one place. " + Em("As they move, the forces acting on the bridge change too") + ".",
                "That's what we call a " + Em("Moving Load") + ".",
                "Your bridge needs to remain safe throughout the entire crossing."),
            Lesson()
        };
        introduction.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = brooks, phaseId = "ProjectBriefing" };
        var briefing = Phase("ProjectBriefing", site, true);
        briefing.contract = Contract();
        briefing.targetBuildLocation = location;
        var review = Phase(ReviewPhaseId, site, true);
        review.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Lesson(),
            Say("You can review the " + Em("Moving Load lesson in your Almanac") + " before starting your quiz.",
                "When you're ready, open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + "."),
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = Quiz(), quizStepId = QuizStepId,
                quizWaitingReminder = "Take your time reviewing Moving Load in the Almanac. Open the Quiz tab and press Start Quiz when you're ready." }
        };
        review.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = brooks, phaseId = "Resolved" };
        var resolved = Phase("Resolved", site, false);
        resolved.showExclamationMark = false;
        resolved.phaseDialogue = Dialogue("The market has a more direct connection now. Thanks again for your help, {PlayerName}.");
        Undo.RecordObject(brooks, "Configure Brooks story phases");
        PhasesField.SetValue(brooks, new List<NPCProgressionPhase> { meeting, explain, introduction, briefing, review, resolved });
        SerializedObject serializedBrooks = new SerializedObject(brooks);
        serializedBrooks.FindProperty("authoredSpawnLocation").objectReferenceValue = spawn;
        serializedBrooks.ApplyModifiedProperties();
        EditorUtility.SetDirty(brooks);

        BrooksPostContractContinuation continuation = Undo.AddComponent<BrooksPostContractContinuation>(brooks.gameObject);
        SerializedObject continuationData = new SerializedObject(continuation);
        continuationData.FindProperty("npc").objectReferenceValue = brooks;
        continuationData.FindProperty("contract").objectReferenceValue = Contract();
        continuationData.FindProperty("reviewPhaseId").stringValue = ReviewPhaseId;
        continuationData.ApplyModifiedProperties();
        UnityEventTools.AddPersistentListener(movie.OnCinematicFinished, continuation.ContinueAfterSituationCinematic);
        EditorUtility.SetDirty(movie);
        // Only remove the audited placeholder. Acceptance owns the real assignment.
        if (location.activeContract != null && location.activeContract != Contract())
        {
            Undo.RecordObject(location, "Clear Brooks workbench placeholder contract");
            location.activeContract = null;
            EditorUtility.SetDirty(location);
        }
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene)) RegisterLesson(tab);
        NPCProgressionManager ronan = NPC(scene, "RonanNpc");
        if (ronan.GetComponent<NPCSpawnerCondition>() == null)
        {
            NPCSpawnerCondition gate = Undo.AddComponent<NPCSpawnerCondition>(ronan.gameObject);
            gate.useMultipleConditions = true;
            gate.matchMode = NPCSpawnerCondition.MatchMode.All;
            gate.conditions = new List<NPCSpawnerCondition.Condition>
            {
                new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.ContractCompleted, contract = Contract() },
                new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.NPCAtPhase, id = "BrooksNpc", phaseId = "Resolved" }
            };
            gate.npcRoot = ronan.gameObject;
            gate.waitForManualSpawn = false;
            gate.persistManualOverride = false;
            gate.updateAutomatically = true;
            EditorUtility.SetDirty(gate);
        }
    }

    private static void ValidateCanyon(Scene scene, StringBuilder report)
    {
        Preflight(scene);
        NPCProgressionManager brooks = NPC(scene, "BrooksNpc");
        var phases = Phases(brooks);
        Require(phases.Select(p => p.phaseId).SequenceEqual(PhaseIds), "Brooks's six story phases have not been applied.");
        GameObject root = StoryRoot(scene);
        Require(root != null && root.transform.parent == null && root.activeSelf, "Missing or inactive Brooks Story helper root.");
        Transform spawn = root.transform.Find("Spawn"), site = root.transform.Find("Site");
        Require(spawn != null && site != null &&
            new SerializedObject(brooks).FindProperty("authoredSpawnLocation").objectReferenceValue == spawn,
            "Brooks requires his independent authored spawn and safe site markers.");
        Require(phases[0].targetLocation == null && phases.Skip(1).All(p => p.targetLocation == site) &&
            phases[0].phaseDialogue.sentences.Length == 4 && phases[1].phaseDialogue.sentences.Length == 1,
            "Brooks's meeting, site destination or explanation dialogue differs.");
        Require(phases[0].onDialogueFinishedPhaseMove.npc == brooks && phases[0].onDialogueFinishedPhaseMove.phaseId == "ExplainSituation" &&
            phases[2].onDialogueFinishedPhaseMove.npc == brooks && phases[2].onDialogueFinishedPhaseMove.phaseId == "ProjectBriefing" &&
            phases[4].onDialogueFinishedPhaseMove.npc == brooks && phases[4].onDialogueFinishedPhaseMove.phaseId == "Resolved",
            "Brooks's story continuation links differ.");
        Require(phases[2].optionalDialogueSequence.Select(s => (int)s.stepType).SequenceEqual(new[] { 0, 0, 3 }) &&
            phases[2].optionalDialogueSequence[0].dialogue.sentences.Length == 4 &&
            phases[2].optionalDialogueSequence[1].dialogue.sentences.Length == 4 &&
            phases[2].optionalDialogueSequence[2].lesson == LessonAsset(), "Brooks's lesson introduction order differs.");
        Require(phases[3].contract == Contract() && phases[3].targetBuildLocation == Location(scene) &&
            phases.Where((p, i) => i != 3).All(p => p.contract == null), "Only Brooks's existing workbench may be bound to ProjectBriefing.");
        Require(phases[4].optionalDialogueSequence.Select(s => (int)s.stepType).SequenceEqual(new[] { 3, 0, 2 }) &&
            phases[4].optionalDialogueSequence[0].lesson == LessonAsset() &&
            phases[4].optionalDialogueSequence[2].quiz == Quiz() && phases[4].optionalDialogueSequence[2].quizStepId == QuizStepId &&
            phases[4].playDialogueOnArrival && !phases[5].playDialogueOnArrival && phases[5].repeatDialogue && !phases[5].showExclamationMark,
            "Post-collection review/assessment or terminal idle state differs.");
        Require(phases.All(p => string.IsNullOrEmpty(p.unlockFeatureIdAfterDialogue) && !p.showFeatureCollectPopup && p.cinematicAfterOffer == null),
            "Brooks must not award default features or play a stale offer cinematic.");
        CinematicDirector movie = root.GetComponentsInChildren<CinematicDirector>(true).Single();
        Require(movie.cinematicID == CinematicId && !movie.playOnlyOnce && !movie.playOnStart && movie.contractToDisplay == Contract() &&
            movie.cinematicCamera == CinematicTemplate(scene).cinematicCamera && movie.shots.Count == 3 &&
            movie.shots.All(s => s.cameraStartPoint != null && s.cameraEndPoint != null && s.duration > 0 &&
                s.playerStartPoint == null && s.playerWalkTarget == null && !s.playWalkAnimation),
            "Brooks cinematic must animate its own points with an existing camera and no player relocation.");
        BrooksPostContractContinuation continuation = brooks.GetComponent<BrooksPostContractContinuation>();
        Require(continuation != null && Calls(phases[1].onDialogueFinished, movie, "PlayCinematic") &&
            Calls(movie.OnCinematicFinished, continuation, "ContinueAfterSituationCinematic"),
            "Brooks's cinematic must use the guarded continuation, not an unconditional phase move.");
        TutorialSequence guide = root.GetComponentsInChildren<TutorialSequence>(true).Single();
        Require(guide.lessonName == FollowLessonId && !guide.playOnStart && !guide.autoStartNextSequence && guide.nextSequence == null &&
            guide.tutorialSteps.Length == 1 && guide.tutorialSteps[0].canSkip && !guide.tutorialSteps[0].showNextButton &&
            guide.tutorialSteps[0].stepWaypoints.Single().target == site &&
            Calls(phases[0].onDialogueFinished, guide, "StartTutorial") && Calls(phases[1].onNPCInteracted, guide, "Skip"),
            "Brooks's optional follow guide must start after meeting and safely close before talking at the site.");
        SerializedObject saved = new SerializedObject(continuation);
        Require(saved.FindProperty("npc").objectReferenceValue == brooks && saved.FindProperty("contract").objectReferenceValue == Contract() &&
            saved.FindProperty("reviewPhaseId").stringValue == ReviewPhaseId, "Brooks continuation has stale references.");
        NPCProgressionManager ronan = NPC(scene, "RonanNpc");
        Require(IsGate(ronan.GetComponent<NPCSpawnerCondition>(), ronan.gameObject, Contract(), "BrooksNpc"),
            "Ronan must wait for the completed Brooks contract and resolved review/quiz.");
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene))
        {
            SerializedProperty list = new SerializedObject(tab).FindProperty("allLessons");
            int count = 0;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == LessonAsset()) count++;
            Require(count == 1, "Moving Load must appear exactly once in each Almanac lesson archive.");
        }
        report.AppendLine("PASS: Meeting Brooks > follow guide/site > animated cinematic > Moving Load lesson > normal contract acceptance.");
        report.AppendLine("PASS: durable Collect > Moving Load review > five-question assessment, any score/no rewards > Resolved > Ronan visibility.");
        report.AppendLine("PASS: BrooksMainContract ID, 58.09-metre span, 500 coins/100 XP and unpublished flags retained.");
    }

    private static Vector3 SafeSitePosition(NPCProgressionManager brooks, BuildLocation location, Vector3 desired)
    {
        NavMeshAgent agent = brooks.GetComponent<NavMeshAgent>();
        Require(agent != null, "Brooks requires his existing NavMeshAgent.");
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        Vector3[] candidates = location.navigationTarget != null &&
            Vector3.Distance(location.navigationTarget.transform.position, desired) <= 8f
            ? new[] { desired, location.navigationTarget.transform.position } : new[] { desired };
        foreach (Vector3 candidate in candidates)
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, filter) &&
                Vector3.Distance(Vector3.ProjectOnPlane(hit.position - candidate, Vector3.up), Vector3.zero) <= 3f &&
                Mathf.Abs(hit.position.y - candidate.y) <= 2f) return hit.position;
        throw new InvalidOperationException("No safe Brooks site NavMesh point within 3 m horizontally/2 m vertically. Check the existing bank NavMesh; no NPC marker was created.");
    }

    private static Dictionary<UnityEngine.Object, string> CaptureProtectedState(Scene scene, NPCProgressionManager brooks, BuildLocation location)
    {
        var result = new Dictionary<UnityEngine.Object, string>();
        foreach (NPCProgressionManager npc in All<NPCProgressionManager>(scene))
            if (npc != brooks) Protect(result, npc);
        foreach (NPCSpawnerCondition gate in All<NPCSpawnerCondition>(scene)) Protect(result, gate);
        foreach (Camera camera in All<Camera>(scene)) Protect(result, camera);
        foreach (CinematicDirector cinematic in All<CinematicDirector>(scene))
        {
            Protect(result, cinematic);
            foreach (CinematicShot shot in cinematic.shots)
            {
                Protect(result, shot.cameraStartPoint);
                Protect(result, shot.cameraEndPoint);
            }
        }
        foreach (BuildLocation build in All<BuildLocation>(scene))
            if (build != location) Protect(result, build);
        foreach (LiveLoadVehicle vehicle in All<LiveLoadVehicle>(scene))
        {
            Protect(result, vehicle);
            Protect(result, vehicle.startPoint);
            Protect(result, vehicle.endPoint);
            foreach (Transform part in vehicle.GetComponentsInChildren<Transform>(true)) Protect(result, part);
        }
        foreach (FinishLineTrigger finish in All<FinishLineTrigger>(scene))
        {
            Protect(result, finish);
            Protect(result, finish.transform);
        }
        Transform site = location.transform;
        while (site.parent != null && site.name != "FordSideContract") site = site.parent;
        Require(site.name == "FordSideContract", "Brooks's audited site hierarchy is missing.");
        foreach (Transform point in site.GetComponentsInChildren<Transform>(true)) Protect(result, point);
        Protect(result, brooks.transform);
        return result;
    }

    private static void Protect(Dictionary<UnityEngine.Object, string> state, UnityEngine.Object value)
    {
        if (value != null && !state.ContainsKey(value)) state.Add(value, EditorJsonUtility.ToJson(value));
    }

    private static string WorkbenchState(BuildLocation location) => Regex.Replace(EditorJsonUtility.ToJson(location),
        "\"activeContract\"\\s*:\\s*\\{[^}]*\\}", "\"activeContract\":null");
    private static void RegisterLesson(AlmanacLessonTab tab)
    {
        SerializedObject data = new SerializedObject(tab);
        SerializedProperty list = data.FindProperty("allLessons");
        for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == LessonAsset()) return;
        Undo.RecordObject(tab, "Register Moving Load lesson");
        int index = list.arraySize++;
        list.GetArrayElementAtIndex(index).objectReferenceValue = LessonAsset();
        data.ApplyModifiedProperties();
    }
    private static CinematicShot Shot(Scene scene, Transform parent, string name, Vector3 from, Vector3 to, Vector3 focus) =>
        new CinematicShot { shotName = name, duration = 4f,
            cameraStartPoint = Marker(scene, parent, name + " Start", from, Quaternion.LookRotation(focus - from, Vector3.up)),
            cameraEndPoint = Marker(scene, parent, name + " End", to, Quaternion.LookRotation(focus - to, Vector3.up)),
            playWalkAnimation = false, showBridgeInfoCanvas = false, postShotDelay = .15f };
    private static GameObject NewObject(Scene scene, string name, Transform parent)
    {
        GameObject created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "Create Brooks story helper");
        SceneManager.MoveGameObjectToScene(created, scene);
        if (parent != null) Undo.SetTransformParent(created.transform, parent, "Parent Brooks story helper");
        return created;
    }
    private static Transform Marker(Scene scene, Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        Transform marker = NewObject(scene, name, parent).transform;
        marker.SetPositionAndRotation(position, rotation);
        return marker;
    }
    private static NPCProgressionPhase Phase(string id, Transform target, bool automatic) => new NPCProgressionPhase
    {
        phaseId = id, targetLocation = target, dialoguePrompt = "Talk to Brooks", phaseDialogue = Dialogue(),
        playDialogueOnArrival = automatic, repeatDialogue = true, showExclamationMark = true,
        unlockFeatureIdAfterDialogue = string.Empty, featureUnlockDisplayName = string.Empty,
        showFeatureCollectPopup = false, introduceMaterialOnlyOnce = false,
        onDialogueFinished = new UnityEvent(), onNPCInteracted = new UnityEvent(), onNPCArrived = new UnityEvent(),
        onMaterialIntroductionClosed = new UnityEvent()
    };
    private static bool IsGate(NPCSpawnerCondition gate, GameObject root, ContractSO contract, string npcId) =>
        gate != null && gate.enabled && gate.npcRoot == root && gate.useMultipleConditions &&
        gate.matchMode == NPCSpawnerCondition.MatchMode.All && !gate.hideWhenMatched && !gate.waitForManualSpawn &&
        !gate.persistManualOverride && !gate.useDespawnConditions && gate.updateAutomatically &&
        gate.conditions != null && gate.conditions.Count == 2 && gate.conditions.All(r => r != null && r.mustBeTrue) &&
        gate.conditions.Any(r => r.kind == NPCSpawnerCondition.ConditionKind.ContractCompleted && r.contract == contract) &&
        gate.conditions.Any(r => r.kind == NPCSpawnerCondition.ConditionKind.NPCAtPhase && r.id == npcId && r.phaseId == "Resolved");
    private static bool Calls(UnityEvent callback, UnityEngine.Object target, string method) => callback != null &&
        callback.GetPersistentEventCount() == 1 && callback.GetPersistentTarget(0) == target &&
        callback.GetPersistentMethodName(0) == method && callback.GetPersistentListenerState(0) != UnityEventCallState.Off;
    private static Scene LoadedCanyon()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Leave Unity in Edit Mode with imports finished and the prefab editor closed.");
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing. This tool never changes the loaded scene setup.");
        return scene;
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static NPCProgressionManager NPC(Scene scene, string id) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == id);
    private static List<NPCProgressionPhase> Phases(NPCProgressionManager npc) => (List<NPCProgressionPhase>)PhasesField.GetValue(npc);
    private static BuildLocation Location(Scene scene) => All<BuildLocation>(scene).Single(location => location.name == "workbenchbrooks" &&
        location.GetComponentsInParent<Transform>(true).Any(t => t.name == "FordSideContract"));
    private static Transform Upper(List<Point> points) => points.OrderByDescending(p => p.transform.position.y).First().transform;
    private static GameObject StoryRoot(Scene scene) => scene.GetRootGameObjects().SingleOrDefault(root => root.name == StoryRootName);
    private static CinematicDirector CinematicTemplate(Scene scene) => All<CinematicDirector>(scene).Single(d => d.cinematicID == "MarshCutScene");
    private static string Hierarchy(Transform target) => target.parent == null ? target.name : Hierarchy(target.parent) + "/" + target.name;
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Missing Brooks story asset: " + path);
    private static ContractSO Contract() => Load<ContractSO>(Data + "Contract/TownRiver/Brooks Contract.asset");
    private static ContractSO FordContract() => Load<ContractSO>(Data + "Contract/TownRiver/Ford Contract.asset");
    private static LessonData LessonAsset() => Load<LessonData>(Data + "Lessons/MovingLoad.asset");
    private static QuizData Quiz() => Load<QuizData>(Data + "Quized/MovingLoadQuiz.asset");
    private static Dialogue Dialogue(params string[] sentences) => new Dialogue { name = "Brooks", sentences = sentences };
    private static NPCOptionalDialogueStep Say(params string[] sentences) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.Dialogue, dialogue = Dialogue(sentences) };
    private static NPCOptionalDialogueStep Lesson() => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = LessonAsset() };
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void WriteReport(string text) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); }
}
#endif
