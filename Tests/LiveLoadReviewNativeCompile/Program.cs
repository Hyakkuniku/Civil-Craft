using System.Diagnostics;

// Reuse the Editor's exact source/reference/define list, but NEVER its output
// destinations. Both assemblies go to an isolated ignored Temp directory.
string root = FindRoot();
string compiler = "C:/Program Files/dotnet/sdk/8.0.423/Roslyn/bincore/csc.dll";
if (!File.Exists(compiler)) throw new FileNotFoundException("Update the local SDK compiler path.", compiler);
string response = Directory.GetFiles(Path.Combine(root, "Library/Bee/artifacts"), "Assembly-CSharp.rsp", SearchOption.AllDirectories)
    .Where(path => Path.GetFileName(Path.GetDirectoryName(path))!.EndsWith("E.dag"))
    .OrderByDescending(File.GetLastWriteTimeUtc).First();
string folder = Path.Combine(root, "Temp/LiveLoadReviewNativeCompile");
Directory.CreateDirectory(folder);
Compile(response, "Assembly-CSharp", "Assets/Script/Level/Tutorial/BuildTutorialPointerLayout.cs",
    "Assets/Script/Level/Tutorial/TutorialWarningLayout.cs", "Assets/Script/Level/Tutorial/TutorialInstructionText.cs",
    "Assets/Script/UI/Almanac/AlmanacCloseButtonStyle.cs", "Assets/Script/Client/NPCStoryHandoff.cs");
Compile(Path.Combine(Path.GetDirectoryName(response)!, "Assembly-CSharp-Editor.rsp"), "Assembly-CSharp-Editor",
    "Assets/Editor/LiveLoadReviewGuidanceValidation.cs", "Assets/Editor/BeamReviewRefinementAuthoring.cs", "Assets/Editor/BuildTutorialPointerValidation.cs",
    "Assets/Editor/BuildTutorialWarningValidation.cs", "Assets/Editor/TutorialInstructionCopyValidation.cs",
    "Assets/Editor/ShopUnlockPointerValidation.cs", "Assets/Editor/AlmanacCloseButtonValidation.cs",
    "Assets/Editor/AlmanacCloseButtonAuthoring.cs", "Assets/Editor/BhanLolaStoryRepair.cs");
Console.WriteLine("PASS: full runtime and Editor sources compile against Unity's actual references/defines. Only isolated Temp outputs were written; Unity assemblies and scenes were not changed.");

void Compile(string path, string name, params string[] additionalSources)
{
    string[] lines = File.ReadAllLines(path);
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(compiler);
    var arguments = new List<string>();
    foreach (string line in lines)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("-out:") || line.StartsWith("-refout:")) continue;
        string argument = line;
        if (name == "Assembly-CSharp-Editor" && line.StartsWith("-r:") &&
            (line.Contains("/Assembly-CSharp.dll") || line.Contains("/Assembly-CSharp.ref.dll")))
            argument = "-r:\"" + Path.Combine(folder, "Assembly-CSharp.dll") + "\"";
        arguments.Add(argument);
    }
    foreach (string additionalSource in additionalSources)
        if (!lines.Any(line => line.Replace("\"", "").Equals(additionalSource, StringComparison.OrdinalIgnoreCase)))
            arguments.Add("\"" + additionalSource + "\"");
    arguments.Add("-out:\"" + Path.Combine(folder, name + ".dll") + "\"");
    arguments.Add("-refout:\"" + Path.Combine(folder, name + ".ref.dll") + "\"");
    string detachedResponse = Path.Combine(folder, name + ".rsp");
    File.WriteAllLines(detachedResponse, arguments); // Generated test artifact only.
    start.ArgumentList.Add("@" + detachedResponse);
    using Process process = Process.Start(start)!;
    Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    string messages = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
    Console.Write(messages);
    if (process.ExitCode != 0) throw new InvalidOperationException(name + " native-reference compile failed: " + process.ExitCode);
}

static string FindRoot()
{
    for (DirectoryInfo? path = new(AppContext.BaseDirectory); path != null; path = path.Parent)
        if (File.Exists(Path.Combine(path.FullName, "ProjectSettings/ProjectVersion.txt"))) return path.FullName;
    throw new DirectoryNotFoundException("Unity project root not found.");
}
