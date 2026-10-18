using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static void Main()
    {
        string root = FindRoot();
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"));
        string scenarios = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"));
        string actual = BuildSource(root, boundary, scenarios, false);
        int checks = Execute(actual, false);
        string previous = BuildSource(root, boundary, scenarios, true);
        int reproductions = Execute(previous, true);
        Console.WriteLine($"PASS: {checks} current-source assertions and {reproductions} prior-variant reproduction assertions. Actual extracted dialogue, pause, and input methods; boundary doubles only. No Unity session, account, network, or save access.");
    }

    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/Dialogue/DialogueManager.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }

    private static string BuildSource(string root, string boundary, string scenarios, bool previous)
    {
        ClassDeclarationSyntax dialogue = ReadClass(root, "Assets/Script/Dialogue/DialogueManager.cs", "DialogueManager");
        ClassDeclarationSyntax pause = ReadClass(root, "Assets/Script/PauseManager.cs", "PauseManager");
        ClassDeclarationSyntax input = ReadClass(root, "Assets/Script/InputManager.cs", "InputManager");
        string dialogueMethods = Methods(dialogue, "StartDialogue", "DisplayNextSentence", "TypeSentence", "EndDialogue", "SkipDialogue", "CancelDialogueIfOwned", "ResolvePlayerInput", "ResolvePlayerTransform");
        if (previous)
        {
            int calls = dialogueMethods.Split("ResolvePlayerInput();", StringSplitOptions.None).Length - 1;
            if (calls != 3) throw new InvalidOperationException($"Expected precisely three added resolver calls, found {calls}.");
            dialogueMethods = dialogueMethods.Replace("ResolvePlayerInput();", "/* Prior implementation: no eager input resolution. */");
        }
        return boundary.Replace("// DIALOGUE_METHODS", dialogueMethods + Properties(dialogue, "IsAnyDialogueActive", "IsPaused"))
            .Replace("// PAUSE_METHODS", Methods(pause, "PauseGame", "ResumeGame", "FinishResumeGame"))
            .Replace("// INPUT_METHODS", Methods(input, "EnsureInputInitialized", "SetPlayerInputEnable", "SetLookEnabled", "ReadMovementInput", "ReadLookInput") + Properties(input, "IsPlayerInputEnabled", "IsLookInputEnabled", "IsUsingMobileControls"))
            + scenarios;
    }

    private static ClassDeclarationSyntax ReadClass(string root, string relative, string name) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, relative))).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == name);

    private static string Methods(ClassDeclarationSyntax type, params string[] names) => string.Join("\n", names.Select(name =>
        type.Members.OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == name).ToFullString()));
    private static string Properties(ClassDeclarationSyntax type, params string[] names) => string.Join("\n", names.Select(name =>
        type.Members.OfType<PropertyDeclarationSyntax>().Single(node => node.Identifier.ValueText == name).ToFullString()));

    private static int Execute(string source, bool previous)
    {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("DialoguePauseControls_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream assemblyStream = new();
        var result = compilation.Emit(assemblyStream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        Assembly assembly = Assembly.Load(assemblyStream.ToArray());
        try { return (int)assembly.GetType("Scenarios").GetMethod("Run").Invoke(null, new object[] { previous }); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException(previous ? "Prior variant reproduction failed." : "Current implementation regression failed.", exception.InnerException); }
    }
}
