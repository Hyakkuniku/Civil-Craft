using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "ProjectSettings/ProjectVersion.txt")))
    root = Directory.GetParent(root)?.FullName ?? throw new Exception("Unity project root not found.");
string Read(string path) => File.ReadAllText(Path.Combine(root, path));
int checks = 0;
void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }

using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline.json")));
foreach (JsonProperty asset in baseline.RootElement.GetProperty("PreservedAssets").EnumerateObject())
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, asset.Name)))) == asset.Value.GetString(),
        "Pier visual seating changed an authored material, physics value or imported visual asset: " + asset.Name);

string helper = Read("Assets/Script/BuildMode/2D/Bar.PierSeating.cs");
var syntax = CSharpSyntaxTree.ParseText(helper).GetRoot();
Check(!syntax.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), "The seating helper must parse.");
var type = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "Bar");
var methods = type.Members.OfType<MethodDeclarationSyntax>().GroupBy(method => method.Identifier.ValueText)
    .ToDictionary(group => group.Key, group => group.ToArray());
var selected = new HashSet<string>();
void Include(string name)
{
    if (!methods.ContainsKey(name) || !selected.Add(name)) return;
    foreach (var method in methods[name])
    {
        Check(method.Modifiers.Any(SyntaxKind.StaticKeyword), "Extracted seating math must remain independent of live scene state: " + name);
        foreach (var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            if (call.Expression is IdentifierNameSyntax identifier) Include(identifier.Identifier.ValueText);
    }
}
Include("RequiredPierCapSeatingDrop");
Include("PierSeatingDepthOverlaps");
Check(selected.Contains("RequiredPierCapSeatingDrop"), "Production seating constraints must be tested directly.");
Check(selected.Contains("PierSeatingDepthOverlaps"), "Actual production depth filtering must be exercised; projected aggregate bounds are insufficient.");
Check(methods["TryPierSeatingRoad"].Single().DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
    call.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "PierSeatingDepthOverlaps"),
    "The connected-road seating path must apply the tested depth filter to its real mesh parts.");
string geometry = string.Join("\n", type.Members.OfType<FieldDeclarationSyntax>()
    .Where(field => field.Modifiers.Any(SyntaxKind.ConstKeyword)).Select(field => field.ToFullString())) + "\n" +
    string.Join("\n", selected.SelectMany(name => methods[name]).Select(method => method.ToFullString()));
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt")).Replace("// PRODUCTION_GEOMETRY", geometry);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("PierRoadSeating_" + Guid.NewGuid().ToString("N"), new[] {
    CSharpSyntaxTree.ParseText(boundary),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream(); var result = compilation.Emit(output);
if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
try { checks += (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new Exception("Production pier seating geometry failed.", error.InnerException); }

foreach (string forbidden in new[] { "FindObjectsOfType", "FindObjectOfType", "FindObjectsByType", "Physics.Raycast",
    "startPoint.transform.position =", "endPoint.transform.position =", "maxCompression =", "massPerMeter =", "costPerMeter =", "isAnchor =" })
    Check(!helper.Contains(forbidden), "The cap-only visual fix must not search scenes, rewrite meshes or change structure: " + forbidden);
Check(!syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
    assignment.Left is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText is "sharedMesh" or "vertices"),
    "The transform-only cap fix cannot replace shared meshes or write mesh vertices.");
Check(!helper.Contains("Destroy("), "Seating must not destroy connected roads or structural objects.");
foreach (var method in selected.SelectMany(name => methods[name]))
    Check(!method.DescendantNodes().OfType<ArrayCreationExpressionSyntax>().Any() &&
        !method.DescendantNodes().OfType<ImplicitArrayCreationExpressionSyntax>().Any(), "Per-frame cap math must not allocate temporary corner arrays.");

string barSource = Read("Assets/Script/BuildMode/2D/Bar.cs");
var barType = CSharpSyntaxTree.ParseText(barSource).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
    .Single(value => value.Identifier.ValueText == "Bar");
Check(barType.Modifiers.Any(SyntaxKind.PartialKeyword), "Bar must include its shared seating implementation.");
string BarMethod(string name) => barType.Members.OfType<MethodDeclarationSyntax>().Single(value => value.Identifier.ValueText == name).ToFullString();
Check(BarMethod("LateUpdate").Contains("RefreshPierCapSeating"), "Existing and newly connected roads must refresh their pier cap without a scene search.");
Check(BarMethod("UpdateCreatingBar").Contains("RefreshPierCapSeating"), "Placement/copy previews must immediately apply the same cap fit.");
Check(Read("Assets/Script/HostWorldBridgeVisual.cs").Contains("visual.transform.localPosition = part.Pose.Position") &&
    Read("Assets/Script/HostWorldBridgeVisual.cs").Contains("visual.transform.localRotation = part.Pose.Rotation"),
    "The read-only host bridge still reconstructs cap transforms from the existing transport.");
Console.WriteLine($"PASS: {checks} production seating geometry, preservation and integration assertions. Actual imported mesh fitting/rendering is checked separately in Unity; no live scenes, physics state or saves changed.");
