using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static readonly List<QuizData> catalogBanks=new();
    private static readonly BindingFlags PrivateStatic=BindingFlags.NonPublic|BindingFlags.Static;
    private static void Main(string[] args)
    {
        int catalogArgument=Array.IndexOf(args,"--quiz-review-catalog");
        if(catalogArgument>=0) CheckQuizCatalogParity(args[catalogArgument+1]);
        CheckAcknowledgments(); CheckQuizRecords(); CheckQuizReviews(); CheckProjection(); CheckTransport(); CheckTransactionalMethods(); CheckCurrentCatalogBudget();
        Console.WriteLine($"PASS: {checks} production dashboard/quiz/photo metadata assertions. Unity engine, disk and PlayFab are test boundaries; no live accounts or network used.");
    }
    private static void Check(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
    private static JsonElement Json(object value)=>JsonDocument.Parse(JsonUtility.ToJson(value)).RootElement.Clone();
    private static object Invoke(string name,params object[] args)=>typeof(DashboardSyncService).GetMethod(name,PrivateStatic).Invoke(null,args);
    private static JsonElement Almanac(PlayerDataManager manager)=>Json(Invoke("BuildAlmanac",manager));
    private static JsonElement Reviews(PlayerDataManager manager)=>JsonDocument.Parse((string)Invoke("BuildQuizReviewJson",manager.CurrentData,Invoke("LatestQuizResults",manager.CurrentData))).RootElement.Clone();
    private static void CheckAcknowledgments()
    {
        foreach(object version in new object[]{(byte)1,(short)1,1,1L,1f,1d,1m})
            Check(GameDashboardRecords.Acknowledged(new Dictionary<string,object>{{"accepted",true},{"schemaVersion",version}}),"SDK numeric 1 acknowledged.");
        foreach(object version in new object[]{null,"1",true,0,2,-1,double.NaN,double.PositiveInfinity,1.0000000000000002})
            Check(!GameDashboardRecords.Acknowledged(new Dictionary<string,object>{{"accepted",true},{"schemaVersion",version}}),"Missing, nonnumeric and wrong schema rejected.");
        Check(!GameDashboardRecords.Acknowledged(new Dictionary<string,object>{{"schemaVersion",1}}),"Acknowledgment required.");
        foreach(object version in new object[]{(byte)1,(short)1,1,1L,1f,1d,1m})
            Check(GameDashboardRecords.AcknowledgedQuizReview(new Dictionary<string,object>{{"quizReviewSchemaVersion",version}}),"Review numeric schema 1 acknowledged.");
        foreach(object version in new object[]{null,"1",true,0,2,-1,double.NaN,double.PositiveInfinity,1.1})
            Check(!GameDashboardRecords.AcknowledgedQuizReview(new Dictionary<string,object>{{"quizReviewSchemaVersion",version}}),"Wrong review schema rejected.");
        Check(!GameDashboardRecords.AcknowledgedQuizReview(new Dictionary<string,object>{{"schemaVersion",1}}),"Older backend cannot silently acknowledge an ignored review.");
    }
    private static QuizData Quiz()=>new(){quizId="quiz-v1",title="Real quiz",assessmentMode=true,questions=new(){
        new(){questionId="q1",prompt="Actual question",correctChoiceIndex=0,choices=new(){new(){text="Yes"},new(){text="No"}}},
        new(){questionId="q2",prompt="Actual question 2",correctChoiceIndex=1,choices=new(){new(){text="No"},new(){text="Yes"}}}}};
    private static QuizProgressSaveData Progress(QuizData quiz)=>new(){quizId=quiz.quizId,contentFingerprint=quiz.ContentFingerprint,
        progressKey="context",submitted=true,answers=new(){new(){questionId="q1",choiceIndex=0},new(){questionId="q2",choiceIndex=0}}};
    private static void CheckQuizRecords()
    {
        QuizData quiz=Quiz(); var p=Progress(quiz);
        Check(GameDashboardRecords.TryQuizResult(quiz,p,"real-id","2026-10-29T12:00:00Z",out var result)&&result.score==1&&result.totalQuestions==2,"Genuine score retained independently of passing.");
        string legacy=GameDashboardRecords.LegacyQuizResultId(p); p.answers.Reverse();
        Check(legacy==GameDashboardRecords.LegacyQuizResultId(p),"Legacy identity independent of answer insertion order.");
        Check(GameDashboardRecords.TryQuizResult(quiz,p,legacy,"",out result)&&result.completedAt=="","Legacy date stays unknown.");
        p.answers.Find(answer=>answer.questionId=="q1").choiceIndex=1;
        Check(GameDashboardRecords.TryQuizResult(quiz,p,legacy,"",out result)&&result.selectedIndices.SequenceEqual(new[]{1,0}),"Canonical question/choice indices ignore shuffled question display and answer insertion order.");
        p.answers[0].choiceIndex=1;
        Check(result.selectedIndices.SequenceEqual(new[]{1,0}),"Submitted review owns an independent answer snapshot.");
        p.answers[0].questionId="foreign"; Check(!GameDashboardRecords.TryQuizResult(quiz,p,legacy,"",out _),"Foreign question rejected.");
        p=Progress(quiz);p.answers.Add(p.answers[0]); Check(!GameDashboardRecords.TryQuizResult(quiz,p,legacy,"",out _),"Duplicate answer rejected.");
        p=Progress(quiz);p.answers[0].choiceIndex=9;Check(!GameDashboardRecords.TryQuizResult(quiz,p,legacy,"",out _),"Out-of-range choice rejected.");
        p=Progress(quiz);quiz.questions[0].prompt="Changed";Check(!GameDashboardRecords.TryQuizResult(quiz,p,legacy,"",out _),"Changed bank cannot reconstruct an old score.");
        quiz=Quiz();while(quiz.questions[0].choices.Count<21)quiz.questions[0].choices.Add(new(){text="Authored extra choice"});
        p=Progress(quiz);p.answers[0].choiceIndex=20;
        Check(GameDashboardRecords.TryQuizResult(quiz,p,"future-valid-quiz","",out result)&&!GameDashboardRecords.ValidQuizReview(result),"A valid future quiz outside review choice bounds keeps its genuine aggregate/submission.");
    }
    private static void CheckQuizReviews()
    {
        var m=Manager();var quiz=Quiz();var progress=Progress(quiz);
        Resources.objects=new object[]{quiz};
        Check(GameDashboardRecords.TryQuizResult(quiz,progress,"submitted-result","2026-10-29T12:00:00Z",out var result),"Review fixture is a real submission.");
        m.CurrentData.quizResults.Add(result);
        var document=Reviews(m);var review=document.GetProperty("reviews")[0];
        Check(document.GetProperty("schemaVersion").GetInt32()==1&&review.GetProperty("quizId").GetString()==result.quizId&&review.GetProperty("attemptId").GetString()==result.attemptId,"Compact review binds quiz and immutable submitted result.");
        Check(review.GetProperty("selectedIndices").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{0,0})&&review.GetProperty("contentFingerprint").GetString()==quiz.ContentFingerprint,"Compact review retains exact content fingerprint and canonical choices.");
        Check(review.EnumerateObject().Select(p=>p.Name).OrderBy(v=>v).SequenceEqual(new[]{"attemptId","contentFingerprint","quizId","selectedIndices"}),"Review projection has no private progress context, question text or correct-answer key.");
        Resources.objects=Array.Empty<object>();
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==1,"Frozen submitted review survives an unloaded/changed quiz bank.");
        result.contentFingerprint="unknown";
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0&&Almanac(m).GetProperty("quizAttempts").GetArrayLength()==1,"Invalid version hides review without removing its genuine aggregate.");
        result.contentFingerprint=quiz.ContentFingerprint;result.selectedIndices[0]=20;
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"Unsupported selected choice is not published.");
        result.selectedIndices=new(){0};
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"Partial answer snapshots cannot be reviewed.");
        result.selectedIndices=null;m.CurrentData.quizProgress.Add(progress);Resources.objects=new object[]{quiz};
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==1&&result.selectedIndices==null,"Exact retained legacy context may reconstruct review without changing the save.");
        m.CurrentData.quizProgress.Add(progress.Clone());
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"Duplicate matching legacy contexts remain ambiguous.");
        m.CurrentData.quizProgress.RemoveAt(1);Resources.objects=new object[]{quiz,quiz};
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"Duplicate matching definitions remain ambiguous.");
        Resources.objects=new object[]{quiz};result.progressKey="different-context";
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"An older or foreign context cannot supply the latest submission's answers.");
        result.progressKey=progress.progressKey;result.score=2;
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"Reconstructed score must match the retained aggregate.");
        result.score=1;result.title="Other quiz";
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"Reconstructed title must match the retained aggregate.");
        result.title=quiz.title;result.totalQuestions=3;
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"Reconstructed total must match the retained aggregate.");
        result.totalQuestions=2;quiz.questions[0].prompt="Changed version";
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0,"New quiz content cannot reinterpret retained answers.");
        quiz=Quiz();Resources.objects=new object[]{quiz};m.CurrentData.quizResults.Clear();
        var legacy=Reviews(m).GetProperty("reviews");
        Check(legacy.GetArrayLength()==1&&legacy[0].GetProperty("attemptId").GetString().StartsWith("legacy-"),"Unambiguous older submitted progress uses the same deterministic legacy identity as its aggregate.");
        Check(Almanac(m).GetProperty("quizAttempts")[0].GetProperty("completedAt").GetString()=="","Recovering answers does not invent a completion date.");
        m.CurrentData.quizResults.Add(result);m.CurrentData.quizResults.Add(result);
        Check(Reviews(m).GetProperty("reviews").GetArrayLength()==0&&Almanac(m).GetProperty("quizAttempts").GetArrayLength()==0,"Duplicate latest aggregates cannot publish ambiguous reviews.");
        m.CurrentData.quizResults.Clear();m.CurrentData.quizResults.Add(result);result.selectedIndices=new(){0,0};
        var payload=Invoke("BuildPayload",m);string signature=(string)Invoke("PayloadSignature",payload);
        var parameters=payload.GetType().GetFields().ToDictionary(field=>field.Name,field=>field.GetValue(payload));
        Check((bool)Invoke("ProjectionFits",payload,parameters),"Compact review has its own bounded projection.");
        result.selectedIndices=new(){1,0};
        Check(signature!=(string)Invoke("PayloadSignature",Invoke("BuildPayload",m)),"Answer changes participate in sync identity even with unchanged aggregates.");
        payload.GetType().GetField("quizReviewProgress").SetValue(payload,new string('x',10001));
        Check(!(bool)Invoke("ProjectionFits",payload,parameters),"Review body is bounded independently; it is never silently truncated.");
    }
    private static void CheckQuizCatalogParity(string path)
    {
        var catalog=JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Check(catalog.GetProperty("schemaVersion").GetInt32()==1,"Website quiz catalog schema is explicit.");
        foreach(var version in catalog.GetProperty("versions").EnumerateArray())
        {
            var quiz=new QuizData {quizId=version.GetProperty("quizId").GetString(),title=version.GetProperty("title").GetString(),assessmentMode=true};
            foreach(var question in version.GetProperty("questions").EnumerateArray())
                quiz.questions.Add(new(){questionId=question.GetProperty("questionId").GetString(),prompt=question.GetProperty("question").GetString(),
                    feedback=question.TryGetProperty("explanation",out var explanation)?explanation.GetString():null,
                    correctChoiceIndex=question.GetProperty("correctIndex").GetInt32(),choices=question.GetProperty("choices").EnumerateArray().Select(choice=>new QuizChoice{text=choice.GetString()}).ToList()});
            Check(quiz.TryValidate(out _)&&quiz.ContentFingerprint==version.GetProperty("contentFingerprint").GetString(),"Actual C# quiz fingerprint agrees with immutable website catalog for "+quiz.quizId+".");
            var progress=CatalogProgress(quiz);
            progress.answers.Reverse();
            Check(GameDashboardRecords.TryQuizResult(quiz,progress,"catalog-submission","2026-10-29T12:00:00Z",out var result)&&
                result.selectedIndices.SequenceEqual(quiz.questions.Select(q=>q.correctChoiceIndex))&&result.score==quiz.QuestionCount,
                "Actual C# canonical selected indices and aggregate match website answer keys for "+quiz.quizId+".");
            catalogBanks.Add(quiz);
        }
        Check(catalogBanks.Count>=10,"All current authored assessment banks participate in cross-runtime parity.");
    }
    private static QuizProgressSaveData CatalogProgress(QuizData quiz)=>new(){quizId=quiz.quizId,progressKey="catalog-budget-context",contentFingerprint=quiz.ContentFingerprint,
        submitted=true,answers=quiz.questions.Select(q=>new QuizAnswerSaveData{questionId=q.questionId.Trim(),choiceIndex=q.correctChoiceIndex}).ToList()};
    private static PlayerDataManager Manager()
    {
        var m=new PlayerDataManager();m.allGameContracts.Add(new(){ContractID="ShopKeeper",name="Shop",contractMap=ContractSO.ContractMap.CanyonCrossing});
        return m;
    }
    private static BridgePhotoSnapshot Snapshot(string id)=>new(){contractId="ShopKeeper",regionId="canyon-crossing",resultId=id,entryName="bridge-"+id+".png",sha256=new string('a',64)};
    private static void CheckProjection()
    {
        var m=Manager(); string id=new string('a',32);
        m.CurrentData.savedBridges.Add(new(){contractId="ShopKeeper",resultId=id,bestStarResult=new(){completed=true,efficient=true,strong=true},latestStarResult=new(){completed=true},snapshot=Snapshot(id)});
        var level=Almanac(m).GetProperty("regions")[0].GetProperty("levels")[0];
        Check(level.GetProperty("status").GetString()!="completed","Bridge success alone is not turn-in.");
        m.CurrentData.completedContracts.Add("ShopKeeper");
        level=Almanac(m).GetProperty("regions")[0].GetProperty("levels")[0];var c=level.GetProperty("completion");
        Check(c.GetProperty("completedAt").GetString()=="","Unknown turn-in date not replaced by sync time.");
        Check(c.GetProperty("score").GetInt32()==1&&c.GetProperty("scoreKnown").GetBoolean(),"Latest stars, not best stars, describe latest photo.");
        Check(c.GetProperty("snapshot").GetProperty("resultId").GetString()==id,"Same selected result included.");
        m.CurrentData.savedBridges[0].resultId=new string('b',32);
        Check(Almanac(m).GetProperty("regions")[0].GetProperty("levels")[0].GetProperty("completion").GetProperty("snapshot").ValueKind==JsonValueKind.Null,"Old design image not published for redesign.");
        m.CurrentData.savedBridges[0].latestStarResult=null;
        Check(!Almanac(m).GetProperty("regions")[0].GetProperty("levels")[0].GetProperty("completion").GetProperty("scoreKnown").GetBoolean(),"Legacy best score not substituted for missing latest score.");
        m.CurrentData.contractCompletionRecords.Add(new(){contractId="ShopKeeper",completedAtUtc="2026-10-29T12:00:00Z"});
        Check(Almanac(m).GetProperty("regions")[0].GetProperty("levels")[0].GetProperty("completion").GetProperty("completedAt").GetString()=="2026-10-29T12:00:00Z","Recorded turn-in date retained.");
        m.CurrentData.unlockedLessonIds.AddRange(new[]{"LESSON002","deadload","deadload"});m.CurrentData.completedLessons.Add("not-a-lesson-id");
        Check(Almanac(m).GetProperty("lessonRecords").GetArrayLength()==2&&Almanac(m).GetProperty("lessonRecords").EnumerateArray().All(v=>v.GetProperty("state").GetString()=="discovered"),"Only distinct explicit discoveries published.");
        m.CurrentData.discoveredMaterialIds.AddRange(new[]{"ConcreteBeam","Beam","ConcreteBeam"});
        Check(Almanac(m).GetProperty("discoveredMaterials").EnumerateArray().Select(v=>v.GetString()).SequenceEqual(new[]{"concrete_member","wood_beam"}),"Concrete member and cumulative discovery map.");
        var equipment=new CosmeticLoadoutData { accessoryIDs=new(){"A","B","C"},shirtID="shirt" };
        var e=JsonDocument.Parse((string)Invoke("BuildCosmeticsJson",equipment)).RootElement;
        Check(e.GetProperty("accessoryIDs").GetArrayLength()==3&&e.GetProperty("hairID").GetString()=="","All accessories and explicitly empty clothing remain separate.");
        var quiz=Quiz();Resources.objects=new object[]{quiz};m.CurrentData.quizProgress.Add(Progress(quiz));
        Check(Almanac(m).GetProperty("quizAttempts")[0].GetProperty("completedAt").GetString()=="","Legacy validated score has no invented date.");
        m.CurrentData.quizProgress.Add(Progress(quiz));
        Check(Almanac(m).GetProperty("quizAttempts").GetArrayLength()==0,"Ambiguous legacy contexts cannot invent a latest attempt.");
    }
    private static void Call(object service,string method)=>service.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(service,null);
    private static void CheckTransport()
    {
        PlayFabClientAPI.Calls.Clear();Time.realtimeSinceStartup=100;
        var m=Manager();var cloud=new CloudSaveManager();var service=new DashboardSyncService();
        service.components[typeof(PlayerDataManager)]=m;service.components[typeof(CloudSaveManager)]=cloud;Call(service,"Awake");Call(service,"Update");
        Check(PlayFabClientAPI.Calls.Count==1,"Ready account starts existing publisher.");Call(service,"Update");Check(PlayFabClientAPI.Calls.Count==1,"One request in flight.");
        var first=PlayFabClientAPI.Calls[0];cloud.ActiveAccountId="B";m.ActiveAccountId="B";cloud.SessionGeneration++;
        Call(service,"Update");Check(PlayFabClientAPI.Calls.Count==2,"New account gets own request.");
        first.Success(new(){FunctionResult=new Dictionary<string,object>{{"accepted",true},{"schemaVersion",1}}});Check(service.IsSyncing,"Old callback cannot finish new account request.");
        PlayFabClientAPI.Calls[1].Success(new(){FunctionResult=new Dictionary<string,object>{{"accepted",true},{"schemaVersion",2}}});
        Check(service.LastSyncError!=null&&!service.IsSyncing,"Wrong schema stays pending.");
        Time.realtimeSinceStartup+=31;Call(service,"Update");Check(PlayFabClientAPI.Calls.Count==3,"Failure retries.");
        PlayFabClientAPI.Calls[2].Success(new(){FunctionResult=new Dictionary<string,object>{{"accepted",true},{"schemaVersion",1L}}});
        Check(service.LastSyncError!=null,"Review publication requires its additive backend acknowledgment.");
        Time.realtimeSinceStartup+=31;Call(service,"Update");Check(PlayFabClientAPI.Calls.Count==4,"Older backend acknowledgment retries without losing submitted review.");
        PlayFabClientAPI.Calls[3].Success(new(){FunctionResult=new Dictionary<string,object>{{"accepted",true},{"schemaVersion",1L},{"quizReviewSchemaVersion",1}}});
        Time.realtimeSinceStartup+=6;Call(service,"Update");Check(PlayFabClientAPI.Calls.Count==4,"Unchanged accepted projection deduplicated.");
        m.CurrentData.unlockedLessonIds.Add(new string('x',11000));m.Commit();Time.realtimeSinceStartup+=6;Call(service,"Update");
        Check(PlayFabClientAPI.Calls.Count==4&&service.LastSyncError.Contains("size"),"Oversized projection not truncated or submitted.");
    }
    private static void CheckTransactionalMethods()
    {
        string root=FindRoot();string source=File.ReadAllText(Path.Combine(root,"Assets/Script/Player/PlayerSave/PlayerDataManager.cs"));
        var owner=CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First(c=>c.Identifier.ValueText=="PlayerDataManager");
        string Method(string name)=>owner.Members.OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText==name).ToFullString();
        string body="using System;using System.Collections.Generic;using System.Text;using System.Security.Cryptography;using UnityEngine;public sealed class SaveProbe {"+
            "public PlayerData CurrentData=new PlayerData(); public bool saveSucceeds=true;public string activeAccountId=\"A\"; public bool TrySaveGame()=>saveSucceeds;"+
            "public SavedBridgeData GetSavedBridge(string id)=>CurrentData.savedBridges.Find(b=>b.contractId==id);private ContractSO FindRegisteredContract(string id)=>new ContractSO{ContractID=id,contractMap=ContractSO.ContractMap.CanyonCrossing};"+
            "public bool TryGetQuizProgress(QuizData quiz,QuizContext context,out QuizProgressSaveData record){var old=CurrentData.quizProgress.Find(p=>p.quizId==quiz.quizId&&p.progressKey==context.ProgressKey);record=old?.Clone()??new QuizProgressSaveData{quizId=quiz.quizId,contentFingerprint=quiz.ContentFingerprint,progressKey=context.ProgressKey};return true;}"+
            Method("TrySaveQuizAssessment")+Method("TryRecordBridgePhotoSnapshot")+Method("RecordCharacterPortraitUpload")+
            Method("ComputeBridgePhotoHash")+Method("ComputeSha256")+
            owner.Members.OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="BridgePhotoCacheIdentity").ToFullString()+"}";
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p)).Append(MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location));
        var compilation=CSharpCompilation.Create("TransactionProbe",new[]{CSharpSyntaxTree.ParseText(body)},refs,new(OutputKind.DynamicallyLinkedLibrary));
        using var ms=new MemoryStream();var emitted=compilation.Emit(ms);if(!emitted.Success)throw new Exception(string.Join("\n",emitted.Diagnostics));
        var type=Assembly.Load(ms.ToArray()).GetType("SaveProbe");var probe=Activator.CreateInstance(type);var data=(PlayerData)type.GetField("CurrentData").GetValue(probe);
        var quiz=Quiz();var ctx=new QuizContext("npc","phase","step");var answers=new Dictionary<string,int>{{"q1",0},{"q2",0}};
        var method=type.GetMethod("TrySaveQuizAssessment");object[] args={quiz,ctx,answers,true,false,null,null,null};
        type.GetField("saveSucceeds").SetValue(probe,false);
        Check(!(bool)method.Invoke(probe,args)&&data.quizProgress.Count==0&&data.quizResults.Count==0,"Failed real submission rolls back answers and result together.");
        type.GetField("saveSucceeds").SetValue(probe,true);
        Check((bool)method.Invoke(probe,args)&&data.quizResults.Count==1&&data.quizResults[0].score==1,"Successful real submission saves aggregate.");
        Check(data.quizResults[0].selectedIndices.SequenceEqual(new[]{0,0}),"Successful real submission commits review with answers and aggregate.");
        string id=data.quizResults[0].attemptId,date=data.quizResults[0].completedAt;args[4]=true;
        Check((bool)method.Invoke(probe,args)&&data.quizResults.Count==1&&data.quizResults[0].attemptId==id&&data.quizResults[0].completedAt==date,"Continue/retry retains submitted identity and date.");
        Check(data.quizResults[0].selectedIndices.SequenceEqual(new[]{0,0}),"Continue/retry retains the same canonical submitted review.");
        var newerContext=new QuizContext("npc","later-phase","step");var newerAnswers=new Dictionary<string,int>{{"q1",1},{"q2",1}};object[] newerArgs={quiz,newerContext,newerAnswers,true,false,null,null,null};
        Check((bool)method.Invoke(probe,newerArgs)&&data.quizResults.Count==1&&data.quizResults[0].attemptId!=id,"Later genuine submission replaces latest retained aggregate.");
        string newerId=data.quizResults[0].attemptId;
        Check((bool)method.Invoke(probe,args)&&data.quizResults[0].attemptId==newerId,"Continuing an old context does not replace newer submitted results.");
        Check(data.quizResults[0].selectedIndices.SequenceEqual(new[]{1,1}),"Continuing an older context cannot replace the latest submission's answer review.");
        string photoId=new string('c',32);var bridge=new SavedBridgeData {contractId="ShopKeeper",resultId=photoId};data.savedBridges.Add(bridge);
        var snapshot=Snapshot(photoId);var recordPhoto=type.GetMethod("TryRecordBridgePhotoSnapshot");object[] photoArgs={"ShopKeeper",photoId,snapshot};
        type.GetField("saveSucceeds").SetValue(probe,false);
        Check(!(bool)recordPhoto.Invoke(probe,photoArgs)&&bridge.snapshot==null,"Failed snapshot metadata save rolls back publication.");
        type.GetField("saveSucceeds").SetValue(probe,true);
        var photoHash=type.GetMethod("ComputeBridgePhotoHash",BindingFlags.NonPublic|BindingFlags.Static);string before=(string)photoHash.Invoke(null,new object[]{bridge});
        Check((bool)recordPhoto.Invoke(probe,photoArgs)&&bridge.snapshot==snapshot,"Same result finalized snapshot may persist.");
        Check(before==(string)photoHash.Invoke(null,new object[]{bridge}),"Snapshot finalization cannot invalidate or regenerate the original local photo.");
        photoArgs[1]=new string('d',32);Check(!(bool)recordPhoto.Invoke(probe,photoArgs),"Obsolete result cannot attach its image to a redesign.");
        type.GetField("saveSucceeds").SetValue(probe,false);data.characterPortraitFileName="old.png";data.characterPortraitUpdatedAtUtc="old-date";data.characterPortraitChecksum="old-hash";
        Check(!(bool)type.GetMethod("RecordCharacterPortraitUpload").Invoke(probe,new object[]{"characterPortrait.png","new-date","new-hash"})&&data.characterPortraitFileName=="old.png"&&data.characterPortraitChecksum=="old-hash","Failed portrait metadata save rolls back its publication fields.");
        string complete=Method("CompleteContract");Check(complete.IndexOf("contractCompletionRecords.Add",StringComparison.Ordinal)<complete.IndexOf("if (!TrySaveGame())",StringComparison.Ordinal)&&complete.Contains("CurrentData.contractCompletionRecords = previousCompletionRecords;"),"Turn-in date shares completion commit and rollback.");
        Check(!Method("WriteBridgePhotoCache").Contains("QueuePhoto")&&Method("TrySaveBridgePhoto").Contains("QueuePhoto"),"Only original successful-result capture queues upload, not regenerated cache.");
    }
    private static void CheckCurrentCatalogBudget()
    {
        string root=FindRoot(),dataRoot=Path.Combine(root,"Assets/BridgeBuilder/Data");
        string Scalar(string text,string key)=>Regex.Match(text,@"(?m)^  "+Regex.Escape(key)+@": ?([^\r\n]*)").Groups[1].Value.Trim().Trim('\'','"');
        var manager=new PlayerDataManager {ActiveAccountId="CURRENTCATALOG"};int ordinal=1;
        foreach(string path in Directory.GetFiles(Path.Combine(dataRoot,"Contract"),"*.asset",SearchOption.AllDirectories))
        {
            string text=File.ReadAllText(path),id=Scalar(text,"contractID");if(string.IsNullOrWhiteSpace(id)||Scalar(text,"hideFromLeaderboard")=="1")continue;
            var contract=new ContractSO {ContractID=id,name=Scalar(text,"m_Name"),contractMap=(ContractSO.ContractMap)int.Parse(Scalar(text,"contractMap")),isStoryModeContract=Scalar(text,"isStoryModeContract")=="1"};
            manager.allGameContracts.Add(contract);manager.CurrentData.completedContracts.Add(id);
            string resultId=(ordinal++).ToString("x32");
            manager.CurrentData.savedBridges.Add(new(){contractId=id,resultId=resultId,capturedAtUtc="2026-10-29T12:00:00.0000000Z",latestStarResult=new(){completed=true,efficient=true,strong=true},
                bestStarResult=new(){completed=true,efficient=true,strong=true},snapshot=new(){contractId=id,regionId=DashboardSyncService.RegionId(contract.contractMap),resultId=resultId,entryName="bridge-"+resultId+".png",sha256=new string('a',64),capturedAt="2026-10-29T12:00:00.0000000Z"}});
            manager.CurrentData.contractCompletionRecords.Add(new(){contractId=id,completedAtUtc="2026-10-29T12:00:00.0000000Z"});
        }
        foreach(string path in Directory.GetFiles(Path.Combine(dataRoot,"Lessons"),"*.asset",SearchOption.AllDirectories))
        { string text=File.ReadAllText(path),id=Scalar(text,"lessonId");if(id.Length>0&&id!="NEWLESSON")manager.CurrentData.unlockedLessonIds.Add(id); }
        foreach(string path in Directory.GetFiles(Path.Combine(dataRoot,"Quized"),"*.asset",SearchOption.AllDirectories))
        {
            string text=File.ReadAllText(path),id=Scalar(text,"quizId");if(id.Length==0||Scalar(text,"assessmentMode")!="1")continue;
            int total=Regex.Matches(text,@"(?m)^  - questionId:").Count;
            var definitions=catalogBanks.Where(q=>q.quizId==id&&q.title==Scalar(text,"title")&&q.QuestionCount==total).ToList();
            if(catalogBanks.Count>0)
            {
                Check(definitions.Count==1,"Full asset catalog has one reviewed current quiz definition for "+id+".");
                var quiz=definitions[0];var progress=CatalogProgress(quiz);
                Check(GameDashboardRecords.TryQuizResult(quiz,progress,Guid.NewGuid().ToString("N"),"2026-10-29T12:00:00.0000000Z",out var actual),"Budget uses actual C# captured answers and score for "+id+".");
                manager.CurrentData.quizResults.Add(actual);
            }
            else manager.CurrentData.quizResults.Add(new(){quizId=id,attemptId=Guid.NewGuid().ToString("N"),title=Scalar(text,"title"),score=3,totalQuestions=total,
                completedAt="2026-10-29T12:00:00.0000000Z",contentFingerprint=new string('a',64),selectedIndices=Enumerable.Repeat(19,total).ToList()});
        }
        foreach(string path in Directory.GetFiles(Path.Combine(dataRoot,"Achievements"),"*.asset",SearchOption.AllDirectories))
        { string text=File.ReadAllText(path),id=Scalar(text,"achievementID");if(id.Length==0)continue;manager.allGameAchievements.Add(new(){achievementID=id,achievementName=Scalar(text,"achievementName"),description=Scalar(text,"description")});manager.CurrentData.unlockedAchievements.Add(id);manager.CurrentData.achievementUnlocks.Add(new(){achievementId=id,unlockedAtUtc="2026-10-29T12:00:00.0000000Z"}); }
        manager.CurrentData.discoveredMaterialIds.AddRange(new[]{"Beam","Pier","RopeMaterial","Wood Road","Concrete Road","ConcreteBeam"});
        manager.CurrentData.cosmeticLoadout.accessoryIDs.AddRange(new[]{"EngineeringHardHat","Accessory_SafetyVest","Accessory_SmallCap"});
        manager.CurrentData.cosmeticLoadout.hairID="Hair_2";manager.CurrentData.cosmeticLoadout.shirtID="Shirt_Polo";manager.CurrentData.cosmeticLoadout.pantsID="Pants_3";manager.CurrentData.cosmeticLoadout.shoesID="Shoes_1";
        var payload=Invoke("BuildPayload",manager);var parameters=new Dictionary<string,object>();
        foreach(var field in payload.GetType().GetFields())parameters[field.Name]=field.GetValue(payload);
        int args=System.Text.Encoding.UTF8.GetByteCount(new Serializer().SerializeObject(parameters));
        string almanac=(string)parameters["almanacProgress"],map=(string)parameters["mapProgress"],reviews=(string)parameters["quizReviewProgress"];
        Console.WriteLine($"Current catalog budget: {manager.allGameContracts.Count} contracts, {manager.CurrentData.unlockedLessonIds.Count} lessons, {manager.CurrentData.quizResults.Count} quizzes, {manager.allGameAchievements.Count} achievements; Almanac={almanac.Length}, Map={map.Length}, QuizReviews={reviews.Length}, args UTF8={args}.");
        Check(JsonDocument.Parse(reviews).RootElement.GetProperty("reviews").GetArrayLength()==manager.CurrentData.quizResults.Count,"Every current quiz fits a compact review without truncation.");
        Check((bool)Invoke("ProjectionFits",payload,parameters),"Full current asset catalog must fit existing projection/argument limits.");
        manager.CurrentData.savedBridges[0].snapshot=null;
        var sparsePayload=Invoke("BuildPayload",manager);var sparse=JsonDocument.Parse((string)sparsePayload.GetType().GetField("almanacProgress").GetValue(sparsePayload)).RootElement;
        var firstId=manager.CurrentData.savedBridges[0].contractId;
        var sparseCompletion=sparse.GetProperty("regions").EnumerateArray().SelectMany(r=>r.GetProperty("levels").EnumerateArray()).Single(l=>l.GetProperty("levelId").GetString()==firstId).GetProperty("completion");
        Check(sparseCompletion.GetProperty("snapshot").ValueKind==JsonValueKind.Null,"Pending design explicitly clears old published photo binding.");
    }
    private static string FindRoot(){var d=new DirectoryInfo(Environment.CurrentDirectory);while(d!=null&&!File.Exists(Path.Combine(d.FullName,"Assets/Script/Player/PlayerSave/PlayerDataManager.cs")))d=d.Parent;return d?.FullName??throw new Exception("Run from repository root.");}
}
