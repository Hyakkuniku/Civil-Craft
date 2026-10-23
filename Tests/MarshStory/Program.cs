using System.Globalization;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Static asset/authoring and saved-serialization regression checks only. This
// runner never starts Unity or invokes a live progression/reward callback.
internal static class Program
{
    private const string Scene = "Assets/Scenes/CanyonCrossing.unity";
    private const string Author = "Assets/Editor/MarshStoryAuthoring.cs";
    private const string Contract = "Assets/BridgeBuilder/Data/Contract/TownRiver/Marsh Contract.asset";
    private const string Quiz = "Assets/BridgeBuilder/Data/Quized/ArchBridgeQuiz.asset";
    private const string Lesson = "Assets/BridgeBuilder/Data/Lessons/ArchBridge.asset";
    private const string ConcreteBeam = "Assets/BridgeBuilder/Data/Resources/ConcreteBeam.asset";
    private const string ConcreteRoad = "Assets/BridgeBuilder/Data/Resources/Concrete Road.asset";
    private const string Pier = "Assets/BridgeBuilder/Data/Resources/Pier.asset";
    private const string ConcretePier = "Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset";
    private static readonly string[] PhaseIds = { "MeetingMarsh", "ExplainSituation", "Material Introduction", "ProjectBriefing", "MarshReview", "Resolved" };
    private static string root;
    private static int assertions;

    private static int Main(string[] args)
    {
        try
        {
            root = FindRoot();
            bool sourceOnly = args.Contains("--source-only");
            Check(args.All(argument => argument == "--source-only"), "Supported option: --source-only.");
            Check(OptionalField("    - stepType: 1\n      material: {fileID: 11400000}\n", "stepType", 4) == "1",
                "The serialized-step parser must accept Unity's list-item '- stepType:' header.");
            Check(OptionalField("    stepType: 3\n", "stepType", 4) == "3",
                "The scalar parser must also accept an ordinary step-type field.");
            CheckAssets();
            CheckAuthoring();
            CheckRuntimeBoundaries();
            CheckLegacyContinuation();
            if (!sourceOnly) CheckSavedScene();
            Console.WriteLine($"PASS: {assertions} Marsh story {(sourceOnly ? "source/asset" : "source/asset/saved-scene")} assertions. " +
                (sourceOnly ? "Scene migration/save is deliberately not claimed. " : string.Empty) +
                "Read-only checks; no Unity scene, player save, account, website or network changed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }

    private static void CheckAssets()
    {
        string contract = Read(Contract);
        Check(Field(contract, "contractID") == "MarshID", "Released Marsh contract identity must remain MarshID.");
        foreach ((string name, string value) in new[] {
            ("budget", "325000"), ("liveLoadWeight", "3000"), ("bridgeSpan", "80"),
            ("goldReward", "500"), ("expReward", "100"), ("liveLoadMode", "0"),
            ("autoCollectReward", "0"), ("winCondition", "0"), ("isTimeAttack", "0"), ("enforceMaxStress", "0") })
            Check(Field(contract, name) == value, "Preserve existing Marsh " + name + " = " + value + ".");
        Check(Field(contract, "clientName") == "Marsh", "The existing Marsh contract must still belong to Marsh.");
        Check(Field(contract, "contractMap") == "1", "Marsh must be correctly catalogued as Town River, not Canyon Crossing.");
        Match allowances = Regex.Match(contract, @"(?ms)^  allowedMaterials:\r?\n(?<body>.*?)(?=^  [a-zA-Z]|\z)");
        Check(allowances.Success, "Marsh needs its supported material allowance list.");
        Check(Regex.Matches(allowances.Groups["body"].Value, @"guid: ([a-f0-9]{32})").Select(value => value.Groups[1].Value)
            .SequenceEqual(new[] { Guid(ConcreteRoad), Guid(ConcreteBeam), Guid(ConcretePier) }), "Preserve Road/Beam allowances and replace only Marsh's Wood Pier allowance with Concrete Pier.");
        Check(Regex.Matches(allowances.Groups["body"].Value, @"(?m)^    maxPieces: 0\s*$").Count == 3, "All three existing Marsh materials must be unlimited allowances.");
        Match hidden = Regex.Match(contract, @"(?ms)^  hiddenMaterials:\r?\n(?<body>.*?)(?=^  [a-zA-Z]|\z)");
        Check(hidden.Success && !hidden.Groups["body"].Value.Contains("guid: " + Guid(ConcretePier), StringComparison.Ordinal), "The offered Concrete Pier must not also be hidden for Marsh.");
        Check(Field(Read(ConcretePier), "requiresExplicitContractAllowance") == "1" && Field(Read(ConcretePier), "isPier") == "1",
            "Marsh must explicitly opt in the configured Concrete Pier instead of weakening its global opt-in gate.");
        Check(Entries(contract, "offerDialogue", "sentences", 2).Length == 5, "Marsh briefing must contain the five project-offer lines.");
        Check(Entries(contract, "reminderDialogue", "sentences", 2).Length >= 1, "Marsh active contract needs its repeat reminder.");
        Check(Entries(contract, "finishedContractDialogue", "sentences", 2).Length == 4, "Marsh turn-in must retain the four requested completion lines.");
        Check(!Entries(contract, "offerDialogue", "sentences", 2).Any(value => value.Contains("Hint ", StringComparison.Ordinal)), "Unsupported hint/system calls must not leak into offer dialogue.");

        string quiz = Read(Quiz);
        Check(ScriptGuid(quiz) == Guid("Assets/Script/UI/Quizzes/QuizData.cs"), "Arch assessment must be a QuizData asset.");
        Check(Field(quiz, "assessmentMode") == "1", "Arch quiz must submit any score, without a passing-score gate.");
        Check(!string.IsNullOrWhiteSpace(Field(quiz, "quizId")), "Arch quiz needs a stable save identity.");
        Check(Field(quiz, "title").Contains("Arch", StringComparison.OrdinalIgnoreCase), "Quiz title should identify the Arch Bridge lesson.");
        var questions = Regex.Matches(quiz, @"(?ms)^  - questionId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - questionId: |\z)").Cast<Match>().ToArray();
        Check(questions.Length == 5, "Arch quiz must contain five questions.");
        Check(questions.Select(question => Scalar(question.Groups["id"].Value)).All(id => !string.IsNullOrWhiteSpace(id)) &&
            questions.Select(question => Scalar(question.Groups["id"].Value)).Distinct(StringComparer.Ordinal).Count() == 5,
            "Every Arch question needs a nonempty unique stable ID.");
        Check(questions.Select(question => int.Parse(Field(question.Groups["body"].Value, "correctChoiceIndex", 4), CultureInfo.InvariantCulture))
            .SequenceEqual(new[] { 1, 2, 0, 3, 1 }), "Arch correct answers must be B, C, A, D, B.");
        foreach (Match question in questions)
        {
            string body = question.Groups["body"].Value;
            Check(!string.IsNullOrWhiteSpace(Field(body, "prompt", 4)), "Every Arch question needs a prompt.");
            Check(Regex.Matches(body, @"(?m)^    - text: \S").Count == 4, "Each supplied question must have four text choices.");
        }

        string lesson = Read(Lesson);
        Check(ScriptGuid(lesson) == Guid("Assets/Script/UI/Lessons/LessonData.cs"), "Arch lesson must use the existing LessonData reader.");
        string lessonId = Field(lesson, "lessonId");
        Check(!string.IsNullOrWhiteSpace(lessonId), "Arch lesson needs a stable Almanac identity.");
        Check(Field(lesson, "lessonTitle").Contains("Arch", StringComparison.OrdinalIgnoreCase), "Arch lesson must have an identifiable title.");
        Check(!string.IsNullOrWhiteSpace(Field(lesson, "popupDescription")) && !string.IsNullOrWhiteSpace(Field(lesson, "lessonDescription")),
            "Arch lesson needs both its short popup and detailed Almanac explanation.");
        foreach (string other in Directory.GetFiles(Path.Combine(root, "Assets/BridgeBuilder/Data/Lessons"), "*.asset"))
            if (!Path.GetFullPath(other).Equals(Path.GetFullPath(Path.Combine(root, Lesson)), StringComparison.OrdinalIgnoreCase))
                Check(OptionalField(File.ReadAllText(other), "lessonId") != lessonId, "Arch lesson must not reuse another lesson's released identity.");

        // Canonical whitespace hashes captured before this migration. Material
        // physics, visual references and shared tutorial content are out of scope.
        foreach ((string path, string expected) in new[] {
            (ConcreteBeam, "886F0A22A402DCC2E32DA0F3D2D148FFE9255C1D3CAD432D9BF92605D4821D53"),
            (ConcreteRoad, "68652527D9796EEBFFD5F49CE1BB135245558FEA7DF6C9F1956AF06A9AAA2B33"),
            (Pier, "C12F8EA4E00011E0E16073CE82BECC190FD0324A75886E763D83EC2631165B76") })
        {
            string normalized = Regex.Replace(Read(path), @"\s+", " ").Trim();
            string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
            Check(actual == expected, "Shared material asset must not change: " + path);
        }
        Check(Field(Read(Pier), "displayName") == "Wood Pier", "The shared Pier asset must not be globally renamed.");
    }

    private static void CheckAuthoring()
    {
        string text = Read(Author);
        SyntaxNode syntax = Parse(text, Author);
        Check(text.Contains("scene.isDirty", StringComparison.Ordinal) || text.Contains("Scene.isDirty", StringComparison.Ordinal), "Migration must refuse an already-dirty scene.");
        Check(text.Contains("EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal), "Migration must refuse Play Mode.");
        Check(text.Contains("EditorApplication.isCompiling", StringComparison.Ordinal) && text.Contains("EditorApplication.isUpdating", StringComparison.Ordinal), "Migration must wait for compilation/imports.");
        Check(text.Contains("PrefabStageUtility.GetCurrentPrefabStage", StringComparison.Ordinal), "Migration must reject prefab-stage authoring.");
        Check(text.Contains("Undo.RevertAllDownToGroup", StringComparison.Ordinal) && text.Contains("Undo.RecordObject", StringComparison.Ordinal), "Migration must support Undo and rollback on failure.");
        Check(text.Contains("File.Copy", StringComparison.Ordinal), "Migration must make a recoverable scene backup.");
        Check(text.Contains("EditorSceneManager.MarkSceneDirty", StringComparison.Ordinal), "Migration must mark authored changes ready for manual save.");
        Check(text.Contains("MarshNPC", StringComparison.Ordinal), "Migration must target Marsh by its exact stable progression ID.");
        foreach (string id in PhaseIds) Check(text.Contains("\"" + id + "\"", StringComparison.Ordinal), "Migration must preserve/use stable phase: " + id);
        var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(call => call.Expression.ToString()).ToArray();
        Check(invocations.Count(name => name == "EditorJsonUtility.ToJson") >= 2,
            "Protected UnityEngine.Object snapshots and their comparison must use the Editor serializer, which supports Camera/Transform objects.");
        Check(!invocations.Any(name => name == "JsonUtility.ToJson" || name == "UnityEngine.JsonUtility.ToJson"),
            "JsonUtility.ToJson cannot safely snapshot native Camera/Transform objects during migration preflight.");
        Check(!invocations.Any(name => Regex.IsMatch(name, @"(?:^|\.)(?:SaveScene|SaveScenes|SaveOpenScenes|SaveAssets|SaveAssetIfDirty)$")), "Authoring must never automatically save scenes/assets.");
        Check(!invocations.Any(name => Regex.IsMatch(name, @"(?:^|\.)(?:OpenScene|CloseScene|NewScene)$")), "Authoring must preserve the current loaded scene setup.");
        Check(!text.Contains("AddComponent<LiveLoadVehicle>", StringComparison.Ordinal) && !text.Contains("AddComponent<BoatBridgeCrossing>", StringComparison.Ordinal), "Story authoring must not create a replacement simulation/traffic system.");
        Check(!text.Contains("AddComponent<Almanac", StringComparison.Ordinal) && !text.Contains("DestroyImmediate", StringComparison.Ordinal), "Adding a lesson must not rebuild/destroy existing Almanac UI.");
        Check(!text.Contains("CompleteContract(", StringComparison.Ordinal) && !text.Contains("UnlockMaterialForContract(", StringComparison.Ordinal), "Editor authoring must not grant live rewards or free material use.");
    }

    private static void CheckRuntimeBoundaries()
    {
        string progressionPath = "Assets/Script/Client/NPCProgressionManager.cs";
        var manager = Type(progressionPath, "NPCProgressionManager");
        Check(Method(manager, "HandleContractCompleted").Contains("AdvanceToNextContract", StringComparison.Ordinal), "Marsh must retain existing collection-driven phase continuation.");
        string ordered = Method(manager, "PlayOrderedOptionalStep");
        Check(ordered.Contains("outcome != QuizOutcome.Completed", StringComparison.Ordinal) && ordered.Contains("!sameStep", StringComparison.Ordinal), "Cancelled/unavailable/stale quizzes must not advance NPC story.");
        Check(ordered.Contains("AlmanacQuizAccess.TryOffer", StringComparison.Ordinal) && ordered.Contains("TryShowLesson", StringComparison.Ordinal), "Marsh must use existing assessment and lesson readers.");
        string arrival = Method(manager, "StartOrderedDialogueAfterArrival");
        Check(arrival.Contains("HasOpenPanel", StringComparison.Ordinal) && arrival.Contains("IsTransitioning", StringComparison.Ordinal) && arrival.Contains("Time.timeScale > 0f", StringComparison.Ordinal), "Ordered post-turn-in presentation must wait for UI, transitions and Pause.");
        string finish = Method(manager, "FinishOrderedOptionalSequence");
        Check(finish.IndexOf("MarkPhaseDialogueCompleted", StringComparison.Ordinal) < finish.IndexOf("phase.InvokeDialogueFinished", StringComparison.Ordinal), "Ordered phase completion must be recorded before its final continuation callback.");
        string giver = "Assets/Script/Client/NPCContractGIver.cs";
        Check(Method(Type(giver, "NPCContractGiver"), "ClaimReward").Contains("ShowCompleteButton", StringComparison.Ordinal), "Turn-in dialogue must still use the existing explicit Collect panel.");
        string saved = Method(Type("Assets/Script/Player/PlayerSave/PlayerDataManager.cs", "PlayerDataManager"), "CompleteContract");
        Check(saved.IndexOf("TrySaveGame()", StringComparison.Ordinal) < saved.IndexOf("OnContractCompleted?.Invoke", StringComparison.Ordinal), "Collection must persist successfully before Marsh progression is notified.");
    }

    private static void CheckSavedScene()
    {
        string scene = Read(Scene);
        var components = Regex.Matches(scene, @"(?ms)^--- !u!114 &(?<id>-?\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)").Cast<Match>().ToArray();
        string progressionGuid = Guid("Assets/Script/Client/NPCProgressionManager.cs");
        Match[] marsh = components.Where(component => component.Groups["body"].Value.Contains("guid: " + progressionGuid, StringComparison.Ordinal) && OptionalField(component.Groups["body"].Value, "progressionSaveId") == "MarshNPC").ToArray();
        Check(marsh.Length == 1, "Saved Canyon must contain exactly one MarshNPC progression.");
        Check(marsh[0].Groups["id"].Value == "1999832195", "Existing Marsh progression component identity must be retained.");
        string body = marsh[0].Groups["body"].Value;
        Check(Field(body, "contractGiver") == "{fileID: 1999832198}", "Marsh must retain its existing contract giver.");
        Check(Field(body, "persistProgression") == "1" && Field(body, "automaticallyAdvanceOnContractCompletion") == "1", "Marsh must save stable phase progress and wait for normal contract collection.");
        Match[] phases = Regex.Matches(body, @"(?ms)^  - phaseId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - phaseId: |^  progressionSaveId:|\z)").Cast<Match>().ToArray();
        Check(phases.Select(phase => Scalar(phase.Groups["id"].Value)).SequenceEqual(PhaseIds), "Preserve four released phase IDs/indexes; append only MarshReview and Resolved.");
        string[] p = phases.Select(phase => phase.Groups["body"].Value).ToArray();
        for (int index = 0; index < p.Length; index++)
        {
            Check(Field(p[index], "contract", 4) == (index == 3 ? "{fileID: 11400000, guid: " + Guid(Contract) + ", type: 2}" : "{fileID: 0}"), "Contract belongs only to ProjectBriefing, not introductions/review.");
            Check(string.IsNullOrWhiteSpace(Field(p[index], "unlockFeatureIdAfterDialogue", 4)) && Field(p[index], "showFeatureCollectPopup", 4) == "0", "No unintended default Minimap/feature reward: " + PhaseIds[index]);
        }
        Check(Field(p[0], "targetLocation", 4) == "{fileID: 0}", "Meeting keeps Marsh's authored initial position.");
        Check(Entries(p[0], "phaseDialogue", "sentences", 4).Length == 5, "Meeting dialogue must contain the five updated introductory lines.");
        Check(p[0].Contains("phaseId: ExplainSituation", StringComparison.Ordinal), "Meeting must continue to the existing walk destination.");
        Check(Entries(p[1], "phaseDialogue", "sentences", 4).Length == 1, "Pre-cinematic situation line must remain separate from the later explanation.");
        Check(p[1].Contains("m_Target: {fileID: 1510433738}", StringComparison.Ordinal) && p[1].Contains("m_MethodName: PlayCinematic", StringComparison.Ordinal), "Situation dialogue must retain the exact existing Marsh camera cinematic.");
        Check(Field(p[3], "targetBuildLocation", 4) == "{fileID: 1984790326}", "Keep Marsh's existing build site.");
        Check(Field(p[3], "cinematicAfterOffer", 4) == "{fileID: 0}", "Marsh cinematic belongs before materials, not after contract acceptance.");
        Match[] steps = OrderedSteps(p[2]);
        Check(steps.Select(step => Field(step.Value, "stepType", 4)).SequenceEqual(new[] { "0", "0", "1", "0", "1", "0", "1", "0", "3" }),
            "Material phase must order post-cinematic situation, three spoken material introductions/popups, then Arch explanation/popup.");
        Check(Entries(steps[0].Value, "dialogue", "sentences", 6).Length == 5, "Post-cinematic situation explanation must contain five lines.");
        Check(Entries(steps[1].Value, "dialogue", "sentences", 6).Length == 3 && Entries(steps[3].Value, "dialogue", "sentences", 6).Length == 2 &&
            Entries(steps[5].Value, "dialogue", "sentences", 6).Length == 2 && Entries(steps[7].Value, "dialogue", "sentences", 6).Length == 5,
            "Concrete Beam/Road/Pier/Arch spoken blocks must contain 3/2/2/5 lines.");
        var introductions = steps.Where(step => Field(step.Value, "stepType", 4) == "1").ToArray();
        Check(introductions.Length == 3, "Material phase must show exactly three existing material introductions.");
        Check(introductions.Select(step => AssetReferenceGuid(Field(step.Value, "material", 6)))
            .SequenceEqual(new[] { Guid(ConcreteBeam), Guid(ConcreteRoad), Guid(ConcretePier) }), "Material popup order must be Concrete Beam, Concrete Road, then Concrete Pier.");
        Check(steps[5].Value.Contains("<b><#E09500>Concrete Pier</color></b>", StringComparison.Ordinal),
            "Marsh must introduce the Concrete Pier by name immediately before its popup.");
        foreach (Match introduction in introductions)
        {
            int at = Array.IndexOf(steps, introduction);
            Check(at > 0 && Field(steps[at - 1].Value, "stepType", 4) == "0" && Entries(steps[at - 1].Value, "dialogue", "sentences", 6).Length > 0,
                "Each material popup must follow its spoken NPC introduction.");
        }
        Check(steps.Any(step => Field(step.Value, "stepType", 4) == "3" && AssetReferenceGuid(Field(step.Value, "lesson", 6)) == Guid(Lesson)), "The Arch lesson popup must finish before the contract briefing.");
        Check(p[2].Contains("phaseId: ProjectBriefing", StringComparison.Ordinal) && Field(p[2], "playDialogueOnArrival", 4) == "1", "Material/lesson completion must continue into the original briefing phase.");

        Match[] review = OrderedSteps(p[4]);
        Check(review.Select(step => Field(step.Value, "stepType", 4)).SequenceEqual(new[] { "3", "0", "2" }), "Post-collection review must order lesson, quiz invitation dialogue, then Almanac assessment.");
        Check(review.Any(step => Field(step.Value, "stepType", 4) == "3" && AssetReferenceGuid(Field(step.Value, "lesson", 6)) == Guid(Lesson)), "MarshReview must show the existing lesson reader after collection.");
        Match[] quizzes = review.Where(step => Field(step.Value, "stepType", 4) == "2").ToArray();
        Check(quizzes.Length == 1 && AssetReferenceGuid(Field(quizzes[0].Value, "quiz", 6)) == Guid(Quiz) && !string.IsNullOrWhiteSpace(Field(quizzes[0].Value, "quizStepId", 6)), "Post-turn-in assessment must reference the new quiz with one stable step identity.");
        Check(Field(p[4], "playDialogueOnArrival", 4) == "1" && p[4].Contains("phaseId: Resolved", StringComparison.Ordinal), "Review opens only after collection and resolves only after its ordered content completes.");
        Check(!OrderedSteps(p[3]).Any(step => Field(step.Value, "stepType", 4) == "2"), "Quiz must not start at contract acceptance.");
        Check(Field(p[5], "showExclamationMark", 4) == "0", "Resolved Marsh must not retain a quest exclamation.");

        Match[] archive = components.Where(component => component.Groups["body"].Value.Contains("guid: " + Guid("Assets/Script/UI/Almanac/AlmanacLessonTab.cs"), StringComparison.Ordinal)).ToArray();
        Check(archive.Length >= 1 && archive.All(component => component.Groups["body"].Value.Contains("guid: " + Guid(Lesson), StringComparison.Ordinal)), "The existing Almanac lesson archive must contain the Arch lesson.");

        Match[] cinematic = components.Where(component => component.Groups["id"].Value == "1510433738").ToArray();
        Check(cinematic.Length == 1 && cinematic[0].Groups["body"].Value.Contains("m_MethodName: MoveToSelectedPhase", StringComparison.Ordinal), "The existing cinematic completion must still enter the material phase.");
        Match[] ford = components.Where(component => component.Groups["body"].Value.Contains("guid: " + progressionGuid, StringComparison.Ordinal) && OptionalField(component.Groups["body"].Value, "progressionSaveId") == "FordNPC").ToArray();
        Check(ford.Length == 1, "Existing Ford progression must not be replaced/duplicated.");
        string fordGameObject = Field(ford[0].Groups["body"].Value, "m_GameObject");
        string spawnGuid = Guid("Assets/Script/Interaction System/Interactables/NPCSpawnerCondition.cs");
        var fordGates = components.Where(component => component.Groups["body"].Value.Contains("guid: " + spawnGuid, StringComparison.Ordinal) && Field(component.Groups["body"].Value, "m_GameObject") == fordGameObject).ToArray();
        Check(fordGates.Length == 1, "Ford must have one existing-system spawn condition, not duplicate competing gates.");
        string gate = fordGates[0].Groups["body"].Value;
        Check(Field(gate, "useMultipleConditions") == "1" && Field(gate, "matchMode") == "0" && Field(gate, "hideWhenMatched") == "0" &&
              Field(gate, "waitForManualSpawn") == "0" && Field(gate, "updateAutomatically") == "1",
            "Ford visibility must automatically require all authored rules, without manual overrides.");
        Check(gate.Contains("guid: " + Guid(Contract), StringComparison.Ordinal) && gate.Contains("id: MarshNPC", StringComparison.Ordinal) && gate.Contains("phaseId: Resolved", StringComparison.Ordinal),
            "Ford must wait for both Marsh contract collection and the finished review/quiz Resolved phase.");
    }

    private static void CheckLegacyContinuation()
    {
        const string helperPath = "Assets/Script/Client/MarshPostContractContinuation.cs";
        string helper = Read(helperPath);
        Parse(helper, helperPath);
        Check(!helper.Contains("CompleteContract(", StringComparison.Ordinal) && !helper.Contains("SaveGame(", StringComparison.Ordinal) &&
            !helper.Contains("UnlockFeature(", StringComparison.Ordinal) && !helper.Contains("UnlockMaterial", StringComparison.Ordinal),
            "Legacy Marsh continuation may resume only the existing post-collection phase, not grant completion/rewards/unlocks.");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var trees = new[] { CSharpSyntaxTree.ParseText(helper), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LegacyBoundary.cs.txt"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LegacyScenarios.cs.txt"))) };
        var compilation = CSharpCompilation.Create("MarshLegacyChecks_" + System.Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var emitted = compilation.Emit(output);
        Check(emitted.Success, "Legacy helper production source failed isolated compilation: " + string.Join("; ", emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        try
        {
            assertions += (int)Assembly.Load(output.ToArray()).GetType("LegacyScenarios").GetMethod("Run").Invoke(null, null);
        }
        catch (TargetInvocationException error) { throw new InvalidOperationException("Legacy continuation scenario: " + error.InnerException?.Message, error.InnerException); }
    }

    private static Match[] OrderedSteps(string phase) => Regex.Matches(phase, @"(?ms)^    - stepType: \d+[^\r\n]*\r?\n.*?(?=^    - stepType: |^    [a-zA-Z]|\z)").Cast<Match>().ToArray();
    private static string[] Entries(string body, string parent, string list, int indent)
    {
        Match section = Regex.Match(body, @"(?ms)^ {" + indent + "}" + Regex.Escape(parent) + @":\r?\n(?<body>.*?)(?=^ {" + indent + @"}[a-zA-Z]|\z)");
        Check(section.Success, "Missing section: " + parent);
        Match value = Regex.Match(section.Groups["body"].Value, @"(?ms)^ {" + (indent + 2) + "}" + Regex.Escape(list) + @":(?<body>.*?)(?=^ {" + (indent + 2) + @"}[a-zA-Z]|\z)");
        Check(value.Success, "Missing list: " + parent + "." + list);
        return Regex.Matches(value.Groups["body"].Value, @"(?m)^ {" + (indent + 2) + @"}- (?<line>[^\r\n]*)").Select(match => Scalar(match.Groups["line"].Value)).ToArray();
    }
    private static string ScriptGuid(string text) => AssetReferenceGuid(Field(text, "m_Script"));
    private static string AssetReferenceGuid(string text) => Regex.Match(text, @"guid: ([a-f0-9]{32})").Groups[1].Value;
    private static string Guid(string path)
    {
        Match result = Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([a-f0-9]{32})");
        Check(result.Success, "Missing asset GUID: " + path);
        return result.Groups[1].Value;
    }
    private static string OptionalField(string text, string name, int indent = 2)
    {
        string listPrefix = name == "stepType" ? "(?:- )?" : string.Empty;
        Match field = Regex.Match(text, @"(?m)^ {" + indent + "}" + listPrefix + Regex.Escape(name) + @":([^\r\n]*)");
        return field.Success ? Scalar(field.Groups[1].Value) : null;
    }
    private static string Field(string text, string name, int indent = 2)
    {
        string value = OptionalField(text, name, indent);
        Check(value != null, "Missing serialized field: " + name);
        return value;
    }
    private static string Scalar(string value)
    {
        string result = value.Trim();
        return result.Length >= 2 && result[0] == '\'' && result[^1] == '\'' ? result[1..^1].Replace("''", "'") : result;
    }
    private static SyntaxNode Parse(string text, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR"));
        var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Check(errors.Length == 0, "C# syntax errors in " + path + ": " + string.Join("; ", errors.Select(error => error.ToString())));
        return tree.GetRoot();
    }
    private static ClassDeclarationSyntax Type(string path, string name) => Parse(Read(path), path).DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name);
    private static string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name).ToFullString();
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static void Check(bool result, string message) { assertions++; if (!result) throw new InvalidOperationException(message); }
    private static string FindRoot()
    {
        DirectoryInfo here = new DirectoryInfo(Environment.CurrentDirectory);
        while (here != null)
        {
            if (File.Exists(Path.Combine(here.FullName, "Assets/Script/Client/NPCProgressionManager.cs"))) return here.FullName;
            here = here.Parent;
        }
        throw new InvalidOperationException("Run this read-only check from the Unity project root.");
    }
}
