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

/// <summary>Explicit, undoable Ford story authoring. Never saves or switches scenes.</summary>
[InitializeOnLoad]
public static class FordStoryAuthoring
{
    public const string RequestPath = "Temp/ford-story-v1.request";
    public const string ReportPath = "Logs/ford-story-v1.txt";
    public const string ReviewPhaseId = "FordReview";
    public const string QuizStepId = "ford-material-stress-assessment-v1";
    public const string StoryRootName = "Ford Story";
    public const string CinematicId = "FordConstructionRoute-v1";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private const string FollowLessonId = "Story/Ford/FollowToSite-v1";
    private static readonly string[] PhaseIds =
        { "MeetingFord", "ExplainSituation", "LessonIntroduction", "ProjectBriefing", ReviewPhaseId, "Resolved" };
    private static readonly FieldInfo PhasesField = typeof(NPCProgressionManager)
        .GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
    private static double nextPoll;

    static FordStoryAuthoring() { EditorApplication.update += Poll; }

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
            else if (command == "reframe") ReframeOverview();
            else throw new InvalidOperationException("Unknown Ford story request: " + command);
        }
        catch (Exception error) { WriteReport("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Ford/1. Apply to Saved Canyon (Do Not Save)")]
    public static void ApplySavedStory()
    {
        Scene scene = LoadedCanyon();
        Require(!scene.isDirty, "Save your current CanyonCrossing edits first. No scene changes were applied or saved.");
        Preflight(scene);
        NPCProgressionManager ford = NPC(scene, "FordNPC");
        if (Phases(ford).Count != 0)
        {
            ValidateLoaded();
            return; // Repeated applies are read-only; never regenerate authored camera points or custom edits.
        }
        BuildLocation location = Location(scene);
        Vector3 start = Upper(location.startingAnchors).position;
        Vector3 end = Upper(location.endingAnchors).position;
        Vector3 direction = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 sitePosition = SafeSitePosition(ford, location, start - direction * 7f + side * 4f);
        var protectedState = CaptureProtectedState(scene, ford, location);
        string workbenchState = WorkbenchState(location);
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-FordStory-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, Path.Combine(backup, "CanyonCrossing.unity"));
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Author Ford Material Stress Story");
        try
        {
            StringBuilder report = new StringBuilder("Ford - The Construction Route\n");
            ApplyCanyon(scene, ford, location, sitePosition, start, end, direction, side, report);
            ValidateCanyon(scene, report);
            foreach (var entry in protectedState)
                Require(entry.Key != null && EditorJsonUtility.ToJson(entry.Key) == entry.Value,
                    "An unrelated NPC, existing gate, camera, cinematic, vehicle or authored transform changed. Migration rolled back.");
            Require(WorkbenchState(location) == workbenchState,
                "The Ford workbench changed outside its active-contract assignment. Migration rolled back.");
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            report.AppendLine("PASS: Marsh progression/gate, all existing cameras, vehicle routes and bridge geometry preserved.");
            report.AppendLine("Budget/load remain editable placeholders chosen by the author; no balancing values were invented.");
            report.AppendLine("No hints, simulation callouts, automatic traffic, side quests or new structural physics added.");
            report.AppendLine("Recoverable pre-migration scene: " + backup);
            report.AppendLine("NEEDS_SAVE: CanyonCrossing. Review, then save this scene in Unity.");
            WriteReport(report.ToString());
            Debug.Log(report.ToString());
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Ford/2. Validate Loaded Canyon")]
    public static void ValidateLoaded()
    {
        Scene scene = LoadedCanyon();
        StringBuilder report = new StringBuilder("Ford saved/loaded story validation\n");
        ValidateCanyon(scene, report);
        report.AppendLine("NEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    [MenuItem("Tools/Civil Craft/Story/Ford/4. Widen Original Overview (Do Not Save)")]
    public static void ReframeOverview()
    {
        Scene scene = LoadedCanyon();
        var report = new StringBuilder("Ford overview framing correction\n");
        ValidateCanyon(scene, report);
        NPCProgressionManager ford = NPC(scene, "FordNPC");
        BuildLocation location = Location(scene);
        CinematicDirector movie = StoryRoot(scene).GetComponentsInChildren<CinematicDirector>(true).Single();
        CinematicShot shot = movie.shots.Single(s => s.shotName == "Bridge site and banks");
        Vector3 start = Upper(location.startingAnchors).position, end = Upper(location.endingAnchors).position;
        Vector3 middle = (start + end) * .5f;
        Vector3 side = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(end - start, Vector3.up).normalized).normalized;
        Vector3 from = middle + side * 75f + Vector3.up * 43f;
        Vector3 to = middle + side * 65f + Vector3.up * 37f;
        Vector3 focus = middle + Vector3.up * 2f;
        if (PoseMatches(shot.cameraStartPoint, from, focus) && PoseMatches(shot.cameraEndPoint, to, focus))
        {
            report.AppendLine("PASS: overview already widened; no changes made.");
            report.AppendLine("NEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
            WriteReport(report.ToString());
            return;
        }
        Require(PoseMatches(shot.cameraStartPoint, middle + side * 34f + Vector3.up * 19f, middle) &&
            PoseMatches(shot.cameraEndPoint, middle + side * 26f + Vector3.up * 15f, middle),
            "The overview has custom poses. They were not overwritten; adjust them manually if desired.");
        var protectedState = CaptureProtectedState(scene, ford, location);
        protectedState.Remove(shot.cameraStartPoint);
        protectedState.Remove(shot.cameraEndPoint);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Widen Ford bridge overview");
        try
        {
            Undo.RecordObjects(new UnityEngine.Object[] { shot.cameraStartPoint, shot.cameraEndPoint }, "Widen Ford bridge overview");
            shot.cameraStartPoint.SetPositionAndRotation(from, Quaternion.LookRotation(focus - from, Vector3.up));
            shot.cameraEndPoint.SetPositionAndRotation(to, Quaternion.LookRotation(focus - to, Vector3.up));
            foreach (var entry in protectedState)
                Require(entry.Key != null && EditorJsonUtility.ToJson(entry.Key) == entry.Value,
                    "An unrelated scene object changed; framing correction rolled back.");
            EditorUtility.SetDirty(shot.cameraStartPoint);
            EditorUtility.SetDirty(shot.cameraEndPoint);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            report.AppendLine("PASS: only the original overview's two camera markers widened to show both banks. Existing cameras and story data preserved.");
            report.AppendLine("NEEDS_SAVE: CanyonCrossing. No scene was saved.");
            WriteReport(report.ToString());
            Debug.Log(report.ToString());
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static bool PoseMatches(Transform pose, Vector3 position, Vector3 focus) => pose != null &&
        Vector3.Distance(pose.position, position) < .02f &&
        Quaternion.Angle(pose.rotation, Quaternion.LookRotation(focus - position, Vector3.up)) < .1f;

    [MenuItem("Tools/Civil Craft/Story/Ford/3. Inspect Existing Site (Read Only)")]
    public static void InspectLoaded()
    {
        Scene scene = LoadedCanyon();
        Preflight(scene);
        NPCProgressionManager ford = NPC(scene, "FordNPC");
        BuildLocation location = Location(scene);
        Vector3 start = Upper(location.startingAnchors).position, end = Upper(location.endingAnchors).position;
        Vector3 direction = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
        Vector3 desired = start - direction * 7f + Vector3.Cross(Vector3.up, direction) * 4f;
        var report = new StringBuilder("Ford site inspection (read only)\n");
        report.AppendLine("Upper anchors: " + start + " -> " + end + "; span " + Vector3.Distance(start, end));
        Vector3 sitePosition = SafeSitePosition(ford, location, desired);
        report.AppendLine("Safe NPC marker: " + sitePosition);
        NavMeshAgent agent = ford.GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        Transform authoredSpawn = new SerializedObject(ford).FindProperty("authoredSpawnLocation").objectReferenceValue as Transform;
        Vector3 spawnPosition = authoredSpawn != null ? authoredSpawn.position : ford.transform.position;
        if (!NavMesh.SamplePosition(spawnPosition, out NavMeshHit spawnHit, 3f, filter) ||
            Vector3.ProjectOnPlane(spawnHit.position - spawnPosition, Vector3.up).magnitude > 3f ||
            Mathf.Abs(spawnHit.position.y - spawnPosition.y) > 2f)
        {
            report.AppendLine("ROUTE_UNAVAILABLE: authored Ford spawn has no valid NavMesh sample within 3 m horizontally/2 m vertically. No bake, teleport or route substitution performed.");
        }
        else
        {
            NavMeshPath route = new NavMeshPath();
            bool calculated = NavMesh.CalculatePath(spawnHit.position, sitePosition, filter, route);
            float routeLength = 0;
            for (int i = 1; i < route.corners.Length; i++) routeLength += Vector3.Distance(route.corners[i - 1], route.corners[i]);
            report.AppendLine("Ford authored-spawn route: sample " + spawnHit.position + "; calculated " + calculated +
                "; status " + route.status + "; corners " + route.corners.Length + "; length " + routeLength.ToString("0.00") + " m.");
            if (!calculated || route.status != NavMeshPathStatus.PathComplete)
                report.AppendLine("ROUTE_REVIEW_REQUIRED: the current NavMesh does not prove a complete Ford walk. No auto-bake, wider sampling or teleport was attempted.");
        }
        Collider rock = FindMidspanRock(scene, start, end);
        report.AppendLine(rock == null ? "No verified midspan rock collider. Cinematic will use three broad site/route shots; no terrain or anchor will be invented."
            : "Verified rock collider: " + Hierarchy(rock.transform) + "; world bounds " + rock.bounds);
        report.AppendLine("An existing Pier is required to use ground as support; touching terrain does not create a permanent Point anchor.");
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Preflight(Scene scene)
    {
        NPCProgressionManager ford = NPC(scene, "FordNPC");
        var phases = Phases(ford);
        Require(phases != null && (phases.Count == 0 || phases.Select(p => p?.phaseId).SequenceEqual(PhaseIds)),
            "Ford has custom phase IDs/order. They were not overwritten.");
        ContractSO contract = Contract();
        Require(contract.ContractID == "FordMainContract" && contract.contractMap == ContractSO.ContractMap.TownRiver &&
            contract.budget > 0 && !float.IsNaN(contract.budget) && !float.IsInfinity(contract.budget) &&
            contract.liveLoadWeight > 0 && !float.IsNaN(contract.liveLoadWeight) && !float.IsInfinity(contract.liveLoadWeight) &&
            Mathf.Approximately(contract.bridgeSpan, 80f) && contract.goldReward == 500 && contract.expReward == 100 &&
            !contract.autoCollectReward && contract.hideFromLeaderboard && !contract.countsTowardMapAchievements,
            "Ford contract identity, span, rewards or unpublished flags differ; budget/load may be edited to positive values.");
        Require(contract.featureUnlockRewards == null || contract.featureUnlockRewards.Count == 0,
            "Ford must not grant an unrequested feature reward.");
        Require(contract.offerDialogue != null && contract.offerDialogue.sentences.Length == 6 &&
            contract.reminderDialogue != null && contract.reminderDialogue.sentences.Length == 1 &&
            contract.finishedContractDialogue != null && contract.finishedContractDialogue.sentences.Length == 4,
            "Ford contract dialogue differs from the supplied briefing/reminder/turn-in reference.");
        BuildLocation location = Location(scene);
        Require(location.startingAnchors.Count == 2 && location.endingAnchors.Count == 2 &&
            location.startingAnchors.All(p => p != null) && location.endingAnchors.All(p => p != null) &&
            Mathf.Abs(Vector3.Distance(Upper(location.startingAnchors).position, Upper(location.endingAnchors).position) - 80f) < 1f,
            "Ford's audited four bank anchors or eighty-metre site span differ.");
        Require(location.activeContract == null || location.activeContract == contract || location.activeContract.name == "NewContract",
            "Ford's workbench has a different authored contract. It was not replaced.");
        Require(All<LiveLoadVehicle>(scene).Count(v => v.assignedContract == contract) == 1 &&
            All<FinishLineTrigger>(scene).Count(t => t.assignedContract == contract) == 1,
            "The existing Ford truck and finish trigger must already reference Ford's contract exactly once.");
        CinematicDirector template = CinematicTemplate(scene);
        Require(template.cinematicCamera != null && template.playerActor != null && template.playerAnimator != null,
            "Marsh's existing cinematic camera/actor references are unavailable; no replacement camera was created.");
        Require(All<LessonUIManager>(scene).Any() && All<AlmanacLessonTab>(scene).Any(), "Existing lesson and Almanac UI must be present.");
        LessonData lesson = LessonAsset();
        QuizData quiz = Quiz();
        Require(lesson != null && quiz.assessmentMode && quiz.QuestionCount == 5 && quiz.TryValidate(out _),
            "Material Stress requires a valid five-question assessment accepting any submitted score.");
        Require(IsGate(ford.GetComponent<NPCSpawnerCondition>(), ford.gameObject, MarshContract(), "MarshNPC"),
            "Ford's existing Marsh contract/review gate differs. It was not replaced.");
        NPCProgressionManager brooks = NPC(scene, "BrooksNpc");
        NPCSpawnerCondition gate = brooks.GetComponent<NPCSpawnerCondition>();
        Require(gate == null || IsGate(gate, brooks.gameObject, contract, "FordNPC"),
            "Brooks has custom spawn conditions. They were not replaced.");
        GameObject root = StoryRoot(scene);
        Require(phases.Count != 0 || root == null,
            "A Ford Story hierarchy already exists without the expected phases. Review it before migrating.");
    }

    private static void ApplyCanyon(Scene scene, NPCProgressionManager ford, BuildLocation location,
        Vector3 sitePosition, Vector3 start, Vector3 end, Vector3 direction, Vector3 side, StringBuilder report)
    {
        GameObject root = NewObject(scene, StoryRootName, null);
        Transform spawn = Marker(scene, root.transform, "Spawn", ford.transform.position, ford.transform.rotation);
        Transform site = Marker(scene, root.transform, "Site", sitePosition, Quaternion.LookRotation(direction, Vector3.up));
        GameObject followRoot = NewObject(scene, "Follow Ford Guide", root.transform);
        TutorialSequence guide = Undo.AddComponent<TutorialSequence>(followRoot);
        guide.lessonName = FollowLessonId;
        guide.requiredPreviousLesson = string.Empty;
        guide.playOnStart = false;
        guide.nextSequence = null;
        guide.autoStartNextSequence = false;
        guide.tutorialSteps = new[] { new TutorialStep { message = "Follow Ford to the construction crossing.",
            screenPosition = TutorialPosition.Left, showNextButton = false, canSkip = true,
            stepWaypoints = new List<GuiderWaypoint> { new GuiderWaypoint { target = site, advancesTutorial = true } } } };
        EditorUtility.SetDirty(guide);

        GameObject movieRoot = NewObject(scene, "Construction Route Cinematic", root.transform);
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
            Shot(scene, movieRoot.transform, "Construction connection", middle + side * 42f + Vector3.up * 24f - direction * 25f,
                middle + side * 35f + Vector3.up * 20f + direction * 12f, end + direction * 12f),
            Shot(scene, movieRoot.transform, "Existing delivery approaches", start - direction * 28f + side * 30f + Vector3.up * 16f,
                start - direction * 12f + side * 27f + Vector3.up * 14f, start - direction * 10f),
            Shot(scene, movieRoot.transform, "Bridge site and banks", middle + side * 75f + Vector3.up * 43f,
                middle + side * 65f + Vector3.up * 37f, middle + Vector3.up * 2f)
        };
        Collider rock = FindMidspanRock(scene, start, end);
        if (rock != null)
        {
            Vector3 focus = rock.bounds.center;
            movie.shots.Insert(2, Shot(scene, movieRoot.transform, "Existing rocky section", focus + side * 20f + Vector3.up * 13f,
                focus + side * 15f + Vector3.up * 10f + direction * 5f, focus));
            report.AppendLine("Verified rock collider: " + Hierarchy(rock.transform) + "; world bounds " + rock.bounds);
        }
        else report.AppendLine("No verified midspan rock collider: three broad site/approach shots used; no terrain or fixed anchor invented.");
        EditorUtility.SetDirty(movie);

        var meeting = Phase("MeetingFord", null, false);
        meeting.phaseDialogue = Dialogue("Hey, you're {PlayerName}, right?",
            "I'm " + Em("Ford") + ". I coordinate the construction deliveries and transportation routes around Rivermere.",
            "Marsh told me about the bridge you built for the residents. Nice work!",
            "I was actually hoping to find someone who could help us with another crossing.",
            "We've got materials to move and a route that is slowing us down.", "Come on, I'll show you what I mean.");
        UnityEventTools.AddPersistentListener(meeting.onDialogueFinished, guide.StartTutorial);
        meeting.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = ford, phaseId = "ExplainSituation" };
        var explain = Phase("ExplainSituation", site, false);
        explain.phaseDialogue = Dialogue("See that construction site on the other side?");
        UnityEventTools.AddPersistentListener(explain.onNPCInteracted, guide.Skip);
        UnityEventTools.AddPersistentListener(explain.onDialogueFinished, movie.PlayCinematic);
        var introduction = Phase("LessonIntroduction", site, true);
        introduction.dialoguePrompt = "Continue Conversation";
        introduction.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("Our trucks have to pass through other route just to deliver materials there, that adds quite a bit of travel time",
                "A bridge across this waterway would give our crews a more direct route.",
                "Now, take a look at that " + Em("rocky section in the middle") + ".",
                "It's a solid ground that might come in handy as an additional support point.",
                "Of course, you'll still have to decide whether it fits your design."),
            Say("Before we begin, there's another thing to consider. These trucks carry much heavier loads than the taxi from your last project.",
                "When a load acts on a bridge, it creates internal stresses within its structural members. We call this " + Em("Material Stress") + ".",
                "Different materials can withstand different amounts of stress. Concrete, for example, generally handles compression better than tension unless it's reinforced.",
                "If the stress becomes greater than what a material can safely withstand, that part of the structure could crack or fail.",
                "That's why it's important to " + Em("understand how your design handles the loads placed on it") + "."),
            Lesson()
        };
        introduction.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = ford, phaseId = "ProjectBriefing" };
        var briefing = Phase("ProjectBriefing", site, true);
        briefing.contract = Contract();
        briefing.targetBuildLocation = location;
        var review = Phase(ReviewPhaseId, site, true);
        review.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Lesson(),
            Say("You can review the " + Em("Material Stress lesson in your Almanac") + " before starting your quiz.",
                "When you're ready, open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + "."),
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = Quiz(), quizStepId = QuizStepId,
                quizWaitingReminder = "Take your time reviewing Material Stress in the Almanac. Open the Quiz tab and press Start Quiz when you're ready." }
        };
        review.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = ford, phaseId = "Resolved" };
        var resolved = Phase("Resolved", site, false);
        resolved.showExclamationMark = false;
        resolved.phaseDialogue = Dialogue("The trucks can use the direct route now. Thanks again for your help, {PlayerName}.");
        Undo.RecordObject(ford, "Configure Ford story phases");
        PhasesField.SetValue(ford, new List<NPCProgressionPhase> { meeting, explain, introduction, briefing, review, resolved });
        SerializedObject serializedFord = new SerializedObject(ford);
        serializedFord.FindProperty("authoredSpawnLocation").objectReferenceValue = spawn;
        serializedFord.ApplyModifiedProperties();
        EditorUtility.SetDirty(ford);

        FordPostContractContinuation continuation = Undo.AddComponent<FordPostContractContinuation>(ford.gameObject);
        SerializedObject continuationData = new SerializedObject(continuation);
        continuationData.FindProperty("npc").objectReferenceValue = ford;
        continuationData.FindProperty("contract").objectReferenceValue = Contract();
        continuationData.FindProperty("reviewPhaseId").stringValue = ReviewPhaseId;
        continuationData.ApplyModifiedProperties();
        UnityEventTools.AddPersistentListener(movie.OnCinematicFinished, continuation.ContinueAfterSituationCinematic);
        EditorUtility.SetDirty(movie);
        // Only remove the audited placeholder. Acceptance owns the real assignment.
        if (location.activeContract != null && location.activeContract != Contract())
        {
            Undo.RecordObject(location, "Clear Ford workbench placeholder contract");
            location.activeContract = null;
            EditorUtility.SetDirty(location);
        }
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene)) RegisterLesson(tab);
        NPCProgressionManager brooks = NPC(scene, "BrooksNpc");
        if (brooks.GetComponent<NPCSpawnerCondition>() == null)
        {
            NPCSpawnerCondition gate = Undo.AddComponent<NPCSpawnerCondition>(brooks.gameObject);
            gate.useMultipleConditions = true;
            gate.matchMode = NPCSpawnerCondition.MatchMode.All;
            gate.conditions = new List<NPCSpawnerCondition.Condition>
            {
                new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.ContractCompleted, contract = Contract() },
                new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.NPCAtPhase, id = "FordNPC", phaseId = "Resolved" }
            };
            gate.npcRoot = brooks.gameObject;
            gate.waitForManualSpawn = false;
            gate.persistManualOverride = false;
            gate.updateAutomatically = true;
            EditorUtility.SetDirty(gate);
        }
    }

    private static void ValidateCanyon(Scene scene, StringBuilder report)
    {
        Preflight(scene);
        NPCProgressionManager ford = NPC(scene, "FordNPC");
        var phases = Phases(ford);
        Require(phases.Select(p => p.phaseId).SequenceEqual(PhaseIds), "Ford's six story phases have not been applied.");
        GameObject root = StoryRoot(scene);
        Require(root != null && root.transform.parent == null && root.activeSelf, "Missing or inactive Ford Story helper root.");
        Transform spawn = root.transform.Find("Spawn"), site = root.transform.Find("Site");
        Require(spawn != null && site != null &&
            new SerializedObject(ford).FindProperty("authoredSpawnLocation").objectReferenceValue == spawn,
            "Ford requires his independent authored spawn and safe site markers.");
        Require(phases[0].targetLocation == null && phases.Skip(1).All(p => p.targetLocation == site) &&
            phases[0].phaseDialogue.sentences.Length == 6 && phases[1].phaseDialogue.sentences.Length == 1,
            "Ford's meeting, site destination or explanation dialogue differs.");
        Require(phases[0].onDialogueFinishedPhaseMove.npc == ford && phases[0].onDialogueFinishedPhaseMove.phaseId == "ExplainSituation" &&
            phases[2].onDialogueFinishedPhaseMove.npc == ford && phases[2].onDialogueFinishedPhaseMove.phaseId == "ProjectBriefing" &&
            phases[4].onDialogueFinishedPhaseMove.npc == ford && phases[4].onDialogueFinishedPhaseMove.phaseId == "Resolved",
            "Ford's story continuation links differ.");
        Require(phases[2].optionalDialogueSequence.Select(s => (int)s.stepType).SequenceEqual(new[] { 0, 0, 3 }) &&
            phases[2].optionalDialogueSequence[0].dialogue.sentences.Length == 5 &&
            phases[2].optionalDialogueSequence[1].dialogue.sentences.Length == 5 &&
            phases[2].optionalDialogueSequence[2].lesson == LessonAsset(), "Ford's lesson introduction order differs.");
        Require(phases[3].contract == Contract() && phases[3].targetBuildLocation == Location(scene) &&
            phases.Where((p, i) => i != 3).All(p => p.contract == null), "Only Ford's existing workbench may be bound to ProjectBriefing.");
        Require(phases[4].optionalDialogueSequence.Select(s => (int)s.stepType).SequenceEqual(new[] { 3, 0, 2 }) &&
            phases[4].optionalDialogueSequence[0].lesson == LessonAsset() &&
            phases[4].optionalDialogueSequence[2].quiz == Quiz() && phases[4].optionalDialogueSequence[2].quizStepId == QuizStepId &&
            phases[4].playDialogueOnArrival && !phases[5].playDialogueOnArrival && phases[5].repeatDialogue && !phases[5].showExclamationMark,
            "Post-collection review/assessment or terminal idle state differs.");
        Require(phases.All(p => string.IsNullOrEmpty(p.unlockFeatureIdAfterDialogue) && !p.showFeatureCollectPopup && p.cinematicAfterOffer == null),
            "Ford must not award default features or play a stale offer cinematic.");
        CinematicDirector movie = root.GetComponentsInChildren<CinematicDirector>(true).Single();
        Require(movie.cinematicID == CinematicId && !movie.playOnlyOnce && !movie.playOnStart && movie.contractToDisplay == Contract() &&
            movie.cinematicCamera == CinematicTemplate(scene).cinematicCamera && movie.shots.Count >= 3 && movie.shots.Count <= 4 &&
            movie.shots.All(s => s.cameraStartPoint != null && s.cameraEndPoint != null && s.duration > 0 &&
                s.playerStartPoint == null && s.playerWalkTarget == null && !s.playWalkAnimation),
            "Ford cinematic must animate its own points with an existing camera and no player relocation.");
        FordPostContractContinuation continuation = ford.GetComponent<FordPostContractContinuation>();
        Require(continuation != null && Calls(phases[1].onDialogueFinished, movie, "PlayCinematic") &&
            Calls(movie.OnCinematicFinished, continuation, "ContinueAfterSituationCinematic"),
            "Ford's cinematic must use the guarded continuation, not an unconditional phase move.");
        TutorialSequence guide = root.GetComponentsInChildren<TutorialSequence>(true).Single();
        Require(guide.lessonName == FollowLessonId && !guide.playOnStart && !guide.autoStartNextSequence && guide.nextSequence == null &&
            guide.tutorialSteps.Length == 1 && guide.tutorialSteps[0].canSkip && !guide.tutorialSteps[0].showNextButton &&
            guide.tutorialSteps[0].stepWaypoints.Single().target == site &&
            Calls(phases[0].onDialogueFinished, guide, "StartTutorial") && Calls(phases[1].onNPCInteracted, guide, "Skip"),
            "Ford's optional follow guide must start after meeting and safely close before talking at the site.");
        SerializedObject saved = new SerializedObject(continuation);
        Require(saved.FindProperty("npc").objectReferenceValue == ford && saved.FindProperty("contract").objectReferenceValue == Contract() &&
            saved.FindProperty("reviewPhaseId").stringValue == ReviewPhaseId, "Ford continuation has stale references.");
        NPCProgressionManager brooks = NPC(scene, "BrooksNpc");
        Require(IsGate(brooks.GetComponent<NPCSpawnerCondition>(), brooks.gameObject, Contract(), "FordNPC"),
            "Brooks must wait for the completed Ford contract and resolved review/quiz.");
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene))
        {
            SerializedProperty list = new SerializedObject(tab).FindProperty("allLessons");
            int count = 0;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == LessonAsset()) count++;
            Require(count == 1, "Material Stress must appear exactly once in each Almanac lesson archive.");
        }
        report.AppendLine("PASS: Meeting Ford > follow guide/site > animated cinematic > Material Stress lesson > normal contract acceptance.");
        report.AppendLine("PASS: durable Collect > Material Stress review > five-question assessment, any score/no rewards > Resolved > Brooks visibility.");
        report.AppendLine("PASS: FordMainContract ID, 80-metre span, 500 coins/100 XP and unpublished flags retained.");
    }

    private static Vector3 SafeSitePosition(NPCProgressionManager ford, BuildLocation location, Vector3 desired)
    {
        NavMeshAgent agent = ford.GetComponent<NavMeshAgent>();
        Require(agent != null, "Ford requires his existing NavMeshAgent.");
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        Vector3[] candidates = location.navigationTarget != null &&
            Vector3.Distance(location.navigationTarget.transform.position, desired) <= 8f
            ? new[] { desired, location.navigationTarget.transform.position } : new[] { desired };
        foreach (Vector3 candidate in candidates)
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, filter) &&
                Vector3.Distance(Vector3.ProjectOnPlane(hit.position - candidate, Vector3.up), Vector3.zero) <= 3f &&
                Mathf.Abs(hit.position.y - candidate.y) <= 2f) return hit.position;
        throw new InvalidOperationException("No safe Ford site NavMesh point within 3 m horizontally/2 m vertically. Check the existing bank NavMesh; no NPC marker was created.");
    }

    private static Collider FindMidspanRock(Scene scene, Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        float length = delta.magnitude;
        Vector3 direction = delta.normalized;
        return All<Collider>(scene).Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger &&
                Hierarchy(c.transform).IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0 &&
                Vector3.Dot(c.bounds.center - start, direction) >= length * .25f &&
                Vector3.Dot(c.bounds.center - start, direction) <= length * .75f &&
                Mathf.Abs(c.bounds.center.z - start.z) <= c.bounds.extents.z + 4f &&
                c.bounds.max.y <= Mathf.Max(start.y, end.y) + 2f && c.bounds.max.y >= Mathf.Min(start.y, end.y) - 20f)
            .OrderBy(c => Vector3.Distance(c.bounds.center, (start + end) * .5f)).FirstOrDefault();
    }

    private static Dictionary<UnityEngine.Object, string> CaptureProtectedState(Scene scene, NPCProgressionManager ford, BuildLocation location)
    {
        var result = new Dictionary<UnityEngine.Object, string>();
        foreach (NPCProgressionManager npc in All<NPCProgressionManager>(scene))
            if (npc != ford) Protect(result, npc);
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
        while (site.parent != null && site.name != "FordMainContract") site = site.parent;
        Require(site.name == "FordMainContract", "Ford's audited site hierarchy is missing.");
        foreach (Transform point in site.GetComponentsInChildren<Transform>(true)) Protect(result, point);
        Protect(result, ford.transform);
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
        Undo.RecordObject(tab, "Register Material Stress lesson");
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
        Undo.RegisterCreatedObjectUndo(created, "Create Ford story helper");
        SceneManager.MoveGameObjectToScene(created, scene);
        if (parent != null) Undo.SetTransformParent(created.transform, parent, "Parent Ford story helper");
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
        phaseId = id, targetLocation = target, dialoguePrompt = "Talk to Ford", phaseDialogue = Dialogue(),
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
    private static BuildLocation Location(Scene scene) => All<BuildLocation>(scene).Single(location => location.name == "workbenchFord" &&
        location.GetComponentsInParent<Transform>(true).Any(t => t.name == "FordMainContract"));
    private static Transform Upper(List<Point> points) => points.OrderByDescending(p => p.transform.position.y).First().transform;
    private static GameObject StoryRoot(Scene scene) => scene.GetRootGameObjects().SingleOrDefault(root => root.name == StoryRootName);
    private static CinematicDirector CinematicTemplate(Scene scene) => All<CinematicDirector>(scene).Single(d => d.cinematicID == "MarshCutScene");
    private static string Hierarchy(Transform target) => target.parent == null ? target.name : Hierarchy(target.parent) + "/" + target.name;
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Missing Ford story asset: " + path);
    private static ContractSO Contract() => Load<ContractSO>(Data + "Contract/TownRiver/Ford Contract.asset");
    private static ContractSO MarshContract() => Load<ContractSO>(Data + "Contract/TownRiver/Marsh Contract.asset");
    private static LessonData LessonAsset() => Load<LessonData>(Data + "Lessons/MaterialStress.asset");
    private static QuizData Quiz() => Load<QuizData>(Data + "Quized/MaterialStressQuiz.asset");
    private static Dialogue Dialogue(params string[] sentences) => new Dialogue { name = "Ford", sentences = sentences };
    private static NPCOptionalDialogueStep Say(params string[] sentences) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.Dialogue, dialogue = Dialogue(sentences) };
    private static NPCOptionalDialogueStep Lesson() => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = LessonAsset() };
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void WriteReport(string text) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, text); }
}
#endif
