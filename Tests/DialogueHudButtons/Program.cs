using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "Assets/Script/UI/DialogueHudButtonGate.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new Exception("Unity root not found.");
ClassDeclarationSyntax Read(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path)))
    .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(item => item.Identifier.ValueText == name);
string Property(ClassDeclarationSyntax type, string name) => type.Members.OfType<PropertyDeclarationSyntax>()
    .Single(item => item.Identifier.ValueText == name).ToFullString();
string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>()
    .Single(item => item.Identifier.ValueText == name).ToFullString();
var gate = Read("Assets/Script/UI/DialogueHudButtonGate.cs", "DialogueHudButtonGate");
var almanac = Read("Assets/Script/UI/Almanac/AlmanacManager.cs", "AlmanacManager");
var objectives = Read("Assets/Script/UI/Objective-Missions/ObjectiveTrackerUI.cs", "ObjectiveTrackerUI");
var shop = Read("Assets/Script/UI/Shop/ShopButtonTrigger.cs", "ShopButtonTrigger");
var dialogue = Read("Assets/Script/Dialogue/DialogueManager.cs", "DialogueManager");
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("// ALMANAC_ELIGIBILITY", Property(almanac, "CanShowHudOpenButton"))
    .Replace("// OBJECTIVES_ELIGIBILITY", Property(objectives, "CanShowHudEntry"))
    .Replace("// SHOP_ELIGIBILITY", Property(shop, "CanShowHudEntry"))
    .Replace("// DIALOGUE_VISIBILITY", Property(dialogue, "IsAnyDialogueVisible") +
        Method(dialogue, "HideDialogueElements") + Method(dialogue, "RestoreDialogueElements"));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("DialogueHudButtons_" + Guid.NewGuid().ToString("N"), new[] {
    CSharpSyntaxTree.ParseText(boundary + "\n" + gate.ToFullString()),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream(); var result = compilation.Emit(output);
if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new Exception("Dialogue HUD mask regression failed.", error.InnerException); }
void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
string source = gate.ToFullString();
foreach (string forbidden in new[] { ".SetActive(", "SaveGame(", "CompleteLesson(", "CompleteContract(", "PlayTutorial(", "OpenPanel(", "FindObject", "Resources." })
    Check(!source.Contains(forbidden), "HUD presentation mask cannot activate objects, write progress, change tutorials, open panels, or search the scene: " + forbidden);
Check(Method(gate, "SetMask").Contains("AddComponent<CanvasGroup>()") && !Method(gate, "SetMask").Contains("GetComponent<CanvasGroup>()"),
    "The dialogue mask must own a separate CanvasGroup, leaving authored fade/lock settings untouched.");
foreach (string name in new[] { "OnEnable", "LateUpdate", "OnDisable", "OnDestroy" })
    Check(!string.IsNullOrEmpty(Method(gate, name)), "Gate lifecycle must cover activation, late refresh, disable and teardown.");
string start = Method(dialogue, "StartDialogue");
Check(start.IndexOf("isDialogueActive = true", StringComparison.Ordinal) < start.IndexOf("DialogueHudButtonGate.RefreshAll()", StringComparison.Ordinal),
    "Starting dialogue must hide the HUD synchronously after claiming dialogue ownership.");
string close = Method(dialogue, "HideDialogueBoxAfterClose");
Check(close.Contains("DialogueHudButtonGate.RefreshAll()") && close.IndexOf("dialogueBox.SetActive(false)", StringComparison.Ordinal) <
    close.IndexOf("DialogueHudButtonGate.RefreshAll()", StringComparison.Ordinal), "Closing animation must release the HUD only after its box disappears.");
string disable = Method(dialogue, "OnDisable");
Check(disable.Contains("DialogueHudButtonGate.RefreshAll()") && disable.IndexOf("isDialogueActive = false", StringComparison.Ordinal) <
    disable.IndexOf("DialogueHudButtonGate.RefreshAll()", StringComparison.Ordinal), "Disabled dialogue must release its own visibility before refreshing all HUD gates.");
Check(!Method(almanac, "OpenAlmanac").Contains("DialogueManager") && !Method(objectives, "ToggleTrackerPanel").Contains("DialogueManager"),
    "Programmatic book/objective opening must preserve its existing flow; the change is scoped to HUD visibility.");
Check(Method(shop, "Awake").Contains("DialogueHudButtonGate.BindShop(gameObject, this)"),
    "Every authored ShopButtonTrigger must bind its exact existing HUD object during Awake.");
Check(!Method(shop, "OpenShop").Contains("DialogueManager"),
    "Programmatic Shop opening remains available to its existing story flow; only the HUD entry is masked.");
Console.WriteLine($"PASS: {checks} production HUD mask/owner eligibility/dialogue-visibility checks and source guards. Unity lifecycle, CanvasGroup composition, dialogue transitions and modal snapshots are boundary inputs; no live UI, saves or scenes touched.");
