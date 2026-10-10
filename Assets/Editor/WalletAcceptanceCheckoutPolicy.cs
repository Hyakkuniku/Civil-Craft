using System;
using System.Text.RegularExpressions;

/// <summary>Fail-closed evaluation of read-only Git checks for a reproducible acceptance checkout.</summary>
public static class WalletAcceptanceCheckoutPolicy
{
    public static string Verify(int commitExitCode, string commitOutput, int trackedExitCode,
        int untrackedExitCode, string untrackedOutput)
    {
        string commit = (commitOutput ?? "").Trim();
        if (commitExitCode != 0 || !Regex.IsMatch(commit, @"\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z"))
            throw new InvalidOperationException("Acceptance builds require a verified committed Git checkout.");
        if (trackedExitCode != 0)
            throw new InvalidOperationException("Acceptance builds require committed, unchanged Assets, Packages and ProjectSettings. Never build unrelated working changes.");
        if (untrackedExitCode != 0 || !string.IsNullOrEmpty(untrackedOutput))
            throw new InvalidOperationException("Acceptance builds reject untracked Assets, Packages or ProjectSettings files and unverifiable Git state.");
        return commit.ToLowerInvariant();
    }
}
