using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static void Main()
    {
        string root = FindRoot();
        string managerSource = File.ReadAllText(Path.Combine(root, "Assets/Script/Player/PlayerSave/PlayerDataManager.cs"));
        ClassDeclarationSyntax manager = CSharpSyntaxTree.ParseText(managerSource).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "PlayerDataManager");
        string methods = string.Join("\n", new[] { "TryGetQuizProgress", "TrySaveQuizProgress", "TrySaveQuizAssessment" }.Select(name =>
            manager.Members.OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == name).ToFullString()));
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt")).Replace("// ACTUAL_SAVE_METHODS", methods);
        CSharpParseOptions options = new CSharpParseOptions().WithPreprocessorSymbols("UNITY_EDITOR");
        SyntaxTree[] trees = new[]
        {
            CSharpSyntaxTree.ParseText(boundary, options),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Quizzes/QuizData.cs")), options),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Quizzes/QuizProgress.cs")), options),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Quizzes/QuizProgressSaveData.cs")), options),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt")), options)
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("NPCQuizChecks_" + Guid.NewGuid().ToString("N"), trees,
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Quiz checks failed.", exception.InnerException); }
        MethodDeclarationSyntax normalize = manager.Members.OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "NormalizeLoadedData");
        if (!normalize.ToFullString().Contains("CurrentData.quizProgress == null") || !normalize.ToFullString().Contains("quizState.correctQuestionIds == null") ||
            !normalize.ToFullString().Contains("quizState.questionOrderIds == null") ||
            !normalize.ToFullString().Contains("quizState.choiceOrders == null") || !normalize.ToFullString().Contains("choiceOrder.choiceIndices == null"))
            throw new InvalidOperationException("Legacy save normalization is missing quiz defaults.");
        string dataSource = File.ReadAllText(Path.Combine(root, "Assets/Script/Player/PlayerSave/PlayerData.cs"));
        if (!dataSource.Contains("public List<QuizProgressSaveData> quizProgress = new List<QuizProgressSaveData>();"))
            throw new InvalidOperationException("PlayerData does not serialize quiz progress.");
        Console.WriteLine($"PASS: {checks + 3} assertions on real quiz data, content fingerprints, editor identity authoring, resume logic, and extracted production save methods. Boundary doubles only; no live saves, accounts, Unity session, or network accessed.");
    }

    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/UI/Quizzes/QuizData.cs"))) return path.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
