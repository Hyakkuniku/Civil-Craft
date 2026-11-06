using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class Program
{
    static void Main()
    {
        DirectoryInfo root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Assets/Script/UI/MaterialToolbarOrdering.cs"))) root = root.Parent;
        if (root == null) throw new Exception("Project root not found.");
        ClassDeclarationSyntax button = Parse("Assets/Script/UI/MaterialButtonTrigger.cs", "MaterialButtonTrigger");
        ClassDeclarationSyntax controller = Parse("Assets/Script/BuildMode/2D/BuildUIController.cs", "BuildUIController");
        var property = button.Members.OfType<PropertyDeclarationSyntax>().Single(member => member.Identifier.ValueText == "IsAvailableForSelection");
        var cache = button.Members.OfType<MethodDeclarationSyntax>().Single(member => member.Identifier.ValueText == "GetToolbarAuthoredIndex");
        string fields = string.Join("\n", button.Members.OfType<FieldDeclarationSyntax>().Where(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText is "toolbarOrderCell" or "toolbarOrderParent" or "toolbarAuthoredIndex")));
        string refresh = controller.Members.OfType<MethodDeclarationSyntax>().Single(member => member.Identifier.ValueText == "RefreshAllMaterialButtons").ToFullString();
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_BUTTON", property.ToFullString() + fields + cache.ToFullString())
            .Replace("// ACTUAL_REFRESH", refresh);
        string utility = File.ReadAllText(Path.Combine(root.FullName, "Assets/Script/UI/MaterialToolbarOrdering.cs"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("MaterialToolbar_" + Guid.NewGuid().ToString("N"), new[] {
            CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(utility),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new(); var emitted = compilation.Emit(stream);
        if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(stream.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException error) { throw new Exception("Material toolbar ordering regression failed.", error.InnerException); }
        if (refresh.IndexOf("EvaluateMaterialRestriction()", StringComparison.Ordinal) > refresh.IndexOf("MaterialToolbarOrdering.Apply", StringComparison.Ordinal)) throw new Exception("Sort must use newly evaluated restrictions.");
        Console.WriteLine($"PASS: {checks} checks on full production toolbar sorter, availability/index cache and refresh hook; evaluation-order source guard passed. Unity hierarchy/UI are boundary doubles; no scenes or saves touched.");
        ClassDeclarationSyntax Parse(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, path))).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name);
    }
}
