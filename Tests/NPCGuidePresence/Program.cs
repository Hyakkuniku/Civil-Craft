using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "Assets/Script/Level/PathGuider.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity root not found.");
string source = File.ReadAllText(Path.Combine(root, "Assets/Script/Level/PathGuider.cs"));
ClassDeclarationSyntax type = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
    .Single(item => item.Identifier.ValueText == "PathGuider");
MethodDeclarationSyntax Method(string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(item => item.Identifier.ValueText == name);
string[] fieldNames = { "currentlyTargetedWaypoint", "presenceCheckedTarget", "targetProgression", "targetGiver", "targetHadProgression", "targetHadGiver" };
string fields = string.Join("\n", type.Members.OfType<FieldDeclarationSyntax>().Where(item => item.Declaration.Variables.Any(value => fieldNames.Contains(value.Identifier.ValueText))));
string update = Method("Update").ToFullString(), gate = Method("IsNPCGuideTargetPresent").ToFullString();
if (update.IndexOf("IsNPCGuideTargetPresent(currentTarget)", StringComparison.Ordinal) >= update.IndexOf("Vector3.Distance", StringComparison.Ordinal))
    throw new InvalidOperationException("NPC presence must be checked before arrival.");
if (gate.Contains("FindObject") || gate.Contains("isVisible") || gate.Contains("Renderer"))
    throw new InvalidOperationException("NPC guidance must not scan the scene or use camera visibility.");
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("// ACTUAL_MEMBERS", fields + "\n" + update + "\n" + gate + "\n" + Method("CanRecallTutorialGuide"));
var trees = new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) };
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("NPCGuidePresence_" + Guid.NewGuid().ToString("N"), trees, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream();
var emitted = compilation.Emit(output);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
try
{
    int checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null);
    Console.WriteLine($"PASS: {checks} production PathGuider.Update/presence assertions and 2 source integration guards. Scene, navigation, visibility monitor and trail visuals are boundary doubles; no live scene or player data accessed.");
}
catch (TargetInvocationException error) { throw new InvalidOperationException("NPC guide presence regression failed.", error.InnerException); }
