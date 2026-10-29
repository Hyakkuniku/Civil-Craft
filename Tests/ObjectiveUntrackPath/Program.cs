using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/Level/PathGuider.cs"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Unity root not found.");
string Read(string path) => File.ReadAllText(Path.Combine(root, path));
var type = CSharpSyntaxTree.ParseText(Read("Assets/Script/Level/PathGuider.cs")).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(t => t.Identifier.ValueText == "PathGuider");
string Method(string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToFullString();
string[] fields = { "currentlyTargetedWaypoint", "presenceCheckedTarget", "targetProgression", "targetGiver", "targetHadProgression", "targetHadGiver" };
string actual = string.Join("\n", type.Members.OfType<FieldDeclarationSyntax>().Where(f => f.Declaration.Variables.Any(v => fields.Contains(v.Identifier.ValueText)))) +
    string.Join("\n", type.Members.OfType<PropertyDeclarationSyntax>().Where(p => p.Identifier.ValueText is "IsObjectiveNavigationHidden" or "HasPendingObjectiveGuide" or "CanUntrackObjectiveGuide")) +
    string.Join("\n", new[] { "Update", "IsNPCGuideTargetPresent", "CanRecallTutorialGuide", "RecallTutorialGuide", "RefreshFromPlayerPosition", "SetNewWaypoints", "RouteToSingleTarget", "HandleBridgeNavMeshUpdated", "UntrackObjectiveGuide", "IsProtectedGuest", "ResumeObjectiveNavigation" }.Select(Method));
string boundary = Read("Tests/NPCGuidePresence/Boundary.cs.txt")
    .Replace("public bool IsObjectiveNavigationHidden;", "")
    .Replace("public class GameObject : UnityObject\n", "public class GameObject : UnityObject\n")
    .Replace("public bool activeSelf = true;", "public Scene scene = Scene.Main; public bool activeSelf = true; public void SetActive(bool active) { activeSelf = active; }")
    .Replace("public class TutorialManager", "public partial class TutorialManager")
    .Replace("public class PathGuider\n", "public class PathGuider\n")
    .Replace("public Transform player;", "public static PathGuider Instance; public GameObject gameObject = new GameObject(); public bool isActiveAndEnabled = true; public PathGuider() { Instance = this; } public void BakeDone() { HandleBridgeNavMeshUpdated(); } public Transform player;")
    .Replace("// ACTUAL_MEMBERS", actual);
string extras = """
public struct Scene { public int id; public bool isLoaded; public bool IsValid() => id != 0; public static Scene Main => new Scene { id=1,isLoaded=true }; public static bool operator ==(Scene a,Scene b) => a.id==b.id; public static bool operator !=(Scene a,Scene b) => a.id!=b.id; public override bool Equals(object o) => o is Scene s && s==this; public override int GetHashCode()=>id; }
public class PlayerData { public bool objectiveNavigationHidden; }
public class PlayerDataManager { public static PlayerDataManager Instance; public PlayerData CurrentData = new PlayerData(); public int saves; public void SaveGame() { saves++; } }
public class FusionConnectionManager { public static FusionConnectionManager Instance; public bool IsGuestInHostWorld, IsGuestSaveProtected; }
""";
var managerType = CSharpSyntaxTree.ParseText(Read("Assets/Script/Level/Tutorial/TutorialManager.cs")).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(t => t.Identifier.ValueText == "TutorialManager");
string ManagerMethod(string name) => managerType.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name).ToFullString();
extras += "public partial class TutorialManager { public bool IsTutorialActive, contractGuideVisible; public GameObject leftPanel = new GameObject(); public int canvasRestores; private void RestoreTutorialCanvas() { canvasRestores++; } public void HideContractHint() { HideUntrackedContractGuide(); } " + ManagerMethod("HideUntrackedContractGuide") + "}";
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("ObjectiveUntrackPath_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(boundary + extras), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream(); var emitted = compilation.Emit(output);
if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new Exception("Untrack path regression failed.", error.InnerException); }
void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
Check(Method("Update").IndexOf("Vector3.Distance") < Method("Update").IndexOf("if (IsObjectiveNavigationHidden)"), "Hidden navigation must retain arrival handling.");
foreach (string mutation in new[] { "waypoints =", "currentWaypointIndex =", "ShowNextStep(", "SetNewWaypoints(" }) Check(!Method("UntrackObjectiveGuide").Contains(mutation), "Untrack may not mutate route/progression: " + mutation);
Check(!Method("SetNewWaypoints").Contains("ResumeObjectiveNavigation"), "Automatic routes must not reset the saved preference.");
Check(ManagerMethod("RestoreContractGuideFromSave").Contains("data.objectiveNavigationHidden") && ManagerMethod("RestoreContractGuideFromSave").IndexOf("data.objectiveNavigationHidden") < ManagerMethod("RestoreContractGuideFromSave").IndexOf("ResolveSavedNavigationTask(data)"), "Startup restore must not recreate a hidden contract hint.");
Check(ManagerMethod("UpdateContractGuide").IndexOf("HideUntrackedContractGuide()") < ManagerMethod("UpdateContractGuide").IndexOf("BindContractGuideRoute(guideWaypoints)"), "Existing hidden contract hints must stop before route binding.");
Check(!ManagerMethod("HideUntrackedContractGuide").Contains("SetNewWaypoints") && !ManagerMethod("HideUntrackedContractGuide").Contains("ClearContractGuide"), "Hiding the contract hint preserves route ownership.");
Console.WriteLine($"PASS: {checks} actual PathGuider untrack/arrival/recall/profile and contract-hint assertions. Rendering/navigation and save services are boundary doubles; no live scenes or player data accessed.");
