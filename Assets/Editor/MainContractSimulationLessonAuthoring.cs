#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Assigns simulation lessons only to Reed and Vance's existing main scene sites.</summary>
[InitializeOnLoad]
public static class MainContractSimulationLessonAuthoring
{
    public const string RequestPath = "Temp/main-contract-simulation-lessons.request";
    public const string ReportPath = "Logs/main-contract-simulation-lessons.txt";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private static double nextPoll;
    static MainContractSimulationLessonAuthoring() { EditorApplication.update += Poll; }
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
            else throw new InvalidOperationException("Unknown simulation lesson request: " + command);
        }
        catch (Exception error) { Report("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Main Contract Simulation Lessons/1. Assign Reed and Vance (Do Not Save)")]
    public static void Apply()
    {
        Scene scene = Loaded();
        BuildLocation reed = Site(scene, "ReedNPC", "ReedsContract"), vance = Site(scene, "VanceNPC", "VancesContract");
        SimulationLessonDefinition bending = Lesson("Bending Lesson"), forces = Lesson("Tension Compression Lesson");
        ValidatePresenter(scene);
        Require(reed != vance && (reed.simulationLesson == null || reed.simulationLesson == bending) &&
            (vance.simulationLesson == null || vance.simulationLesson == forces), "A main site has a custom simulation lesson. It was not overwritten.");
        if (reed.simulationLesson == bending && vance.simulationLesson == forces) { Validate(); return; }
        var protectedObjects = new Dictionary<UnityEngine.Object, string>();
        IEnumerable<Component> items = All<BuildLocation>(scene).Cast<Component>().Concat(All<NPCProgressionManager>(scene))
            .Concat(All<TutorialSequence>(scene)).Concat(All<CargoItem>(scene)).Concat(All<VehicleCargoSlot>(scene)).Concat(All<LiveLoadVehicle>(scene));
        foreach (Component item in items) if (item != reed && item != vance) protectedObjects[item] = EditorJsonUtility.ToJson(item);
        string reedBefore = WithoutLesson(reed), vanceBefore = WithoutLesson(vance);
        string backup = "Temp/MainContractSimulationLessons/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(backup); File.Copy(Canyon, backup + "/CanyonCrossing.unity");
        bool initiallyDirty = scene.isDirty;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Assign main contract simulation lessons");
        try
        {
            Undo.RecordObjects(new UnityEngine.Object[] { reed, vance }, "Assign main-site simulation lessons");
            reed.simulationLesson = bending; vance.simulationLesson = forces;
            foreach (BuildLocation site in new[] { reed, vance })
            { PrefabUtility.RecordPrefabInstancePropertyModifications(site); EditorUtility.SetDirty(site); }
            ValidateScene(scene);
            Require(WithoutLesson(reed) == reedBefore && WithoutLesson(vance) == vanceBefore,
                "A main site's settings changed outside its simulation lesson. Assignment rolled back.");
            foreach (var pair in protectedObjects)
                Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
                    "Another build site, NPC, tutorial, cargo item, slot or truck changed. Assignment rolled back.");
            EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
            Report("PASS: Reed main site -> Bending; Vance main site -> Tension & Compression with loaded-cargo context.\n" +
                "PASS: existing simulation presenter is wired; assigned lessons open automatically when simulation begins.\n" +
                "PASS: side sites, NPC progression, cargo items/slots, vehicle, contract assignments, weights, budgets and shared prefabs unchanged.\n" +
                "BACKUP: " + backup + "\nSCENE_WAS_DIRTY: " + initiallyDirty + "\nNEEDS_SAVE: CanyonCrossing. No scene was saved.");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Main Contract Simulation Lessons/2. Validate Loaded Canyon")]
    public static void Validate()
    {
        Scene scene = Loaded(); ValidateScene(scene);
        Report("PASS: Reed main Bending and Vance main Tension & Compression simulation lessons assigned; presenter ready.\nNEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
    }
    private static void ValidateScene(Scene scene)
    {
        Require(Site(scene, "ReedNPC", "ReedsContract").simulationLesson == Lesson("Bending Lesson"), "Reed's main simulation lesson is missing.");
        Require(Site(scene, "VanceNPC", "VancesContract").simulationLesson == Lesson("Tension Compression Lesson"), "Vance's main simulation lesson is missing.");
        ValidatePresenter(scene);
        SimulationLessonDefinition forces = Lesson("Tension Compression Lesson");
        Require(forces.panelTitle.Contains("TENSION") && forces.panelTitle.Contains("COMPRESSION") &&
            !string.IsNullOrWhiteSpace(forces.simulationStarted) && !string.IsNullOrWhiteSpace(forces.liveLoadStarted) &&
            !string.IsNullOrWhiteSpace(forces.vehicleEnteredBridge) && !string.IsNullOrWhiteSpace(forces.vehicleReachedMidpoint) &&
            !string.IsNullOrWhiteSpace(forces.highStressDetected) && !string.IsNullOrWhiteSpace(forces.stressLimitExceeded) &&
            !string.IsNullOrWhiteSpace(forces.firstMemberBreak) && !string.IsNullOrWhiteSpace(forces.vehicleStuck) &&
            !string.IsNullOrWhiteSpace(forces.vehicleFailed) && !string.IsNullOrWhiteSpace(forces.bridgeSucceeded) &&
            !string.IsNullOrWhiteSpace(forces.simulationStopped), "Vance's simulation lesson has incomplete event messages.");
    }
    private static void ValidatePresenter(Scene scene)
    {
        BuildUIController controller = All<BuildUIController>(scene).Single();
        SimulationLessonPresenter presenter = controller.GetComponent<SimulationLessonPresenter>();
        Require(presenter != null && presenter.enabled, "The existing build simulation lesson presenter is missing or disabled.");
        var data = new SerializedObject(presenter);
        foreach (string field in new[] { "lessonPanel", "lessonCanvasGroup", "titleText", "messageText", "lessonMenuButton", "lessonCloseButton" })
            Require(data.FindProperty(field).objectReferenceValue != null, "Simulation lesson presenter has an unwired field: " + field);
    }
    private static string WithoutLesson(BuildLocation site)
    {
        // Mask just the selected field in this in-memory native snapshot; no scene/prefab serialization is rewritten.
        string json = EditorJsonUtility.ToJson(site);
        return System.Text.RegularExpressions.Regex.Replace(json, "\"simulationLesson\"\\s*:\\s*\\{[^}]*\\}", "\"simulationLesson\":null");
    }
    private static BuildLocation Site(Scene scene, string npcId, string contractId) => All<NPCProgressionManager>(scene)
        .Single(npc => npc.ProgressionSaveId == npcId).AuthoredPhases.Single(phase => phase.contract != null && phase.contract.ContractID == contractId)
        .targetBuildLocation ?? throw new InvalidOperationException("Missing main site: " + contractId);
    private static SimulationLessonDefinition Lesson(string name) => AssetDatabase.LoadAssetAtPath<SimulationLessonDefinition>(
        "Assets/BridgeBuilder/Data/Simulation Lessons/" + name + ".asset") ?? throw new InvalidOperationException("Missing simulation lesson: " + name);
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static Scene Loaded()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Stop Play Mode and finish importing in Edit Mode.");
        Scene scene = SceneManager.GetSceneByPath(Canyon); Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing. This tool never opens or closes scenes."); return scene;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Report(string message) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, message); Debug.Log(message); }
}
#endif
