using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class Program
{
    static void Main()
    {
        DirectoryInfo root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Assets/Script/BuildMode/BuildLocation.cs"))) root = root.Parent;
        if (root == null) throw new Exception("Project root not found.");
        var location = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Assets/Script/BuildMode/BuildLocation.cs"))).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "BuildLocation");
        string Method(string name) => location.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToFullString();
        var field = location.Members.OfType<FieldDeclarationSyntax>().Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "savedRoadAnchorVerticalTolerance"));
        string photo = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Assets/Script/Player/PlayerSave/PlayerDataManager.cs"))).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "DoesLoadedBridgeMatch").ToFullString();
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_FIELD", field.WithAttributeLists(default).ToFullString())
            .Replace("// ACTUAL_ANCHOR_METHODS", Method("MatchesSavedBridgePoint") + Method("FindMigratedSavedRoadAnchor"))
            .Replace("// ACTUAL_PHOTO_METHOD", photo);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("SavedRoadAnchor_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new(); var emitted = compilation.Emit(stream);
        if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int executed;
        try { executed = (int)Assembly.Load(stream.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException error) { throw new Exception("Saved-anchor compatibility regression failed.", error.InnerException); }
        string reconstruct = Method("ReconstructSavedBridge");
        int oldMatch = reconstruct.IndexOf("Vector3.Distance(anchor.transform.position, pos) < 0.1f", StringComparison.Ordinal);
        int fallback = reconstruct.IndexOf("if (matchedAnchor == null) matchedAnchor = FindMigratedSavedRoadAnchor(ptData);", StringComparison.Ordinal);
        int instantiate = reconstruct.IndexOf("Instantiate(creator.pointToInstantiate", StringComparison.Ordinal);
        if (!(oldMatch >= 0 && fallback > oldMatch && instantiate > fallback)) throw new Exception("Keep old strict matching before the migration fallback and before new node creation.");
        if (Method("FindMigratedSavedRoadAnchor").Contains("savedPoint.position =") || Method("MatchesSavedBridgePoint").Contains("SaveGame")) throw new Exception("Matching must never mutate a saved bridge.");
        Console.WriteLine($"PASS: {executed} checks on extracted production anchor compatibility and photo verification; strict reconstruction order and read-only guards passed. No scenes or saves changed.");
    }
}
