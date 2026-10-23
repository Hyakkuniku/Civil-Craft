using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private const string Scene = "Assets/Scenes/CanyonCrossing.unity";
    private const string Author = "Assets/Editor/FordStoryAuthoring.cs";
    private const string Helper = "Assets/Script/Client/FordPostContractContinuation.cs";
    private const string Contract = "Assets/BridgeBuilder/Data/Contract/TownRiver/Ford Contract.asset";
    private const string MarshContract = "Assets/BridgeBuilder/Data/Contract/TownRiver/Marsh Contract.asset";
    private const string Quiz = "Assets/BridgeBuilder/Data/Quized/MaterialStressQuiz.asset";
    private const string Lesson = "Assets/BridgeBuilder/Data/Lessons/MaterialStress.asset";
    private static readonly string[] PhaseIds = { "MeetingFord", "ExplainSituation", "LessonIntroduction", "ProjectBriefing", "FordReview", "Resolved" };
    // Complete saved Marsh component, canonicalized for whitespace, captured
    // before any Ford authoring. Previous Marsh story is out of this task.
    private const string MarshFingerprint = "CB3E196F8A95822AB9591F27D601EF2DFCBF2C8ABA79A8D612FB32E9D8412BA9";
    private static string root;
    private static int checks;
    private static int Main(string[] args)
    {
        try
        {
            root = FindRoot();
            bool sourceOnly = args.Contains("--source-only");
            bool helperOnly = args.Contains("--helper-only");
            Check(args.All(argument => argument == "--source-only" || argument == "--helper-only") && !(sourceOnly && helperOnly), "Supported options: --source-only or --helper-only.");
            Check(OptionalField("    - stepType: 3\n      lesson: {fileID: 0}", "stepType", 4) == "3", "Unity list-item step headers must parse correctly.");
            Assets();
            LegacyContinuation();
            ExistingRuntimeGuards();
            if (helperOnly)
            {
                Console.WriteLine($"PASS: {checks} Ford asset/helper/runtime-boundary assertions. Authoring migration and saved-scene checks deliberately not run. " +
                    "Read-only checks; no live Unity scene, save, account, reward, website or network touched.");
                return 0;
            }
            Authoring();
            if (!sourceOnly) SavedScene();
            Console.WriteLine($"PASS: {checks} Ford story {(sourceOnly ? "source/asset" : "source/asset/saved-scene")} assertions. " +
                (sourceOnly ? "Scene migration/save is deliberately not claimed. " : string.Empty) +
                "Read-only checks; no live Unity scene, save, account, reward, website or network touched.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }

    private static void Assets()
    {
        string contract = Read(Contract);
        Check(Field(contract, "contractID") == "FordMainContract" && Field(contract, "clientName") == "Ford", "Ford's existing contract identity/owner must remain unchanged.");
        foreach ((string field, string expected) in new[] { ("bridgeSpan", "80"), ("goldReward", "500"), ("expReward", "100"),
            ("liveLoadMode", "0"), ("autoCollectReward", "0"), ("winCondition", "0"), ("isTimeAttack", "0"),
            ("enforceMaxStress", "0"), ("contractMap", "1"), ("hideFromLeaderboard", "1"), ("countsTowardMapAchievements", "0") })
            Check(Field(contract, field) == expected, "Ford contract must keep " + field + " = " + expected + ".");
        foreach (string field in new[] { "budget", "liveLoadWeight" })
        {
            bool parsed = double.TryParse(Field(contract, field), NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
            Check(parsed && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0, "Ford " + field + " must remain a positive, user-tunable value; do not force an inferred balance.");
        }
        string[] offer = Entries(contract, "offerDialogue", "sentences", 2);
        Check(offer.Length == 6 && offer.Any(line => line.Contains("rocky", StringComparison.OrdinalIgnoreCase)) &&
              offer.Any(line => line.Contains("direct route", StringComparison.OrdinalIgnoreCase)) &&
              offer.Any(line => line.Contains("stress", StringComparison.OrdinalIgnoreCase)) && offer.Any(line => line.Contains("take the contract", StringComparison.OrdinalIgnoreCase)),
            "Ford briefing must retain all six lines, including the rocky support the author will add later.");
        Check(Entries(contract, "reminderDialogue", "sentences", 2).Length == 1, "Ford active contract needs its repeatable stress/support reminder.");
        Check(Entries(contract, "finishedContractDialogue", "sentences", 2).Length == 4, "Ford turn-in must contain the four completion lines.");
        Check(Field(contract, "allowedMaterials") == "[]" && Field(contract, "hiddenTools") == "[]" && Field(contract, "hiddenMaterials") == "[]", "Ford does not introduce new material/tool restrictions.");
        Check(Field(contract, "featureUnlockRewards") == "[]", "No unsupported feature/reward system should be added to Ford.");

        string quiz = Read(Quiz);
        Check(ScriptGuid(quiz) == Guid("Assets/Script/UI/Quizzes/QuizData.cs"), "Material Stress quiz must use existing QuizData.");
        Check(Field(quiz, "quizId") == "civil-craft-material-stress-quiz-v1" && Field(quiz, "assessmentMode") == "1", "Material Stress assessment needs its stable identity and any-score behavior.");
        Check(Field(quiz, "title") == "Material Stress Quiz", "Ford assessment title should identify Material Stress.");
        Match[] questions = Regex.Matches(quiz, @"(?ms)^  - questionId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - questionId: |\z)").Cast<Match>().ToArray();
        Check(questions.Length == 5 && questions.Select(question => Scalar(question.Groups["id"].Value)).Distinct(StringComparer.Ordinal).Count() == 5, "The Material Stress quiz needs five uniquely identified questions.");
        Check(questions.Select(question => int.Parse(Field(question.Groups["body"].Value, "correctChoiceIndex", 4), CultureInfo.InvariantCulture)).SequenceEqual(new[] { 2, 1, 3, 0, 2 }), "Material Stress answers must be C, B, D, A, C.");
        foreach (Match question in questions)
        {
            Check(!string.IsNullOrWhiteSpace(Scalar(question.Groups["id"].Value)) && !string.IsNullOrWhiteSpace(Field(question.Groups["body"].Value, "prompt", 4)), "Each Material Stress question needs stable nonempty identity and prompt.");
            Check(Regex.Matches(question.Groups["body"].Value, @"(?m)^    - text: \S").Count == 4, "Each Material Stress question must have four text choices.");
        }

        string lesson = Read(Lesson);
        Check(ScriptGuid(lesson) == Guid("Assets/Script/UI/Lessons/LessonData.cs"), "Material Stress must use the current lesson popup/Almanac reader.");
        Check(Field(lesson, "lessonId") == "civil-craft-material-stress-lesson-v1" && Field(lesson, "lessonTitle") == "Material Stress", "Material Stress lesson needs a stable independent identity/title.");
        Check(!string.IsNullOrWhiteSpace(Field(lesson, "popupDescription")) && !string.IsNullOrWhiteSpace(Field(lesson, "lessonDescription")), "Material Stress needs short introduction and detailed Almanac text.");
        foreach (string path in Directory.GetFiles(Path.Combine(root, "Assets/BridgeBuilder/Data/Lessons"), "*.asset"))
            if (!Path.GetFullPath(path).Equals(Path.GetFullPath(Path.Combine(root, Lesson)), StringComparison.OrdinalIgnoreCase))
                Check(OptionalField(File.ReadAllText(path), "lessonId") != Field(lesson, "lessonId"), "Material Stress must not reuse an existing lesson save ID.");
    }

    private static void Authoring()
    {
        string source = Read(Author);
        SyntaxNode syntax = Parse(source, Author);
        foreach (string guard in new[] { "scene.isDirty", "EditorApplication.isPlayingOrWillChangePlaymode", "EditorApplication.isCompiling", "EditorApplication.isUpdating",
            "PrefabStageUtility.GetCurrentPrefabStage", "Undo.RevertAllDownToGroup", "Undo.RecordObject", "File.Copy", "EditorSceneManager.MarkSceneDirty" })
            Check(source.Contains(guard, StringComparison.Ordinal), "Ford authoring must keep saved-scene safety guard: " + guard);
        foreach (string identity in PhaseIds.Concat(new[] { "FordNPC", "MarshNPC", "BrooksNpc" }))
            Check(source.Contains("\"" + identity + "\"", StringComparison.Ordinal), "Ford authoring must target stable identity: " + identity);
        var calls = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(call => call.Expression.ToString()).ToArray();
        Check(calls.Count(call => call == "EditorJsonUtility.ToJson") >= 2 && !calls.Any(call => call == "JsonUtility.ToJson" || call == "UnityEngine.JsonUtility.ToJson"), "Native Camera/Transform snapshots/comparison must use EditorJsonUtility, not JsonUtility.");
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:SaveScene|SaveScenes|SaveOpenScenes|SaveAssets|SaveAssetIfDirty|OpenScene|CloseScene|NewScene)$")), "Ford authoring must not auto-save or switch scenes/assets.");
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:CompleteContract|SaveGame|UnlockMaterialForContract|UnlockFeature)$")), "Editor authoring must not mutate live player progression/rewards.");
        Check(!source.Contains("AddComponent<LiveLoadVehicle>", StringComparison.Ordinal) && !source.Contains("AddComponent<BoatBridgeCrossing>", StringComparison.Ordinal) &&
            !source.Contains("AddComponent<Almanac", StringComparison.Ordinal) && !source.Contains("DestroyImmediate", StringComparison.Ordinal), "Ford story must not replace simulations, existing Almanac UI, or authored geometry.");
        Check(!Regex.IsMatch(source, @"AddComponent\s*<\s*Camera\s*>"), "Ford cinematic must use the existing gameplay camera, not create another world/map camera.");
        string reframe = Method(Type(Author, "FordStoryAuthoring"), "ReframeOverview");
        Check(reframe.Contains("PoseMatches", StringComparison.Ordinal) && reframe.Contains("custom poses", StringComparison.Ordinal) &&
              reframe.Contains("Undo.RecordObjects", StringComparison.Ordinal) && reframe.Contains("Undo.RevertAllDownToGroup", StringComparison.Ordinal),
            "The optional overview repair must protect custom poses and be undoable without saving the scene.");
        foreach (AssignmentExpressionSyntax assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            string left = assignment.Left.ToString();
            Check(!Regex.IsMatch(left, @"\.(?:budget|liveLoadWeight|goldReward|expReward)\s*$"), "Authoring must not overwrite user-owned contract balance/rewards: " + left);
        }
    }

    private static void ExistingRuntimeGuards()
    {
        var npc = Type("Assets/Script/Client/NPCProgressionManager.cs", "NPCProgressionManager");
        Check(Method(npc, "HandleContractCompleted").Contains("AdvanceToNextContract", StringComparison.Ordinal), "Ford review must use existing collection-driven phase continuation.");
        string ordered = Method(npc, "PlayOrderedOptionalStep");
        Check(ordered.Contains("outcome != QuizOutcome.Completed", StringComparison.Ordinal) && ordered.Contains("!sameStep", StringComparison.Ordinal), "Cancelled/unavailable/stale assessment callbacks must not advance Ford.");
        Check(ordered.Contains("AlmanacQuizAccess.TryOffer", StringComparison.Ordinal) && ordered.Contains("TryShowLesson", StringComparison.Ordinal), "Ford must use existing Almanac assessment and lesson systems.");
        string arrival = Method(npc, "StartOrderedDialogueAfterArrival");
        Check(arrival.Contains("HasOpenPanel", StringComparison.Ordinal) && arrival.Contains("IsTransitioning", StringComparison.Ordinal) && arrival.Contains("Time.timeScale > 0f", StringComparison.Ordinal), "Post-turn-in presentation must wait for panel/Pause/dialogue/transition restoration.");
        string completion = Method(Type("Assets/Script/Player/PlayerSave/PlayerDataManager.cs", "PlayerDataManager"), "CompleteContract");
        Check(completion.IndexOf("TrySaveGame()", StringComparison.Ordinal) < completion.IndexOf("OnContractCompleted?.Invoke", StringComparison.Ordinal), "Ford progression must not advance before durable collection.");
    }

    private static void LegacyContinuation()
    {
        string helper = Read(Helper);
        Parse(helper, Helper);
        Check(helper.Contains("\"FordNPC\"", StringComparison.Ordinal) && helper.Contains("\"FordReview\"", StringComparison.Ordinal), "Ford's legacy continuation must be scoped to its own stable NPC/review identities.");
        Check(!helper.Contains("CompleteContract(", StringComparison.Ordinal) && !helper.Contains("SaveGame(", StringComparison.Ordinal) && !helper.Contains("UnlockFeature(", StringComparison.Ordinal), "Legacy helper may resume a completed review, never grant rewards/completion.");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var trees = new[] { CSharpSyntaxTree.ParseText(helper), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LegacyBoundary.cs.txt"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LegacyScenarios.cs.txt"))) };
        var compilation = CSharpCompilation.Create("FordLegacyChecks_" + System.Guid.NewGuid().ToString("N"), trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        Check(result.Success, "Ford legacy helper production source isolated compilation: " + string.Join("; ", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        try { checks += (int)Assembly.Load(output.ToArray()).GetType("LegacyScenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Ford legacy continuation scenario: " + exception.InnerException?.Message, exception.InnerException); }
    }

    private static void SavedScene()
    {
        string scene = Read(Scene);
        Match[] blocks = Regex.Matches(scene, @"(?ms)^--- !u!114 &(?<id>-?\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)").Cast<Match>().ToArray();
        string progressionGuid = Guid("Assets/Script/Client/NPCProgressionManager.cs");
        Match ExactNPC(string saveId) => blocks.Single(block => block.Groups["body"].Value.Contains("guid: " + progressionGuid, StringComparison.Ordinal) && OptionalField(block.Groups["body"].Value, "progressionSaveId") == saveId);
        Match marsh = ExactNPC("MarshNPC");
        Check(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Regex.Replace(NormalizeAuthorizedMarshPier(marsh.Value), @"\s+", " ").Trim()))) == MarshFingerprint, "Ford authoring must preserve Marsh except the later explicitly authorized Concrete Pier introduction replacement.");
        Match ford = ExactNPC("FordNPC");
        Check(ford.Groups["id"].Value == "6542345702712754136", "Keep the existing Ford progression component identity.");
        string body = ford.Groups["body"].Value;
        Check(Field(body, "m_GameObject") == "{fileID: 5139320289784941425}" && Field(body, "contractGiver") == "{fileID: 1353656389295492758}", "Keep Ford's authored model/root and contract giver.");
        Check(Field(body, "persistProgression") == "1" && Field(body, "automaticallyAdvanceOnContractCompletion") == "1", "Ford must persist progression and wait for normal contract collection.");
        Match[] phases = Regex.Matches(body, @"(?ms)^  - phaseId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - phaseId: |^  progressionSaveId:|\z)").Cast<Match>().ToArray();
        Check(phases.Select(phase => Scalar(phase.Groups["id"].Value)).SequenceEqual(PhaseIds), "Ford's six stable story phases must have the agreed order.");
        string[] p = phases.Select(phase => phase.Groups["body"].Value).ToArray();
        for (int index = 0; index < p.Length; index++)
        {
            string reference = Field(p[index], "contract", 4);
            Check(index == 3 ? AssetReferenceGuid(reference) == Guid(Contract) : reference == "{fileID: 0}", "Ford contract belongs only to ProjectBriefing, not learning/review/idle phases.");
            Check(string.IsNullOrWhiteSpace(Field(p[index], "unlockFeatureIdAfterDialogue", 4)) && Field(p[index], "showFeatureCollectPopup", 4) == "0", "Ford must not grant unintended default feature rewards: " + PhaseIds[index]);
        }
        Check(Field(p[3], "targetBuildLocation", 4) == "{fileID: 1890576949}", "Ford must retain the exact existing construction workbench.");
        Check(Field(p[3], "cinematicAfterOffer", 4) == "{fileID: 0}", "Ford situation cinematic belongs before lesson/offer, not after acceptance.");
        Check(p[0].Contains("phaseId: ExplainSituation", StringComparison.Ordinal), "Meeting must lead Ford to the construction site.");
        Check(p[2].Contains("phaseId: ProjectBriefing", StringComparison.Ordinal), "Lesson introduction must finish before opening the contract offer.");
        Match[] introduction = Steps(p[2]);
        Check(introduction.Any(step => Field(step.Value, "stepType", 4) == "3" && AssetReferenceGuid(Field(step.Value, "lesson", 6)) == Guid(Lesson)), "Ford must introduce the actual Material Stress lesson before acceptance.");
        Check(!Steps(p[3]).Any(step => Field(step.Value, "stepType", 4) == "2"), "Ford assessment may not start at acceptance.");
        Match[] review = Steps(p[4]);
        Check(review.Select(step => Field(step.Value, "stepType", 4)).SequenceEqual(new[] { "3", "0", "2" }), "Ford review must order lesson, invitation dialogue, then assessment.");
        Check(AssetReferenceGuid(Field(review[0].Value, "lesson", 6)) == Guid(Lesson), "Post-collection review must use the Material Stress reader.");
        Check(AssetReferenceGuid(Field(review[2].Value, "quiz", 6)) == Guid(Quiz) && !string.IsNullOrWhiteSpace(Field(review[2].Value, "quizStepId", 6)), "Ford assessment must use the new quiz and stable step ID.");
        Check(Field(p[4], "playDialogueOnArrival", 4) == "1" && p[4].Contains("phaseId: Resolved", StringComparison.Ordinal), "Post-collection review must resolve only after its ordered completion.");
        Check(Field(p[5], "showExclamationMark", 4) == "0" && Field(p[5], "playDialogueOnArrival", 4) == "0" && Field(p[5], "repeatDialogue", 4) == "1", "Resolved Ford must be repeatable idle without quest marker or automatic dialogue.");
        CheckCameraAndGuide(scene, blocks, p, body);

        string spawnGuid = Guid("Assets/Script/Interaction System/Interactables/NPCSpawnerCondition.cs");
        Match[] Gates(string gameObject) => blocks.Where(block => block.Groups["body"].Value.Contains("guid: " + spawnGuid, StringComparison.Ordinal) && Field(block.Groups["body"].Value, "m_GameObject") == gameObject).ToArray();
        Match[] fordGates = Gates(Field(body, "m_GameObject"));
        Check(fordGates.Length == 1 && fordGates[0].Groups["id"].Value == "6542345702712754137", "Keep Ford's single existing Marsh visibility gate, without duplicates.");
        Gate(fordGates[0].Groups["body"].Value, MarshContract, "MarshNPC", Field(body, "m_GameObject"));
        Match brooks = ExactNPC("BrooksNpc");
        Check(Field(brooks.Groups["body"].Value, "m_GameObject") == "{fileID: 7596029509589877757}", "Keep Brooks's existing root, not a substitute NPC.");
        Match[] brooksGates = Gates(Field(brooks.Groups["body"].Value, "m_GameObject"));
        Check(brooksGates.Length == 1, "Brooks needs one existing-system visibility gate.");
        Gate(brooksGates[0].Groups["body"].Value, Contract, "FordNPC", Field(brooks.Groups["body"].Value, "m_GameObject"));

        string lessonTabGuid = Guid("Assets/Script/UI/Almanac/AlmanacLessonTab.cs");
        Match[] archives = blocks.Where(block => block.Groups["body"].Value.Contains("guid: " + lessonTabGuid, StringComparison.Ordinal)).ToArray();
        Check(archives.Length > 0, "Existing Almanac lesson archive must remain available.");
        foreach (Match archive in archives)
        {
            Match list = Regex.Match(archive.Groups["body"].Value, @"(?ms)^  allLessons:\r?\n(?<body>.*?)(?=^  [a-zA-Z]|\z)");
            Check(list.Success && Regex.Matches(list.Groups["body"].Value, "guid: " + Guid(Lesson)).Count == 1, "Material Stress must be registered once in the real serialized Almanac list, not merely found by Resources fallback.");
        }
        Match[] legacy = blocks.Where(block => block.Groups["body"].Value.Contains("guid: " + Guid(Helper), StringComparison.Ordinal) && Field(block.Groups["body"].Value, "m_GameObject") == Field(body, "m_GameObject")).ToArray();
        Check(legacy.Length == 1 && Field(legacy[0].Groups["body"].Value, "npc") == "{fileID: 6542345702712754136}" &&
            AssetReferenceGuid(Field(legacy[0].Groups["body"].Value, "contract")) == Guid(Contract) && Field(legacy[0].Groups["body"].Value, "reviewPhaseId") == "FordReview", "Ford needs one correctly configured legacy-completion continuation.");
        Match workbench = blocks.Single(block => block.Groups["id"].Value == "1890576949");
        Check(Field(workbench.Groups["body"].Value, "activeContract") == "{fileID: 0}", "The workbench must not retain its unrelated placeholder NewContract before phase acceptance.");
    }

    private static void CheckCameraAndGuide(string scene, Match[] components, string[] phases, string ford)
    {
        string Component(string reference) => components.Single(block => block.Groups["id"].Value == LocalId(reference)).Groups["body"].Value;
        Match[] objects = Regex.Matches(scene, @"(?ms)^--- !u!1 &(?<id>-?\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)").Cast<Match>().ToArray();
        Match[] transforms = Regex.Matches(scene, @"(?ms)^--- !u!4 &(?<id>-?\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)").Cast<Match>().ToArray();
        Match[] storyRoots = objects.Where(block => OptionalField(block.Groups["body"].Value, "m_Name") == "Ford Story").ToArray();
        Check(storyRoots.Length == 1 && Field(storyRoots[0].Groups["body"].Value, "m_IsActive") == "1", "Ford needs exactly one active authored helper root.");
        string rootObjectId = storyRoots[0].Groups["id"].Value;
        // Resolve the new root's explicitly listed component identity instead of
        // requiring m_GameObject on every Transform in the world. Unity prefab
        // instances can serialize unrelated native Transforms as stripped
        // records, where m_GameObject is intentionally absent.
        HashSet<string> rootComponents = Regex.Matches(storyRoots[0].Groups["body"].Value, @"(?m)^  - component: \{fileID: (-?\d+)\}")
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Match[] rootTransforms = transforms.Where(block => rootComponents.Contains(block.Groups["id"].Value)).ToArray();
        Check(rootTransforms.Length == 1, "The authored Ford Story root must explicitly own exactly one serialized Transform component.");
        Match rootTransform = rootTransforms[0];
        Check(LocalId(Field(rootTransform.Groups["body"].Value, "m_GameObject")) == rootObjectId,
            "Ford Story's listed Transform must point back to the exact authored GameObject.");
        Check(Field(rootTransform.Groups["body"].Value, "m_Father") == "{fileID: 0}", "Ford Story helpers must be independent scene roots, not placed under a hidden NPC.");
        bool IsUnderStory(string reference)
        {
            string id = LocalId(reference);
            HashSet<string> visited = new(StringComparer.Ordinal);
            while (id != "0" && visited.Add(id))
            {
                Match transform = transforms.SingleOrDefault(block => block.Groups["id"].Value == id);
                if (transform == null) return false;
                if (LocalId(Field(transform.Groups["body"].Value, "m_GameObject")) == rootObjectId) return true;
                id = LocalId(Field(transform.Groups["body"].Value, "m_Father"));
            }
            return false;
        }
        string spawn = Field(ford, "authoredSpawnLocation");
        string site = Field(phases[1], "targetLocation", 4);
        Check(IsUnderStory(spawn) && IsUnderStory(site) && spawn != site, "Ford spawn and construction-site markers must be distinct independent helpers.");
        Check(Field(phases[0], "targetLocation", 4) == "{fileID: 0}" && phases.Skip(1).All(phase => Field(phase, "targetLocation", 4) == site), "Ford starts at his authored spawn and uses one stable site for the rest of this story.");
        Check(Entries(phases[0], "phaseDialogue", "sentences", 4).Length == 6 && Entries(phases[1], "phaseDialogue", "sentences", 4).Length == 1, "Ford meeting and pre-cinematic explanations must contain six/one lines.");
        Match[] intro = Steps(phases[2]);
        Check(intro.Select(step => Field(step.Value, "stepType", 4)).SequenceEqual(new[] { "0", "0", "3" }) &&
              Entries(intro[0].Value, "dialogue", "sentences", 6).Length == 5 &&
              intro[0].Value.Contains("rocky section in the middle", StringComparison.Ordinal) &&
              Entries(intro[1].Value, "dialogue", "sentences", 6).Length == 5,
            "Ford introduction must retain situation explanation, Material Stress explanation, then its lesson popup.");

        string cinematicReference = EventTarget(phases[1], "onDialogueFinished", "PlayCinematic", 4);
        string cinematic = Component(cinematicReference);
        Check(ScriptGuid(cinematic) == Guid("Assets/Script/Scene/CinematicDirector.cs") && Field(cinematic, "cinematicID") == "FordConstructionRoute-v1", "Ford's situation event must play his own animated construction-route cinematic.");
        Check(Field(cinematic, "playOnlyOnce") == "0" && Field(cinematic, "playOnStart") == "0", "Ford situation cinematic must be explicitly triggered and retryable, without one-shot callback stalls.");
        Check(AssetReferenceGuid(Field(cinematic, "contractToDisplay")) == Guid(Contract), "Ford cinematic may display only Ford's contract.");
        string template = components.Single(block => block.Groups["id"].Value == "1510433738").Groups["body"].Value;
        foreach (string field in new[] { "cinematicCamera", "playerActor", "playerAnimator" })
            Check(Field(cinematic, field) == Field(template, field) && Field(cinematic, field) != "{fileID: 0}", "Ford must reuse existing Marsh gameplay presentation actor/camera references: " + field);
        Match[] shots = Regex.Matches(cinematic, @"(?ms)^  - shotName: (?<name>[^\r\n]*)\r?\n(?<body>.*?)(?=^  - shotName: |^  [a-zA-Z]|\z)").Cast<Match>().ToArray();
        Check(shots.Length is 3 or 4, "Ford must have three broad route/site shots, plus only a verified optional rock shot.");
        Check(shots[0].Groups["name"].Value.Trim() == "Construction connection" && shots[1].Groups["name"].Value.Trim() == "Existing delivery approaches" && shots[^1].Groups["name"].Value.Trim() == "Bridge site and banks", "Ford cinematic should show the new connection, existing approaches, and bridge banks in order.");
        if (shots.Length == 4) Check(shots[2].Groups["name"].Value.Trim() == "Existing rocky section", "Only an existing verified rock may add a fourth shot.");
        foreach (Match shot in shots)
        {
            string body = shot.Groups["body"].Value;
            Check(double.Parse(Field(body, "duration", 4), CultureInfo.InvariantCulture) > 0, "Camera shots need a real positive animation duration.");
            string start = Field(body, "cameraStartPoint", 4), end = Field(body, "cameraEndPoint", 4);
            Check(start != end && IsUnderStory(start) && IsUnderStory(end), "Camera shot endpoints must be separate authored Ford helpers.");
            Check(Field(body, "playerStartPoint", 4) == "{fileID: 0}" && Field(body, "playerWalkTarget", 4) == "{fileID: 0}" && Field(body, "playWalkAnimation", 4) == "0", "Ford camera sequence may not reposition/walk the player.");
        }
        string actionReference = EventTarget(cinematic, "OnCinematicFinished", "ContinueAfterSituationCinematic", 2);
        string action = Component(actionReference);
        Check(ScriptGuid(action) == Guid(Helper) && Field(action, "npc") == "{fileID: 6542345702712754136}" && Field(action, "reviewPhaseId") == "FordReview", "The final camera callback must use Ford's guarded continuation, not an unconditional stale phase action.");
        string guideReference = EventTarget(phases[0], "onDialogueFinished", "StartTutorial", 4);
        Check(EventTarget(phases[1], "onNPCInteracted", "Skip", 4) == guideReference, "The site interaction must dismiss exactly the follow guide that Meeting started.");
        string guide = Component(guideReference);
        Check(ScriptGuid(guide) == Guid("Assets/Script/Level/Tutorial/TutorialSequence.cs") && Field(guide, "lessonName") == "Story/Ford/FollowToSite-v1", "Ford follow guidance must use the existing TutorialSequence system with stable identity.");
        Check(Field(guide, "playOnStart") == "0" && Field(guide, "autoStartNextSequence") == "0" && Field(guide, "nextSequence") == "{fileID: 0}", "Ford guide must not start globally or chain an unrelated tutorial.");
        Check(Regex.Matches(guide, @"(?m)^  - message:").Count == 1 && Field(guide, "canSkip", 4) == "1" && Field(guide, "showNextButton", 4) == "0", "Ford follow guide must be one skippable navigation step, not an undismissable panel blocker.");
        Check(guide.Contains("target: " + site, StringComparison.Ordinal) && guide.Contains("advancesTutorial: 1", StringComparison.Ordinal), "The follow-guide waypoint must target Ford's site and complete on arrival.");
    }

    private static string LocalId(string reference)
    {
        Match match = Regex.Match(reference, @"fileID: (-?\d+)");
        Check(match.Success, "Expected local Unity file reference: " + reference); return match.Groups[1].Value;
    }
    private static string EventTarget(string body, string field, string method, int indent)
    {
        Match callback = Regex.Match(body, @"(?ms)^ {" + indent + "}" + Regex.Escape(field) + @":\r?\n(?<body>.*?)(?=^ {" + indent + @"}[a-zA-Z]|\z)");
        Check(callback.Success, "Missing persistent event: " + field);
        Match[] calls = Regex.Matches(callback.Groups["body"].Value, @"(?ms)^ +\- m_Target: (?<target>\{fileID: -?\d+\})[^\r\n]*\r?\n(?<body>.*?)(?=^ +\- m_Target: |\z)").Cast<Match>().ToArray();
        Check(calls.Length == 1 && OptionalField(calls[0].Groups["body"].Value, "m_MethodName", indent + 6) == method && OptionalField(calls[0].Groups["body"].Value, "m_CallState", indent + 6) != "0", "Expected exactly one enabled " + method + " callback in " + field);
        return calls[0].Groups["target"].Value;
    }

    private static void Gate(string body, string requiredContract, string npcId, string rootReference)
    {
        foreach ((string field, string value) in new[] { ("m_Enabled", "1"), ("useMultipleConditions", "1"), ("matchMode", "0"), ("hideWhenMatched", "0"),
            ("updateAutomatically", "1"), ("waitForManualSpawn", "0"), ("persistManualOverride", "0"), ("useDespawnConditions", "0") })
            Check(Field(body, field) == value, npcId + " dependent visibility must use automatic All rules, without overrides: " + field);
        Check(Field(body, "npcRoot") == rootReference, "Visibility gate must control the correct existing NPC root.");
        Match section = Regex.Match(body, @"(?ms)^  conditions:\r?\n(?<body>.*?)(?=^  [a-zA-Z]|\z)");
        Match[] rules = Regex.Matches(section.Groups["body"].Value, @"(?ms)^  - kind: (?<kind>\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^  - kind: |\z)").Cast<Match>().ToArray();
        Check(rules.Length == 2 && rules.All(rule => Field(rule.Groups["body"].Value, "mustBeTrue", 4) == "1"), "Visibility must require two positive conditions.");
        Check(rules.Any(rule => rule.Groups["kind"].Value == "1" && AssetReferenceGuid(Field(rule.Groups["body"].Value, "contract", 4)) == Guid(requiredContract)), "Visibility must wait for collected contract completion.");
        Check(rules.Any(rule => rule.Groups["kind"].Value == "2" && Field(rule.Groups["body"].Value, "id", 4) == npcId && Field(rule.Groups["body"].Value, "phaseId", 4) == "Resolved"), "Visibility must also wait for resolved lesson/quiz phase arrival.");
    }

    private static Match[] Steps(string body) => Regex.Matches(body, @"(?ms)^    - stepType: \d+[^\r\n]*\r?\n.*?(?=^    - stepType: |^    [a-zA-Z]|\z)").Cast<Match>().ToArray();
    private static string[] Entries(string body, string parent, string list, int indent)
    {
        Match section = Regex.Match(body, @"(?ms)^ {" + indent + "}" + Regex.Escape(parent) + @":\r?\n(?<body>.*?)(?=^ {" + indent + @"}[a-zA-Z]|\z)");
        Check(section.Success, "Missing section: " + parent);
        Match value = Regex.Match(section.Groups["body"].Value, @"(?ms)^ {" + (indent + 2) + "}" + Regex.Escape(list) + @":(?<body>.*?)(?=^ {" + (indent + 2) + @"}[a-zA-Z]|\z)");
        Check(value.Success, "Missing list: " + parent + "." + list);
        return Regex.Matches(value.Groups["body"].Value, @"(?m)^ {" + (indent + 2) + @"}- (?<line>[^\r\n]*)").Select(match => Scalar(match.Groups["line"].Value)).ToArray();
    }
    private static string ScriptGuid(string text) => AssetReferenceGuid(Field(text, "m_Script"));
    private static string NormalizeAuthorizedMarshPier(string value)
    {
        // Do not replace the original fingerprint: permit only the later
        // user-authorized popup identity and its preceding highlighted name.
        const string concrete = "7fbcf5a7e459d434f970d2c976504d5c", wood = "b98aa9ba752ac28449feebc76f256bf2";
        int count = Regex.Matches(value, Regex.Escape(concrete)).Count;
        if (count == 0) return value;
        const string label = @"<b><#E09500>Concrete\s+Pier</color></b>";
        Check(count == 1 && Regex.Matches(value, label).Count == 1, "Only one Marsh material popup and one spoken label may change to Concrete Pier.");
        return Regex.Replace(value.Replace(concrete, wood, StringComparison.Ordinal), label, "<b><#E09500>Pier</color></b>");
    }
    private static string AssetReferenceGuid(string text) => Regex.Match(text, @"guid: ([a-f0-9]{32})").Groups[1].Value;
    private static string Guid(string path)
    {
        Match match = Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([a-f0-9]{32})");
        Check(match.Success, "Missing asset GUID: " + path); return match.Groups[1].Value;
    }
    private static string OptionalField(string text, string name, int indent = 2)
    {
        Match match = Regex.Match(text, @"(?m)^ {" + indent + "}" + (name == "stepType" ? "(?:- )?" : string.Empty) + Regex.Escape(name) + @":([^\r\n]*)");
        return match.Success ? Scalar(match.Groups[1].Value) : null;
    }
    private static string Field(string text, string name, int indent = 2) { string result = OptionalField(text, name, indent); Check(result != null, "Missing serialized field: " + name); return result; }
    private static string Scalar(string value) { string result = value.Trim(); return result.Length >= 2 && result[0] == '\'' && result[^1] == '\'' ? result[1..^1].Replace("''", "'") : result; }
    private static SyntaxNode Parse(string text, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR"));
        var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Check(errors.Length == 0, "C# syntax errors in " + path + ": " + string.Join("; ", errors.Select(error => error.ToString()))); return tree.GetRoot();
    }
    private static ClassDeclarationSyntax Type(string path, string name) => Parse(Read(path), path).DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name);
    private static string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name).ToFullString();
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static void Check(bool result, string message) { checks++; if (!result) throw new InvalidOperationException(message); }
    private static string FindRoot()
    {
        for (DirectoryInfo here = new(Environment.CurrentDirectory); here != null; here = here.Parent)
            if (File.Exists(Path.Combine(here.FullName, "Assets/Script/Client/NPCProgressionManager.cs"))) return here.FullName;
        throw new InvalidOperationException("Run Ford story checks from the Unity project root.");
    }
}
