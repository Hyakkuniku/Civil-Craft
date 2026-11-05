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
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class MillsFinalStoryAuthoring
{
    private const string Request = "Temp/mills-final-story.request";
    private const string Report = "Logs/mills-final-story.txt";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private const string RootName = "Mills Reconnecting Arcadia Story";
    private static readonly string[] AddedIds = { "MillsFinalExplain", "MillsFinalReview", "MillsFinalBriefing", "MillsResolved" };
    private const string ReviewText = "Apply the ideas you have learned across Arcadia. This final project introduces no new structural concept.\n\nLOAD TRANSFER: Follow the load through the roadway, members, supports, and ground.\n\nSTRUCTURAL FORCES: Consider bending, tension, compression, and shear. Moving loads change the structural response; in a real bridge, off-center loads can also cause twisting.\n\nENGINEERING JUDGMENT: Check the entire bridge against the contract requirements. Use stress colors and broken members to identify what needs improving, then adjust and retest. You can revisit your earlier Almanac entries at any time.";

    static MillsFinalStoryAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if (command == "apply") Apply(); else if (command == "validate") Validate(); else throw new InvalidOperationException("Use apply or validate."); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }
    private static Scene Canyon()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.IsValid() && scene.isLoaded && !EditorApplication.isPlayingOrWillChangePlaymode, "Open CanyonCrossing in Edit Mode."); return scene;
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static NPCProgressionManager Mills(Scene scene) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == "Mills");
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Data + path) ?? throw new InvalidOperationException("Missing " + path);
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static bool HasFinal(NPCProgressionManager mills) => MillsWorkersCrossingAuthoring.HasWorkersCrossing(mills) && mills.AuthoredPhases.Count == 15 && mills.AuthoredPhases.Skip(11).Select(p => p.phaseId).SequenceEqual(AddedIds);

    [MenuItem("Tools/Civil Craft/Story/Mills/Final Contract/Apply")]
    public static void Apply()
    {
        var scene = Canyon(); var mills = Mills(scene); if (HasFinal(mills)) { Validate(); return; }
        Require(MillsWorkersCrossingAuthoring.HasWorkersCrossing(mills) && mills.AuthoredPhases.Count == 11 && !scene.GetRootGameObjects().Any(root => root.name == RootName), "Validate the first eleven Mills phases before adding the final project.");
        MillsWorkersCrossingAuthoring.Check(scene, mills, new StringBuilder());
        var contract = Load<ContractSO>("Contract/IndustrialZone/Mills Reconnecting Arcadia Contract.asset");
        var quiz = Load<QuizData>("Quized/FinalIntegratedApplicationQuiz.asset");
        Require(!contract.autoCollectReward && quiz.assessmentMode && quiz.QuestionCount == 5 && quiz.TryValidate(out _), "The final project uses manual turn-in and an any-score five-question assessment.");
        var location = MillsFinalSiteAuthoring.FindLocation(scene); var (start, end) = MillsFinalSiteAuthoring.GetRoadAnchors(location);
        var destination = FindMeeting(scene, start.transform.position, location.transform.position);
        var template = All<CinematicDirector>(scene).Single(movie => movie.cinematicID == "MillsWorkersCrossing-v1");
        bool wasDirty = scene.isDirty;
        string backup = "LocalBackups/MillsFinal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); Directory.CreateDirectory(backup); File.Copy(scene.path, backup + "/CanyonCrossing.unity");
        var originalOverrides = PrefabUtility.GetPropertyModifications(mills.gameObject);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Author Mills final contract"); var report = new StringBuilder();
        try
        {
            var root = NewObject(scene, RootName).transform;
            var wait = Marker(scene, root, "Final Corridor Meeting", destination, Quaternion.LookRotation(Vector3.right));
            Undo.RecordObject(mills, "Extend Mills's final story while preserving existing identities");
            var phases = mills.AuthoredPhases.ToList(); var invitation = phases[10];
            invitation.targetLocation = wait; invitation.teleportToTargetLocation = true; invitation.travelWaypoints.Clear(); invitation.useGroundedWaypoints = false;
            invitation.playDialogueOnArrival = false; invitation.showExclamationMark = true; invitation.repeatDialogue = true;
            invitation.phaseDialogue = Dialogue("There's one final project I'd like you to take on.",
                "Throughout Ironhaven, you've worked on crossings for freight, factory access, heavy equipment, and the workers who keep this place running.",
                "This last project reaches beyond a single site. It could bring several of Arcadia's regional routes together.",
                "Take a look at this crossing. I want you to see what we're working toward.");
            MoveAfter(invitation, mills, AddedIds[0]);
            var explain = Phase(AddedIds[0], wait, true); explain.phaseDialogue = Dialogue("Look beyond the crossing.");
            var movie = CreateCinematic(scene, root, template, contract, start.transform.position, end.transform.position);
            UnityEventTools.AddPersistentListener(explain.onDialogueFinished, movie.PlayCinematic);
            var action = Undo.AddComponent<NPCPhaseAction>(movie.gameObject); var actionData = new SerializedObject(action);
            actionData.FindProperty("npc").objectReferenceValue = mills; actionData.FindProperty("phaseId").stringValue = AddedIds[1]; actionData.ApplyModifiedProperties();
            UnityEventTools.AddPersistentListener(movie.OnCinematicFinished, action.MoveToSelectedPhase);
            var review = Phase(AddedIds[1], wait, true);
            review.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                Say("Arcadia already has roads connecting its regions, but many were built around the natural obstacles separating each area.",
                    "They work, but reaching another region can still mean taking a longer route than necessary.",
                    "This is the last major gap in a corridor that could connect Ironhaven more directly with Rivermere and the route toward Canyon Crossing.",
                    "Completing this bridge would bring the connections you've helped build into a wider route.",
                    "You've studied the concepts you need, so there won't be a new structural lesson. Review your earlier Almanac entries and think about the whole bridge.",
                    "Consider how loads reach the supports, how the members respond, and how vehicle position changes the loading. Your design must satisfy the contract requirements."),
                ReferenceReview()
            }; MoveAfter(review, mills, AddedIds[2]);
            var briefing = Phase(AddedIds[2], wait, true); briefing.contract = contract; briefing.targetBuildLocation = location;
            briefing.runOptionalSequenceBeforeTurnIn = true;
            briefing.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                Say("The bridge has passed the test.",
                    "Before we finish the contract, review your results and the concepts you've applied.",
                    "I want you to understand not only that the design worked, but why it met the requirements.",
                    "Complete the final review and quiz, then speak to me again to officially close the project."),
                ReferenceReview(),
                Say("You can revisit your earlier lessons in the Almanac. Open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + " when you're ready."),
                new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = quiz, quizStepId = "mills-final-integrated-assessment-v1",
                    quizWaitingReminder = "Complete the Final Integrated Application quiz in the Almanac, then speak to me again to turn in the project. You can review your earlier lessons first." },
                Say("You've completed the final review. Speak to me again when you're ready to turn in the contract.")
            };
            var done = Phase(AddedIds[3], wait); done.showExclamationMark = false;
            done.phaseDialogue = Dialogue("You've completed the final crossing, {PlayerName}. You've earned my respect.", "Keep studying each problem, testing your ideas, and learning from the results. That's how an engineer keeps improving.");
            phases.AddRange(new[] { explain, review, briefing, done });
            typeof(NPCProgressionManager).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mills, phases); Mark(mills);
            MillsFinalSiteAuthoring.Apply(scene, location, contract, report);
            Undo.RecordObject(location, "Assign final simulation review"); location.enableSimulationLesson = true; location.simulationLesson = Load<SimulationLessonDefinition>("Simulation Lessons/Final Integrated Application Review.asset"); Mark(location);
            var guidance = mills.GetComponent<NPCStoryObjectiveGuidance>(); Require(guidance != null, "Keep Mills's objective history."); Undo.RecordObject(guidance, "Append final story objectives");
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = "MillsWorkersComplete", objective = Objective(scene, root, "Story/Mills/FinalInvitation-v1", "Talk to Mills at the final crossing", mills.transform) });
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = AddedIds[0], objective = Objective(scene, root, "Story/Mills/FinalSite-v1", "Discuss the final crossing with Mills", wait) });
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = AddedIds[1], objective = Objective(scene, root, "Story/Mills/FinalReview-v1", "Review your engineering knowledge with Mills", mills.transform) }); Mark(guidance);
            Check(scene, mills, report); Undo.FlushUndoRecordObjects(); EditorSceneManager.MarkSceneDirty(scene);
            bool saved = !wasDirty && EditorSceneManager.SaveScene(scene); Undo.CollapseUndoOperations(group);
            report.AppendLine(saved ? "SAVED: CanyonCrossing" : "NEEDS_SAVE: existing unsaved scene edits preserved; save CanyonCrossing after review.");
            report.AppendLine("Backup: " + backup); File.WriteAllText(Report, report.ToString());
        }
        catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); if (originalOverrides != null) PrefabUtility.SetPropertyModifications(mills.gameObject, originalOverrides); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Mills/Final Contract/Validate")]
    public static void Validate() { var scene = Canyon(); var report = new StringBuilder(); Check(scene, Mills(scene), report); report.AppendLine("Scene dirty=" + scene.isDirty); File.WriteAllText(Report, report.ToString()); }
    private static void Check(Scene scene, NPCProgressionManager mills, StringBuilder report)
    {
        MillsWorkersCrossingAuthoring.Check(scene, mills, report);
        Require(HasFinal(mills) && mills.AuthoredPhases.Select(p => p.phaseId).Distinct().Count() == 15, "Keep fifteen unique Mills phase IDs, including the unchanged first eleven.");
        var root = scene.GetRootGameObjects().Single(r => r.name == RootName).transform; var wait = root.Find("Final Corridor Meeting"); var phases = mills.AuthoredPhases;
        Require(wait != null && phases.Skip(10).All(p => p.targetLocation == wait) && phases[10].teleportToTargetLocation && !phases[10].playDialogueOnArrival, "Restore every final-story target and the post-quiz teleport.");
        var contract = Load<ContractSO>("Contract/IndustrialZone/Mills Reconnecting Arcadia Contract.asset"); var location = MillsFinalSiteAuthoring.FindLocation(scene);
        var final = phases[13]; Require(phases.Count(p => p.contract != null) == 3 && final.contract == contract && final.targetBuildLocation == location && !contract.autoCollectReward, "Keep three separate contracts and manual final turn-in.");
        Require(final.runOptionalSequenceBeforeTurnIn && phases.Count(p => p.runOptionalSequenceBeforeTurnIn) == 1 && final.onDialogueFinished.GetPersistentEventCount() == 0 && final.onDialogueFinishedPhaseMove.npc == null, "Final review must wait for a saved success, then leave ordinary turn-in/rewards unchanged.");
        Require(final.optionalDialogueSequence.Count == 5 && final.optionalDialogueSequence[1].stepType == NPCOptionalDialogueStep.StepType.ReferenceReview && final.optionalDialogueSequence[3].quiz == Load<QuizData>("Quized/FinalIntegratedApplicationQuiz.asset") && final.optionalDialogueSequence[3].quiz.assessmentMode && final.optionalDialogueSequence[3].quiz.TryValidate(out _), "Keep the final review and five-question any-score assessment before turn-in.");
        Require(phases[10].onDialogueFinishedPhaseMove.npc == mills && phases[10].onDialogueFinishedPhaseMove.phaseId == AddedIds[0] && phases[12].onDialogueFinishedPhaseMove.npc == mills && phases[12].onDialogueFinishedPhaseMove.phaseId == AddedIds[2], "Keep final invitation and review transitions.");
        var movie = All<CinematicDirector>(scene).Single(c => c.cinematicID == "MillsReconnectingArcadia-v1"); var action = movie.GetComponent<NPCPhaseAction>();
        var actionData = new SerializedObject(action); Require(actionData.FindProperty("npc").objectReferenceValue == mills && actionData.FindProperty("phaseId").stringValue == AddedIds[1] && movie.OnCinematicFinished.GetPersistentEventCount() == 1 && phases[11].onDialogueFinished.GetPersistentEventCount() == 1, "Keep final overview and return-to-review callbacks.");
        Require(location.enableSimulationLesson && location.simulationLesson == Load<SimulationLessonDefinition>("Simulation Lessons/Final Integrated Application Review.asset"), "Assign the final simulation review.");
        Require(mills.GetComponent<NPCStoryObjectiveGuidance>().entries.Count == 10, "Preserve the seven earlier objectives and three final-story objectives.");
        MillsFinalSiteAuthoring.Validate(scene, location, contract, report);
        for (int i = 10; i < phases.Count; i++) report.AppendLine($"PASS {i} {phases[i].phaseId}: {phases[i].targetLocation.name} at {phases[i].targetLocation.position:F2}");
        report.AppendLine("PASS: final site, temporary live load, overview, reference-only review, simulation lesson, any-score pre-turn-in quiz, objective history, and all earlier Mills/Clark bindings.");
    }
    private static Vector3 FindMeeting(Scene scene, Vector3 bank, Vector3 workbench)
    {
        foreach (var point in new[] { bank + new Vector3(-12, 0, 6), bank + new Vector3(-16, 0, 8), workbench + Vector3.forward * 3 })
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 3, Vector3.down, 6, LayerMask.GetMask("Default", "Ground", "Environment"), QueryTriggerInteraction.Ignore).OrderBy(h => Mathf.Abs(h.point.y - bank.y)))
                if (hit.collider.gameObject.scene == scene && hit.normal.y > .7f && Mathf.Abs(hit.point.y - bank.y) < 1.5f && hit.collider.GetComponentInParent<LiveLoadVehicle>() == null) return hit.point;
        throw new InvalidOperationException("No grounded meeting spot beside the final crossing.");
    }
    private static CinematicDirector CreateCinematic(Scene scene, Transform root, CinematicDirector template, ContractSO contract, Vector3 from, Vector3 to)
    {
        var movie = Undo.AddComponent<CinematicDirector>(NewObject(scene, "Final Corridor Overview", root)); movie.cinematicID = "MillsReconnectingArcadia-v1"; movie.playOnStart = movie.playOnlyOnce = false; movie.cinematicCamera = template.cinematicCamera; movie.playerActor = template.playerActor; movie.playerAnimator = template.playerAnimator; movie.cameraTransitionDuration = template.cameraTransitionDuration; movie.cameraReturnDuration = template.cameraReturnDuration; movie.hudElementsToHide = new List<GameObject>(template.hudElementsToHide); movie.contractToDisplay = contract; movie.OnCinematicFinished = new UnityEvent();
        movie.shots = new List<CinematicShot> { Shot("Ironhaven approach", from + new Vector3(-10, 22, 40), from + new Vector3(-7, 19, 35), from), Shot("Arcadia corridor", (from + to) * .5f + new Vector3(0, 42, 82), (from + to) * .5f + new Vector3(0, 36, 74), (from + to) * .5f), Shot("Opposite road", to + new Vector3(10, 24, 40), to + new Vector3(7, 21, 35), to) }; Mark(movie); return movie;
        CinematicShot Shot(string name, Vector3 a, Vector3 b, Vector3 focus) => new CinematicShot { shotName = name, duration = 3, postShotDelay = .1f, cameraStartPoint = Marker(scene, movie.transform, name + " Start", a, Quaternion.LookRotation(focus - a)), cameraEndPoint = Marker(scene, movie.transform, name + " End", b, Quaternion.LookRotation(focus - b)) };
    }
    private static GameObject NewObject(Scene scene, string name, Transform parent = null) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, scene); Undo.RegisterCreatedObjectUndo(obj, "Create final Mills story"); if (parent != null) Undo.SetTransformParent(obj.transform, parent, "Parent final story"); return obj; }
    private static Transform Marker(Scene scene, Transform root, string name, Vector3 position, Quaternion rotation) { var value = NewObject(scene, name, root).transform; value.SetPositionAndRotation(position, rotation); return value; }
    private static TutorialSequence Objective(Scene scene, Transform root, string id, string title, Transform target) { var value = Undo.AddComponent<TutorialSequence>(NewObject(scene, title, root)); value.lessonName = id; value.showInObjectives = true; value.objectiveTitle = title; value.ConfigureObjectiveIdentity(id); value.playOnStart = false; value.tutorialSteps = new[] { new TutorialStep { message = title + ".", screenPosition = TutorialPosition.Left, showNextButton = false, canSkip = false, stepWaypoints = new List<GuiderWaypoint> { new GuiderWaypoint { target = target, advancesTutorial = false } } } }; Mark(value); return value; }
    private static NPCProgressionPhase Phase(string id, Transform target, bool automatic = false) => new NPCProgressionPhase { phaseId = id, targetLocation = target, dialoguePrompt = "Talk to Mills", phaseDialogue = Dialogue(), repeatDialogue = true, playDialogueOnArrival = automatic, showExclamationMark = true, unlockFeatureIdAfterDialogue = string.Empty, featureUnlockDisplayName = string.Empty, showFeatureCollectPopup = false, onDialogueFinished = new UnityEvent(), onNPCInteracted = new UnityEvent() };
    private static Dialogue Dialogue(params string[] lines) => new Dialogue { name = "Mills", sentences = lines };
    private static NPCOptionalDialogueStep Say(params string[] lines) => new NPCOptionalDialogueStep { dialogue = Dialogue(lines) };
    private static NPCOptionalDialogueStep ReferenceReview() => new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.ReferenceReview, referenceTitle = "Final Integrated Application", referenceDescription = ReviewText };
    private static void MoveAfter(NPCProgressionPhase phase, NPCProgressionManager npc, string id) => phase.onDialogueFinishedPhaseMove = new NPCPhaseCall { npc = npc, phaseId = id };
    private static string Em(string value) => "<b><#E09500>" + value + "</color></b>";
    private static void Mark(UnityEngine.Object value) { EditorUtility.SetDirty(value); PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
}
#endif
