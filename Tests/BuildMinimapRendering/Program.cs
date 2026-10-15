using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "Assets"))) root = Directory.GetParent(root)!.FullName;
CompilationUnitSyntax Read(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path))).GetCompilationUnitRoot();
var minimap = Read("Assets/Script/Player/MinimapUnlockController.cs");
var game = Read("Assets/Script/GameManager.cs");
var challenge = Read("Assets/Script/GameManager.ChallengeBuild.cs");
MethodDeclarationSyntax Method(CompilationUnitSyntax tree, string name) => tree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name);
string Calls(InvocationExpressionSyntax call) => call.Expression.ToString();
bool IsRefresh(StatementSyntax statement) => statement is ExpressionStatementSyntax expression && expression.Expression is InvocationExpressionSyntax call && Calls(call) == "MinimapUnlockController.RefreshAll";
bool IsState(StatementSyntax statement) => statement is ExpressionStatementSyntax expression && expression.Expression is AssignmentExpressionSyntax assignment && assignment.Left.ToString() == "CurrentState";
bool IsCapturedUI(StatementSyntax statement) => statement is ForEachStatementSyntax loop && loop.Expression.ToString() == "uiStateBeforeBuildMode";
bool IsCaptureCall(StatementSyntax statement) => statement is ExpressionStatementSyntax expression && expression.Expression is InvocationExpressionSyntax call && Calls(call) is "CaptureAndHide" or "RestoreCapturedStates" && call.ArgumentList.Arguments.Any(a => a.ToString() == "uiStateBeforeBuildMode");
var transitions = new[] {
    (Tree:game, Method:"EnterBuildModeRoutine", Wrapper:"EnterBuild", Event:"OnEnterBuildMode"),
    (Tree:game, Method:"ExitBuildModeRoutine", Wrapper:"ExitBuild", Event:"OnExitBuildMode"),
    (Tree:game, Method:"EnterCargoTestView", Wrapper:"EnterCargoTest", Event:""),
    (Tree:game, Method:"RestoreCargoBuildView", Wrapper:"RestoreCargoBuild", Event:""),
    (Tree:challenge, Method:"EnterSessionChallengeBuild", Wrapper:"EnterChallenge", Event:"OnEnterBuildMode"),
    (Tree:challenge, Method:"ExitSessionChallengeBuild", Wrapper:"ExitChallenge", Event:"OnExitBuildMode")
};
int guards = 0;
void Guard(bool passed, string reason) { guards++; if (!passed) throw new Exception(reason); }
var wrappers = new List<string>();
foreach (var transition in transitions)
{
    var method = Method(transition.Tree, transition.Method);
    var ordered = method.DescendantNodes().OfType<StatementSyntax>().Where(s => IsState(s) || IsRefresh(s) || IsCapturedUI(s) || IsCaptureCall(s)).OrderBy(s => s.SpanStart).ToList();
    var refresh = ordered.Single(IsRefresh);
    var ui = ordered.Single(s => IsCapturedUI(s) || IsCaptureCall(s));
    Guard(ordered.Single(IsState).SpanStart < ui.SpanStart && ui.SpanStart < refresh.SpanStart,
        transition.Method + ": set state, capture/restore UI, then refresh map camera");
    Guard(!method.DescendantNodes().OfType<YieldStatementSyntax>().Any(y => y.SpanStart > ui.SpanStart && y.SpanStart < refresh.SpanStart),
        transition.Method + ": no rendered frame between changing UI and camera visibility");
    if (transition.Event.Length > 0)
    {
        var notify = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(c => Calls(c) == "InvokeEventSafely" && c.ArgumentList.Arguments.Any(a => a.ToString() == transition.Event));
        Guard(refresh.SpanStart < notify.SpanStart, transition.Method + ": subscribers observe synchronized camera visibility");
    }
    wrappers.Add("public void " + transition.Wrapper + "() {\n" + string.Join("\n", ordered.Select(s => s.ToFullString())) + "\n}");
}
string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("/*MINIMAP*/", string.Join("\n", new[] { "RefreshVisibility", "GetFeatureId", "RefreshAll" }.Select(n => Method(minimap, n).ToFullString())))
    .Replace("/*SAVE_UI*/", Method(game, "CaptureAndHide").ToFullString() + Method(game, "RestoreCapturedStates").ToFullString())
    .Replace("/*IS_BUILDING*/", Method(game, "IsInBuildMode").ToFullString())
    .Replace("/*TRANSITIONS*/", string.Join("\n", wrappers));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
var trees = new[] { source, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt")) }.Select(s => CSharpSyntaxTree.ParseText(s));
var compilation = CSharpCompilation.Create("BuildMinimapChecks", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream();
var result = compilation.Emit(output);
if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics));
int cases = (int)Assembly.Load(output.ToArray()).GetType("Scenarios")!.GetMethod("Run")!.Invoke(null, null)!;
Console.WriteLine($"PASS: {cases} visibility/lifecycle checks and {guards} actual-source transition ordering guards.");
