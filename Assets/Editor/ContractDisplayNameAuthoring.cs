#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit display-only contract title update. Save identities and asset names stay unchanged.</summary>
[InitializeOnLoad]
public static class ContractDisplayNameAuthoring
{
    private const string Request = "Temp/contract-display-names.request";
    private const string Report = "Logs/contract-display-names.txt";
    private const string Folder = "Assets/BridgeBuilder/Data/Contract/";
    private static double nextCheck;
    private static bool running;
    private sealed class Entry
    {
        internal string path, id, title;
        internal Entry(string path, string id, string title) { this.path = Folder + path + ".asset"; this.id = id; this.title = title; }
    }
    private static readonly Entry[] Entries = {
        new Entry("CanyonCrossing/Bhan/Bhan Tutorial 1", "TUT_CONTRACT1", "Professor Bhan Tutorial 1 - Beam Bridge"),
        new Entry("CanyonCrossing/Bhan/Bhan Tutorial 2", "TUT_CONTRACT2", "Professor Bhan Tutorial 2 - Truss Bridge"),
        new Entry("CanyonCrossing/Bhan/ShopKeeperContract", "ShopKeeper", "Shopkeeper - Shop Extension"),
        new Entry("CanyonCrossing/Reed/Reed's Contract", "ReedsContract", "Reed Main Contract 1 - The Supply Route"),
        new Entry("CanyonCrossing/Vance/Vance's Contract", "VancesContract", "Vance Main Contract 1 - Ridge Community"),
        // SilasStoryAuthoring.DistributionContract is the first project;
        // TransferContract is the subsequent route toward Town River.
        new Entry("CanyonCrossing/Silas/SilasContract", "SilasMainContract", "Silas Main Contract 1 - The Community Connection"),
        new Entry("CanyonCrossing/Silas/MainContractSilas", "MainContractSilas", "Silas Main Contract 2 - The Grand Gorge"),
        new Entry("TownRiver/Marsh Contract", "MarshID", "Marsh Main Contract 1 - Across Rivermere"),
        new Entry("TownRiver/Ford Contract", "FordMainContract", "Ford Main Contract 1 - The Construction Route"),
        new Entry("TownRiver/Brooks Contract", "BrooksMainContract", "Brooks Main Contract 1 - Market Connection"),
        new Entry("TownRiver/Ronan Contract", "RonanMainContract", "Ronan Main Contract 1 - The Southern Connection"),
        new Entry("IndustrialZone/Clark Contract", "ClarkMainContract", "Clark Main Contract 1 - Freight Channel"),
        new Entry("IndustrialZone/Clark Offset Road Contract", "ClarkOffsetRoad", "Clark Main Contract 2 - The Offset Road"),
        new Entry("IndustrialZone/Mills Heavy Equipment Contract", "MillsMainContract", "Mills Main Contract 1 - Heavy Equipment Route"),
        new Entry("IndustrialZone/Mills Workers Crossing Contract", "MillsWorkersCrossing", "Mills Main Contract 2 - Workers' Crossing"),
        new Entry("IndustrialZone/Mills Reconnecting Arcadia Contract", "MillsReconnectingArcadia", "Mills Main Contract 3 - Reconnecting Arcadia")
    };
    static ContractDisplayNameAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (running || !File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            AssetDatabase.IsAssetImportWorkerProcess() || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        if (command == "weather") ContractWeatherBriefings.Apply(); else Run(command);
    }
    [MenuItem("Tools/Civil Craft/Contracts/Display Names/Apply")]
    public static void Apply() => Run("apply");
    [MenuItem("Tools/Civil Craft/Contracts/Display Names/Validate")]
    public static void Validate() => Run("validate");

    private static void Run(string command)
    {
        running = true;
        Directory.CreateDirectory("Logs");
        var report = new StringBuilder();
        var originals = new Dictionary<ContractSO, string>();
        var protectedJson = new Dictionary<ContractSO, string>();
        var originalDirty = new Dictionary<ContractSO, bool>();
        var changed = new List<ContractSO>();
        bool beganSaving = false;
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        var dirty = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToDictionary(scene => scene, scene => scene.isDirty);
        try
        {
            Require(command == "apply" || command == "validate", "Use apply or validate.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating, "Run in idle Edit Mode after compilation.");
            Require(typeof(ContractSO).GetProperty("DisplayName") != null, "Refresh Unity to load ContractSO.DisplayName first.");
            var all = AssetDatabase.FindAssets("t:ContractSO", new[] { Folder.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ContractSO>).ToArray();
            Require(all.Length == 19, "Expected the 19 audited contracts; review new or missing assets first.");
            var assets = Entries.ToDictionary(entry => entry, entry => AssetDatabase.LoadAssetAtPath<ContractSO>(entry.path));
            Require(assets.All(pair => pair.Value != null), "A named contract asset is missing.");
            foreach (ContractSO asset in all)
            {
                originals.Add(asset, EditorJsonUtility.ToJson(asset)); protectedJson.Add(asset, WithoutDisplayName(asset));
                originalDirty.Add(asset, EditorUtility.IsDirty(asset));
            }
            foreach (var pair in assets)
            {
                Entry entry = pair.Key; ContractSO asset = pair.Value;
                Require(asset.ContractID == entry.id && asset.MatchesIdentifier(asset.name), "Permanent or legacy identity changed: " + entry.path);
                var data = new SerializedObject(asset); SerializedProperty display = data.FindProperty("displayName");
                Require(display != null, "Refresh Unity to load the displayName field.");
                if (command == "apply" && display.stringValue != entry.title)
                {
                    Undo.RecordObject(asset, "Update contract display name");
                    display.stringValue = entry.title; data.ApplyModifiedProperties(); EditorUtility.SetDirty(asset); changed.Add(asset);
                }
                Require(data.FindProperty("displayName").stringValue == entry.title && Display(asset) == entry.title,
                    "Contract display name differs: " + entry.path);
                Require(asset.ContractID == entry.id && asset.MatchesIdentifier(asset.name), "Changing the title affected save identity: " + entry.path);
                report.AppendLine("PASS: " + entry.title + "; permanent ID=" + asset.ContractID + "; legacy asset=" + asset.name);
            }
            foreach (ContractSO asset in all)
                Require(WithoutDisplayName(asset) == protectedJson[asset], "A non-title field changed: " + AssetDatabase.GetAssetPath(asset));
            foreach (ContractSO asset in all.Except(assets.Values))
                Require(EditorJsonUtility.ToJson(asset) == originals[asset], "An unrelated side/test contract changed: " + asset.name);
            ValidateFallback(report);
            if (command == "apply")
            {
                string backup = "LocalBackups/ContractDisplayNames-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                foreach (ContractSO asset in changed)
                {
                    string path = AssetDatabase.GetAssetPath(asset), destination = backup + "/" + path.Substring(Folder.Length);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination, false);
                    if (originalDirty[asset]) File.WriteAllText(destination + ".loaded-before.json", originals[asset]);
                }
                beganSaving = true;
                int savedCount = 0;
                foreach (ContractSO asset in changed)
                {
                    if (originalDirty[asset])
                    {
                        Require(EditorUtility.IsDirty(asset), "Existing unsaved asset edits lost their dirty state: " + asset.name);
                        report.AppendLine("UNSAVED: " + AssetDatabase.GetAssetPath(asset) + "; new display name is applied with the user's existing Inspector edits. No asset save was made.");
                    }
                    else
                    {
                        AssetDatabase.SaveAssetIfDirty(asset); Require(!EditorUtility.IsDirty(asset), "Unity did not save " + asset.name); savedCount++;
                    }
                }
                report.AppendLine("SAVED: " + savedCount + " originally clean contract assets; recoverable originals=" + backup);
            }
            foreach (ContractSO asset in all)
                Require(EditorUtility.IsDirty(asset) == originalDirty[asset], "An asset's original unsaved state changed: " + asset.name);
            Require(EditorSceneManager.GetSceneManagerSetup().Select(SceneKey).SequenceEqual(setup.Select(SceneKey)), "Scene setup changed.");
            foreach (var pair in dirty) Require(pair.Key.isDirty == pair.Value, "Scene dirty state changed: " + pair.Key.path);
            report.AppendLine("PASS: All 16 display names match; all 19 permanent IDs, legacy names, GUID references, materials, budgets, loads, dialogue and progression fields remain unchanged. The 3 side/test assets were untouched.");
            report.AppendLine("PASS: Existing unsaved Inspector edits remain unsaved. No scenes opened/saved or player-data/preferences, network, authentication or reward actions performed.");
            report.AppendLine("RESULT: PASS; command=" + command + "; UTC=" + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception error)
        {
            if (!beganSaving) foreach (ContractSO asset in changed)
            {
                EditorJsonUtility.FromJsonOverwrite(originals[asset], asset);
                if (originalDirty[asset]) EditorUtility.SetDirty(asset); else EditorUtility.ClearDirty(asset);
            }
            report.AppendLine("FAIL: " + error); Debug.LogException(error);
        }
        finally { File.WriteAllText(Report, report.ToString()); running = false; }
    }
    private static string WithoutDisplayName(ContractSO asset)
    {
        ContractSO copy = Object.Instantiate(asset);
        try
        {
            copy.name = asset.name;
            var data = new SerializedObject(copy); SerializedProperty display = data.FindProperty("displayName");
            Require(display != null, "ContractSO.displayName has not compiled."); display.stringValue = string.Empty; data.ApplyModifiedPropertiesWithoutUndo();
            return EditorJsonUtility.ToJson(copy);
        }
        finally { Object.DestroyImmediate(copy); }
    }
    private static string Display(ContractSO asset) => (string)typeof(ContractSO).GetProperty("DisplayName").GetValue(asset);
    private static void ValidateFallback(StringBuilder report)
    {
        ContractSO copy = ScriptableObject.CreateInstance<ContractSO>();
        try
        {
            copy.name = "Legacy Side Contract";
            Require(Display(copy) == copy.name, "An unnamed side contract lost its legacy display fallback.");
            var data = new SerializedObject(copy); data.FindProperty("displayName").stringValue = "  Edited Title  "; data.ApplyModifiedPropertiesWithoutUndo();
            Require(Display(copy) == "Edited Title" && copy.ContractID == copy.name && copy.MatchesIdentifier(copy.name), "Display title affected fallback identity or whitespace handling.");
            data.FindProperty("displayName").stringValue = "  "; data.ApplyModifiedPropertiesWithoutUndo();
            Require(Display(copy) == copy.name, "A blank display title must retain the asset name fallback.");
            report.AppendLine("PASS: Synthetic legacy assets retain display/identity fallback; edited titles trim whitespace without becoming save IDs.");
        }
        finally { Object.DestroyImmediate(copy); }
    }
    private static string SceneKey(SceneSetup value) => value.path + ":" + value.isLoaded + ":" + value.isActive;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

/// <summary>Authors hazard notices from each contract's actual solo simulation settings.</summary>
public static class ContractWeatherBriefings
{
    private const string Folder = "Assets/BridgeBuilder/Data/Contract";
    private const string Report = "Logs/contract-weather-briefings.txt";

    private static string Condition(int flags) => flags == 3 ? "wind gusts and earthquake shaking" :
        flags == 1 ? "wind gusts" : "earthquake shaking";
    private static string Brief(int flags) => "Solo test conditions: Expect " + Condition(flags) + ". Keep the bridge stable throughout the vehicle crossing.";
    private static string Spoken(int flags) => "During the solo vehicle test, expect <b><#E09500>" + Condition(flags) +
        "</color></b>. Check your supports, bracing, and connections.";
    private static string[] CleanLines(Dialogue dialogue) => (dialogue.sentences ?? Array.Empty<string>())
        .Where(line => !Enumerable.Range(1, 3).Any(flags => line == Spoken(flags))).ToArray();

    [MenuItem("Tools/Civil Craft/Contracts/Update Wind and Earthquake Briefings")]
    public static void Apply()
    {
        Directory.CreateDirectory("Logs");
        var report = new StringBuilder();
        var changed = new List<ContractSO>();
        var originals = new Dictionary<ContractSO, string>();
        var protectedState = new Dictionary<ContractSO, string>();
        var dirty = new Dictionary<ContractSO, bool>();
        bool saving = false;
        var sceneState = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .ToDictionary(scene => scene, scene => scene.isDirty);
        try
        {
            Require(!ParrelSync.ClonesManager.IsClone() && !EditorApplication.isPlayingOrWillChangePlaymode &&
                !EditorApplication.isCompiling && !EditorApplication.isUpdating, "Run in the original Editor's idle Edit Mode.");
            ContractSO[] all = AssetDatabase.FindAssets("t:ContractSO", new[] { Folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ContractSO>).ToArray();
            foreach (ContractSO contract in all)
            {
                originals[contract] = EditorJsonUtility.ToJson(contract);
                protectedState[contract] = WithoutBriefing(contract);
                dirty[contract] = EditorUtility.IsDirty(contract);
            }
            foreach (ContractSO contract in all)
            {
                BridgeEnvironmentalTest environment = contract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle ? contract.environmentalTest : null;
                int flags = environment == null ? 0 : (environment.HasWind ? 1 : 0) | (environment.HasEarthquake ? 2 : 0);
                if (flags == 0) continue;
                Require(contract.offerDialogue != null && contract.reminderDialogue != null, "Missing authored dialogue: " + contract.name);
                string brief = contract.jobDescription ?? string.Empty;
                for (int previous = 1; previous <= 3; previous++) brief = brief.Replace("\n\n" + Brief(previous), string.Empty);
                brief = brief.TrimEnd() + "\n\n" + Brief(flags);
                var offer = CleanLines(contract.offerDialogue).ToList();
                offer.Insert(Mathf.Max(0, offer.Count - 1), Spoken(flags));
                string[] reminder = CleanLines(contract.reminderDialogue).Concat(new[] { Spoken(flags) }).ToArray();
                if (contract.jobDescription != brief || !(contract.offerDialogue.sentences ?? Array.Empty<string>()).SequenceEqual(offer) ||
                    !(contract.reminderDialogue.sentences ?? Array.Empty<string>()).SequenceEqual(reminder))
                {
                    Undo.RecordObject(contract, "Mention contract wind and earthquakes");
                    contract.jobDescription = brief;
                    contract.offerDialogue.sentences = offer.ToArray();
                    contract.reminderDialogue.sentences = reminder;
                    EditorUtility.SetDirty(contract); changed.Add(contract);
                }
                Require(contract.jobDescription.Contains(Brief(flags)) && contract.offerDialogue.sentences.Contains(Spoken(flags)) &&
                    contract.reminderDialogue.sentences.Contains(Spoken(flags)), "Missing condition notice: " + contract.name);
                report.AppendLine("VERIFIED: " + contract.DisplayName + " — " + Condition(flags) + "; project brief, offer and reminder.");
            }
            foreach (ContractSO contract in all)
                Require(WithoutBriefing(contract) == protectedState[contract], "A non-briefing field changed: " + contract.name);
            string backup = "LocalBackups/ContractWeatherBriefings-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            foreach (ContractSO contract in changed)
            {
                string path = AssetDatabase.GetAssetPath(contract), destination = backup + "/" + path.Substring(Folder.Length + 1);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination);
                if (dirty[contract]) File.WriteAllText(destination + ".loaded-before.json", originals[contract]);
            }
            saving = true;
            foreach (ContractSO contract in changed)
            {
                if (!dirty[contract]) AssetDatabase.SaveAssetIfDirty(contract);
                report.AppendLine((dirty[contract] ? "UNSAVED: " : "SAVED: ") + AssetDatabase.GetAssetPath(contract));
            }
            foreach (ContractSO contract in all) Require(EditorUtility.IsDirty(contract) == dirty[contract], "Original asset dirty state changed: " + contract.name);
            foreach (var scene in sceneState) Require(scene.Key.isDirty == scene.Value, "A scene's unsaved state changed.");
            report.AppendLine("PASS: " + all.Length + " contracts audited. IDs, load rosters, budgets, materials, environmental settings, other dialogue and progression unchanged. Existing unsaved edits preserved; no scenes saved.");
        }
        catch (Exception error)
        {
            if (!saving) foreach (ContractSO contract in changed)
            {
                EditorJsonUtility.FromJsonOverwrite(originals[contract], contract);
                if (dirty[contract]) EditorUtility.SetDirty(contract); else EditorUtility.ClearDirty(contract);
            }
            report.AppendLine("FAILED: " + error); Debug.LogException(error);
        }
        File.WriteAllText(Report, report.ToString());
    }

    private static string WithoutBriefing(ContractSO source)
    {
        ContractSO copy = Object.Instantiate(source);
        try
        {
            copy.name = source.name; copy.jobDescription = string.Empty;
            if (copy.offerDialogue != null) copy.offerDialogue.sentences = Array.Empty<string>();
            if (copy.reminderDialogue != null) copy.reminderDialogue.sentences = Array.Empty<string>();
            return EditorJsonUtility.ToJson(copy);
        }
        finally { Object.DestroyImmediate(copy); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
