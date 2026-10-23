using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string root=Directory.GetCurrentDirectory();
while(!File.Exists(Path.Combine(root,"Assets/Script/Client/NPCProgressionManager.cs")))root=Directory.GetParent(root)?.FullName??throw new InvalidOperationException("Unity root not found.");
string npcSource=File.ReadAllText(Path.Combine(root,"Assets/Script/Client/NPCProgressionManager.cs"));
var npc=CSharpSyntaxTree.ParseText(npcSource).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node=>node.Identifier.ValueText=="NPCProgressionManager");
string Method(string name)=>npc.Members.OfType<MethodDeclarationSyntax>().Single(method=>method.Identifier.ValueText==name).ToFullString();
string[] fields={"travelReviewPhase","travelReviewDeparturePhase","travelReviewStep","travelReviewOwner","travelReviewPhaseIndex","travelReviewVersion","travelReviewSerial","travelReviewId","orderedLearningFlow","optionalSequenceVersion","runningOptionalSequencePhases","arrivalDialogueVersion","phaseHasArrived"};
string code=string.Join("\n",npc.Members.OfType<FieldDeclarationSyntax>().Where(field=>field.Declaration.Variables.Any(variable=>fields.Contains(variable.Identifier.ValueText))).Select(field=>field.ToFullString()))+
    string.Join("\n",new[]{"TryStartTravelAlmanacReview","StartTravelAlmanacReviewWhenReady","CanPreserveTravelAlmanacReview","IsTravelAlmanacReviewOwnerValid","IsTravelAlmanacReviewAuthorized","HasPendingTravelAlmanacReview","CancelTravelAlmanacReview","ClearTravelAlmanacReviewState","CancelOrderedOptionalSequence","CanRunLocalProgression","ActivatePhase","StartOrderedDialogueAfterArrival"}.Select(Method));
string boundary=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Boundary.cs.txt")).Replace("// ACTUAL_NPC",code);
string flow=File.ReadAllText(Path.Combine(root,"Assets/Script/Client/NPCOrderedLearningFlow.cs"));
var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
var compilation=CSharpCompilation.Create("TravelReview_"+Guid.NewGuid().ToString("N"),new[]{CSharpSyntaxTree.ParseText(boundary),CSharpSyntaxTree.ParseText(flow),CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Scenarios.cs.txt")))},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using MemoryStream output=new();var emitted=compilation.Emit(output);
if(!emitted.Success)throw new InvalidOperationException(string.Join("\n",emitted.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
int checks;
try{checks=(int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null,null);}
catch(TargetInvocationException error){throw new InvalidOperationException("Walking review checks failed.",error.InnerException);}
int guards=0;void Guard(bool okay,string label){guards++;if(!okay)throw new InvalidOperationException(label);}
string move=Method("MoveToPhaseRoutine");
Guard(move.IndexOf("CancelOrderedOptionalSequence",StringComparison.Ordinal)<move.IndexOf("TryStartTravelAlmanacReview",StringComparison.Ordinal),"Travel starts only after cancelling outgoing stage.");
Guard(Method("OnDisable").Contains("CancelOrderedOptionalSequence();"),"Disable must cancel owned travel review.");
string failure=Method("HandleMovementFailure");
Guard(failure.Contains("CancelTravelAlmanacReview();")&&failure.IndexOf("CancelTravelAlmanacReview();",StringComparison.Ordinal)<failure.IndexOf("SaveProgressionState(currentPhaseIndex, false)",StringComparison.Ordinal),
    "Unrecoverable failed placement must cancel travel review before restoring departure phase.");
Guard(Method("LateUpdate").Contains("!IsTravelAlmanacReviewAuthorized()")&&Method("LateUpdate").Contains("CancelTravelAlmanacReview()"),"Profile/phase invalidation must clear travel ownership.");
Guard(Method("HandleNPCInteracted").IndexOf("HasPendingTravelAlmanacReview",StringComparison.Ordinal)<Method("HandleNPCInteracted").IndexOf("arrivalDialogueVersion++",StringComparison.Ordinal),"Pending travel review must suppress manual tap without invalidating arrival.");
Console.WriteLine($"PASS: {checks} executed actual travel/arrival/cancel/owner + full ordered-flow assertions; {guards} production call-path guards. Boundary doubles only; no live Unity scenes/player saves/accounts/network modified.");
