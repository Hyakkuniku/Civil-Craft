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

/// <summary>Explicit, undoable Ronan story authoring. Never saves or switches scenes.</summary>
[InitializeOnLoad]
public static class RonanStoryAuthoring
{
    public const string RequestPath = "Temp/ronan-story-v1.request";
    public const string ReportPath = "Logs/ronan-story-v1.txt";
    public const string ReviewPhaseId = "RonanReview";
    public const string QuizStepId = "ronan-shear-assessment-v1";
    public const string StoryRootName = "Ronan Story";
    public const string CinematicId = "RonanSouthernConnection-v1";
    private const string FollowLessonId = "Story/Ronan/FollowToSite-v1";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private const string VehiclePrefabGuid = "8ec4d71c4aff70e409b32509171bc20a";
    private const long VehicleSourceId = 1369226356499901863;
    private static readonly string[] PhaseIds =
        { "MeetingRonan", "ExplainSituation", "LessonIntroduction", "ProjectBriefing", ReviewPhaseId, "Resolved" };
    private static readonly FieldInfo PhasesField = typeof(NPCProgressionManager)
        .GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
    private static double nextPoll;

    static RonanStoryAuthoring() { EditorApplication.update += Poll; }

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
            else if (command == "inspect" || command == "inspector") InspectLoaded();
            else throw new InvalidOperationException("Unknown Ronan story request: " + command);
        }
        catch (Exception error) { WriteReport("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Ronan/1. Apply to Saved Canyon (Do Not Save)")]
    public static void ApplySavedStory()
    {
        Scene scene = LoadedCanyon();
        Require(!scene.isDirty, "Save your current CanyonCrossing edits first. No scene changes were applied or saved.");
        Preflight(scene);
        NPCProgressionManager ronan = NPC(scene, "RonanNpc");
        if (Phases(ronan).Count != 0) { ValidateLoaded(); return; }
        BuildLocation location = Location(scene);
        LiveLoadVehicle vehicle = AuditedVehicle(scene);
        Vector3 start = Upper(location.startingAnchors).position, end = Upper(location.endingAnchors).position;
        Vector3 direction = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 sitePosition = SafeSitePosition(ronan, location, start - direction * 7f + side * 4f);
        var protectedState = CaptureProtectedState(scene, ronan, location, vehicle);
        string workbenchState = WorkbenchState(location), vehicleState = VehicleState(vehicle);
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-RonanStory-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, Path.Combine(backup, "CanyonCrossing.unity"));
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Author Ronan Shear Story");
        try
        {
            var report = new StringBuilder("Ronan - The Southern Connection\n");
            ApplyCanyon(scene, ronan, location, sitePosition, start, end, direction, side, report);
            if (vehicle.assignedContract == BrooksContract())
            {
                Undo.RecordObject(vehicle, "Repair Ronan vehicle contract assignment only");
                vehicle.assignedContract = Contract();
                PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle);
                EditorUtility.SetDirty(vehicle);
                report.AppendLine("Repaired only the audited RonanCar LiveLoadVehicle contract reference, Brooks -> Ronan. Route, mass and finish trigger unchanged.");
            }
            ValidateCanyon(scene, report);
            foreach (var entry in protectedState)
                Require(entry.Key != null && EditorJsonUtility.ToJson(entry.Key) == entry.Value,
                    "An unrelated NPC, gate, camera, cinematic, vehicle or authored transform changed. Migration rolled back.");
            Require(WorkbenchState(location) == workbenchState && VehicleState(vehicle) == vehicleState,
                "Ronan's workbench or vehicle changed outside its permitted contract assignment. Migration rolled back.");
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            report.AppendLine("PASS: prior Marsh/Ford/Brooks progression, Ronan's Brooks visibility gate, existing cameras, vehicle routes and bridge geometry preserved.");
            report.AppendLine("Budget/load remain editable placeholders; no balance, traffic, side quests, region unlock, hints, callouts or new shear physics added.");
            report.AppendLine("Recoverable pre-migration scene: " + backup);
            report.AppendLine("NEEDS_SAVE: CanyonCrossing. Review, then save this scene in Unity.");
            WriteReport(report.ToString());
            Debug.Log(report.ToString());
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Ronan/2. Validate Loaded Canyon")]
    public static void ValidateLoaded()
    {
        Scene scene = LoadedCanyon();
        var report = new StringBuilder("Ronan saved/loaded story validation\n");
        ValidateCanyon(scene, report);
        report.AppendLine("NEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    [MenuItem("Tools/Civil Craft/Story/Ronan/3. Inspect Existing Site (Read Only)")]
    public static void InspectLoaded()
    {
        Scene scene = LoadedCanyon();
        var report = new StringBuilder("Ronan site inspection (read only; no assignment precondition)\n");
        foreach (LiveLoadVehicle vehicle in All<LiveLoadVehicle>(scene))
            report.AppendLine("VEHICLE: " + Hierarchy(vehicle.transform) + "; instance " + vehicle.GetInstanceID() +
                "; contract " + AssetIdentity(vehicle.assignedContract) + "; source " + SourceIdentity(vehicle) +
                "; start " + (vehicle.startPoint != null ? vehicle.startPoint.position.ToString() : "missing") +
                "; end " + (vehicle.endPoint != null ? vehicle.endPoint.position.ToString() : "missing"));
        foreach (FinishLineTrigger finish in All<FinishLineTrigger>(scene))
            report.AppendLine("FINISH: " + Hierarchy(finish.transform) + "; instance " + finish.GetInstanceID() +
                "; contract " + AssetIdentity(finish.assignedContract) + "; source " + SourceIdentity(finish));
        try
        {
            NPCProgressionManager ronan = NPC(scene, "RonanNpc");
            BuildLocation location = Location(scene);
            Vector3 start = Upper(location.startingAnchors).position, end = Upper(location.endingAnchors).position;
            report.AppendLine("NPC: " + Hierarchy(ronan.transform) + "; spawn " + ronan.transform.position + "; phases " + Phases(ronan).Count);
            report.AppendLine("BUILD: " + Hierarchy(location.transform) + "; contract " + AssetIdentity(location.activeContract));
            report.AppendLine("Upper anchors: " + start + " -> " + end + "; physical span " + Vector3.Distance(start, end) +
                "; elevation difference " + (end.y - start.y) + "; contract span " + Contract().bridgeSpan);
            if (Mathf.Abs(Vector3.Distance(start, end) - Contract().bridgeSpan) > .2f)
                report.AppendLine("CONTENT_REVIEW: contract wording/span and physical site differ. No geometry, road height or contract asset changed.");
            Vector3 direction = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
            Vector3 sitePosition = SafeSitePosition(ronan, location, start - direction * 7f + Vector3.Cross(Vector3.up, direction) * 4f);
            report.AppendLine("Safe NPC marker: " + sitePosition);
            NavMeshAgent agent = ronan.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            Transform authoredSpawn = new SerializedObject(ronan).FindProperty("authoredSpawnLocation").objectReferenceValue as Transform;
            Vector3 spawnPosition = authoredSpawn != null ? authoredSpawn.position : ronan.transform.position;
            if (!NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, 3f, filter) ||
                Vector3.ProjectOnPlane(hit.position - spawnPosition, Vector3.up).magnitude > 3f || Mathf.Abs(hit.position.y - spawnPosition.y) > 2f)
                report.AppendLine("ROUTE_UNAVAILABLE: no valid spawn sample within 3 m horizontally/2 m vertically. No auto-bake or teleport.");
            else
            {
                NavMeshPath route = new NavMeshPath();
                bool calculated = NavMesh.CalculatePath(hit.position, sitePosition, filter, route);
                float length = 0;
                for (int i = 1; i < route.corners.Length; i++) length += Vector3.Distance(route.corners[i - 1], route.corners[i]);
                report.AppendLine("Ronan authored-spawn route: sample " + hit.position + "; calculated " + calculated + "; status " + route.status +
                    "; corners " + route.corners.Length + "; length " + length.ToString("0.00") + " m.");
                if (!calculated || route.status != NavMeshPathStatus.PathComplete)
                    report.AppendLine("ROUTE_REVIEW_REQUIRED: current NavMesh does not prove a complete walk. No wider sampling or substitute route.");
            }
        }
        catch (Exception error) { report.AppendLine("INSPECTION_LIMIT: " + error.Message); }
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Preflight(Scene scene)
    {
        NPCProgressionManager ronan = NPC(scene, "RonanNpc");
        List<NPCProgressionPhase> phases = Phases(ronan);
        Require(phases != null && (phases.Count == 0 || phases.Select(p => p?.phaseId).SequenceEqual(PhaseIds)),
            "Ronan has custom phase IDs/order. They were not overwritten.");
        Require(PrefabUtility.IsPartOfPrefabInstance(ronan), "Ronan must remain his original prefab instance, not an unpacked substitute.");
        ContractSO contract = Contract();
        Require(contract.ContractID == "RonanMainContract" && contract.contractMap == ContractSO.ContractMap.TownRiver &&
            Positive(contract.budget) && Positive(contract.liveLoadWeight) && Mathf.Approximately(contract.bridgeSpan, 100f) &&
            contract.goldReward == 500 && contract.expReward == 100 && !contract.autoCollectReward &&
            contract.hideFromLeaderboard && !contract.countsTowardMapAchievements,
            "Ronan contract identity/rewards/unpublished flags differ. Budget and load remain author-editable positive values.");
        Require(contract.featureUnlockRewards == null || contract.featureUnlockRewards.Count == 0,
            "No new Industrial Zone feature reward is implemented.");
        Require(contract.offerDialogue != null && contract.offerDialogue.sentences.Length == 4 &&
            contract.reminderDialogue != null && contract.reminderDialogue.sentences.Length == 1 &&
            contract.finishedContractDialogue != null && contract.finishedContractDialogue.sentences.Length == 5,
            "Ronan's existing briefing/reminder/finale dialogue count differs.");
        BuildLocation location = Location(scene);
        Require(location.startingAnchors.Count == 1 && location.endingAnchors.Count == 1 &&
            location.startingAnchors.All(p => p != null) && location.endingAnchors.All(p => p != null), "Ronan's audited anchor references are missing.");
        Vector3 start = Upper(location.startingAnchors).position, end = Upper(location.endingAnchors).position;
        Require(Mathf.Abs(Vector3.Distance(start, end) - 70f) < .2f && Mathf.Abs(end.y - start.y) < .1f,
            "The existing 70-metre/equal-elevation site changed. No geometry will be substituted.");
        Require(location.activeContract == null || location.activeContract == contract || location.activeContract.name == "NewContract",
            "Ronan's workbench has a different authored contract. It was not replaced.");
        LiveLoadVehicle vehicle = AuditedVehicle(scene);
        Require(vehicle.assignedContract == contract || vehicle.assignedContract == BrooksContract(),
            "The audited RonanCar has a different contract. Only the known Brooks-to-Ronan assignment is repairable.");
        Require(All<FinishLineTrigger>(scene).Count(f => f.assignedContract == contract) == 1,
            "The existing Ronan finish trigger must already reference his contract exactly once.");
        Require(IsGate(ronan.GetComponent<NPCSpawnerCondition>(), ronan.gameObject, BrooksContract(), "BrooksNpc"),
            "Ronan's existing Brooks completion/Resolved visibility gate differs. It was not replaced.");
        CinematicDirector template = CinematicTemplate(scene);
        Require(template.cinematicCamera != null && template.playerActor != null && template.playerAnimator != null,
            "Marsh's existing cinematic camera/actor references are unavailable. No replacement camera was created.");
        Require(All<LessonUIManager>(scene).Any() && All<AlmanacLessonTab>(scene).Any(), "Existing lesson/Almanac UI is required.");
        Require(LessonAsset() != null && Quiz().assessmentMode && Quiz().QuestionCount == 5 && Quiz().TryValidate(out _),
            "Shear requires a valid five-question assessment accepting any submitted score.");
        Require(phases.Count != 0 || StoryRoot(scene) == null, "An existing Ronan Story root needs manual review before migration.");
    }

    private static void ApplyCanyon(Scene scene, NPCProgressionManager ronan, BuildLocation location,
        Vector3 sitePosition, Vector3 start, Vector3 end, Vector3 direction, Vector3 side, StringBuilder report)
    {
        GameObject root = NewObject(scene, StoryRootName, null);
        Transform spawn = Marker(scene, root.transform, "Spawn", ronan.transform.position, ronan.transform.rotation);
        Transform site = Marker(scene, root.transform, "Site", sitePosition, Quaternion.LookRotation(direction, Vector3.up));
        TutorialSequence guide = Undo.AddComponent<TutorialSequence>(NewObject(scene, "Follow Ronan Guide", root.transform));
        guide.lessonName = FollowLessonId;
        guide.requiredPreviousLesson = string.Empty;
        guide.playOnStart = false;
        guide.nextSequence = null;
        guide.autoStartNextSequence = false;
        guide.tutorialSteps = new[] { new TutorialStep { message = "Follow Ronan to the southern crossing.",
            screenPosition = TutorialPosition.Left, showNextButton = false, canSkip = true,
            stepWaypoints = new List<GuiderWaypoint> { new GuiderWaypoint { target = site, advancesTutorial = true } } } };
        EditorUtility.SetDirty(guide);
        GameObject movieRoot = NewObject(scene, "Southern Connection Cinematic", root.transform);
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
            Shot(scene, movieRoot.transform, "Southern approach", start - direction * 22f + side * 38f + Vector3.up * 25f,
                start - direction * 12f + side * 33f + Vector3.up * 22f, start + direction * 9f),
            Shot(scene, movieRoot.transform, "Existing opposite road", end + direction * 10f + side * 43f + Vector3.up * 25f,
                end + direction * 5f + side * 38f + Vector3.up * 22f, end - direction * 8f),
            Shot(scene, movieRoot.transform, "Bridge site and banks", middle + side * 82f + Vector3.up * 46f,
                middle + side * 73f + Vector3.up * 41f, middle + Vector3.up * 2f)
        };
        report.AppendLine("Three animated views show the existing site/approaches only. No higher cliff, industrial skyline or road geometry was invented.");
        EditorUtility.SetDirty(movie);
        NPCProgressionPhase meeting = Phase("MeetingRonan", null, false);
        meeting.phaseDialogue = Dialogue("So, you're {PlayerName}. Good to finally meet you.",
            "I'm " + Em("Ronan") + ". I oversee some of Rivermere's larger infrastructure projects.",
            "I've seen the difference your bridges have made around town.",
            "There's one last project I'd like you to take on before you go farther ahead. Come, I'll show you.");
        UnityEventTools.AddPersistentListener(meeting.onDialogueFinished, guide.StartTutorial);
        meeting.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = ronan, phaseId = "ExplainSituation" };
        NPCProgressionPhase explain = Phase("ExplainSituation", site, false);
        explain.phaseDialogue = Dialogue("Take a look at the road across the waterway.");
        UnityEventTools.AddPersistentListener(explain.onNPCInteracted, guide.Skip);
        UnityEventTools.AddPersistentListener(explain.onDialogueFinished, movie.PlayCinematic);
        NPCProgressionPhase introduction = Phase("LessonIntroduction", site, true);
        introduction.dialoguePrompt = "Continue Conversation";
        introduction.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("This is where Rivermere's road ends. The road across that gap leads toward " + Em("Ironhaven") + ".",
                "There's another way to reach the " + Em("Industrial Zone") + ", but it takes vehicles on a much longer route.",
                "A bridge here would give us a direct connection.",
                "The challenge is that the " + Em("opposite road sits higher") + ", and we'll need to accommodate heavier vehicles."),
            Say("Before you begin, there's one more structural concept worth understanding. It's called " + Em("Shear") + ".",
                "Shear is an internal force that resists nearby parts of a structural member sliding past each other.",
                Em("It can become significant near the supports, depending on the load and how the bridge is supported") + ".",
                "Keep that in mind when you test your design."),
            Lesson()
        };
        introduction.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = ronan, phaseId = "ProjectBriefing" };
        NPCProgressionPhase briefing = Phase("ProjectBriefing", site, true);
        briefing.contract = Contract();
        briefing.targetBuildLocation = location;
        NPCProgressionPhase review = Phase(ReviewPhaseId, site, true);
        review.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Lesson(),
            Say("You can review the " + Em("Shear lesson in your Almanac") + " before starting your quiz.",
                "When you're ready, open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + "."),
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = Quiz(), quizStepId = QuizStepId,
                quizWaitingReminder = "Take your time reviewing Shear in the Almanac. Open the Quiz tab and press Start Quiz when you're ready." }
        };
        review.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = ronan, phaseId = "Resolved" };
        NPCProgressionPhase resolved = Phase("Resolved", site, false);
        resolved.showExclamationMark = false;
        resolved.phaseDialogue = Dialogue("You've made a real difference here in Rivermere. Take care out there, {PlayerName}.");
        Undo.RecordObject(ronan, "Configure Ronan story phases");
        PhasesField.SetValue(ronan, new List<NPCProgressionPhase> { meeting, explain, introduction, briefing, review, resolved });
        SerializedObject serialized = new SerializedObject(ronan);
        serialized.FindProperty("authoredSpawnLocation").objectReferenceValue = spawn;
        serialized.ApplyModifiedProperties();
        // The manager is inherited from NPC_TEMPLATE; dirtiness alone does not persist its overrides.
        PrefabUtility.RecordPrefabInstancePropertyModifications(ronan);
        EditorUtility.SetDirty(ronan);
        RonanPostContractContinuation continuation = Undo.AddComponent<RonanPostContractContinuation>(ronan.gameObject);
        SerializedObject continuationData = new SerializedObject(continuation);
        continuationData.FindProperty("npc").objectReferenceValue = ronan;
        continuationData.FindProperty("contract").objectReferenceValue = Contract();
        continuationData.FindProperty("reviewPhaseId").stringValue = ReviewPhaseId;
        continuationData.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(continuation);
        UnityEventTools.AddPersistentListener(movie.OnCinematicFinished, continuation.ContinueAfterSituationCinematic);
        EditorUtility.SetDirty(movie);
        if (location.activeContract != null && location.activeContract != Contract())
        {
            Undo.RecordObject(location, "Clear Ronan workbench placeholder contract");
            location.activeContract = null;
            EditorUtility.SetDirty(location);
        }
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene)) RegisterLesson(tab);
    }

    private static void ValidateCanyon(Scene scene, StringBuilder report)
    {
        Preflight(scene);
        NPCProgressionManager ronan = NPC(scene, "RonanNpc");
        List<NPCProgressionPhase> phases = Phases(ronan);
        Require(phases.Select(p => p.phaseId).SequenceEqual(PhaseIds), "Ronan's six story phases have not been applied.");
        GameObject root = StoryRoot(scene);
        Require(root != null && root.transform.parent == null && root.activeSelf, "Missing or inactive Ronan Story root.");
        Transform spawn = root.transform.Find("Spawn"), site = root.transform.Find("Site");
        Require(spawn != null && site != null && new SerializedObject(ronan).FindProperty("authoredSpawnLocation").objectReferenceValue == spawn,
            "Ronan requires independent authored-spawn and safe site markers.");
        Require(phases[0].targetLocation == null && phases.Skip(1).All(p => p.targetLocation == site) &&
            phases[0].phaseDialogue.sentences.Length == 4 && phases[1].phaseDialogue.sentences.Length == 1,
            "Ronan's meeting/site/explanation differs.");
        Require(phases[0].onDialogueFinishedPhaseMove.npc == ronan && phases[0].onDialogueFinishedPhaseMove.phaseId == "ExplainSituation" &&
            phases[2].onDialogueFinishedPhaseMove.npc == ronan && phases[2].onDialogueFinishedPhaseMove.phaseId == "ProjectBriefing" &&
            phases[4].onDialogueFinishedPhaseMove.npc == ronan && phases[4].onDialogueFinishedPhaseMove.phaseId == "Resolved", "Ronan's continuation links differ.");
        Require(phases[2].optionalDialogueSequence.Select(s => (int)s.stepType).SequenceEqual(new[] { 0, 0, 3 }) &&
            phases[2].optionalDialogueSequence[0].dialogue.sentences.Length == 4 && phases[2].optionalDialogueSequence[1].dialogue.sentences.Length == 4 &&
            phases[2].optionalDialogueSequence[2].lesson == LessonAsset(), "Ronan's situation/Shear lesson order differs.");
        Require(phases[3].contract == Contract() && phases[3].targetBuildLocation == Location(scene) &&
            phases.Where((p, i) => i != 3).All(p => p.contract == null), "Only Ronan's existing workbench may be bound to ProjectBriefing.");
        Require(phases[4].optionalDialogueSequence.Select(s => (int)s.stepType).SequenceEqual(new[] { 3, 0, 2 }) &&
            phases[4].optionalDialogueSequence[0].lesson == LessonAsset() && phases[4].optionalDialogueSequence[2].quiz == Quiz() &&
            phases[4].optionalDialogueSequence[2].quizStepId == QuizStepId && phases[4].playDialogueOnArrival &&
            !phases[5].playDialogueOnArrival && phases[5].repeatDialogue && !phases[5].showExclamationMark,
            "Post-collection review/assessment or terminal idle differs.");
        Require(phases.All(p => string.IsNullOrEmpty(p.unlockFeatureIdAfterDialogue) && !p.showFeatureCollectPopup && p.cinematicAfterOffer == null),
            "Ronan must not grant default features, an Industrial Zone unlock or a stale offer cinematic.");
        CinematicDirector movie = root.GetComponentsInChildren<CinematicDirector>(true).Single();
        Require(movie.cinematicID == CinematicId && !movie.playOnlyOnce && !movie.playOnStart && movie.contractToDisplay == Contract() &&
            movie.cinematicCamera == CinematicTemplate(scene).cinematicCamera && movie.shots.Count == 3 &&
            movie.shots.All(s => s.cameraStartPoint != null && s.cameraEndPoint != null && s.duration > 0 &&
                s.playerStartPoint == null && s.playerWalkTarget == null && !s.playWalkAnimation), "Ronan's cinematic camera/animated views differ.");
        RonanPostContractContinuation continuation = ronan.GetComponent<RonanPostContractContinuation>();
        Require(continuation != null && Calls(phases[1].onDialogueFinished, movie, "PlayCinematic") &&
            Calls(movie.OnCinematicFinished, continuation, "ContinueAfterSituationCinematic"), "Ronan's cinematic continuation is not guarded.");
        TutorialSequence guide = root.GetComponentsInChildren<TutorialSequence>(true).Single();
        Require(guide.lessonName == FollowLessonId && !guide.playOnStart && !guide.autoStartNextSequence && guide.nextSequence == null &&
            guide.tutorialSteps.Length == 1 && guide.tutorialSteps[0].canSkip && !guide.tutorialSteps[0].showNextButton &&
            guide.tutorialSteps[0].stepWaypoints.Single().target == site && Calls(phases[0].onDialogueFinished, guide, "StartTutorial") &&
            Calls(phases[1].onNPCInteracted, guide, "Skip"), "Ronan's optional follow guide must close before the site conversation.");
        SerializedObject saved = new SerializedObject(continuation);
        Require(saved.FindProperty("npc").objectReferenceValue == ronan && saved.FindProperty("contract").objectReferenceValue == Contract() &&
            saved.FindProperty("reviewPhaseId").stringValue == ReviewPhaseId, "Ronan continuation has stale references.");
        Require(AuditedVehicle(scene).assignedContract == Contract() && All<LiveLoadVehicle>(scene).Count(v => v.assignedContract == Contract()) == 1,
            "Ronan's existing test vehicle must use his contract exactly once.");
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene))
        {
            SerializedProperty list = new SerializedObject(tab).FindProperty("allLessons");
            int count = 0;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == LessonAsset()) count++;
            Require(count == 1, "Shear must appear exactly once in each Almanac lesson archive.");
        }
        report.AppendLine("PASS: Meeting Ronan > follow/site > animated cinematic > Shear lesson > normal acceptance at original workbench.");
        report.AppendLine("PASS: durable Collect > Shear review > five-question assessment, any score/no rewards > terminal Resolved.");
        report.AppendLine("PASS: RonanMainContract, 500 coins/100 XP, draft flags and existing Brooks completion/Resolved gate retained.");
        report.AppendLine("PLANNED_LAYOUT: document/contract keep the future 100-metre, different-elevation crossing by author choice; current physical anchors remain 70 metres at equal height. Site redesign is not part of this migration.");
    }

    private static Vector3 SafeSitePosition(NPCProgressionManager ronan, BuildLocation location, Vector3 desired)
    {
        NavMeshAgent agent = ronan.GetComponent<NavMeshAgent>();
        Require(agent != null, "Ronan requires his existing NavMeshAgent.");
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        Vector3[] candidates = location.navigationTarget != null && Vector3.Distance(location.navigationTarget.transform.position, desired) <= 8f
            ? new[] { desired, location.navigationTarget.transform.position } : new[] { desired };
        foreach (Vector3 candidate in candidates)
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, filter) &&
                Vector3.ProjectOnPlane(hit.position - candidate, Vector3.up).magnitude <= 3f && Mathf.Abs(hit.position.y - candidate.y) <= 2f)
                return hit.position;
        throw new InvalidOperationException("No safe Ronan site NavMesh point within 3 m horizontally/2 m vertically. No marker, auto-bake or teleport.");
    }

    private static Dictionary<UnityEngine.Object, string> CaptureProtectedState(Scene scene, NPCProgressionManager ronan,
        BuildLocation location, LiveLoadVehicle repairableVehicle)
    {
        var result = new Dictionary<UnityEngine.Object, string>();
        foreach (NPCProgressionManager npc in All<NPCProgressionManager>(scene)) if (npc != ronan) Protect(result, npc);
        foreach (NPCSpawnerCondition gate in All<NPCSpawnerCondition>(scene)) Protect(result, gate);
        foreach (Camera camera in All<Camera>(scene)) Protect(result, camera);
        foreach (CinematicDirector movie in All<CinematicDirector>(scene))
        {
            Protect(result, movie);
            foreach (CinematicShot shot in movie.shots) { Protect(result, shot.cameraStartPoint); Protect(result, shot.cameraEndPoint); }
        }
        foreach (BuildLocation build in All<BuildLocation>(scene)) if (build != location) Protect(result, build);
        foreach (LiveLoadVehicle vehicle in All<LiveLoadVehicle>(scene))
        {
            if (vehicle != repairableVehicle) Protect(result, vehicle);
            Protect(result, vehicle.startPoint); Protect(result, vehicle.endPoint);
            foreach (Transform part in vehicle.GetComponentsInChildren<Transform>(true)) Protect(result, part);
        }
        foreach (FinishLineTrigger finish in All<FinishLineTrigger>(scene)) { Protect(result, finish); Protect(result, finish.transform); }
        Transform site = location.transform;
        while (site.parent != null && site.name != "BrookMainContract") site = site.parent;
        Require(site.name == "BrookMainContract", "Ronan's audited site hierarchy is missing.");
        foreach (Transform point in site.GetComponentsInChildren<Transform>(true)) Protect(result, point);
        Protect(result, ronan.transform);
        Protect(result, ronan.GetComponent<NPCContractGiver>());
        Protect(result, ronan.GetComponent<NavMeshAgent>());
        foreach (MarshPostContractContinuation helper in All<MarshPostContractContinuation>(scene)) Protect(result, helper);
        foreach (FordPostContractContinuation helper in All<FordPostContractContinuation>(scene)) Protect(result, helper);
        foreach (BrooksPostContractContinuation helper in All<BrooksPostContractContinuation>(scene)) Protect(result, helper);
        return result;
    }

    private static LiveLoadVehicle AuditedVehicle(Scene scene)
    {
        LiveLoadVehicle vehicle = All<LiveLoadVehicle>(scene).Single(v => v.GetComponentsInParent<Transform>(true).Any(t => t.name == "RonanCar"));
        UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(vehicle);
        Require(source != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id) &&
            guid == VehiclePrefabGuid && id == VehicleSourceId,
            "RonanCar does not use the exact audited inherited LiveLoadVehicle. No vehicle reference was changed.");
        return vehicle;
    }
    private static string AssetIdentity(ContractSO value) => value == null ? "none" : value.name + " / " + value.ContractID +
        " / " + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value));
    private static string SourceIdentity(UnityEngine.Object value)
    {
        UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(value);
        return source != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id) ? guid + "/" + id : "none";
    }
    private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Protect(Dictionary<UnityEngine.Object, string> state, UnityEngine.Object value)
    { if (value != null && !state.ContainsKey(value)) state.Add(value, EditorJsonUtility.ToJson(value)); }
    private static string WorkbenchState(BuildLocation value) => Regex.Replace(EditorJsonUtility.ToJson(value),
        "\"activeContract\"\\s*:\\s*\\{[^}]*\\}", "\"activeContract\":null");
    private static string VehicleState(LiveLoadVehicle value) => Regex.Replace(EditorJsonUtility.ToJson(value),
        "\"assignedContract\"\\s*:\\s*\\{[^}]*\\}", "\"assignedContract\":null");
    private static void RegisterLesson(AlmanacLessonTab tab)
    {
        SerializedObject data = new SerializedObject(tab);
        SerializedProperty list = data.FindProperty("allLessons");
        for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == LessonAsset()) return;
        Undo.RecordObject(tab, "Register Shear lesson");
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
        GameObject value = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(value, "Create Ronan story helper");
        SceneManager.MoveGameObjectToScene(value, scene);
        if (parent != null) Undo.SetTransformParent(value.transform, parent, "Parent Ronan story helper");
        return value;
    }
    private static Transform Marker(Scene scene, Transform parent, string name, Vector3 position, Quaternion rotation)
    { Transform value = NewObject(scene, name, parent).transform; value.SetPositionAndRotation(position, rotation); return value; }
    private static NPCProgressionPhase Phase(string id, Transform target, bool automatic) => new NPCProgressionPhase
    {
        phaseId = id, targetLocation = target, dialoguePrompt = "Talk to Ronan", phaseDialogue = Dialogue(),
        playDialogueOnArrival = automatic, repeatDialogue = true, showExclamationMark = true,
        unlockFeatureIdAfterDialogue = string.Empty, featureUnlockDisplayName = string.Empty,
        showFeatureCollectPopup = false, introduceMaterialOnlyOnce = false,
        onDialogueFinished = new UnityEvent(), onNPCInteracted = new UnityEvent(), onNPCArrived = new UnityEvent(),
        onMaterialIntroductionClosed = new UnityEvent()
    };
    private static bool IsGate(NPCSpawnerCondition gate, GameObject root, ContractSO contract, string npcId) =>
        gate != null && gate.enabled && gate.npcRoot == root && gate.useMultipleConditions && gate.matchMode == NPCSpawnerCondition.MatchMode.All &&
        !gate.hideWhenMatched && !gate.waitForManualSpawn && !gate.persistManualOverride && !gate.useDespawnConditions && gate.updateAutomatically &&
        gate.conditions != null && gate.conditions.Count == 2 && gate.conditions.All(r => r != null && r.mustBeTrue) &&
        gate.conditions.Any(r => r.kind == NPCSpawnerCondition.ConditionKind.ContractCompleted && r.contract == contract) &&
        gate.conditions.Any(r => r.kind == NPCSpawnerCondition.ConditionKind.NPCAtPhase && r.id == npcId && r.phaseId == "Resolved");
    private static bool Calls(UnityEvent callback, UnityEngine.Object target, string method) => callback != null && callback.GetPersistentEventCount() == 1 &&
        callback.GetPersistentTarget(0) == target && callback.GetPersistentMethodName(0) == method && callback.GetPersistentListenerState(0) != UnityEventCallState.Off;
    private static Scene LoadedCanyon()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Leave Unity in Edit Mode with imports finished and the prefab editor closed.");
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing. This tool never changes loaded scenes.");
        return scene;
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static NPCProgressionManager NPC(Scene scene, string id) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == id);
    private static List<NPCProgressionPhase> Phases(NPCProgressionManager npc) => (List<NPCProgressionPhase>)PhasesField.GetValue(npc);
    private static BuildLocation Location(Scene scene) => All<BuildLocation>(scene).Single(location => location.name == "workbenchronan" &&
        location.GetComponentsInParent<Transform>(true).Any(t => t.name == "BrookMainContract"));
    private static Transform Upper(List<Point> points) => points.OrderByDescending(p => p.transform.position.y).First().transform;
    private static GameObject StoryRoot(Scene scene) => scene.GetRootGameObjects().SingleOrDefault(root => root.name == StoryRootName);
    private static CinematicDirector CinematicTemplate(Scene scene) => All<CinematicDirector>(scene).Single(d => d.cinematicID == "MarshCutScene");
    private static string Hierarchy(Transform value) => value.parent == null ? value.name : Hierarchy(value.parent) + "/" + value.name;
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Missing Ronan story asset: " + path);
    private static ContractSO Contract() => Load<ContractSO>(Data + "Contract/TownRiver/Ronan Contract.asset");
    private static ContractSO BrooksContract() => Load<ContractSO>(Data + "Contract/TownRiver/Brooks Contract.asset");
    private static LessonData LessonAsset() => Load<LessonData>(Data + "Lessons/Shear.asset");
    private static QuizData Quiz() => Load<QuizData>(Data + "Quized/ShearQuiz.asset");
    private static Dialogue Dialogue(params string[] sentences) => new Dialogue { name = "Ronan", sentences = sentences };
    private static NPCOptionalDialogueStep Say(params string[] sentences) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.Dialogue, dialogue = Dialogue(sentences) };
    private static NPCOptionalDialogueStep Lesson() => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = LessonAsset() };
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void WriteReport(string value) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, value); }
}
#endif
