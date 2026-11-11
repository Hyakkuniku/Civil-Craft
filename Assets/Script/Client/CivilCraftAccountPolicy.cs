using System;
using System.Text.RegularExpressions;

/// <summary>Matches the Civil Craft website's player sign-up and sign-in forms.</summary>
public static class CivilCraftAccountPolicy
{
    public const string TermsVersion = "2026-10-29";
    public const string PrivacyVersion = "2026-10-29";
    public const int MaximumEmailLength = 255;
    public const int MaximumPasswordLength = 128;

    private static readonly Regex Username = new Regex(@"\A[A-Za-z0-9_]{3,24}\z");
    private static readonly Regex Email = new Regex(
        @"\A(?!\.)(?!.*\.\.)([a-zA-Z0-9_'+.\-]*)[a-zA-Z0-9_+\-]@([a-zA-Z0-9][a-zA-Z0-9\-]*\.)+[a-zA-Z]{2,}\z");

    public static string TrimIdentifier(string value) =>
        (value ?? string.Empty).Trim().Trim('\uFEFF').Trim();

    public static bool IsEmailLogin(string identifier) => identifier.IndexOf('@') >= 0;

    public static bool IsEmail(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaximumEmailLength && Email.IsMatch(value);

    public enum Verification { Confirmed, Unconfirmed, Unavailable }

    public static Verification CheckVerification(long httpStatus, string expectedId, string receivedId,
        bool verified, string status)
    {
        if (httpStatus == 403) return Verification.Unconfirmed;
        if (httpStatus != 200 || string.IsNullOrEmpty(expectedId) ||
            !string.Equals(expectedId, receivedId, StringComparison.Ordinal))
            return Verification.Unavailable;
        if (verified && status == "Confirmed") return Verification.Confirmed;
        if (!verified && (status == "Unconfirmed" || status == "Unknown")) return Verification.Unconfirmed;
        return Verification.Unavailable;
    }

    public static string LoginError(string identifier, string password)
    {
        if (identifier.Length < 3 || identifier.Length > MaximumEmailLength ||
            (IsEmailLogin(identifier) && !IsEmail(identifier)))
            return "Enter a valid email address or username.";
        if (password == null || password.Length < 4 || password.Length > MaximumPasswordLength)
            return "Enter a password with 4–128 characters.";
        return null;
    }

    public static string RegistrationError(string username, string email, string password,
        string confirmation, bool acceptedPolicies)
    {
        if (!Username.IsMatch(username))
            return "Use 3–24 letters, numbers or underscores for your username.";
        if (!IsEmail(email))
            return "Enter a valid email address.";
        if (password == null || password.Length < 8 || password.Length > MaximumPasswordLength ||
            !Regex.IsMatch(password, "[A-Z]") || !Regex.IsMatch(password, "[a-z]") ||
            !Regex.IsMatch(password, "[0-9]"))
            return "Use 8–128 characters with an uppercase letter, a lowercase letter and a number.";
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            return "Passwords do not match.";
        if (!acceptedPolicies)
            return "Agree to the Terms of Use and Privacy Policy to create an account.";
        return null;
    }
}
