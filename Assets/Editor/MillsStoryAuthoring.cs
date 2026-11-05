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
internal static class MillsStoryAuthoring
{
    private const string Request="Temp/mills-heavy-equipment-story.request";
    private const string Report="Logs/mills-heavy-equipment-story.txt";
    private const string Data="Assets/BridgeBuilder/Data/";
    private const string RootName="Mills Heavy Equipment Story";
    private static readonly string[] Ids={"MeetingMills","MillsExplainSituation","MillsFailureLesson","MillsProjectBriefing","MillsFailureReview","MillsHeavyEquipmentComplete"};
    static MillsStoryAuthoring(){EditorApplication.update+=Poll;}
    private static void Poll()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;
        string command=File.ReadAllText(Request).Trim();File.Delete(Request);
        try{if(command=="inspect")Inspect();else if(command=="apply")Apply();else if(command=="validate")Validate();else throw new InvalidOperationException("Unknown Mills command.");}
        catch(Exception error){File.WriteAllText(Report,"FAIL: "+error);Debug.LogException(error);}
    }
    private static Scene Canyon()
    {var scene=SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");Require(scene.IsValid()&&scene.isLoaded&&!EditorApplication.isPlayingOrWillChangePlaymode,"Open CanyonCrossing in Edit Mode.");return scene;}
    private static T[] All<T>(Scene scene)where T:Component=>scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<T>(true)).ToArray();
    private static NPCProgressionManager Mills(Scene scene)=>All<NPCProgressionManager>(scene).Single(npc=>npc.name=="Mills");
    private static T Load<T>(string path)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(Data+path)??throw new InvalidOperationException("Missing "+path);
    private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    private static string PathOf(Transform value)=>value.parent==null?value.name:PathOf(value.parent)+"/"+value.name;
    [MenuItem("Tools/Civil Craft/Story/Mills/Inspect Heavy Equipment Site")]
    public static void Inspect()
    {
        var scene=Canyon();var report=new StringBuilder($"Mills inspection, dirty={scene.isDirty}\n");var mills=Mills(scene);
        report.AppendLine($"NPC {PathOf(mills.transform)} position={mills.transform.position:F3} saveID={mills.ProgressionSaveId} phases={mills.AuthoredPhases.Count}");
        foreach(var animator in mills.GetComponentsInChildren<Animator>(true))report.AppendLine($"Animator {PathOf(animator.transform)} pose={animator.transform.position:F3} active={animator.gameObject.activeInHierarchy} controller={animator.runtimeAnimatorController?.name}");
        foreach(var renderer in mills.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy))report.AppendLine($"Renderer {PathOf(renderer.transform)} bounds={renderer.bounds}");
        foreach(var site in All<BuildLocation>(scene).Where(v=>v.transform.position.x>2650f))
        {
            report.AppendLine($"SITE {PathOf(site.transform)} position={site.transform.position:F3} activeContract={site.activeContract?.name}");
            report.AppendLine("STARTS "+string.Join(", ",site.startingAnchors.Where(p=>p!=null).Select(p=>$"{p.name}={p.transform.position:F3}")));
            report.AppendLine("ENDS "+string.Join(", ",site.endingAnchors.Where(p=>p!=null).Select(p=>$"{p.name}={p.transform.position:F3}")));
        }
        MillsHeavyEquipmentSiteAuthoring.Inspect(scene,report);
        File.WriteAllText(Report,report.ToString());
    }
    [MenuItem("Tools/Civil Craft/Story/Mills/Apply Heavy Equipment Story")]
    public static void Apply()
    {
        var scene=Canyon();var mills=Mills(scene);
        if(mills.AuthoredPhases.Take(Ids.Length).Select(p=>p.phaseId).SequenceEqual(Ids)){Validate();return;}
        Require(mills.AuthoredPhases.Count==0&&!scene.GetRootGameObjects().Any(root=>root.name==RootName),"Review Mills's existing story before authoring.");
        Require(All<NPCProgressionManager>(scene).All(npc=>npc==mills||npc.ProgressionSaveId!="Mills"),"Mills needs a unique permanent save ID.");
        var contract=Load<ContractSO>("Contract/IndustrialZone/Mills Heavy Equipment Contract.asset");
        var lesson=Load<LessonData>("Lessons/Structural Failure.asset");
        var quiz=Load<QuizData>("Quized/StructuralFailureQuiz.asset");
        var simulation=Load<SimulationLessonDefinition>("Simulation Lessons/Structural Failure Lesson.asset");
        Require(quiz.assessmentMode&&quiz.QuestionCount==5&&quiz.TryValidate(out _),"Validate the five-question Structural Failure assessment.");
        var location=MillsHeavyEquipmentSiteAuthoring.FindLocation(scene);
        var (start,end)=MillsHeavyEquipmentSiteAuthoring.GetRoadAnchors(location);
        var template=All<CinematicDirector>(scene).Single(c=>c.cinematicID=="ClarkOffsetRoad-v1");
        var animator=mills.GetComponentsInChildren<Animator>(true).Single(a=>a.runtimeAnimatorController!=null);
        var actorVisual=animator.transform;
        Require(actorVisual.parent==mills.transform,"Mills needs his existing direct-child animated visual.");
        var giver=mills.GetComponent<NPCContractGiver>();var agent=mills.GetComponent<NavMeshAgent>();
        Require(giver!=null&&agent!=null,"Keep Mills's existing contract giver and navigation agent.");
        var (spawn,meeting,via)=FindWalk(scene,mills.transform.position,start.transform.position,location.transform.position);
        bool wasDirty=scene.isDirty;
        string backup="LocalBackups/MillsHeavyEquipment-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(backup);File.Copy(scene.path,backup+"/CanyonCrossing.unity");
        var originalOverrides=PrefabUtility.GetPropertyModifications(mills.gameObject);
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Author Mills Heavy Equipment Route");
        var report=new StringBuilder();
        try
        {
            var root=NewObject(scene,RootName).transform;
            var spawnMarker=Marker(scene,root,"Mills Spawn",spawn,mills.transform.rotation);
            var meetingMarker=Marker(scene,root,"Heavy Equipment Meeting",meeting,Quaternion.LookRotation(Vector3.right));
            Undo.RecordObjects(new UnityEngine.Object[]{mills,mills.transform,actorVisual,giver,animator},"Align Mills's model, interaction and story");
            // Align the visible character with its interaction/movement root without
            // changing the imported skin, animation controller or avatar.
            Vector3 visualDelta=mills.transform.position-actorVisual.position;visualDelta.y=0f;
            actorVisual.position+=visualDelta;
            mills.transform.position=spawn;
            float feet=mills.GetComponentsInChildren<Renderer>(true).Where(r=>r is SkinnedMeshRenderer&&r.enabled).Min(r=>r.bounds.min.y);
            actorVisual.position+=Vector3.up*(spawn.y+.01f-feet);
            animator.applyRootMotion=false;
            var follow=Objective(scene,root,"Story/Mills/FollowHeavyEquipment-v1","Follow Mills to the equipment crossing",meetingMarker,true);
            var intro=Phase(Ids[0],spawnMarker);
            intro.phaseDialogue=Dialogue("So, you're the engineer Clark has been telling me about.",
                "I'm "+Em("Mills")+". I've worked on Ironhaven's infrastructure for years, and I knew Professor Bhan before he began mentoring engineers.",
                "He teaches people to understand why a structure behaves as it does, rather than simply copying a design.",
                "You've handled some challenging projects. Now let's look at a crossing for heavier equipment. Come with me.");
            MoveAfter(intro,mills,Ids[1]);UnityEventTools.AddPersistentListener(intro.onDialogueFinished,follow.StartTutorial);
            var explain=Phase(Ids[1],meetingMarker);
            explain.useGroundedWaypoints=true;
            if(via.HasValue)explain.travelWaypoints.Add(Marker(scene,root,"Equipment walk waypoint",via.Value,Quaternion.identity));
            explain.phaseDialogue=Dialogue("Take a look at the site across the excavation.");
            UnityEventTools.AddPersistentListener(explain.onNPCInteracted,follow.Skip);
            var movie=CreateCinematic(scene,root,template,contract,start.transform.position,end.transform.position);
            UnityEventTools.AddPersistentListener(explain.onDialogueFinished,movie.PlayCinematic);
            var action=Undo.AddComponent<NPCPhaseAction>(movie.gameObject);var actionData=new SerializedObject(action);
            actionData.FindProperty("npc").objectReferenceValue=mills;actionData.FindProperty("phaseId").stringValue=Ids[2];actionData.ApplyModifiedProperties();
            UnityEventTools.AddPersistentListener(movie.OnCinematicFinished,action.MoveToSelectedPhase);
            var teach=Phase(Ids[2],meetingMarker,true);
            teach.optionalDialogueSequence=new List<NPCOptionalDialogueStep>
            {
                Say("The heavy equipment is stored on this side. The active construction site is across the excavation.",
                    "Machinery currently travels around the perimeter. A "+Em("direct crossing")+" would make moving the mobile crane much easier.",
                    "But a bridge's appearance doesn't tell us whether it can safely carry that load.",
                    "You've studied structural forces. Now let's consider what happens when a structure can no longer safely withstand them: "+Em("Structural Failure")+".",
                    "A problem can begin in one member or connection. It doesn't always mean the whole bridge collapses at once.",
                    "In a real structure, excessive bending, buckling, or a failed connection can stop it performing as intended.",
                    "Normal movement within safe limits is not automatically failure. During this test, inspect the stress colors and broken members to find where a problem begins."),
                Review(lesson)
            };
            MoveAfter(teach,mills,Ids[3]);
            var briefing=Phase(Ids[3],meetingMarker,true);briefing.contract=contract;briefing.targetBuildLocation=location;
            var review=Phase(Ids[4],meetingMarker,true);
            review.optionalDialogueSequence=new List<NPCOptionalDialogueStep>
            {
                Review(lesson),Say("Review "+Em("Structural Failure")+" in your Almanac. Open the "+Em("Quiz tab")+" and press "+Em("Start Quiz")+" when you're ready."),
                new NPCOptionalDialogueStep{stepType=NPCOptionalDialogueStep.StepType.Quiz,quiz=quiz,quizStepId="mills-structural-failure-assessment-v1",
                    quizWaitingReminder="Review where structural failure can begin, then open the Almanac Quiz tab and press Start Quiz when you're ready."}
            };
            MoveAfter(review,mills,Ids[5]);
            var done=Phase(Ids[5],meetingMarker);done.showExclamationMark=false;
            done.phaseDialogue=Dialogue("The equipment crews have their direct crossing now. Good work, {PlayerName}.",
                "Keep examining where a problem begins before changing a design. That will help with the projects ahead.");
            typeof(NPCProgressionManager).GetField("phases",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mills,new List<NPCProgressionPhase>{intro,explain,teach,briefing,review,done});
            var data=new SerializedObject(mills);
            data.FindProperty("progressionSaveId").stringValue="Mills";
            data.FindProperty("authoredSpawnLocation").objectReferenceValue=spawnMarker;
            data.FindProperty("contractGiver").objectReferenceValue=giver;data.FindProperty("navMeshAgent").objectReferenceValue=agent;
            data.FindProperty("animator").objectReferenceValue=animator;
            data.FindProperty("dialogueManager").objectReferenceValue=All<DialogueManager>(scene).Single(manager=>manager.isActiveAndEnabled);
            data.FindProperty("movementMode").enumValueIndex=(int)NPCProgressionMovementMode.Waypoints;
            data.FindProperty("waypointGroundLayers").intValue|=LayerMask.GetMask("Default");
            data.FindProperty("persistProgression").boolValue=true;data.FindProperty("restoreSavedPhaseBridgesOnStart").boolValue=true;
            data.FindProperty("onlyShowExclamationWhenInteractionNeeded").boolValue=true;data.ApplyModifiedProperties();Mark(mills);
            giver.advancesTutorial=false;Mark(giver);Mark(actorVisual);Mark(mills.transform);Mark(animator);
            MillsHeavyEquipmentSiteAuthoring.Apply(scene,location,contract,report);
            Undo.RecordObject(location,"Assign Structural Failure simulation lesson");location.enableSimulationLesson=true;location.simulationLesson=simulation;Mark(location);
            foreach(var tab in All<AlmanacLessonTab>(scene))
            {
                var tabData=new SerializedObject(tab);var lessons=tabData.FindProperty("allLessons");
                if(!Enumerable.Range(0,lessons.arraySize).Any(i=>lessons.GetArrayElementAtIndex(i).objectReferenceValue==lesson))
                {Undo.RecordObject(tab,"Register Structural Failure lesson");lessons.GetArrayElementAtIndex(lessons.arraySize++).objectReferenceValue=lesson;tabData.ApplyModifiedProperties();Mark(tab);}
            }
            var guidance=Undo.AddComponent<NPCStoryObjectiveGuidance>(mills.gameObject);
            guidance.entries=new List<NPCStoryObjectiveGuidance.Entry>
            {
                new NPCStoryObjectiveGuidance.Entry{phaseId=Ids[0],objective=Objective(scene,root,"Story/Mills/Find-v1","Find Mills at the industrial expansion site",mills.transform)},
                new NPCStoryObjectiveGuidance.Entry{phaseId=Ids[1],objective=Objective(scene,root,"Story/Mills/DiscussSite-v1","Talk to Mills at the equipment crossing",meetingMarker)},
                new NPCStoryObjectiveGuidance.Entry{phaseId=Ids[4],objective=Objective(scene,root,"Story/Mills/FailureQuiz-v1","Complete Mills's Structural Failure quiz",null)}
            };Mark(guidance);
            var gate=mills.GetComponent<NPCSpawnerCondition>();if(gate==null)gate=Undo.AddComponent<NPCSpawnerCondition>(mills.gameObject);else Undo.RecordObject(gate,"Gate Mills after Clark's second quiz");
            gate.npcRoot=mills.gameObject;gate.useMultipleConditions=true;gate.matchMode=NPCSpawnerCondition.MatchMode.All;gate.waitForManualSpawn=false;gate.hideWhenMatched=false;
            gate.conditions=new List<NPCSpawnerCondition.Condition>{new NPCSpawnerCondition.Condition{kind=NPCSpawnerCondition.ConditionKind.ContractCompleted,contract=Load<ContractSO>("Contract/IndustrialZone/Clark Offset Road Contract.asset")},
                new NPCSpawnerCondition.Condition{kind=NPCSpawnerCondition.ConditionKind.NPCAtPhase,id="Clark",phaseId="ClarkResolved"}};Mark(gate);
            Check(scene,mills,report);
            Undo.RecordObject(mills.gameObject,"Start Mills hidden until his story unlocks");mills.gameObject.SetActive(false);Mark(mills.gameObject);
            Undo.FlushUndoRecordObjects();EditorSceneManager.MarkSceneDirty(scene);
            bool saved=!wasDirty&&EditorSceneManager.SaveScene(scene);Undo.CollapseUndoOperations(group);
            report.AppendLine(saved?"SAVED: CanyonCrossing":"NEEDS_SAVE: current scene edits preserved; save CanyonCrossing after review.");
            report.AppendLine("Provisional balance: 30,000 kg and 1,000,000 budget. Temporary crane tires have physics but no separate visible wheel animation.");
            report.AppendLine("Backup: "+backup);File.WriteAllText(Report,report.ToString());
        }
        catch{Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(group);if(originalOverrides!=null)PrefabUtility.SetPropertyModifications(mills.gameObject,originalOverrides);throw;}
    }
    [MenuItem("Tools/Civil Craft/Story/Mills/Validate Heavy Equipment Story")]
    public static void Validate(){var scene=Canyon();var report=new StringBuilder();Check(scene,Mills(scene),report);report.AppendLine("Scene dirty="+scene.isDirty);File.WriteAllText(Report,report.ToString());}
    internal static void Check(Scene scene,NPCProgressionManager mills,StringBuilder report)
    {
        var phases=mills.AuthoredPhases;
        Require(mills.ProgressionSaveId=="Mills"&&phases.Take(6).Select(p=>p.phaseId).SequenceEqual(Ids)&&(phases.Count==6||MillsWorkersCrossingAuthoring.HasWorkersCrossing(mills)),"Preserve Mills's first six stable phase identities.");
        var root=scene.GetRootGameObjects().Single(r=>r.name==RootName).transform;var spawn=root.Find("Mills Spawn");var wait=root.Find("Heavy Equipment Meeting");
        Require(spawn!=null&&wait!=null&&phases[0].targetLocation==spawn&&phases.Skip(1).Take(4).All(p=>p.targetLocation!=null&&p.targetLocation==wait),"Every first-contract Mills phase needs its canonical authored target.");
        if(phases.Count==6)Require(phases[5].targetLocation==wait,"Keep the first-story terminal meeting target.");
        var data=new SerializedObject(mills);Require(data.FindProperty("authoredSpawnLocation").objectReferenceValue==spawn,"Missing Mills spawn reference.");
        foreach(int index in new[]{0,2,4})Require(phases[index].onDialogueFinishedPhaseMove.npc==mills&&phases[index].onDialogueFinishedPhaseMove.phaseId==Ids[index+1],"Missing next-phase callback for "+Ids[index]);
        var contract=Load<ContractSO>("Contract/IndustrialZone/Mills Heavy Equipment Contract.asset");var location=MillsHeavyEquipmentSiteAuthoring.FindLocation(scene);
        Require(phases.Take(6).Count(p=>p.contract!=null)==1&&phases[3].contract==contract&&phases[3].targetBuildLocation==location,"Mills needs one first-main-contract offer bound to his exact site.");
        Require(phases[2].optionalDialogueSequence.Any(s=>s.lesson==Load<LessonData>("Lessons/Structural Failure.asset")),"Missing Structural Failure introduction.");
        var review=phases[4].optionalDialogueSequence;
        Require(review.Count==3&&review[0].lesson==Load<LessonData>("Lessons/Structural Failure.asset")&&review[2].stepType==NPCOptionalDialogueStep.StepType.Quiz&&review[2].quiz.assessmentMode&&review[2].quiz.QuestionCount==5&&review[2].quiz.TryValidate(out _),"Turn-in must lead through lesson review and the any-score five-question quiz.");
        Require(location.enableSimulationLesson&&location.simulationLesson==Load<SimulationLessonDefinition>("Simulation Lessons/Structural Failure Lesson.asset"),"Mills must use the Structural Failure simulation lesson.");
        foreach(var tab in All<AlmanacLessonTab>(scene)){var lessons=new SerializedObject(tab).FindProperty("allLessons");Require(Enumerable.Range(0,lessons.arraySize).Count(i=>lessons.GetArrayElementAtIndex(i).objectReferenceValue==Load<LessonData>("Lessons/Structural Failure.asset"))==1,"Register the lesson once in each Almanac.");}
        var follow=root.GetComponentsInChildren<TutorialSequence>(true).Single(t=>t.lessonName=="Story/Mills/FollowHeavyEquipment-v1");var movie=All<CinematicDirector>(scene).Single(c=>c.cinematicID=="MillsHeavyEquipment-v1");
        Require(HasCall(phases[0].onDialogueFinished,follow,"StartTutorial")&&HasCall(phases[1].onNPCInteracted,follow,"Skip")&&HasCall(phases[1].onDialogueFinished,movie,"PlayCinematic"),"Keep Mills's follow and overview callbacks.");
        Require(mills.GetComponent<NPCStoryObjectiveGuidance>().entries.Count>=3,"Missing Mills objective guidance.");
        var gate=mills.GetComponent<NPCSpawnerCondition>();Require(gate!=null&&gate.useMultipleConditions&&gate.matchMode==NPCSpawnerCondition.MatchMode.All&&gate.conditions.Count==2&&
            gate.conditions.Any(c=>c.kind==NPCSpawnerCondition.ConditionKind.ContractCompleted&&c.contract==Load<ContractSO>("Contract/IndustrialZone/Clark Offset Road Contract.asset"))&&
            gate.conditions.Any(c=>c.kind==NPCSpawnerCondition.ConditionKind.NPCAtPhase&&c.id=="Clark"&&c.phaseId=="ClarkResolved"),"Mills must unlock after Clark's second contract and quiz.");
        var animator=(Animator)data.FindProperty("animator").objectReferenceValue;
        Require(animator!=null&&Vector2.Distance(new Vector2(animator.transform.position.x,animator.transform.position.z),new Vector2(mills.transform.position.x,mills.transform.position.z))<.1f,"Align Mills's visible model and interaction root.");
        MillsHeavyEquipmentSiteAuthoring.Validate(scene,location,contract,report);
        ClarkProgressionTargets.Check(scene,All<NPCProgressionManager>(scene).Single(n=>n.ProgressionSaveId=="Clark"),report);
        for(int i=0;i<6;i++)report.AppendLine($"PASS Mills {i} {Ids[i]}: {phases[i].targetLocation.name} at {phases[i].targetLocation.position:F2}");
        report.AppendLine("PASS: Clark-to-Mills gate, all phase/spawn targets, overview, follow objectives, exact contract site, lesson introduction/Almanac/simulation/review, five-question assessment and next-project idle phase.");
    }
    private static (Vector3 spawn,Vector3 meeting,Vector3? via) FindWalk(Scene scene,Vector3 spawn,Vector3 bank,Vector3 workbench)
    {
        Physics.SyncTransforms();Require(Ground(spawn,out var start),"Mills's interaction root needs walkable ground.");
        foreach(var candidate in new[]{bank+new Vector3(-12,0,6),bank+new Vector3(-16,0,8),workbench+Vector3.forward*3,workbench-Vector3.forward*3})
        {
            if(!Ground(candidate,out var target))continue;if(Walk(start,target))return(start,target,null);
            foreach(var raw in new[]{new Vector3(start.x,start.y,target.z),new Vector3(target.x,start.y,start.z)})
                if(Ground(raw,out var via)&&Walk(start,via)&&Walk(via,target))return(start,target,via);
        }
        throw new InvalidOperationException("No continuous short grounded route from Mills to his workbench could be verified.");
        bool Ground(Vector3 point,out Vector3 result){var hits=Physics.RaycastAll(point+Vector3.up*3,Vector3.down,6,LayerMask.GetMask("Default","Ground","Environment"),QueryTriggerInteraction.Ignore).Where(h=>h.collider.gameObject.scene==scene&&h.normal.y>.7f&&Mathf.Abs(h.point.y-bank.y)<1.5f&&h.collider.GetComponentInParent<LiveLoadVehicle>()==null).OrderBy(h=>Mathf.Abs(h.point.y-bank.y)).ToArray();result=hits.Length>0?hits[0].point:point;return hits.Length>0;}
        bool Walk(Vector3 a,Vector3 b){int samples=Mathf.CeilToInt(Vector3.Distance(a,b)/.65f);var previous=a;for(int i=1;i<=samples;i++){if(!Ground(Vector3.Lerp(a,b,i/(float)samples),out var p)||Mathf.Abs(p.y-previous.y)>.48f)return false;previous=p;}return true;}
    }
    private static CinematicDirector CreateCinematic(Scene scene,Transform root,CinematicDirector template,ContractSO contract,Vector3 from,Vector3 to)
    {
        var movie=Undo.AddComponent<CinematicDirector>(NewObject(scene,"Heavy Equipment Overview",root));movie.cinematicID="MillsHeavyEquipment-v1";movie.playOnStart=movie.playOnlyOnce=false;
        movie.cinematicCamera=template.cinematicCamera;movie.playerActor=template.playerActor;movie.playerAnimator=template.playerAnimator;movie.cameraTransitionDuration=template.cameraTransitionDuration;movie.cameraReturnDuration=template.cameraReturnDuration;movie.hudElementsToHide=new List<GameObject>(template.hudElementsToHide);movie.contractToDisplay=contract;movie.OnCinematicFinished=new UnityEvent();
        movie.shots=new List<CinematicShot>{Shot("Equipment storage",from+new Vector3(-15,18,35),from+new Vector3(-12,16,30),from+Vector3.left*10),Shot("Excavation",(from+to)*.5f+new Vector3(0,30,50),(from+to)*.5f+new Vector3(0,26,45),(from+to)*.5f),Shot("Active work site",to+new Vector3(10,22,35),to+new Vector3(7,19,30),to)};Mark(movie);return movie;
        CinematicShot Shot(string name,Vector3 a,Vector3 b,Vector3 focus)=>new CinematicShot{shotName=name,duration=3,postShotDelay=.1f,cameraStartPoint=Marker(scene,movie.transform,name+" Start",a,Quaternion.LookRotation(focus-a)),cameraEndPoint=Marker(scene,movie.transform,name+" End",b,Quaternion.LookRotation(focus-b))};
    }
    private static GameObject NewObject(Scene scene,string name,Transform parent=null){var obj=new GameObject(name);SceneManager.MoveGameObjectToScene(obj,scene);Undo.RegisterCreatedObjectUndo(obj,"Create Mills story object");if(parent!=null)Undo.SetTransformParent(obj.transform,parent,"Parent Mills story object");return obj;}
    private static Transform Marker(Scene scene,Transform root,string name,Vector3 position,Quaternion rotation){var value=NewObject(scene,name,root).transform;value.SetPositionAndRotation(position,rotation);return value;}
    private static TutorialSequence Objective(Scene scene,Transform root,string id,string title,Transform target,bool follow=false){var value=Undo.AddComponent<TutorialSequence>(NewObject(scene,title,root));value.lessonName=id;value.showInObjectives=true;value.objectiveTitle=title;value.ConfigureObjectiveIdentity(id);value.playOnStart=false;value.tutorialSteps=new[]{new TutorialStep{message=title+".",screenPosition=TutorialPosition.Left,showNextButton=false,canSkip=follow,stepWaypoints=target==null?new List<GuiderWaypoint>():new List<GuiderWaypoint>{new GuiderWaypoint{target=target,advancesTutorial=follow}}}};Mark(value);return value;}
    private static NPCProgressionPhase Phase(string id,Transform target,bool automatic=false)=>new NPCProgressionPhase{phaseId=id,targetLocation=target,dialoguePrompt="Talk to Mills",phaseDialogue=Dialogue(),repeatDialogue=true,playDialogueOnArrival=automatic,showExclamationMark=true,unlockFeatureIdAfterDialogue=string.Empty,featureUnlockDisplayName=string.Empty,showFeatureCollectPopup=false,onDialogueFinished=new UnityEvent(),onNPCInteracted=new UnityEvent()};
    private static Dialogue Dialogue(params string[] lines)=>new Dialogue{name="Mills",sentences=lines};
    private static NPCOptionalDialogueStep Say(params string[] lines)=>new NPCOptionalDialogueStep{dialogue=Dialogue(lines)};
    private static NPCOptionalDialogueStep Review(LessonData lesson)=>new NPCOptionalDialogueStep{stepType=NPCOptionalDialogueStep.StepType.LessonIntroduction,lesson=lesson};
    private static void MoveAfter(NPCProgressionPhase phase,NPCProgressionManager npc,string id)=>phase.onDialogueFinishedPhaseMove=new NPCPhaseCall{npc=npc,phaseId=id};
    private static string Em(string value)=>"<b><#E09500>"+value+"</color></b>";
    private static bool HasCall(UnityEvent action,UnityEngine.Object target,string method){if(action==null||target==null)return false;for(int i=0;i<action.GetPersistentEventCount();i++)if(action.GetPersistentTarget(i)==target&&action.GetPersistentMethodName(i)==method&&action.GetPersistentListenerState(i)!=UnityEventCallState.Off)return true;return false;}
    private static void Mark(UnityEngine.Object value){EditorUtility.SetDirty(value);PrefabUtility.RecordPrefabInstancePropertyModifications(value);}
}
#endif
