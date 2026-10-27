using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

string root=Directory.GetCurrentDirectory();
while(!File.Exists(Path.Combine(root,"Assets/Script/Client/ReedStoryIntroduction.cs")))
    root=Directory.GetParent(root)?.FullName??throw new InvalidOperationException("Unity root not found.");
string source=File.ReadAllText(Path.Combine(root,"Assets/Script/Client/ReedStoryIntroduction.cs"));
var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
var trees=new[]{source, File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Boundary.cs.txt")),File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Scenarios.cs.txt"))}.Select(text=>CSharpSyntaxTree.ParseText(text));
var compilation=CSharpCompilation.Create("ReedIntroduction_"+Guid.NewGuid().ToString("N"),trees,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output=new MemoryStream();var result=compilation.Emit(output);
if(!result.Success)throw new InvalidOperationException(string.Join("\n",result.Diagnostics.Where(value=>value.Severity==DiagnosticSeverity.Error)));
int checks;
try{checks=(int)Assembly.Load(output.ToArray()).GetType("Scenarios").GetMethod("Run").Invoke(null,null);}
catch(TargetInvocationException error){throw new InvalidOperationException("Reed introduction regression failed.",error.InnerException);}
int guards=0;void Guard(bool okay,string label){guards++;if(!okay)throw new InvalidOperationException(label);}
foreach(string forbidden in new[]{"FindObject", "SaveData(","SaveGame(","CompleteContract(","ClaimReward(","goldReward", "expReward", "phases.Add(","StopAllCoroutines(","cinematic.enabled ="})
    Guard(!source.Contains(forbidden),"No global search, unrelated cancellation, saved phase mutation or invented completion/reward: "+forbidden);
Guard(source.Contains("PlayContractOfferCinematic()")&&!source.Contains(".PlayCinematic()"),"Interrupted introductions can replay the existing cinematic independently of its one-shot flag.");
Console.WriteLine($"PASS: {checks} executed Reed introduction lifecycle and ownership assertions; {guards} production safety guards. Production helper compiled whole; UI, cinematic and coroutine-engine boundaries only.");
