using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/UI/Almanac/AlmanacBookmarkLayout.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity project not found.");
string Source(string name) => File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Almanac/", name + ".cs"));
var motion = CSharpSyntaxTree.ParseText(Source("AlmanacBookMotion")).GetRoot().DescendantNodes()
    .OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "AlmanacBookMotion");
string layer = motion.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "LayerCoversBehindBookmarks").ToFullString();
var access = CSharpSyntaxTree.ParseText(Source("AlmanacQuizAccess")).GetRoot().DescendantNodes()
    .OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "AlmanacQuizAccess");
string contrast = access.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "RefreshTabContrast").ToFullString();
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("// ACTUAL_LAYER", layer).Replace("// ACTUAL_CONTRAST", contrast);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("AlmanacBookmarkChecks_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(Source("AlmanacBookmarkLayout")), CSharpSyntaxTree.ParseText(boundary),
        CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using MemoryStream result = new();
var emitted = compilation.Emit(result);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(result.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("Almanac bookmark checks failed.", error.InnerException); }
int guards = 0;
void Guard(bool success, string detail) { if (!success) throw new InvalidOperationException(detail); guards++; }
string build = access.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "BuildTab").ToFullString();
Guard(build.Contains("AlmanacBookmarkLayout.ConfigureQuizLabel(tabLabel)"), "Runtime-created Quiz label bypasses the shared layout.");
Guard(build.Contains("AlmanacBookmarkLayout.Reflow(book.categories)"), "Runtime-added Quiz bypasses bookmark stacking.");
string bind = motion.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Bind").ToFullString();
Guard(bind.Contains("LayerCoversBehindBookmarks();"), "Book binding does not split covers behind bookmarks.");
Guard(!layer.Contains("Destroy("), "Layer migration unexpectedly destroys content.");
Guard(!Source("AlmanacBookmarkLayout").Contains("FindObject"), "Bookmark layout introduced scene-wide searches.");
Guard(!Source("AlmanacBookmarkLayout").Contains("SaveGame"), "Bookmark layout modifies player progress.");
Console.WriteLine($"PASS: {checks} extracted-production bookmark/cover/contrast checks and {guards} integration guards. Unity transforms/text are isolated boundary doubles; no scenes, player saves, or live runtime globals touched. Native rendering/corner checks remain separate.");
