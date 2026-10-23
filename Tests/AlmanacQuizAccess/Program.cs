using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/UI/Almanac/AlmanacQuizAccess.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity project not found.");
var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Almanac/AlmanacQuizAccess.cs"))).GetRoot();
var access = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "AlmanacQuizAccess");
string[] names = { "TryOffer", "RequestOpenWaitingQuiz", "TryGetWaitingQuiz", "CancelOwned", "ProtectedVisit", "IsAuthorized", "Update", "OpenWaitingQuizWhenReady", "ClearWaitingQuizOpenRequest", "OnDisable", "OnDestroy", "Resolve", "StartQuiz", "Refresh", "ApplySelection", "RefreshTabContrast" };
string extracted = string.Join("\n", access.Members.OfType<FieldDeclarationSyntax>().Select(field => field.ToFullString())) +
    string.Join("\n", names.Select(name => access.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name).ToFullString()));
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt")).Replace("// ACTUAL_ACCESS", extracted);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("AlmanacQuizChecks_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using MemoryStream result = new();
var emitted = compilation.Emit(result);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(result.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("Almanac quiz lifecycle failed.", error.InnerException); }
Console.WriteLine($"PASS: {checks} checks on extracted production Almanac quiz offer/queued-tab/start/cancel callbacks. Panels, saves and Unity are isolated boundary doubles; no live globals, player saves, scenes or accounts touched.");
