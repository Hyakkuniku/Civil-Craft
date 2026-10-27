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
    private const string Author = "Assets/Editor/SilasStoryAuthoring.cs";
    private const string Data = "Assets/BridgeBuilder/Data/";
    private static readonly string[] PhaseIds = { "Meeting Silas", "The Final Supply Gap ", "AfterCinematic", "Project Briefing", "SilasLoadTransfer",
        "ExplainSituation2", "ExplainSituationContinuation", "MATERIAL INTRODUCTION", "ProjectBriefing2", "ProjectBriefing3", "Multiplayer" };
    private static readonly string[] ContractPaths = { Data + "Contract/CanyonCrossing/Silas/SilasContract.asset", Data + "Contract/CanyonCrossing/Silas/MainContractSilas.asset" };
    private static readonly string[] Topics = { "LoadDistribution", "LoadTransfer" };
    private static readonly string[] Names = { "Load Distribution", "Load Transfer" };
    private static string root;
    private static int checks;
    private static Baseline baseline;
    private sealed class Baseline
    {
        public Dictionary<string, string> SharedRuntimeSources { get; set; }
        public Dictionary<string, string> ProtectedSceneDocuments { get; set; }
        public Dictionary<string, string> ProtectedPhaseHashes { get; set; }
        public Dictionary<string, string> ProtectedContractHashes { get; set; }
        public Dictionary<string, string> ProtectedSiteHashes { get; set; }
        public string RopeAsset { get; set; }
        public string RopeAssetSHA256 { get; set; }
    }
    private static int Main(string[] args)
    {
        try
        {
            root = FindRoot();
            Check(args.All(value => value == "--source-only"), "Only --source-only is supported.");
            bool sourceOnly = args.Contains("--source-only");
            baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline.json")));
            Assets(sourceOnly); Authoring();
            if (!sourceOnly) SavedScene();
            Console.WriteLine($"PASS: {checks} Silas {(sourceOnly ? "asset/source" : "asset/source/saved-scene")} assertions. " +
                (sourceOnly ? "Scene apply/save is deliberately not claimed. " : "") + "Read-only checks; player saves, rewards, multiplayer sessions and network untouched.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }
    private static void Assets(bool sourceOnly)
    {
        foreach (var pair in baseline.SharedRuntimeSources)
            Check(Hash(ReadBytes(pair.Key)) == pair.Value, "Existing progression, cargo, dialogue, multiplayer gate or simulation implementation changed: " + pair.Key);
        Check(Hash(ReadBytes(baseline.RopeAsset)) == baseline.RopeAssetSHA256, "Keep existing Rope material balance, physics and unlock identity unchanged.");
        string[] ids = { "SilasMainContract", "MainContractSilas" };
        for (int i = 0; i < ContractPaths.Length; i++)
        {
            string contract = Read(ContractPaths[i]);
            Check(HashText(RemoveFields(contract, 2, "jobDescription", "offerDialogue", "continueOfferDialogue", "reminderDialogue", "finishedContractDialogue", "bridgeSpan")) == baseline.ProtectedContractHashes[ContractPaths[i]],
                "Every Silas contract setting outside narrative and confirmed span must remain unchanged: " + ids[i]);
            foreach (var pair in new Dictionary<string, string> { ["contractID"] = ids[i], ["clientName"] = "Silas", ["budget"] = "200000", ["liveLoadWeight"] = i == 0 ? "1000" : "5000",
                ["goldReward"] = "500", ["expReward"] = "100", ["isStoryModeContract"] = "1", ["countsTowardMapAchievements"] = "1", ["autoCollectReward"] = "0", ["allowVehicleCargo"] = "0" })
                Check(Field(contract, pair.Key) == pair.Value, "Preserve both MAIN contracts' classification, identity, weight, rewards and settings: " + pair.Key);
            Check(sourceOnly ? new[] { "30", "100" }.Contains(Field(contract, "bridgeSpan")) : Field(contract, "bridgeSpan") == "100", "Only the verified30m placeholder to100m metadata correction is allowed.");
        }
        for (int i = 0; i < Topics.Length; i++)
        {
            string lessonPath = Lesson(i), quizPath = Quiz(i), simulationPath = Simulation(i);
            string lesson = Read(lessonPath), quiz = Read(quizPath), simulation = Read(simulationPath);
            string slug = i == 0 ? "load-distribution" : "load-transfer";
            Check(ReferenceGuid(Field(lesson, "m_Script")) == Guid("Assets/Script/UI/Lessons/LessonData.cs"), "Use the existing lesson popup and Almanac reader.");
            Check(Field(lesson, "lessonId") == "civil-craft-" + slug + "-lesson-v1" && Field(lesson, "lessonTitle") == Names[i], "Distinct permanent lesson ID/title for " + Names[i]);
            Check(Field(lesson, "popupDescription").Length > 100 && Field(lesson, "lessonDescription").Length > 250, "Each topic needs short introduction and full Almanac explanation.");
            Check(ReferenceGuid(Field(quiz, "m_Script")) == Guid("Assets/Script/UI/Quizzes/QuizData.cs"), "Use existing shuffled assessment/answer-review system.");
            Check(Field(quiz, "quizId") == "civil-craft-" + slug + "-quiz-v1" && Field(quiz, "assessmentMode") == "1", "Each submitted score advances with no new quiz rewards.");
            Match[] questions = Questions(quiz);
            Check(questions.Length == 5 && questions.Select(question => question.Groups["id"].Value).Distinct().Count() == 5, "Five uniquely identified questions for each main contract.");
            Check(questions.Select(question => int.Parse(Field(question.Groups["body"].Value, "correctChoiceIndex", 4))).SequenceEqual(i == 0 ? new[] { 2, 1, 0, 3, 2 } : new[] { 3, 1, 0, 2, 1 }),
                "Keep the reference answer mapping for " + Names[i]);
            foreach (Match question in questions)
            {
                Check(Choices(question.Groups["body"].Value).Length == 4, "Every question needs four choices.");
                Check(Field(question.Groups["body"].Value, "prompt", 4).Length > 10 && Field(question.Groups["body"].Value, "feedback", 4).Length > 10, "Keep question and answer-review explanation complete.");
            }
            ValidateProductionQuiz(quiz);
            Check(ReferenceGuid(Field(simulation, "m_Script")) == Guid("Assets/Script/BuildMode/Simulation Lessons/SimulationLessonDefinition.cs"), "Use existing observed-event simulation presenter.");
            foreach (string field in new[] { "simulationStarted", "liveLoadStarted", "vehicleEnteredBridge", "vehicleReachedMidpoint", "highStressDetected", "stressLimitExceeded", "firstMemberBreak", "vehicleStuck", "vehicleFailed", "bridgeSucceeded", "simulationStopped" })
                Check(Field(simulation, field).Length > 10, "Complete existing simulation event message: " + field);
            Check(!Regex.IsMatch(lesson + quiz + simulation, @"\b\d[\d,]*\s*(?:kg|kilograms)\b", RegexOptions.IgnoreCase), "Do not copy different reference vehicle weights into learning content.");
            foreach (string path in Directory.GetFiles(Path.Combine(root, Data + "Lessons"), "*.asset"))
                if (!Path.GetFullPath(path).Equals(Path.GetFullPath(Path.Combine(root, lessonPath)), StringComparison.OrdinalIgnoreCase))
                    Check(OptionalField(File.ReadAllText(path), "lessonId") != Field(lesson, "lessonId"), "Do not reuse another lesson's save identity.");
        }
    }
    private static void ValidateProductionQuiz(string asset)
    {
        const string boundary = "namespace UnityEngine { public class ScriptableObject {} public class Sprite {} public sealed class TextAreaAttribute : System.Attribute { public TextAreaAttribute(int a,int b){} } public sealed class TooltipAttribute : System.Attribute { public TooltipAttribute(string s){} } public sealed class HideInInspector : System.Attribute {} public sealed class CreateAssetMenuAttribute : System.Attribute { public string fileName; public string menuName; } }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("SilasQuizChecks_" + System.Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(Read("Assets/Script/UI/Quizzes/QuizData.cs")), CSharpSyntaxTree.ParseText(boundary) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new(); var result = compilation.Emit(output);
        Check(result.Success, "Compile real QuizData: " + string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
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
        Check((bool)quizType.GetMethod("TryValidate").Invoke(quiz, parameters), "Real QuizData.TryValidate accepts serialized Silas quiz: " + parameters[0]);
        Check(((string)quizType.GetProperty("ContentFingerprint").GetValue(quiz)).Length == 64, "Quiz save fingerprint remains usable across restarts.");
        questionType.GetField("correctChoiceIndex").SetValue(list[0], 4);
        Check(!(bool)quizType.GetMethod("TryValidate").Invoke(quiz, parameters), "Production validation rejects an invalid answer mapping.");
    }
    private static void Authoring()
    {
        string source = Read(Author); SyntaxNode syntax = Parse(source, Author);
        var phaseArrays = syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>().Where(value => value.Identifier.ValueText.EndsWith("PhaseIds", StringComparison.Ordinal));
        Check(phaseArrays.Any(value => value.Initializer.Value.DescendantNodes().OfType<LiteralExpressionSyntax>().Select(literal => literal.Token.ValueText).SequenceEqual(PhaseIds)),
            "Keep all eleven saved phase IDs/indexes, including the trailing space in the first tour phase.");
        foreach (string guard in new[] { "EditorApplication.isPlayingOrWillChangePlaymode", "EditorApplication.isCompiling", "EditorApplication.isUpdating", "PrefabStageUtility.GetCurrentPrefabStage", "Undo.RevertAllDownToGroup", "Undo.RecordObject", "File.Copy", "EditorUtility.IsDirty" })
            Check(source.Contains(guard), "Retain saved-edit/Undo safety guard: " + guard);
        var calls = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(call => call.Expression.ToString()).ToArray();
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:SaveScene|SaveScenes|SaveOpenScenes|SaveAssets|OpenScene|CloseScene|NewScene|SaveAsPrefabAsset|ApplyPrefabInstance)$")), "Do not automatically save/switch scenes or shared prefabs.");
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:CompleteContract|SaveGame|UnlockFeature|UnlockMaterialForContract)$")), "Authoring must not grant rewards, connect multiplayer or write player progression.");
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:phases|AuthoredPhases)\.(?:Add|Insert|Remove|RemoveAt|Clear)$")), "Do not append, insert, remove or reorder saved phases.");
        foreach (var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            string left = assignment.Left.ToString();
            Check(!Regex.IsMatch(left, @"\.(?:budget|liveLoadWeight|goldReward|expReward|isStoryModeContract|countsTowardMapAchievements|unlockFeatureIdAfterDialogue|featureUnlockDisplayName|featureUnlockIcon|showFeatureCollectPopup)\s*$"),
                "Do not change masses, main classification, rewards or multiplayer grant settings: " + left);
            if (left.EndsWith(".bridgeSpan", StringComparison.Ordinal)) Check(assignment.Right.ToString() == "100", "Only confirmed100m metadata correction is permitted.");
        }
        string runtime = Read("Assets/Script/Client/NPCProgressionManager.cs");
        Check(runtime.Contains("outcome != QuizOutcome.Completed") && runtime.Contains("!sameStep") && runtime.Contains("AlmanacQuizAccess.TryOffer"), "Shared assessment completion must retain cancellation/stale-callback guards and automatic Quiz-tab entry.");
        Check(runtime.Contains("GrantConfiguredPhaseFeature(phase)") && runtime.Contains("RestoreMissedPhaseFeatureUnlocks"), "Keep original permanent multiplayer grant and restart recovery path.");
    }
    private static void SavedScene()
    {
        var docs = Documents(Read(Scene));
        foreach (var pair in baseline.ProtectedSceneDocuments)
            Check(docs.TryGetValue(pair.Key, out string doc) && HashText(doc) == pair.Value, "Protected cinematic/tutorial/other-NPC/cargo/presenter changed: " + pair.Key);
        string npc = docs["318150247"]; Match[] phases = Phases(npc);
        Check(phases.Length == 11 && phases.Select(phase => Scalar(phase.Groups["id"].Value)).SequenceEqual(PhaseIds), "Saved scene preserves the original eleven ordered phase identities.");
        Check(Field(npc, "progressionSaveId") == "SilasNPC", "Preserve Silas's permanent progression identity.");
        for (int i = 0; i < phases.Length; i++)
        {
            var excluded = new List<string> { "phaseDialogue" };
            if (i == 1 || i == 5 || i == 6) excluded.Add("repeatDialogue");
            if (new[] { 2, 4, 7, 9 }.Contains(i)) excluded.AddRange(new[] { "optionalDialogueSequence", "repeatDialogue", "playDialogueOnArrival" });
            Check(HashText(RemoveFields(phases[i].Value, 4, excluded.ToArray())) == baseline.ProtectedPhaseHashes[i.ToString()],
                "Only approved narrative/ordered-flow presentation fields may change in phase " + i + "; preserve callbacks, targets, contracts and grant settings.");
        }
        Match[] distributionIntro = Steps(phases[2].Value), distributionReview = Steps(phases[4].Value), transferIntro = Steps(phases[7].Value), transferReview = Steps(phases[9].Value);
        Check(distributionIntro.Select(step => step.Groups["type"].Value).SequenceEqual(new[] { "0", "0", "3" }), "Distribution situation and explanation precede its lesson popup.");
        Check(ReferenceGuid(Field(distributionIntro[2].Value, "lesson", 6)) == Guid(Lesson(0)), "First introduction uses actual Load Distribution asset.");
        Check(transferIntro.Select(step => step.Groups["type"].Value).SequenceEqual(new[] { "0", "1", "0", "0", "3" }), "Rope must be introduced in spoken dialogue, then collected, followed by towers and Load Transfer lesson.");
        Check(ReferenceGuid(Field(transferIntro[1].Value, "material", 6)) == "4524a3fc876c6b640a735e6808fe1271" && ReferenceGuid(Field(transferIntro[4].Value, "lesson", 6)) == Guid(Lesson(1)), "Preserve real Rope unlock and wire actual transfer lesson.");
        Match[][] reviews = { distributionReview, transferReview };
        for (int i = 0; i < reviews.Length; i++)
        {
            Check(reviews[i].Select(step => step.Groups["type"].Value).SequenceEqual(new[] { "3", "0", "2", "0" }), "Each completed main contract gets lesson review > Quiz intro > assessment > story continuation.");
            Check(ReferenceGuid(Field(reviews[i][0].Value, "lesson", 6)) == Guid(Lesson(i)) && ReferenceGuid(Field(reviews[i][2].Value, "quiz", 6)) == Guid(Quiz(i)), "Review/assessment references must match the correct main topic.");
            Check(Field(reviews[i][2].Value, "quizStepId", 6) == "silas-" + (i == 0 ? "load-distribution" : "load-transfer") + "-assessment-v1" && !string.IsNullOrWhiteSpace(Field(reviews[i][2].Value, "quizWaitingReminder", 6)), "Stable assessment-step identity and retry reminder.");
        }
        foreach (int index in new[] { 2, 4, 7, 9 }) Check(Field(phases[index].Value, "playDialogueOnArrival", 4) == "1" && Field(phases[index].Value, "repeatDialogue", 4) == "1", "Ordered lesson/review remains retryable after interruption: phase" + index);
        foreach (int index in new[] { 1, 5, 6 }) Check(Field(phases[index].Value, "repeatDialogue", 4) == "1", "Cancelled camera tours/conversation remain retryable: phase" + index);
        string finale = Block(phases[9].Value, "onDialogueFinished", 4);
        Check(finale.Contains("m_Target: {fileID: 1171701037}") && finale.Contains("m_MethodName: PlayCinematic"), "Finale camera runs only after the final quiz and story continuation close.");
        Check(docs["1171701037"].Contains("m_Target: {fileID: 318150256}") && docs["318150256"].Contains("phaseId: Multiplayer"), "Preserve final camera completion's original Multiplayer phase handoff.");
        string multiplayer = phases[10].Value;
        Check(Field(multiplayer, "unlockFeatureIdAfterDialogue", 4) == "multiplayer" && Field(multiplayer, "featureUnlockDisplayName", 4) == "Multiplayer" && Field(multiplayer, "showFeatureCollectPopup", 4) == "1" &&
            ReferenceGuid(Field(multiplayer, "featureUnlockIcon", 4)) == "366072043fdddde49a26ca9e066e7602", "Retain original multiplayer feature ID, Collect acknowledgement, name and icon.");
        string multiplayerText = Regex.Replace(Block(multiplayer, "phaseDialogue", 4), @"\s+", " ");
        Check(multiplayerText.Contains("Multiplayer") && multiplayerText.Contains("room code", StringComparison.OrdinalIgnoreCase), "Silas still explains the real private-room multiplayer feature.");
        foreach (var pair in baseline.ProtectedSiteHashes)
            Check(HashText(RemoveFields(docs[pair.Key], 2, "simulationLesson")) == pair.Value, "Only simulation lesson assignment may change the existing main site's camera/geometry/settings.");
        string[] siteIds = { "4247076027259698586", "4247076026978126500" };
        for (int i = 0; i < siteIds.Length; i++)
        {
            Check(ReferenceGuid(Field(docs[siteIds[i]], "simulationLesson")) == Guid(Simulation(i)), "Each main site uses its matching observed-event simulation lesson.");
            Check(Regex.Matches(docs["321885003091865885"], "guid: " + Guid(Lesson(i))).Count == 1, "Each new lesson appears exactly once in the Almanac archive.");
        }
    }
    private static string Lesson(int i) => Data + "Lessons/" + Topics[i] + ".asset";
    private static string Quiz(int i) => Data + "Quized/" + Topics[i] + "Quiz.asset";
    private static string Simulation(int i) => Data + "Simulation Lessons/" + Names[i] + " Lesson.asset";
    private static Dictionary<string, string> Documents(string text) => Regex.Matches(text, @"(?ms)^--- !u!\d+ &(?<id>-?\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)").Cast<Match>().ToDictionary(match => match.Groups["id"].Value, match => match.Value);
    private static Match[] Phases(string text) => Regex.Matches(text, @"(?ms)^  - phaseId: (?<id>[^\r\n]+)\r?\n.*?(?=^  - phaseId: |^  progressionSaveId:)").Cast<Match>().ToArray();
    private static Match[] Steps(string phase) => Regex.Matches(Block(phase, "optionalDialogueSequence", 4), @"(?ms)^    - stepType: (?<type>\d+)\r?\n.*?(?=^    - stepType: |\z)").Cast<Match>().ToArray();
    private static string Block(string text, string key, int indent) => Regex.Match(text, @"(?ms)^ {" + indent + "}" + Regex.Escape(key) + @":.*?(?=^ {" + indent + @"}[A-Za-z_]|\z)").Value;
    private static string RemoveFields(string text, int indent, params string[] keys) => Regex.Replace(text, @"(?ms)^ {" + indent + "}(?:" + string.Join("|", keys.Select(Regex.Escape)) + @"):.*?(?=^ {" + indent + @"}[A-Za-z_]|\z)", string.Empty);
    private static Match[] Questions(string text) => Regex.Matches(text, @"(?ms)^  - questionId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - questionId: |\z)").Cast<Match>().ToArray();
    private static string[] Choices(string text) => Regex.Matches(text, @"(?m)^    - text: (?<text>[^\r\n]+)").Select(match => Scalar(match.Groups["text"].Value)).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashText(string value) => Hash(Encoding.UTF8.GetBytes(Regex.Replace(value, @"\s+", " ").Trim()));
    private static string ReferenceGuid(string text) => Regex.Match(text, @"guid: ([a-f0-9]{32})").Groups[1].Value;
    private static string Guid(string path) { var match = Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([a-f0-9]{32})"); Check(match.Success, "Missing GUID: " + path); return match.Groups[1].Value; }
    private static string OptionalField(string text, string key, int indent = 2) { var match = Regex.Match(text, @"(?m)^ {" + indent + "}" + Regex.Escape(key) + @":([^\r\n]*)"); return match.Success ? Scalar(match.Groups[1].Value) : null; }
    private static string Field(string text, string key, int indent = 2) { string value = OptionalField(text, key, indent); Check(value != null, "Missing field: " + key); return value; }
    private static string Scalar(string value) { value = value.Trim(); if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') return value[1..^1].Replace("''", "'"); if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return JsonSerializer.Deserialize<string>(value); return value; }
    private static SyntaxNode Parse(string source, string path) { var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR")); Check(!tree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), "C# syntax error: " + path); return tree.GetRoot(); }
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static byte[] ReadBytes(string path) => File.ReadAllBytes(Path.Combine(root, path));
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static string FindRoot() { for (DirectoryInfo directory = new(Environment.CurrentDirectory); directory != null; directory = directory.Parent) if (File.Exists(Path.Combine(directory.FullName, "ProjectSettings/ProjectVersion.txt"))) return directory.FullName; throw new InvalidOperationException("Run from Unity project root."); }
}
