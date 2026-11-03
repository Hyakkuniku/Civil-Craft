using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "ProjectSettings/ProjectVersion.txt"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Project root missing.");
var production = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/ChallengeBridgeTestPresentation.cs"))).GetCompilationUnitRoot();
ClassDeclarationSyntax Class(string name) => production.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(item => item.Identifier.Text == name);
var descriptor = Class("ChallengeTestDescriptor");
var descriptorMethods = new HashSet<string> { "GetVehicles", "Encode", "Decode", "ValidateVehicles" };
descriptor = descriptor.WithMembers(new SyntaxList<MemberDeclarationSyntax>(descriptor.Members.Where(item => item is not MethodDeclarationSyntax method || descriptorMethods.Contains(method.Identifier.Text))));
var codec = Class("ChallengeTestMotionCodec");
var codecMethods = new HashSet<string> { "TryGetWheelBody", "WriteVector", "WriteRotation", "ReadVector", "ReadRotation", "ValidateRotation", "Finite" };
codec = codec.WithMembers(new SyntaxList<MemberDeclarationSyntax>(codec.Members.Where(item => item is not MethodDeclarationSyntax method || codecMethods.Contains(method.Identifier.Text))));
var packetRoot = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/FusionChallengeTestSync.cs"))).GetCompilationUnitRoot();
var assembly = packetRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(item => item.Identifier.Text == "ChallengeTestPacketAssembly");
var worldRoot = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/FusionMultiplayerAvatar.LiveLoads.cs"))).GetCompilationUnitRoot();
var identityPolicy = worldRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(item => item.Identifier.Text == "MultipleRosterIdentityChanged");
string selected = "using System; using System.IO; using System.Text; using System.Collections.Generic; using UnityEngine;\n" +
    Class("ChallengeTestVehicle").ToFullString() + descriptor.ToFullString() + codec.ToFullString() + assembly.ToFullString() +
    "\npublic static class OrdinaryWorldPolicy { " + identityPolicy.ToFullString() + " }";
string boundaries = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"));
var trees = new[] { CSharpSyntaxTree.ParseText(selected), CSharpSyntaxTree.ParseText(boundaries),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/HostWorldBridgeSnapshot.cs"))),
    CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"))) };
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("MultiLoadTransport_" + Guid.NewGuid().ToString("N"), trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream(); var emitted = compilation.Emit(output);
if (!emitted.Success) throw new Exception(string.Join(Environment.NewLine, emitted.Diagnostics));
Assembly.Load(output.ToArray()).GetType("NetworkScenarios")!.GetMethod("Run")!.Invoke(null, null);
