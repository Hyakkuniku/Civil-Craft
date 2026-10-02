using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Reversible visibility only: no redesign, bridge deletion or save writes.</summary>
public sealed class ChallengeSiteIsolation : IDisposable
{
    private readonly Dictionary<GameObject, bool> states = new Dictionary<GameObject, bool>();

    public void ShowOnly(BuildLocation selected, IEnumerable<BuildLocation> sites)
    {
        Dispose();
        if (selected == null) return;
        var targets = new List<GameObject>();
        foreach (BuildLocation site in sites)
        {
            if (site == null || site.gameObject.scene != selected.gameObject.scene) continue;
            targets.Clear();
            site.AppendBuildModeIsolationTargets(targets);
            foreach (GameObject target in targets)
            {
                if (target == null || states.ContainsKey(target)) continue;
                // Never hide an ancestor shared with the selected build site.
                if (site != selected && selected.transform.IsChildOf(target.transform)) continue;
                states.Add(target, target.activeSelf);
                target.SetActive(site == selected);
            }
        }
    }

    public void Dispose()
    {
        foreach (var entry in states) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        states.Clear();
    }
}
