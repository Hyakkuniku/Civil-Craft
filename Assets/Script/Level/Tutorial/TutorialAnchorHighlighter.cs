using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UnityEvent bridge for tutorial steps that need the Select-tool outline on an anchor.
/// It never changes normal selection or makes the anchor editable.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialAnchorHighlighter : MonoBehaviour
{
    private static readonly HashSet<TutorialAnchorHighlighter> activeHighlighters =
        new HashSet<TutorialAnchorHighlighter>();

    private readonly HashSet<Point> highlightedPoints = new HashSet<Point>();

    public int HighlightedCount => highlightedPoints.Count;

    /// <summary>Assign this from a TutorialStep OnStepStart UnityEvent.</summary>
    public void HighlightAnchor(Transform target)
    {
        if (target == null)
        {
            Debug.LogWarning("[TutorialAnchorHighlighter] No anchor Transform was assigned.", this);
            return;
        }

        Point point = target.GetComponent<Point>();
        if (point == null) point = target.GetComponentInParent<Point>();
        if (point == null) point = target.GetComponentInChildren<Point>(true);
        if (point == null)
        {
            Debug.LogWarning(
                $"[TutorialAnchorHighlighter] '{target.name}' does not contain a Point component.", target);
            return;
        }

        if (!highlightedPoints.Add(point)) return;

        point.SetTutorialHighlighted(true);
        activeHighlighters.Add(this);
    }

    public void ClearHighlight()
    {
        foreach (Point point in highlightedPoints)
            if (point != null) point.SetTutorialHighlighted(false);
        highlightedPoints.Clear();
        activeHighlighters.Remove(this);
    }

    public static void ClearAll()
    {
        if (activeHighlighters.Count == 0) return;
        TutorialAnchorHighlighter[] snapshot =
            new TutorialAnchorHighlighter[activeHighlighters.Count];
        activeHighlighters.CopyTo(snapshot);
        foreach (TutorialAnchorHighlighter highlighter in snapshot)
            if (highlighter != null) highlighter.ClearHighlight();
        activeHighlighters.Clear();
    }

    private void OnDisable() => ClearHighlight();
    private void OnDestroy() => ClearHighlight();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        ClearAll();
        activeHighlighters.Clear();
    }
}
