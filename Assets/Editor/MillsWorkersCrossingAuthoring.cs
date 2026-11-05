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
internal static class MillsWorkersCrossingAuthoring
{
    private const string Request="Temp/mills-workers-crossing.request";
    private const string Report="Logs/mills-workers-crossing.txt";
    private const string Data="Assets/BridgeBuilder/Data/";
    private const string RootName="Mills Workers Crossing Story";
    private static readonly string[] FirstIds={"MeetingMills","MillsExplainSituation","MillsFailureLesson","MillsProjectBriefing","MillsFailureReview","MillsHeavyEquipmentComplete"};
    internal static readonly string[] AddedIds={"MillsWorkersExplain","MillsWorkersReview","MillsWorkersBriefing","MillsWorkersQuiz","MillsWorkersComplete"};
    private const string ReviewText="Bring together what you have learned: follow the load path through the roadway, members, supports, and ground.\n\nConsider the span, materials, vehicle weight, and changing load position. In a real bridge, bending, shear, and off-center twisting also matter.\n\nUse the test results to locate overloaded or broken members before changing the design. Your earlier Almanac entries remain available; this review adds no new lesson.";
    static MillsWorkersCrossingAuthoring(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;
        string command=File.ReadAllText(Request).Trim();File.Delete(Request);
        try{if(command=="inspect")Inspect();else if(command=="apply")Apply();else if(command=="validate")Validate();else throw new InvalidOperationException("Unknown Workers Crossing command.");}
        catch(Exception error){File.WriteAllText(Report,"FAIL: "+error);Debug.LogException(error);}
    }
    private static Scene Canyon(){var scene=SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");Require(scene.IsValid()&&scene.isLoaded&&!EditorApplication.isPlayingOrWillChangePlaymode,"Open CanyonCrossing in Edit Mode.");return scene;}
    private static T[] All<T>(Scene scene)where T:Component=>scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<T>(true)).ToArray();
    private static NPCProgressionManager Mills(Scene scene)=>All<NPCProgressionManager>(scene).Single(npc=>npc.ProgressionSaveId=="Mills");
    private static T Load<T>(string path)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(Data+path)??throw new InvalidOperationException("Missing "+path);
    private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    internal static bool HasWorkersCrossing(NPCProgressionManager mills)=>mills.AuthoredPhases.Count>=11&&mills.AuthoredPhases.Take(6).Select(p=>p.phaseId).SequenceEqual(FirstIds)&&mills.AuthoredPhases.Skip(6).Take(5).Select(p=>p.phaseId).SequenceEqual(AddedIds);
    [MenuItem("Tools/Civil Craft/Story/Mills/Workers Crossing/Inspect")]
    public static void Inspect()
    {
        var scene=Canyon();var report=new StringBuilder($"Workers Crossing inspection dirty={scene.isDirty}\n");
        var mills=Mills(scene);foreach(var phase in mills.AuthoredPhases)report.AppendLine($"{phase.phaseId}: target={(phase.targetLocation!=null?phase.targetLocation.name:"MISSING")}");
        MillsWorkersCrossingSiteAuthoring.Inspect(scene,report);File.WriteAllText(Report,report.ToString());
    }
    [MenuItem("Tools/Civil Craft/Story/Mills/Workers Crossing/Apply")]
    public static void Apply()
    {
        var scene=Canyon();var mills=Mills(scene);if(HasWorkersCrossing(mills)){Validate();return;}
        Require(mills.AuthoredPhases.Select(p=>p.phaseId).SequenceEqual(FirstIds)&&!scene.GetRootGameObjects().Any(r=>r.name==RootName),"Preserve and review Mills's first six phases before extending the story.");
        var oldRoot=scene.GetRootGameObjects().Single(r=>r.name=="Mills Heavy Equipment Story").transform;
        var oldSpawn=oldRoot.Find("Mills Spawn");var oldMeeting=oldRoot.Find("Heavy Equipment Meeting");
        Require(oldSpawn!=null&&oldMeeting!=null&&mills.AuthoredPhases[0].targetLocation==oldSpawn&&mills.AuthoredPhases.Skip(1).All(p=>p.targetLocation==oldMeeting),"Restore Mills's first-story targets before appending the second contract.");
        var contract=Load<ContractSO>("Contract/IndustrialZone/Mills Workers Crossing Contract.asset");var quiz=Load<QuizData>("Quized/IntegratedApplicationQuiz.asset");
        Require(quiz.assessmentMode&&quiz.QuestionCount==5&&quiz.TryValidate(out _),"The Integrated Application assessment must contain five valid questions.");
        var location=MillsWorkersCrossingSiteAuthoring.FindLocation(scene);var(start,end)=MillsWorkersCrossingSiteAuthoring.GetRoadAnchors(location);
        var destination=FindMeeting(scene,start.transform.position,location.transform.position);
        var template=All<CinematicDirector>(scene).Single(movie=>movie.cinematicID=="MillsHeavyEquipment-v1");
        bool wasDirty=scene.isDirty;string backup="LocalBackups/MillsWorkersCrossing-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(backup);File.Copy(scene.path,backup+"/CanyonCrossing.unity");
        var originalOverrides=PrefabUtility.GetPropertyModifications(mills.gameObject);
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Author Mills Workers Crossing");var report=new StringBuilder();
        try
        {
            var root=NewObject(scene,RootName).transform;var wait=Marker(scene,root,"Workers Route Meeting",destination,Quaternion.LookRotation(Vector3.right));
            Undo.RecordObject(mills,"Extend Mills's story without changing existing phase IDs");
            var phases=mills.AuthoredPhases.ToList();var invitation=phases[5];
            invitation.targetLocation=wait;invitation.teleportToTargetLocation=true;invitation.travelWaypoints.Clear();invitation.useGroundedWaypoints=false;
            invitation.playDialogueOnArrival=false;invitation.showExclamationMark=true;invitation.repeatDialogue=true;
            invitation.phaseDialogue=Dialogue("Ready for another project, {PlayerName}? This time we're looking at the people who keep these facilities running.",
                "Their route has become crowded. This crossing could give the worker buses a more direct way into the industrial district. Let me show you the site.");
            MoveAfter(invitation,mills,AddedIds[0]);
            var explain=Phase(AddedIds[0],wait,true);explain.phaseDialogue=Dialogue("Watch the road for a moment.");
            var movie=CreateCinematic(scene,root,template,contract,start.transform.position,end.transform.position);
            UnityEventTools.AddPersistentListener(explain.onDialogueFinished,movie.PlayCinematic);
            var action=Undo.AddComponent<NPCPhaseAction>(movie.gameObject);var actionData=new SerializedObject(action);
            actionData.FindProperty("npc").objectReferenceValue=mills;actionData.FindProperty("phaseId").stringValue=AddedIds[1];actionData.ApplyModifiedProperties();
            UnityEventTools.AddPersistentListener(movie.OnCinematicFinished,action.MoveToSelectedPhase);
            var knowledge=Phase(AddedIds[1],wait,true);
            knowledge.optionalDialogueSequence=new List<NPCOptionalDialogueStep>
            {
                Say("Many workers travel between the residential areas and factories every day. At shift changes, their buses share busy roads with cargo trucks.",
                    "The city wants a separate crossing from the "+Em("worker route to the industrial district")+". It would give buses their own route and ease pressure on the freight roads.",
                    "There's no new structural concept this time. Bring together what you've learned about loads and structural forces.",
                    "In a real bridge, bending, tension, compression, shear, and off-center twisting can act together. Vehicle position matters throughout the crossing.",
                    "Think about the whole structure: "+Em("how the load reaches the supports, how members work together, and whether the bridge safely carries the required traffic")+"."),
                ReferenceReview()
            };MoveAfter(knowledge,mills,AddedIds[2]);
            var briefing=Phase(AddedIds[2],wait,true);briefing.contract=contract;briefing.targetBuildLocation=location;
            var assessment=Phase(AddedIds[3],wait,true);assessment.optionalDialogueSequence=new List<NPCOptionalDialogueStep>
            {
                ReferenceReview(),
                Say("You can revisit your earlier lessons in the Almanac. When you're ready, open the "+Em("Quiz tab")+" and press "+Em("Start Quiz")+"."),
                new NPCOptionalDialogueStep{stepType=NPCOptionalDialogueStep.StepType.Quiz,quiz=quiz,quizStepId="mills-integrated-application-assessment-v1",
                    quizWaitingReminder="Review how loads, structural members, and supports work together. Open the Almanac Quiz tab and press Start Quiz when you're ready."}
            };MoveAfter(assessment,mills,AddedIds[4]);
            var done=Phase(AddedIds[4],wait);done.showExclamationMark=false;
            done.phaseDialogue=Dialogue("The workers have their direct route now. Good work, {PlayerName}.","Keep considering the bridge as a whole. The final connection will bring those ideas together on a larger scale.");
            phases.AddRange(new[]{explain,knowledge,briefing,assessment,done});
            typeof(NPCProgressionManager).GetField("phases",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mills,phases);Mark(mills);
            MillsWorkersCrossingSiteAuthoring.Apply(scene,location,contract,report);
            Undo.RecordObject(location,"Assign the integrated simulation review");location.enableSimulationLesson=true;location.simulationLesson=Load<SimulationLessonDefinition>("Simulation Lessons/Integrated Application Review.asset");Mark(location);
            var guidance=mills.GetComponent<NPCStoryObjectiveGuidance>();Require(guidance!=null,"Preserve Mills's first-story guidance.");Undo.RecordObject(guidance,"Add Workers Crossing objectives");
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry{phaseId=FirstIds[5],objective=Objective(scene,root,"Story/Mills/WorkersInvitation-v1","Talk to Mills at the worker crossing",mills.transform)});
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry{phaseId=AddedIds[0],objective=Objective(scene,root,"Story/Mills/WorkersSite-v1","Discuss the worker crossing with Mills",wait)});
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry{phaseId=AddedIds[1],objective=Objective(scene,root,"Story/Mills/IntegratedReview-v1","Review the bridge concepts with Mills",mills.transform)});
            guidance.entries.Add(new NPCStoryObjectiveGuidance.Entry{phaseId=AddedIds[3],objective=Objective(scene,root,"Story/Mills/IntegratedQuiz-v1","Complete Mills's Integrated Application quiz",null)});Mark(guidance);
            Check(scene,mills,report);Undo.FlushUndoRecordObjects();EditorSceneManager.MarkSceneDirty(scene);
            bool saved=!wasDirty&&EditorSceneManager.SaveScene(scene);Undo.CollapseUndoOperations(group);
            report.AppendLine(saved?"SAVED: CanyonCrossing":"NEEDS_SAVE: current scene edits preserved; save CanyonCrossing after review.");report.AppendLine("Backup: "+backup);File.WriteAllText(Report,report.ToString());
        }
        catch{Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(group);if(originalOverrides!=null)PrefabUtility.SetPropertyModifications(mills.gameObject,originalOverrides);throw;}
    }
    [MenuItem("Tools/Civil Craft/Story/Mills/Workers Crossing/Validate")]
    public static void Validate(){var scene=Canyon();var report=new StringBuilder();Check(scene,Mills(scene),report);report.AppendLine("Scene dirty="+scene.isDirty);File.WriteAllText(Report,report.ToString());}
    internal static void Check(Scene scene,NPCProgressionManager mills,StringBuilder report)
    {
        MillsStoryAuthoring.Check(scene,mills,report);
        Require(HasWorkersCrossing(mills)&&mills.AuthoredPhases.Select(p=>p.phaseId).Distinct().Count()==mills.AuthoredPhases.Count,"Preserve Mills's first eleven unique phase identities.");
        var root=scene.GetRootGameObjects().Single(r=>r.name==RootName).transform;var wait=root.Find("Workers Route Meeting");Require(wait!=null,"Missing Workers Route Meeting marker.");var phases=mills.AuthoredPhases;
        Require(phases.Skip(5).Take(phases.Count==11?6:5).All(p=>p.targetLocation!=null&&p.targetLocation==wait)&&phases[5].teleportToTargetLocation&&!phases[5].playDialogueOnArrival,"Teleport Mills after the first quiz and keep every second-story phase at the worker site.");
        Require(phases[5].onDialogueFinishedPhaseMove.npc==mills&&phases[5].onDialogueFinishedPhaseMove.phaseId==AddedIds[0]&&
            phases[7].onDialogueFinishedPhaseMove.npc==mills&&phases[7].onDialogueFinishedPhaseMove.phaseId==AddedIds[2]&&
            phases[9].onDialogueFinishedPhaseMove.npc==mills&&phases[9].onDialogueFinishedPhaseMove.phaseId==AddedIds[4],"Missing Workers Crossing phase transition.");
        var contract=Load<ContractSO>("Contract/IndustrialZone/Mills Workers Crossing Contract.asset");var location=MillsWorkersCrossingSiteAuthoring.FindLocation(scene);
        Require(phases.Take(11).Count(p=>p.contract!=null)==2&&phases[8].contract==contract&&phases[8].targetBuildLocation==location,"Keep the first crane contract and separate second worker-bus contract.");
        Require(phases[7].optionalDialogueSequence.Last().stepType==NPCOptionalDialogueStep.StepType.ReferenceReview&&
            phases[9].optionalDialogueSequence[0].stepType==NPCOptionalDialogueStep.StepType.ReferenceReview&&
            phases[9].optionalDialogueSequence[2].quiz==Load<QuizData>("Quized/IntegratedApplicationQuiz.asset")&&phases[9].optionalDialogueSequence[2].quiz.assessmentMode&&phases[9].optionalDialogueSequence[2].quiz.TryValidate(out _),"Use review-only popups and the any-score Integrated Application quiz.");
        Require(!phases.Skip(6).Take(5).SelectMany(p=>p.optionalDialogueSequence).Any(s=>s.stepType==NPCOptionalDialogueStep.StepType.LessonIntroduction),"Workers Crossing introduces no new formal lesson.");
        Require(location.enableSimulationLesson&&location.simulationLesson==Load<SimulationLessonDefinition>("Simulation Lessons/Integrated Application Review.asset"),"Assign the integrated simulation review.");
        Require(mills.GetComponent<NPCStoryObjectiveGuidance>().entries.Count>=7,"Keep all first-story objectives plus the four worker-crossing objectives.");
        MillsWorkersCrossingSiteAuthoring.Validate(scene,location,contract,report);
        for(int i=5;i<11;i++)report.AppendLine($"PASS {i} {phases[i].phaseId}: {phases[i].targetLocation.name} at {phases[i].targetLocation.position:F2}");
        report.AppendLine("PASS: Workers Crossing contract, post-quiz teleport, overview, review-only popup without new lesson discovery, simulation review, assessment and preserved prior phase identities.");
    }
    private static Vector3 FindMeeting(Scene scene,Vector3 bank,Vector3 workbench)
    {
        foreach(var point in new[]{bank+new Vector3(-12,0,6),bank+new Vector3(-16,0,8),workbench+Vector3.forward*3})
            foreach(var hit in Physics.RaycastAll(point+Vector3.up*3,Vector3.down,6,LayerMask.GetMask("Default","Ground","Environment"),QueryTriggerInteraction.Ignore).OrderBy(h=>Mathf.Abs(h.point.y-bank.y)))
                if(hit.collider.gameObject.scene==scene&&hit.normal.y>.7f&&Mathf.Abs(hit.point.y-bank.y)<1.5f&&hit.collider.GetComponentInParent<LiveLoadVehicle>()==null)return hit.point;
        throw new InvalidOperationException("No grounded meeting spot beside the worker crossing.");
    }
    private static CinematicDirector CreateCinematic(Scene scene,Transform root,CinematicDirector template,ContractSO contract,Vector3 from,Vector3 to)
    {
        var movie=Undo.AddComponent<CinematicDirector>(NewObject(scene,"Workers Crossing Overview",root));movie.cinematicID="MillsWorkersCrossing-v1";movie.playOnStart=movie.playOnlyOnce=false;movie.cinematicCamera=template.cinematicCamera;movie.playerActor=template.playerActor;movie.playerAnimator=template.playerAnimator;movie.cameraTransitionDuration=template.cameraTransitionDuration;movie.cameraReturnDuration=template.cameraReturnDuration;movie.hudElementsToHide=new List<GameObject>(template.hudElementsToHide);movie.contractToDisplay=contract;movie.OnCinematicFinished=new UnityEvent();
        movie.shots=new List<CinematicShot>{Shot("Worker approach",from+new Vector3(-10,20,35),from+new Vector3(-7,17,30),from),Shot("Workers crossing",(from+to)*.5f+new Vector3(0,30,54),(from+to)*.5f+new Vector3(0,25,48),(from+to)*.5f),Shot("Industrial arrival",to+new Vector3(10,22,35),to+new Vector3(7,19,30),to)};Mark(movie);return movie;
        CinematicShot Shot(string name,Vector3 a,Vector3 b,Vector3 focus)=>new CinematicShot{shotName=name,duration=3,postShotDelay=.1f,cameraStartPoint=Marker(scene,movie.transform,name+" Start",a,Quaternion.LookRotation(focus-a)),cameraEndPoint=Marker(scene,movie.transform,name+" End",b,Quaternion.LookRotation(focus-b))};
    }
    private static GameObject NewObject(Scene scene,string name,Transform parent=null){var obj=new GameObject(name);SceneManager.MoveGameObjectToScene(obj,scene);Undo.RegisterCreatedObjectUndo(obj,"Create Workers Crossing story");if(parent!=null)Undo.SetTransformParent(obj.transform,parent,"Parent Workers Crossing story");return obj;}
    private static Transform Marker(Scene scene,Transform root,string name,Vector3 position,Quaternion rotation){var value=NewObject(scene,name,root).transform;value.SetPositionAndRotation(position,rotation);return value;}
    private static TutorialSequence Objective(Scene scene,Transform root,string id,string title,Transform target){var value=Undo.AddComponent<TutorialSequence>(NewObject(scene,title,root));value.lessonName=id;value.showInObjectives=true;value.objectiveTitle=title;value.ConfigureObjectiveIdentity(id);value.playOnStart=false;value.tutorialSteps=new[]{new TutorialStep{message=title+".",screenPosition=TutorialPosition.Left,showNextButton=false,canSkip=false,stepWaypoints=target==null?new List<GuiderWaypoint>():new List<GuiderWaypoint>{new GuiderWaypoint{target=target,advancesTutorial=false}}}};Mark(value);return value;}
    private static NPCProgressionPhase Phase(string id,Transform target,bool automatic=false)=>new NPCProgressionPhase{phaseId=id,targetLocation=target,dialoguePrompt="Talk to Mills",phaseDialogue=Dialogue(),repeatDialogue=true,playDialogueOnArrival=automatic,showExclamationMark=true,unlockFeatureIdAfterDialogue=string.Empty,featureUnlockDisplayName=string.Empty,showFeatureCollectPopup=false,onDialogueFinished=new UnityEvent(),onNPCInteracted=new UnityEvent()};
    private static Dialogue Dialogue(params string[] lines)=>new Dialogue{name="Mills",sentences=lines};
    private static NPCOptionalDialogueStep Say(params string[] lines)=>new NPCOptionalDialogueStep{dialogue=Dialogue(lines)};
    private static NPCOptionalDialogueStep ReferenceReview()=>new NPCOptionalDialogueStep{stepType=NPCOptionalDialogueStep.StepType.ReferenceReview,referenceTitle="Integrated Application",referenceDescription=ReviewText};
    private static void MoveAfter(NPCProgressionPhase phase,NPCProgressionManager npc,string id)=>phase.onDialogueFinishedPhaseMove=new NPCPhaseCall{npc=npc,phaseId=id};
    private static string Em(string value)=>"<b><#E09500>"+value+"</color></b>";
    private static void Mark(UnityEngine.Object value){EditorUtility.SetDirty(value);PrefabUtility.RecordPrefabInstancePropertyModifications(value);}
}
#endif
