#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ClarkStoryAuthoring
{
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private const string RootName = "Clark Freight Passage Story";
    private const string Request = "Temp/clark-freight-story.request";
    private const string Report = "Logs/clark-freight-story.txt";
    private const string QuizStep = "clark-suspension-assessment-v1";
    private const string FollowId = "Story/Clark/FollowToFreightPassage-v1";
    private static readonly string[] PhaseIds = { "MeetingClark", "ExplainSituation", "MaterialIntroduction",
        "LessonIntroduction", "ProjectBriefing", "ClarkFreightReview", "FreightPassageComplete" };
    private static readonly FieldInfo PhaseField = typeof(NPCProgressionManager).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
    private static double nextPoll;

    static ClarkStoryAuthoring() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            if (command == "apply") Apply();
            else if (command == "validate") Validate();
            else if (command == "inspect") InspectSite();
            else throw new InvalidOperationException("Unknown Clark story request.");
        }
        catch (Exception error) { WriteReport("FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Clark/Apply Freight Passage")]
    public static void Apply()
    {
        Scene scene = LoadedCanyon();
        NPCProgressionManager clark = FindClark(scene);
        if (clark.AuthoredPhases.Take(PhaseIds.Length).Select(phase => phase?.phaseId).SequenceEqual(PhaseIds)) { Validate(); return; }
        Require(clark.AuthoredPhases.Count == 0 && !scene.GetRootGameObjects().Any(root => root.name == RootName),
            "Clark already has authored progression. Review those phases before applying Freight Passage.");
        ContractSO contract = Contract();
        BuildLocation location = ClarkFreightSiteAuthoring.FindLocation(scene);
        ClarkHandoffAuthoring.Preflight(scene, clark);
        ClarkFreightSiteAuthoring.Preflight(scene, location, contract);
        var (start, end) = ClarkFreightSiteAuthoring.GetRoadAnchors(location);
        CinematicDirector template = All<CinematicDirector>(scene).Single(movie => movie.cinematicID == "MarshCutScene");
        Require(template.cinematicCamera != null && template.playerActor != null && template.playerAnimator != null,
            "The existing story camera and player actor references are required.");
        Require(All<AlmanacLessonTab>(scene).Any() && All<LessonUIManager>(scene).Any(), "The scene is missing the Almanac or lesson UI.");
        Require(Quiz().assessmentMode && Quiz().QuestionCount == 5 && Quiz().TryValidate(out _), "The five-question suspension quiz is invalid.");
        LessonData lesson = Lesson();
        SimulationLessonDefinition simulationLesson = SimulationLesson();
        BridgeMaterialSO[] materials = { Material("SteelBeam"), Material("SteelPier"), Material("SteelRope") };
        Vector3 from = start.transform.position, to = end.transform.position;
        Vector3 direction = Vector3.ProjectOnPlane(to - from, Vector3.up).normalized;
        var walk = FindWalk(clark, location, from, direction);
        bool originallyDirty = scene.isDirty;
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-Clark-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        File.Copy(Canyon, Path.Combine(backup, "CanyonCrossing.unity"));
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Author Clark Freight Passage");
        var report = new StringBuilder("Clark — Freight Passage\n");
        try
        {
            AuthorPhases(scene, clark, location, contract, lesson, materials, template, from, to, direction, walk);
            ClarkHandoffAuthoring.Apply(scene, clark);
            ClarkFreightSiteAuthoring.Apply(scene, location, contract, report);
            Undo.RecordObject(location, "Assign Clark's suspension simulation lesson");
            location.advancesTutorial = false;
            location.enableSimulationLesson = true;
            location.simulationLesson = simulationLesson;
            Mark(location);
            foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene)) RegisterLesson(tab, lesson);
            Check(scene, clark, location, contract, report);
            report.AppendLine($"Clark's checked {(walk.grounded ? "grounded" : "NavMesh")} route: {walk.spawn:F2} to {walk.site:F2}.");
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            report.AppendLine("BACKUP: " + backup);
            bool saved = !originallyDirty && EditorSceneManager.SaveScene(scene);
            report.AppendLine(saved ? "SAVED: CanyonCrossing" : "NEEDS_SAVE: CanyonCrossing was left open with all existing edits.");
            WriteReport(report.ToString());
            Debug.Log(report.ToString());
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Clark/Validate Freight Passage")]
    public static void Validate()
    {
        Scene scene = LoadedCanyon();
        var report = new StringBuilder("Clark saved/loaded story validation\n");
        Check(scene, FindClark(scene), ClarkFreightSiteAuthoring.FindLocation(scene), Contract(), report);
        report.AppendLine(scene.isDirty ? "NEEDS_SAVE: CanyonCrossing" : "SAVED: CanyonCrossing");
        WriteReport(report.ToString());
        Debug.Log(report.ToString());
    }

    [MenuItem("Tools/Civil Craft/Story/Clark/Inspect Freight Passage Ground")]
    public static void InspectSite()
    {
        Scene scene = LoadedCanyon();
        var clark = FindClark(scene);
        var location = ClarkFreightSiteAuthoring.FindLocation(scene);
        var (start, end) = ClarkFreightSiteAuthoring.GetRoadAnchors(location);
        Physics.SyncTransforms();
        var output = new StringBuilder("Clark site ground inspection\n");
        Sample("Clark", clark.transform.position);
        Sample("Clark 3 m toward site", clark.transform.position + Vector3.right * 3f);
        Sample("Clark on road centerline", new Vector3(clark.transform.position.x, clark.transform.position.y, start.transform.position.z));
        Sample("Clark 6 m left", clark.transform.position + Vector3.forward * 6f);
        Sample("Clark 6 m right", clark.transform.position - Vector3.forward * 6f);
        Sample("Workbench", location.transform.position);
        for (float x = clark.transform.position.x; x <= start.transform.position.x; x += 10f)
            Sample("Walk sample", new Vector3(x, start.transform.position.y, 5f));
        foreach (float offset in new[] { 16f, 8f, 2f, .2f })
        {
            Sample("Start bank", start.transform.position - Vector3.right * offset);
            Sample("End bank", end.transform.position + Vector3.right * offset);
        }
        WriteReport(output.ToString());
        Debug.Log(output.ToString());

        void Sample(string label, Vector3 point)
        {
            string hits = string.Join(", ", Physics.RaycastAll(point + 12f * Vector3.up, Vector3.down, 50f, ~0,
                    QueryTriggerInteraction.Ignore).Where(hit => hit.collider.gameObject.scene == scene).OrderBy(hit => hit.distance)
                .Take(4).Select(hit => $"{hit.collider.name} [{LayerMask.LayerToName(hit.collider.gameObject.layer)}] Y={hit.point.y:F2} normalY={hit.normal.y:F2}"));
            output.AppendLine($"{label} {point:F2}: {(hits.Length == 0 ? "no hits" : hits)}");
        }
    }

    private static void AuthorPhases(Scene scene, NPCProgressionManager clark, BuildLocation location, ContractSO contract,
        LessonData lesson, BridgeMaterialSO[] steel, CinematicDirector template, Vector3 from, Vector3 to, Vector3 direction,
        (Vector3 spawn, Vector3 site, bool grounded, Vector3? via) walk)
    {
        Transform root = NewObject(scene, RootName).transform;
        Transform spawn = Marker(scene, root, "Spawn", walk.spawn, clark.transform.rotation);
        Transform site = Marker(scene, root, "Freight Yard", walk.site, Quaternion.LookRotation(direction, Vector3.up));
        var follow = Objective(scene, root, FollowId, "Follow Clark to the freight yard", site, true);
        var meeting = Phase(PhaseIds[0], null);
        meeting.phaseDialogue = Dialogue("You must be the engineer Ronan told me about. I'm " + Em("Clark") + ".",
            "I coordinate the freight routes around Ironhaven's factories and warehouses.",
            "Ronan mentioned the work you did in Town River. Sounds like you've improved quite a few routes down there.",
            "Things are different here. We have heavier traffic, active factories, and less room to work around.",
            "There's a freight route nearby that needs work. Come with me and I'll show you.");
        MoveAfter(meeting, clark, PhaseIds[1]);
        UnityEventTools.AddPersistentListener(meeting.onDialogueFinished, follow.StartTutorial);

        var explain = Phase(PhaseIds[1], site);
        explain.phaseDialogue = Dialogue("Take a look across the passage.");
        explain.useGroundedWaypoints = walk.grounded;
        if (walk.via.HasValue) explain.travelWaypoints.Add(Marker(scene, root, "Freight walk waypoint", walk.via.Value, Quaternion.identity));
        UnityEventTools.AddPersistentListener(explain.onNPCInteracted, follow.Skip);
        CinematicDirector cinematic = CreateCinematic(scene, root, template, contract, from, to, direction);
        UnityEventTools.AddPersistentListener(explain.onDialogueFinished, cinematic.PlayCinematic);
        var afterTour = Undo.AddComponent<NPCPhaseAction>(cinematic.gameObject);
        var actionData = new SerializedObject(afterTour);
        actionData.FindProperty("npc").objectReferenceValue = clark;
        actionData.FindProperty("phaseId").stringValue = PhaseIds[2];
        actionData.ApplyModifiedProperties();
        UnityEventTools.AddPersistentListener(cinematic.OnCinematicFinished, afterTour.MoveToSelectedPhase);

        var intro = Phase(PhaseIds[2], site, true);
        intro.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("That's one of Ironhaven's freight yards. The warehouses are just across the channel, but the trucks have to go around several factory blocks to reach them.",
                "That detour adds time to every delivery. " + Em("A crossing here would give the freight crews a direct route") + ".",
                "You used wood in CanyonCrossing and concrete in Town River. Here in Ironhaven, we'll use steel for more demanding projects.",
                "A " + Em("Steel Beam") + " supports the structure and transfers loads. It can carry both tension and compression."),
            Unlock(steel[0]),
            Say("A " + Em("Steel Pier") + " adds a support beneath the bridge and carries load into its foundation.",
                "A pier can help support a long span. Where you place it and how it connects to the bridge matter."),
            Unlock(steel[1]),
            Say("The " + Em("Steel Rope") + " works as a suspension cable. It carries tension and helps hold up the deck.",
                "A " + Em("Suspension Bridge") + " is worth considering for a channel this wide."),
            Unlock(steel[2])
        };
        MoveAfter(intro, clark, PhaseIds[3]);

        var teach = Phase(PhaseIds[3], site, true);
        teach.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Say("A " + Em("Suspension Bridge") + " uses main cables supported by towers. Vertical cables called " + Em("hangers") + " connect the main cables to the roadway.",
                "When a truck crosses, the hangers transfer the deck load to the main cables. Those cables mainly carry " + Em("tension") + ".",
                "The towers mainly carry " + Em("compression") + " down to the ground, and the " + Em("anchorages") + " resist the cables' pull.",
                "Keep those connections in mind. A suspension bridge can span a long distance when all its parts work together."),
            Review(lesson)
        };
        MoveAfter(teach, clark, PhaseIds[4]);

        var briefing = Phase(PhaseIds[4], site, true);
        briefing.contract = contract;
        briefing.targetBuildLocation = location;
        var review = Phase(PhaseIds[5], site, true);
        review.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
        {
            Review(lesson),
            Say("You can review the " + Em("Suspension Bridge lesson") + " in your Almanac before the quiz.",
                "When you're ready, go to the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + "."),
            new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = Quiz(), quizStepId = QuizStep,
                quizWaitingReminder = "Review the Suspension Bridge lesson in your Almanac, then press Start Quiz when you're ready." }
        };
        MoveAfter(review, clark, PhaseIds[6]);
        var done = Phase(PhaseIds[6], site);
        done.showExclamationMark = false;
        done.phaseDialogue = Dialogue("The freight crews can take the direct route now. Thanks for your help, {PlayerName}.");

        Undo.RecordObject(clark, "Author Clark's Freight Passage phases");
        PhaseField.SetValue(clark, new List<NPCProgressionPhase> { meeting, explain, intro, teach, briefing, review, done });
        var data = new SerializedObject(clark);
        data.FindProperty("authoredSpawnLocation").objectReferenceValue = spawn;
        data.FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue = true;
        // Ironhaven's existing walkable freight-yard cliff is on Default.
        if (walk.grounded) data.FindProperty("waypointGroundLayers").intValue |= LayerMask.GetMask("Default");
        data.ApplyModifiedProperties();
        Mark(clark);
        var giver = clark.GetComponent<NPCContractGiver>();
        Require(giver != null, "Clark's existing contract giver is missing.");
        Undo.RecordObject(giver, "Keep Clark's contract separate from the active tutorial");
        giver.advancesTutorial = false;
        Mark(giver);
        var guidance = Undo.AddComponent<NPCStoryObjectiveGuidance>(clark.gameObject);
        guidance.entries = new List<NPCStoryObjectiveGuidance.Entry>
        {
            Entry(PhaseIds[0], Objective(scene, root, "Story/Clark/Find-v1", "Find Clark in Ironhaven", clark.transform)),
            Entry(PhaseIds[1], Objective(scene, root, "Story/Clark/DiscussSite-v1", "Talk to Clark at the freight yard", site)),
            Entry(PhaseIds[5], Objective(scene, root, "Story/Clark/Quiz-v1", "Complete Clark's Suspension Bridge quiz", null))
        };
        Mark(guidance);
    }

    private static CinematicDirector CreateCinematic(Scene scene, Transform root, CinematicDirector template, ContractSO contract,
        Vector3 from, Vector3 to, Vector3 direction)
    {
        var cinematic = Undo.AddComponent<CinematicDirector>(NewObject(scene, "Freight Passage Overview", root));
        cinematic.cinematicID = "ClarkFreightPassage-v1";
        cinematic.playOnlyOnce = cinematic.playOnStart = false;
        cinematic.cinematicCamera = template.cinematicCamera;
        cinematic.playerActor = template.playerActor;
        cinematic.playerAnimator = template.playerAnimator;
        cinematic.cameraTransitionDuration = template.cameraTransitionDuration;
        cinematic.cameraReturnDuration = template.cameraReturnDuration;
        cinematic.hudElementsToHide = new List<GameObject>(template.hudElementsToHide ?? new List<GameObject>());
        cinematic.contractToDisplay = contract;
        cinematic.OnCinematicFinished = new UnityEvent();
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        Vector3 mid = (from + to) * .5f;
        cinematic.shots = new List<CinematicShot>
        {
            Shot("Freight yard", from + side * 35f + Vector3.up * 22f, from + side * 30f + Vector3.up * 19f, from - direction * 10f),
            Shot("Warehouses", to + side * 45f + Vector3.up * 30f, to + side * 37f + Vector3.up * 24f, to + direction * 10f),
            Shot("Freight channel", mid + side * 94f + Vector3.up * 52f, mid + side * 88f + Vector3.up * 47f, mid)
        };
        Mark(cinematic);
        return cinematic;

        CinematicShot Shot(string name, Vector3 a, Vector3 b, Vector3 focus) => new CinematicShot
        {
            shotName = name, duration = 3f, postShotDelay = .1f, playWalkAnimation = false,
            cameraStartPoint = Marker(scene, cinematic.transform, name + " Start", a, Quaternion.LookRotation(focus - a)),
            cameraEndPoint = Marker(scene, cinematic.transform, name + " End", b, Quaternion.LookRotation(focus - b))
        };
    }

    private static (Vector3 spawn, Vector3 site, bool grounded, Vector3? via) FindWalk(NPCProgressionManager clark,
        BuildLocation location, Vector3 bank, Vector3 direction)
    {
        Physics.SyncTransforms();
        Vector3 spawn = clark.transform.position;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        Vector3[] destinations = { bank - direction * 12f - side * 7f, bank - direction * 17f - side * 8f,
            location.transform.position - side * 3f, location.transform.position + side * 3f };
        var agent = clark.GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        var route = new NavMeshPath();
        if (NavMesh.SamplePosition(spawn, out var navSpawn, 3f, filter))
            foreach (Vector3 candidate in destinations)
                if (NavMesh.SamplePosition(candidate, out var navEnd, 3f, filter) &&
                    NavMesh.CalculatePath(navSpawn.position, navEnd.position, filter, route) && route.status == NavMeshPathStatus.PathComplete)
                    return (navSpawn.position, navEnd.position, false, null);

        var npcData = new SerializedObject(clark);
        int mask = npcData.FindProperty("waypointGroundLayers").intValue | LayerMask.GetMask("Default");
        Require(Ground(spawn, out var groundSpawn), "Clark's existing spawn has no walkable ground nearby.");
        foreach (Vector3 candidate in destinations)
        {
            if (!Ground(candidate, out var groundEnd)) continue;
            if (Walkable(groundSpawn, groundEnd)) return (groundSpawn, groundEnd, true, null);
            Vector3 midpoint = Vector3.Lerp(groundSpawn, groundEnd, .5f);
            foreach (float offset in new[] { -6f, 6f, -12f, 12f })
                if (Ground(midpoint + side * offset, out var via) && Walkable(groundSpawn, via) && Walkable(via, groundEnd))
                    return (groundSpawn, groundEnd, true, via);
        }
        throw new InvalidOperationException("No continuous route from Clark's spawn to the freight yard could be verified.");

        bool Ground(Vector3 point, out Vector3 result)
        {
            result = point;
            if (!Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out var hit, 4.5f, mask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < .7f) return false;
            result = hit.point;
            return true;
        }

        bool Walkable(Vector3 a, Vector3 b)
        {
            int samples = Mathf.CeilToInt(Vector3.Distance(a, b) / .65f);
            Vector3 previous = a;
            for (int i = 1; i <= samples; i++)
            {
                if (!Ground(Vector3.Lerp(a, b, i / (float)samples), out Vector3 sample) || Mathf.Abs(sample.y - previous.y) > .48f)
                    return false;
                previous = sample;
            }
            return true;
        }
    }

    private static void Check(Scene scene, NPCProgressionManager clark, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        ClarkProgressionTargets.Check(scene, clark, report);
        var phases = clark.AuthoredPhases;
        Require(phases.Take(PhaseIds.Length).Select(p => p?.phaseId).SequenceEqual(PhaseIds) && (phases.Count == 7 || ClarkOffsetRoadAuthoring.HasOffsetRoad(clark)), "Clark's Freight Passage phase order is incorrect.");
        Require(phases[4].contract == contract && phases[4].targetBuildLocation == location && phases.Take(7).Count(p => p.contract != null) == 1,
            "Clark must offer exactly the Freight Passage contract at his existing workbench.");
        Require(phases[2].optionalDialogueSequence.Where(step => step.stepType == NPCOptionalDialogueStep.StepType.MaterialUnlock)
            .Select(step => step.material.Id).SequenceEqual(new[] { "SteelBeam", "SteelPier", "SteelRope" }), "The steel material introductions are incomplete.");
        var quiz = phases[5].optionalDialogueSequence.Single(step => step.stepType == NPCOptionalDialogueStep.StepType.Quiz);
        Require(quiz.quiz == Quiz() && quiz.quizStepId == QuizStep && phases[5].onDialogueFinishedPhaseMove.phaseId == PhaseIds[6],
            "The post-turn-in review must finish with the five-question suspension quiz.");
        Require(clark.GetComponent<NPCStoryObjectiveGuidance>().entries.Count >= 3 && location.enableSimulationLesson &&
            location.simulationLesson == SimulationLesson(), "Clark's objectives or simulation lesson are incomplete.");
        foreach (AlmanacLessonTab tab in All<AlmanacLessonTab>(scene))
        {
            var lessons = new SerializedObject(tab).FindProperty("allLessons");
            Require(Enumerable.Range(0, lessons.arraySize).Count(i => lessons.GetArrayElementAtIndex(i).objectReferenceValue == Lesson()) == 1,
                "The Suspension Bridge lesson must be registered once in the Almanac.");
        }
        ClarkHandoffAuthoring.Validate(scene, clark);
        ClarkFreightSiteAuthoring.Validate(scene, location, contract, report);
        report.AppendLine("PASS: Ronan handoff, Clark phases, three steel introductions, Suspension Bridge lesson and assessment, and objective wiring.");
    }

    private static void RegisterLesson(AlmanacLessonTab tab, LessonData lesson)
    {
        var data = new SerializedObject(tab);
        var lessons = data.FindProperty("allLessons");
        for (int i = 0; i < lessons.arraySize; i++)
            if (lessons.GetArrayElementAtIndex(i).objectReferenceValue == lesson) return;
        lessons.GetArrayElementAtIndex(lessons.arraySize++).objectReferenceValue = lesson;
        data.ApplyModifiedProperties();
        Mark(tab);
    }

    private static TutorialSequence Objective(Scene scene, Transform root, string id, string title, Transform target, bool follow = false)
    {
        var sequence = Undo.AddComponent<TutorialSequence>(NewObject(scene, title, root));
        sequence.lessonName = id;
        sequence.showInObjectives = true;
        sequence.objectiveTitle = title;
        sequence.ConfigureObjectiveIdentity(id);
        sequence.playOnStart = false;
        sequence.tutorialSteps = new[] { new TutorialStep { message = title + ".", screenPosition = TutorialPosition.Left,
            showNextButton = false, canSkip = false, stepWaypoints = target == null ? new List<GuiderWaypoint>() :
                new List<GuiderWaypoint> { new GuiderWaypoint { target = target, advancesTutorial = follow } } } };
        Mark(sequence);
        return sequence;
    }

    private static NPCStoryObjectiveGuidance.Entry Entry(string phase, TutorialSequence objective) =>
        new NPCStoryObjectiveGuidance.Entry { phaseId = phase, objective = objective };
    private static NPCProgressionPhase Phase(string id, Transform target, bool automatic = false) => new NPCProgressionPhase
    {
        phaseId = id, targetLocation = target, dialoguePrompt = "Talk to Clark", phaseDialogue = Dialogue(),
        playDialogueOnArrival = automatic, repeatDialogue = true, showExclamationMark = true,
        unlockFeatureIdAfterDialogue = string.Empty, featureUnlockDisplayName = string.Empty, showFeatureCollectPopup = false,
        introduceMaterialOnlyOnce = false, materialIntroductionButtonLabel = "CONTINUE",
        onDialogueFinished = new UnityEvent(), onNPCInteracted = new UnityEvent()
    };
    private static void MoveAfter(NPCProgressionPhase phase, NPCProgressionManager npc, string id) =>
        phase.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = npc, phaseId = id };
    private static Dialogue Dialogue(params string[] lines) => new Dialogue { name = "Clark", sentences = lines };
    private static NPCOptionalDialogueStep Say(params string[] lines) => new NPCOptionalDialogueStep { dialogue = Dialogue(lines) };
    private static NPCOptionalDialogueStep Unlock(BridgeMaterialSO material) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.MaterialUnlock, material = material, materialButtonLabel = "CONTINUE" };
    private static NPCOptionalDialogueStep Review(LessonData lesson) => new NPCOptionalDialogueStep
        { stepType = NPCOptionalDialogueStep.StepType.LessonIntroduction, lesson = lesson };
    private static string Em(string text) => "<b><#E09500>" + text + "</color></b>";

    private static NPCProgressionManager FindClark(Scene scene) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == "Clark");
    private static ContractSO Contract() => Load<ContractSO>(Data + "Contract/IndustrialZone/Clark Contract.asset");
    private static LessonData Lesson() => Load<LessonData>(Data + "Lessons/Suspension Bridge.asset");
    private static QuizData Quiz() => Load<QuizData>(Data + "Quized/SuspensionBridgeQuiz.asset");
    private static SimulationLessonDefinition SimulationLesson() => Load<SimulationLessonDefinition>(Data + "Simulation Lessons/Suspension Bridge Lesson.asset");
    private static BridgeMaterialSO Material(string id) => Load<BridgeMaterialSO>(Data + "Resources/" + id + ".asset");
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Missing Clark asset: " + path);
    private static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static Scene LoadedCanyon()
    {
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        Require(scene.IsValid() && scene.isLoaded && !EditorApplication.isPlayingOrWillChangePlaymode &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Open CanyonCrossing in Edit Mode to apply or validate Clark's story.");
        return scene;
    }
    private static GameObject NewObject(Scene scene, string name, Transform parent = null)
    {
        var value = new GameObject(name);
        SceneManager.MoveGameObjectToScene(value, scene);
        Undo.RegisterCreatedObjectUndo(value, "Create Clark story object");
        if (parent != null) Undo.SetTransformParent(value.transform, parent, "Parent Clark story object");
        return value;
    }
    private static Transform Marker(Scene scene, Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        Transform result = NewObject(scene, name, parent).transform;
        result.SetPositionAndRotation(position, rotation);
        return result;
    }
    private static void Mark(UnityEngine.Object value)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        EditorUtility.SetDirty(value);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void WriteReport(string message) { Directory.CreateDirectory("Logs"); File.WriteAllText(Report, message); }
}
#endif
