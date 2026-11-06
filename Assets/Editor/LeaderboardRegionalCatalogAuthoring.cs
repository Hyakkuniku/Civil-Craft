#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in leaderboard eligibility rollout. Never edits scenes, scores or display policy.</summary>
[InitializeOnLoad]
public static class LeaderboardRegionalCatalogAuthoring
{
    private const string Request = "Temp/leaderboard-regional-catalog.request";
    private const string Report = "Logs/leaderboard-regional-catalog.txt";
    private const string ContractFolder = "Assets/BridgeBuilder/Data/Contract";
    private const string SaveManagerGuid = "fbe92ff9608ed654894f6ceb2d977492";
    private static readonly string[] Existing = { "ShopKeeper", "TUT_CONTRACT1", "TUT_CONTRACT2", "ReedsContract", "VancesContract", "ReedSideInspection", "VanceSideRealignment", "SilasMainContract", "MainContractSilas" };
    private static readonly string[] Added = { "MarshID", "FordMainContract", "BrooksMainContract", "RonanMainContract", "ClarkMainContract", "ClarkOffsetRoad", "MillsMainContract", "MillsWorkersCrossing", "MillsReconnectingArcadia" };
    private static readonly string[] Scenes = { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/CanyonCrossing.unity" };
    private static bool running;
    private static double next;
    static LeaderboardRegionalCatalogAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            AssetDatabase.IsAssetImportWorkerProcess() || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request); Run(command);
    }
    [MenuItem("Tools/Civil Craft/Contracts/Regional Leaderboards/Apply")]
    public static void Apply() => Run("apply");
    [MenuItem("Tools/Civil Craft/Contracts/Regional Leaderboards/Validate")]
    public static void Validate() => Run("validate");

    private static void Run(string command)
    {
        running = true; Directory.CreateDirectory("Logs");
        var report = new StringBuilder();
        var originals = new Dictionary<ContractSO, string>();
        var dirtyBefore = new Dictionary<ContractSO, bool>();
        var changed = new List<ContractSO>();
        SceneSetup[] sceneSetup = EditorSceneManager.GetSceneManagerSetup();
        var sceneDirty = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToDictionary(scene => scene, scene => scene.isDirty);
        bool saving = false;
        try
        {
            Require(command == "apply" || command == "validate", "Use apply or validate.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating,
                "Run in idle Edit Mode after compilation.");
            ContractSO[] all = AssetDatabase.FindAssets("t:ContractSO", new[] { ContractFolder }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ContractSO>).ToArray();
            Require(all.Length == 19 && all.Select(contract => contract.ContractID).Distinct(StringComparer.Ordinal).Count() == 19,
                "Expected 19 unique audited contracts including the excluded 123 test asset.");
            var catalog = all.ToDictionary(contract => contract.ContractID, StringComparer.Ordinal);
            string[] supported = Existing.Concat(Added).ToArray();
            Require(supported.All(catalog.ContainsKey) && catalog.ContainsKey("123"), "A required production contract or excluded test asset is missing.");
            string feed = "{\"schemaVersion\":1,\"contractIds\":[" + string.Join(",", supported.Select(id => "\"" + id + "\"")) + "]}";
            Require(LeaderboardContractVisibility.TryParse(feed, out HashSet<string> parsed) && parsed.SetEquals(supported),
                "Refresh Unity: the shared leaderboard display-policy parser must support all 18 production IDs before eligibility is enabled.");
            foreach (ContractSO contract in all) { originals[contract] = EditorJsonUtility.ToJson(contract); dirtyBefore[contract] = EditorUtility.IsDirty(contract); }
            ValidateSceneFallbacks(catalog, supported, report);
            if (command == "apply")
            {
                foreach (string id in Added)
                {
                    ContractSO contract = catalog[id];
                    if (!contract.hideFromLeaderboard) continue;
                    Undo.RecordObject(contract, "Enable regional contract leaderboard eligibility");
                    contract.hideFromLeaderboard = false; EditorUtility.SetDirty(contract); changed.Add(contract);
                }
            }
            foreach (ContractSO contract in all)
            {
                string before = originals[contract], after = EditorJsonUtility.ToJson(contract);
                if (Added.Contains(contract.ContractID))
                {
                    Require(!contract.hideFromLeaderboard && Protected(after) == Protected(before),
                        "Only leaderboard eligibility may change: " + AssetDatabase.GetAssetPath(contract));
                    report.AppendLine("PASS: " + contract.ContractID + " is locally eligible; all names, IDs, materials, quantities, budgets, loads and progression fields preserved.");
                }
                else Require(before == after, "An existing Canyon/tutorial/side/test contract changed: " + contract.ContractID);
            }
            if (command == "apply")
            {
                string backup = "LocalBackups/LeaderboardRegionalCatalog-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                foreach (ContractSO contract in changed)
                {
                    string path = AssetDatabase.GetAssetPath(contract), destination = backup + "/" + path.Substring(ContractFolder.Length + 1);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination, false);
                    if (dirtyBefore[contract]) File.WriteAllText(destination + ".loaded-before.json", originals[contract]);
                }
                saving = true;
                foreach (ContractSO contract in changed)
                {
                    if (dirtyBefore[contract]) report.AppendLine("UNSAVED: " + AssetDatabase.GetAssetPath(contract) + "; existing Inspector edits preserved with the eligibility change.");
                    else { AssetDatabase.SaveAssetIfDirty(contract); Require(!EditorUtility.IsDirty(contract), "Unity did not save " + contract.name); report.AppendLine("SAVED: " + AssetDatabase.GetAssetPath(contract)); }
                }
                report.AppendLine("BACKUP: " + backup);
            }
            Require(EditorSceneManager.GetSceneManagerSetup().Select(SceneKey).SequenceEqual(sceneSetup.Select(SceneKey)), "Scene setup changed.");
            foreach (var entry in sceneDirty) Require(entry.Key.isDirty == entry.Value, "Scene dirty state changed: " + entry.Key.path);
            foreach (var entry in dirtyBefore) Require(EditorUtility.IsDirty(entry.Key) == entry.Value, "Original asset dirty state changed: " + entry.Key.name);
            report.AppendLine("PASS: Existing nine Canyon/tutorial/side leaderboard flags and the 123 test asset are unchanged. No scene reauthoring is required: all three saved PlayerDataManager catalogs already supply the 18 eligible contracts through the leaderboard's existing union fallback.");
            report.AppendLine("PASS: This only enables local eligibility. The administrator's public display selection remains authoritative; no website policy, PlayFab score/statistic, player data, scene or material asset was changed.");
            report.AppendLine("RESULT: PASS; command=" + command + "; UTC=" + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception error)
        {
            if (!saving) foreach (ContractSO contract in changed)
            {
                EditorJsonUtility.FromJsonOverwrite(originals[contract], contract);
                if (dirtyBefore[contract]) EditorUtility.SetDirty(contract); else EditorUtility.ClearDirty(contract);
            }
            report.AppendLine("FAIL: " + error); Debug.LogException(error);
        }
        finally { File.WriteAllText(Report, report.ToString()); running = false; }
    }
    private static void ValidateSceneFallbacks(Dictionary<string, ContractSO> catalog, string[] ids, StringBuilder report)
    {
        foreach (string path in Scenes)
        {
            string text = File.ReadAllText(path), marker = "guid: " + SaveManagerGuid;
            int script = text.IndexOf(marker, StringComparison.Ordinal);
            Require(script >= 0 && text.IndexOf(marker, script + marker.Length, StringComparison.Ordinal) < 0,
                "Expected one saved PlayerDataManager in " + path);
            int end = text.IndexOf("\n--- ", script, StringComparison.Ordinal);
            string component = text.Substring(script, (end < 0 ? text.Length : end) - script);
            // The component slice ends before the separator's newline, so its final
            // serialized item can end at EOF rather than with a line break.
            Match list = Regex.Match(component, @"(?ms)^  allGameContracts:\r?\n(?<items>(?:  - [^\r\n]*(?:\r?\n|$))*)");
            Require(list.Success, "Saved fallback catalog is missing in " + path);
            var guids = new HashSet<string>(Regex.Matches(list.Groups["items"].Value, @"guid: (\w+)").Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.Ordinal);
            foreach (string id in ids) Require(guids.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog[id]))), "The saved PlayerDataManager catalog cannot expose " + id + " in " + path);
            report.AppendLine("PASS: " + path + " has all 18 production contracts in its saved fallback catalog; existing scene lists left untouched.");
        }
        foreach (PlayerDataManager manager in Resources.FindObjectsOfTypeAll<PlayerDataManager>())
        {
            if (manager == null || !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded ||
                EditorSceneManager.IsPreviewScene(manager.gameObject.scene)) continue;
            foreach (string id in ids) Require(manager.allGameContracts != null && manager.allGameContracts.Any(contract => contract != null && contract.ContractID == id),
                "Loaded PlayerDataManager has an unsaved catalog missing " + id + "; preserve and review that Inspector edit first.");
        }
    }
    private static string Protected(string json) => Regex.Replace(json, "\\\"hideFromLeaderboard\\\"\\s*:\\s*(?:true|false)", "\"hideFromLeaderboard\":false");
    private static string SceneKey(SceneSetup setup) => setup.path + ":" + setup.isLoaded + ":" + setup.isActive;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
