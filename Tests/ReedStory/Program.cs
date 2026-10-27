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
    private const string Author = "Assets/Editor/ReedStoryAuthoring.cs";
    private const string Helper = "Assets/Script/Client/ReedStoryIntroduction.cs";
    private const string MainContract = "Assets/BridgeBuilder/Data/Contract/CanyonCrossing/Reed/Reed's Contract.asset";
    private const string SideContract = "Assets/BridgeBuilder/Data/Contract/CanyonCrossing/Reed/Reed Side Inspection.asset";
    private const string Prefab = "Assets/Elements/Characters/Prefabs/Reed.prefab";
    private const string Lesson = "Assets/BridgeBuilder/Data/Lessons/Bending.asset";
    private const string Quiz = "Assets/BridgeBuilder/Data/Quized/BendingQuiz.asset";
    private const string InstanceId = "1110249782773445749";
    private static readonly string[] PhaseIds = { "MeetingReed", "ExplainSituation", "Reed Contract Give", "ReedFareWell", "SideContract" };
    private static string root;
    private static int checks;
    private static Baseline baseline;
    private sealed class Baseline
    {
        public string SideContractSHA256 { get; set; }
        public string SharedPrefabSHA256 { get; set; }
        public Dictionary<string, string> SidePhaseOverrides { get; set; }
        public Dictionary<string, string> OtherReedInstances { get; set; }
        public Dictionary<string, string> SharedRuntimeSources { get; set; }
    }

    private static int Main(string[] args)
    {
        try
        {
            root = FindRoot();
            Check(args.All(argument => argument == "--source-only"), "Only --source-only is supported.");
            bool sourceOnly = args.Contains("--source-only");
            baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline.json")));
            Assets(); Authoring(); RuntimeBoundaries();
            if (!sourceOnly) SavedScene();
            Console.WriteLine($"PASS: {checks} Reed {(sourceOnly ? "asset/source" : "asset/source/saved-scene")} assertions. " +
                (sourceOnly ? "Scene apply/save is deliberately not claimed. " : "") + "Read-only checks; player saves, rewards and network untouched.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }

    private static void Assets()
    {
        Check(Hash(ReadBytes(SideContract)) == baseline.SideContractSHA256, "Existing Reed Side Inspection asset must remain byte-for-byte unchanged.");
        Check(Hash(ReadBytes(Prefab)) == baseline.SharedPrefabSHA256, "Shared Reed prefab must remain byte-for-byte unchanged.");
        foreach(var entry in baseline.SharedRuntimeSources)
            Check(Hash(ReadBytes(entry.Key))==entry.Value,"No changes to existing shared progression/dialogue logic: "+entry.Key);
        string contract = Read(MainContract);
        foreach (var entry in new Dictionary<string, string> { ["contractID"]="ReedsContract", ["clientName"]="Reed", ["budget"]="100000",
            ["liveLoadWeight"]="5000", ["bridgeSpan"]="60", ["goldReward"]="500", ["expReward"]="100", ["liveLoadMode"]="0",
            ["contractMap"]="0", ["isStoryModeContract"]="1", ["autoCollectReward"]="0", ["allowedMaterials"]="[]", ["featureUnlockRewards"]="[]" })
            Check(Field(contract, entry.Key) == entry.Value, "Preserve Reed's existing contract field " + entry.Key);
        string lesson = Read(Lesson), quiz = Read(Quiz);
        Check(ReferenceGuid(Field(lesson,"m_Script")) == Guid("Assets/Script/UI/Lessons/LessonData.cs"), "Bending must use existing LessonData.");
        Check(Field(lesson,"lessonId") == "civil-craft-bending-lesson-v1" && Field(lesson,"lessonTitle") == "Bending", "Independent stable Bending lesson identity.");
        Check(Field(lesson,"popupDescription").Contains("simply supported") && Field(lesson,"lessonDescription").Contains("upper part experiences compression") &&
            Field(lesson,"lessonDescription").Contains("lower part experiences tension"), "Bending explanation must include its support/load qualifications and compression/tension behavior.");
        Check(Field(lesson,"lessonImage") == "{fileID: 0}" && Field(lesson,"almanacImage") == "{fileID: 0}", "Do not invent a lesson image.");
        Check(ReferenceGuid(Field(quiz,"m_Script")) == Guid("Assets/Script/UI/Quizzes/QuizData.cs"), "Bending uses current shared quiz system.");
        Check(Field(quiz,"quizId") == "civil-craft-bending-quiz-v1" && Field(quiz,"assessmentMode") == "1", "Stable any-score assessment identity.");
        Match[] questions = Questions(quiz);
        Check(questions.Length == 5 && questions.Select(q => q.Groups["id"].Value).Distinct().Count() == 5, "Five uniquely identified reference questions.");
        Check(questions.Select(q => int.Parse(Field(q.Groups["body"].Value,"correctChoiceIndex",4))).SequenceEqual(new[]{2,0,3,1,2}), "Reference answer key remains C, A, D, B, C.");
        foreach (Match question in questions)
        {
            string body = question.Groups["body"].Value;
            Check(Choices(body).Length == 4, "Each question has four choices.");
            Check(Field(body,"prompt",4).Length > 10 && Field(body,"feedback",4).Length > 10, "Each question has complete prompt and review explanation.");
        }
        Check(!Regex.IsMatch(lesson + quiz, @"\b(?:750|5000|5,000)\b"), "Lesson and quiz must not introduce numeric load changes.");
        ValidateProductionQuiz(quiz);
        foreach (string path in Directory.GetFiles(Path.Combine(root,"Assets/BridgeBuilder/Data/Lessons"),"*.asset"))
            if (!Path.GetFullPath(path).Equals(Path.GetFullPath(Path.Combine(root,Lesson)),StringComparison.OrdinalIgnoreCase))
                Check(OptionalField(File.ReadAllText(path),"lessonId") != Field(lesson,"lessonId"), "Bending lesson save ID must be unique.");
    }

    private static void ValidateProductionQuiz(string asset)
    {
        const string boundary = "namespace UnityEngine { public class ScriptableObject {} public class Sprite {} public sealed class TextAreaAttribute : System.Attribute { public TextAreaAttribute(int a,int b){} } public sealed class TooltipAttribute : System.Attribute { public TooltipAttribute(string s){} } public sealed class HideInInspector : System.Attribute {} public sealed class CreateAssetMenuAttribute : System.Attribute { public string fileName; public string menuName; } }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ReedQuizChecks_"+System.Guid.NewGuid().ToString("N"),
            new[]{CSharpSyntaxTree.ParseText(Read("Assets/Script/UI/Quizzes/QuizData.cs")),CSharpSyntaxTree.ParseText(boundary)}, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new(); var result = compilation.Emit(output);
        Check(result.Success,"Production QuizData isolated compilation: "+string.Join("; ",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        Assembly assembly = Assembly.Load(output.ToArray()); Type quizType=assembly.GetType("QuizData"), questionType=assembly.GetType("QuizQuestion"), choiceType=assembly.GetType("QuizChoice");
        object quiz=Activator.CreateInstance(quizType);
        quizType.GetField("quizId").SetValue(quiz,Field(asset,"quizId")); quizType.GetField("title").SetValue(quiz,Field(asset,"title")); quizType.GetField("assessmentMode").SetValue(quiz,true);
        var list=(System.Collections.IList)quizType.GetField("questions").GetValue(quiz);
        foreach(Match serialized in Questions(asset))
        {
            string body=serialized.Groups["body"].Value; object question=Activator.CreateInstance(questionType);
            questionType.GetField("questionId").SetValue(question,serialized.Groups["id"].Value); questionType.GetField("prompt").SetValue(question,Field(body,"prompt",4));
            questionType.GetField("feedback").SetValue(question,Field(body,"feedback",4)); questionType.GetField("correctChoiceIndex").SetValue(question,int.Parse(Field(body,"correctChoiceIndex",4)));
            var choices=(System.Collections.IList)questionType.GetField("choices").GetValue(question);
            foreach(string text in Choices(body)) {object choice=Activator.CreateInstance(choiceType);choiceType.GetField("text").SetValue(choice,text);choices.Add(choice);}
            list.Add(question);
        }
        object[] parameters={null};
        Check((bool)quizType.GetMethod("TryValidate").Invoke(quiz,parameters),"Actual QuizData.TryValidate accepts serialized Bending assessment: "+parameters[0]);
        Check(((string)quizType.GetProperty("ContentFingerprint").GetValue(quiz)).Length==64,"Production quiz fingerprint must be usable for saved attempts.");
        questionType.GetField("correctChoiceIndex").SetValue(list[0],4);
        Check(!(bool)quizType.GetMethod("TryValidate").Invoke(quiz,parameters),"Production validation must reject a mismatched answer index.");
    }

    private static void Authoring()
    {
        string source=Read(Author); SyntaxNode syntax=Parse(source,Author);
        var phases=syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(v=>v.Identifier.ValueText=="PhaseIds");
        Check(phases.Initializer.Value.DescendantNodes().OfType<LiteralExpressionSyntax>().Select(l=>l.Token.ValueText).SequenceEqual(PhaseIds),"All five existing phase IDs and indexes remain unchanged.");
        foreach(string guard in new[]{"EditorApplication.isPlayingOrWillChangePlaymode","EditorApplication.isCompiling","EditorApplication.isUpdating","PrefabStageUtility.GetCurrentPrefabStage","Undo.RevertAllDownToGroup","Undo.RecordObject","PrefabUtility.RecordPrefabInstancePropertyModifications","File.Copy","EditorUtility.IsDirty(contract)"})
            Check(source.Contains(guard),"Authoring safety guard: "+guard);
        var calls=syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(call=>call.Expression.ToString()).ToArray();
        Check(!calls.Any(call=>Regex.IsMatch(call,@"(?:^|\.)(?:SaveScene|SaveScenes|SaveOpenScenes|SaveAssets|OpenScene|CloseScene|NewScene|SaveAsPrefabAsset|ApplyPrefabInstance)$")),"No scene/prefab auto-save or scene switching.");
        Check(calls.Count(call=>call=="AssetDatabase.SaveAssetIfDirty")==1 && source.Contains("SaveAssetIfDirty(contract)"),"Save only explicitly scoped, initially clean main contract asset.");
        Check(!calls.Any(call=>Regex.IsMatch(call,@"(?:^|\.)(?:CompleteContract|SaveGame|UnlockFeature|UnlockMaterialForContract)$")),"No live save/progression/reward mutation during authoring.");
        foreach(var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            string left=assignment.Left.ToString();
            Check(!Regex.IsMatch(left,@"\.(?:budget|liveLoadWeight|bridgeSpan|goldReward|expReward)\s*$"),"Do not overwrite contract balance: "+left);
            Check(!left.StartsWith("phases[4].",StringComparison.Ordinal),"Do not change side phase fields: "+left);
            Check(!left.StartsWith("phases[3].onDialogueFinished",StringComparison.Ordinal),"Do not overwrite the existing Vance/SideContract completion callbacks: "+left);
        }
        Check(source.Contains("JsonUtility.ToJson(phases[4]) == sidePhase") && source.Contains("== sideFile") && source.Contains("ProtectedContract(contract) == mainProtected"),"Verify entire side phase/asset and protected main settings, not only selected fields.");
        Check(source.Contains("pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value"),"Preserve unrelated existing NPCs, gates, cameras, cinematics and build sites.");
        Check(source.Contains("phases[3].optionalDialogueSequence = new List<NPCOptionalDialogueStep>") && source.Contains("StepType.LessonIntroduction, lesson = Lesson()") && source.Contains("StepType.Quiz, quiz = Quiz()"),"Post-turn-in flow must include actual Bending review and quiz.");
        Check(source.Contains("onDialogueFinishedPhaseMove.phaseId == \"SideContract\"") && source.Contains("vance.visibilitySaveId == \"VanceNPC\"") && source.Contains("== \"SpawnNPC\""),"Require and retain existing side-contract and Vance callbacks.");
        Check(source.Contains("ProgressionSaveId == \"ReedNPC\"") && source.Contains("cinematicID == \"ReedCutscene\""),"Scope update to exact existing Reed actor and movie.");
        Check(source.Contains("MainContract().bridgeSpan == 60") && source.Contains("Em(\"60 meters\")"), "Authoring must validate and describe the confirmed 60m crossing.");
        Check(!Regex.IsMatch(source,@"750\s+(?:kilograms|kg)|(?:70|80)\s+meters|reference70m|contract80m",RegexOptions.IgnoreCase),"Do not quote changed weight or stale spans in new dialogue/reports.");
    }

    private static void RuntimeBoundaries()
    {
        string helper=Read(Helper); Parse(helper,Helper);
        foreach(string guard in new[]{"ProgressionSaveId == \"ReedNPC\"","phaseId == \"ExplainSituation\"","ReferenceEquals(sourcePhase, npc.CurrentPhase)","ReferenceEquals(saveOwner, PlayerDataManager.Instance.CurrentData)","IsGuestInHostWorld","IsGuestSaveProtected","IsAnyDialogueVisible","Time.timeScale > 0f","OnDisable() => StopFlow(true)","OnDestroy() => StopFlow(true)"})
            Check(helper.Contains(guard),"Introduction ownership/restoration guard: "+guard);
        Check(!Regex.IsMatch(helper,@"\b(?:CompleteContract|SaveGame|UnlockFeature|UnlockMaterialForContract)\s*\("),"Introduction may not award/rewrite progression or repeat contract rewards.");
        Check(helper.IndexOf("cinematic.PlayContractOfferCinematic()",StringComparison.Ordinal)<helper.IndexOf("ShowDialogue(situationDialogue)",StringComparison.Ordinal) &&
            helper.IndexOf("ShowDialogue(bendingDialogue)",StringComparison.Ordinal)<helper.IndexOf("learning.TryShowLesson(bendingLesson",StringComparison.Ordinal),"Real cinematic must finish before situation, Bending discussion and actual lesson popup.");
        Check(helper.Contains("destination.MoveToPhaseById(\"Reed Contract Give\")") && helper.Contains("RemoveListener(cinematicFinished)") && helper.Contains("CancelDialogueIfOwned(callback)"),"Move to original contract phase only after lesson; release owned callbacks/readers safely.");
        string npc=Read("Assets/Script/Client/NPCProgressionManager.cs");
        Check(npc.Contains("outcome != QuizOutcome.Completed") && npc.Contains("!sameStep") && npc.Contains("AlmanacQuizAccess.TryOffer"),"Existing shared quiz completion must retain cancellation/stale-step guards and Almanac entry.");
    }

    private static void SavedScene()
    {
        string scene=Read(Scene); Match[] documents=Regex.Matches(scene,@"(?ms)^--- !u!(?<type>\d+) &(?<id>-?\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)").Cast<Match>().ToArray();
        string prefab=Guid(Prefab), helperGuid=Guid(Helper); Match instance=documents.Single(d=>d.Groups["id"].Value==InstanceId);
        Check(instance.Groups["type"].Value=="1001" && instance.Value.Contains("m_SourcePrefab: {fileID: 100100000, guid: "+prefab),"Update original authored Reed prefab instance.");
        var helpers=documents.Where(d=>d.Groups["type"].Value=="114" && d.Value.Contains("guid: "+helperGuid)).ToArray();
        Check(helpers.Length==1,"Reed story helper is not saved: apply the update and save CanyonCrossing before running saved-scene checks.");
        var changes=Overrides(instance.Value);
        Check(Scalar(changes["phases.Array.size"].value)=="5","Keep original five saved phase indexes.");
        foreach(var entry in baseline.SidePhaseOverrides)
        {
            // The original side-phase dialogue has a zero-length sentence array.
            // Unity can discard its three stale out-of-range prefab overrides on save.
            if(entry.Key.Contains("phaseDialogue.sentences.Array.data[",StringComparison.Ordinal))continue;
            int separator=entry.Value.LastIndexOf('|');
            Check(changes.TryGetValue(entry.Key,out var actual) && Scalar(actual.value)==Scalar(entry.Value[..separator]) && actual.reference==entry.Value[(separator+1)..],"Keep existing side-phase override: "+entry.Key);
        }
        Check(Scalar(changes["phases.Array.data[3].onDialogueFinishedPhaseMove.phaseId"].value)=="SideContract" && changes["phases.Array.data[3].onDialogueFinishedPhaseMove.npc"].reference=="{fileID: 1070910668}","Quiz completion retains original SideContract handoff.");
        Check(changes["phases.Array.data[3].onDialogueFinished.m_PersistentCalls.m_Calls.Array.data[0].m_Target"].reference=="{fileID: 1601157402}","Retain existing Vance spawn target.");
        Check(changes["phases.Array.data[3].optionalDialogueSequence.Array.size"].value=="3","Turn-in has three ordered learning/quiz steps.");
        Check(changes["phases.Array.data[3].optionalDialogueSequence.Array.data[0].stepType"].value=="3" && changes["phases.Array.data[3].optionalDialogueSequence.Array.data[2].stepType"].value=="2","Review lesson precedes quiz.");
        Check(ReferenceGuid(changes["phases.Array.data[3].optionalDialogueSequence.Array.data[0].lesson"].reference)==Guid(Lesson) && ReferenceGuid(changes["phases.Array.data[3].optionalDialogueSequence.Array.data[2].quiz"].reference)==Guid(Quiz),"Serialized flow uses actual new Bending assets.");
        Check(helpers.Length==1 && Field(helpers[0].Value,"npc")=="{fileID: 1070910668}","One helper bound to exact Reed instance, not every prefab user.");
        Check(ReferenceGuid(Field(helpers[0].Value,"bendingLesson"))==Guid(Lesson),"Introduction helper references Bending lesson.");
        string archiveGuid=Guid("Assets/Script/UI/Almanac/AlmanacLessonTab.cs");
        Match[] archives=documents.Where(d=>d.Groups["type"].Value=="114" && d.Value.Contains("guid: "+archiveGuid)).ToArray();
        Check(archives.Length>0,"Existing lesson archives remain present.");
        foreach(var archive in archives) Check(Regex.Matches(archive.Value,"guid: "+Guid(Lesson)).Count==1,"Bending appears exactly once in each real Almanac archive.");
        foreach(var entry in baseline.OtherReedInstances)
            Check(HashText(documents.Single(d=>d.Groups["id"].Value==entry.Key).Value)==entry.Value,"Unrelated Reed-prefab instance remains unchanged: "+entry.Key);
        string contract=Read(MainContract);
        Check(contract.Contains("Now that you understand bending") && contract.Contains("Central Canyonville") && contract.Contains("Remember what we discussed about bending"),"Saved main contract includes revised story wording.");
        Check(contract.Contains("The crossing is <b><#E09500>60 meters</color></b> wide."), "Saved briefing describes the confirmed 60m crossing.");
        Check(!Regex.IsMatch(contract,@"750\s+(?:kilograms|kg)|(?:70|80)\s+meters",RegexOptions.IgnoreCase),"Saved dialogue must not contradict protected current test weight/span.");
    }

    private static Match[] Questions(string text)=>Regex.Matches(text,@"(?ms)^  - questionId: (?<id>[^\r\n]+)\r?\n(?<body>.*?)(?=^  - questionId: |\z)").Cast<Match>().ToArray();
    private static string[] Choices(string text)=>Regex.Matches(text,@"(?m)^    - text: (?<text>[^\r\n]+)").Select(m=>Scalar(m.Groups["text"].Value)).ToArray();
    private static Dictionary<string,(string value,string reference)> Overrides(string text)=>Regex.Matches(text,@"(?ms)^      propertyPath: (?<path>[^\r\n]+)\r?\n      value:(?<value>.*?)\r?\n      objectReference:(?<reference>.*?)(?=\r?\n    - target:|\r?\n    m_Removed|\z)").Cast<Match>().ToDictionary(m=>m.Groups["path"].Value,m=>(m.Groups["value"].Value.Trim(),Regex.Replace(m.Groups["reference"].Value.Trim(),@"\s+"," ")));
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashText(string value)=>Hash(Encoding.UTF8.GetBytes(Regex.Replace(value,@"\s+"," ").Trim()));
    private static string ReferenceGuid(string text)=>Regex.Match(text,@"guid: ([a-f0-9]{32})").Groups[1].Value;
    private static string Guid(string path){var m=Regex.Match(Read(path+".meta"),@"(?m)^guid: ([a-f0-9]{32})");Check(m.Success,"Missing GUID: "+path);return m.Groups[1].Value;}
    private static string OptionalField(string text,string key,int indent=2){var m=Regex.Match(text,@"(?m)^ {"+indent+"}"+Regex.Escape(key)+@":([^\r\n]*)");return m.Success?Scalar(m.Groups[1].Value):null;}
    private static string Field(string text,string key,int indent=2){var value=OptionalField(text,key,indent);Check(value!=null,"Missing field: "+key);return value;}
    private static string Scalar(string value){value=value.Trim();if(value.Length>=2&&value[0]=='\''&&value[^1]=='\'')return value[1..^1].Replace("''","'");if(value.Length>=2&&value[0]=='"'&&value[^1]=='"')return JsonSerializer.Deserialize<string>(value);return value;}
    private static SyntaxNode Parse(string text,string path){var tree=CSharpSyntaxTree.ParseText(text,CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_EDITOR"));Check(!tree.GetDiagnostics().Any(d=>d.Severity==DiagnosticSeverity.Error),"C# syntax errors: "+path);return tree.GetRoot();}
    private static string Read(string path)=>File.ReadAllText(Path.Combine(root,path));
    private static byte[] ReadBytes(string path)=>File.ReadAllBytes(Path.Combine(root,path));
    private static void Check(bool condition,string message){checks++;if(!condition)throw new InvalidOperationException(message);}
    private static string FindRoot(){for(DirectoryInfo dir=new(Environment.CurrentDirectory);dir!=null;dir=dir.Parent)if(File.Exists(Path.Combine(dir.FullName,Author)))return dir.FullName;throw new InvalidOperationException("Run from Unity project root.");}
}
