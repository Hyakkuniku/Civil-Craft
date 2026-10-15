using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "Assets"))) root = Directory.GetParent(root)!.FullName;
string Source(string path) => File.ReadAllText(Path.Combine(root, path));
var contract = CSharpSyntaxTree.ParseText(Source("Assets/Script/Client/ContractSO.cs")).GetRoot()
    .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ContractSO");
var fields = new HashSet<string> { "cachedCargoProgression", "cachedCargoProgressionId", "cachedCargoProgressionSession", "cargoProgressionSession", "CargoResolveMarker", "CargoDialogueMarker" };
var methods = new HashSet<string> { "ResolveCargoProgression", "IsCargoInteractionUnlocked", "ResetCargoProgressionSession" };
string members = string.Join("\n", contract.Members.Where(m =>
    m is FieldDeclarationSyntax field && field.Declaration.Variables.Any(v => fields.Contains(v.Identifier.ValueText)) ||
    m is MethodDeclarationSyntax method && methods.Contains(method.Identifier.ValueText)).Select(m => m.ToFullString()));
string cargo = Source("Assets/Script/Interaction System/Interactables/CargoItem.cs");
var cargoClass = CSharpSyntaxTree.ParseText(cargo).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First();
string Method(string name) => cargoClass.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToFullString();
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("/*CONTRACT*/", members)
    .Replace("/*OUTLINE*/", Method("UpdatePickupOutline") + Method("DisposePickupOutline"));
var trees = new[] { boundary, Source("Assets/Script/CargoOutlineRefreshSchedule.cs"), File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt")) }
    .Select(s => CSharpSyntaxTree.ParseText(s));
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("CargoChecks", trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var stream = new MemoryStream();
var result = compilation.Emit(stream);
if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics));
int checks = (int)Assembly.Load(stream.ToArray()).GetType("Scenarios")!.GetMethod("Run")!.Invoke(null, null)!;
void Guard(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
Guard(cargo.Contains("base.IsInteractionAvailable && IsProgressionInteractionUnlocked &&") && cargo.Contains("!deliveryRestorePending && (!deliveredToLocation || HasAvailableStoryDestination()) &&") && cargo.Contains("!(isHeld && RestrictsFreeDrop)"), "All original availability predicates retained");
foreach (string name in new[] { "PickUp", "MountInVehicle", "PlaceAtStoryDropLocation", "FixedUpdate", "ReleaseHeldCargo" })
    Guard(!Method(name).Contains("outlineVisible") && !Method(name).Contains("outlineRefresh"), name + " must not use cached cosmetic authorization");
Guard(Method("UpdatePickupOutline").Contains("isHeld || deliveryRestorePending || loadedSlot != null || !base.IsInteractionAvailable"), "Hide held/restoring/loaded/visitor hints immediately");
Guard(Method("DisposePickupOutline").Contains("outlineRefresh.Reset()"), "Disable cleanup resets schedule");
Guard(Method("LateUpdate").Contains("transform.SetPositionAndRotation") && Method("FixedUpdate").Contains("AddForceAtPosition"), "Per-frame carry and physics preserved");
string development = Source("Assets/Script/Performance/DevelopmentPerformanceSettings.cs");
Guard(development.Contains("OcclusionOverride { get; private set; } = false") && development.Contains("OcclusionOverride = false;"), "Default and reset culling OFF");
Console.WriteLine($"PASS: {checks} actual-source cache, live-unlock, miss/respawn, scene/session, visual scheduling and gameplay safety checks.");
