using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/Player/PlayerSave/PlayerData.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity project root not found.");
string Source(string path) => File.ReadAllText(Path.Combine(root, path));
var data = CSharpSyntaxTree.ParseText(Source("Assets/Script/Player/PlayerSave/PlayerData.cs")).GetRoot();
string task = data.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "TrackedTask").ToFullString();
var trees = new[]
{
    CSharpSyntaxTree.ParseText(task),
    CSharpSyntaxTree.ParseText(Source("Assets/Script/UI/Objective-Missions/TutorialObjectiveHistory.cs")),
    CSharpSyntaxTree.ParseText(Source("Assets/Script/Level/Tutorial/TutorialInstructionText.cs")),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt")))
};
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("TutorialHistory_" + Guid.NewGuid().ToString("N"), trees, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream();
var emitted = compilation.Emit(output);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("Tutorial objective history checks failed.", error.InnerException); }
Console.WriteLine($"PASS: {checks} assertions on complete production history service and actual TrackedTask schema. Unity ownership/scene boundaries only; no live saves/scenes/accounts/network changed.");
