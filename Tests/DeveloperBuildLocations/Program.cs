using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "Assets/Script/Debug")))
    root = Directory.GetParent(root)?.FullName ?? throw new Exception("Project root missing.");
string Methods(string file, params string[] names)
{
    var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, file))).GetRoot();
    return string.Join("\n", names.Select(name => syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == name).ToFullString()));
}
string dataMethods = Methods("Assets/Script/Player/PlayerSave/PlayerDataManager.cs", "DebugCompleteBuildLocations",
    "HasDeveloperBuildLocationCompletion", "IsContractCompleted", "HasContractCompletionRecord", "HasAnyCompletedContract", "IsContractLocked");
string teleport = Methods("Assets/Script/Debug/DeveloperDebugManager.cs", "TeleportPlayerToSelectedBuildLocation");
var schema = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/Player/PlayerSave/PlayerData.cs"))).GetRoot();
string savedField = schema.DescendantNodes().OfType<FieldDeclarationSyntax>().Single(field =>
    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "developerCompletedBuildLocations")).ToFullString();
string source = "using System; using System.Collections.Generic; using UnityEngine;\n" +
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt")) +
    "\npublic partial class PlayerData { " + savedField + " }\n" +
    "\npublic partial class PlayerDataManager { " + dataMethods + " }\n" +
    "\npublic partial class DeveloperDebugManager { " + teleport + " }\n" +
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Select(file => MetadataReference.CreateFromFile(file));
var compilation = CSharpCompilation.Create("DevBuildLocations_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(source) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream();
var result = compilation.Emit(output);
if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics.Where(row => row.Severity == DiagnosticSeverity.Error)));
try
{
    int count = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null);
    Console.WriteLine($"PASS: {count} actual developer completion/teleport assertions; no player files changed.");
}
catch (TargetInvocationException error) { throw error.InnerException ?? error; }
