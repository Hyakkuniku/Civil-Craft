using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/Client/NPCProgressionManager.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity project not found.");
ClassDeclarationSyntax Read(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path))).GetRoot()
    .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == name);
string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name).ToFullString();
string Fields(ClassDeclarationSyntax type, params string[] names) => string.Join("\n", type.Members.OfType<FieldDeclarationSyntax>()
    .Where(field => field.Declaration.Variables.Any(variable => names.Contains(variable.Identifier.ValueText))).Select(field => field.ToFullString()));
ClassDeclarationSyntax npc = Read("Assets/Script/Client/NPCProgressionManager.cs", "NPCProgressionManager");
ClassDeclarationSyntax access = Read("Assets/Script/UI/Almanac/AlmanacQuizAccess.cs", "AlmanacQuizAccess");
ClassDeclarationSyntax dialogue = Read("Assets/Script/Dialogue/DialogueManager.cs", "DialogueManager");
string npcCode = Fields(npc, "quizReminderDialogueManager", "quizReminderCompletion", "runningOptionalSequencePhases", "optionalSequenceVersion") +
    string.Join("\n", new[] { "TryGetWaitingQuizStep", "TryHandleWaitingQuizInteraction", "GetQuizReminderSpeakerName", "LateUpdate", "CancelQuizWaitingReminder", "StopOrderedOptionalSequence", "CancelOrderedOptionalSequence", "IsCurrentOrderedSequence", "CanRunLocalProgression" }.Select(name => Method(npc, name)));
string accessCode = Fields(access, "quiz", "context", "owner", "closed", "saveOwner", "saveManager", "starting") +
    string.Join("\n", new[] { "TryGetWaitingQuiz", "ProtectedVisit", "IsAuthorized" }.Select(name => Method(access, name)));
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("// ACTUAL_NPC", npcCode).Replace("// ACTUAL_ACCESS", accessCode).Replace("// ACTUAL_CANCEL", Method(dialogue, "CancelDialogueIfOwned"));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("NPCQuizReminderChecks_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using MemoryStream result = new();
var emitted = compilation.Emit(result);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(result.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("NPC quiz reminder failed.", error.InnerException); }
string interact = Method(npc, "HandleNPCInteracted");
if (interact.IndexOf("TryHandleWaitingQuizInteraction", StringComparison.Ordinal) > interact.IndexOf("phase.InvokeNPCInteracted", StringComparison.Ordinal) ||
    !interact.Contains("sender.SuppressCurrentInteractionForProgression()")) throw new InvalidOperationException("Reminder must claim interaction before story events/contract handling.");
string remind = Method(npc, "TryHandleWaitingQuizInteraction");
foreach (string prohibited in new[] { "TryOffer(", "TryOpen(", "InvokeDialogueFinished", "MarkPhaseDialogueCompleted", "GrantConfiguredPhaseFeature", "PlayOrderedOptionalStep" })
    if (remind.Contains(prohibited)) throw new InvalidOperationException("Reminder must not advance/open/reward: " + prohibited);
Console.WriteLine($"PASS: {checks} executed production reminder/query/ownership-cancel checks plus 7 source guards. Boundary doubles only; no Unity scenes, accounts, saves, network or story content changed.");
