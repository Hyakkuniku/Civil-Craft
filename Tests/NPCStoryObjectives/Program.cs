using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/Client/NPCStoryObjectiveGuidance.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new Exception("Unity root not found.");
string Read(string path) => File.ReadAllText(Path.Combine(root, path));
string Class(string path, string name) => CSharpSyntaxTree.ParseText(Read(path)).GetRoot().DescendantNodes()
    .OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name).ToFullString();
string helper = Read("Assets/Script/Client/NPCStoryObjectiveGuidance.cs");
var trees = new[] {
    CSharpSyntaxTree.ParseText(helper),
    CSharpSyntaxTree.ParseText(Read("Assets/Script/UI/Objective-Missions/TutorialObjectiveHistory.cs")),
    CSharpSyntaxTree.ParseText(Read("Assets/Script/Level/Tutorial/TutorialInstructionText.cs")),
    CSharpSyntaxTree.ParseText(Class("Assets/Script/Player/PlayerSave/PlayerData.cs", "TrackedTask")),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) };
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("NPCStoryObjectiveChecks_" + Guid.NewGuid().ToString("N"), trees, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream(); var result = compilation.Emit(output);
if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new Exception("Story objective lifecycle failed.", error.InnerException); }
void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
foreach (string forbidden in new[] { ".StartTutorial(", ".PlayTutorial(", ".CompleteLesson(", ".CompleteContract(", ".UnlockFeature(",
    ".MoveToPhase", "FindObject", "Resources.", ".SetActive(", "OnDialogueFinished.Invoke", "onDialogueFinished.Invoke" })
    Check(!helper.Contains(forbidden), "Objective-only helper must not execute story/UI/search side effects: " + forbidden);
Check(helper.Split("manager.SaveGame()").Length == 2, "Changed objective batches have exactly one save call site.");
string ui = Read("Assets/Script/UI/Objective-Missions/ObjectiveTrackerUI.cs");
Check(ui.Contains("observedStoryObjectiveVersion != NPCStoryObjectiveGuidance.Version"), "Story changes refresh existing Objective UI.");
Check(ui.Contains("if (NPCStoryObjectiveGuidance.TryNavigate(currentlySelectedTask)) CloseTrackerPanel(false);"), "Tracking closes the panel only after successful exact-guide routing.");
Check(ui.Contains("return NPCStoryObjectiveGuidance.CanNavigate(task);"), "The normal navigation eligibility path understands story guides.");
int offer = ui.IndexOf("if (pendingContractOffer != null)", ui.IndexOf("public void OnNavigateButtonClicked"));
int story = ui.IndexOf("NPCStoryObjectiveGuidance.TryNavigate", ui.IndexOf("public void OnNavigateButtonClicked"));
Check(offer >= 0 && story > offer, "Contract confirmation retains priority over story navigation.");
var uiTree = CSharpSyntaxTree.ParseText(ui).GetRoot();
string complete = uiTree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "OnCompleteButtonClicked").ToFullString();
Check(!complete.Contains("NPCStoryObjectiveGuidance.TryNavigate") && complete.Contains("if (TutorialObjectiveHistory.IsSequenceTask(currentlySelectedTask)) return;"), "Completed/history actions cannot route or manually complete story tasks.");
Console.WriteLine($"PASS: {checks} whole production story-objective/history/schema assertions and UI integration guards. Unity mesh, saved-NPC, modal and pathfinder boundaries only; no live scene, progress, reward or network touched.");
