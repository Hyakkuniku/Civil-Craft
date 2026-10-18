using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static void Main()
    {
        string root = FindRoot();
        ClassDeclarationSyntax Read(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path))).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name);
        var location = Read("Assets/Script/BuildMode/BuildLocation.cs", "BuildLocation");
        var failed = Read("Assets/Script/Level/LevelFailedManager.cs", "LevelFailedManager");
        string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name).ToFullString();
        var policy = location.Members.OfType<FieldDeclarationSyntax>().Single(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "restartGuidedTutorialOnFailure"));
        var property = location.Members.OfType<PropertyDeclarationSyntax>().Single(value => value.Identifier.ValueText == "RestartGuidedTutorialOnFailure");
        Require(policy.ToFullString().Contains("= true") && policy.ToFullString().Contains("SerializeField"), "Legacy locations must retain serialized restart-on-failure default true.");
        Require(!property.ToFullString().Contains("set;"), "Retry policy must be exposed read-only.");
        var restart = location.Members.OfType<MethodDeclarationSyntax>().Single(value => value.Identifier.ValueText == "RestartBuildTutorialAfterFailure");
        Require(restart.Body.Statements[0].ToString().Contains("if (!restartGuidedTutorialOnFailure) return false;"), "Disabled policy must return before every restart side effect.");
        string retry = Method(failed, "RetryLevel"), delayed = Method(failed, "RestartTutorialAfterRetry");
        Require(retry.Contains("CanRestartGuidedTutorialAfterRetry(restartLocation)") && delayed.Contains("CanRestartGuidedTutorialAfterRetry(restartLocation)"), "Both coroutine scheduling and delayed execution must recheck policy and ownership.");
        Require(Method(failed, "ShowFailScreen").Contains("activeLocation.RestartGuidedTutorialOnFailure"), "Disabled locations must not be captured as restart candidates.");
        Require(retry.Contains("physicsManager.StopPhysicsAndReset()") && retry.Contains("RestoreHiddenUI()") && retry.Contains("levelFailedPanel.SetActive(false)"), "Ordinary physics reset and failure UI recovery remain unchanged.");
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_LOCATION", policy.ToFullString() + property.ToFullString() + restart.ToFullString())
            .Replace("// ACTUAL_RETRY", retry + delayed + Method(failed, "CanRestartGuidedTutorialAfterRetry"));
        var trees = new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("TrussRetry_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Truss tutorial retry regression failed.", exception.InnerException); }
        Console.WriteLine($"PASS: {checks} production-source retry policy/lifecycle assertions and 6 integration guards. Physics, bridge graph, save writer, tutorials and coroutine scheduler are boundary doubles; no live scene/player save/account/network accessed.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/BuildMode/BuildLocation.cs"))) return path.FullName;
        throw new InvalidOperationException("Unity root not found.");
    }
}
