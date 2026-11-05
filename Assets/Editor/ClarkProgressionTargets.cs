#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>Repair only missing references; preserve dialogue, stable IDs, saves and existing markers.</summary>
[InitializeOnLoad]
internal static class ClarkProgressionTargets
{
    private const string Request = "Temp/clark-progression-targets.request";
    private const string Report = "Logs/clark-progression-targets.txt";
    private static readonly string[] FirstIds = {"MeetingClark", "ExplainSituation", "MaterialIntroduction", "LessonIntroduction", "ProjectBriefing", "ClarkFreightReview", "FreightPassageComplete"};
    static ClarkProgressionTargets() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if(command == "repair") Repair(); else if(command == "validate") Validate(); else throw new InvalidOperationException("Unknown target command."); }
        catch(Exception error) { File.WriteAllText(Report,"FAIL: " + error); Debug.LogException(error); }
    }
    private static Scene Canyon()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.IsValid() && scene.isLoaded && !EditorApplication.isPlayingOrWillChangePlaymode,"Open CanyonCrossing in Edit Mode.");
        return scene;
    }
    private static NPCProgressionManager Clark(Scene scene) => scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<NPCProgressionManager>(true)).Single(npc=>npc.ProgressionSaveId=="Clark");
    private static Transform Marker(Scene scene,string rootName,string markerName)
    {
        var root=scene.GetRootGameObjects().Single(obj=>obj.name==rootName);
        var marker=root.transform.Find(markerName);
        Require(marker!=null,$"Missing authored marker {rootName}/{markerName}; stop instead of substituting another location.");
        return marker;
    }
    private static TutorialSequence Follow(Scene scene) => scene.GetRootGameObjects().Single(root=>root.name=="Clark Freight Passage Story")
        .GetComponentsInChildren<TutorialSequence>(true).Single(t=>t.lessonName=="Story/Clark/FollowToFreightPassage-v1");
    private static CinematicDirector Movie(Scene scene) => scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<CinematicDirector>(true)).Single(c=>c.cinematicID=="ClarkFreightPassage-v1");
    [MenuItem("Tools/Civil Craft/Story/Clark/Repair Missing Progression Targets")]
    public static void Repair()
    {
        Scene scene=Canyon(); var clark=Clark(scene); bool wasDirty=scene.isDirty;
        var spawn=Marker(scene,"Clark Freight Passage Story","Spawn");
        var yard=Marker(scene,"Clark Freight Passage Story","Freight Yard");
        var follow=Follow(scene); var movie=Movie(scene);
        Require(clark.AuthoredPhases.Take(7).Select(p=>p.phaseId).SequenceEqual(FirstIds),"Review Clark's phase IDs before repairing references.");
        var expected=new Dictionary<string,UnityEngine.Object>
        {
            {"authoredSpawnLocation",spawn},
            {"phases.Array.data[0].targetLocation",spawn},
            {"phases.Array.data[4].targetBuildLocation",ClarkFreightSiteAuthoring.FindLocation(scene)},
            {"phases.Array.data[0].onDialogueFinished.m_PersistentCalls.m_Calls.Array.data[0].m_Target",follow},
            {"phases.Array.data[1].onDialogueFinished.m_PersistentCalls.m_Calls.Array.data[0].m_Target",movie},
            {"phases.Array.data[1].onNPCInteracted.m_PersistentCalls.m_Calls.Array.data[0].m_Target",follow}
        };
        for(int index=1;index<=5;index++) expected.Add($"phases.Array.data[{index}].targetLocation",yard);
        foreach(int index in new[]{0,2,3,5}) expected.Add($"phases.Array.data[{index}].onDialogueFinishedPhaseMove.npc",clark);
        if(ClarkOffsetRoadAuthoring.HasOffsetRoad(clark))
        {
            var factory=Marker(scene,"Clark Offset Road Story","Factory Access Meeting");
            for(int index=6;index<12;index++) expected.Add($"phases.Array.data[{index}].targetLocation",factory);
        }
        var data=new SerializedObject(clark);
        foreach(var entry in expected) Require(data.FindProperty(entry.Key)!=null,"Missing serialized property: " + entry.Key);
        string backup="LocalBackups/ClarkTargetRepair-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path,backup+"/CanyonCrossing.unity");
        // Diagnostic only. Never use JSON round trips to restore scene-object references.
        File.WriteAllText(backup+"/Clark-before.json",EditorJsonUtility.ToJson(clark,true));
        var report=new StringBuilder();
        Undo.RecordObject(clark,"Restore Clark's missing story references");
        foreach(var entry in expected)
        {
            var property=data.FindProperty(entry.Key);
            if(property.objectReferenceValue!=null) continue;
            property.objectReferenceValue=entry.Value;
            report.AppendLine("RESTORED: "+entry.Key+" -> "+entry.Value.name);
        }
        data.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(clark); EditorUtility.SetDirty(clark);
        Check(scene,clark,report);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved=!wasDirty && EditorSceneManager.SaveScene(scene);
        report.AppendLine(saved ? "SAVED: CanyonCrossing" : "NEEDS_SAVE: current scene edits preserved; save CanyonCrossing.");
        report.AppendLine("Backup: "+backup);
        File.WriteAllText(Report,report.ToString());
    }
    [MenuItem("Tools/Civil Craft/Story/Clark/Validate All Progression Targets")]
    public static void Validate()
    {
        var scene=Canyon(); var report=new StringBuilder(); Check(scene,Clark(scene),report);
        report.AppendLine("Scene dirty="+scene.isDirty); File.WriteAllText(Report,report.ToString());
    }
    internal static void Check(Scene scene,NPCProgressionManager clark,StringBuilder report)
    {
        var phases=clark.AuthoredPhases;
        Require(phases.Count==7 || ClarkOffsetRoadAuthoring.HasOffsetRoad(clark),"Clark needs the authored seven or twelve phases.");
        Require(phases.Take(7).Select(p=>p.phaseId).SequenceEqual(FirstIds),"Preserve Clark's original phase identities.");
        var spawn=Marker(scene,"Clark Freight Passage Story","Spawn");
        var yard=Marker(scene,"Clark Freight Passage Story","Freight Yard");
        var data=new SerializedObject(clark);
        Require(data.FindProperty("authoredSpawnLocation").objectReferenceValue==spawn,"Clark's authored Spawn reference is missing or incorrect.");
        Require(phases[0].targetLocation==null || phases[0].targetLocation==spawn,"Clark's first meeting must use his authored Spawn.");
        for(int index=1;index<=5;index++) Require(phases[index].targetLocation!=null && phases[index].targetLocation==yard,
            "Missing Freight Yard target for "+phases[index].phaseId);
        if(phases.Count==12)
        {
            var factory=Marker(scene,"Clark Offset Road Story","Factory Access Meeting");
            for(int index=6;index<12;index++) Require(phases[index].targetLocation!=null && phases[index].targetLocation==factory,
                "Missing factory access target for "+phases[index].phaseId);
            Require(phases[6].teleportToTargetLocation,"Keep Clark's post-first-quiz teleport enabled.");
        }
        else Require(phases[6].targetLocation==yard,"Keep the first story's terminal Freight Yard target.");
        Require(phases[4].targetBuildLocation==ClarkFreightSiteAuthoring.FindLocation(scene),"Clark's first contract lost its build location.");
        foreach(var pair in new[]{(0,"ExplainSituation"),(2,"LessonIntroduction"),(3,"ProjectBriefing"),(5,"FreightPassageComplete")})
            Require(phases[pair.Item1].onDialogueFinishedPhaseMove.npc==clark && phases[pair.Item1].onDialogueFinishedPhaseMove.phaseId==pair.Item2,
                "Missing next-phase NPC binding for "+phases[pair.Item1].phaseId);
        Require(HasCall(phases[0].onDialogueFinished,Follow(scene),"StartTutorial"),"The first meeting must start Clark's follow guidance.");
        Require(HasCall(phases[1].onNPCInteracted,Follow(scene),"Skip"),"Talking at the Freight Yard must close its follow guidance.");
        Require(HasCall(phases[1].onDialogueFinished,Movie(scene),"PlayCinematic"),"Freight Yard dialogue must play its overview.");
        for(int index=0;index<phases.Count;index++)
        {
            var target=phases[index].targetLocation!=null ? phases[index].targetLocation : spawn;
            Require(target.gameObject.scene==scene && target.gameObject.activeInHierarchy,"Unavailable target for "+phases[index].phaseId);
            report.AppendLine($"PASS {index} {phases[index].phaseId}: {target.name} at {target.position:F2}");
        }
        report.AppendLine("PASS: spawn, every phase target, first workbench, phase-move NPCs, follow callbacks, overview callback and post-quiz teleport.");
    }
    private static bool HasCall(UnityEvent action,UnityEngine.Object target,string method)
    { if(action==null || target==null)return false;for(int i=0;i<action.GetPersistentEventCount();i++)if(action.GetPersistentTarget(i)==target && action.GetPersistentMethodName(i)==method && action.GetPersistentListenerState(i)!=UnityEventCallState.Off)return true;return false; }
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
}
#endif
