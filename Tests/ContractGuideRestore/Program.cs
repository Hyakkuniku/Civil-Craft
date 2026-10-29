using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialManager.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity project not found.");
var type = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialManager.cs")))
    .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "TutorialManager");
string Method(string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(value => value.Identifier.ValueText == name).ToFullString();
string fields = string.Join("\n", type.Members.OfType<FieldDeclarationSyntax>().Where(field => field.Declaration.Variables.Any(variable =>
    variable.Identifier.ValueText.Contains("ContractGuide", StringComparison.OrdinalIgnoreCase) ||
    variable.Identifier.ValueText is "contractNavigationSteps" or "turnInGuideContract")).Select(value => value.ToFullString()));
string actual = fields + type.Members.OfType<PropertyDeclarationSyntax>().Single(value => value.Identifier.ValueText == "IsTutorialPresentationSuppressed").ToFullString() + string.Join("\n", new[]
{
    "GuideToContractLocation", "GuideToContractGiver", "SetContractLocationGuide", "SetContractGiverGuide", "RestoreContractGuideFromSave", "ResolveSavedNavigationTask", "SavedNavigationTaskKey",
    "SetContractGuideOwner", "ContractGuideBelongsToCurrentSave", "IsContractGuidePresentationBlocked", "SuspendContractGuide", "HideUntrackedContractGuide", "ClearContractGuide",
    "BindContractGuideRoute", "UpdateContractGuide", "InvalidateContractGuideRestoreCandidates", "GetRecallableStep", "RecallCurrentTutorialGuide"
}.Select(Method));
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt")).Replace("// ACTUAL_MANAGER", actual);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("ContractGuideRestore_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using MemoryStream result = new();
var emitted = compilation.Emit(result);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(result.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("Contract guide restore regression failed.", error.InnerException); }
int guards = 0;
void Guard(bool value, string message) { guards++; if (!value) throw new InvalidOperationException(message); }
foreach (string name in new[] { "RestoreContractGuideFromSave", "ResolveSavedNavigationTask", "UpdateContractGuide", "ClearContractGuide", "SetContractGuideOwner" })
    foreach (string forbidden in new[] { "SaveGame(", "TrySaveGame(", "CompleteContract(", "ShowNextStep(", "PlayTutorial(", "GrantConfigured", "goldReward", "expReward" })
        Guard(!Method(name).Contains(forbidden), "Guide recovery may not save, reward or advance progression: " + name + " / " + forbidden);
Guard(Method("Update").Contains("RestoreContractGuideFromSave()"), "Recovery must be wired into the runtime update, not an unused helper.");
Guard(Method("Start").Contains("hasStartedContractGuideRecovery = true"), "Startup must arm recovery after ordinary scene initialization.");
Guard(Method("Update").IndexOf("RestoreContractGuideFromSave()", StringComparison.Ordinal) < Method("Update").IndexOf("UpdateContractGuide()", StringComparison.Ordinal), "Recovery runs before the current navigation presentation refresh.");
Guard(!Method("Update").Contains("FindObjectsOfType"), "Ordinary frame updates cannot perform new scene searches.");
Guard(Method("RestoreContractGuideFromSave").Contains("if (contractGuideRestoreCandidates == null)") && Method("RestoreContractGuideFromSave").Contains("Time.unscaledTime < nextContractGuideRestoreTime"), "Recovery discovery is cached and guarded by the existing polling interval.");
Guard(Method("OnEnable").Contains("SceneManager.sceneLoaded += HandleContractGuideSceneLoaded") && Method("OnEnable").Contains("LevelCompleteManager.BridgeSavedAtLocation += HandleContractGuideBridgeSaved"), "Scene and bridge-save lifecycle notifications invalidate recovery candidates.");
foreach (string name in new[] { "OnDisable", "OnDestroy" })
    Guard(Method(name).Contains("SceneManager.sceneLoaded -= HandleContractGuideSceneLoaded") && Method(name).Contains("LevelCompleteManager.BridgeSavedAtLocation -= HandleContractGuideBridgeSaved") &&
        (Method(name).Contains("SuspendContractGuide()") || Method(name).Contains("ClearContractGuide()") && Method("ClearContractGuide").Contains("SuspendContractGuide()")), "Guide lifecycle releases subscriptions and owned presentation: " + name);
var giver = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/Client/NPCContractGIver.cs")))
    .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(value => value.Identifier.ValueText == "NPCContractGiver");
string GiverMethod(string name) => giver.Members.OfType<MethodDeclarationSyntax>().Single(value => value.Identifier.ValueText == name).ToFullString();
string giverGate = giver.Members.OfType<PropertyDeclarationSyntax>().Single(value => value.Identifier.ValueText == "CanRestoreContractGuide").ToFullString();
Guard(giverGate.Contains("IsInteractionAvailable") && giverGate.Contains("NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(transform)"), "Actual giver eligibility includes interaction/story and spawn-presence gates.");
Guard(giverGate.Contains("!isLocked") && giverGate.Contains("!isFullyTurnedIn") && giverGate.Contains("!isProgressionInteractionLocked") && giverGate.Contains("!isAwaitingContractDecision"), "Actual giver eligibility rejects locked/completed/moving/pending-offer NPCs.");
foreach (string name in new[] { "Start", "ConfigureProgressionPhase" })
    Guard(GiverMethod(name).Contains("InvalidateContractGuideRestoreCandidates()"), "Late NPC startup/phase restore notifies the recovery cache: " + name);
Console.WriteLine($"PASS: {checks} actual contract-guide recovery/navigation assertions plus {guards} integration/source guards. Scene discovery, durable save records, modal presentation and path rendering are boundary doubles; no live scene, save, account or network accessed.");
