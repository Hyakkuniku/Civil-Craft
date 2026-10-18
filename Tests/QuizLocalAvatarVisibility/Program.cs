using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class Program
{
    private static void Main()
    {
        string root = FindRoot();
        string production = Path.Combine(root, "Assets/Script/UI/Quizzes/QuizLocalAvatarVisibility.cs");
        SyntaxTree[] trees = new[]
        {
            CSharpSyntaxTree.ParseText(File.ReadAllText(production)),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt")))
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("QuizLocalAvatarVisibility_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Local quiz-camera avatar visibility regression failed.", exception.InnerException); }
        Console.WriteLine($"PASS: {checks} production-source local-avatar visibility assertions. Render callbacks, scene objects and renderer flags are boundary doubles; no live scene, save, account, or network accessed.");
    }
    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/UI/Quizzes/QuizUIManager.cs"))) return path.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
