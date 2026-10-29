using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private const string Scene = "Assets/Scenes/CanyonCrossing.unity";
    private const string Author = "Assets/Editor/ReedToSilasGuidanceAuthoring.cs";
    private const string Helper = "Assets/Script/Client/NPCStoryObjectiveGuidance.cs";
    private const string Catalog = "Assets/Resources/TutorialObjectiveCatalog.asset";
    private static string root;
    private static Baseline baseline;
    private static int checks;
    private sealed class Baseline
    {
        public Dictionary<string, string> ProtectedAssets { get; set; }
        public Dictionary<string, string> ProtectedSceneDocuments { get; set; }
        public Dictionary<string, string> TargetNPCsWithoutAttention { get; set; }
        public Dictionary<string, string> ExistingGuidesWithoutObjective { get; set; }
        public Dictionary<string, string> ReedOverrides { get; set; }
        public string CatalogBefore { get; set; }
    }
    private sealed record Definition(string Npc, string Phase, string Title, bool Route = true, bool Terminal = false, string FollowTarget = "");
    private static readonly Definition[] Definitions = {
        new("ReedNPC", "MeetingReed", "Meet Reed"),
        new("ReedNPC", "ExplainSituation", "Follow Reed and learn about Bending", FollowTarget:"1063296217"),
        new("ReedNPC", "ReedFareWell", "Review Bending and complete the quiz", false),
        new("VanceNPC", "MeetingVance", "Meet Vance"),
        new("VanceNPC", "The Supply Route", "Follow Vance", FollowTarget:"2978106"),
        new("VanceNPC", "The Supply Route Part Two", "Learn Tension & Compression"),
        new("VanceNPC", "GoodByeForNow", "Review Tension & Compression and complete the quiz", false),
        new("SilasNPC", "Meeting Silas", "Meet Silas"),
        new("SilasNPC", "The Final Supply Gap ", "Follow Silas", FollowTarget:"1654018038"),
        new("SilasNPC", "AfterCinematic", "Learn Load Distribution"),
        new("SilasNPC", "SilasLoadTransfer", "Review Load Distribution and complete the quiz", false),
        new("SilasNPC", "ExplainSituation2", "Follow Silas to the eastern passage", FollowTarget:"323707688"),
        new("SilasNPC", "ExplainSituationContinuation", "Inspect the eastern passage"),
        new("SilasNPC", "MATERIAL INTRODUCTION", "Learn Load Transfer"),
        new("SilasNPC", "ProjectBriefing3", "Review Load Transfer and complete the quiz", false),
        new("SilasNPC", "Multiplayer", "Unlock Multiplayer", true, true)
    };
    private static readonly (string id, string lesson, string title)[] Existing = {
        ("1143740769", "LoadCargoVance", "Load all four cargo items"),
        ("1487862495", "GobacktoVance ", "Return to Vance"),
        ("22004694", "EraseReedSideQuest", "Inspect Reed's bridge"),
        ("669876432", "MoveVanceSideQuest", "Realign Vance's bridge")
    };
    private static int Main(string[] args)
    {
        try
        {
            root = FindRoot(); Check(args.All(arg => arg == "--source-only"), "Supported option: --source-only.");
            baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline.json")));
            Assets(); Source();
            if (!args.Contains("--source-only")) SavedScene();
            Console.WriteLine($"PASS: {checks} Reed-to-Silas guidance {(args.Contains("--source-only") ? "source/asset" : "source/asset/saved-scene")} assertions. No live scenes, player data, rewards or network touched.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }
    private static void Assets()
    {
        foreach (var pair in baseline.ProtectedAssets) Check(Hash(File.ReadAllBytes(Path.Combine(root, pair.Key))) == pair.Value, "Existing main/side contract changed: " + pair.Key);
        Check(baseline.ProtectedAssets.Count == 6, "The baseline must protect all six Reed/Vance/Silas contracts.");
        Check(Definitions.Length == 16 && Definitions.Select(d => d.Npc + "|" + d.Phase).Distinct().Count() == 16, "All new phase objectives have unique source identities.");
    }
    private static void Source()
    {
        string source = Read(Author); var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR"));
        Check(!tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), "Authoring source must parse.");
        var syntax = tree.GetRoot();
        foreach (string guard in new[] { "EditorApplication.isPlayingOrWillChangePlaymode", "EditorApplication.isCompiling", "EditorApplication.isUpdating", "BuildPipeline.isBuildingPlayer",
            "PrefabStageUtility.GetCurrentPrefabStage", "Undo.RevertAllDownToGroup", "File.Copy", "EditorUtility.IsDirty(catalog)", "PrefabUtility.RecordPrefabInstancePropertyModifications",
            "WithoutAttention", "WithoutObjective", "playerBefore", "catalogState" }) Check(source.Contains(guard), "Native authoring guard missing: " + guard);
        var calls = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(node => node.Expression.ToString()).ToArray();
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:SaveScene|SaveScenes|SaveOpenScenes|OpenScene|CloseScene|SaveAssets|CompleteContract|CompleteLesson|StartTutorial|UnlockFeature|MoveToPhaseById)$")),
            "Authoring must not auto-save/open scenes or execute story/tutorial/progression operations.");
        Check(calls.Count(call => call == "AssetDatabase.SaveAssetIfDirty") == 1 && source.Contains("AssetDatabase.SaveAssetIfDirty(catalog)"), "Only the audited catalog asset may be saved by authoring.");
        foreach (AssignmentExpressionSyntax assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            string left = assignment.Left.ToString();
            Check(!Regex.IsMatch(left, @"\.(?:budget|liveLoadWeight|bridgeSpan|goldReward|expReward|phaseId|contract|targetLocation|phaseDialogue|unlockFeatureIdAfterDialogue)\s*$"),
                "Guidance authoring cannot rewrite existing story/balance: " + left);
        }
        foreach (string value in new[] { "ReedNPC", "VanceNPC", "SilasNPC", "The Final Supply Gap ", "GobacktoVance " }) Check(source.Contains("\"" + value + "\""), "Preserve exact authored identity: " + value);
        Check(source.Contains("sequence.showInObjectives = true") && source.Contains("objective.showInObjectives = true") && source.Contains("phase.showExclamationMark = true"),
            "Existing guides, passive definitions and authored attention must be explicitly enabled.");
        Check(source.Contains("completionFeatureId = definition.terminal ? \"multiplayer\" : string.Empty"), "Multiplayer objective requires the actual feature unlock as well as the final dialogue.");
        Check(source.Contains("advancesTutorial = false") && source.Contains("playOnStart = false"), "Objective route templates must not advance story or play tutorials on load.");
        string helper = Read(Helper);
        foreach (string guard in new[] { "IsGuestInHostWorld", "IsGuestSaveProtected", "Application.isPlaying", "TryGetNPCProgression", "ReferenceEquals" })
            Check(helper.Contains(guard), "Runtime owner/profile guard is required: " + guard);
        Check(!Regex.IsMatch(helper, @"\b(?:StartTutorial|PlayTutorial|CompleteLesson|CompleteContract|UnlockMaterialForContract|MoveToPhaseById)\s*\("), "Guidance observer may not play or advance the story.");
    }
    private static void SavedScene()
    {
        var docs = Documents(Read(Scene));
        foreach (var pair in baseline.ProtectedSceneDocuments) Check(docs.TryGetValue(pair.Key, out string doc) && HashText(doc) == pair.Value,
            "An existing unrelated component changed: " + pair.Key);
        foreach (var pair in baseline.TargetNPCsWithoutAttention) Check(HashText(RemoveAttention(docs[pair.Key])) == pair.Value, "NPC changed outside attention: " + pair.Key);
        foreach (var pair in baseline.ExistingGuidesWithoutObjective) Check(HashText(RemoveObjective(docs[pair.Key])) == pair.Value, "Existing tutorial steps/lesson ID/callbacks changed: " + pair.Key);
        var reedOverrides = Overrides(docs["1110249782773445749"]);
        foreach (var pair in baseline.ReedOverrides) Check(reedOverrides.TryGetValue(pair.Key, out string value) && HashText(value) == pair.Value, "Reed changed outside attention: " + pair.Key);
        string helperGuid = Guid(Helper), tutorialGuid = Guid("Assets/Script/Level/Tutorial/TutorialSequence.cs");
        var helpers = docs.Where(pair => pair.Value.Contains("m_Script: {fileID: 11500000, guid: " + helperGuid)).ToArray();
        Check(helpers.Length == 3, "Save exactly one objective observer on each of Reed, Vance and Silas.");
        var owners = new Dictionary<string, string> { ["1070910668"] = "ReedNPC", ["1601157397"] = "VanceNPC", ["318150247"] = "SilasNPC" };
        var sourceIds = new HashSet<string>();
        foreach (var helper in helpers)
        {
            string npcReference = ReferenceId(Field(helper.Value, "npc"));
            Check(owners.TryGetValue(npcReference, out string npc), "Guidance must bind an exact existing NPC component.");
            Match[] entries = Regex.Matches(helper.Value, @"(?ms)^  - phaseId: (?<phase>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - phaseId: |\z)").Cast<Match>().ToArray();
            Check(entries.Length == Definitions.Count(definition => definition.Npc == npc), "Each NPC has only its audited non-contract objectives.");
            foreach (var entry in entries)
            {
                string phase = Scalar(entry.Groups["phase"].Value); var expected = Definitions.Single(definition => definition.Npc == npc && definition.Phase == phase);
                string objectiveId = ReferenceId(Field(entry.Groups["body"].Value, "objective", 4));
                Check(docs.TryGetValue(objectiveId, out string objective), "Objective definition must be serialized in this scene.");
                Check(sourceIds.Add(objectiveId) && objective.Contains("m_Script: {fileID: 11500000, guid: " + tutorialGuid), "Every checked definition is a unique TutorialSequence.");
                Check(Field(objective, "showInObjectives") == "1" && Field(objective, "objectiveTitle") == expected.Title &&
                    Field(objective, "objectiveId") == Scene + "|story|" + npc + "|" + phase, "Use the checkbox and stable per-phase identity: " + expected.Title);
                Check(Field(objective, "lessonName") == "Objective/" + npc + "/" + phase && Field(objective, "playOnStart") == "0" && Field(objective, "autoStartNextSequence") == "0" && Field(objective, "nextSequence") == "{fileID: 0}",
                    "Passive objective cannot silently play or chain tutorials: " + expected.Title);
                Check(Field(entry.Groups["body"].Value, "completeAfterDialogue", 4) == (expected.Terminal ? "1" : "0") &&
                    Field(entry.Groups["body"].Value, "completionFeatureId", 4) == (expected.Terminal ? "multiplayer" : ""), "Terminal objective completion evidence must match its phase.");
                Check(Regex.Matches(objective, @"(?m)^  - message: ").Count == 1 && !string.IsNullOrWhiteSpace(Field(objective, "message", 4, true)), "One concise instruction per passive objective.");
                foreach (string field in new[] { "showNextButton", "canSkip", "lockLook", "lockJump", "lockRun", "usePointer", "advanceOnClick", "requiredAction" }) Check(Field(objective, field, 4) == "0", "Passive objective cannot lock controls, own the pointer or require a tutorial action: " + field);
                Match[] waypoints = Regex.Matches(objective, @"(?m)^    - target: \{fileID: (?<id>-?\d+)\}").Cast<Match>().ToArray();
                Check(waypoints.Length == (expected.Route ? 1 : 0), "Quiz objectives have no fake route; other guides use one authored route.");
                if (expected.Route)
                {
                    Check(waypoints[0].Groups["id"].Value != "0" && Field(objective, "advancesTutorial", 6) == "0", "TRACK may route but cannot complete/advance a tutorial.");
                    if (expected.FollowTarget.Length > 0) Check(waypoints[0].Groups["id"].Value == expected.FollowTarget, "Follow guide uses its existing static site marker.");
                }
            }
        }
        Check(sourceIds.Count == 16, "Exactly sixteen passive objective definitions are authored.");
        foreach (string npcId in new[] { "1601157397", "318150247" })
        {
            Check(Field(docs[npcId], "onlyShowExclamationWhenInteractionNeeded") == "1", "Only show attention when interaction is needed.");
            Match[] marks = Regex.Matches(docs[npcId], @"(?m)^    showExclamationMark: (?<value>\d)").Cast<Match>().ToArray();
            Check(marks.Length == (npcId == "1601157397" ? 7 : 11) && marks.All(mark => mark.Groups["value"].Value == "1"), "Every authored conversation/contract/quiz phase opts into attention.");
        }
        Check(Scalar(OverrideValue(reedOverrides, "onlyShowExclamationWhenInteractionNeeded")) == "1", "Reed opts into the interaction-needed attention policy.");
        for (int i = 0; i < 5; i++) Check(Scalar(OverrideValue(reedOverrides, $"phases.Array.data[{i}].showExclamationMark")) == "1", "Reed attention must cover every existing phase.");
        string catalog = Read(Catalog); var catalogEntries = CatalogEntries(catalog); var beforeEntries = CatalogEntries(baseline.CatalogBefore);
        foreach (var pair in beforeEntries) Check(catalogEntries.TryGetValue(pair.Key, out string entry) && HashText(entry) == HashText(pair.Value), "Preserve every pre-existing Bhan/catalog entry.");
        Check(catalogEntries.Count == beforeEntries.Count + 4, "Only the four existing guide definitions are merged into the recovery catalog.");
        foreach (var existing in Existing)
        {
            string doc = docs[existing.id]; Check(Field(doc, "lessonName") == existing.lesson && Field(doc, "showInObjectives") == "1" && Field(doc, "objectiveTitle") == existing.title && Field(doc, "objectiveId") == Scene + "|" + existing.id,
                "The existing cargo/return/side tutorial retains its lesson ID and gets checked objective settings.");
            Check(catalogEntries.TryGetValue(Scene + "|" + existing.id, out string entry) && Field(entry, "lessonId", 4) == existing.lesson && Field(entry, "title", 4) == existing.title && Field(entry, "allowLegacyLessonMatch", 4) == "1", "Recover only the four audited unique legacy tutorials.");
        }
    }
    private static Dictionary<string, string> Documents(string text) => Regex.Matches(text, @"(?ms)^--- !u!\d+ &(?<id>-?\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)").Cast<Match>().ToDictionary(match => match.Groups["id"].Value, match => match.Value);
    private static Dictionary<string, string> Overrides(string text) => Regex.Matches(text, @"(?ms)^    - target:.*?(?=^    - target:|^    m_RemovedComponents:)").Cast<Match>()
        .Where(match => Regex.IsMatch(match.Value, @"(?m)^      propertyPath: ")).ToDictionary(match => Regex.Match(match.Value, @"(?m)^      propertyPath: ([^\r\n]+)").Groups[1].Value, match => match.Value);
    private static string OverrideValue(Dictionary<string, string> overrides, string key) => Field(overrides[key], "value", 6);
    private static Dictionary<string, string> CatalogEntries(string text) => Regex.Matches(text, @"(?ms)^  - objectiveId: (?<id>[^\r\n]+)\r?\n.*?(?=^  - objectiveId: |\z)").Cast<Match>().ToDictionary(match => Scalar(match.Groups["id"].Value), match => match.Value);
    private static string RemoveAttention(string text) => Regex.Replace(text, @"(?m)^\s*(?:showExclamationMark|onlyShowExclamationWhenInteractionNeeded):[^\r\n]*\r?\n", "");
    private static string RemoveObjective(string text) => Regex.Replace(text, @"(?m)^  (?:showInObjectives|objectiveTitle|objectiveId):[^\r\n]*\r?\n", "");
    private static string ReferenceId(string text) => Regex.Match(text, @"fileID: (-?\d+)").Groups[1].Value;
    private static string Field(string text, string key, int indent = 2, bool list = false)
    {
        Match match = Regex.Match(text, @"(?m)^ {" + (list ? indent - 2 : indent) + "}" + (list ? "- " : "") + Regex.Escape(key) +
            @":(?<value>[^\r\n]*(?:\r?\n {" + (indent + 2) + @"}[^\r\n]*)*)");
        Check(match.Success, "Missing field: " + key); return Scalar(Regex.Replace(match.Groups["value"].Value, @"\r?\n\s*", " "));
    }
    private static string Scalar(string value) { value = value.Trim(); if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1].Replace("''", "'"); if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return JsonSerializer.Deserialize<string>(value); return value; }
    private static string Guid(string path) { string value = Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([a-f0-9]{32})").Groups[1].Value; Check(value.Length == 32, "Missing metadata identity: " + path); return value; }
    private static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(Regex.Replace(text, @"\s+", " ").Trim()));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static string FindRoot() { for (DirectoryInfo directory = new(Environment.CurrentDirectory); directory != null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, Author))) return directory.FullName; throw new InvalidOperationException("Run from Unity project root."); }
}
