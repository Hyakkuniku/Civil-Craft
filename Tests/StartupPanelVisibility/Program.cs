using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int guards;
    private static void Main(string[] args)
    {
        string root = FindRoot();
        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
        ClassDeclarationSyntax Type(string relative, string name) => CSharpSyntaxTree.ParseText(Read(relative)).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == name);
        var complete = Type("Assets/Script/Level/LevelCompleteManager.cs", "LevelCompleteManager");
        var failed = Type("Assets/Script/Level/LevelFailedManager.cs", "LevelFailedManager");
        var tutorial = Type("Assets/Script/Level/Tutorial/TutorialManager.cs", "TutorialManager");
        string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>()
            .Single(value => value.Identifier.ValueText == name).ToFullString();
        string completeAwake = Method(complete, "Awake"), failedAwake = Method(failed, "Awake");
        string tutorialAwake = Method(tutorial, "Awake"), tutorialHide = Method(tutorial, "HideStartupPresentation");
        Check(completeAwake.IndexOf("levelCompletePanel.SetActive(false)", StringComparison.Ordinal) < completeAwake.IndexOf("Instance == null", StringComparison.Ordinal),
            "Completion hides its panel before rejecting an additive duplicate.");
        Check(failedAwake.IndexOf("levelFailedPanel.SetActive(false)", StringComparison.Ordinal) < failedAwake.IndexOf("Instance == null", StringComparison.Ordinal),
            "Failure hides its panel before rejecting an additive duplicate.");
        Check(tutorialAwake.IndexOf("HideStartupPresentation()", StringComparison.Ordinal) < tutorialAwake.IndexOf("Instance == null", StringComparison.Ordinal),
            "Tutorial hides presentation before rejecting an additive duplicate.");
        foreach (string body in new[] { completeAwake, failedAwake, tutorialAwake, tutorialHide })
            foreach (string forbidden in new[] { "ClosePanel(", "CloseAllPanels(", "CompleteTutorial(", "SkipTutorial(", "PlayTutorial(", "CompleteLesson(", "Save(", "SetPlayerInputEnable(", "SetLookEnabled(" })
                Check(!body.Contains(forbidden, StringComparison.Ordinal), "Startup visibility must not call progression, save or control-restoration actions: " + forbidden);
        Check(Method(complete, "TakeSnapshotAndShowUIRoutine").Contains("levelCompletePanel.SetActive(true)"), "Verified completion can still show its report.");
        Check(failed.Members.OfType<MethodDeclarationSyntax>().Any(value => value.ToFullString().Contains("levelFailedPanel.SetActive(true)")), "Verified failure can still show its report.");
        Check(Method(tutorial, "ShowTutorialStep").Contains(".SetActive(true)"), "A real tutorial step can still show its banner.");
        Check(!complete.Members.OfType<MethodDeclarationSyntax>().Any(value => value.Identifier.ValueText == "OnEnable") &&
            !failed.Members.OfType<MethodDeclarationSyntax>().Any(value => value.Identifier.ValueText == "OnEnable") &&
            !tutorial.Members.OfType<MethodDeclarationSyntax>().Any(value => value.Identifier.ValueText == "OnEnable"),
            "No unconditional OnEnable hide may interrupt legitimate reopening or nested Pause.");

        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_COMPLETE_AWAKE", completeAwake)
            .Replace("// ACTUAL_FAILED_AWAKE", failedAwake)
            .Replace("// ACTUAL_TUTORIAL_AWAKE", tutorialAwake + tutorialHide);
        var trees = new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("StartupPanels_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Startup panel scenario failed.", exception.InnerException); }
        bool runtimeOnly = args.Contains("--runtime-only", StringComparer.Ordinal);
        int serialized = runtimeOnly ? 0 : CheckSerializedAssets(root);
        string serializedResult = runtimeOnly ? "serialized checks deferred (--runtime-only)" : $"{serialized} serialized manager/panel invariants";
        Console.WriteLine($"PASS: {checks} production-source startup lifecycle assertions, {guards} safety guards; {serializedResult}. Engine callbacks/UI rendering are boundary doubles; no live scene, player save, account or network access.");
    }

    private static int CheckSerializedAssets(string root)
    {
        int assertions = 0;
        foreach (string relative in new[] { "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/CanyonCrossing.unity" })
        {
            var documents = ReadDocuments(File.ReadAllText(Path.Combine(root, relative)));
            foreach (var manager in new[] {
                new { Guid = "3311f20eb2c4ee449ace8becae8cf8c7", Panels = new[] { "levelCompletePanel" } },
                new { Guid = "0c743bf27bcffd649a370a512b3a45e6", Panels = new[] { "levelFailedPanel" } },
                new { Guid = "cbdcc7b7825c79c4d8972bd1206907dc", Panels = new[] { "centerPanel", "leftPanel", "nextButton", "skipButton" } } })
            {
                string[] owners = documents.Values.Where(value => value.Contains("guid: " + manager.Guid + ",", StringComparison.Ordinal)).ToArray();
                Verify(owners.Length == 1, relative + " has exactly one " + manager.Panels[0] + " manager.");
                string owner = owners.Single();
                string ownerId = Reference(owner, "m_GameObject");
                foreach (string field in manager.Panels)
                {
                    string id = Reference(owner, field);
                    if (id == "0" && (field == "nextButton" || field == "skipButton")) continue;
                    Verify(id != "0" && documents.ContainsKey(id), relative + " has an exact authored " + field + " reference.");
                    string panel = documents[id];
                    Verify(Regex.IsMatch(panel, @"(?m)^GameObject:\r?$"), relative + " " + field + " targets a GameObject, not a component.");
                    Verify(id != ownerId, relative + " " + field + " does not disable its own manager GameObject.");
                    Verify(Regex.IsMatch(panel, @"(?m)^  m_IsActive: 0\r?$"), relative + " " + field + " is serialized inactive.");
                }
            }
        }
        return assertions;
        void Verify(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); assertions++; }
    }

    private static Dictionary<string, string> ReadDocuments(string yaml)
    {
        MatchCollection headers = Regex.Matches(yaml, @"(?m)^--- !u!\d+ &(-?\d+)(?: stripped)?\r?$", RegexOptions.CultureInvariant);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < headers.Count; i++)
        {
            int end = i + 1 < headers.Count ? headers[i + 1].Index : yaml.Length;
            result[headers[i].Groups[1].Value] = yaml.Substring(headers[i].Index, end - headers[i].Index);
        }
        return result;
    }
    private static string Reference(string document, string field)
    {
        Match match = Regex.Match(document, @"(?m)^  " + Regex.Escape(field) + @": \{fileID: (-?\d+)\}\r?$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidOperationException("Missing serialized reference: " + field);
        return match.Groups[1].Value;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); guards++; }
    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/Level/LevelCompleteManager.cs"))) return path.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
