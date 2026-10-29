using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string First = "SilasMainContract", Second = "MainContractSilas";
    private static string root;
    private static int Main(string[] args)
    {
        try
        {
            root = FindRoot(); Check(args.All(arg => arg == "--source-only"), "Only --source-only is supported.");
            Preservation(); Runtime(); if (!args.Contains("--source-only")) SavedScene();
            Console.WriteLine($"PASS: {checks} actual spawn-condition/monitor, saved prerequisite and story-preservation checks. No Unity session, scenes, player saves or accounts modified.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }
    private static void Preservation()
    {
        using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline.json")));
        foreach (var pair in baseline.RootElement.GetProperty("Assets").EnumerateObject())
            Check(Hash(File.ReadAllBytes(Path.Combine(root, pair.Name))) == pair.Value.GetString(), "A protected contract changed: " + pair.Name);
        var docs = Documents(Read(ScenePath));
        foreach (var pair in baseline.RootElement.GetProperty("NPCs").EnumerateObject())
            Check(docs.TryGetValue(pair.Name, out string text) && Hash(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))) == pair.Value.GetString(),
                "Existing Marsh/Silas phase, dialogue, lesson, quiz, cinematic or contract wiring changed: " + pair.Name);
        foreach (var pair in baseline.RootElement.GetProperty("Sites").EnumerateObject())
            Check(docs.TryGetValue(pair.Name, out string text) && Hash(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))) == pair.Value.GetString(),
                "Existing site settings, anchors or simulation lesson changed: " + pair.Name);
        string monitor = Read("Assets/Script/Interaction System/Interactables/NPCSpawnConditionMonitor.cs");
        Check(monitor.Contains("root.GetComponentsInChildren<NPCSpawnerCondition>(true)"), "Scene loading must register initially inactive Marsh.");
        Check(monitor.Contains("condition.TickConditions()"), "The independent monitor must evaluate inactive NPCs.");
        foreach (var contract in new[] { ("SilasContract", First), ("MainContractSilas", Second) })
            Check(Field(Read("Assets/BridgeBuilder/Data/Contract/CanyonCrossing/Silas/" + contract.Item1 + ".asset"), "contractID") == contract.Item2,
                "Use exact stable contract completion identities.");
    }
    private static void Runtime()
    {
        foreach (bool first in new[] { false, true })
            foreach (bool second in new[] { false, true })
            {
                var data = NewProfile(first, second);
                NPCSpawnerCondition gate = Gate();
                Check(!gate.gameObject.activeSelf && !gate.CanShowAttentionMarkerFor(gate.gameObject.transform), "Marsh starts hidden with no attention marker.");
                gate.EvaluateNow();
                Check(gate.IsShown == (first && second), "Both main contracts are required; one alone must not unlock Marsh.");
                Check(gate.CanShowAttentionMarkerFor(gate.gameObject.transform) == (first && second), "Attention availability matches the actual spawn decision.");
                Check(data.SaveCalls == 0, "Evaluating completion prerequisites must not write player data.");
                var reloaded = Gate(); reloaded.EvaluateNow();
                Check(reloaded.IsShown == (first && second), "Reloaded gate reconstructs availability from durable completion records.");
            }

        NPCSpawnerCondition waiting = Gate(); PlayerDataManager.Instance = null; waiting.EvaluateNow();
        Check(!waiting.IsShown && !waiting.CanShowAttentionMarkerFor(waiting.gameObject.transform), "Missing player manager cannot reveal Marsh or a badge.");
        PlayerDataManager.Instance = new PlayerDataManager { CurrentData = null }; waiting.TickConditions();
        Check(!waiting.IsShown && !waiting.CanShowAttentionMarkerFor(waiting.gameObject.transform), "Loading player data cannot reveal Marsh or a badge.");

        var account = NewProfile(false, false); var monitored = Gate(); var monitor = new NPCSpawnConditionMonitor();
        typeof(NPCSpawnConditionMonitor).GetMethod("ResetState", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        NPCSpawnConditionMonitor.Register(monitored);
        Tick(monitor); Check(!monitored.IsShown, "Independent monitor keeps a fresh profile's Marsh hidden.");
        account.CurrentData.completedContracts.Add(First); Tick(monitor); Check(!monitored.IsShown, "First turn-in does not unlock Marsh.");
        account.CurrentData.completedContracts.Add(Second); Tick(monitor);
        Check(monitored.IsShown && NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(monitored.gameObject.transform), "Second turn-in makes inactive Marsh spawn and attention eligible without a scene reload.");
        Check(monitored.onShown.Calls == 1, "The gate shows Marsh exactly once when the requirement changes.");
        Tick(monitor); Check(monitored.onShown.Calls == 1, "Repeated ticks do not replay visibility callbacks.");
        account.CurrentData = new PlayerData(); Tick(monitor);
        Check(!monitored.IsShown && !NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(monitored.gameObject.transform), "Switching to an unfinished profile hides Marsh again.");
        Check(account.SaveCalls == 0, "Spawn monitor neither completes contracts nor changes saves.");
        NPCSpawnConditionMonitor.Unregister(monitored);

        NewProfile(true, true); NPCSpawnerCondition beforeEvaluation = Gate(); beforeEvaluation.gameObject.SetActive(true);
        Check(!beforeEvaluation.CanShowAttentionMarkerFor(beforeEvaluation.gameObject.transform), "Even an accidentally active authored root has no badge before its prerequisite check.");
        var unrelated = new GameObject(); Check(beforeEvaluation.CanShowAttentionMarkerFor(unrelated.transform), "Marsh's gate must not suppress another NPC's attention.");
    }
    private static PlayerDataManager NewProfile(bool first, bool second)
    {
        var manager = new PlayerDataManager(); PlayerDataManager.Instance = manager;
        if (first) manager.CurrentData.completedContracts.Add(First);
        if (second) manager.CurrentData.completedContracts.Add(Second);
        return manager;
    }
    private static NPCSpawnerCondition Gate()
    {
        var gate = new NPCSpawnerCondition { useMultipleConditions = true, matchMode = NPCSpawnerCondition.MatchMode.All,
            updateAutomatically = true, hideWhenMatched = false, waitForManualSpawn = false, persistManualOverride = false };
        gate.npcRoot = gate.gameObject; gate.npcRoot.SetActive(false);
        gate.conditions.Add(new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.ContractCompleted, contract = new ContractSO { ContractID = First } });
        gate.conditions.Add(new NPCSpawnerCondition.Condition { kind = NPCSpawnerCondition.ConditionKind.ContractCompleted, contract = new ContractSO { ContractID = Second } });
        return gate;
    }
    private static void Tick(NPCSpawnConditionMonitor monitor)
    {
        Time.unscaledTime += 1f;
        typeof(NPCSpawnConditionMonitor).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(monitor, null);
    }
    private static void SavedScene()
    {
        var docs = Documents(Read(ScenePath)); string owner = docs["1999832193"];
        Check(Field(owner, "m_IsActive") == "0", "Saved Marsh root must start hidden until its rule is evaluated.");
        string gateGuid = Guid("Assets/Script/Interaction System/Interactables/NPCSpawnerCondition.cs");
        var gates = docs.Where(pair => pair.Value.Contains("guid: " + gateGuid) && Field(pair.Value, "m_GameObject") == "{fileID: 1999832193}").ToArray();
        Check(gates.Length == 1, "Saved Marsh needs exactly one spawn gate on his existing root.");
        string gate = gates[0].Value;
        Check(owner.Contains("component: {fileID: " + gates[0].Key + "}"), "The saved root must reference its gate component.");
        foreach (var expected in new Dictionary<string, string> { ["m_Enabled"] = "1", ["useMultipleConditions"] = "1", ["matchMode"] = "0", ["hideWhenMatched"] = "0",
            ["waitForManualSpawn"] = "0", ["persistManualOverride"] = "0", ["updateAutomatically"] = "1", ["useDespawnConditions"] = "0", ["npcRoot"] = "{fileID: 1999832193}" })
            Check(Field(gate, expected.Key) == expected.Value, "Wrong Marsh gate setting: " + expected.Key);
        string conditions = Regex.Match(gate, @"(?ms)^  conditions:\r?\n(?<body>.*?)(?=^  [A-Za-z])").Groups["body"].Value;
        var rules = Regex.Matches(conditions, @"(?ms)^  - kind: (?<kind>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - kind:|\z)").Cast<Match>().ToArray();
        Check(rules.Length == 2, "Use exactly the two main contract prerequisites.");
        foreach (var path in new[] { "SilasContract", "MainContractSilas" })
            Check(rules.Any(rule => rule.Groups["kind"].Value == "1" && rule.Groups["body"].Value.Contains("guid: " + Guid("Assets/BridgeBuilder/Data/Contract/CanyonCrossing/Silas/" + path + ".asset")) && Field(rule.Groups["body"].Value, "mustBeTrue", 4) == "1"), "A required Silas main contract is absent or inverted: " + path);
    }
    private static Dictionary<string, string> Documents(string value) => Regex.Matches(value, @"(?ms)^--- !u!\d+ &(\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Value);
    private static string Field(string text, string name, int spaces = 2) => Regex.Match(text, @"(?m)^" + new string(' ', spaces) + Regex.Escape(name) + @": ?([^\r\n]*)").Groups[1].Value.Trim();
    private static string Guid(string path) => Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([0-9a-f]+)").Groups[1].Value;
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Check(bool value, string reason) { checks++; if (!value) throw new InvalidOperationException(reason); }
    private static string FindRoot() { for (DirectoryInfo item = new DirectoryInfo(Environment.CurrentDirectory); item != null; item = item.Parent) if (File.Exists(Path.Combine(item.FullName, "ProjectSettings/ProjectVersion.txt"))) return item.FullName; throw new DirectoryNotFoundException("Run from the Unity project root."); }
}
