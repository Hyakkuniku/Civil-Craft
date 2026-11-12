using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation-only layout for the existing required-Undo warning.</summary>
public static class TutorialWarningLayout
{
    public static bool Apply(RectTransform panel, TextMeshProUGUI text,
        Vector2 authoredPosition, Vector2 authoredSize, RectTransform instruction)
    {
        if (panel == null || !(panel.parent is RectTransform parent)) return false;
        Canvas canvas = parent.GetComponentInParent<Canvas>(true);
        canvas = canvas != null ? canvas.rootCanvas : null;
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Rect safe = parent.rect;
        if (Screen.width > 0 && Screen.height > 0)
        {
            Rect pixels = camera != null && camera.targetTexture != null ? camera.pixelRect : Screen.safeArea;
            if (pixels.width <= 0f || pixels.height <= 0f) pixels = new Rect(0f, 0f, Screen.width, Screen.height);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixels.min, camera, out Vector2 min) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixels.max, camera, out Vector2 max))
                safe = Rect.MinMaxRect(Mathf.Max(parent.rect.xMin, Mathf.Min(min.x, max.x)),
                    Mathf.Max(parent.rect.yMin, Mathf.Min(min.y, max.y)),
                    Mathf.Min(parent.rect.xMax, Mathf.Max(min.x, max.x)),
                    Mathf.Min(parent.rect.yMax, Mathf.Max(min.y, max.y)));
        }
        return ApplyWithin(panel, text, authoredPosition, authoredSize, safe, default);
    }

    /// <summary>
    /// Bounds are in the warning parent's coordinates. The warning replaces the
    /// instruction visually while Undo is required. Its center is independent of
    /// that instruction's position and of the warning's authored anchor/pivot.
    /// </summary>
    public static bool ApplyWithin(RectTransform panel, TextMeshProUGUI text,
        Vector2 authoredPosition, Vector2 authoredSize, Rect safeParentBounds, Rect instructionParentBounds)
    {
        if (panel == null || text == null || !(panel.parent is RectTransform parent)) return false;
        // This card explains Undo; only the real Undo button accepts the action.
        foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        Rect safe = Rect.MinMaxRect(Mathf.Max(parent.rect.xMin, safeParentBounds.xMin),
            Mathf.Max(parent.rect.yMin, safeParentBounds.yMin), Mathf.Min(parent.rect.xMax, safeParentBounds.xMax),
            Mathf.Min(parent.rect.yMax, safeParentBounds.yMax));
        if (safe.width < 100f || safe.height < 80f || !text.transform.IsChildOf(panel) ||
            authoredSize.x <= 0f || authoredSize.y <= 0f) return false;
        bool fits = TutorialCenterLayout.ApplyWithin(panel, text, authoredPosition, authoredSize, Vector2.zero, safe);
        // Center after measuring so multiline text grows equally in both
        // directions, rather than keeping the preceding instruction's top edge.
        Vector2 currentCenter = parent.InverseTransformPoint(panel.TransformPoint(panel.rect.center));
        panel.anchoredPosition += safe.center - currentCenter;
        return fits;
    }
}
