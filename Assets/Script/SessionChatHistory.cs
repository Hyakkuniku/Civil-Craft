using System.Text;
using System.Collections.Generic;

/// <summary>Session-only bounded history and shared client/server message limits.</summary>
public sealed class SessionChatHistory
{
    public const int MessageLimit = 280;
    public const int HistoryLimit = 40;
    public const float SendInterval = 1f;
    private readonly Queue<string> lines = new Queue<string>();
    private int lastSequence;
    public int Count => lines.Count;
    public string Text => string.Join("\n\n", lines);

    public bool Add(int sequence, string name, string message)
    {
        if (sequence <= lastSequence) return false;
        string body = Normalize(message);
        if (body.Length == 0) return false;
        lastSequence = sequence;
        if (lines.Count == HistoryLimit) lines.Dequeue();
        lines.Enqueue(FormatLine(name, body));
        return true;
    }

    public void Clear() { lines.Clear(); lastSequence = 0; }

    public static string FormatLine(string name, string message)
    {
        string sender = string.IsNullOrWhiteSpace(name) ? "Engineer" : name;
        return "<color=#98601C><b>" + EscapeMarkup(sender) + "</b></color>:\n" + EscapeMarkup(message);
    }

    // Escape each opening bracket as a single literal glyph. Wrapping the whole
    // payload in noparse alone is unsafe: a typed </noparse> could close it.
    private static string EscapeMarkup(string value) => (value ?? string.Empty).Replace("<", "<noparse><</noparse>");

    public static string Normalize(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        var result = new StringBuilder(MessageLimit);
        foreach (char character in message.Trim())
        {
            if (result.Length == MessageLimit) break;
            if (character == '\n' || character == '\r' || character == '\t') result.Append(' ');
            else if (!char.IsControl(character)) result.Append(character);
        }
        if (result.Length > 0 && char.IsHighSurrogate(result[result.Length - 1])) result.Length--;
        return result.ToString().Trim();
    }
}
