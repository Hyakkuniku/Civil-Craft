using System.Reflection;
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
    private const string Author = "Assets/Editor/VanceStoryAuthoring.cs";
    private const string MainContract = "Assets/BridgeBuilder/Data/Contract/CanyonCrossing/Vance/Vance's Contract.asset";
    private const string SideContract = "Assets/BridgeBuilder/Data/Contract/CanyonCrossing/Vance/Vance Side Realignment.asset";
    private const string Lesson = "Assets/BridgeBuilder/Data/Lessons/TensionCompression.asset";
    private const string Quiz = "Assets/BridgeBuilder/Data/Quized/TensionCompressionQuiz.asset";
    private static readonly string[] PhaseIds = { "MeetingVance", "The Supply Route", "The Supply Route Part Two",
        "Cargo and Live-Load Introduction", "Project Briefing", "GoodByeForNow", "SideQuestStart" };
    private static string root;
    private static int checks;
    private static Baseline baseline;
    private sealed class Baseline
    {
        public string SideContractSHA256 { get; set; }
        public string ProtectedContractHash { get; set; }
        public string SidePhaseHash { get; set; }
        public string CargoPhaseProtectedHash { get; set; }
        public string Phase4Hash { get; set; }
        public string[] OriginalPhaseIds { get; set; }
        public Dictionary<string, string> ProtectedSceneDocuments { get; set; }
        public Dictionary<string, string> SharedRuntimeSources { get; set; }
    }

    private static int Main(string[] args)
    {
        try
        {
            root = FindRoot();
            Check(args.All(value => value == "--source-only"), "Only --source-only is supported.");
            bool sourceOnly = args.Contains("--source-only");
            baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline.json")));
            Check(Steps("    optionalDialogueSequence:\n    - stepType: 0\n      dialogue:\n        sentences: []\n    - stepType: 3\n      lesson: {fileID: 0}\n    dialoguePrompt: Talk\n").Length == 2,
                "The saved-scene reader must retain Unity list entries at the same indentation as their parent field.");
            Assets(sourceOnly); Authoring();
            if (!sourceOnly) SavedScene();
            Console.WriteLine($"PASS: {checks} Vance {(sourceOnly ? "asset/source" : "asset/source/saved-scene")} assertions. " +
                (sourceOnly ? "Scene apply/save is deliberately not claimed. " : "") +
                "Read-only checks; player saves, cargo, rewards and network untouched.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }

    private static void Assets(bool sourceOnly)
    {
        Check(baseline.OriginalPhaseIds.SequenceEqual(PhaseIds), "The baseline must capture the original seven saved phase IDs.");
        Check(Hash(ReadBytes(SideContract)) == baseline.SideContractSHA256, "Existing realignment side contract must remain byte-for-byte unchanged.");
        foreach (var entry in baseline.SharedRuntimeSources)
            Check(Hash(ReadBytes(entry.Key)) == entry.Value, "Existing progression/cargo/simulation implementation changed: " + entry.Key);
        string contract = Read(MainContract);
        Check(HashText(RemoveFields(contract, 2, "jobDescription", "offerDialogue", "continueOfferDialogue", "reminderDialogue", "finishedContractDialogue", "bridgeSpan")) == baseline.ProtectedContractHash,
            "Every main-contract setting outside narrative and the intentional span correction must remain unchanged.");
        foreach (var entry in new Dictionary<string, string> { ["contractID"] = "VancesContract", ["clientName"] = "Vance", ["budget"] = "150000",
            ["liveLoadWeight"] = "5000", ["goldReward"] = "500", ["expReward"] = "100", ["liveLoadMode"] = "0", ["allowVehicleCargo"] = "1",
            ["minimumLoadedCargo"] = "4", ["cargoUnlockProgressionId"] = "VanceNPC", ["cargoUnlockAfterDialoguePhaseIndex"] = "3",
            ["cargoLoadedNPCPhaseId"] = "Project Briefing", ["autoCollectReward"] = "0", ["allowedMaterials"] = "[]", ["featureUnlockRewards"] = "[]" })
            Check(Field(contract, entry.Key) == entry.Value, "Preserve Vance's existing contract field: " + entry.Key);
        Check(sourceOnly ? new[] { "3", "80" }.Contains(Field(contract, "bridgeSpan")) : Field(contract, "bridgeSpan") == "80",
            "Only the audited placeholder3m to measured80m correction is allowed.");

        string lesson = Read(Lesson), quiz = Read(Quiz);
        Check(ReferenceGuid(Field(lesson, "m_Script")) == Guid("Assets/Script/UI/Lessons/LessonData.cs"), "Use the existing lesson reader and archive.");
        Check(Field(lesson, "lessonId") == "civil-craft-tension-compression-lesson-v1" && Field(lesson, "lessonTitle") == "Tension & Compression",
            "Combined lesson has independent permanent save identity and title.");
        foreach (string idea in new[] { "Tension", "Compression", "stretch", "shorten", "supports" })
            Check(Field(lesson, "popupDescription").Contains(idea, StringComparison.OrdinalIgnoreCase) ||
                Field(lesson, "lessonDescription").Contains(idea, StringComparison.OrdinalIgnoreCase), "Retain the lesson concept: " + idea);
        Check(Field(lesson, "lessonDescription").Contains("load the cargo onto the vehicle before testing", StringComparison.OrdinalIgnoreCase) &&
            Field(lesson, "lessonDescription").Contains("cargo adds to the vehicle's load", StringComparison.OrdinalIgnoreCase),
            "The lesson must reflect Vance's actual cargo-loading requirement and combined load.");
        Check(Field(lesson, "lessonImage") == "{fileID: 0}" && Field(lesson, "almanacImage") == "{fileID: 0}", "Do not invent a lesson image.");
        Check(ReferenceGuid(Field(quiz, "m_Script")) == Guid("Assets/Script/UI/Quizzes/QuizData.cs"), "Use the existing quiz system.");
        Check(Field(quiz, "quizId") == "civil-craft-tension-compression-quiz-v1" && Field(quiz, "assessmentMode") == "1",
            "Stable assessment identity; any submitted score advances without quiz rewards.");
        Match[] questions = Questions(quiz);
        Check(questions.Length == 5 && questions.Select(q => q.Groups["id"].Value).Distinct().Count() == 5, "Five unique reference questions.");
        Check(questions.Select(q => int.Parse(Field(q.Groups["body"].Value, "correctChoiceIndex", 4))).SequenceEqual(new[] { 1, 3, 0, 2, 1 }),
            "The reference answer key must remain B, D, A, C, B.");
        foreach (Match question in questions)
        {
            string body = question.Groups["body"].Value;
            Check(Choices(body).Length == 4, "Each question must have four choices.");
            Check(Field(body, "prompt", 4).Length > 10 && Field(body, "feedback", 4).Length > 10, "Complete question and result-card explanation.");
        }
        Check(!Regex.IsMatch(lesson + quiz, @"\b\d+\s*(?:kg|kilograms)\b", RegexOptions.IgnoreCase), "No fabricated numerical cargo or vehicle weight in learning content.");
        ValidateProductionQuiz(quiz);
        foreach (string path in Directory.GetFiles(Path.Combine(root, "Assets/BridgeBuilder/Data/Lessons"), "*.asset"))
            if (!Path.GetFullPath(path).Equals(Path.GetFullPath(Path.Combine(root, Lesson)), StringComparison.OrdinalIgnoreCase))
                Check(OptionalField(File.ReadAllText(path), "lessonId") != Field(lesson, "lessonId"), "Combined lesson must not reuse an existing lesson save ID.");
    }

    private static void ValidateProductionQuiz(string asset)
    {
        const string boundary = "namespace UnityEngine { public class ScriptableObject {} public class Sprite {} public sealed class TextAreaAttribute : System.Attribute { public TextAreaAttribute(int a,int b){} } public sealed class TooltipAttribute : System.Attribute { public TooltipAttribute(string s){} } public sealed class HideInInspector : System.Attribute {} public sealed class CreateAssetMenuAttribute : System.Attribute { public string fileName; public string menuName; } }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("VanceQuizChecks_" + System.Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(Read("Assets/Script/UI/Quizzes/QuizData.cs")), CSharpSyntaxTree.ParseText(boundary) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new(); var result = compilation.Emit(output);
        Check(result.Success, "Compile actual QuizData: " + string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        Assembly assembly = Assembly.Load(output.ToArray()); Type quizType = assembly.GetType("QuizData"), questionType = assembly.GetType("QuizQuestion"), choiceType = assembly.GetType("QuizChoice");
        object quiz = Activator.CreateInstance(quizType);
        quizType.GetField("quizId").SetValue(quiz, Field(asset, "quizId")); quizType.GetField("title").SetValue(quiz, Field(asset, "title")); quizType.GetField("assessmentMode").SetValue(quiz, true);
        var list = (System.Collections.IList)quizType.GetField("questions").GetValue(quiz);
        foreach (Match serialized in Questions(asset))
        {
            string body = serialized.Groups["body"].Value; object question = Activator.CreateInstance(questionType);
            questionType.GetField("questionId").SetValue(question, serialized.Groups["id"].Value); questionType.GetField("prompt").SetValue(question, Field(body, "prompt", 4));
            questionType.GetField("feedback").SetValue(question, Field(body, "feedback", 4)); questionType.GetField("correctChoiceIndex").SetValue(question, int.Parse(Field(body, "correctChoiceIndex", 4)));
            var choices = (System.Collections.IList)questionType.GetField("choices").GetValue(question);
            foreach (string text in Choices(body)) { object choice = Activator.CreateInstance(choiceType); choiceType.GetField("text").SetValue(choice, text); choices.Add(choice); }
            list.Add(question);
        }
        object[] parameters = { null };
        Check((bool)quizType.GetMethod("TryValidate").Invoke(quiz, parameters), "Real QuizData.TryValidate must accept Vance's serialized quiz: " + parameters[0]);
        Check(((string)quizType.GetProperty("ContentFingerprint").GetValue(quiz)).Length == 64, "Quiz fingerprint is valid for saved/restarted attempts.");
        questionType.GetField("correctChoiceIndex").SetValue(list[0], 4);
        Check(!(bool)quizType.GetMethod("TryValidate").Invoke(quiz, parameters), "Production validation rejects a broken answer mapping.");
    }

    private static void Authoring()
    {
        string source = Read(Author); SyntaxNode syntax = Parse(source, Author);
        var phases = syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(v => v.Identifier.ValueText == "PhaseIds");
        Check(phases.Initializer.Value.DescendantNodes().OfType<LiteralExpressionSyntax>().Select(l => l.Token.ValueText).SequenceEqual(PhaseIds),
            "Preserve all seven original phase IDs and indexes, including the cargo-unlock phase3.");
        foreach (string guard in new[] { "EditorApplication.isPlayingOrWillChangePlaymode", "EditorApplication.isCompiling", "EditorApplication.isUpdating",
            "PrefabStageUtility.GetCurrentPrefabStage", "Undo.RevertAllDownToGroup", "Undo.RecordObject", "File.Copy", "EditorUtility.IsDirty(contract)" })
            Check(source.Contains(guard), "Retain authoring safety guard: " + guard);
        var calls = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(call => call.Expression.ToString()).ToArray();
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:SaveScene|SaveScenes|SaveOpenScenes|SaveAssets|OpenScene|CloseScene|NewScene|SaveAsPrefabAsset|ApplyPrefabInstance)$")),
            "No automatic scene/prefab save or scene switch.");
        Check(calls.Count(call => call == "AssetDatabase.SaveAssetIfDirty") == 1 && source.Contains("SaveAssetIfDirty(contract)"), "Only save the scoped, initially-clean main contract asset.");
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:CompleteContract|SaveGame|UnlockFeature|UnlockMaterialForContract)$")), "No live player/reward mutations during authoring.");
        foreach (var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            string left = assignment.Left.ToString();
            Check(!Regex.IsMatch(left, @"\.(?:budget|liveLoadWeight|goldReward|expReward|minimumLoadedCargo|cargoUnlockAfterDialoguePhaseIndex|cargoUnlockProgressionId|cargoLoadedNPCPhaseId)\s*$"),
                "Do not rewrite cargo gate, mass, budget or rewards: " + left);
            Check(!left.StartsWith("phases[6].", StringComparison.Ordinal) && !left.StartsWith("phases[4].", StringComparison.Ordinal), "Preserve side phase and loaded-contract phase: " + left);
            Check(!left.StartsWith("phases[3].", StringComparison.Ordinal) || left == "phases[3].phaseDialogue", "Only cargo explanation text may change in phase3: " + left);
            if (left.EndsWith(".bridgeSpan", StringComparison.Ordinal)) Check(assignment.Right.ToString() == "80", "Only the verified80m span correction is allowed.");
        }
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:phases|AuthoredPhases)\.(?:Add|Insert|Remove|RemoveAt|Clear)$")), "Do not append, insert, remove or reorder saved phases.");
        Check(source.Contains("JsonUtility.ToJson(phases[6]) == sidePhase") && source.Contains("== sideAsset") && source.Contains("ProtectedContract(contract) == protectedContract"),
            "Verify complete side phase/asset and main settings, not only selected fields.");
        Check(source.Contains("EditorJsonUtility.ToJson(pair.Key) == pair.Value") && source.Contains("All<VehicleCargoSlot>") && source.Contains("All<CargoItem>") && source.Contains("All<LiveLoadVehicle>"),
            "Protect physical cargo, slots, truck, other NPCs and existing scene presentation.");
        Check(source.Contains("phases[2].onNPCArrivedPhaseMove = new NPCPhaseCall()") && source.Contains("phases[2].optionalDialogueSequence"),
            "Remove the arrival jump that skipped the new lesson, while preserving the real completed-dialogue cargo handoff.");
        Check(source.Contains("four marked cargo items") && source.Contains("Once all four are loaded") && source.Contains("combined live load"),
            "Player-facing narrative must still explain the actual four-item loading stage before the offer.");
        Check(!Regex.IsMatch(source, @"1,?000\s+(?:kilograms|kg)", RegexOptions.IgnoreCase), "Do not copy the reference's changed live-load weight into dialogue.");
        Check(source.Contains("silas.visibilitySaveId == \"SilasNPC\"") && source.Contains("onDialogueFinishedPhaseMove.phaseId == \"SideQuestStart\""),
            "Retain original Silas and side-contract handoff ownership.");
        string npc = Read("Assets/Script/Client/NPCProgressionManager.cs");
        Check(npc.Contains("outcome != QuizOutcome.Completed") && npc.Contains("!sameStep") && npc.Contains("AlmanacQuizAccess.TryOffer"),
            "Shared quiz flow must keep submission/cancellation guards and the Almanac entry.");
    }

    private static void SavedScene()
    {
        var docs = Documents(Read(Scene));
        foreach (var pair in baseline.ProtectedSceneDocuments)
            Check(docs.TryGetValue(pair.Key, out string doc) && HashText(OriginalPresentation(pair.Key, doc)) == pair.Value, "Protected truck/cargo/slot/tutorial/movie/other-NPC changed outside approved guidance presentation: " + pair.Key);
        Check(docs.ContainsKey("1601157397"), "Keep original scene-local Vance progression component.");
        string npc = docs["1601157397"];
        Match[] phases = Phases(npc);
        Check(phases.Length == 7 && phases.Select(p => p.Groups["id"].Value.Trim()).SequenceEqual(PhaseIds), "Saved scene retains all seven original phase indexes.");
        Check(Field(npc, "progressionSaveId") == "VanceNPC", "Keep original Vance progression identity.");
        Check(HashText(OriginalAttentionPresentation(phases[6].Value)) == baseline.SidePhaseHash, "Keep the realignment side phase unchanged apart from the approved attention flag.");
        Check(HashText(OriginalAttentionPresentation(RemoveFields(phases[3].Value, 4, "phaseDialogue"))) == baseline.CargoPhaseProtectedHash,
            "Cargo phase may change its explanation only; preserve loading tutorial, unlock stage, destination and callbacks.");
        Check(HashText(OriginalAttentionPresentation(phases[4].Value)) == baseline.Phase4Hash, "Preserve original loaded-contract phase, site and tutorial callbacks apart from its approved attention flag.");
        Check(phases[int.Parse(Field(Read(MainContract), "cargoUnlockAfterDialoguePhaseIndex"))].Groups["id"].Value == PhaseIds[3], "The cargo dialogue remains at the contract's unlock index3.");
        string cargoExplanation = Regex.Replace(phases[3].Value, @"\s+", " ");
        Check(cargoExplanation.Contains("four marked cargo items") && cargoExplanation.Contains("Once all four are loaded"), "Cargo-loading instructions must be present in the saved scene.");
        string arrival = Block(phases[2].Value, "onNPCArrivedPhaseMove", 4);
        Check(Field(arrival, "npc", 6) == "{fileID: 0}" && Field(arrival, "phaseId", 6) == string.Empty, "Lesson phase cannot jump to cargo on arrival before reading finishes.");
        string lessonFinish = Block(phases[2].Value, "onDialogueFinishedPhaseMove", 4);
        Check(Field(lessonFinish, "npc", 6) == "{fileID: 1601157397}" && Field(lessonFinish, "phaseId", 6) == PhaseIds[3], "Finished lesson must lead to the existing cargo stage.");
        Match[] intro = Steps(phases[2].Value), review = Steps(phases[5].Value);
        Check(intro.Select(s => s.Groups["type"].Value).SequenceEqual(new[] { "0", "0", "3" }), "Saved situation and forces discussion precede actual lesson popup.");
        Check(ReferenceGuid(Field(intro[2].Value, "lesson", 6)) == Guid(Lesson), "Introduction uses the combined lesson asset.");
        Check(review.Select(s => s.Groups["type"].Value).SequenceEqual(new[] { "3", "0", "2", "0" }), "Saved review > quiz introduction > assessment > Silas guidance order.");
        Check(ReferenceGuid(Field(review[0].Value, "lesson", 6)) == Guid(Lesson) && ReferenceGuid(Field(review[2].Value, "quiz", 6)) == Guid(Quiz), "Review/quiz references use the actual new assets.");
        Check(Field(review[2].Value, "quizStepId", 6) == "vance-tension-compression-assessment-v1" && !string.IsNullOrWhiteSpace(Field(review[2].Value, "quizWaitingReminder", 6)),
            "Stable quiz-step identity and a repeatable waiting reminder are required.");
        Check(review[3].Value.Contains("Silas"), "Silas handoff dialogue waits until after the completed quiz.");
        string handoff = Block(phases[5].Value, "onDialogueFinishedPhaseMove", 4), spawn = Block(phases[5].Value, "onDialogueFinished", 4);
        Check(Field(handoff, "npc", 6) == "{fileID: 1601157397}" && Field(handoff, "phaseId", 6) == "SideQuestStart", "Completion retains the existing side-contract transition.");
        Check(spawn.Contains("m_Target: {fileID: 318150252}") && spawn.Contains("m_MethodName: SpawnNPC"), "The existing Silas spawn runs only after the ordered sequence completes.");
        string archive = docs["321885003091865885"];
        Check(Regex.Matches(archive, "guid: " + Guid(Lesson)).Count == 1, "Combined lesson appears exactly once in the existing Almanac archive.");
        string contract = Regex.Replace(Read(MainContract), @"\s+", " ");
        Check(contract.Contains("80 meters") && contract.Contains("four cargo items") && contract.Contains("tension or compression"), "Saved briefing describes the actual span, loaded cargo and new lesson.");
        Check(contract.Contains("Keep all four required cargo items on the truck") && contract.Contains("complete vehicle-and-cargo load"), "Reminders and description keep the full loaded test requirement.");
    }

    // The follow-up guidance tests validate the actual flags and objective settings.
    // Restore only those approved scalar fields here, preserving this story's frozen behavior hashes.
    private static string OriginalPresentation(string id, string text)
    {
        if (id == "1601157397" || id == "318150247") return OriginalAttentionPresentation(text);
        if (new[] { "1143740769", "1487862495", "22004694", "669876432" }.Contains(id))
        {
            text = Regex.Replace(text, @"(?m)^  showInObjectives:[^\r\n]*", "  showInObjectives: 0");
            return Regex.Replace(text, @"(?m)^(  objective(?:Title|Id):)[^\r\n]*", "$1 ");
        }
        return text;
    }
    private static string OriginalAttentionPresentation(string text) => Regex.Replace(text,
        @"(?m)^( +(?:showExclamationMark|onlyShowExclamationWhenInteractionNeeded):)[^\r\n]*", "$1 0");

    private static Dictionary<string, string> Documents(string text) => Regex.Matches(text, @"(?ms)^--- !u!\d+ &(?<id>-?\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)")
        .Cast<Match>().ToDictionary(m => m.Groups["id"].Value, m => m.Value);
    private static Match[] Phases(string text) => Regex.Matches(text, @"(?ms)^  - phaseId: (?<id>[^\r\n]+)\r?\n.*?(?=^  - phaseId: |^  progressionSaveId:)").Cast<Match>().ToArray();
    private static Match[] Steps(string phase) => Regex.Matches(Block(phase, "optionalDialogueSequence", 4), @"(?ms)^    - stepType: (?<type>\d+)\r?\n.*?(?=^    - stepType: |\z)").Cast<Match>().ToArray();
    private static string Block(string text, string key, int indent) => Regex.Match(text, @"(?ms)^ {" + indent + "}" + Regex.Escape(key) + @":.*?(?=^ {" + indent + @"}[A-Za-z_]|\z)").Value;
    private static string RemoveFields(string text, int indent, params string[] keys) => Regex.Replace(text, @"(?ms)^ {" + indent + "}(?:" + string.Join("|", keys.Select(Regex.Escape)) + @"):.*?(?=^ {" + indent + @"}\S|\z)", string.Empty);
    private static Match[] Questions(string text) => Regex.Matches(text, @"(?ms)^  - questionId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - questionId: |\z)").Cast<Match>().ToArray();
    private static string[] Choices(string text) => Regex.Matches(text, @"(?m)^    - text: (?<text>[^\r\n]+)").Select(m => Scalar(m.Groups["text"].Value)).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashText(string value) => Hash(Encoding.UTF8.GetBytes(Regex.Replace(value, @"\s+", " ").Trim()));
    private static string ReferenceGuid(string text) => Regex.Match(text, @"guid: ([a-f0-9]{32})").Groups[1].Value;
    private static string Guid(string path) { var match = Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([a-f0-9]{32})"); Check(match.Success, "Missing GUID: " + path); return match.Groups[1].Value; }
    private static string OptionalField(string text, string key, int indent = 2) { var match = Regex.Match(text, @"(?m)^ {" + indent + "}" + Regex.Escape(key) + @":([^\r\n]*)"); return match.Success ? Scalar(match.Groups[1].Value) : null; }
    private static string Field(string text, string key, int indent = 2) { string value = OptionalField(text, key, indent); Check(value != null, "Missing field: " + key); return value; }
    private static string Scalar(string value) { value = value.Trim(); if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1].Replace("''", "'"); if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return JsonSerializer.Deserialize<string>(value); return value; }
    private static SyntaxNode Parse(string source, string path) { var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR")); Check(!tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), "C# syntax errors: " + path); return tree.GetRoot(); }
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static byte[] ReadBytes(string path) => File.ReadAllBytes(Path.Combine(root, path));
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static string FindRoot() { for (DirectoryInfo dir = new(Environment.CurrentDirectory); dir != null; dir = dir.Parent) if (File.Exists(Path.Combine(dir.FullName, Author))) return dir.FullName; throw new InvalidOperationException("Run from Unity project root."); }
}
