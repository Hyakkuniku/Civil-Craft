using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Assets/Script/Client/NPCProgressionManager.cs")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Unity project not found.");
ClassDeclarationSyntax Read(string path, string name) => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path))).GetRoot()
    .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Identifier.ValueText == name);
string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>()
    .Single(method => method.Identifier.ValueText == name).ToFullString();
string Fields(ClassDeclarationSyntax type, params string[] names) => string.Join("\n", type.Members.OfType<FieldDeclarationSyntax>()
    .Where(field => field.Declaration.Variables.Any(variable => names.Contains(variable.Identifier.ValueText))).Select(field => field.ToFullString()));
ClassDeclarationSyntax npc = Read("Assets/Script/Client/NPCProgressionManager.cs", "NPCProgressionManager");
ClassDeclarationSyntax dialogue = Read("Assets/Script/Dialogue/DialogueManager.cs", "DialogueManager");
string[] methods = { "TryHandleLessonReviewInteraction", "GetQuizReminderSpeakerName", "LateUpdate", "CancelLessonReviewReminder",
    "HandleNPCInteracted", "CanRunLocalProgression", "CancelOrderedOptionalSequence" };
string source = Fields(npc, "lessonReviewReminderDialogueManager", "lessonReviewReminderCompletion", "lessonReviewReminderTrigger",
    "lessonReviewReminderPhase", "runningOptionalSequencePhases", "optionalSequenceVersion") +
    string.Join("\n", methods.Select(name => Method(npc, name)));
string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
    .Replace("// ACTUAL_NPC", source).Replace("// ACTUAL_CANCEL", Method(dialogue, "CancelDialogueIfOwned"));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("NPCAlmanacReminder_" + Guid.NewGuid().ToString("N"),
    new[] { CSharpSyntaxTree.ParseText(boundary), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))
        // Reflection does not fill optional C# arguments. Preserve ordinary
        // cancellation behavior by explicitly passing the new default false.
        .Replace("\"CancelOrderedOptionalSequence\");", "\"CancelOrderedOptionalSequence\", false);")) },
    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using MemoryStream result = new();
var emitted = compilation.Emit(result);
if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
int checks;
try { checks = (int)Assembly.Load(result.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new InvalidOperationException("NPC Almanac reminder failed.", error.InnerException); }
int guards = 0;
void Guard(bool value, string message) { guards++; if (!value) throw new InvalidOperationException(message); }
string interact = Method(npc, "HandleNPCInteracted");
Guard(interact.IndexOf("TryHandleLessonReviewInteraction", StringComparison.Ordinal) < interact.IndexOf("phase.InvokeNPCInteracted", StringComparison.Ordinal),
    "Book reminder must claim interaction before phase events.");
Guard(Method(npc, "OnDisable").Contains("CancelOrderedOptionalSequence();", StringComparison.Ordinal),
    "Production disable must route through tested cancellation.");
string reminder = Method(npc, "TryHandleLessonReviewInteraction");
foreach (string prohibited in new[] { "OpenAlmanac(", "MoveToPhase(", "SaveGame(", "InvokeDialogueFinished", "MarkPhaseDialogueCompleted", "GrantConfiguredPhaseFeature", "PlayOrderedOptionalStep" })
    Guard(!reminder.Contains(prohibited, StringComparison.Ordinal), "Reminder must not itself open/save/advance/reward: " + prohibited);
Console.WriteLine($"PASS: {checks} executed production NPC Almanac-reminder/interaction/cancel checks plus {guards} source guards. LessonTrigger ownership/resume/content are explicit boundary doubles; no Unity scenes, player saves, accounts, network, or story data changed.");
