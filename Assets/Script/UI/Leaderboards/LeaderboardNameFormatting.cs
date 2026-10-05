using System;
using System.Text;

/// <summary>Displays PlayFab's name colors without interpreting other user-supplied TMP markup.</summary>
public static class LeaderboardNameFormatting
{
    public static string Format(string name, bool isYou = false)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Engineer";
        var result = new StringBuilder(name.Length + 32);
        int openColors = 0;
        bool hasVisibleName = false;
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (character == '<')
            {
                if (TryColor(name, index, out int length, out string hex))
                {
                    result.Append("<color=#").Append(hex).Append('>');
                    openColors++;
                    index += length - 1;
                    continue;
                }
                if (openColors > 0 && Matches(name, index, "</color>"))
                {
                    result.Append("</color>");
                    openColors--;
                    index += "</color>".Length - 1;
                    continue;
                }
                // Escape only this bracket, not the whole name: a literal
                // </noparse> in a username must not terminate our escape.
                result.Append("<noparse><</noparse>");
                hasVisibleName = true;
                continue;
            }
            if (char.IsControl(character)) character = ' ';
            result.Append(character);
            if (!char.IsWhiteSpace(character)) hasVisibleName = true;
        }
        while (openColors-- > 0) result.Append("</color>");
        if (!hasVisibleName) result.Clear().Append("Engineer");
        if (isYou) result.Append(" (YOU)");
        return result.ToString();
    }

    private static bool TryColor(string name, int index, out int length, out string hex)
    {
        length = 0;
        hex = null;
        int hexStart;
        if (Matches(name, index, "<#")) hexStart = index + 2;
        else if (Matches(name, index, "<color=#")) hexStart = index + 8;
        else return false;

        // Six hex digits only: colors remain opaque and no extra tag
        // attributes can change the row's size, layout or embedded content.
        if (hexStart + 6 >= name.Length || name[hexStart + 6] != '>') return false;
        for (int digit = hexStart; digit < hexStart + 6; digit++)
        {
            char value = name[digit];
            if (!(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f') &&
                !(value >= 'A' && value <= 'F')) return false;
        }
        length = hexStart + 7 - index;
        hex = name.Substring(hexStart, 6);
        return true;
    }

    private static bool Matches(string name, int index, string token) =>
        index + token.Length <= name.Length &&
        string.Compare(name, index, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
}
