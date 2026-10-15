using UnityEngine;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public partial class BarCreator
{
    [Header("Touch Drawing")]
    [Tooltip("Extra touch distance for starting a piece outside the visible point mesh, at a 720-pixel screen short edge. Capped at 12 pixels.")]
    [SerializeField, Range(0f, 12f)] private float touchPointPaddingPixels = 12f;
    [Tooltip("Extra distance outside the destination's depth-free bridge-plane footprint. Capped at 6 reference pixels; the long side mesh does not attract the endpoint.")]
    [SerializeField, Range(0f, 6f)] private float touchEndpointPaddingPixels = 6f;
    [Tooltip("Finger travel before a drawing gesture can place a piece. Tapping any part of a point does not accidentally place a bar.")]
    [SerializeField, Min(1f)] private float touchDrawDragPixels = 10f;
#if UNITY_EDITOR
    [Tooltip("Use mobile touch tolerance for mouse testing in the Editor.")]
    [SerializeField] private bool testTouchDrawingInEditor = true;
#endif
    private bool drawingPointerCaptured;
    private bool drawingGestureMoved;
    private int drawingEventPointerId;
    private int drawingTouchId = -1;
    private int drawingTouchLastSeenFrame;
    private Vector2 drawingPointerDown;
    private Vector2 drawingRawPointer;
    private Vector2 drawingScreenOffset;
    private static readonly Unity.Profiling.ProfilerMarker DrawingPickMarker =
        new Unity.Profiling.ProfilerMarker("Bridge.Draw.PickPoint");

    private bool DrawingGestureCanPlace => !drawingPointerCaptured || drawingGestureMoved;

    private bool UsesTouchDrawingAssistance
    {
        get
        {
            if (Application.isMobilePlatform || Touch.activeTouches.Count > 0) return true;
#if UNITY_EDITOR
            return testTouchDrawingInEditor;
#else
            return false;
#endif
        }
    }

    private static float DrawingPixelScale => Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, .8f, 1.75f);

    // Clamp at use time as well, so previously serialized larger values cannot
    // restore the old wide snap zones after upgrading an existing scene.
    private float DrawingEndpointPadding => UsesTouchDrawingAssistance
        ? Mathf.Clamp(touchEndpointPaddingPixels, 0f, 6f) * DrawingPixelScale : 2f;

    private void CaptureDrawingPointer(PointerEventData eventData, Vector3 startPosition, bool preserveGrabOffset)
    {
        drawingPointerCaptured = true;
        drawingGestureMoved = false;
        drawingEventPointerId = eventData.pointerId;
        drawingPointerDown = drawingRawPointer = eventData.position;
        drawingTouchId = -1;
        drawingTouchLastSeenFrame = Time.frameCount;
        drawingScreenOffset = Vector2.zero;
        float nearest = float.PositiveInfinity;
        foreach (Touch touch in Touch.activeTouches)
        {
            float distance = (touch.screenPosition - eventData.position).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            drawingTouchId = touch.touchId;
        }
        Camera camera = GetActiveCamera();
        if (preserveGrabOffset && UsesTouchDrawingAssistance && camera != null)
        {
            Vector3 projected = camera.WorldToScreenPoint(startPosition);
            // Grabbing the side of a deep node should move the endpoint relative
            // to that grab, rather than jump when projected onto the bridge plane.
            if (projected.z >= camera.nearClipPlane)
                drawingScreenOffset = new Vector2(projected.x, projected.y) - eventData.position;
        }
    }

    private Vector2 ReadDrawingPointer()
    {
        if (drawingTouchId >= 0)
        {
            foreach (Touch touch in Touch.activeTouches)
                if (touch.touchId == drawingTouchId)
                {
                    drawingTouchLastSeenFrame = Time.frameCount;
                    return drawingRawPointer = touch.screenPosition;
                }
            return drawingRawPointer;
        }
        if (UnityEngine.InputSystem.Pointer.current != null)
            drawingRawPointer = UnityEngine.InputSystem.Pointer.current.position.ReadValue();
        return drawingRawPointer;
    }

    private void UpdateDrawingGesture(Vector2 rawPosition)
    {
        if (!drawingPointerCaptured) return;
        drawingRawPointer = rawPosition;
        float slop = UsesTouchDrawingAssistance ? touchDrawDragPixels * DrawingPixelScale : 3f;
        if ((rawPosition - drawingPointerDown).sqrMagnitude >= slop * slop)
            drawingGestureMoved = true;
    }

    private Vector2 GetDrawingAimPosition(Vector2 rawPosition) =>
        rawPosition + (drawingPointerCaptured ? drawingScreenOffset : Vector2.zero);

    private bool DrawingTouchWasCancelled()
    {
        if (drawingTouchId < 0) return false;
        foreach (Touch touch in Touch.activeTouches)
            if (touch.touchId == drawingTouchId)
                return touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
        return false;
    }

    private bool DrawingSessionIsValid()
    {
        if (Touch.activeTouches.Count > 1 || isSimulating ||
            (PauseManager.Instance != null && PauseManager.Instance.isPaused) ||
            (GameManager.Instance != null &&
            (GameManager.Instance.CurrentState != GameManager.GameState.Building || GameManager.Instance.IsTransitioning)))
            return false;
        if (drawingTouchId < 0) return true;
        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.touchId != drawingTouchId) continue;
            drawingTouchLastSeenFrame = Time.frameCount;
            return touch.phase != UnityEngine.InputSystem.TouchPhase.Canceled;
        }
        // UI pointer-up can arrive after this Update. Give it one rendered frame
        // to finish a normal release; a lost touch must then cancel the preview.
        return Time.frameCount <= drawingTouchLastSeenFrame + 1;
    }

    private void UpdateDrawingMagnifier(Vector2 screenPosition, Vector3 endpoint)
    {
        if (!showMagnifierWhileDrawing || magnifyingGlass == null) return;
        // A destination hold temporarily hides the magnifier behind its ring.
        // Moving away restores it once instead of losing aim help for the drag.
        if (!magnifyingGlass.IsVisible && autoDrawConfirmationRing == null)
            magnifyingGlass.ShowMagnifier(screenPosition, endpoint);
        else
            magnifyingGlass.UpdateTrackedPosition(screenPosition, endpoint);
    }

    private void ResetDrawingPointer()
    {
        drawingPointerCaptured = false;
        drawingGestureMoved = false;
        drawingTouchId = -1;
        drawingScreenOffset = Vector2.zero;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused && drawingPointerCaptured) CancelCreation();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && drawingPointerCaptured) CancelCreation();
    }

    // Grab the whole visible mesh to START, but aim at its depth-free footprint
    // to CONNECT. Raw finger and offset aim must not form two competing targets.
    // Select/Move/Delete keep their original world-space targeting.
    private bool CheckForDrawingPoint(Vector2 screenPosition, out Point closestPoint, out Vector3 snapPosition)
    {
        using (DrawingPickMarker.Auto())
            return FindDrawingPoint(screenPosition, out closestPoint, out snapPosition);
    }

    private bool FindDrawingPoint(Vector2 screenPosition, out Point closestPoint, out Vector3 snapPosition)
    {
        closestPoint = null;
        snapPosition = Vector3.zero;
        Camera camera = GetActiveCamera();
        if (camera == null || !camera.isActiveAndEnabled || !camera.pixelRect.Contains(screenPosition))
        {
            return false;
        }
        BuildLocation location = GameManager.Instance != null ? GameManager.Instance.ActiveBuildLocation : null;
        float planeZ = GetBuildPlaneZ();
        float padding = barCreationStarted ? DrawingEndpointPadding : UsesTouchDrawingAssistance
            ? Mathf.Clamp(touchPointPaddingPixels, 0f, 12f) * DrawingPixelScale : 2f;
        float closestEdge = float.PositiveInfinity, closestCenter = float.PositiveInfinity, closestDepth = float.PositiveInfinity;
        int closestPriority = int.MaxValue;
        foreach (Point point in Point.AllPoints)
        {
            if (point == null || point == currentStartPoint || point == currentEndPoint ||
                !point.isActiveAndEnabled || !point.gameObject.activeInHierarchy ||
                (location != null && point.OwnerLocation != null && point.OwnerLocation != location) ||
                Mathf.Abs(point.transform.position.z - planeZ) > nodeSnapDepthTolerance) continue;
            Vector3 center = camera.WorldToScreenPoint(point.transform.position);
            if (center.z < camera.nearClipPlane || center.z > camera.farClipPlane) continue;
            float edge;
            bool hasFootprint = barCreationStarted
                ? point.TryGetDrawingEndpointScreenDistance(camera, screenPosition, out edge)
                : point.TryGetDrawingScreenDistance(camera, screenPosition, out edge);
            if (!hasFootprint || edge > padding) continue;
            int priority = edge <= .01f ? 0 : 1;
            float rankedEdge = edge;
            float centerDistance = (new Vector2(center.x, center.y) - screenPosition).sqrMagnitude;
            // Direct mesh touches outrank fuzzy neighbours. Break overlap ties
            // by projected center, depth and stable instance ID, not list order.
            bool better = priority < closestPriority;
            if (priority == closestPriority)
            {
                if (Mathf.Abs(rankedEdge - closestEdge) > .01f) better = rankedEdge < closestEdge;
                else if (Mathf.Abs(centerDistance - closestCenter) > .01f) better = centerDistance < closestCenter;
                else if (Mathf.Abs(center.z - closestDepth) > .001f) better = center.z < closestDepth;
                else better = closestPoint == null || point.GetInstanceID() < closestPoint.GetInstanceID();
            }
            if (!better) continue;
            closestPoint = point;
            closestPriority = priority;
            closestEdge = rankedEdge;
            closestCenter = centerDistance;
            closestDepth = center.z;
        }
        if (closestPoint == null) return false;
        snapPosition = closestPoint.transform.position;
        return true;
    }
}
