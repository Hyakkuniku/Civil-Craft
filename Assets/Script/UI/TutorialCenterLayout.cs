using TMPro;
using UnityEngine;

/// <summary>
/// Fits the existing centered tutorial card to its complete message. Authored
/// positions are supplied separately so repeated steps never accumulate offsets.
/// </summary>
public static class TutorialCenterLayout
{
    private const float PreferredFontSize = 30f;
    private const float MinimumFontSize = 26f;
    private const float HorizontalPadding = 28f;
    private const float VerticalPadding = 24f;
    private const float MeasurementAllowance = 4f;

    /// <summary>Uses the device safe area in the card parent's coordinate space.</summary>
    public static bool Apply(RectTransform panel, TextMeshProUGUI text,
        Vector2 authoredPosition, Vector2 authoredSize, Vector2 offset)
    {
        if (panel == null || !(panel.parent is RectTransform parent)) return false;
        Rect safe = parent.rect;
        Canvas canvas = parent.GetComponentInParent<Canvas>(true);
        if (canvas != null && Screen.width > 0 && Screen.height > 0)
        {
            canvas = canvas.rootCanvas;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect screenSafe = Screen.safeArea;
            if (screenSafe.width <= 0f || screenSafe.height <= 0f)
                screenSafe = new Rect(0f, 0f, Screen.width, Screen.height);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenSafe.min,
                    camera, out Vector2 min) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenSafe.max,
                    camera, out Vector2 max))
            {
                Vector2 lower = Vector2.Min(min, max);
                Vector2 upper = Vector2.Max(min, max);
                safe = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
            }
        }
        return ApplyWithin(panel, text, authoredPosition, authoredSize, offset, safe);
    }

    /// <summary>
    /// Fits within explicit parent-local bounds, also used by detached Editor
    /// previews. Returns false if a future message exceeds even the readable
    /// minimum size; excess text is contained rather than drawn over other UI.
    /// </summary>
    public static bool ApplyWithin(RectTransform panel, TextMeshProUGUI text,
        Vector2 authoredPosition, Vector2 authoredSize, Vector2 offset, Rect safeParentBounds)
    {
        if (panel == null || text == null || !(panel.parent is RectTransform parent) ||
            !text.transform.IsChildOf(panel) || authoredSize.x <= 0f || authoredSize.y <= 0f)
            return false;

        Rect parentBounds = parent.rect;
        float left = Mathf.Max(parentBounds.xMin, safeParentBounds.xMin);
        float right = Mathf.Min(parentBounds.xMax, safeParentBounds.xMax);
        float bottom = Mathf.Max(parentBounds.yMin, safeParentBounds.yMin);
        float top = Mathf.Min(parentBounds.yMax, safeParentBounds.yMax);
        if (right <= left || top <= bottom) return false;
        float edge = Mathf.Min(16f, Mathf.Min(right - left, top - bottom) * .025f);
        Rect available = Rect.MinMaxRect(left + edge, bottom + edge, right - edge, top - edge);
        if (available.width < 100f || available.height < 80f) return false;

        // Keep the authored anchor and pivot. Its original top edge is the
        // reference, not the preceding step's already-expanded rectangle.
        Vector2 anchor = parentBounds.min + Vector2.Scale(parentBounds.size,
            new Vector2(Mathf.Lerp(panel.anchorMin.x, panel.anchorMax.x, panel.pivot.x),
                Mathf.Lerp(panel.anchorMin.y, panel.anchorMax.y, panel.pivot.y)));
        Vector2 authoredPivot = anchor + authoredPosition + offset;
        float authoredTop = authoredPivot.y + authoredSize.y * (1f - panel.pivot.y);
        float width = Mathf.Min(authoredSize.x, available.width);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, authoredSize.y);

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(HorizontalPadding, VerticalPadding);
        textRect.offsetMax = new Vector2(-HorizontalPadding, -VerticalPadding);
        text.margin = Vector4.zero;
        text.enableWordWrapping = true;
        text.enableAutoSizing = false;
        text.fontSizeMin = MinimumFontSize;
        text.fontSizeMax = PreferredFontSize;
        // Truncate is only a safety net for content beyond the available screen;
        // authored messages are measured in full before selecting the card size.
        text.overflowMode = TextOverflowModes.Truncate;

        float usableWidth = textRect.rect.width;
        float verticalInsets = Mathf.Max(VerticalPadding * 2f,
            panel.rect.height - textRect.rect.height);
        float height = available.height;
        bool fits = false;
        if (usableWidth > 0f)
        {
            for (float fontSize = PreferredFontSize; fontSize >= MinimumFontSize; fontSize -= 1f)
            {
                text.fontSize = fontSize;
                Vector2 preferred = text.GetPreferredValues(text.text, usableWidth, Mathf.Infinity);
                float requiredHeight = preferred.y + verticalInsets + MeasurementAllowance;
                // TMP 3.0's preferred X is the unwrapped line width even when
                // a constrained width is supplied. Its preferred Y measures
                // wrapping, so do not reject a valid multi-line paragraph for X.
                if (!float.IsNaN(requiredHeight) && !float.IsInfinity(requiredHeight) &&
                    requiredHeight <= available.height)
                {
                    height = Mathf.Min(available.height, Mathf.Max(authoredSize.y, requiredHeight));
                    fits = true;
                    break;
                }
            }
        }
        else text.fontSize = MinimumFontSize;

        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        // Growing down leaves the budget above the original card undisturbed.
        // Near the lower safe edge (including LowCenter), shift up only as much
        // as required to keep the whole card visible.
        float fittedTop = Mathf.Clamp(authoredTop, available.yMin + height, available.yMax);
        float x = Mathf.Clamp(authoredPivot.x,
            available.xMin + width * panel.pivot.x,
            available.xMax - width * (1f - panel.pivot.x));
        Vector2 fittedPivot = new Vector2(x, fittedTop - height * (1f - panel.pivot.y));
        panel.anchoredPosition = fittedPivot - anchor;
        return fits;
    }
}
