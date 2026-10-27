using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root=Directory.GetCurrentDirectory();
while(!File.Exists(Path.Combine(root,"Assets/Script/UI/Almanac/AlmanacManager.cs")))
    root=Directory.GetParent(root)?.FullName??throw new InvalidOperationException("Unity root not found.");
string source=File.ReadAllText(Path.Combine(root,"Assets/Script/UI/Almanac/AlmanacManager.cs"));
var type=CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="AlmanacManager");
string Method(string name)=>type.Members.OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText==name).ToFullString();
string[] names={"OpenAlmanac","OpenAlmanacRoutine","CloseAlmanac","CloseAlmanacThen","CloseAlmanacRoutine",
    "TryStartPendingTabUnlockTutorial","TryStartSelectedTabUnlockTutorial","StartSelectedTabUnlockTutorialRoutine",
    "TryStartTabUnlockTutorial","GetTabUnlockTutorial","IsTabUnlocked","HoldSuspendedTutorialResume","ReleaseSuspendedTutorialResume",
    "IsAnyTabUnlockTutorialPlaying","IsTabUnlockTutorialSequence","OwnsTutorialSequence","IsRequiredFirstOpenTutorialBlockingClose","IsTabUnlockTutorialBlockingClose",
    "HandleTutorialCompleted","StartNextPendingTabTutorialRoutine","OnDestroy"};
string actual=string.Join("\n",names.Select(Method))+string.Join("\n",type.Members.OfType<PropertyDeclarationSyntax>().Where(value=>value.Identifier.ValueText is "IsOpenAndReady" or "CanShowHudOpenButton"));
string boundary=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Boundary.cs.txt")).Replace("// PRODUCTION_METHODS",actual);
var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
var compilation=CSharpCompilation.Create("AlmanacDialogue_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(boundary),CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Scenarios.cs.txt")))},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output=new MemoryStream();var result=compilation.Emit(output);
if(!result.Success)throw new InvalidOperationException(string.Join("\n",result.Diagnostics.Where(value=>value.Severity==DiagnosticSeverity.Error)));
int checks;
try{checks=(int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null,null);}
catch(TargetInvocationException error){throw new InvalidOperationException("Almanac dialogue tutorial regression failed.",error.InnerException);}
int guards=0;void Guard(bool okay,string label){guards++;if(!okay)throw new InvalidOperationException(label);}
Guard(!Method("OpenAlmanac").Contains("DialogueManager")&&!Method("OpenAlmanacRoutine").Contains("DialogueManager"),"Programmatic story opening never rejects a closing dialogue callback.");
Guard(type.Members.OfType<MethodDeclarationSyntax>().All(value=>value.Identifier.ValueText is not ("RefreshBookIntroduction" or "CanPresentBookIntroduction" or "TryStartBookIntroduction" or "CancelOwnedBookTutorials")),"Rejected deferred/cancelled introduction behavior stays removed.");
Guard(Method("CloseAlmanacRoutine").IndexOf("yield return PlayBookAnimation(false)",StringComparison.Ordinal)<Method("CloseAlmanacRoutine").IndexOf("ReleaseSuspendedTutorialResume()",StringComparison.Ordinal),"Normal close retains resume hold through animation.");
foreach(string name in new[]{"CloseAlmanac","CloseAlmanacThen","OnDestroy"})
    Guard(!Method(name).Contains("CompleteLesson(")&&!Method(name).Contains("CompleteTutorial(")&&!Method(name).Contains("SkipTutorial("),"Book cancellation/teardown cannot manufacture completion: "+name);
Guard(Method("Awake").Contains("DialogueHudButtonGate.BindAlmanac(hudOpenButton, this)"),"Actual Almanac binds its exact serialized HUD control to the separate dialogue gate.");
Console.WriteLine($"PASS: {checks} executed Almanac HUD-eligibility and preserved story/reader behavior assertions; {guards} production policy guards. UI/animation/tutorial-engine boundaries only; no live data changed.");
