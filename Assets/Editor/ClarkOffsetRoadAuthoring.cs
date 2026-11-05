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
internal static class ClarkOffsetRoadAuthoring
{
    private const string Request = "Temp/clark-offset-road.request";
    private const string Report = "Logs/clark-offset-road.txt";
    private const string Data = "Assets/BridgeBuilder/Data/";
    static ClarkOffsetRoadAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if (command == "inspect") Inspect(); else if (command == "apply") Apply(); else if (command == "validate") Validate(); else throw new InvalidOperationException("Unknown command."); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }
    private static Scene Canyon()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.IsValid() && scene.isLoaded && !EditorApplication.isPlayingOrWillChangePlaymode, "Open CanyonCrossing in Edit Mode.");
        return scene;
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static string PathOf(Transform value) => value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Data + path) ?? throw new InvalidOperationException("Missing " + path);
    [MenuItem("Tools/Civil Craft/Story/Clark/Offset Road/Inspect")]
    public static void Inspect()
    {
        Scene scene = Canyon(); var report = new StringBuilder($"Offset Road inspection dirty={scene.isDirty}\n");
        foreach (var site in All<BuildLocation>(scene).Where(v => v.transform.position.x > 2150f))
        {
            report.AppendLine($"SITE ID={site.GetInstanceID()} {PathOf(site.transform)} pos={site.transform.position:F3} contract={site.activeContract?.name}");
            report.AppendLine("  Starts: " + string.Join(", ", site.startingAnchors.Where(p=>p!=null).Select(p=>$"{p.name}={p.transform.position:F3}")));
            report.AppendLine("  Ends: " + string.Join(", ", site.endingAnchors.Where(p=>p!=null).Select(p=>$"{p.name}={p.transform.position:F3}")));
        }
        foreach (var v in All<LiveLoadVehicle>(scene).Where(v => v.name.IndexOf("suv",StringComparison.OrdinalIgnoreCase)>=0 || v.transform.position.x > 2450f))
        {
            v.TryGetMultiLoadEnvelopeBounds(out Bounds bounds);
            report.AppendLine($"VEHICLE ID={v.GetInstanceID()} {PathOf(v.transform)} pos={v.transform.position:F3} rot={v.transform.eulerAngles:F2} mass={v.vehicleMass} contract={v.assignedContract?.name} bounds={bounds} radius={v.wheelRadius} axis={v.spinAxis}");
            report.AppendLine($"  Start={(v.startPoint != null ? v.startPoint.position.ToString("F3") : "unassigned")} End={(v.endPoint != null ? v.endPoint.position.ToString("F3") : "unassigned")} Wheels="+string.Join(",",v.wheelObjects.Where(w=>w!=null).Select(w=>w.name)));
        }
        foreach (var npc in All<NPCProgressionManager>(scene).Where(v=>v.ProgressionSaveId=="Clark" || v.name.IndexOf("Mills",StringComparison.OrdinalIgnoreCase)>=0))
            report.AppendLine($"NPC {PathOf(npc.transform)} pos={npc.transform.position:F2} saveID={npc.ProgressionSaveId}, phases="+string.Join(",",npc.AuthoredPhases.Select(p=>p.phaseId)));
        ClarkOffsetSiteAuthoring.Inspect(scene, report);
        File.WriteAllText(Report, report.ToString());
    }
    internal static readonly string[] AddedPhaseIds = { "ClarkOffsetExplain", "ClarkOffsetLesson", "ClarkOffsetBriefing", "ClarkOffsetReview", "ClarkResolved" };
    private const string RootName = "Clark Offset Road Story";
    private const string QuizStepId = "clark-torsion-assessment-v1";
    internal static bool HasOffsetRoad(NPCProgressionManager clark) => clark.AuthoredPhases.Count == 12 &&
        clark.AuthoredPhases.Skip(7).Select(p => p.phaseId).SequenceEqual(AddedPhaseIds);

    [MenuItem("Tools/Civil Craft/Story/Clark/Offset Road/Apply")]
    public static void Apply()
    {
        Scene scene = Canyon();
        var clark = All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == "Clark");
        if (HasOffsetRoad(clark)) { Validate(); return; }
        bool revertedEmptyTail = clark.AuthoredPhases.Count == 12 && clark.AuthoredPhases.Skip(7).All(p => p != null && string.IsNullOrEmpty(p.phaseId));
        Require((clark.AuthoredPhases.Count == 7 || revertedEmptyTail) && clark.AuthoredPhases[6].phaseId == "FreightPassageComplete" &&
            !scene.GetRootGameObjects().Any(root => root.name == RootName), "Preserve and review Clark's existing phases before extending the story.");
        if (!revertedEmptyTail) ClarkProgressionTargets.Check(scene, clark, new StringBuilder());
        var contract = Load<ContractSO>("Contract/IndustrialZone/Clark Offset Road Contract.asset");
        var lesson = Load<LessonData>("Lessons/Torsion.asset");
        var quiz = Load<QuizData>("Quized/TorsionQuiz.asset");
        var simulation = Load<SimulationLessonDefinition>("Simulation Lessons/Torsion Lesson.asset");
        Require(quiz.assessmentMode && quiz.QuestionCount == 5 && quiz.TryValidate(out _), "The five-question Torsion assessment must be valid.");
        var location = ClarkOffsetSiteAuthoring.FindLocation(scene);
        var (start, end) = ClarkOffsetSiteAuthoring.GetRoadAnchors(location);
        var template = All<CinematicDirector>(scene).Single(movie => movie.cinematicID == "ClarkFreightPassage-v1");
        Require(template.cinematicCamera != null && template.playerActor != null && template.playerAnimator != null,
            "Preserve Clark's existing cinematic camera/player references.");
        Vector3 destination = FindDestination(scene, location, start.transform.position);
        bool originallyDirty = scene.isDirty;
        string backup = "LocalBackups/ClarkOffsetRoad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path, backup + "/CanyonCrossing.unity");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Author Clark Offset Road story");
        var originalOverrides = PrefabUtility.IsPartOfPrefabInstance(clark.gameObject)
            ? PrefabUtility.GetPropertyModifications(clark.gameObject) : null;
        var report = new StringBuilder();
        try
        {
            Transform root = NewObject(scene, RootName).transform;
            Transform wait = Marker(scene, root, "Factory Access Meeting", destination, Quaternion.LookRotation(Vector3.right));
            Undo.RecordObject(clark, "Extend Clark's existing story with Offset Road");
            var phases = clark.AuthoredPhases.Take(7).ToList();
            // Keep all existing phase IDs and indices so completed first-contract saves remain valid.
            var invitation = phases[6];
            for (int i = invitation.onDialogueFinished.GetPersistentEventCount() - 1; i >= 0; i--)
                if (invitation.onDialogueFinished.GetPersistentTarget(i) == null) UnityEventTools.RemovePersistentListener(invitation.onDialogueFinished, i);
            invitation.showExclamationMark = true; invitation.repeatDialogue = true;
            invitation.targetLocation = wait; invitation.teleportToTargetLocation = true;
            invitation.travelWaypoints.Clear(); invitation.useGroundedWaypoints = false;
            invitation.playDialogueOnArrival = false;
            invitation.phaseDialogue = Dialogue("Good to see you again, {PlayerName}. There's another site farther into the factory district that I'd like you to look at.",
                "This is the factory access road. The surrounding equipment limits where we can place the crossing. Let me show you the challenge.");
            MoveAfter(invitation, clark, AddedPhaseIds[0]);
            var explain = Phase(AddedPhaseIds[0], wait, true);
            explain.phaseDialogue = Dialogue("Have a look at the approach to the passage.");
            explain.useGroundedWaypoints = false;
            var cinematic = CreateCinematic(scene, root, template, contract, start.transform.position, end.transform.position);
            UnityEventTools.AddPersistentListener(explain.onDialogueFinished, cinematic.PlayCinematic);
            var action = Undo.AddComponent<NPCPhaseAction>(cinematic.gameObject);
            var actionData = new SerializedObject(action);
            actionData.FindProperty("npc").objectReferenceValue = clark;
            actionData.FindProperty("phaseId").stringValue = AddedPhaseIds[1]; actionData.ApplyModifiedProperties();
            UnityEventTools.AddPersistentListener(cinematic.OnCinematicFinished, action.MoveToSelectedPhase);
            var teach = Phase(AddedPhaseIds[1], wait, true);
            teach.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                Say("These utilities and factory structures are still in use. Moving them would interrupt operations, so we must work around them.",
                    "That leaves the traffic lane " + Em("closer to one side of the bridge") + ". The factory still needs a usable connection.",
                    "A load near the center and the same load near an edge can affect a real bridge differently.",
                    "An off-center load can create " + Em("Torsion") + ": a twisting effect around a member's length.",
                    "For the same weight, a greater sideways distance from the center of resistance can increase the twisting moment.",
                    "Engineers consider both the downward load and that twisting effect. Here, the lesson explains torsion while your bridge test checks its load-carrying members."),
                Review(lesson)
            };
            MoveAfter(teach, clark, AddedPhaseIds[2]);
            var briefing = Phase(AddedPhaseIds[2], wait, true);
            briefing.contract = contract; briefing.targetBuildLocation = location;
            var review = Phase(AddedPhaseIds[3], wait, true);
            review.optionalDialogueSequence = new List<NPCOptionalDialogueStep>
            {
                Review(lesson),
                Say("Review the " + Em("Torsion lesson") + " in your Almanac. When you're ready, open the " + Em("Quiz tab") + " and press " + Em("Start Quiz") + "."),
                new NPCOptionalDialogueStep { stepType = NPCOptionalDialogueStep.StepType.Quiz, quiz = quiz,
                    quizStepId = QuizStepId, quizWaitingReminder = "Review how an off-center load can create torsion, then open the Almanac Quiz tab and press Start Quiz when you're ready." }
            };
            MoveAfter(review, clark, AddedPhaseIds[4]);
            var done = Phase(AddedPhaseIds[4], wait);
            done.showExclamationMark = false;
            done.phaseDialogue = Dialogue("The factory has its direct connection now. Thanks for your help, {PlayerName}.",
                "Mills handles the larger equipment projects farther into Ironhaven. He's the person to speak to next.");
            phases.AddRange(new[] { explain, teach, briefing, review, done });
            typeof(NPCProgressionManager).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(clark, phases);
            var npcData = new SerializedObject(clark);
            npcData.FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue = true;
            npcData.ApplyModifiedProperties(); Mark(clark);
            ClarkOffsetSiteAuthoring.Apply(scene, location, contract, report);
            Undo.RecordObject(location, "Assign Offset Road's simulation lesson");
            location.enableSimulationLesson = true; location.simulationLesson = simulation; Mark(location);
            foreach (var tab in All<AlmanacLessonTab>(scene))
            {
                var tabData = new SerializedObject(tab); var lessons = tabData.FindProperty("allLessons");
                if (!Enumerable.Range(0, lessons.arraySize).Any(i => lessons.GetArrayElementAtIndex(i).objectReferenceValue == lesson))
                {
                    Undo.RecordObject(tab, "Register Torsion in the Almanac");
                    lessons.GetArrayElementAtIndex(lessons.arraySize++).objectReferenceValue = lesson;
                    tabData.ApplyModifiedProperties(); Mark(tab);
                }
            }
            var guidance = clark.GetComponent<NPCStoryObjectiveGuidance>();
            Require(guidance != null, "Keep Clark's existing objective guidance.");
            Undo.RecordObject(guidance, "Add Offset Road objectives");
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = "FreightPassageComplete", objective = Objective(scene, root, "Story/Clark/OffsetInvitation-v1", "Talk to Clark at the factory access road", clark.transform) });
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = AddedPhaseIds[0], objective = Objective(scene, root, "Story/Clark/OffsetSite-v1", "Talk to Clark at the factory access road", wait) });
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = AddedPhaseIds[1], objective = Objective(scene, root, "Story/Clark/LearnTorsion-v1", "Learn about Torsion with Clark", clark.transform) });
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry { phaseId = AddedPhaseIds[3], objective = Objective(scene, root, "Story/Clark/TorsionQuiz-v1", "Complete Clark's Torsion quiz", null) });
            Mark(guidance);
            Check(scene, clark, report);
            Undo.FlushUndoRecordObjects(); EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            bool saved = !originallyDirty && EditorSceneManager.SaveScene(scene);
            report.AppendLine(saved ? "SAVED: CanyonCrossing" : "NEEDS_SAVE: existing edits preserved; save CanyonCrossing after review.");
            report.AppendLine("Budget is provisional: 600,000. SUV weight matches its authored 1,000 kg. Backup: " + backup);
            File.WriteAllText(Report, report.ToString());
        }
        catch
        {
            Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group);
            // Preserve actual Unity object references when recovering a prefab instance.
            // JSON recovery clears scene references in nested phase data.
            if(originalOverrides != null) PrefabUtility.SetPropertyModifications(clark.gameObject, originalOverrides);
            throw;
        }
    }
    [MenuItem("Tools/Civil Craft/Story/Clark/Offset Road/Validate")]
    public static void Validate()
    {
        Scene scene = Canyon(); var report = new StringBuilder();
        Check(scene, All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == "Clark"), report);
        report.AppendLine("Scene dirty=" + scene.isDirty); File.WriteAllText(Report, report.ToString());
    }
    internal static void Check(Scene scene, NPCProgressionManager clark, StringBuilder report)
    {
        ClarkProgressionTargets.Check(scene, clark, report);
        var contract = Load<ContractSO>("Contract/IndustrialZone/Clark Offset Road Contract.asset");
        var location = ClarkOffsetSiteAuthoring.FindLocation(scene);
        Require(HasOffsetRoad(clark) && clark.AuthoredPhases.Select(p=>p.phaseId).Distinct().Count() == 12, "Clark's appended phase IDs must remain unique and ordered.");
        var phases = clark.AuthoredPhases;
        var persistence = new SerializedObject(clark);
        Require(persistence.FindProperty("persistProgression").boolValue && persistence.FindProperty("restoreSavedPhaseBridgesOnStart").boolValue,
            "Keep Clark's story and completed bridges persistent across restarts.");
        Require(phases[6].phaseId == "FreightPassageComplete" && phases[6].onDialogueFinishedPhaseMove.phaseId == AddedPhaseIds[0] &&
            phases[9].contract == contract && phases[9].targetBuildLocation == location && phases.Count(p=>p.contract != null) == 2,
            "Clark needs the original first contract and the separate Offset Road offer.");
        Require(phases[6].teleportToTargetLocation && phases[6].targetLocation==phases[7].targetLocation &&
            !phases[6].playDialogueOnArrival && phases[7].travelWaypoints.Count == 0 && !phases[7].teleportToTargetLocation,
            "Teleport Clark to the second site immediately after the first quiz, then wait for the player to interact.");
        Require(phases[8].optionalDialogueSequence.Any(s=>s.lesson==Load<LessonData>("Lessons/Torsion.asset")) &&
            phases[8].onDialogueFinishedPhaseMove.phaseId==AddedPhaseIds[2] &&
            phases[10].optionalDialogueSequence.Count==3 && phases[10].optionalDialogueSequence[0].stepType==NPCOptionalDialogueStep.StepType.LessonIntroduction &&
            phases[10].optionalDialogueSequence[2].quizStepId==QuizStepId && phases[10].optionalDialogueSequence[2].quiz.assessmentMode &&
            phases[10].optionalDialogueSequence[2].quiz.TryValidate(out _) && phases[10].onDialogueFinishedPhaseMove.phaseId==AddedPhaseIds[4],
            "Torsion introduction, turn-in review, and any-score quiz must precede the resolved phase.");
        Require(location.enableSimulationLesson && location.simulationLesson==Load<SimulationLessonDefinition>("Simulation Lessons/Torsion Lesson.asset"), "Assign the site-specific simulation lesson.");
        foreach(var tab in All<AlmanacLessonTab>(scene))
        {
            var lessons=new SerializedObject(tab).FindProperty("allLessons");
            Require(Enumerable.Range(0,lessons.arraySize).Count(i=>lessons.GetArrayElementAtIndex(i).objectReferenceValue==Load<LessonData>("Lessons/Torsion.asset"))==1,"Register the Torsion lesson once in each Almanac.");
        }
        ClarkOffsetSiteAuthoring.Validate(scene, location, contract, report);
        var firstLocation = ClarkFreightSiteAuthoring.FindLocation(scene);
        ClarkFourVehicleSetup.Check(scene, firstLocation, Load<ContractSO>("Contract/IndustrialZone/Clark Contract.asset"), report);
        Require(clark.GetComponent<NPCStoryObjectiveGuidance>().entries.Count==7,"Keep the first three guidance entries and add four Offset Road entries.");
        report.AppendLine("PASS: original seven phase identities, both main contracts, post-quiz teleport with Talk objective, overview cinematic, Torsion lesson/review/quiz, first four-truck roster, and second single-SUV site.");
        report.AppendLine(All<NPCProgressionManager>(scene).Any(npc=>npc.ProgressionSaveId=="Mills")
            ? "Mills is authored in this scene; his own progression gate controls the next handoff."
            : "Mills is not yet authored in this scene; the final dialogue names him without creating a missing navigation target.");
    }
    private static Vector3 FindDestination(Scene scene, BuildLocation location, Vector3 bank)
    {
        Physics.SyncTransforms();
        foreach(var point in new[]{bank+new Vector3(-12,0,6),bank+new Vector3(-16,0,8),location.transform.position+Vector3.forward*3,location.transform.position-Vector3.forward*3})
            foreach(var hit in Physics.RaycastAll(point+Vector3.up*3,Vector3.down,6,LayerMask.GetMask("Default","Ground","Environment"),QueryTriggerInteraction.Ignore).OrderBy(h=>Mathf.Abs(h.point.y-bank.y)))
                if(hit.collider.gameObject.scene==scene && hit.normal.y>.7f && Mathf.Abs(hit.point.y-bank.y)<1.5f && hit.collider.GetComponentInParent<LiveLoadVehicle>()==null) return hit.point;
        throw new InvalidOperationException("No grounded meeting position beside Clark's new site.");
    }
    private static CinematicDirector CreateCinematic(Scene scene, Transform root, CinematicDirector template, ContractSO contract, Vector3 from, Vector3 to)
    {
        var movie=Undo.AddComponent<CinematicDirector>(NewObject(scene,"Offset Road Overview",root));
        movie.cinematicID="ClarkOffsetRoad-v1"; movie.playOnlyOnce=movie.playOnStart=false;
        movie.cinematicCamera=template.cinematicCamera; movie.playerActor=template.playerActor; movie.playerAnimator=template.playerAnimator;
        movie.cameraTransitionDuration=template.cameraTransitionDuration; movie.cameraReturnDuration=template.cameraReturnDuration;
        movie.hudElementsToHide=new List<GameObject>(template.hudElementsToHide); movie.contractToDisplay=contract;
        movie.OnCinematicFinished=new UnityEvent();
        movie.shots=new List<CinematicShot>{Shot("Factory approach",from+new Vector3(-8,18,35),from+new Vector3(-4,16,30),from),
            Shot("Offset lane",(from+to)*.5f+new Vector3(0,26,48),(from+to)*.5f+new Vector3(0,23,42),(from+to)*.5f),
            Shot("Opposite bank",to+new Vector3(6,18,30),to+new Vector3(4,16,25),to)};
        Mark(movie); return movie;
        CinematicShot Shot(string name,Vector3 a,Vector3 b,Vector3 focus)=>new CinematicShot{shotName=name,duration=3,postShotDelay=.1f,
            cameraStartPoint=Marker(scene,movie.transform,name+" Start",a,Quaternion.LookRotation(focus-a)),cameraEndPoint=Marker(scene,movie.transform,name+" End",b,Quaternion.LookRotation(focus-b))};
    }
    private static GameObject NewObject(Scene scene,string name,Transform parent=null)
    {var obj=new GameObject(name);SceneManager.MoveGameObjectToScene(obj,scene);Undo.RegisterCreatedObjectUndo(obj,"Create Offset Road story");if(parent!=null)Undo.SetTransformParent(obj.transform,parent,"Parent Offset Road story");return obj;}
    private static Transform Marker(Scene scene,Transform parent,string name,Vector3 position,Quaternion rotation)
    {var value=NewObject(scene,name,parent).transform;value.SetPositionAndRotation(position,rotation);return value;}
    private static TutorialSequence Objective(Scene scene,Transform parent,string id,string title,Transform target,bool follow=false)
    {
        var value=Undo.AddComponent<TutorialSequence>(NewObject(scene,title,parent));value.lessonName=id;value.showInObjectives=true;value.objectiveTitle=title;value.ConfigureObjectiveIdentity(id);value.playOnStart=false;
        value.tutorialSteps=new[]{new TutorialStep{message=title+".",screenPosition=TutorialPosition.Left,showNextButton=false,canSkip=follow,
            stepWaypoints=target==null?new List<GuiderWaypoint>():new List<GuiderWaypoint>{new GuiderWaypoint{target=target,advancesTutorial=follow}}}};Mark(value);return value;
    }
    private static NPCProgressionPhase Phase(string id,Transform target,bool automatic=false)=>new NPCProgressionPhase{phaseId=id,targetLocation=target,dialoguePrompt="Talk to Clark",phaseDialogue=Dialogue(),repeatDialogue=true,playDialogueOnArrival=automatic,showExclamationMark=true,
        unlockFeatureIdAfterDialogue=string.Empty,featureUnlockDisplayName=string.Empty,showFeatureCollectPopup=false,onDialogueFinished=new UnityEvent(),onNPCInteracted=new UnityEvent()};
    private static Dialogue Dialogue(params string[] lines)=>new Dialogue{name="Clark",sentences=lines};
    private static NPCOptionalDialogueStep Say(params string[] lines)=>new NPCOptionalDialogueStep{dialogue=Dialogue(lines)};
    private static NPCOptionalDialogueStep Review(LessonData lesson)=>new NPCOptionalDialogueStep{stepType=NPCOptionalDialogueStep.StepType.LessonIntroduction,lesson=lesson};
    private static void MoveAfter(NPCProgressionPhase phase,NPCProgressionManager npc,string id)=>phase.onDialogueFinishedPhaseMove=new NPCPhaseCall{npc=npc,phaseId=id};
    private static string Em(string value)=>"<b><#E09500>"+value+"</color></b>";
    private static void Mark(UnityEngine.Object value){EditorUtility.SetDirty(value);PrefabUtility.RecordPrefabInstancePropertyModifications(value);}
}
#endif
