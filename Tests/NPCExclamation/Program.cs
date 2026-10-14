using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int guards;

    private static void Main()
    {
        string root = FindRoot();
        string path = Path.Combine(root, "Assets/Script/Client/NPCProgressionManager.cs");
        SyntaxNode syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        ClassDeclarationSyntax manager = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "NPCProgressionManager");
        ClassDeclarationSyntax phase = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "NPCProgressionPhase");
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"));
        string scenarios = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"));
        string extracted = Property(manager, "ShouldShowExclamationMark") + Property(manager, "CurrentPhase") +
            Property(manager, "IsTravelling") + Method(manager, "CanRunLocalProgression") +
            Method(manager, "HasReachedPhaseDestination");
        ClassDeclarationSyntax graphic = ReadClass(root, "Assets/Script/UI/NPCExclamationGraphic.cs", "NPCExclamationGraphic");
        ClassDeclarationSyntax map = ReadClass(root, "Assets/Script/Player/ExpandedMinimapController.cs", "ExpandedMinimapController");
        ClassDeclarationSyntax reservations = ReadClass(root, "Assets/Script/UI/UIReservedRegionLayout.cs", "UIReservedRegionLayout");
        string mesh = string.Join("\n", graphic.Members.OfType<FieldDeclarationSyntax>().Select(node => node.ToFullString())) +
            Method(graphic, "Awake") + Method(graphic, "OnPopulateMesh") + Method(graphic, "Disc") + Method(graphic, "Quad");
        string projection = Method(map, "GetNPCMarkerCanvasSize") + Method(map, "IsFiniteNPCMapValue") + Method(map, "TryProjectNPCMapPoint");
        string hudOverlap = Method(reservations, "OverlapsVisibleReservation") + Method(reservations, "IsRenderable") +
            Method(reservations, "Expand") + Method(reservations, "Register") + Method(reservations, "Unregister");
        int checks = Execute(boundary.Replace("// ACTUAL_NPC_POLICY", extracted)
            .Replace("// ACTUAL_GRAPHIC", mesh).Replace("// ACTUAL_MAP_PROJECTION", projection)
            .Replace("// ACTUAL_HUD_RESERVATIONS", hudOverlap) + scenarios);
        CheckLifecycle(manager, phase);
        CheckVisualBoundaries(root);
        Console.WriteLine($"PASS: {checks} actual-source eligibility/destination/geometry/HUD-reservation assertions and {guards} lifecycle/visual boundary guards. No Unity session, accounts, scenes, saves, or network access.");
    }

    private static void CheckLifecycle(ClassDeclarationSyntax manager, ClassDeclarationSyntax phase)
    {
        VariableDeclaratorSyntax field = phase.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(node => node.Declaration.Variables).Single(node => node.Identifier.ValueText == "showExclamationMark");
        Require(field.Initializer == null || field.Initializer.Value.IsKind(SyntaxKind.FalseLiteralExpression),
            "Existing phase records must default to markers off.");
        FieldDeclarationSyntax latch = manager.Members.OfType<FieldDeclarationSyntax>()
            .Single(node => node.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "phaseHasArrived"));
        Require(!latch.AttributeLists.ToString().Contains("SerializeField"), "Arrival must not become stale persisted/serialized state.");
        foreach (string name in new[] { "MoveToPhaseRoutine", "RepositionForCargoTurnIn", "AdvanceFromCargoTurnIn", "DebugWarpToPhase" })
        {
            string body = Method(manager, name);
            int clear = body.IndexOf("phaseHasArrived = false", StringComparison.Ordinal);
            Require(clear >= 0, name + " must clear old arrival state.");
            int transition = body.IndexOf("SaveProgressionState(", StringComparison.Ordinal);
            if (transition < 0) transition = body.IndexOf("PlaceAtPhase(", StringComparison.Ordinal);
            Require(transition < 0 || clear < transition, name + " must clear markers before committing/placing destination.");
        }
        string activate = Method(manager, "ActivatePhase");
        int set = activate.IndexOf("phaseHasArrived = HasReachedPhaseDestination(phaseIndex)", StringComparison.Ordinal);
        Require(set >= 0, "ActivatePhase must verify destination rather than blindly treating activation as arrival.");
        Require(set < activate.IndexOf("phase.InvokeNPCArrived", StringComparison.Ordinal), "Arrival events must see resolved marker eligibility.");
        string complete = Method(manager, "CompleteArrival");
        Require(complete.IndexOf("PlaceAtPhase(phaseIndex)", StringComparison.Ordinal) <
            complete.IndexOf("ActivatePhase(phaseIndex", StringComparison.Ordinal), "Normal movement must place before activating.");
        string failure = Method(manager, "HandleMovementFailure");
        Require(failure.Contains("if (PlaceAtPhase(phaseIndex))"), "Failed routes can count as arrival only after successful destination placement.");
        Require(failure.Contains("phaseHasArrived = HasReachedPhaseDestination(currentPhaseIndex)"), "Departure recovery must not leave a stranded-state latch.");
        string cargo = Method(manager, "RepositionForCargoTurnIn");
        Require(cargo.Contains("phaseHasArrived = HasReachedPhaseDestination(currentPhaseIndex)"), "Cargo relocation must verify current phase position.");
        Require(!Method(manager, "OnDisable").Contains("phaseHasArrived = true"), "Disabling cannot invent an arrival.");
        string start = Method(manager, "Start");
        Require(start.Contains("wasTravellingWhenSaved") && start.Contains("MoveToPhaseRoutine(destinationPhaseIndex)"),
            "Interrupted waypoint saves must still resume travel through its marker-clearing routine.");
    }

    private static void CheckVisualBoundaries(string root)
    {
        string visualPath = Path.Combine(root, "Assets/Script/Client/NPCExclamationMarker.cs");
        string visual = File.ReadAllText(visualPath);
        Require(visual.Contains("const float diameter = 32f"), "Overhead badge must keep the reduced fixed-pixel default.");
        Require(visual.Contains("ShouldShowExclamationMark"), "Overhead must use the same phase eligibility as the map.");
        ClassDeclarationSyntax overhead = ReadClass(root, "Assets/Script/Client/NPCExclamationMarker.cs", "NPCExclamationMarker");
        MethodDeclarationSyntax overheadUpdate = overhead.Members.OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "LateUpdate");
        IfStatementSyntax overlapGate = overheadUpdate.DescendantNodes().OfType<IfStatementSyntax>()
            .SingleOrDefault(node => node.Condition.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(call => call.Expression.ToString() == "UIReservedRegionLayout.OverlapsVisibleReservation"));
        Require(overlapGate != null && overlapGate.Statement.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any() &&
            overlapGate.Statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(call => call.Expression.ToString() == "Hide"),
            "The overhead HUD intersection gate must immediately hide and return, not move the marker or alter map rendering.");
        Require(overlapGate != null && overlapGate.SpanStart < overheadUpdate.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Single(node => node.Left.ToString() == "badge.anchoredPosition").SpanStart,
            "HUD overlap must be resolved before applying the overhead position or restoring its Canvas.");
        Require(Method(overhead, "LateUpdate").Contains("point.x - half, point.y - half") &&
            Method(overhead, "LateUpdate").Contains("point.x + half, point.y + half"),
            "Suppression must measure the entire physical badge, including edge/corner overlap outside its center.");
        Require(overlapGate != null && overlapGate.Condition.ToString().Contains(", 2f"), "Overhead must leave the HUD's small physical-pixel separation margin.");
        Require(visual.Contains("raycastTarget = false"), "Overhead graphics must never intercept interactions.");
        Require(!visual.Contains("AddComponent<Collider") && !visual.Contains("AddComponent<BoxCollider") &&
            !visual.Contains("AddComponent<SphereCollider") && !visual.Contains("AddComponent<CapsuleCollider") &&
            !visual.Contains("AddComponent<MeshCollider"), "Marker must not create a physics obstacle.");
        string map = File.ReadAllText(Path.Combine(root, "Assets/Script/Player/ExpandedMinimapController.cs"));
        Require(map.Contains("ShouldShowExclamationMark"), "Small and expanded maps must honor actual arrival gating.");
        Require(!map.Contains("OverlapsVisibleReservation("), "The map's own exclamation markers must remain visible inside the reserved minimap region.");
        Require(map.Contains("NPCExclamation"), "Map must maintain a dedicated NPC indicator instead of replacing contract markers.");
        Require(!map.Contains("AddComponent<Collider"), "Map overlay must not enter physics hit-testing.");
        Require(map.Contains("group.blocksRaycasts = false") && map.Contains("group.interactable = false"),
            "NPC map overlay must let existing map controls receive pointer input.");
        Require(map.Contains("raycastTarget = false"), "Map NPC graphics must not intercept pointer input.");
        string graphic = File.ReadAllText(Path.Combine(root, "Assets/Script/UI/NPCExclamationGraphic.cs"));
        Require(graphic.Contains("raycastTarget = false"), "Badge defaults to noninteractive in any reuse.");
    }

    private static int Execute(string source)
    {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("NPCExclamation_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        Assembly assembly = Assembly.Load(stream.ToArray());
        try { return (int)assembly.GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Actual NPC marker policy failed.", exception.InnerException); }
    }

    private static string Property(ClassDeclarationSyntax type, string name) => type.Members.OfType<PropertyDeclarationSyntax>()
        .Single(node => node.Identifier.ValueText == name).ToFullString();
    private static string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>()
        .Single(node => node.Identifier.ValueText == name).ToFullString();
    private static ClassDeclarationSyntax ReadClass(string root, string relative, string name) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, relative))).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == name);
    private static void Require(bool condition, string message)
    {
        guards++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/Client/NPCProgressionManager.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
