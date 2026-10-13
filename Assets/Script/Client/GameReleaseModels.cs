using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class GameReleaseSnapshot
{
    public bool Initialized;
    public GameReleaseDownload Current;
    public GameReleaseNotes Latest;
}

public sealed class GameReleaseDownload
{
    public string Id, Version, Build, Platform, FileUrl;
    public long? AndroidVersionCode;
}

public sealed class GameReleaseNotes
{
    public string Id, Title, Version, Build, ReleaseDate, Notes;
}

public enum GameReleaseFetchState { NotChecked, Checking, Ready, Failed, Unconfigured }

/// <summary>Public release data only. No player, wallet or authentication state.</summary>
public static class GameReleasePolicy
{
    public const string WebsiteOrigin = "https://civil-craft.vercel.app";
    public const string FeedPath = "/api/releases";
    public const string DownloadUrl = WebsiteOrigin + "/download";
    public const int FetchTimeoutSeconds = 10;
    public const int RefreshIntervalSeconds = 300;
    public const int MaxResponseBytes = 65536;
    public const long MaxAndroidVersionCode = 2100000000L;

    public static bool IsJsonObjectDocument(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxResponseBytes * 2) return false;
        return new JsonDocumentGuard(text).Check();
    }

    public static bool NeedsRefresh(DateTime? lastAttemptUtc, DateTime nowUtc, bool force)
    {
        return force || !lastAttemptUtc.HasValue || nowUtc < lastAttemptUtc.Value ||
            (nowUtc - lastAttemptUtc.Value).TotalSeconds >= RefreshIntervalSeconds;
    }

    public static bool HasDownload(GameReleaseSnapshot snapshot)
    {
        return snapshot != null && snapshot.Initialized && snapshot.Current != null &&
            !string.IsNullOrWhiteSpace(snapshot.Current.FileUrl);
    }

    public static bool IsVerifiedUpdate(GameReleaseSnapshot snapshot, long? installedCode, bool verified)
    {
        return verified && HasDownload(snapshot) && installedCode.HasValue && installedCode.Value > 0 &&
            snapshot.Current.AndroidVersionCode.HasValue &&
            snapshot.Current.AndroidVersionCode.Value > installedCode.Value;
    }

    public static bool TryParse(IDictionary<string, object> body, out GameReleaseSnapshot snapshot)
    {
        snapshot = null;
        if (body == null || !body.TryGetValue("initialized", out object initialized) || !(initialized is bool) ||
            !body.TryGetValue("current", out object currentValue) || !body.TryGetValue("latest", out object latestValue)) return false;
        var result = new GameReleaseSnapshot { Initialized = (bool)initialized };
        if (!result.Initialized && (currentValue != null || latestValue != null)) return false;
        if (currentValue != null)
        {
            if (!(currentValue is IDictionary<string, object> current) ||
                !Identifier(current, "id", out string id) || !Text(current, "version", 40, true, out string version) ||
                !Text(current, "build", 40, true, out string build) || !Text(current, "platform", 80, false, out string platform) ||
                !current.TryGetValue("fileUrl", out object urlValue)) return false;
            string url = null;
            if (urlValue != null)
            {
                if (!(urlValue is string supplied) || supplied.Length > 2000) return false;
                url = supplied.Trim();
                if (url.Length == 0) url = null;
                if (url != null && url != "/api/releases/" + id + "/download" &&
                    (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)) return false;
            }
            long? code = null;
            if (current.TryGetValue("androidVersionCode", out object codeValue) && codeValue != null)
            {
                if (!VersionCode(codeValue, out long numericCode)) return false;
                code = numericCode;
            }
            result.Current = new GameReleaseDownload { Id = id, Version = version, Build = build,
                Platform = platform, FileUrl = url, AndroidVersionCode = code };
        }
        if (latestValue != null)
        {
            if (!(latestValue is IDictionary<string, object> latest) || !Identifier(latest, "id", out string id) ||
                !Text(latest, "title", 160, true, out string title) || !Text(latest, "version", 40, true, out string version) ||
                !Text(latest, "build", 40, true, out string build) || !Text(latest, "notes", 5000, true, out string notes) ||
                !Text(latest, "releaseDate", 10, true, out string date) ||
                !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
            result.Latest = new GameReleaseNotes { Id = id, Title = title, Version = version, Build = build,
                ReleaseDate = date, Notes = notes };
        }
        snapshot = result;
        return true;
    }

    public static Dictionary<string, object> PublicFeed(GameReleaseSnapshot snapshot)
    {
        GameReleaseDownload current = snapshot.Current;
        GameReleaseNotes latest = snapshot.Latest;
        return new Dictionary<string, object>
        {
            ["initialized"] = snapshot.Initialized,
            ["current"] = current == null ? null : new Dictionary<string, object>
            {
                ["id"] = current.Id, ["version"] = current.Version, ["build"] = current.Build,
                ["platform"] = current.Platform, ["fileUrl"] = current.FileUrl,
                ["androidVersionCode"] = current.AndroidVersionCode.HasValue ? (object)current.AndroidVersionCode.Value : null
            },
            ["latest"] = latest == null ? null : new Dictionary<string, object>
            {
                ["id"] = latest.Id, ["title"] = latest.Title, ["version"] = latest.Version,
                ["build"] = latest.Build, ["releaseDate"] = latest.ReleaseDate, ["notes"] = latest.Notes
            }
        };
    }

    private static bool Identifier(IDictionary<string, object> body, string key, out string value)
    {
        if (!Text(body, key, 64, true, out value)) return false;
        foreach (char c in value)
            if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-')) return false;
        return true;
    }

    private static bool Text(IDictionary<string, object> body, string key, int maximum, bool required, out string value)
    {
        value = null;
        if (!body.TryGetValue(key, out object raw) || !(raw is string text) || text.Length > maximum ||
            text.IndexOf('\0') >= 0 || required && string.IsNullOrWhiteSpace(text)) return false;
        value = text;
        return true;
    }

    private static bool VersionCode(object value, out long code)
    {
        code = 0;
        if (!(value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint ||
            value is long || value is ulong || value is float || value is double || value is decimal)) return false;
        try
        {
            decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            if (number < 1 || number > MaxAndroidVersionCode || decimal.Truncate(number) != number) return false;
            code = (long)number;
            return true;
        }
        catch (Exception) { return false; }
    }

    // The existing SDK decoder accepts trailing data and duplicate keys. Check
    // the complete public document first without changing that shared decoder.
    private sealed class JsonDocumentGuard
    {
        private readonly string text;
        private int position;
        public JsonDocumentGuard(string text) { this.text = text; }
        public bool Check() { Space(); bool valid = Object(0); Space(); return valid && position == text.Length; }
        private void Space() { while (position < text.Length && (text[position] == ' ' || text[position] == '\t' || text[position] == '\n' || text[position] == '\r')) position++; }
        private bool Take(char c) { Space(); if (position >= text.Length || text[position] != c) return false; position++; return true; }
        private bool Object(int depth)
        {
            if (depth > 64 || !Take('{')) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (Take('}')) return true;
            do
            {
                if (!String(out string key) || !keys.Add(key) || !Take(':') || !Value(depth + 1)) return false;
                if (Take('}')) return true;
            } while (Take(','));
            return false;
        }
        private bool Value(int depth)
        {
            Space(); if (position >= text.Length || depth > 64) return false;
            char c = text[position];
            if (c == '{') return Object(depth);
            if (c == '[')
            {
                position++;
                if (Take(']')) return true;
                do { if (!Value(depth + 1)) return false; if (Take(']')) return true; } while (Take(','));
                return false;
            }
            if (c == '"') return String(out _);
            if (c == 't') return Literal("true");
            if (c == 'f') return Literal("false");
            if (c == 'n') return Literal("null");
            return Number();
        }
        private bool Literal(string token)
        {
            if (position + token.Length > text.Length || string.CompareOrdinal(text, position, token, 0, token.Length) != 0) return false;
            position += token.Length; return true;
        }
        private bool Digit() => position < text.Length && text[position] >= '0' && text[position] <= '9';
        private bool Number()
        {
            if (position < text.Length && text[position] == '-') position++;
            if (!Digit()) return false;
            if (text[position] == '0') position++;
            else while (Digit()) position++;
            if (position < text.Length && text[position] == '.')
            { position++; if (!Digit()) return false; while (Digit()) position++; }
            if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
            {
                position++;
                if (position < text.Length && (text[position] == '+' || text[position] == '-')) position++;
                if (!Digit()) return false; while (Digit()) position++;
            }
            return true;
        }
        private bool String(out string value)
        {
            value = null; if (!Take('"')) return false;
            var decoded = new System.Text.StringBuilder();
            while (position < text.Length)
            {
                char c = text[position++];
                if (c == '"') { value = decoded.ToString(); return true; }
                if (c < 32) return false;
                if (c != '\\') { decoded.Append(c); continue; }
                if (position >= text.Length) return false;
                c = text[position++];
                if (c == '"' || c == '\\' || c == '/') decoded.Append(c);
                else if (c == 'b') decoded.Append('\b');
                else if (c == 'f') decoded.Append('\f');
                else if (c == 'n') decoded.Append('\n');
                else if (c == 'r') decoded.Append('\r');
                else if (c == 't') decoded.Append('\t');
                else if (c == 'u')
                {
                    int scalar = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        if (position >= text.Length) return false;
                        char h = text[position++];
                        int number = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                        if (number < 0) return false; scalar = scalar * 16 + number;
                    }
                    decoded.Append((char)scalar);
                }
                else return false;
            }
            return false;
        }
    }
}
