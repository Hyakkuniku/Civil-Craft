using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class CoroutineChecks
{
    public static int Run(string root)
    {
        string trigger = File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Lessons/BuildLocationLessonTrigger.cs"));
        var progression = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/Client/NPCProgressionManager.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "NPCProgressionManager");
        string methods = string.Join("\n", new[] { "StartOrderedDialogueAfterArrival", "CanRunLocalProgression" }.Select(name =>
            progression.Members.OfType<MethodDeclarationSyntax>().Single(value => value.Identifier.ValueText == name).ToFullString()));
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CoroutineBoundary.cs.txt"))
            .Replace("// ACTUAL_NPC_METHODS", methods);
        var trees = new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(trigger),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CoroutineScenarios.cs.txt"))) };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("TrussCoroutines_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var emitted = compilation.Emit(output);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)));
        try { return (int)Assembly.Load(output.ToArray()).GetType("CoroutineScenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException error) { throw new InvalidOperationException("Production coroutine regression failed.", error.InnerException); }
    }
}
