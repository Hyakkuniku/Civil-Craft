using System.Diagnostics;

// Compile with Unity's real saved response files, without opening the Editor,
// importing assets or writing to its live assemblies. Add new source files that
// have not yet been indexed by the Editor since this integration was authored.
string root = FindRoot();
string sdkRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk");
string compiler = Directory.GetDirectories(sdkRoot).Select(path => Path.Combine(path, "Roslyn", "bincore", "csc.dll"))
    .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).First();
string response = Directory.GetFiles(Path.Combine(root, "Library/Bee/artifacts"), "Assembly-CSharp.rsp", SearchOption.AllDirectories)
    .Where(path => Path.GetFileName(Path.GetDirectoryName(path))!.EndsWith("E.dag"))
    .OrderByDescending(File.GetLastWriteTimeUtc).First();
string output = Path.Combine(root, "Temp/GameWebsiteNativeCompile");
Directory.CreateDirectory(output);
Compile(response, "Assembly-CSharp", "Assets/Script");
Compile(Path.Combine(Path.GetDirectoryName(response)!, "Assembly-CSharp-Editor.rsp"), "Assembly-CSharp-Editor", "Assets/Editor");
string? androidResponse = Directory.GetFiles(Path.Combine(root, "Library/Bee/artifacts"), "Assembly-CSharp.rsp", SearchOption.AllDirectories)
    .Where(path => Path.GetFileName(Path.GetDirectoryName(path))!.EndsWith("P.dag") &&
        File.ReadAllText(path).Contains("UNITY_ANDROID"))
    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
if (androidResponse != null) Compile(androidResponse, "Assembly-CSharp-Android", "Assets/Script");
Console.WriteLine("PASS: full game and Editor source compile against Unity's actual references" +
    (androidResponse != null ? ", including the saved Android target" : "") +
    ". Only isolated Temp assemblies were generated; no APK was built.");

void Compile(string sourceResponse, string name, string newSourceDirectory)
{
    string[] lines = File.ReadAllLines(sourceResponse);
    var arguments = new List<string>();
    var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string line in lines)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("-out:") || line.StartsWith("-refout:")) continue;
        string argument = line;
        if (name.EndsWith("-Editor") && line.StartsWith("-r:") &&
            (line.Contains("/Assembly-CSharp.dll") || line.Contains("/Assembly-CSharp.ref.dll")))
            argument = "-r:\"" + Path.Combine(output, "Assembly-CSharp.dll") + "\"";
        arguments.Add(argument);
        string unquoted = line.Trim('"');
        if (unquoted.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            sources.Add(Path.GetFullPath(unquoted, root));
    }
    foreach (string file in Directory.GetFiles(Path.Combine(root, newSourceDirectory), "*.cs", SearchOption.AllDirectories))
        if (sources.Add(Path.GetFullPath(file))) arguments.Add("\"" + file + "\"");
    arguments.Add("-out:\"" + Path.Combine(output, name + ".dll") + "\"");
    arguments.Add("-refout:\"" + Path.Combine(output, name + ".ref.dll") + "\"");
    string detachedResponse = Path.Combine(output, name + ".rsp");
    File.WriteAllLines(detachedResponse, arguments); // Generated compiler artifact only.
    var start = new ProcessStartInfo("dotnet") {
        WorkingDirectory = root, UseShellExecute = false,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    start.ArgumentList.Add(compiler);
    start.ArgumentList.Add("@" + detachedResponse);
    using Process process = Process.Start(start)!;
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Console.Write(stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    if (process.ExitCode != 0) throw new InvalidOperationException(name + " failed: " + process.ExitCode);
}

static string FindRoot()
{
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "ProjectSettings/ProjectVersion.txt"))) return directory.FullName;
    throw new DirectoryNotFoundException("Run from the Unity project workspace.");
}
