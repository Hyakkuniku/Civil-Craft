using System;

public enum PlayFabUsernameVerification { NotRequired, RequiresAccountInfo, Matches, Mismatch }

/// <summary>The submitted login identifier is immutable while PlayFab responds.</summary>
public sealed class PlayFabUsernameLoginPolicy
{
    public const string CapitalizationError =
        "Login Failed: Username is case-sensitive. Use the exact capitalization from registration.";
    public const string VerificationError =
        "Login Failed: Could not verify your username. Please try again.";

    public string Identifier { get; }
    public bool IsEmailLogin { get; }

    public PlayFabUsernameLoginPolicy(string identifier)
    {
        // Keep the established surrounding-whitespace handling, never fold case.
        Identifier = (identifier ?? string.Empty).Trim();
        IsEmailLogin = Identifier.Contains("@");
    }

    public PlayFabUsernameVerification Verify(string accountUsername)
    {
        if (IsEmailLogin) return PlayFabUsernameVerification.NotRequired;
        if (string.IsNullOrWhiteSpace(accountUsername)) return PlayFabUsernameVerification.RequiresAccountInfo;
        return string.Equals(Identifier, accountUsername, StringComparison.Ordinal)
            ? PlayFabUsernameVerification.Matches : PlayFabUsernameVerification.Mismatch;
    }
}
