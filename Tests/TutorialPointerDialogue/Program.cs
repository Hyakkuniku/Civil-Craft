using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialPointer.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity root not found.");
string source = File.ReadAllText(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialPointer.cs"));
var type = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
    .Single(item => item.Identifier.ValueText == "TutorialPointer");
string[] names = { "PointAt", "PointAtBuildTarget", "Hide", "OnEnable", "LateUpdate", "RefreshModalVisibility", "IsPointingAt" };
string actual = string.Join("\n", type.Members.OfType<FieldDeclarationSyntax>().Select(field => field.WithAttributeLists(default).ToFullString())) +
    type.Members.OfType<PropertyDeclarationSyntax>().Single(item => item.Identifier.ValueText == "IsSuppressedByModal").ToFullString() +
    string.Join("\n", type.Members.OfType<MethodDeclarationSyntax>().Where(item => names.Contains(item.Identifier.ValueText)));
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt")).Replace("// ACTUAL_MEMBERS", actual);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("TutorialPointerDialogue_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream(); var result = compilation.Emit(output);
if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("Pointer dialogue visibility failed.", error.InnerException); }
Console.WriteLine($"PASS: {checks} production TutorialPointer dialogue/modal/ownership assertions. Geometry and engine rendering are boundary doubles; no live scenes, callbacks or saves touched.");
