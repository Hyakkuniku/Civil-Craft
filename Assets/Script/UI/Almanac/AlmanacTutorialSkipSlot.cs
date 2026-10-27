using UnityEngine;

/// <summary>Temporarily presents SKIP in the book's authored CLOSE slot without changing either button's events.</summary>
public sealed class AlmanacTutorialSkipSlot
{
    private readonly Vector3[] corners = new Vector3[4];
    private RectTransform skipRect, closeRect;
    private CanvasGroup closeGroup;
    private float closeAlpha;
    private bool closeInteractable, closeBlocksRaycasts;
    private Vector2 skipAnchorMin, skipAnchorMax, skipPivot, skipSize, skipPosition;
    private Vector3 skipScale;
    private Quaternion skipRotation;

    public bool TryPlace(RectTransform skip, RectTransform close)
    {
        if (skip == null || close == null || skip == close ||
            !(skip.parent is RectTransform parent) || !close.gameObject.activeInHierarchy ||
            !TryGetLocalBounds(close, parent, out Rect bounds)) return false;
        if (skipRect != skip || closeRect != close || closeGroup == null)
        {
            Release();
            skipRect = skip; closeRect = close;
            skipAnchorMin = skip.anchorMin; skipAnchorMax = skip.anchorMax; skipPivot = skip.pivot;
            skipSize = skip.sizeDelta; skipPosition = skip.anchoredPosition;
            skipScale = skip.localScale; skipRotation = skip.localRotation;
            closeGroup = close.GetComponent<CanvasGroup>();
            if (closeGroup == null) closeGroup = close.gameObject.AddComponent<CanvasGroup>();
            closeAlpha = closeGroup.alpha;
            closeInteractable = closeGroup.interactable; closeBlocksRaycasts = closeGroup.blocksRaycasts;
        }

        skip.anchorMin = skip.anchorMax = skip.pivot = new Vector2(.5f, .5f);
        skip.localScale = Vector3.one;
        skip.localRotation = Quaternion.identity;
        skip.sizeDelta = bounds.size;
        skip.anchoredPosition = bounds.center - parent.rect.center;
        // Keep CLOSE active so its authored layout/target remains measurable.
        // Only the visible, usable SKIP button owns this slot until release.
        closeGroup.alpha = 0f;
        closeGroup.interactable = false;
        closeGroup.blocksRaycasts = false;
        return true;
    }

    public bool Release()
    {
        bool occupied = skipRect != null || closeRect != null;
        if (closeGroup != null)
        {
            closeGroup.alpha = closeAlpha;
            closeGroup.interactable = closeInteractable;
            closeGroup.blocksRaycasts = closeBlocksRaycasts;
        }
        if (skipRect != null)
        {
            skipRect.anchorMin = skipAnchorMin; skipRect.anchorMax = skipAnchorMax; skipRect.pivot = skipPivot;
            skipRect.sizeDelta = skipSize; skipRect.anchoredPosition = skipPosition;
            skipRect.localScale = skipScale; skipRect.localRotation = skipRotation;
        }
        skipRect = null; closeRect = null; closeGroup = null;
        return occupied;
    }

    private bool TryGetLocalBounds(RectTransform source, RectTransform parent, out Rect bounds)
    {
        bounds = default;
        Canvas sourceCanvas = source.GetComponentInParent<Canvas>(true);
        Canvas destinationCanvas = parent.GetComponentInParent<Canvas>(true);
        if (sourceCanvas == null || destinationCanvas == null) return false;
        sourceCanvas = sourceCanvas.rootCanvas; destinationCanvas = destinationCanvas.rootCanvas;
        if (!sourceCanvas.isActiveAndEnabled || !destinationCanvas.isActiveAndEnabled) return false;
        Camera sourceCamera = sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : sourceCanvas.worldCamera;
        Camera destinationCamera = destinationCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : destinationCanvas.worldCamera;
        if ((sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay && sourceCamera == null) ||
            (destinationCanvas.renderMode != RenderMode.ScreenSpaceOverlay && destinationCamera == null)) return false;

        source.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int index = 0; index < corners.Length; index++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[index]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, destinationCamera, out Vector2 local) ||
                float.IsNaN(local.x) || float.IsNaN(local.y) || float.IsInfinity(local.x) || float.IsInfinity(local.y)) return false;
            min = Vector2.Min(min, local); max = Vector2.Max(max, local);
        }
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return bounds.width > .01f && bounds.height > .01f;
    }
}
