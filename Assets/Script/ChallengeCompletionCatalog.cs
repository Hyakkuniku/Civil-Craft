using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

/// <summary>A bounded immutable contract manifest and its personal-completion bit layout.</summary>
public sealed class ChallengeCompletionCatalog
{
    public const int MaximumContracts = 128;
    public const int MaximumIdLength = 128;
    private readonly Dictionary<string, int> indices;
    private readonly ReadOnlyCollection<string> ids;

    public int Count => ids.Count;
    public IReadOnlyList<string> Ids => ids;
    public string Fingerprint { get; }

    private ChallengeCompletionCatalog(string[] canonicalIds)
    {
        ids = Array.AsReadOnly(canonicalIds);
        indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < canonicalIds.Length; index++) indices.Add(canonicalIds[index], index);
        // IDs cannot contain line separators. Versioning the serialization keeps
        // future manifest formats from accidentally sharing this signature.
        byte[] bytes = Encoding.UTF8.GetBytes("CivilCraft.ChallengeCompletion.v1\n" + string.Join("\n", canonicalIds));
        using (SHA256 hash = SHA256.Create())
        {
            byte[] digest = hash.ComputeHash(bytes);
            const string hex = "0123456789abcdef";
            var text = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest) text.Append(hex[value >> 4]).Append(hex[value & 15]);
            Fingerprint = text.ToString();
        }
    }

    public static ChallengeCompletionCatalog Create(IEnumerable<string> permanentIds)
    {
        if (permanentIds == null) throw new ArgumentNullException(nameof(permanentIds));
        var unique = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string value in permanentIds)
        {
            if (!TryCanonicalId(value, out string canonical))
                throw new ArgumentException("Completion manifests require nonempty permanent IDs without control characters, up to 128 characters.", nameof(permanentIds));
            unique.Add(canonical);
            if (unique.Count > MaximumContracts)
                throw new ArgumentException("A completion manifest supports at most 128 distinct contracts.", nameof(permanentIds));
        }
        var copy = new string[unique.Count]; unique.CopyTo(copy);
        return new ChallengeCompletionCatalog(copy);
    }

    public void Capture(Func<string, bool> completed, out ulong low, out ulong high)
    {
        if (completed == null) throw new ArgumentNullException(nameof(completed));
        low = high = 0;
        for (int index = 0; index < ids.Count; index++)
        {
            if (!completed(ids[index])) continue;
            if (index < 64) low |= 1UL << index;
            else high |= 1UL << (index - 64);
        }
    }

    public bool HasCompletion(string id, string fingerprint, bool ready, ulong low, ulong high)
    {
        if (!ready || !string.Equals(Fingerprint, fingerprint, StringComparison.Ordinal) || !ValidBits(low, high) ||
            !TryCanonicalId(id, out string canonical) || !indices.TryGetValue(canonical, out int index)) return false;
        return index < 64 ? (low & (1UL << index)) != 0 : (high & (1UL << (index - 64))) != 0;
    }

    public bool ValidBits(ulong low, ulong high)
    {
        // C# masks shift counts modulo 64, so the exact 0/64/128 boundaries
        // need explicit cases rather than a single shifted mask expression.
        if (Count == 0) return low == 0 && high == 0;
        if (Count < 64) return high == 0 && (low >> Count) == 0;
        if (Count == 64) return high == 0;
        if (Count < MaximumContracts) return (high >> (Count - 64)) == 0;
        return true;
    }

    private static bool TryCanonicalId(string value, out string canonical)
    {
        canonical = null;
        if (value == null) return false;
        foreach (char character in value) if (char.IsControl(character)) return false;
        canonical = value.Trim();
        return canonical.Length > 0 && canonical.Length <= MaximumIdLength;
    }
}
