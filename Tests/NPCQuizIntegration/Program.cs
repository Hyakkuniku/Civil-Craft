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
        SyntaxNode syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/Client/NPCProgressionManager.cs"))).GetRoot();
        ClassDeclarationSyntax manager = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "NPCProgressionManager");
        string Method(string name) => manager.Members.OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == name).ToFullString();
        string methods = string.Join("\n", new[] { "HasOrderedOptionalSequence", "PhaseContainsQuiz", "TryValidateOrderedQuizzes", "IsUsableDialogue" }.Select(Method));
        string step = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "NPCOptionalDialogueStep").ToFullString();
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("// ACTUAL_STEP_TYPE", step).Replace("// ACTUAL_NPC_METHODS", methods);
        SyntaxTree[] trees = new[]
        {
            CSharpSyntaxTree.ParseText(boundary),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Quizzes/QuizData.cs"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt")))
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("NPCQuizIntegration_" + Guid.NewGuid().ToString("N"), trees,
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int executed;
        try { executed = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("NPC quiz checks failed.", exception.InnerException); }
        Check(Method("ActivatePhase").Contains("IsUsableDialogue(phase.phaseDialogue) || HasOrderedOptionalSequence(phase)"), "Ordered-only phases need visible interaction prompts.");
        string start = Method("TryStartOrderedOptionalSequence");
        Check(start.IndexOf("TryValidateOrderedQuizzes", StringComparison.Ordinal) < start.IndexOf("PlayOrderedOptionalStep", StringComparison.Ordinal), "Validate quizzes before dialogue/material rewards.");
        Check(start.Contains("runningOptionalSequencePhases.Add(phaseIndex)"), "Repeated interaction must not open the same sequence twice.");
        Check(start.Contains("!PhaseContainsQuiz(phase)"), "Legacy material-once timing remains unchanged.");
        string play = Method("PlayOrderedOptionalStep");
        Check(play.Contains("if (!IsCurrentOrderedSequence"), "All async callbacks need generation/current-phase guards.");
        Check(play.Contains("step.stepType == NPCOptionalDialogueStep.StepType.Quiz"), "Quiz needs its own branch.");
        Check(play.Contains("AlmanacQuizAccess.TryOffer(step.quiz, context, this, onClosed)") &&
            !play.Contains("step.quiz.assessmentMode") && !play.Contains("QuizUIManager.TryOpen("),
            "All NPC practice and assessment steps use the same explicit Almanac Start Quiz entry.");
        Check(play.Contains("step.stepType != NPCOptionalDialogueStep.StepType.MaterialUnlock"), "Quiz/unknown enum values cannot fall into Material handling.");
        Check(play.Contains("outcome != QuizOutcome.Completed || !sameStep"), "Only successful matching quiz sessions may continue.");
        Check(play.Contains("step.quiz == expectedQuiz") && play.Contains("step.quizStepId, expectedStepId"), "Changed step assets/identities invalidate callbacks.");
        Check(play.Contains("if (!opened)") && play.Contains("StopOrderedOptionalSequence(phaseIndex, sequenceVersion)"), "Unavailable/busy sessions permit safe retry.");
        string finish = Method("FinishOrderedOptionalSequence");
        Check(finish.IndexOf("IsCurrentOrderedSequence", StringComparison.Ordinal) < finish.IndexOf("MarkPhaseDialogueCompleted", StringComparison.Ordinal), "Stale callbacks cannot mark completion.");
        Check(finish.Contains("isInvokingPhaseDialogueFinished = true;") && finish.Contains("finally"), "Ordered flows retain legacy self-target phase-event safety.");
        string stop = Method("StopOrderedOptionalSequence");
        Check(stop.Contains("runningOptionalSequencePhases.Remove(phaseIndex)") && !stop.Contains("MarkPhaseDialogueCompleted") && !stop.Contains("GrantConfiguredPhaseFeature"), "Leaving/unavailable must not grant completion rewards.");
        string cancel = Method("CancelOrderedOptionalSequence");
        Check(cancel.IndexOf("optionalSequenceVersion++", StringComparison.Ordinal) < cancel.IndexOf("CancelOwnedSession", StringComparison.Ordinal), "Cancel invalidates the callback before closing UI.");
        foreach (string method in new[] { "OnDisable", "MoveToPhaseRoutine", "ActivatePhase", "DebugWarpToPhase", "RepositionForCargoTurnIn", "AdvanceFromCargoTurnIn" })
            Check(CSharpSyntaxTree.ParseText("class CancellationProbe {" + Method(method) + "}").GetRoot()
                .DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                    call.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == "CancelOrderedOptionalSequence"),
                "Cancel sessions on " + method + ", including the travel-review-preserving overload.");
        string current = Method("IsCurrentOrderedSequence");
        Check(current.Contains("sequenceVersion == optionalSequenceVersion") && current.Contains("CurrentPhase == phase") && current.Contains("!IsTravelling"), "Generation, identity and travel all guard callbacks.");
        string local = Method("CanRunLocalProgression");
        Check(local.Contains("isActiveAndEnabled") && local.Contains("scene.IsValid()") && local.Contains("scene.isLoaded") && local.Contains("IsGuestInHostWorld"), "Visitors and inactive/unloaded scenes cannot run progression.");
        string interact = Method("HandleNPCInteracted");
        Check(interact.Contains("sender.HasAcceptedProgressionContract") && interact.Contains("PhaseContainsQuiz(phase)") && interact.Contains("!HasCompletedPhaseDialogue(currentPhaseIndex)"), "Accepted unfinished Quiz phases resume on interaction.");
        Check(interact.Contains("sender.SuppressCurrentInteractionForProgression()"), "Contract reminder/reward cannot overlap a retried quiz.");
        SyntaxNode giverSyntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/Client/NPCContractGIver.cs"))).GetRoot();
        ClassDeclarationSyntax giver = giverSyntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "NPCContractGiver");
        string GiverMethod(string name) => giver.Members.OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == name).ToFullString();
        Check(giver.Members.OfType<PropertyDeclarationSyntax>().Single(node => node.Identifier.ValueText == "HasAcceptedProgressionContract").ToFullString().Contains("hasGivenContract"), "Acceptance reads restored state, not a transient quiz flag.");
        string configure = GiverMethod("ConfigureProgressionPhase");
        Check(configure.Contains("isCompleted || hasSavedBridge || hasActiveQuest") && configure.Contains("hasGivenContract = true"), "Reloaded accepted quests can resume pending quizzes.");
        string intract = GiverMethod("Intract");
        Check(intract.IndexOf("progressionHandledCurrentInteraction = false", StringComparison.Ordinal) < intract.IndexOf("OnNPCInteracted?.Invoke", StringComparison.Ordinal), "Suppression resets for every new interaction.");
        Check(intract.IndexOf("if (progressionHandledCurrentInteraction", StringComparison.Ordinal) < intract.IndexOf("if (contractToGive == null)", StringComparison.Ordinal), "Claimed quiz taps cannot reach contract reminder/claim logic.");
        Console.WriteLine($"PASS: {executed} executed checks on extracted production quiz detection/validation and {structuralChecks} source guards for NPC sequence callbacks, cancellation, prompts, enum compatibility, and accepted-contract restart/retry. Boundary doubles only; no Unity session, scenes, saves, accounts, or network changed.");
    }

    private static string FindRoot()
    {
        for (DirectoryInfo path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Assets/Script/Client/NPCProgressionManager.cs"))) return path.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
