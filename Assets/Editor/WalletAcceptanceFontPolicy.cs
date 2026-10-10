using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Read-only, single reviewed TMP fallback baseline. Never rewrites a font or exempts Git drift.</summary>
public static class WalletAcceptanceFontPolicy
{
    public const string AssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";
    public const string AssetGuid = "2e498d1c8094910479dc3e1b768306a4";
    // Reviewed source/material/IDs/settings bytes, excluding only generated cache data.
    private const string ProtectedSha256 = "55116973072672d27813afe3fe29ca9baee8f2ba72f0b816587fcefe408db44c";
    private const string CanonicalSha256 = "007f8a625aeb49389dc456331ae0ded1b5a223a985db5aa6c298d30043ec235b";

    public static List<string> CollectErrors(string assetPath, string assetText)
    {
        var errors = new List<string>();
        if (!string.Equals(assetPath, AssetPath, StringComparison.Ordinal))
        { errors.Add("Only the exact reviewed LiberationSans fallback baseline is permitted."); return errors; }
        string text = Normalize(assetText);
        if (ProtectedFingerprint(text) != ProtectedSha256)
            errors.Add("Reviewed source font, material, object IDs or font configuration differs from the canonical baseline.");
        if (Fingerprint(text) != CanonicalSha256)
            errors.Add("Generated cache bytes/layout are not the exact reviewed canonical-empty baseline.");
        foreach (string field in new[] { "m_GlyphTable", "m_CharacterTable", "m_UsedGlyphRects" })
            if (!Regex.IsMatch(text, @"^  " + field + @": \[\]$", RegexOptions.Multiline))
                errors.Add(field + " must be an empty generated cache before an acceptance build.");
        if (!text.Contains("  m_FreeGlyphRects:\n  - m_X: 0\n    m_Y: 0\n    m_Width: 511\n    m_Height: 511\n"))
            errors.Add("Generated free-rectangle cache is not the reviewed empty atlas state.");
        foreach (string field in new[] { "m_Width", "m_Height", "m_CompleteImageSize", "image data" })
            if (!Regex.IsMatch(text, @"^  " + Regex.Escape(field) + @": 0$", RegexOptions.Multiline))
                errors.Add(field + " must be zero in the cleared atlas texture.");
        if (!Regex.IsMatch(text, @"^  _typelessdata: *$", RegexOptions.Multiline))
            errors.Add("Cleared atlas texture must contain no generated pixel data.");
        return errors;
    }

    public static bool IsCanonicalClearOnly(string baseline, string unityCleared)
    {
        return ProtectedFingerprint(baseline) == ProtectedSha256 &&
            CollectErrors(AssetPath, unityCleared).Count == 0 &&
            RemoveGeneratedCaches(Normalize(baseline)) == RemoveGeneratedCaches(Normalize(unityCleared));
    }

    private static string Normalize(string text) => (text ?? "").Replace("\r\n", "\n").TrimEnd('\n') + "\n";

    private static string ProtectedFingerprint(string text)
        => Fingerprint(RemoveGeneratedCaches(Normalize(text)));

    private static string Fingerprint(string text)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }

    private static string RemoveGeneratedCaches(string text)
    {
        foreach (string field in new[] { "m_GlyphTable", "m_CharacterTable", "m_UsedGlyphRects", "m_FreeGlyphRects" })
            text = Regex.Replace(text, @"^  " + field + @":.*?(?=^  [A-Za-z_][A-Za-z0-9_]*:|^--- |\z)", "", RegexOptions.Multiline | RegexOptions.Singleline);
        foreach (string field in new[] { "m_Width", "m_Height", "m_CompleteImageSize", "image data", "_typelessdata" })
            text = Regex.Replace(text, @"^  " + Regex.Escape(field) + @":.*\n", "", RegexOptions.Multiline);
        return text;
    }
}
