using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "Assets/Script/UI/Objective-Missions/ObjectiveTrackerUI.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity root not found.");
ClassDeclarationSyntax Read(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path)))
    .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(item => item.Identifier.ValueText == name);
string Method(ClassDeclarationSyntax type, string name, int parameters = -1) => type.Members.OfType<MethodDeclarationSyntax>()
    .Single(item => item.Identifier.ValueText == name && (parameters < 0 || item.ParameterList.Parameters.Count == parameters)).ToFullString();
var tracker = Read("Assets/Script/UI/Objective-Missions/ObjectiveTrackerUI.cs", "ObjectiveTrackerUI");
var lesson = Read("Assets/Script/UI/Lessons/LessonUIManager.cs", "LessonUIManager");
var coordinator = Read("Assets/Script/UI/UIPanelCoordinator.cs", "UIPanelCoordinator");
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("// TRACKER_VISIBILITY", Method(tracker, "RefreshObjectiveEntryButton"))
    .Replace("// LESSON_CLOSE", Method(lesson, "CloseLesson") + Method(lesson, "RestoreHiddenCanvases"))
    .Replace("// MODAL_RESTORE", Method(coordinator, "ClosePanel", 2) + Method(coordinator, "ContainsPanel") +
        Method(coordinator, "RestoreStates") + Method(coordinator, "RestoreCanvasStates"));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("TutorialObjectiveVisibility_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) },
    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream();
var emitted = compilation.Emit(output);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
try
{
    int checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null);
    Console.WriteLine($"PASS: {checks} actual-source Objectives button/lesson-close/coordinator-restore checks. Tutorial registration/save, Canvas and HUD objects are boundary doubles. No live Unity, scenes, saves, or accounts touched.");
}
catch (TargetInvocationException error) { throw new InvalidOperationException("Tutorial Objectives visibility regression failed.", error.InnerException); }
