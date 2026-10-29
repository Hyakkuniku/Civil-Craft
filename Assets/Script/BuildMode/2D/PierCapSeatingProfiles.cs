using System;
using UnityEngine;

/// <summary>Editor-measured top faces; no imported mesh needs CPU readability on a phone.</summary>
public sealed class PierCapSeatingProfiles : ScriptableObject
{
    [Serializable] public sealed class Entry
    {
        public GameObject source;
        public Bounds sourceBounds;
        public Bounds topFace;
    }
    public Entry[] entries = Array.Empty<Entry>();
    private static PierCapSeatingProfiles catalog;

    public static bool TryGetTopFace(GameObject source, out Bounds topFace)
    {
        topFace = default;
        if (catalog == null) catalog = Resources.Load<PierCapSeatingProfiles>("PierCapSeatingProfiles");
        if (catalog == null || source == null) return false;
        foreach (Entry entry in catalog.entries)
            if (entry.source == source) { topFace = entry.topFace; return true; }
        return false;
    }
}
