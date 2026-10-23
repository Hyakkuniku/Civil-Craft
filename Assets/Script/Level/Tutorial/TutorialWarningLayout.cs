using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation-only layout for the existing required-Undo warning.</summary>
public static class TutorialWarningLayout
{
    private static readonly Vector3[] corners = new Vector3[4];

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
        Rect obstacle = default;
        if (instruction != null)
        {
            Canvas sourceCanvas = instruction.GetComponentInParent<Canvas>(true);
            sourceCanvas = sourceCanvas != null ? sourceCanvas.rootCanvas : null;
            Camera sourceCamera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? sourceCanvas.worldCamera : null;
            instruction.GetWorldCorners(corners);
            Vector2 lower = new Vector2(float.PositiveInfinity, float.PositiveInfinity), upper = -lower;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 pixel = RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pixel, camera, out Vector2 point)) return false;
                lower = Vector2.Min(lower, point); upper = Vector2.Max(upper, point);
            }
            obstacle = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
        }
        return ApplyWithin(panel, text, authoredPosition, authoredSize, safe, obstacle);
    }

    /// <summary>
    /// Bounds are in the warning parent's coordinates. The warning replaces the
    /// instruction visually while Undo is required; it is not a second card to stack.
    /// </summary>
    public static bool ApplyWithin(RectTransform panel, TextMeshProUGUI text,
        Vector2 authoredPosition, Vector2 authoredSize, Rect safeParentBounds, Rect instructionParentBounds)
    {
        if (panel == null || text == null || !(panel.parent is RectTransform parent)) return false;
        // This card explains Undo; only the real Undo button accepts the action.
        foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        if (instructionParentBounds.width > 0f && instructionParentBounds.height > 0f)
        {
            Vector2 anchor = parent.rect.min + Vector2.Scale(parent.rect.size,
                new Vector2(Mathf.Lerp(panel.anchorMin.x, panel.anchorMax.x, panel.pivot.x),
                    Mathf.Lerp(panel.anchorMin.y, panel.anchorMax.y, panel.pivot.y)));
            // Reuse the instruction's top edge, not its expanded height. Clearing
            // the warning never moves or resizes that instruction.
            authoredPosition = new Vector2(instructionParentBounds.center.x,
                instructionParentBounds.yMax - authoredSize.y * (1f - panel.pivot.y)) - anchor;
        }
        return TutorialCenterLayout.ApplyWithin(panel, text, authoredPosition, authoredSize, Vector2.zero, safeParentBounds);
    }
}
