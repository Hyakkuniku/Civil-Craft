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
        var selectedTargets = new List<GameObject>();
        selected.AppendBuildModeIsolationTargets(selectedTargets);
        var targets = new List<GameObject>();
        foreach (BuildLocation site in sites)
        {
            if (site == null || site == selected || site.IsSessionChallengeLocation || site.gameObject.scene != selected.gameObject.scene) continue;
            targets.Clear();
            site.AppendBuildModeIsolationTargets(targets);
            foreach (GameObject target in targets)
            {
                if (target == null || states.ContainsKey(target)) continue;
                // Visual roots often live elsewhere in the hierarchy, and more
                // than one site can reference the same environment root.
                if (selected.transform.IsChildOf(target.transform) || OverlapsSelected(target, selectedTargets)) continue;
                states.Add(target, target.activeSelf);
                target.SetActive(false);
            }
        }
        // Selected roots win regardless of the order in which sites were found.
        // An active child still cannot render below an inactive container.
        foreach (GameObject target in selectedTargets)
            if (target != null && target.scene == selected.gameObject.scene)
                for (Transform ancestor = target.transform; ancestor != null; ancestor = ancestor.parent)
                {
                    GameObject obj = ancestor.gameObject;
                    if (!states.ContainsKey(obj)) states.Add(obj, obj.activeSelf);
                    if (!obj.activeSelf) obj.SetActive(true);
                }
    }

    private static bool OverlapsSelected(GameObject target, List<GameObject> selectedTargets)
    {
        foreach (GameObject selectedTarget in selectedTargets)
            if (selectedTarget != null && (target == selectedTarget || target.transform.IsChildOf(selectedTarget.transform) ||
                selectedTarget.transform.IsChildOf(target.transform))) return true;
        return false;
    }

    public void Dispose()
    {
        foreach (var entry in states) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        states.Clear();
    }
}
