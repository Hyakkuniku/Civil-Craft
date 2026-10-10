using System;
using System.IO;

/// <summary>Pure local output validation, shared by the build entry point and no-network tests.</summary>
public static class WalletAcceptanceBuildPath
{
    public static string Validate(string projectPath, string requestedOutput)
    {
        if (string.IsNullOrWhiteSpace(requestedOutput) || !Path.IsPathRooted(requestedOutput) ||
            string.Equals(Path.GetPathRoot(requestedOutput), "\\", StringComparison.Ordinal) ||
            (requestedOutput.Length >= 2 && requestedOutput[1] == ':' && (requestedOutput.Length < 3 || (requestedOutput[2] != '\\' && requestedOutput[2] != '/'))))
            throw new InvalidOperationException("Acceptance APK output must be an absolute path.");
        string project = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string allowed = Path.GetFullPath(Path.Combine(project, "Builds", "WalletAcceptance"));
        string output = Path.GetFullPath(requestedOutput);
        if (!output.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(output).StartsWith("CivilCraft-wallet-acceptance-", StringComparison.Ordinal) ||
            !output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a new CivilCraft-wallet-acceptance-*.apk under this project's Builds/WalletAcceptance directory.");
        if (File.Exists(output) || Directory.Exists(output)) throw new InvalidOperationException("Acceptance APK output already exists; never overwrite an installer.");
        string parent = Path.GetDirectoryName(output);
        while (!string.Equals(parent, project, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Acceptance output directory may not traverse a junction or symbolic link.");
            parent = Path.GetDirectoryName(parent);
            if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException("Acceptance output left the project boundary.");
        }
        return output;
    }
}
