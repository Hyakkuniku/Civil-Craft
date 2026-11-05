using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int structuralChecks;
    private static void Check(bool value, string message)
    {
        structuralChecks++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        string root = FindRoot();
        SyntaxNode source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/Client/NPCProgressionManager.cs"))).GetRoot();
        ClassDeclarationSyntax manager = source.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "NPCProgressionManager");
        string Method(string name) => manager.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToFullString();
        string[] extracted = { "HasCompletedPhaseDialogue", "FindSavedPhaseIndex", "FindPhaseIndexById", "MarkPhaseDialogueCompleted",
            "CanStartOptionalSequenceForContractState", "HandleOfferDialogueCompleted", "HandleNPCInteracted", "TryStartOrderedOptionalSequence",
            "FinishOrderedOptionalSequence", "StopOrderedOptionalSequence", "CancelOrderedOptionalSequence", "IsCurrentOrderedSequence",
            "HasOrderedOptionalSequence", "PhaseContainsQuiz", "IsUsableDialogue", "NeedsPhaseInteraction" };
        string step = source.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "NPCOptionalDialogueStep").ToFullString();
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_STEP", step).Replace("// ACTUAL_METHODS", string.Join("\n", extracted.Select(Method)));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("TurnInReview_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) },
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitted = compilation.Emit(stream);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int executed;
        try { executed = (int)Assembly.Load(stream.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Turn-in review checks failed.", exception.InnerException); }

        ClassDeclarationSyntax phase = source.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "NPCProgressionPhase");
        VariableDeclaratorSyntax flag = phase.Members.OfType<FieldDeclarationSyntax>().SelectMany(f => f.Declaration.Variables).Single(v => v.Identifier.ValueText == "runOptionalSequenceBeforeTurnIn");
        Check(flag.Initializer == null || flag.Initializer.Value.IsKind(SyntaxKind.FalseLiteralExpression), "Released phases must default to existing sequence timing.");
        string play = Method("PlayOrderedOptionalStep");
        Check(play.Contains("outcome != QuizOutcome.Completed || !sameStep") && play.Contains("StopOrderedOptionalSequence(phaseIndex, sequenceVersion)"), "Cancellation/failure must stop without marking review complete.");
        Check(!play.Contains("scoreThreshold") && !play.Contains("correctAnswers") && !play.Contains("passingScore"), "The turn-in flow must not add a passing-score gate.");
        foreach (string name in extracted)
            Check(!Method(name).Contains("ClaimReward(") && !Method(name).Contains("CompleteContract(") && !Method(name).Contains("ShowCompleteButton("), name + " must never award or collect the contract reward.");
        string finish = Method("FinishOrderedOptionalSequence");
        Check(finish.IndexOf("phase.contract != null && phase.runOptionalSequenceBeforeTurnIn", StringComparison.Ordinal) < finish.IndexOf("phase.InvokeDialogueFinished()", StringComparison.Ordinal), "Deferred review must stop before phase advancement events.");
        string giver = File.ReadAllText(Path.Combine(root, "Assets/Script/Client/NPCContractGIver.cs"));
        Check(giver.IndexOf("OnNPCInteracted?.Invoke(this)", StringComparison.Ordinal) < giver.IndexOf("if (isContractCompleted)", giver.IndexOf("protected override void Intract()", StringComparison.Ordinal), StringComparison.Ordinal), "Progression review must intercept before the normal completed-contract dialogue.");
        Check(giver.Contains("if (progressionHandledCurrentInteraction ||"), "Claiming an interaction must suppress the original giver path.");
        Console.WriteLine($"PASS: {executed} executed checks on extracted production review, interaction, offer, persistence, cancellation and attention methods; {structuralChecks} production-source guards. Boundary doubles only; no scenes, player saves, accounts or network changed.");
    }

    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/Client/NPCProgressionManager.cs"))) return path.FullName;
        throw new InvalidOperationException("Project root not found.");
    }
}
