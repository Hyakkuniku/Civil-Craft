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
        var pause = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/PauseManager.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "PauseManager");
        string Method(string name) => pause.Members.OfType<MethodDeclarationSyntax>()
            .Single(value => value.Identifier.ValueText == name).ToFullString();
        string awake = Method("Awake"), hide = Method("HideStartupPresentation");
        string authored = Method("FindAuthoredPauseButton"), find = Method("FindPauseButton");
        string create = Method("CreateMobilePauseButton"), start = Method("Start");
        Check(awake.IndexOf("HideStartupPresentation()", StringComparison.Ordinal) >= 0 &&
            awake.IndexOf("HideStartupPresentation()", StringComparison.Ordinal) < awake.IndexOf("Instance == null", StringComparison.Ordinal),
            "Every Pause manager hides its own presentation before singleton duplicate rejection.");
        foreach (string body in new[] { awake, hide, authored })
            foreach (string forbidden in new[] { "ResumeGame(", "FinishResumeGame(", "ClosePanel(", "CloseAllPanels(",
                "SetPlayerInputEnable(", "SetLookEnabled(", "Time.timeScale", "ResumeMusic(", "SaveGame(",
                "StartTutorial(", "PauseGame(", "CreateMobilePauseButton(", "FindPauseButton(" })
                Check(!body.Contains(forbidden, StringComparison.Ordinal), "Startup hiding must not invoke gameplay restoration, progression, or UI generation: " + forbidden);
        Check(hide.Contains("isPaused = false", StringComparison.Ordinal), "Startup cleanup clears a stale serialized local pause flag without running Resume.");
        Check(authored.Contains("GetPersistentTarget(i) == this", StringComparison.Ordinal) && authored.Contains("nameof(TogglePause)", StringComparison.Ordinal),
            "Authored discovery requires a persistent callback to this exact manager.");
        Check(authored.Contains("gameObject.scene", StringComparison.Ordinal), "Authored discovery is constrained to this manager's scene.");
        Check(find.Contains("FindAuthoredPauseButton()", StringComparison.Ordinal) && find.Contains("CreateMobilePauseButton()", StringComparison.Ordinal),
            "Normal availability retains a fallback only after trying authored discovery.");
        Check(create.Contains("gameObject.scene", StringComparison.Ordinal) && create.Contains("AccessButtons", StringComparison.Ordinal),
            "The normal fallback searches an AccessButtons container in the owning scene.");
        Check(start.Contains("RefreshPauseAvailability()", StringComparison.Ordinal), "The owning Start still applies normal HUD availability.");
        Check(!pause.Members.OfType<MethodDeclarationSyntax>().Any(value => value.Identifier.ValueText == "OnEnable"),
            "No unconditional OnEnable hide interrupts legitimate Pause reopening.");
        Check(Method("PauseGame").Contains("pausePanel.SetActive(true)", StringComparison.Ordinal), "Real Pause still opens the panel without a coordinator.");
        Check(Method("FinishResumeGame").Contains("pausePanel.SetActive(false)", StringComparison.Ordinal), "Real Resume still closes the panel.");
        Check(Method("FinishResumeGame").Contains("!DialogueManager.IsAnyDialogueActive && !QuizUIManager.IsAnyQuizActive", StringComparison.Ordinal),
            "Resume retains the dialogue and quiz input-lock restoration gate.");
        var authoring = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Editor/PausePanelUIAuthoring.cs")))
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "PausePanelUIAuthoring");
        string author = authoring.Members.OfType<MethodDeclarationSyntax>().Single(value => value.Identifier.ValueText == "Author").ToFullString();
        int hideBeforeSave = author.LastIndexOf("panel.SetActive(false)", StringComparison.Ordinal);
        int save = author.IndexOf("EditorSceneManager.SaveScene(", StringComparison.Ordinal);
        Check(hideBeforeSave >= 0 && save > hideBeforeSave, "Pause re-authoring always closes the styled modal before saving it.");

        string methods = string.Join("\n", new[] { awake, hide, authored, find, start, Method("RefreshPauseAvailability") });
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_PAUSE_METHODS", methods);
        var trees = new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("PauseStartup_" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException error) { throw new InvalidOperationException("Pause startup regression failed.", error.InnerException); }
        bool runtimeOnly = args.Contains("--runtime-only", StringComparer.Ordinal);
        int serialized = runtimeOnly ? 0 : CheckSerializedScenes(root);
        string saved = runtimeOnly ? "serialized checks deferred (--runtime-only)" : $"{serialized} saved-scene invariants";
        Console.WriteLine($"PASS: {checks} production-source pause startup assertions, {guards} source safety guards; {saved}. Unity objects/events are boundary doubles. No scene, save, account or network mutations.");
    }

    private static int CheckSerializedScenes(string root)
    {
        int checks = 0;
        foreach (string path in new[] { "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/CanyonCrossing.unity" })
        {
            var docs = ReadDocuments(File.ReadAllText(Path.Combine(root, path)));
            var managers = docs.Where(value => value.Value.Contains("guid: bb79f60c33f4e5b4fb2dd34033a0aa2b,", StringComparison.Ordinal)).ToArray();
            Verify(managers.Length == 1, path + " has one authored Pause manager.");
            var manager = managers.Single();
            string ownerId = Reference(manager.Value, "m_GameObject");
            string panelId = Reference(manager.Value, "pausePanel");
            Verify(panelId != "0" && docs.TryGetValue(panelId, out _), path + " has an exact Pause panel reference.");
            string panel = docs[panelId];
            Verify(Regex.IsMatch(panel, @"(?m)^GameObject:\r?$"), path + " Pause panel targets a GameObject.");
            Verify(panelId != ownerId, path + " startup hide cannot disable the manager's own GameObject.");
            Verify(Regex.IsMatch(panel, @"(?m)^  m_IsActive: 0\r?$"), path + " Pause panel is saved inactive.");
            Verify(Regex.IsMatch(manager.Value, @"(?m)^  isPaused: 0\r?$"), path + " starts without a saved paused state.");
            string callback = @"(?m)^      - m_Target: \{fileID: " + Regex.Escape(manager.Key) + @"\}\r?\n        m_TargetAssemblyTypeName: PauseManager, Assembly-CSharp\r?\n        m_MethodName: TogglePause\r?$";
            var buttons = docs.Values.Where(value => Regex.IsMatch(value, callback, RegexOptions.CultureInvariant)).ToArray();
            Verify(buttons.Length == 1, path + " has exactly one authored HUD button wired to this manager.");
            string buttonId = Reference(buttons.Single(), "m_GameObject");
            Verify(buttonId != "0" && docs.ContainsKey(buttonId) && buttonId != panelId && buttonId != ownerId,
                path + " HUD Pause is a separate local scene object.");
            Verify(Regex.IsMatch(docs[buttonId], @"(?m)^GameObject:\r?$"), path + " Pause button resolves to a local GameObject.");
            string assignedId = Reference(manager.Value, "pauseButton");
            Verify(assignedId == "0" || assignedId == buttonId, path + " assigned Pause button is absent for discovery or matches this manager's authored callback.");
            // A normal HUD Pause button is intentionally allowed to be active.
            // Only the modal and local paused state must start closed.
        }
        return checks;
        void Verify(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
    }

    private static Dictionary<string, string> ReadDocuments(string yaml)
    {
        var headers = Regex.Matches(yaml, @"(?m)^--- !u!\d+ &(-?\d+)(?: stripped)?\r?$", RegexOptions.CultureInvariant);
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
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); guards++; }
    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/PauseManager.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
