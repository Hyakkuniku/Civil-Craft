using UnityEngine;

/// <summary>Single-finger joystick geometry in Canvas-local coordinates; no UI or device state.</summary>
public sealed class MobileJoystickMotion
{
    public bool HasPointer { get; private set; }
    public int PointerId { get; private set; } = int.MinValue;
    public bool IsFixed { get; private set; } = true;
    public Vector2 Origin { get; private set; }
    public Vector2 HandlePosition { get; private set; }
    public Vector2 Value { get; private set; }
    private Vector2 pointerDown;
    private Vector2 floatingPointerOffset;
    private Rect floatingBounds;
    private bool hasFloatingBounds;

    public bool SetFloatingBounds(Rect bounds)
    {
        if (HasPointer || bounds.width < 0f || bounds.height < 0f ||
            !IsFinite(bounds.min) || !IsFinite(bounds.max)) return false;
        floatingBounds = bounds;
        hasFloatingBounds = true;
        return true;
    }

    public void ClearFloatingBounds()
    {
        if (!HasPointer) hasFloatingBounds = false;
    }

    private Vector2 ClampFloatingOrigin(Vector2 point) => !hasFloatingBounds ? point : new Vector2(
        Mathf.Clamp(point.x, floatingBounds.xMin, floatingBounds.xMax),
        Mathf.Clamp(point.y, floatingBounds.yMin, floatingBounds.yMax));

    public bool TryBegin(int pointerId, Vector2 position, Vector2 fixedOrigin, bool isFixed)
    {
        if (HasPointer || pointerId == int.MinValue || !IsFinite(position) || !IsFinite(fixedOrigin)) return false;
        HasPointer = true;
        PointerId = pointerId;
        IsFixed = isFixed;
        pointerDown = position;
        Origin = isFixed ? fixedOrigin : ClampFloatingOrigin(position);
        // A touch at the edge still begins at rest even when the graphic has
        // to be inset from that touch to stay inside the movement area.
        floatingPointerOffset = isFixed ? Vector2.zero : position - Origin;
        HandlePosition = Origin;
        Value = Vector2.zero;
        return true;
    }

    public bool Move(int pointerId, Vector2 position, float movementRange)
    {
        if (!HasPointer || pointerId != PointerId || !IsFinite(position) ||
            float.IsNaN(movementRange) || float.IsInfinity(movementRange) || movementRange <= 0f) return false;
        Vector2 floatingPosition = position - floatingPointerOffset;
        Vector2 delta = IsFixed ? position - pointerDown : floatingPosition - Origin;
        Vector2 clamped = Vector2.ClampMagnitude(delta, movementRange);
        if (!IsFixed)
        {
            Origin = ClampFloatingOrigin(Origin + delta - clamped);
            clamped = Vector2.ClampMagnitude(floatingPosition - Origin, movementRange);
        }
        HandlePosition = Origin + clamped;
        Value = clamped / movementRange;
        return true;
    }

    public bool End(int pointerId)
    {
        if (!HasPointer || pointerId != PointerId) return false;
        Cancel();
        return true;
    }

    public void Cancel()
    {
        HasPointer = false;
        PointerId = int.MinValue;
        Origin = HandlePosition = Value = Vector2.zero;
        pointerDown = Vector2.zero;
        floatingPointerOffset = Vector2.zero;
        hasFloatingBounds = false;
    }

    private static bool IsFinite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y);
}
