using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/UI/ContractLiveLoadPresentation.cs"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Unity root not found.");
var type = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/ContractLiveLoadPresentation.cs"))).GetRoot()
    .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(item => item.Identifier.ValueText == "ContractLiveLoadPresentation");
string[] methods = { "ShortSummary", "Requirements", "SessionStatus", "ResultSummary", "PreviewWeight", "Label", "Escape" };
string production = "using System; using System.Text; using System.Collections.Generic; using UnityEngine; public static class ContractLiveLoadPresentation {\n" +
    string.Join("\n", type.Members.OfType<MethodDeclarationSyntax>().Where(item => methods.Contains(item.Identifier.ValueText))) + "\n}";
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("MultiLoadPresentation_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(production), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) },
    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var stream = new MemoryStream(); var emit = compilation.Emit(stream);
if (!emit.Success) throw new Exception(string.Join(Environment.NewLine, emit.Diagnostics));
try
{
    int checks = (int)Assembly.Load(stream.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null);
    Console.WriteLine("PASS: " + checks + " production multi-load formatter, per-vehicle weight, outcome, markup and legacy assertions. Unity rendering is covered separately by detached Editor previews.");
}
catch (TargetInvocationException error) { throw new Exception("Multi-load presentation regression failed.", error.InnerException); }
