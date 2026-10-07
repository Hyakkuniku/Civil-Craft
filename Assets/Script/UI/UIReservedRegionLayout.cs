using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.SceneManagement;

/// <summary>
/// Screen-space reservations shared by scene-authored tutorial panels, inspection
/// panels, and transient messages. RectTransforms remain in their own Canvases;
/// their rendered bounds are compared in pixels so Canvas scaling and safe areas
/// are handled in one place.
/// </summary>
public static class UIReservedRegionLayout
{
    private static readonly HashSet<RectTransform> reservedRects = new HashSet<RectTransform>();
    private static readonly List<Rect> occupiedScreenRects = new List<Rect>(8);
    private static readonly Vector3[] worldCorners = new Vector3[4];
    private static readonly List<float> edgeCandidateYs = new List<float>(16);
    private static Selectable[] selectableBuffer = new Selectable[32];
    private static readonly List<RectTransform> touchControlRegions = new List<RectTransform>(4);
    private static bool touchControlsCached;
    private static float transitionUntil;
    public static event System.Action LayoutChanging;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetTransientState()
    {
        transitionUntil = 0f;
        reservedRects.Clear();
        occupiedScreenRects.Clear();
        touchControlRegions.Clear();
        touchControlsCached = false;
        SceneManager.sceneLoaded -= InvalidateTouchControls;
        SceneManager.sceneLoaded += InvalidateTouchControls;
        LayoutChanging = null;
    }

    public static void NotifyLayoutChanging()
    {
        LayoutChanging?.Invoke();
    }

    public static void NotifyTransition(float duration)
    {
        transitionUntil = Mathf.Max(transitionUntil,
            Time.unscaledTime + Mathf.Max(0f, duration));
        NotifyLayoutChanging();
    }

    public static void Register(RectTransform rect)
    {
        if (rect != null && reservedRects.Add(rect)) NotifyLayoutChanging();
    }

    public static void Unregister(RectTransform rect)
    {
        if (rect != null && reservedRects.Remove(rect)) NotifyLayoutChanging();
    }

    public static bool IsModalTransitioning => Time.unscaledTime < transitionUntil;

    public static bool IsLayoutTransitioning =>
        IsModalTransitioning ||
        (TutorialManager.Instance != null && TutorialManager.Instance.IsBannerTransitioning);

    public static bool IsRenderable(RectTransform rect)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy) return false;

        bool hasCanvas = false;
        bool checkParentOpacity = true;
        float opacity = 1f;
        for (Transform parent = rect; parent != null; parent = parent.parent)
        {
            if (checkParentOpacity && parent.TryGetComponent(out CanvasGroup group))
            {
                opacity *= group.alpha;
                if (opacity <= .001f) return false;
                if (group.ignoreParentGroups) checkParentOpacity = false;
            }
            if (!parent.TryGetComponent(out Canvas canvas)) continue;
            hasCanvas = true;
            if (!canvas.isActiveAndEnabled) return false;
        }
        return hasCanvas;
    }

    /// <summary>
    /// A passive notification uses only the top screen edge, never the bottom or
    /// central play area. Registered panels and active controls take priority.
    /// Geometry can be measured while the item's own CanvasGroup is faded out.
    /// </summary>
    public static bool TryFindTopEdgePlacement(RectTransform item, Vector2 preferredCenter,
        bool keepCurrentIfSafe, float edgeMarginPixels, float regionGapPixels,
        out Vector2 chosenCenter)
    {
        chosenCenter = preferredCenter;
        if (!TryGetScreenRect(item, out Rect itemRect)) return false;
        Rect safe = Screen.safeArea;
        if (safe.width <= 0f || safe.height <= 0f)
            safe = new Rect(0f, 0f, Screen.width, Screen.height);
        float edge = Mathf.Max(0f, edgeMarginPixels);
        safe = Rect.MinMaxRect(safe.xMin + edge, safe.yMin + edge,
            safe.xMax - edge, safe.yMax - edge);
        float halfWidth = itemRect.width * .5f, halfHeight = itemRect.height * .5f;
        if (safe.width < itemRect.width || safe.height < itemRect.height) return false;

        float gap = Mathf.Max(0f, regionGapPixels);
        GatherReservations(item, gap);
        GatherActiveControls(item, gap);
        GatherTouchControls(item, gap);
        BuildUIController buildUI = BuildUIController.Instance;
        if (buildUI != null && buildUI.actionLogText != null && buildUI.actionLogText.enabled &&
            !string.IsNullOrEmpty(buildUI.actionLogText.text))
            AddReservation(item, buildUI.actionLogText.rectTransform, gap);

        if (keepCurrentIfSafe && FitsTopEdge(itemRect.center, halfWidth, halfHeight, safe))
        {
            chosenCenter = itemRect.center;
            return true;
        }
        Vector2 preferred = new Vector2(
            Mathf.Clamp(preferredCenter.x, safe.xMin + halfWidth, safe.xMax - halfWidth),
            Mathf.Clamp(preferredCenter.y, safe.yMin + halfHeight, safe.yMax - halfHeight));
        if (FitsTopEdge(preferred, halfWidth, halfHeight, safe))
        {
            chosenCenter = preferred;
            return true;
        }

        edgeCandidateYs.Clear();
        edgeCandidateYs.Add(preferred.y);
        edgeCandidateYs.Add(safe.yMax - halfHeight);
        foreach (Rect occupied in occupiedScreenRects)
        {
            edgeCandidateYs.Add(occupied.yMax + halfHeight + 1f);
            edgeCandidateYs.Add(occupied.yMin - halfHeight - 1f);
        }
        bool found = false;
        float bestDistance = float.PositiveInfinity;
        for (int column = 0; column < 3; column++)
        {
            float x = column == 0 ? preferred.x : column == 1
                ? safe.xMin + halfWidth : safe.xMax - halfWidth;
            foreach (float y in edgeCandidateYs)
            {
                Vector2 candidate = new Vector2(x, y);
                if (!FitsTopEdge(candidate, halfWidth, halfHeight, safe)) continue;
                float distance = (candidate - preferredCenter).sqrMagnitude;
                if (found && distance >= bestDistance) continue;
                found = true;
                bestDistance = distance;
                chosenCenter = candidate;
            }
        }
        return found;
    }

    private static bool FitsTopEdge(Vector2 center, float halfWidth, float halfHeight, Rect safe)
    {
        float band = safe.height * .30f;
        bool atEdge = center.y - halfHeight >= safe.yMax - band - .5f;
        return atEdge && Fits(center, halfWidth, halfHeight, safe);
    }

    private static void GatherActiveControls(RectTransform item, float gap)
    {
        int count = Selectable.allSelectableCount;
        if (selectableBuffer.Length < count)
            selectableBuffer = new Selectable[Mathf.NextPowerOfTwo(count)];
        count = Selectable.AllSelectablesNoAlloc(selectableBuffer);
        for (int index = 0; index < count; index++)
        {
            Selectable control = selectableBuffer[index];
            if (control != null) AddReservation(item, control.transform as RectTransform, gap);
            selectableBuffer[index] = null;
        }
    }

    private static void AddReservation(RectTransform item, RectTransform region, float gap)
    {
        if (region == null || region == item || region.IsChildOf(item) || item.IsChildOf(region) ||
            !IsRenderable(region) || !TryGetScreenRect(region, out Rect bounds)) return;
        occupiedScreenRects.Add(Expand(bounds, gap));
    }

    private static void InvalidateTouchControls(Scene scene, LoadSceneMode mode)
    {
        touchControlsCached = false;
    }

    private static void GatherTouchControls(RectTransform item, float gap)
    {
        if (!touchControlsCached)
        {
            touchControlRegions.Clear();
            // Joysticks are not Selectables. Discover their small visible bounds
            // once per loaded scene, not with an object search every frame.
            foreach (OnScreenStick stick in Object.FindObjectsOfType<OnScreenStick>(true))
            {
                RectTransform rect = stick.transform as RectTransform;
                RectTransform parent = rect != null ? rect.parent as RectTransform : null;
                if (parent != null && parent.GetComponent<Canvas>() == null &&
                    parent.GetComponent<Image>() != null) rect = parent;
                if (rect != null) touchControlRegions.Add(rect);
            }
            touchControlsCached = true;
        }
        foreach (RectTransform region in touchControlRegions) AddReservation(item, region, gap);
    }

    public static bool TryGetScreenRect(RectTransform rect, out Rect screenRect)
    {
        screenRect = default;
        if (rect == null) return false;

        rect.GetWorldCorners(worldCorners);
        Camera camera = GetCanvasCamera(rect);
        Vector2 first = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[0]);
        float left = first.x;
        float right = first.x;
        float bottom = first.y;
        float top = first.y;
        for (int i = 1; i < worldCorners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[i]);
            left = Mathf.Min(left, point.x);
            right = Mathf.Max(right, point.x);
            bottom = Mathf.Min(bottom, point.y);
            top = Mathf.Max(top, point.y);
        }

        screenRect = Rect.MinMaxRect(left, bottom, right, top);
        return screenRect.width > 0.5f && screenRect.height > 0.5f;
    }

    /// <summary>
    /// Keeps an already-safe position stable; otherwise tries the authored position
    /// and the nearest spaces immediately above or below a reserved panel.
    /// Returns false when no vertical placement fits inside the device safe area.
    /// </summary>
    public static bool TryFindVerticalPlacement(
        RectTransform item,
        Vector2 preferredCenter,
        bool keepCurrentIfSafe,
        float edgeMarginPixels,
        float regionGapPixels,
        out Vector2 chosenCenter)
    {
        chosenCenter = preferredCenter;
        if (!IsRenderable(item) || !TryGetScreenRect(item, out Rect itemRect)) return false;

        Rect safe = Screen.safeArea;
        if (safe.width <= 0f || safe.height <= 0f)
            safe = new Rect(0f, 0f, Screen.width, Screen.height);
        float edge = Mathf.Max(0f, edgeMarginPixels);
        safe = Rect.MinMaxRect(safe.xMin + edge, safe.yMin + edge,
            safe.xMax - edge, safe.yMax - edge);

        float halfWidth = itemRect.width * 0.5f;
        float halfHeight = itemRect.height * 0.5f;
        if (safe.width < itemRect.width || safe.height < itemRect.height) return false;

        GatherReservations(item, Mathf.Max(0f, regionGapPixels));
        if (keepCurrentIfSafe && Fits(itemRect.center, halfWidth, halfHeight, safe))
        {
            chosenCenter = itemRect.center;
            return true;
        }

        float x = Mathf.Clamp(preferredCenter.x, safe.xMin + halfWidth, safe.xMax - halfWidth);
        float y = Mathf.Clamp(preferredCenter.y, safe.yMin + halfHeight, safe.yMax - halfHeight);
        Vector2 preferred = new Vector2(x, y);
        if (Fits(preferred, halfWidth, halfHeight, safe))
        {
            chosenCenter = preferred;
            return true;
        }

        bool found = false;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < occupiedScreenRects.Count; i++)
        {
            Rect occupied = occupiedScreenRects[i];
            if (x + halfWidth <= occupied.xMin || x - halfWidth >= occupied.xMax)
                continue;

            TryCandidate(new Vector2(x, occupied.yMax + halfHeight), preferredCenter,
                halfWidth, halfHeight, safe, ref found, ref bestDistance, ref chosenCenter);
            TryCandidate(new Vector2(x, occupied.yMin - halfHeight), preferredCenter,
                halfWidth, halfHeight, safe, ref found, ref bestDistance, ref chosenCenter);
        }
        return found;
    }

    /// <summary>
    /// Places a transient item immediately below a priority panel. It never
    /// searches above that panel; if the space below is occupied or outside the
    /// safe area the caller keeps the item queued until space is available.
    /// </summary>
    public static bool TryFindPlacementBelow(
        RectTransform item,
        RectTransform priorityPanel,
        float edgeMarginPixels,
        float regionGapPixels,
        out Vector2 chosenCenter)
    {
        chosenCenter = default;
        if (!IsRenderable(item) || !IsRenderable(priorityPanel) ||
            !TryGetScreenRect(item, out Rect itemRect) ||
            !TryGetScreenRect(priorityPanel, out Rect panelRect)) return false;

        Rect safe = Screen.safeArea;
        if (safe.width <= 0f || safe.height <= 0f)
            safe = new Rect(0f, 0f, Screen.width, Screen.height);
        float edge = Mathf.Max(0f, edgeMarginPixels);
        safe = Rect.MinMaxRect(safe.xMin + edge, safe.yMin + edge,
            safe.xMax - edge, safe.yMax - edge);

        float halfWidth = itemRect.width * 0.5f;
        float halfHeight = itemRect.height * 0.5f;
        if (safe.width < itemRect.width || safe.height < itemRect.height) return false;

        float gap = Mathf.Max(0f, regionGapPixels);
        chosenCenter = new Vector2(
            Mathf.Clamp(panelRect.center.x, safe.xMin + halfWidth, safe.xMax - halfWidth),
            panelRect.yMin - gap - halfHeight);
        GatherReservations(item, gap);
        return Fits(chosenCenter, halfWidth, halfHeight, safe);
    }

    public static bool TrySetScreenCenter(RectTransform item, Vector2 screenCenter)
    {
        if (item == null || !(item.parent is RectTransform parent) ||
            !TryGetScreenRect(item, out Rect currentRect)) return false;
        if ((screenCenter - currentRect.center).sqrMagnitude < 0.25f) return true;

        Camera camera = GetCanvasCamera(item);
        Vector2 pivotScreen = RectTransformUtility.WorldToScreenPoint(camera, item.position);
        Vector2 targetPivotScreen = pivotScreen + screenCenter - currentRect.center;
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parent, targetPivotScreen, camera, out Vector3 worldPosition)) return false;

        item.position = worldPosition;
        return true;
    }

    private static void GatherReservations(RectTransform item, float gap)
    {
        occupiedScreenRects.Clear();
        foreach (RectTransform reserved in reservedRects)
        {
            if (reserved == null || reserved == item || !IsRenderable(reserved) ||
                !TryGetScreenRect(reserved, out Rect bounds)) continue;
            occupiedScreenRects.Add(Expand(bounds, gap));
        }

        UIPanelCoordinator coordinator = UIPanelCoordinator.Instance;
        RectTransform modal = coordinator != null && coordinator.CurrentPanel != null
            ? coordinator.CurrentPanel.transform as RectTransform : null;
        if (modal != null && modal != item && !reservedRects.Contains(modal) &&
            IsRenderable(modal) && TryGetScreenRect(modal, out Rect modalBounds))
            occupiedScreenRects.Add(Expand(modalBounds, gap));
    }

    private static Rect Expand(Rect rect, float gap)
    {
        return Rect.MinMaxRect(rect.xMin - gap, rect.yMin - gap,
            rect.xMax + gap, rect.yMax + gap);
    }

    private static bool Fits(Vector2 center, float halfWidth, float halfHeight, Rect safe)
    {
        Rect candidate = Rect.MinMaxRect(center.x - halfWidth, center.y - halfHeight,
            center.x + halfWidth, center.y + halfHeight);
        if (candidate.xMin < safe.xMin || candidate.xMax > safe.xMax ||
            candidate.yMin < safe.yMin || candidate.yMax > safe.yMax) return false;

        for (int i = 0; i < occupiedScreenRects.Count; i++)
            if (candidate.Overlaps(occupiedScreenRects[i])) return false;
        return true;
    }

    private static void TryCandidate(Vector2 candidate, Vector2 preferred,
        float halfWidth, float halfHeight, Rect safe, ref bool found,
        ref float bestDistance, ref Vector2 chosen)
    {
        if (!Fits(candidate, halfWidth, halfHeight, safe)) return;
        float distance = Mathf.Abs(candidate.y - preferred.y);
        if (found && distance >= bestDistance) return;
        found = true;
        bestDistance = distance;
        chosen = candidate;
    }

    private static Camera GetCanvasCamera(RectTransform rect)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>(true);
        if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;
        return canvas.rootCanvas.worldCamera != null
            ? canvas.rootCanvas.worldCamera : Camera.main;
    }
}
