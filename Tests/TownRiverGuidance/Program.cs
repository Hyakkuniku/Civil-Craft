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
    private const string Author = "Assets/Editor/TownRiverGuidanceAuthoring.cs";
    private const string Helper = "Assets/Script/Client/NPCStoryObjectiveGuidance.cs";
    private const string Catalog = "Assets/Resources/TutorialObjectiveCatalog.asset";
    private static string root;
    private static Baseline baseline;
    private static int checks;
    private sealed class OwnerRecord { public string WithoutComponents { get; set; } public string[] Components { get; set; } }
    private sealed class Baseline
    {
        public Dictionary<string, string> ProtectedAssets { get; set; }
        public Dictionary<string, string> ProtectedSceneDocuments { get; set; }
        public Dictionary<string, string> TargetNPCsWithoutAttention { get; set; }
        public Dictionary<string, string> ExistingGuidesWithoutObjective { get; set; }
        public Dictionary<string, OwnerRecord> OwnerGameObjects { get; set; }
        public Dictionary<string, string> RonanOverrides { get; set; }
        public string[] RonanAddedComponents { get; set; }
        public string RonanNonOverrides { get; set; }
        public string CatalogBefore { get; set; }
    }
    private sealed record Definition(string Npc, string Phase, string Title, bool Route = true, string Site = "");
    private static readonly Definition[] Definitions = {
        new("MarshNPC", "MeetingMarsh", "Meet Marsh"),
        new("MarshNPC", "ExplainSituation", "Follow Marsh and inspect the crossing", Site:"1166066132"),
        new("MarshNPC", "Material Introduction", "Learn about concrete and Arch Bridges"),
        new("MarshNPC", "MarshReview", "Review Arch Bridges and complete the quiz", false),
        new("FordNPC", "MeetingFord", "Meet Ford"),
        new("FordNPC", "ExplainSituation", "Talk to Ford at the construction site", Site:"1929835384"),
        new("FordNPC", "LessonIntroduction", "Learn Material Stress"),
        new("FordNPC", "FordReview", "Review Material Stress and complete the quiz", false),
        new("BrooksNpc", "MeetingBrooks", "Meet Brooks"),
        new("BrooksNpc", "ExplainSituation", "Talk to Brooks at the market crossing", Site:"1499115992"),
        new("BrooksNpc", "LessonIntroduction", "Learn Moving Loads"),
        new("BrooksNpc", "BrooksReview", "Review Moving Loads and complete the quiz", false),
        new("RonanNpc", "MeetingRonan", "Meet Ronan"),
        new("RonanNpc", "ExplainSituation", "Talk to Ronan at the southern crossing", Site:"2130887295"),
        new("RonanNpc", "LessonIntroduction", "Learn Shear"),
        new("RonanNpc", "RonanReview", "Review Shear and complete the quiz", false)
    };
    private static readonly (string id, string lesson, string title)[] Existing = {
        ("1274516943", "Story/Ford/FollowToSite-v1", "Follow Ford to the construction crossing"),
        ("1922576282", "Story/Brooks/FollowToSite-v1", "Follow Brooks to the market crossing"),
        ("1042508027", "Story/Ronan/FollowToSite-v1", "Follow Ronan to the southern crossing")
    };
    private static readonly Dictionary<string, string> Owners = new() {
        ["1999832195"] = "MarshNPC", ["6542345702712754136"] = "FordNPC",
        ["8427106558270551892"] = "BrooksNpc", ["1671760476"] = "RonanNpc"
    };
    private static int Main(string[] args)
    {
        try
        {
            root = FindRoot(); Check(args.All(arg => arg == "--source-only"), "Supported option: --source-only.");
            baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline.json")));
            Assets(); Source(); if (!args.Contains("--source-only")) SavedScene();
            Console.WriteLine($"PASS: {checks} Town River guidance {(args.Contains("--source-only") ? "source/asset" : "source/asset/saved-scene")} assertions. No live scenes, player data, rewards or network touched.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }
    private static void Assets()
    {
        Check(baseline.ProtectedAssets.Count >= 31, "Frozen contract/lesson/quiz/simulation/prefab asset coverage is incomplete.");
        foreach (var pair in baseline.ProtectedAssets) Check(Hash(File.ReadAllBytes(Path.Combine(root, pair.Key))) == pair.Value, "Existing story/balance asset changed: " + pair.Key);
        foreach (string npc in new[] { "Marsh", "Ford", "Brooks", "Ronan" })
            Check(baseline.ProtectedAssets.ContainsKey("Assets/BridgeBuilder/Data/Contract/TownRiver/" + npc + " Contract.asset"), "Protect every existing Town River contract.");
    }
    private static void Source()
    {
        string source = Read(Author); var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR"));
        Check(!tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), "Authoring source must parse.");
        var syntax = tree.GetRoot();
        foreach (string guard in new[] { "EditorApplication.isPlayingOrWillChangePlaymode", "EditorApplication.isCompiling", "EditorApplication.isUpdating", "BuildPipeline.isBuildingPlayer",
            "PrefabStageUtility.GetCurrentPrefabStage", "Undo.RevertAllDownToGroup", "File.Copy", "EditorUtility.IsDirty(catalog)", "PrefabUtility.RecordPrefabInstancePropertyModifications",
            "WithoutAttention", "WithoutObjective", "playerBefore", "catalogState", "originalActive" }) Check(source.Contains(guard), "Native authoring guard missing: " + guard);
        var calls = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(node => node.Expression.ToString()).ToArray();
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:SaveScene|SaveScenes|SaveOpenScenes|OpenScene|CloseScene|SaveAssets|CompleteContract|CompleteLesson|StartTutorial|UnlockFeature|MoveToPhaseById)$")),
            "Authoring must not save/open scenes or execute gameplay operations.");
        Check(calls.Count(call => call == "AssetDatabase.SaveAssetIfDirty") == 1 && source.Contains("AssetDatabase.SaveAssetIfDirty(catalog)"), "Only the audited catalog may be saved.");
        foreach (AssignmentExpressionSyntax assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            Check(!Regex.IsMatch(assignment.Left.ToString(), @"\.(?:budget|liveLoadWeight|bridgeSpan|goldReward|expReward|phaseId|contract|targetLocation|phaseDialogue|unlockFeatureIdAfterDialogue)\s*$"),
                "Guidance cannot rewrite existing story/balance: " + assignment.Left);
        foreach (string id in Owners.Values) Check(source.Contains("\"" + id + "\""), "Use the exact authored save identity: " + id);
        Check(source.Contains("phase.showExclamationMark = phase.phaseId != \"Resolved\""), "Resolved phases remain idle while actionable phases opt into attention.");
        Check(source.Contains("sequence.showInObjectives = true") && source.Contains("objective.showInObjectives = true"), "Real guides and passive definitions use the existing checkbox.");
        Check(source.Contains("advancesTutorial = false") && source.Contains("playOnStart = false"), "Passive routes cannot advance or auto-play tutorials.");
        string helper = Read(Helper);
        foreach (string guard in new[] { "IsGuestInHostWorld", "IsGuestSaveProtected", "Application.isPlaying", "TryGetNPCProgression", "ReferenceEquals" }) Check(helper.Contains(guard), "Runtime profile guard is required: " + guard);
        Check(!Regex.IsMatch(helper, @"\b(?:StartTutorial|PlayTutorial|CompleteLesson|CompleteContract|UnlockMaterialForContract|MoveToPhaseById)\s*\("), "Observer may not play or advance the story.");
    }
    private static void SavedScene()
    {
        var docs = Documents(Read(Scene));
        foreach (var pair in baseline.ProtectedSceneDocuments) Check(docs.TryGetValue(pair.Key, out string doc) && HashText(doc) == pair.Value, "Unrelated component changed: " + pair.Key);
        foreach (var pair in baseline.TargetNPCsWithoutAttention) Check(HashText(RemoveAttention(docs[pair.Key])) == pair.Value, "NPC changed outside attention: " + pair.Key);
        foreach (var pair in baseline.ExistingGuidesWithoutObjective) Check(HashText(RemoveObjective(docs[pair.Key])) == pair.Value, "Existing follow steps/arrival/lesson/callbacks changed: " + pair.Key);
        string helperGuid = Guid(Helper), tutorialGuid = Guid("Assets/Script/Level/Tutorial/TutorialSequence.cs");
        var helpers = docs.Where(pair => pair.Value.Contains("m_Script: {fileID: 11500000, guid: " + helperGuid) && Owners.ContainsKey(ReferenceId(Field(pair.Value, "npc")))).ToArray();
        Check(helpers.Length == 4, "Exactly one observer belongs to each Town River NPC.");
        var definitionIds = new HashSet<string>(); var helperByGameObject = new Dictionary<string, string>();
        foreach (var helper in helpers)
        {
            string npcRef = ReferenceId(Field(helper.Value, "npc")), npc = Owners[npcRef];
            Check(helperByGameObject.TryAdd(ReferenceId(Field(helper.Value, "m_GameObject")), helper.Key), "Observer owners may not duplicate.");
            Match[] entries = Regex.Matches(helper.Value, @"(?ms)^  - phaseId: (?<phase>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - phaseId: |\z)").Cast<Match>().ToArray();
            Check(entries.Length == 4, "Each NPC has exactly four non-contract, non-Resolved objective phases.");
            foreach (var entry in entries)
            {
                string phase = Scalar(entry.Groups["phase"].Value); var expected = Definitions.Single(d => d.Npc == npc && d.Phase == phase);
                string objectiveId = ReferenceId(Field(entry.Groups["body"].Value, "objective", 4));
                Check(docs.TryGetValue(objectiveId, out string objective) && definitionIds.Add(objectiveId) && objective.Contains("m_Script: {fileID: 11500000, guid: " + tutorialGuid), "Every passive definition is a unique scene TutorialSequence.");
                Check(Field(objective, "showInObjectives") == "1" && Field(objective, "objectiveTitle") == expected.Title && Field(objective, "objectiveId") == Scene + "|story|" + npc + "|" + phase, "Checked objective stable identity/title differs: " + expected.Title);
                Check(Field(objective, "lessonName") == "Objective/" + npc + "/" + phase && Field(objective, "playOnStart") == "0" && Field(objective, "autoStartNextSequence") == "0" && Field(objective, "nextSequence") == "{fileID: 0}", "Passive definition must not play or chain.");
                Check(Field(entry.Groups["body"].Value, "completeAfterDialogue", 4) == "0" && Field(entry.Groups["body"].Value, "completionFeatureId", 4) == "", "Completion follows committed later story phase, never an invented terminal feature.");
                Check(Regex.Matches(objective, @"(?m)^  - message: ").Count == 1 && !string.IsNullOrWhiteSpace(Field(objective, "message", 4, true)), "One concise passive instruction is required.");
                foreach (string field in new[] { "showNextButton", "canSkip", "lockLook", "lockJump", "lockRun", "usePointer", "advanceOnClick", "requiredAction" }) Check(Field(objective, field, 4) == "0", "Passive objective cannot own controls or advance: " + field);
                Match[] points = Regex.Matches(objective, @"(?m)^    - target: \{fileID: (?<id>-?\d+)\}").Cast<Match>().ToArray();
                Check(points.Length == (expected.Route ? 1 : 0), "Quiz goals have no fake world route.");
                if (expected.Route)
                {
                    Check(points[0].Groups["id"].Value != "0" && Field(objective, "advancesTutorial", 6) == "0", "Tracking cannot advance a tutorial.");
                    if (expected.Site.Length > 0) Check(points[0].Groups["id"].Value == expected.Site, "Use the exact existing site target.");
                }
            }
        }
        Check(definitionIds.Count == 16, "Exactly sixteen passive definitions are saved.");
        foreach (var pair in baseline.OwnerGameObjects)
        {
            string doc = docs[pair.Key]; var ids = ComponentIds(doc);
            Check(HashText(RemoveComponents(doc)) == pair.Value.WithoutComponents && pair.Value.Components.All(ids.Contains), "Original NPC GameObject fields/components changed.");
            Check(ids.Length == pair.Value.Components.Length + 1 && ids.Except(pair.Value.Components).Single() == helperByGameObject[pair.Key], "Only the objective observer is appended to an NPC.");
        }
        foreach (string id in baseline.TargetNPCsWithoutAttention.Keys)
        {
            Check(Field(docs[id], "onlyShowExclamationWhenInteractionNeeded") == "1", "NPC opts into interaction-needed attention.");
            string[] marks = Regex.Matches(docs[id], @"(?m)^    showExclamationMark: (?<value>\d)").Cast<Match>().Select(match => match.Groups["value"].Value).ToArray();
            Check(marks.SequenceEqual(new[] { "1", "1", "1", "1", "1", "0" }), "Resolved attention must stay off; five actionable phases use policy gating.");
        }
        Ronan(docs, helperByGameObject["1671760475"]);
        var catalog = CatalogEntries(Read(Catalog)); var previous = CatalogEntries(baseline.CatalogBefore);
        foreach (var pair in previous) Check(catalog.TryGetValue(pair.Key, out string entry) && HashText(entry) == HashText(pair.Value), "Existing objective catalog definition changed.");
        Check(catalog.Count == previous.Count + 3, "Only the three real follow definitions are merged into the catalog.");
        foreach (var existing in Existing)
        {
            string doc = docs[existing.id], key = Scene + "|" + existing.id;
            Check(Field(doc, "lessonName") == existing.lesson && Field(doc, "showInObjectives") == "1" && Field(doc, "objectiveTitle") == existing.title && Field(doc, "objectiveId") == key, "Existing follow checkbox/title/stable ID differs.");
            Check(catalog.TryGetValue(key, out string entry) && Field(entry, "lessonId", 4) == existing.lesson && Field(entry, "title", 4) == existing.title && Field(entry, "allowLegacyLessonMatch", 4) == "1", "Only audited unique legacy follow completion may backfill history.");
        }
    }
    private static void Ronan(Dictionary<string, string> docs, string helperId)
    {
        string doc = docs["1186642534"]; var overrides = Overrides(doc);
        foreach (var pair in baseline.RonanOverrides) Check(overrides.TryGetValue(pair.Key, out string entry) && HashText(entry) == pair.Value, "Ronan prefab story override changed: " + pair.Key);
        Check(Field(overrides["onlyShowExclamationWhenInteractionNeeded"], "value", 6) == "1", "Ronan opts into attention policy.");
        for (int index = 0; index < 6; index++)
        {
            string key = $"phases.Array.data[{index}].showExclamationMark";
            // The template has no phases. Unity omits an override when a newly
            // expanded phase keeps the field's false C# default.
            bool implicitResolvedFalse = index == 5 && !overrides.ContainsKey(key) &&
                Regex.IsMatch(Read("Assets/Elements/Characters/NPCS/NPC_TEMPLATE.prefab"), @"(?m)^  phases: \[\]\r?$") &&
                Regex.IsMatch(Read("Assets/Script/Client/NPCProgressionManager.cs"), @"(?m)^    public bool showExclamationMark;\r?$");
            Check(implicitResolvedFalse || overrides.ContainsKey(key) && Field(overrides[key], "value", 6) == (index == 5 ? "0" : "1"), "Ronan Resolved attention stays off.");
        }
        string core = Regex.Replace(doc, @"(?ms)^    - target:.*?(?=^    - target:|^    m_RemovedComponents:)", "");
        core = Regex.Replace(core, @"(?ms)^    m_AddedComponents:.*?(?=^  m_SourcePrefab:)", "");
        Check(HashText(core) == baseline.RonanNonOverrides, "Ronan prefab transform, source, removed objects or child overrides changed.");
        string added = Regex.Match(doc, @"(?ms)^    m_AddedComponents:(?<body>.*?)(?=^  m_SourcePrefab:)").Groups["body"].Value;
        var blocks = Regex.Matches(added, @"(?ms)^    - targetCorrespondingSourceObject:.*?(?=^    - targetCorrespondingSourceObject:|\z)").Cast<Match>().Select(match => match.Value).ToArray();
        Check(blocks.Length == baseline.RonanAddedComponents.Length + 1 && baseline.RonanAddedComponents.All(hash => blocks.Any(block => HashText(block) == hash)), "Preserve existing Ronan added components and append only one observer.");
        Check(blocks.Count(block => block.Contains("addedObject: {fileID: " + helperId + "}")) == 1, "The one new Ronan prefab component is its objective observer.");
    }
    private static Dictionary<string, string> Documents(string text) => Regex.Matches(text, @"(?ms)^--- !u!\d+ &(?<id>-?\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)").Cast<Match>().ToDictionary(match => match.Groups["id"].Value, match => match.Value);
    private static Dictionary<string, string> Overrides(string text) => Regex.Matches(text, @"(?ms)^    - target:.*?(?=^    - target:|^    m_RemovedComponents:)").Cast<Match>().Where(match => Regex.IsMatch(match.Value, @"(?m)^      propertyPath: ")).ToDictionary(match => Regex.Match(match.Value, @"(?m)^      propertyPath: ([^\r\n]+)").Groups[1].Value, match => match.Value);
    private static Dictionary<string, string> CatalogEntries(string text) => Regex.Matches(text, @"(?ms)^  - objectiveId: (?<id>[^\r\n]+)\r?\n.*?(?=^  - objectiveId: |\z)").Cast<Match>().ToDictionary(match => Scalar(match.Groups["id"].Value), match => match.Value);
    private static string RemoveAttention(string text) => Regex.Replace(text, @"(?m)^\s*(?:showExclamationMark|onlyShowExclamationWhenInteractionNeeded):[^\r\n]*\r?\n", "");
    private static string RemoveObjective(string text) => Regex.Replace(text, @"(?m)^  (?:showInObjectives|objectiveTitle|objectiveId):[^\r\n]*\r?\n", "");
    private static string RemoveComponents(string text) => Regex.Replace(text, @"(?ms)^  m_Component:\r?\n(?:  - component: [^\r\n]*\r?\n)*", "");
    private static string[] ComponentIds(string text) => Regex.Matches(text, @"(?m)^  - component: \{fileID: (-?\d+)\}").Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
    private static string ReferenceId(string text) => Regex.Match(text, @"fileID: (-?\d+)").Groups[1].Value;
    private static string Field(string text, string key, int indent = 2, bool list = false)
    {
        Match match = Regex.Match(text, @"(?m)^ {" + (list ? indent - 2 : indent) + "}" + (list ? "- " : "") + Regex.Escape(key) + @":(?<value>[^\r\n]*(?:\r?\n {" + (indent + 2) + @"}[^\r\n]*)*)");
        Check(match.Success, "Missing field: " + key); return Scalar(Regex.Replace(match.Groups["value"].Value, @"\r?\n\s*", " "));
    }
    private static string Scalar(string value) { value = value.Trim(); if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1].Replace("''", "'"); if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return JsonSerializer.Deserialize<string>(value); return value; }
    private static string Guid(string path) { string value = Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([a-f0-9]{32})").Groups[1].Value; Check(value.Length == 32, "Missing metadata: " + path); return value; }
    private static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(Regex.Replace(text, @"\s+", " ").Trim()));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static string FindRoot() { for (DirectoryInfo directory = new(Environment.CurrentDirectory); directory != null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, Author))) return directory.FullName; throw new InvalidOperationException("Run from Unity project root."); }
}
