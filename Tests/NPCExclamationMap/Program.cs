using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static void Main()
    {
        string source = File.ReadAllText(Path.Combine(FindRoot(), "Assets/Script/Player/ExpandedMinimapController.cs"));
        ClassDeclarationSyntax controller = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == "ExpandedMinimapController");
        string Method(string name) => controller.Members.OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == name).ToFullString();
        string actual = string.Join("\n", new[] { "TryProjectNPCMapPoint", "GetNPCMarkerCanvasSize", "IsFiniteNPCMapValue" }
            .Select(Method));
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))
            .Replace("// ACTUAL_METHODS", actual);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("NPCExclamationMapChecks_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(boundary) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        int checks;
        try { checks = (int)Assembly.Load(output.ToArray()).GetType("MapScenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Map checks failed.", exception.InnerException); }
        void Guard(bool valid, string message) { checks++; if (!valid) throw new InvalidOperationException(message); }
        string update = Method("UpdateNPCExclamationMarkers");
        Guard(update.Contains("npc.ShouldShowExclamationMark"), "Map badges must use the phase/arrival property.");
        Guard(update.Contains("npc.gameObject.scene == gameObject.scene") && update.Contains("npc.isActiveAndEnabled"), "Map must filter inactive/foreign-scene NPCs.");
        Guard(update.Contains("WorldToViewportPoint(npc.transform.position)"), "NPC map positions must follow the current camera each frame.");
        Guard(update.Contains("mapImage.uvRect"), "Map projection must apply RawImage cropping/flipping.");
        Guard(update.Contains("Time.unscaledTime + 0.5f"), "Scene discovery must be throttled independently of time scale.");
        Guard(update.Contains("mapImage.isActiveAndEnabled") && update.Contains("minimapCamera.isActiveAndEnabled"), "Disabled/locked map cannot show badges.");
        IfStatementSyntax animationBranch = controller.Members.OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "LateUpdate").DescendantNodes().OfType<IfStatementSyntax>()
            .FirstOrDefault(node => node.Condition.ToString() == "isAnimating");
        Guard(animationBranch != null && animationBranch.Statement.DescendantNodes().OfType<InvocationExpressionSyntax>()
              .Any(node => node.Expression.ToString() == "UpdateNPCExclamationMarkers") &&
              animationBranch.Statement.DescendantNodes().OfType<ReturnStatementSyntax>().Any(),
              "The animation branch must refresh NPC badges before returning.");
        Guard(Method("UpdateMarkerPositions").Contains("UpdateNPCExclamationMarkers()"), "Animation projection calls must refresh badges too.");
        string create = Method("CreateNPCExclamationMapMarker");
        Guard(create.Contains("typeof(NPCExclamationGraphic)") && create.Contains("raycastTarget = false") && !create.Contains("typeof(Button)"), "Shared non-clickable graphic required.");
        string layer = Method("EnsureNPCExclamationLayer");
        Guard(layer.Contains("typeof(RectMask2D)") && layer.Contains("group.blocksRaycasts = false"), "Map badges must be clipped and not block map gestures.");
        Guard(layer.Contains("SetParent(mapImage.rectTransform") && layer.Contains("markerLayer.GetSiblingIndex()") &&
              layer.Contains("markerIndex + (iconIndex > markerIndex ? 1 : 0)"), "Dedicated map layer must draw directly above location/player labels.");
        Guard(Method("OnDisable").Contains("ClearNPCExclamationMarkers()") && Method("OnDestroy").Contains("ClearNPCExclamationMarkers()"), "Both disable/destroy must clean owned markers.");
        Guard(!Method("ClearNPCExclamationMarkers").Contains("Find") && !Method("DestroyOwnedNPCMarker").Contains("Find"), "Cleanup must never find/delete unrelated authored objects.");
        Guard(source.Contains("compactNPCMarkerPixels = 22f") && source.Contains("expandedNPCMarkerPixels = 28f"), "Compact readable fixed-pixel defaults required.");
        Console.WriteLine($"PASS: {checks} NPC map projection, crop/flip, off-screen rejection, physical-pixel sizing, and lifecycle/source guards. Actual production methods with engine arithmetic doubles; no Unity session, player saves, accounts, or network touched.");
    }

    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/Player/ExpandedMinimapController.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
