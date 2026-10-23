using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/UI/Almanac/AlmanacManager.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity project not found.");
var type = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Almanac/AlmanacManager.cs")))
    .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "AlmanacManager");
string Method(string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == name).ToFullString();
string Property(string name) => type.Members.OfType<PropertyDeclarationSyntax>().Single(node => node.Identifier.ValueText == name).ToFullString();
string actual = string.Join("\n", new[] { Property("IsOpenAndReady"), Property("IsOpeningOrClosing"), Method("TrySelectQuizOfferCategory"), Method("IsAnyTabUnlockTutorialPlaying"), Method("SelectCategory") });
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt")).Replace("// ACTUAL_MANAGER", actual);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("AlmanacQuizEntry_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using MemoryStream result = new();
var emitted = compilation.Emit(result);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(result.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("Almanac quiz entry API failed.", error.InnerException); }
string entry = Method("TrySelectQuizOfferCategory");
foreach (string forbidden in new[] { "TryOpen(", "TrySave", "SaveGame", "Invoke(", "StartQuiz(", "MarkPhase" })
    if (entry.Contains(forbidden)) throw new InvalidOperationException("Selection API cannot start, save, advance, or bypass existing category routing: " + forbidden);
if (CSharpSyntaxTree.ParseText(entry).GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(node => node.Left.ToString() == "currentCategoryIndex"))
    throw new InvalidOperationException("Selection API cannot bypass the existing category route by writing its selected index directly.");
if (!entry.Contains("SelectCategory(index)")) throw new InvalidOperationException("Selection must use the existing category selection route.");
Console.WriteLine($"PASS: {checks} executed production Almanac quiz-selection/readiness checks plus 8 source guards. Engine-heavy category rendering is a boundary double; no scenes, profiles, saves, or UI assets changed.");
