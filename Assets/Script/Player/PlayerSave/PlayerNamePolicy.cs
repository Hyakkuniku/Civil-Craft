using System;

/// <summary>Shared plain-name and reserved-name rules for registrations and display-name changes.</summary>
public static class PlayerNamePolicy
{
    public const string FormattingError = "Use a plain name. Hex color codes and formatting tags are not allowed.";
    public const string ReservedNameError = "Names containing .dev_ are reserved. Please choose another name.";

    public static bool TryValidate(string value, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Please enter a name.";
            return false;
        }
        if (value.IndexOf(".dev_", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            error = ReservedNameError;
            return false;
        }
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '<' || value[index] == '>')
            {
                error = FormattingError;
                return false;
            }
            if (value[index] != '#') continue;
            // Reject RGB/RGBA color codes, including short forms and codes
            // embedded in a name. Ordinary hex-looking names without # stay valid.
            int digits = 0;
            for (int next = index + 1; next < value.Length && IsHex(value[next]); next++)
                if (++digits >= 3)
                {
                    error = FormattingError;
                    return false;
                }
        }
        error = null;
        return true;
    }

    private static bool IsHex(char value) => value >= '0' && value <= '9' ||
        value >= 'a' && value <= 'f' || value >= 'A' && value <= 'F';
}
