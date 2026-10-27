using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialManager.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity root not found.");
string Read(string path) => File.ReadAllText(Path.Combine(root, path));
ClassDeclarationSyntax Class(string path, string name) => CSharpSyntaxTree.ParseText(Read(path)).GetRoot()
    .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == name);
var manager = Class("Assets/Script/Level/Tutorial/TutorialManager.cs", "TutorialManager");
var ui = Class("Assets/Script/UI/Objective-Missions/ObjectiveTrackerUI.cs", "ObjectiveTrackerUI");
string Method(ClassDeclarationSyntax owner, string name) => owner.Members.OfType<MethodDeclarationSyntax>().Single(value => value.Identifier.ValueText == name).ToFullString();
string[] methods = { "RefreshTutorialObjectiveHistory", "BeginTutorialObjective", "OwnsTutorialObjective", "RecordTutorialObjectiveStep", "FinishTutorialObjective",
    "CompleteTutorial", "SuspendCurrentTutorial", "CancelTutorialWithoutCompletion", "IsPlayingSequence", "IsPlayingLesson", "CompleteTutorialIfPlaying" };
string[] fields = { "tutorialObjectiveOwners", "objectiveHistoryManager", "objectiveHistoryData" };
string managerCode = manager.Members.OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "TutorialObjectiveOwner").ToFullString() +
    string.Join("\n", manager.Members.OfType<FieldDeclarationSyntax>().Where(value => value.Declaration.Variables.Any(variable => fields.Contains(variable.Identifier.ValueText)))) +
    manager.Members.OfType<PropertyDeclarationSyntax>().Single(value => value.Identifier.ValueText == "MayRecordTutorialObjectives").ToFullString() +
    string.Join("\n", methods.Select(name => Method(manager, name)));
string[] uiMethods = { "IsCurrentSavedTutorialRow", "GetObjectiveKey", "SetActiveObjective", "AddGenericTask", "CompleteGenericTask", "OnCompleteButtonClicked", "CanNavigateToTask" };
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("// PRODUCTION_MANAGER", managerCode).Replace("// PRODUCTION_UI", string.Join("\n", uiMethods.Select(name => Method(ui, name))));
var trees = new[] {
    CSharpSyntaxTree.ParseText(Class("Assets/Script/Player/PlayerSave/PlayerData.cs", "TrackedTask").ToFullString()),
    CSharpSyntaxTree.ParseText(Read("Assets/Script/UI/Objective-Missions/TutorialObjectiveHistory.cs")),
    CSharpSyntaxTree.ParseText(Read("Assets/Script/Level/Tutorial/TutorialInstructionText.cs")),
    CSharpSyntaxTree.ParseText(Read("Assets/Script/UI/Objective-Missions/ObjectiveTabButton.cs")),
    CSharpSyntaxTree.ParseText(boundary),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) };
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("TutorialLifecycle_" + Guid.NewGuid().ToString("N"), trees, refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream(); var result = compilation.Emit(output);
if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("Lifecycle integration checks failed.", error.InnerException); }
int guards = 0;
void Guard(bool okay, string reason) { guards++; if (!okay) throw new InvalidOperationException(reason); }
string play = Method(manager, "PlayTutorial"), show = Method(manager, "ShowTutorialStep");
Guard(play.IndexOf("BeginTutorialObjective(sequence)", StringComparison.Ordinal) < play.IndexOf("ShowNextStep()", StringComparison.Ordinal), "Objective begin precedes the first tutorial step.");
Guard(show.Contains("RecordTutorialObjectiveStep(step)"), "Actual step presentation must update history.");
Guard(!Method(manager, "SuspendCurrentTutorial").Contains("FinishTutorialObjective") && !Method(manager, "CancelTutorialWithoutCompletion").Contains("FinishTutorialObjective"), "Suspension and cancellation never complete history.");
Guard(!Method(manager, "OnDisable").Contains("CompleteTutorial(") && !Method(manager, "OnDestroy").Contains("CompleteTutorial("), "Disable/teardown must not manufacture completion.");
Guard(Method(ui, "SelectTask").Contains("IsCurrentSavedTutorialRow(task)"), "Saved guide history remains selectable after runtime ownership expires.");
Guard(Method(ui, "RefreshQuestList").Contains("activeList.Contains(tutorialObjective.Task)"), "Live saved guide must not duplicate its row in the objective list.");
Console.WriteLine($"PASS: {checks} executed production manager lifecycle/UI guard/card assertions; {guards} production hook guards. Engine/UI/save transports are doubles; no live data changed.");
