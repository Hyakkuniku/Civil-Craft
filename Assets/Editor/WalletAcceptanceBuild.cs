#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Explicit local acceptance APK only. Never installs, uploads, publishes or changes player settings.</summary>
public static class WalletAcceptanceBuild
{
    public static void BuildAndroid()
    {
        if (!Application.isBatchMode)
            throw new BuildFailedException("Wallet acceptance builds require the explicit batch-mode command. Use a clean release checkout, not unsaved working scenes.");
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new BuildFailedException("Wait for Editor import/compilation and stop Play Mode or another build first.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new BuildFailedException("Start Unity with -buildTarget Android; this helper never switches targets or modifies settings.");
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP ||
            PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            throw new BuildFailedException("Saved Android settings must already be IL2CPP and ARM64 only. Configure and review them before building.");

        string[] args = Environment.GetCommandLineArgs();
        int option = Array.IndexOf(args, "-civilCraftWalletApk");
        if (option < 0 || option + 1 >= args.Length || Array.LastIndexOf(args, "-civilCraftWalletApk") != option)
            throw new BuildFailedException("Supply exactly one -civilCraftWalletApk absolute new output path.");
        string project = Directory.GetParent(Application.dataPath).FullName;
        string output = WalletAcceptanceBuildPath.Validate(project, args[option + 1]);
        string commit = VerifyCleanCheckout(project);
        WebsiteShopBuildValidation.ValidateSavedScenes();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        // Check again immediately before BuildPlayer. An existing/partial APK is
        // never reused or overwritten; every retry needs a new acceptance filename.
        if (File.Exists(output) || Directory.Exists(output))
            throw new BuildFailedException("The acceptance output already exists. Choose a new filename.");
        var scenes = Array.FindAll(EditorBuildSettings.scenes, scene => scene.enabled);
        var paths = Array.ConvertAll(scenes, scene => scene.path);
        Debug.Log("[Wallet acceptance build] LOCAL DEVELOPMENT TEST APK from verified commit " + commit + ". No installation, upload, payment, wallet activation or player-setting change is performed.");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = paths,
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        });
        if (report.summary.result != BuildResult.Succeeded || !File.Exists(output))
            throw new BuildFailedException("Local wallet acceptance APK failed: " + report.summary.result + ". Inspect the build log; do not publish a partial APK.");
        if (VerifyCleanCheckout(project) != commit)
            throw new BuildFailedException("Git commit changed during the build; do not use this APK for acceptance.");
        Debug.Log("[Wallet acceptance build] Created local DEVELOPMENT TEST APK: " + output +
            ". Device/browser acceptance is still required; nothing was installed or published.");
    }

    private static string VerifyCleanCheckout(string project)
    {
        var commit = RunGit(project, "rev-parse --verify HEAD");
        var tracked = RunGit(project, "diff --no-ext-diff --no-textconv --quiet HEAD -- Assets Packages ProjectSettings");
        // Intentionally include ignored files inside build inputs: an ignored
        // .cs/.dll/prefab can still affect Unity. Library/Builds/logs lie outside
        // these pathspecs, so normal generated build output remains permitted.
        var untracked = RunGit(project, "ls-files --others -- Assets Packages ProjectSettings");
        return WalletAcceptanceCheckoutPolicy.Verify(commit.Item1, commit.Item2, tracked.Item1, untracked.Item1, untracked.Item2);
    }

    private static Tuple<int, string> RunGit(string project, string arguments)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("git", arguments) {
                WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = System.Diagnostics.Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    throw new InvalidOperationException();
                }
                System.Threading.Tasks.Task.WaitAll(output, error);
                // Raw errors or filenames are not printed. A failed command is
                // enough to reject the build without exposing local contents.
                return Tuple.Create(process.ExitCode, output.Result);
            }
        }
        catch { throw new BuildFailedException("Read-only Git checkout verification failed; no acceptance build is allowed."); }
    }
}
#endif
