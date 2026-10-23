using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static void Main()
    {
        string root = FindRoot();
        ClassDeclarationSyntax quiz = ReadClass(root, "Assets/Script/UI/Quizzes/QuizUIManager.cs", "QuizUIManager");
        ClassDeclarationSyntax pause = ReadClass(root, "Assets/Script/PauseManager.cs", "PauseManager");
        ClassDeclarationSyntax save = ReadClass(root, "Assets/Script/Player/PlayerSave/PlayerDataManager.cs", "PlayerDataManager");
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_QUIZ_MEMBERS", string.Join("\n", quiz.Members.OfType<FieldDeclarationSyntax>().Select(member => member.ToFullString())) +
                string.Join("\n", quiz.Members.OfType<PropertyDeclarationSyntax>().Select(member => member.ToFullString())) +
                Methods(quiz, "Awake", "OnDisable", "OnDestroy", "Update", "CancelOwnedSession", "TryHandleBack", "Open", "CanInteract", "Select", "Submit", "Next", "Previous", "ShowAssessmentQuestion", "ShowAssessmentResult", "ShowFirstUnresolved", "Finish", "RetrySave", "CommitProgress", "QuestionAt", "Leave", "Pause", "CloseSession", "LockPlayerControls", "RestorePlayerControls", "IsProtectedVisit", "BindView", "BeginNpcPresentation", "UpdateNpcPresentation", "PresentNpcQuestion", "EndNpcPresentation"))
            .Replace("// ACTUAL_PAUSE_METHODS", Methods(pause, "PauseGame", "ResumeGame", "FinishResumeGame", "ResumeForQuizTeardown"))
            .Replace("// ACTUAL_SAVE_METHODS", Methods(save, "TryGetQuizProgress", "TrySaveQuizProgress", "TrySaveQuizAssessment"));
        SyntaxTree[] trees = new[]
        {
            CSharpSyntaxTree.ParseText(boundary),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Quizzes/QuizData.cs"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Quizzes/QuizProgress.cs"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Quizzes/QuizProgressSaveData.cs"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt")))
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("NPCQuizSession_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Quiz session regression failed.", exception.InnerException); }
        Console.WriteLine($"PASS: {checks} production-source quiz session/Pause/save assertions. Unity input, UI rendering, and durable save writer are boundary doubles; no live scene, player save, network, or account accessed.");
    }
    private static ClassDeclarationSyntax ReadClass(string root, string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path))).GetRoot()
        .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name);
    private static string Methods(ClassDeclarationSyntax type, params string[] names) => string.Join("\n", names.Select(name => type.Members.OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == name).ToFullString()));
    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/UI/Quizzes/QuizUIManager.cs"))) return path.FullName;
        throw new InvalidOperationException("Unity root not found.");
    }
}
