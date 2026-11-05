using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class Program
{
    static void Main()
    {
        DirectoryInfo root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Assets/Script/BuildMode/2D/Point.cs"))) root = root.Parent;
        if (root == null) throw new Exception("Project root not found.");
        ClassDeclarationSyntax point = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "Assets/Script/BuildMode/2D/Point.cs"))).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "Point");
        string Method(string name) => point.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToFullString();
        string Property(string name) => point.Members.OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == name).ToFullString();
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_POINT", Method("Update") + Property("IsScenePlacedAnchor") + Property("IsSessionAnchor"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("PointAnchorPlacement_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new(); var emitted = compilation.Emit(stream);
        if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(stream.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException error) { throw new Exception("Runtime anchor placement regression failed.", error.InnerException); }
        Console.WriteLine($"PASS: {checks} checks on actual Point.Update and anchor-identity properties. Boundary doubles only; no Unity scenes, gameplay, or player saves touched.");
    }
}
