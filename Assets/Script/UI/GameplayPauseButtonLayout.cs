using UnityEngine;
using UnityEngine.UI;

/// <summary>Compact authored pause artwork with a separate, density-aware touch area.</summary>
public static class GameplayPauseButtonLayout
{
    public const float VisibleSize = 80f;
    public const float MinimumTouchSize = 120f;
    public const float MinimumTouchDp = 48f;
    public const float EdgeMargin = 32f;

    public static void Apply(RectTransform rect)
    {
        if (rect == null) return;
        Vector2 screen = new Vector2(Screen.width, Screen.height);
        Apply(rect, CanvasScale(rect, screen), screen, Screen.safeArea, Screen.dpi, Application.isMobilePlatform);
    }

    public static void Apply(RectTransform rect, float canvasScale, Vector2 screenSize,
        Rect safeArea, float dpi, bool mobile)
    {
        if (rect == null) return;
        float scale = ValidScale(canvasScale);
        if (safeArea.width <= 0f || safeArea.height <= 0f)
            safeArea = new Rect(Vector2.zero, screenSize);
        float touchSize = TouchSize(scale, screenSize, dpi, mobile);
        float top = Mathf.Max(0f, screenSize.y - safeArea.yMax) / scale;
        float center = (safeArea.center.x - screenSize.x * .5f) / scale;

        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = Vector2.one * VisibleSize;
        // Reserve room for the touch bounds, not just the smaller visible image.
        rect.anchoredPosition = new Vector2(center, -(top + EdgeMargin + touchSize * .5f));
        Graphic graphic = rect.GetComponent<Graphic>();
        if (graphic != null)
        {
            float padding = -(touchSize - VisibleSize) * .5f;
            graphic.raycastPadding = new Vector4(padding, padding, padding, padding);
        }
    }

    public static float TouchSize(float canvasScale, Vector2 screenSize, float dpi, bool mobile)
    {
        if (!mobile) return MinimumTouchSize;
        // Unity reports Android's density bucket. When unavailable, estimate a
        // conservative phone density without making the visible artwork larger.
        float density = dpi > 0f && !float.IsNaN(dpi) && !float.IsInfinity(dpi)
            ? dpi / 160f : Mathf.Max(1f, Mathf.Min(screenSize.x, screenSize.y) / 360f);
        return Mathf.Max(MinimumTouchSize, MinimumTouchDp * density / ValidScale(canvasScale));
    }

    private static float CanvasScale(RectTransform rect, Vector2 screen)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>(true);
        if (canvas == null) return 1f;
        Canvas root = canvas.rootCanvas;
        CanvasScaler scaler = root != null ? root.GetComponent<CanvasScaler>() : null;
        if (root != null && root.renderMode != RenderMode.WorldSpace && scaler != null && scaler.enabled &&
            scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            // Match CanvasScaler immediately, even before its next Update after
            // rotation or resolution changes; canvas.scaleFactor may still be stale.
            Vector2 reference = scaler.referenceResolution;
            float width = Mathf.Max(.001f, screen.x / Mathf.Max(1f, reference.x));
            float height = Mathf.Max(.001f, screen.y / Mathf.Max(1f, reference.y));
            if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand) return Mathf.Min(width, height);
            if (scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink) return Mathf.Max(width, height);
            return Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(width, 2f), Mathf.Log(height, 2f), scaler.matchWidthOrHeight));
        }
        return ValidScale(canvas.scaleFactor);
    }

    private static float ValidScale(float scale) => scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale)
        ? Mathf.Max(.001f, scale) : 1f;
}
