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
    private const string Author = "Assets/Editor/RonanStoryAuthoring.cs";
    private const string Helper = "Assets/Script/Client/RonanPostContractContinuation.cs";
    private const string Contract = "Assets/BridgeBuilder/Data/Contract/TownRiver/Ronan Contract.asset";
    private const string BrooksContract = "Assets/BridgeBuilder/Data/Contract/TownRiver/Brooks Contract.asset";
    private const string Quiz = "Assets/BridgeBuilder/Data/Quized/ShearQuiz.asset";
    private const string Lesson = "Assets/BridgeBuilder/Data/Lessons/Shear.asset";
    private static readonly string[] PhaseIds = { "MeetingRonan", "ExplainSituation", "LessonIntroduction", "ProjectBriefing", "RonanReview", "Resolved" };
    // Complete saved Marsh component, canonicalized for whitespace, captured
    // before any Ronan authoring. Previous Marsh story is out of this task.
    private const string MarshFingerprint = "CB3E196F8A95822AB9591F27D601EF2DFCBF2C8ABA79A8D612FB32E9D8412BA9";
    private const string FordFingerprint = "4DF4107BDA7DC7A475FF3AD75A7ED03804887B462992D425E5B614BB207CC620";
    private const string BrooksFingerprint = "40ACC1098913E78FA677DDF4E6A23718B06A86F2FCB34F8165ECBB54EC635A0F";
    private const string RonanGateFingerprint = "D0A3A63E58F3A89511D22EEB41DD9064FBED81848754B608EA4DA747A55931C4";
    private const string ContractFingerprint = "D82CCE2724A8B09C31E3E0866CB2E59FF4C360A9BDC91915B2F2857867713F5D";
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
                Console.WriteLine($"PASS: {checks} Ronan asset/helper/runtime-boundary assertions. Authoring migration and saved-scene checks deliberately not run. " +
                    "Read-only checks; no live Unity scene, save, account, reward, website or network touched.");
                return 0;
            }
            Authoring();
            if (!sourceOnly) SavedScene();
            Console.WriteLine($"PASS: {checks} Ronan story {(sourceOnly ? "source/asset" : "source/asset/saved-scene")} assertions. " +
                (sourceOnly ? "Scene migration/save is deliberately not claimed. " : string.Empty) +
                "Read-only checks; no live Unity scene, save, account, reward, website or network touched.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    private static void Assets()
    {
        string contract = Read(Contract);
        Check(Fingerprint(contract) == ContractFingerprint, "This story migration must leave Ronan's entire authored contract unchanged, including the deliberately retained 100-metre future brief and current 2000/50 balance.");
        Check(Field(contract, "contractID") == "RonanMainContract" && Field(contract, "clientName") == "Ronan", "Ronan's existing contract identity/owner must remain unchanged.");
        foreach ((string field, string expected) in new[] { ("bridgeSpan", "100"), ("goldReward", "500"), ("expReward", "100"),
            ("liveLoadMode", "0"), ("autoCollectReward", "0"), ("winCondition", "0"), ("isTimeAttack", "0"),
            ("enforceMaxStress", "0"), ("contractMap", "1"), ("hideFromLeaderboard", "1"), ("countsTowardMapAchievements", "0") })
            Check(Field(contract, field) == expected, "Ronan contract must keep " + field + " = " + expected + ".");
        foreach (string field in new[] { "budget", "liveLoadWeight" })
        {
            bool parsed = double.TryParse(Field(contract, field), NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
            Check(parsed && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0, "Ronan " + field + " must remain a positive, user-tunable value; do not force an inferred balance.");
        }
        string[] offer = Entries(contract, "offerDialogue", "sentences", 2);
        Check(offer.Length == 4 && offer.Any(line => line.Contains("100-meter", StringComparison.OrdinalIgnoreCase)) &&
              offer.Any(line => line.Contains("elevations", StringComparison.OrdinalIgnoreCase)) && offer.Any(line => line.Contains("take the contract", StringComparison.OrdinalIgnoreCase)),
            "Ronan briefing must retain its four hundred-metre, unequal-elevation, heavy-vehicle and contract-offer lines.");
        Check(Entries(contract, "reminderDialogue", "sentences", 2).Length == 1, "Ronan active contract needs its repeatable stress/support reminder.");
        Check(Entries(contract, "finishedContractDialogue", "sentences", 2).Length == 5, "Ronan turn-in must contain the five southern-connection finale lines.");
        Check(Field(contract, "allowedMaterials") == "[]" && Field(contract, "hiddenTools") == "[]" && Field(contract, "hiddenMaterials") == "[]", "Ronan does not introduce new material/tool restrictions.");
        Check(Field(contract, "featureUnlockRewards") == "[]", "No unsupported feature/reward system should be added to Ronan.");

        string quiz = Read(Quiz);
        Check(ScriptGuid(quiz) == Guid("Assets/Script/UI/Quizzes/QuizData.cs"), "Shear quiz must use existing QuizData.");
        Check(Field(quiz, "quizId") == "civil-craft-shear-quiz-v1" && Field(quiz, "assessmentMode") == "1", "Shear assessment needs its stable identity and any-score behavior.");
        Check(Field(quiz, "title") == "Shear Quiz", "Ronan assessment title should identify Shear.");
        Match[] questions = Regex.Matches(quiz, @"(?ms)^  - questionId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - questionId: |\z)").Cast<Match>().ToArray();
        Check(questions.Length == 5 && questions.Select(question => Scalar(question.Groups["id"].Value)).Distinct(StringComparer.Ordinal).Count() == 5, "The Shear quiz needs five uniquely identified questions.");
        Check(questions.Select(question => int.Parse(Field(question.Groups["body"].Value, "correctChoiceIndex", 4), CultureInfo.InvariantCulture)).SequenceEqual(new[] { 1, 2, 0, 3, 1 }), "Shear answers must be B, C, A, D, B.");
        foreach (Match question in questions)
        {
            Check(!string.IsNullOrWhiteSpace(Scalar(question.Groups["id"].Value)) && !string.IsNullOrWhiteSpace(Field(question.Groups["body"].Value, "prompt", 4)), "Each Shear question needs stable nonempty identity and prompt.");
            Check(Regex.Matches(question.Groups["body"].Value, @"(?m)^    - text: \S").Count == 4, "Each Shear question must have four text choices.");
        }

        string lesson = Read(Lesson);
        Check(ScriptGuid(lesson) == Guid("Assets/Script/UI/Lessons/LessonData.cs"), "Shear must use the current lesson popup/Almanac reader.");
        Check(Field(lesson, "lessonId") == "civil-craft-shear-lesson-v1" && Field(lesson, "lessonTitle") == "Shear", "Shear lesson needs a stable independent identity/title.");
        Check(!string.IsNullOrWhiteSpace(Field(lesson, "popupDescription")) && !string.IsNullOrWhiteSpace(Field(lesson, "lessonDescription")), "Shear needs short introduction and detailed Almanac text.");
        foreach (string path in Directory.GetFiles(Path.Combine(root, "Assets/BridgeBuilder/Data/Lessons"), "*.asset"))
            if (!Path.GetFullPath(path).Equals(Path.GetFullPath(Path.Combine(root, Lesson)), StringComparison.OrdinalIgnoreCase))
                Check(OptionalField(File.ReadAllText(path), "lessonId") != Field(lesson, "lessonId"), "Shear must not reuse an existing lesson save ID.");
    }

    private static void Authoring()
    {
        string source = Read(Author);
        SyntaxNode syntax = Parse(source, Author);
        foreach (string guard in new[] { "scene.isDirty", "EditorApplication.isPlayingOrWillChangePlaymode", "EditorApplication.isCompiling", "EditorApplication.isUpdating",
            "PrefabStageUtility.GetCurrentPrefabStage", "Undo.RevertAllDownToGroup", "Undo.RecordObject", "File.Copy", "EditorSceneManager.MarkSceneDirty" })
            Check(source.Contains(guard, StringComparison.Ordinal), "Ronan authoring must keep saved-scene safety guard: " + guard);
        foreach (string identity in PhaseIds.Concat(new[] { "RonanNpc", "BrooksNpc" }))
            Check(source.Contains("\"" + identity + "\"", StringComparison.Ordinal), "Ronan authoring must target stable identity: " + identity);
        var calls = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(call => call.Expression.ToString()).ToArray();
        Check(calls.Count(call => call == "EditorJsonUtility.ToJson") >= 2 && !calls.Any(call => call == "JsonUtility.ToJson" || call == "UnityEngine.JsonUtility.ToJson"), "Native Camera/Transform snapshots/comparison must use EditorJsonUtility, not JsonUtility.");
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:SaveScene|SaveScenes|SaveOpenScenes|SaveAssets|SaveAssetIfDirty|OpenScene|CloseScene|NewScene)$")), "Ronan authoring must not auto-save or switch scenes/assets.");
        Check(!calls.Any(call => Regex.IsMatch(call, @"(?:^|\.)(?:CompleteContract|SaveGame|UnlockMaterialForContract|UnlockFeature)$")), "Editor authoring must not mutate live player progression/rewards.");
        Check(!source.Contains("AddComponent<LiveLoadVehicle>", StringComparison.Ordinal) && !source.Contains("AddComponent<BoatBridgeCrossing>", StringComparison.Ordinal) &&
            !source.Contains("AddComponent<Almanac", StringComparison.Ordinal) && !source.Contains("DestroyImmediate", StringComparison.Ordinal), "Ronan story must not replace simulations, existing Almanac UI, or authored geometry.");
        Check(!Regex.IsMatch(source, @"AddComponent\s*<\s*Camera\s*>"), "Ronan cinematic must use the existing gameplay camera, not create another world/map camera.");
        Check(!source.Contains("UnpackPrefabInstance", StringComparison.Ordinal) && !source.Contains("AddComponent<NPCSpawnerCondition>", StringComparison.Ordinal), "Ronan must remain his original packed prefab and keep his existing Brooks gate; no next-NPC/region gate is introduced.");
        string apply = Method(Type(Author, "RonanStoryAuthoring"), "ApplySavedStory");
        Check(Regex.IsMatch(apply, @"Phases\(\w+\)\.Count != 0") && apply.Contains("ValidateLoaded();", StringComparison.Ordinal),
            "Repeated Ronan authoring must validate the existing story without regenerating custom camera poses or scene edits.");
        Check(calls.Any(call => call.EndsWith("RecordPrefabInstancePropertyModifications", StringComparison.Ordinal)),
            "Ronan's inherited prefab progression changes must be explicitly recorded for durable scene serialization.");
        string phaseBuilder = Method(Type(Author, "RonanStoryAuthoring"), "Phase");
        Check(phaseBuilder.Contains("unlockFeatureIdAfterDialogue = string.Empty", StringComparison.Ordinal) &&
            phaseBuilder.Contains("showFeatureCollectPopup = false", StringComparison.Ordinal),
            "Every newly authored Ronan phase must explicitly clear the default feature reward before Unity serializes empty/zero values.");
        foreach (AssignmentExpressionSyntax assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            string left = assignment.Left.ToString();
            Check(!Regex.IsMatch(left, @"\.(?:budget|liveLoadWeight|goldReward|expReward)\s*$"), "Authoring must not overwrite user-owned contract balance/rewards: " + left);
        }
    }

    private static void ExistingRuntimeGuards()
    {
        var npc = Type("Assets/Script/Client/NPCProgressionManager.cs", "NPCProgressionManager");
        Check(Method(npc, "HandleContractCompleted").Contains("AdvanceToNextContract", StringComparison.Ordinal), "Ronan review must use existing collection-driven phase continuation.");
        string ordered = Method(npc, "PlayOrderedOptionalStep");
        Check(ordered.Contains("outcome != QuizOutcome.Completed", StringComparison.Ordinal) && ordered.Contains("!sameStep", StringComparison.Ordinal), "Cancelled/unavailable/stale assessment callbacks must not advance Ronan.");
        Check(ordered.Contains("AlmanacQuizAccess.TryOffer", StringComparison.Ordinal) && ordered.Contains("TryShowLesson", StringComparison.Ordinal), "Ronan must use existing Almanac assessment and lesson systems.");
        string arrival = Method(npc, "StartOrderedDialogueAfterArrival");
        Check(arrival.Contains("HasOpenPanel", StringComparison.Ordinal) && arrival.Contains("IsTransitioning", StringComparison.Ordinal) && arrival.Contains("Time.timeScale > 0f", StringComparison.Ordinal), "Post-turn-in presentation must wait for panel/Pause/dialogue/transition restoration.");
        string completion = Method(Type("Assets/Script/Player/PlayerSave/PlayerDataManager.cs", "PlayerDataManager"), "CompleteContract");
        Check(completion.IndexOf("TrySaveGame()", StringComparison.Ordinal) < completion.IndexOf("OnContractCompleted?.Invoke", StringComparison.Ordinal), "Ronan progression must not advance before durable collection.");
    }

    private static void LegacyContinuation()
    {
        string helper = Read(Helper);
        Parse(helper, Helper);
        Check(helper.Contains("\"RonanNpc\"", StringComparison.Ordinal) && helper.Contains("\"RonanReview\"", StringComparison.Ordinal), "Ronan's legacy continuation must be scoped to its own stable NPC/review identities.");
        Check(!helper.Contains("CompleteContract(", StringComparison.Ordinal) && !helper.Contains("SaveGame(", StringComparison.Ordinal) && !helper.Contains("UnlockFeature(", StringComparison.Ordinal), "Legacy helper may resume a completed review, never grant rewards/completion.");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var trees = new[] { CSharpSyntaxTree.ParseText(helper), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LegacyBoundary.cs.txt"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LegacyScenarios.cs.txt"))) };
        var compilation = CSharpCompilation.Create("RonanLegacyChecks_" + System.Guid.NewGuid().ToString("N"), trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        Check(result.Success, "Ronan legacy helper production source isolated compilation: " + string.Join("; ", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        try { checks += (int)Assembly.Load(output.ToArray()).GetType("LegacyScenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Ronan legacy continuation scenario: " + exception.InnerException?.Message, exception.InnerException); }
    }

    private static void SavedScene()
    {
        CheckSavedRonanPrefab();
    }

    private static void CheckSavedRonanPrefab()
    {
        string scene = Read(Scene);
        Match[] all = Regex.Matches(scene, @"(?ms)^--- !u!(?<type>\d+) &(?<id>-?\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)").Cast<Match>().ToArray();
        Match[] scripts = all.Where(block => block.Groups["type"].Value == "114").ToArray();
        string npcScript = Guid("Assets/Script/Client/NPCProgressionManager.cs");
        foreach ((string id, string expected) in new[] { ("MarshNPC", MarshFingerprint), ("FordNPC", FordFingerprint), ("BrooksNpc", BrooksFingerprint) })
        {
            Match prior = scripts.Single(block => block.Groups["body"].Value.Contains("guid: " + npcScript, StringComparison.Ordinal) && OptionalField(block.Groups["body"].Value, "progressionSaveId") == id);
            Check(Fingerprint(id == "MarshNPC" ? NormalizeAuthorizedMarshPier(prior.Value) : prior.Value) == expected,
                "Ronan migration must preserve previous " + id + " story content, allowing only the later authorized Marsh Concrete Pier reference/name.");
        }
        Match gate = scripts.Single(block => block.Groups["id"].Value == "1671760480");
        Check(Fingerprint(gate.Value) == RonanGateFingerprint, "Ronan's exact existing Brooks completion/review gate must remain unchanged.");
        Check(Field(gate.Groups["body"].Value, "m_GameObject") == "{fileID: 1671760475}" && Field(gate.Groups["body"].Value, "npcRoot") == "{fileID: 1671760475}", "Ronan gate must continue controlling his original prefab root.");
        Match instance = all.Single(block => block.Groups["type"].Value == "1001" && block.Groups["id"].Value == "1186642534");
        string inherited = Read("Assets/Elements/Characters/NPCS/NPC_TEMPLATE.prefab");
        Match inheritedManager = Regex.Match(inherited, @"(?ms)^--- !u!114 &7436711577060982690[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)");
        Check(inheritedManager.Success && Field(inheritedManager.Groups["body"].Value, "phases") == "[]",
            "Known zero/empty defaults may be resolved only for new array elements of the unchanged empty source prefab, never over inherited story content.");
        var overrides = ReadOverrides(instance.Groups["body"].Value, "7436711577060982690");
        string V(string path, string optionalDefault = null)
        {
            if (overrides.TryGetValue(path, out OverrideValue value)) return value.Value;
            Check(optionalDefault != null, "Missing durable Ronan prefab value override: " + path); return optionalDefault;
        }
        string R(string path, string optionalDefault = null)
        {
            if (overrides.TryGetValue(path, out OverrideValue value)) return value.Reference;
            Check(optionalDefault != null, "Missing durable Ronan prefab reference override: " + path); return optionalDefault;
        }
        const string sourcePrefabGuid = "b82f06dcbe3da674e95e69a64637a54c";
        Match[] localManagers = scripts.Where(block => block.Groups["body"].Value.Contains("m_CorrespondingSourceObject: {fileID: 7436711577060982690, guid: " + sourcePrefabGuid, StringComparison.Ordinal) &&
            OptionalField(block.Groups["body"].Value, "m_PrefabInstance") == "{fileID: 1186642534}").ToArray();
        Check(localManagers.Length == 1, "Ronan's inherited manager must retain one unambiguous local prefab reference for callbacks.");
        string managerReference = "{fileID: " + localManagers[0].Groups["id"].Value + "}";
        Check(V("progressionSaveId") == "RonanNpc" && V("phases.Array.size") == "6", "The original Ronan prefab must durably serialize his stable ID and all six phases.");
        string P(int index) => "phases.Array.data[" + index + "]";
        for (int i = 0; i < PhaseIds.Length; i++)
        {
            string prefix = P(i);
            Check(V(prefix + ".phaseId") == PhaseIds[i], "Ronan phase order/identity must match " + PhaseIds[i] + ".");
            Check(i == 3 ? AssetReferenceGuid(R(prefix + ".contract")) == Guid(Contract) : R(prefix + ".contract", "{fileID: 0}") == "{fileID: 0}", "Only ProjectBriefing may own Ronan's contract.");
            // New prefab-array elements omit serialized empty strings, zeroes
            // and null references. Resolve only these known zero defaults after
            // proving the original source array was empty; substantive IDs,
            // references, true flags and array sizes remain mandatory overrides.
            Check(V(prefix + ".unlockFeatureIdAfterDialogue", string.Empty) == string.Empty && V(prefix + ".showFeatureCollectPopup", "0") == "0",
                "Ronan phases must explicitly clear the constructor's default feature reward, not invent a region unlock.");
            Check(R(prefix + ".cinematicAfterOffer", "{fileID: 0}") == "{fileID: 0}", "Situation cinematic must not move to post-acceptance timing.");
        }
        Check(R(P(3) + ".targetBuildLocation") == "{fileID: 1517540892}", "Ronan must retain his existing southern workbench.");
        string site = R(P(1) + ".targetLocation");
        Check(R(P(0) + ".targetLocation", "{fileID: 0}") == "{fileID: 0}" && Enumerable.Range(1, 5).All(i => R(P(i) + ".targetLocation") == site), "Ronan starts at his authored spawn and keeps one destination marker for the remaining story.");
        foreach ((int index, string next) in new[] { (0, "ExplainSituation"), (2, "ProjectBriefing"), (4, "Resolved") })
            Check(R(P(index) + ".onDialogueFinishedPhaseMove.npc") == managerReference && V(P(index) + ".onDialogueFinishedPhaseMove.phaseId") == next,
                "Ronan phase completion must continue to its own " + next + " phase.");
        string Intro(int step) => P(2) + ".optionalDialogueSequence.Array.data[" + step + "]";
        Check(V(P(2) + ".optionalDialogueSequence.Array.size") == "3" && Enumerable.Range(0, 3).Select(i => V(Intro(i) + ".stepType", "0")).SequenceEqual(new[] { "0", "0", "3" }) &&
            AssetReferenceGuid(R(Intro(2) + ".lesson")) == Guid(Lesson), "Ronan learning must preserve situation dialogue, Shear dialogue, then the actual lesson popup.");
        string Review(int step) => P(4) + ".optionalDialogueSequence.Array.data[" + step + "]";
        Check(V(P(4) + ".optionalDialogueSequence.Array.size") == "3" && Enumerable.Range(0, 3).Select(i => V(Review(i) + ".stepType", "0")).SequenceEqual(new[] { "3", "0", "2" }), "Post-Collect review must order lesson, invitation dialogue, then assessment.");
        Check(AssetReferenceGuid(R(Review(0) + ".lesson")) == Guid(Lesson) && AssetReferenceGuid(R(Review(2) + ".quiz")) == Guid(Quiz) && V(Review(2) + ".quizStepId") == "ronan-shear-assessment-v1", "The review must use Shear content with one stable assessment identity.");
        Check(V(P(4) + ".playDialogueOnArrival") == "1" && V(P(5) + ".playDialogueOnArrival", "0") == "0" && V(P(5) + ".showExclamationMark", "0") == "0" && V(P(5) + ".repeatDialogue") == "1", "Ronan review follows collection; resolved finale remains quiet, marker-free and repeatable.");

        Match[] helpers = scripts.Where(block => block.Groups["body"].Value.Contains("guid: " + Guid(Helper), StringComparison.Ordinal) && OptionalField(block.Groups["body"].Value, "m_GameObject") == "{fileID: 1671760475}").ToArray();
        Check(helpers.Length == 1, "Ronan needs one scoped continuation helper on his existing prefab root.");
        string helperReference = "{fileID: " + helpers[0].Groups["id"].Value + "}";
        string helper = helpers[0].Groups["body"].Value;
        Check(Field(helper, "npc") == managerReference && AssetReferenceGuid(Field(helper, "contract")) == Guid(Contract) && Field(helper, "reviewPhaseId") == "RonanReview", "Ronan continuation must retain exact NPC, contract and review ownership.");

        string Call(int phase, string eventName, string method)
        {
            string prefix = P(phase) + "." + eventName + ".m_PersistentCalls.m_Calls";
            Check(V(prefix + ".Array.size") == "1" && V(prefix + ".Array.data[0].m_MethodName") == method && V(prefix + ".Array.data[0].m_CallState") != "0", "Ronan phase event must contain one enabled " + method + " callback.");
            return R(prefix + ".Array.data[0].m_Target");
        }
        string movieReference = Call(1, "onDialogueFinished", "PlayCinematic");
        string movie = scripts.Single(block => block.Groups["id"].Value == RefId(movieReference)).Groups["body"].Value;
        Check(ScriptGuid(movie) == Guid("Assets/Script/Scene/CinematicDirector.cs") && Field(movie, "cinematicID") == "RonanSouthernConnection-v1" && Field(movie, "playOnlyOnce") == "0" && Field(movie, "playOnStart") == "0", "Ronan must explicitly request his own retryable southern-connection tour.");
        string template = scripts.Single(block => block.Groups["id"].Value == "1510433738").Groups["body"].Value;
        foreach (string field in new[] { "cinematicCamera", "playerActor", "playerAnimator" }) Check(Field(movie, field) == Field(template, field) && Field(movie, field) != "{fileID: 0}", "Ronan tour must reuse existing camera/actor references: " + field);
        Check(AssetReferenceGuid(Field(movie, "contractToDisplay")) == Guid(Contract), "Ronan tour must display only his contract.");
        string callback = Section(movie, "OnCinematicFinished", 2);
        Check(callback.Contains("m_Target: " + helperReference, StringComparison.Ordinal) && callback.Contains("m_MethodName: ContinueAfterSituationCinematic", StringComparison.Ordinal) && Regex.Matches(callback, "m_Target:").Count == 1,
            "Cinematic completion must use Ronan's guarded owner/phase callback, never an unconditional stale phase move.");
        string guideReference = Call(0, "onDialogueFinished", "StartTutorial");
        Check(Call(1, "onNPCInteracted", "Skip") == guideReference, "Talking at the site must safely dismiss the same follow guide started at the meeting.");
        string guide = scripts.Single(block => block.Groups["id"].Value == RefId(guideReference)).Groups["body"].Value;
        Check(Field(guide, "lessonName") == "Story/Ronan/FollowToSite-v1" && Field(guide, "playOnStart") == "0" && Field(guide, "nextSequence") == "{fileID: 0}" && Field(guide, "autoStartNextSequence") == "0", "Ronan follow guidance must remain a standalone explicitly triggered existing TutorialSequence.");
        Check(Regex.Matches(guide, @"(?m)^  - message:").Count == 1 && Field(guide, "canSkip", 4) == "1" && Field(guide, "showNextButton", 4) == "0" && guide.Contains("target: " + site, StringComparison.Ordinal) && guide.Contains("advancesTutorial: 1", StringComparison.Ordinal), "Ronan follow guide must be skippable and finish at the exact site waypoint.");
        CheckNewMarkers(all, R("authoredSpawnLocation"), site, movie);
        foreach (Match archive in scripts.Where(block => block.Groups["body"].Value.Contains("guid: " + Guid("Assets/Script/UI/Almanac/AlmanacLessonTab.cs"), StringComparison.Ordinal)))
        {
            string list = Section(archive.Groups["body"].Value, "allLessons", 2);
            Check(Regex.Matches(list, "guid: " + Guid(Lesson)).Count == 1, "Shear must be registered exactly once in the actual serialized Almanac list.");
        }
        foreach ((string id, string expected) in new[] { ("1792442206", "1B6D031127DDE079756BBD4ED48DD11854D33DBA396BBA16DF01A9B359100DFE"),
            ("1511747791", "918B95CC68608990662B35259FBAB16173AFC2237911ED99D299548A513E2E5F"),
            ("2062136823", "BD24B93810D77FEC08E6D1B683AE36A889424D51A90CBED85A47F491ECDA2CD3") })
            Check(Fingerprint(all.Single(block => block.Groups["type"].Value == "4" && block.Groups["id"].Value == id).Value) == expected,
                "The current physical 70-metre level site must not be resized/raised/renamed by story authoring: transform " + id + ".");
        Match car = all.Single(block => block.Groups["type"].Value == "1001" && block.Groups["id"].Value == "746010395");
        string normalizedCar = Regex.Replace(car.Value,
            @"(?ms)^    - target: \{fileID: 1369226356499901863,.*?(?=^    - target: |^    m_RemovedComponents:|\z)",
            entry => OptionalField(entry.Value, "propertyPath", 6) == "assignedContract"
                ? Regex.Replace(entry.Value, @"(?m)(^      objectReference: \{fileID: 11400000, guid: )[a-f0-9]{32}", "$1<live-load-contract>")
                : entry.Value);
        Check(Fingerprint(normalizedCar) == "0CEE963AE1374B4350D51606A2CA7F075E31AB5A5B5406F596A2DBA8EFC6312C",
            "Ronan's existing vehicle prefab, cargo, physics and route must remain unchanged outside the explicitly permitted contract reference correction.");
        foreach (string sourceId in new[] { "1369226356499901863", "3922368601465291137" })
        {
            var assignment = ReadOverrides(car.Groups["body"].Value, sourceId);
            Check(assignment.TryGetValue("assignedContract", out OverrideValue assigned) && AssetReferenceGuid(assigned.Reference) == Guid(Contract),
                "The exact existing RonanCar live-load/finish components must both refer to Ronan's own contract after native diagnosis: source " + sourceId + ".");
        }
    }

    private sealed record OverrideValue(string Value, string Reference);
    private static Dictionary<string, OverrideValue> ReadOverrides(string prefab, string sourceComponentId)
    {
        var values = new Dictionary<string, OverrideValue>(StringComparer.Ordinal);
        Match[] entries = Regex.Matches(prefab, @"(?ms)^    - target: \{fileID: (?<id>-?\d+),.*?(?=^    - target: |^    m_RemovedComponents:|\z)").Cast<Match>().ToArray();
        foreach (Match entry in entries.Where(entry => entry.Groups["id"].Value == sourceComponentId))
        {
            string path = Field(entry.Value, "propertyPath", 6);
            Match value = Regex.Match(entry.Value, @"(?ms)^      value: (?<body>.*?)(?=^      objectReference:)");
            Match reference = Regex.Match(entry.Value, @"(?ms)^      objectReference: (?<body>\{.*?\})");
            Check(value.Success && reference.Success && !values.ContainsKey(path), "Ronan prefab override must have one complete, unambiguous value/reference for " + path + ".");
            values.Add(path, new OverrideValue(Scalar(value.Groups["body"].Value.Trim()), Regex.Replace(reference.Groups["body"].Value, @"\s+", " ")));
        }
        Check(values.Count > 0, "No durable overrides were found for Ronan's inherited progression component.");
        return values;
    }
    private static string Section(string body, string field, int indent)
    {
        Match section = Regex.Match(body, @"(?ms)^ {" + indent + "}" + Regex.Escape(field) + @":\r?\n(?<body>.*?)(?=^ {" + indent + @"}[a-zA-Z]|\z)");
        Check(section.Success, "Missing serialized section: " + field); return section.Groups["body"].Value;
    }
    private static string RefId(string reference)
    {
        Match match = Regex.Match(reference, @"fileID: (-?\d+)"); Check(match.Success, "Invalid local Unity reference: " + reference); return match.Groups[1].Value;
    }
    private static void CheckNewMarkers(Match[] all, string spawn, string site, string movie)
    {
        Match[] transforms = all.Where(block => block.Groups["type"].Value == "4").ToArray();
        Match story = all.Single(block => block.Groups["type"].Value == "1" && OptionalField(block.Groups["body"].Value, "m_Name") == "Ronan Story");
        var componentIds = Regex.Matches(story.Groups["body"].Value, @"(?m)^  - component: \{fileID: (-?\d+)\}").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Match rootTransform = transforms.Single(block => componentIds.Contains(block.Groups["id"].Value));
        Check(Field(rootTransform.Groups["body"].Value, "m_Father") == "{fileID: 0}" && Field(story.Groups["body"].Value, "m_IsActive") == "1", "Ronan Story must be one independent active root, not under the gated NPC.");
        bool UnderRoot(string reference)
        {
            string id = RefId(reference); var visited = new HashSet<string>(StringComparer.Ordinal);
            while (id != "0" && visited.Add(id))
            {
                Match transform = transforms.Single(block => block.Groups["id"].Value == id);
                if (RefId(Field(transform.Groups["body"].Value, "m_GameObject")) == story.Groups["id"].Value) return true;
                id = RefId(Field(transform.Groups["body"].Value, "m_Father"));
            }
            return false;
        }
        Check(spawn != site && UnderRoot(spawn) && UnderRoot(site), "Ronan needs separate authored spawn/site markers under his independent helper root.");
        Match[] shots = Regex.Matches(movie, @"(?ms)^  - shotName: (?<name>[^\r\n]*)\r?\n(?<body>.*?)(?=^  - shotName: |^  [a-zA-Z]|\z)").Cast<Match>().ToArray();
        Check(shots.Length == 3, "Ronan's existing southern site must use three authored cinematic shots.");
        foreach (Match shot in shots)
        {
            string body = shot.Groups["body"].Value;
            Check(double.Parse(Field(body, "duration", 4), CultureInfo.InvariantCulture) > 0 && UnderRoot(Field(body, "cameraStartPoint", 4)) && UnderRoot(Field(body, "cameraEndPoint", 4)), "Each cinematic shot must animate real Ronan-owned markers for a positive duration.");
            Check(Field(body, "playerStartPoint", 4) == "{fileID: 0}" && Field(body, "playerWalkTarget", 4) == "{fileID: 0}" && Field(body, "playWalkAnimation", 4) == "0", "The tour may not reposition or walk the player.");
        }
    }

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Regex.Replace(value, @"\s+", " ").Trim())));
    private static string NormalizeAuthorizedMarshPier(string value)
    {
        // The existing baseline remains unchanged. Normalize only the exact
        // later user-approved material popup and its highlighted spoken name.
        const string concrete = "7fbcf5a7e459d434f970d2c976504d5c", wood = "b98aa9ba752ac28449feebc76f256bf2";
        int count = Regex.Matches(value, Regex.Escape(concrete)).Count;
        if (count == 0) return value;
        const string label = @"<b><#E09500>Concrete\s+Pier</color></b>";
        Check(count == 1 && Regex.Matches(value, label).Count == 1, "Only one Marsh material popup and one spoken label may change to Concrete Pier.");
        return Regex.Replace(value.Replace(concrete, wood, StringComparison.Ordinal), label, "<b><#E09500>Pier</color></b>");
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
        throw new InvalidOperationException("Run Ronan story checks from the Unity project root.");
    }
}
