#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Uses the existing persisted spawn rules to open Town River after both Silas turn-ins.</summary>
[InitializeOnLoad]
public static class MarshUnlockAuthoring
{
    public const string RequestPath = "Temp/marsh-unlock.request";
    public const string ReportPath = "Logs/marsh-unlock.txt";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string SilasData = "Assets/BridgeBuilder/Data/Contract/CanyonCrossing/Silas/";
    private static double nextPoll;
    static MarshUnlockAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
        try
        {
            if (command == "apply") Apply();
            else if (command == "validate") Validate();
            else throw new InvalidOperationException("Use apply or validate for Marsh's unlock.");
        }
        catch (Exception error) { Report("FAILED\n" + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Marsh Unlock/1. Apply After Silas Main Contracts")]
    public static void Apply()
    {
        Scene scene = Loaded(); NPCProgressionManager marsh = NPC(scene, "MarshNPC"), silas = NPC(scene, "SilasNPC");
        ContractSO first = Contract("SilasContract.asset"), second = Contract("MainContractSilas.asset");
        Require(first.ContractID == "SilasMainContract" && second.ContractID == "MainContractSilas" && first.isStoryModeContract && second.isStoryModeContract,
            "Silas's two main contract identities differ. No substitutes were assigned.");
        Require(silas.AuthoredPhases.Count(phase => phase.contract == first) == 1 && silas.AuthoredPhases.Count(phase => phase.contract == second) == 1,
            "Silas must still own both main contracts exactly once.");
        Require(All<NPCSpawnerCondition>(scene).All(gate => gate.gameObject == marsh.gameObject || gate.npcRoot != marsh.gameObject),
            "A separate controller already controls Marsh. Review it before applying a second gate.");
        NPCSpawnerCondition[] existing = marsh.GetComponents<NPCSpawnerCondition>();
        Require(existing.Length <= 1 && (existing.Length == 0 || Correct(existing[0], marsh, first, second)),
            "Marsh has a custom spawn rule. It was not overwritten.");
        if (existing.Length == 1 && !marsh.gameObject.activeSelf) { Validate(); return; }
        var components = All<Component>(scene).Where(item => item != null && !(item is Transform)).ToDictionary(item => item, EditorJsonUtility.ToJson);
        var poses = All<Transform>(scene).ToDictionary(item => item, EditorJsonUtility.ToJson);
        var activeObjects = All<Transform>(scene).Select(item => item.gameObject).Distinct().ToDictionary(item => item, item => item.activeSelf);
        var sceneSetup = EditorSceneManager.GetSceneManagerSetup(); Scene originalActive = SceneManager.GetActiveScene(); Object[] selection = Selection.objects;
        var hashes = new[] { first, second }.ToDictionary(item => AssetDatabase.GetAssetPath(item), item => Hash(AssetDatabase.GetAssetPath(item)));
        PlayerDataManager manager = PlayerDataManager.Instance; PlayerData player = manager != null ? manager.CurrentData : null;
        string playerBefore = player != null ? JsonUtility.ToJson(player) : null;
        bool initiallyDirty = scene.isDirty;
        string backup = "Logs/MarshUnlockBackups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"); Directory.CreateDirectory(backup);
        File.Copy(Canyon, backup + "/CanyonCrossing.unity");
        File.WriteAllText(backup + "/Marsh.loaded-before.json", EditorJsonUtility.ToJson(marsh));
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Unlock Marsh after both Silas main contracts");
        try
        {
            NPCSpawnerCondition gate = existing.Length == 1 ? existing[0] : Undo.AddComponent<NPCSpawnerCondition>(marsh.gameObject);
            Undo.RecordObject(gate, "Require both Silas turn-ins");
            gate.enabled = true; gate.requiredLessonName = string.Empty; gate.appearAfterLesson = true;
            gate.useMultipleConditions = true; gate.matchMode = NPCSpawnerCondition.MatchMode.All;
            gate.conditions = new List<NPCSpawnerCondition.Condition> {
                new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.ContractCompleted, contract = first, id = string.Empty, phaseId = string.Empty, mustBeTrue = true },
                new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.ContractCompleted, contract = second, id = string.Empty, phaseId = string.Empty, mustBeTrue = true }
            };
            gate.hideWhenMatched = false; gate.waitForManualSpawn = false; gate.persistManualOverride = false; gate.visibilitySaveId = string.Empty;
            gate.useDespawnConditions = false; gate.despawnConditions = new List<NPCSpawnerCondition.Condition>();
            gate.updateAutomatically = true; gate.npcRoot = marsh.gameObject;
            Undo.RecordObject(marsh.gameObject, "Start Marsh hidden until his unlock is evaluated"); marsh.gameObject.SetActive(false);
            EditorUtility.SetDirty(gate); EditorUtility.SetDirty(marsh.gameObject);
            if (PrefabUtility.IsPartOfPrefabInstance(gate)) PrefabUtility.RecordPrefabInstancePropertyModifications(gate);
            if (PrefabUtility.IsPartOfPrefabInstance(marsh.gameObject)) PrefabUtility.RecordPrefabInstancePropertyModifications(marsh.gameObject);
            ValidateScene(scene);
            foreach (var pair in components) Require(pair.Key != null && (pair.Key == gate || EditorJsonUtility.ToJson(pair.Key) == pair.Value), "An existing component changed: " + pair.Key);
            foreach (var pair in poses) Require(pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value, "A transform changed: " + pair.Key);
            foreach (var pair in activeObjects) Require(pair.Key == marsh.gameObject || pair.Key.activeSelf == pair.Value, "Another object's visibility changed.");
            Require(All<Component>(scene).Count(item => item != null) == components.Count + poses.Count + (existing.Length == 0 ? 1 : 0), "Unexpected components were created.");
            Require(hashes.All(pair => Hash(pair.Key) == pair.Value), "A Silas contract asset changed.");
            Require(manager == PlayerDataManager.Instance && (player == null || ReferenceEquals(player, manager.CurrentData) && playerBefore == JsonUtility.ToJson(player)), "Player data changed.");
            Require(SceneManager.GetActiveScene() == originalActive && Selection.objects.SequenceEqual(selection) && SameSetup(sceneSetup, EditorSceneManager.GetSceneManagerSetup()), "Scene setup or selection changed.");
            EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
            // Only a previously clean Canyon may be saved. Existing unsaved user edits stay unsaved.
            bool saved = !initiallyDirty && EditorSceneManager.SaveScene(scene);
            Report("PASS: Marsh appears only after both SilasMainContract and MainContractSilas have completion records.\n" +
                "PASS: existing spawn monitor rechecks inactive Marsh and restores visibility from saved progress; attention and story guidance use the same presence gate.\n" +
                "PASS: dialogue, phase IDs, objectives/history, contracts, budgets, loads, rewards, lessons, quizzes, Multiplayer and other NPCs preserved.\n" +
                "BACKUP: " + backup + "\nSCENE_WAS_DIRTY: " + initiallyDirty + "\nSAVED_CANYON: " + saved + "\nNEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    [MenuItem("Tools/Civil Craft/Story/Marsh Unlock/2. Validate Loaded Canyon")]
    public static void Validate()
    {
        Scene scene = Loaded(); ValidateScene(scene);
        Report("PASS: Marsh's exact two-contract unlock, hidden startup, automatic recheck and attention/objective presence guard verified.\nNEEDS_SAVE: " + (scene.isDirty ? "CanyonCrossing" : "none"));
    }
    private static void ValidateScene(Scene scene)
    {
        NPCProgressionManager marsh = NPC(scene, "MarshNPC"); var gates = marsh.GetComponents<NPCSpawnerCondition>();
        Require(gates.Length == 1 && Correct(gates[0], marsh, Contract("SilasContract.asset"), Contract("MainContractSilas.asset")) && !marsh.gameObject.activeSelf,
            "Marsh's unlock or hidden startup differs from both Silas main turn-ins.");
        Require(marsh.AuthoredPhases[0].phaseId == "MeetingMarsh" && marsh.AuthoredPhases[0].showExclamationMark && marsh.GetComponent<NPCStoryObjectiveGuidance>() != null,
            "Marsh's existing meeting, attention or objectives are missing.");
    }
    private static bool Correct(NPCSpawnerCondition gate, NPCProgressionManager npc, ContractSO first, ContractSO second) =>
        gate.enabled && gate.useMultipleConditions && gate.matchMode == NPCSpawnerCondition.MatchMode.All && gate.conditions != null && gate.conditions.Count == 2 &&
        gate.conditions.All(rule => rule != null && rule.kind == NPCSpawnerCondition.ConditionKind.ContractCompleted && rule.mustBeTrue) &&
        gate.conditions.Count(rule => rule.contract == first) == 1 && gate.conditions.Count(rule => rule.contract == second) == 1 &&
        !gate.hideWhenMatched && !gate.waitForManualSpawn && !gate.persistManualOverride && !gate.useDespawnConditions && gate.updateAutomatically && gate.npcRoot == npc.gameObject;
    private static NPCProgressionManager NPC(Scene scene, string id) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == id);
    private static ContractSO Contract(string name) => AssetDatabase.LoadAssetAtPath<ContractSO>(SilasData + name) ?? throw new InvalidOperationException("Missing Silas contract: " + name);
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static Scene Loaded()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
            PrefabStageUtility.GetCurrentPrefabStage() == null, "Stop Play Mode and finish importing in Edit Mode.");
        Scene scene = SceneManager.GetSceneByPath(Canyon); Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing; no scene will be opened or closed automatically."); return scene;
    }
    private static bool SameSetup(SceneSetup[] first, SceneSetup[] second) => first.Length == second.Length && first.Where((value, index) =>
        value.path != second[index].path || value.isActive != second[index].isActive || value.isLoaded != second[index].isLoaded).Count() == 0;
    private static string Hash(string path) { using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Report(string value) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, value); Debug.Log(value); }
}
#endif
