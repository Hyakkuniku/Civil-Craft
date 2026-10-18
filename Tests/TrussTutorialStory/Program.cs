using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Contract = "Assets/BridgeBuilder/Data/Contract/CanyonCrossing/Bhan/Bhan Tutorial 2.asset";
    private const string Quiz = "Assets/BridgeBuilder/Data/Quized/TrussBridgeQuiz.asset";
    private const string Truss = "Assets/BridgeBuilder/Data/Lessons/Truss.asset";
    private const string Equilibrium = "Assets/BridgeBuilder/Data/Lessons/Equilibruim.asset";
    private static readonly string[] PhaseIds = { "Greetings", "LiveLoad", "WorkBench", "Give Contract Build Location 1", "DeadLoad", "Beam", "Build Tutorial 2", "Follow", "City", "Shopkeeper", "Go to Shopkeeper", "CollectMap", "Farewell", "Find Reed", "Delete", "Delete" };
    // Audited pre-migration first six serialized steps, canonicalized only for
    // whitespace/newline formatting. Includes messages, action gates, pointers,
    // screen/control fields and persistent events; never learned from new output.
    private static readonly string[] IntroductionHashes = {
        "DF23E25B482F9FCEADBD189199C2FEB081033E3A8BD3ABEE0E90A1EF67D52486",
        "339A5BE271525289EAD4E14CACEEAE240E16ABF4BF5E5CDE60467F56D90DF8CF",
        "16D8BE9243CFC75594FDE544C93BCE4279AAAD3E002DA3086297BC225E40B281",
        "B1E3DEC8B2634F1ADAA27134DAC3803E6FA9160C96CB22EF0B5956171E8AE76F",
        "B752F06BAC8E1850BA2473FF3700F47377C53054F980CA53B56B7F6EE28BE8C4",
        "FB2C6C56F43C7C78D0A8F50EB955FAADCD5B8FE39AF6C117778AB4DB01251BDF"
    };
    private static string root;
    private static int checks;
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static string Field(string text, string field, int indent = 2)
    {
        Match match = Regex.Match(text, @"(?m)^ {" + indent + "}" + Regex.Escape(field) + @":([^\r\n]*)");
        Check(match.Success, "Missing serialized field: " + field);
        return Scalar(match.Groups[1].Value);
    }
    private static string Scalar(string text)
    {
        string value = text.Trim();
        return value.Length >= 2 && value[0] == '\'' && value[^1] == '\'' ? value[1..^1].Replace("''", "'") : value;
    }
    private static string Guid(string path)
    {
        Match match = Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([a-f0-9]+)");
        Check(match.Success, "Missing asset GUID: " + path); return match.Groups[1].Value;
    }
    private static ClassDeclarationSyntax Class(string path, string name) => CSharpSyntaxTree.ParseText(Read(path), CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR")).GetRoot()
        .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == name);
    private static string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>()
        .First(value => value.Identifier.ValueText == name).ToFullString();

    private static void Main(string[] args)
    {
        root = FindRoot();
        bool sourceOnly = args.Contains("--source-only");
        RuntimeSource();
        Assets();
        checks += CoroutineChecks.Run(root);
        if (!sourceOnly) SavedScene();
        Console.WriteLine($"PASS: {checks} Truss Tutorial 2 {(sourceOnly ? "source/asset" : "source/asset/saved-scene")} assertions. " +
            (sourceOnly ? "Scene migration/save is deliberately not claimed. " : string.Empty) +
            "Read-only validation; no Unity scene, save, account, website, or network changed.");
    }

    private static void RuntimeSource()
    {
        var giver = Class("Assets/Script/Client/NPCContractGIver.cs", "NPCContractGiver");
        var progression = Class("Assets/Script/Client/NPCProgressionManager.cs", "NPCProgressionManager");
        var saves = Class("Assets/Script/Player/PlayerSave/PlayerDataManager.cs", "PlayerDataManager");
        var quiz = Class("Assets/Script/UI/Quizzes/QuizUIManager.cs", "QuizUIManager");
        string interaction = Method(giver, "Intract");
        Check(interaction.Contains("finishedContractDialogue") && interaction.Contains("ClaimReward();"), "Existing completed-contract conversation must precede the Collect flow.");
        string claim = Method(giver, "ClaimReward");
        Check(claim.Contains("ShowCompleteButton") && !claim.Contains("CompleteContract("), "NPC conversation requests the existing Collect quote, not an early completion/reward.");
        string complete = Method(saves, "CompleteContract");
        Check(complete.IndexOf("TrySaveGame()", StringComparison.Ordinal) < complete.IndexOf("OnContractCompleted?.Invoke", StringComparison.Ordinal), "Turn-in event must follow durable completion save.");
        Check(Method(progression, "HandleContractCompleted").Contains("AdvanceToNextContract"), "Existing contract turn-in advances to next stable phase.");
        Check(Method(progression, "HandleOfferDialogueCompleted").Contains("TryStartOrderedOptionalSequence"), "Contract-phase ordered sequence still belongs to acceptance, not turn-in.");
        string activate = Method(progression, "ActivatePhase");
        Check(activate.Contains("else if (HasOrderedOptionalSequence(phase))") &&
            activate.Contains("StartCoroutine(StartOrderedDialogueAfterArrival(phaseIndex, phase, arrivalVersion))"),
            "Contract-free ordered arrival uses the tested deferred Follow path, not an unused helper.");
        string ordered = Method(progression, "PlayOrderedOptionalStep");
        Check(ordered.Contains("outcome != QuizOutcome.Completed") && ordered.Contains("!sameStep"), "Quiz cancellation/unavailability/stale owner cannot invoke the next story step.");
        Check(ordered.Contains("AlmanacQuizAccess.TryOffer(step.quiz, context, this, onClosed)"), "Assessment keeps the asking NPC as session owner through Almanac access.");
        string finishSequence = Method(progression, "FinishOrderedOptionalSequence");
        Check(finishSequence.IndexOf("MarkPhaseDialogueCompleted", StringComparison.Ordinal) < finishSequence.IndexOf("phase.InvokeDialogueFinished", StringComparison.Ordinal), "Departure callbacks occur only after final ordered completion is recorded.");
        string finishQuiz = Method(quiz, "Finish");
        Check(finishQuiz.Contains("CommitProgress(true)") && finishQuiz.Contains("CloseSession(QuizOutcome.Completed)"), "Assessment completion must save before NPC continuation.");
        Check(!finishQuiz.Contains("AssessmentScore") && !finishQuiz.Contains("gold") && !finishQuiz.Contains("expReward"), "Assessment continuation has no score gate or additional reward.");

        var lesson = Class("Assets/Script/UI/Lessons/BuildLocationLessonTrigger.cs", "BuildLocationLessonTrigger");
        string saved = Method(lesson, "HandleBridgeSaved");
        Check(saved.Contains("completedLocation != buildLocation") && saved.Contains("buildLocation.activeContract != completedContract"), "Post-save lesson validates the exact location and matching active contract.");
        Check(Read("Assets/Script/UI/Lessons/BuildLocationLessonTrigger.cs").Contains("LevelCompleteManager.BridgeSavedAtLocation += HandleBridgeSaved"), "Post-save lesson listens to saved geometry, not merely simulation success.");
        string saveBridge = Method(Class("Assets/Script/Level/LevelCompleteManager.cs", "LevelCompleteManager"), "SaveAndBakeBridge");
        Check(saveBridge.Contains("BridgeSavedAtLocation?.Invoke"), "Authoritative bridge-save event still exists.");

        var location = Class("Assets/Script/BuildMode/BuildLocation.cs", "BuildLocation");
        string restart = Method(location, "RestartBuildTutorialAfterFailure");
        Check(restart.Contains("!restartGuidedTutorialOnFailure") && restart.IndexOf("!restartGuidedTutorialOnFailure", StringComparison.Ordinal) < restart.IndexOf("ClearBridgeForTutorialRestart", StringComparison.Ordinal), "Tutorial 2 opt-out must guard the restart entry point before deleting bridge pieces.");
        Check(Read("Assets/Script/BuildMode/BuildLocation.cs").Contains("restartGuidedTutorialOnFailure = true"), "Existing locations retain tutorial-restart behavior by default.");
        Check(Read("Assets/Script/Level/LevelFailedManager.cs").Contains("activeLocation.RestartGuidedTutorialOnFailure"), "Failure flow does not schedule a deliberate Tutorial 2 no-restart as an error.");
        var buildAuthor = Class("Assets/Editor/TrussTutorialBuildAuthoring.cs", "TrussTutorialBuildAuthoring");
        string buildSteps = Method(buildAuthor, "BuildUpdatedSteps");
        Check(buildSteps.Contains("source.Take(6).Select(CopyStep)") && buildSteps.Contains("result.AddRange(revised)"),
            "Migration must copy all six existing opening steps, including Erase and Move, before the ten retained actions.");
        Check(buildSteps.Contains("result.Insert(8, Feedback(RoadFeedback, director))") &&
            buildSteps.Contains("result.Insert(11, Feedback(FirstSectionFeedback, director))") &&
            buildSteps.Contains("result.Insert(17, Feedback(CompletionFeedback, director))"), "Feedback is inserted without replacing the retained introductions or actions.");
        string map = Method(buildAuthor, "ActionMap");
        Check(map.Contains("source.Length == 16") && map.Contains("source.Length == 19") && !map.Contains("source.Length == 13"),
            "Build migration accepts the audited old 16/new 19 formats, not the superseded tool-removing 13-step format.");
        string copier = Method(buildAuthor, "CopyStep");
        Check(copier.Contains("message = step.message") && copier.Contains("usePointer = step.usePointer") &&
            copier.Contains("pointerTarget = step.pointerTarget") && copier.Contains("requiredAction = step.requiredAction") && copier.Contains("OnStepStart = step.OnStepStart"),
            "Retained introductions preserve actual pointer, gate and callback fields, not just their labels.");
        string storyPath = Path.Combine(root, "Assets/Editor/TrussTutorialStoryAuthoring.cs");
        if (File.Exists(storyPath))
        {
            string author = File.ReadAllText(storyPath);
            Check(!Regex.IsMatch(author, @"AddComponent\s*<\s*(?:NPCSpawnerCondition|PersistentFeatureGate|\w*Barrier\w*|\w*Gate\w*)\s*>"), "Story migration must not introduce the explicitly excluded world route gate.");
        }
    }

    private static void Assets()
    {
        string contract = Read(Contract);
        Check(Field(contract, "contractID") == "TUT_CONTRACT2", "Released Tutorial 2 contract ID is unchanged.");
        Check(Field(contract, "liveLoadWeight") == "400", "Second training vehicle must weigh 400 kg.");
        Check(Field(contract, "budget") == "75000" && Field(contract, "goldReward") == "500" && Field(contract, "expReward") == "150", "Existing budget and contract rewards must remain 75000/500/150.");
        Check(Field(contract, "autoCollectReward") == "0", "Tutorial 2 must retain explicit NPC turn-in/Collect.");
        Match allowance = Regex.Match(contract, @"(?ms)^  allowedMaterials:\r?\n(?<body>.*?)(?=^  \w|\z)");
        string[] allowed = Regex.Matches(allowance.Groups["body"].Value, @"guid: ([a-f0-9]+)").Select(value => value.Groups[1].Value).ToArray();
        Check(allowed.Length == 2 && allowed.Contains("d96454d8483949a4fb084d742f9a0ac8") && allowed.Contains("d168cda963676074692e7020faf3c791"), "Only Wood Road and Wood Beam are allowed for Tutorial 2.");
        string quiz = Read(Quiz);
        Check(Field(quiz, "quizId") == "civil-craft-truss-bridge-quiz-v1" && Field(quiz, "assessmentMode") == "1", "New truss quiz has a stable independent assessment identity.");
        Match[] questions = Regex.Matches(quiz, @"(?ms)^  - questionId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - questionId: |\z)").Cast<Match>().ToArray();
        Check(questions.Length == 5 && questions.Select(value => Scalar(value.Groups["id"].Value)).Distinct().Count() == 5, "Truss quiz has five uniquely identified questions.");
        Check(questions.Select(value => int.Parse(Field(value.Groups["body"].Value, "correctChoiceIndex", 4))).SequenceEqual(new[] { 1, 2, 0, 3, 1 }), "Truss quiz correct answers are B, C, A, D, B.");
        foreach (Match question in questions)
            Check(Regex.Matches(question.Groups["body"].Value, @"(?m)^    - text: \S").Count == 4, "Every truss question retains four authored text choices.");
        Check(Field(Read(Truss), "lessonId") == "LESSON004", "Released Truss lesson discovery ID remains unchanged.");
        Check(Field(Read(Equilibrium), "lessonId") == "What is Equilibrium", "Released Equilibrium lesson discovery ID remains unchanged.");
    }

    private static void SavedScene()
    {
        string scene = Read(Canyon);
        Match[] components = Regex.Matches(scene, @"(?ms)^--- !u!114 &(?<id>-?\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)").Cast<Match>().ToArray();
        Match[] bhanMatches = components.Where(value => value.Groups["body"].Value.Contains("guid: a1f34bc1e3214d1bab7180c2f9e260bb") &&
            Regex.IsMatch(value.Groups["body"].Value, @"(?m)^  progressionSaveId: MainContractNPC\s*$")).ToArray();
        Check(bhanMatches.Length == 1, "Saved Canyon scene has one exact MainContractNPC progression.");
        string bhan = bhanMatches[0].Groups["body"].Value;
        Match[] phases = Regex.Matches(bhan, @"(?ms)^  - phaseId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - phaseId: |^  progressionSaveId:|\z)").Cast<Match>().ToArray();
        Check(phases.Select(value => Scalar(value.Groups["id"].Value)).SequenceEqual(PhaseIds), "All 16 released Bhan phase IDs/indexes remain unchanged.");
        string beam = phases[5].Groups["body"].Value, second = phases[6].Groups["body"].Value, follow = phases[7].Groups["body"].Value;
        Check(Regex.Matches(beam, @"(?m)^    - stepType: (\d+)").Select(value => int.Parse(value.Groups[1].Value)).SequenceEqual(new[] { 0, 0, 1, 0, 3, 0, 3 }),
            "Beam transition orders both conversations, Wood Beam, Truss introduction/lesson, then Equilibrium introduction/lesson.");
        Check(beam.Contains("guid: d168cda963676074692e7020faf3c791") && beam.Contains("guid: " + Guid(Truss)) && beam.Contains("guid: " + Guid(Equilibrium)),
            "Pre-contract introduction contains Wood Beam and the existing Truss/Equilibrium lessons.");
        Check(Field(second, "contract", 4).Contains(Guid(Contract)) && Field(second, "targetBuildLocation", 4).Contains("358124909"), "Tutorial 2 contract/location stay on phase index 6.");
        Check(!Regex.IsMatch(second, @"(?m)^    - stepType: 2\s*$"), "Assessment is not attached to the contract acceptance phase.");
        int[] followSteps = Regex.Matches(follow, @"(?m)^    - stepType: (\d+)").Select(value => int.Parse(value.Groups[1].Value)).ToArray();
        Check(followSteps.SequenceEqual(new[] { 0, 2, 0 }), "Follow orders turn-in follow-up dialogue, assessment, then departure dialogue.");
        Check(follow.Contains("guid: " + Guid(Quiz)) && Regex.IsMatch(follow, @"(?m)^      quizStepId: \S"), "Follow owns the new quiz with a stable step ID.");
        Check(Field(follow, "contract", 4) == "{fileID: 0}" && Field(follow, "targetLocation", 4) == "{fileID: 0}", "Follow is contract-free and remains at the completed training site until its final callback.");
        Check(Field(follow, "repeatDialogue", 4) == "1" && Field(follow, "playDialogueOnArrival", 4) == "1", "Existing Follow-phase saves can access the new assessment without rewinding City+ progress.");
        Check(Field(follow, "unlockFeatureIdAfterDialogue", 4) == "" && Field(follow, "showFeatureCollectPopup", 4) == "0", "The quiz/Follow phase grants no extra feature reward.");
        Check(follow.Contains("m_Target: {fileID: 2135021272}") && follow.Contains("m_MethodName: StartTutorial") &&
            Regex.IsMatch(follow, @"(?ms)^    onDialogueFinishedPhaseMove:\r?\n      npc: \{fileID: 650395270\}\r?\n      phaseId: City"), "Existing FollowProfessorBhan guide and City departure callbacks are retained after the sequence.");

        string lessonGuid = Guid(Equilibrium);
        Match[] postSave = components.Where(value => value.Groups["body"].Value.Contains("guid: 54670a244ce45e04fbf60aaae95760ad") &&
            Regex.IsMatch(value.Groups["body"].Value, @"(?m)^  buildLocation: \{fileID: 358124909\}")).ToArray();
        Check(postSave.Length == 1, "Exactly one post-save lesson trigger is assigned to the second training location.");
        string trigger = postSave[0].Groups["body"].Value;
        Check(Field(trigger, "m_Enabled") == "1" && Field(trigger, "listenForSavedBridge") == "1" &&
            Field(trigger, "waitForGameplayPresentation") == "1" && Field(trigger, "triggerOnlyOnce") == "1" && trigger.Contains("guid: " + lessonGuid),
            "Second-site saved bridge opens Equilibrium once, only after gameplay presentation is ready.");
        string simulationGuid = Guid("Assets/BridgeBuilder/Data/Simulation Lessons/Truss Bridge Lesson.asset");
        Check(Regex.IsMatch(scene, @"(?ms)propertyPath: simulationLesson\r?\n      value: ?\r?\n      objectReference: \{fileID: 11400000, guid: " + simulationGuid), "The second site keeps its Truss simulation lesson override.");
        Match secondInstance = Regex.Match(scene, @"(?ms)^--- !u!1001 &262623509[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)");
        Check(secondInstance.Success && Regex.IsMatch(secondInstance.Groups["body"].Value,
            @"propertyPath: restartGuidedTutorialOnFailure\r?\n      value: 0\s*\r?\n"),
            "Tutorial 2 no-restart override is serialized on the exact second training site.");
        SavedBuildInstructions(components);
    }

    private static void SavedBuildInstructions(Match[] components)
    {
        Match[] sequences = components.Where(value => value.Groups["body"].Value.Contains("guid: 61a2c41d02260a248a74764d3b3b4d29") &&
            Regex.IsMatch(value.Groups["body"].Value, @"(?m)^  lessonName: Sequence_Build2\s*$")).ToArray();
        Check(sequences.Length == 1, "Exactly one saved Tutorial 2 build sequence retains its released lesson identity.");
        string sequence = sequences[0].Groups["body"].Value;
        Match[] steps = Regex.Matches(sequence, @"(?ms)^  - message:(?<body>.*?)(?=^  - message:|^  [A-Za-z]|\z)").Cast<Match>().ToArray();
        Check(steps.Length == 19, "Saved Tutorial 2 has all six opening/tool introductions, ten actions and three feedback screens (19 total).");
        for (int index = 0; index < 6; index++)
        {
            string canonical = Regex.Replace(steps[index].Value.Trim(), @"\s+", " ");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            Check(hash == IntroductionHashes[index], "Saved original introduction/tool step is unchanged: " + index);
        }
        foreach ((int index, string name, string pointer) in new[] {
            (4, "Erase Tool", "7295718549872878977"), (5, "Move Tool", "1674851341248528657") })
        {
            string step = steps[index].Groups["body"].Value;
            Check(step.Contains(name) && Field(step, "usePointer", 4) == "1" &&
                Field(step, "pointerTarget", 4) == "{fileID: " + pointer + "}" && Field(step, "advanceOnClick", 4) == "1" &&
                Field(step, "showNextButton", 4) == "0" && Field(step, "canSkip", 4) == "0",
                "The real " + name + " introduction retains its visible pointer and tool-click advancement.");
        }
        foreach (int index in new[] { 8, 11, 17 })
        {
            string step = steps[index].Groups["body"].Value;
            Check(Field(step, "showNextButton", 4) == "1" && Field(step, "canSkip", 4) == "0" &&
                Field(step, "advanceOnClick", 4) == "0" && Field(step, "requiredAction", 4) == "0" && step.Contains("m_MethodName: LockAllUI"),
                "Inserted feedback requires Continue with editing locked: " + index);
        }
        Check(new[] { 12, 13, 14, 15, 16 }.Select(index => Field(steps[index].Groups["body"].Value, "requiredAction", 4)).SequenceEqual(new[] { "1", "2", "3", "4", "5" }),
            "Actual Select Tool, drag selection, Copy, position, Paste action gates remain in order.");
        Check(steps[18].Value.Contains("m_MethodName: UnlockSimulation") && Field(steps[18].Groups["body"].Value, "advanceOnClick", 4) == "1",
            "Simulation unlock remains after completion feedback and advances on real Play.");
    }

    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/Client/NPCProgressionManager.cs"))) return path.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
