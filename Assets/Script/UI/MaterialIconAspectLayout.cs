using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fits this one icon in root-canvas units, compensating legacy nonuniform dock
/// scale. The input cell is never resized and selection lifts only the artwork.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform), typeof(Image))]
public sealed class MaterialIconAspectLayout : MonoBehaviour
{
    public RectTransform fitBox;
    public RectTransform selectionMotionRoot;
    public Sprite selectedSprite;
    public float selectedUpOffset = 10f;
    public Vector2 maximumCanvasSize = new Vector2(104f, 86f);
    public Vector2 canvasPadding = new Vector2(8f, 6f);

    [Header("Cropped Sprite Pair Registration")]
    [Tooltip("Optional unselected sprite sharing a pixel frame with Selected Sprite. A zero frame size retains the original independent-fit behavior.")]
    public Sprite registrationDefaultSprite;
    [Tooltip("Shared virtual canvas in unselected-source pixel units, containing both registered images. Neither texture nor importer is changed.")]
    public Vector2 registeredPixelCanvasSize;
    [Tooltip("Unselected image center relative to the shared canvas center, in reference-frame pixel units; positive Y points up.")]
    public Vector2 defaultPixelCenterOffset;
    [Tooltip("Selected image center relative to the shared canvas center, in reference-frame pixel units; positive Y points up.")]
    public Vector2 selectedPixelCenterOffset;
    [Min(.01f)]
    [Tooltip("Optional uniform correction when the selected source artwork was authored at a different scale. Does not stretch either image axis independently.")]
    public float selectedRegistrationScale = 1f;

    private RectTransform icon;
    private Image image;
    private Canvas rootCanvas;
    public string LastFailureReason { get; private set; }

    private void OnEnable()
    {
        Cache();
        if (Application.isPlaying) RefreshNow();
    }

    private void LateUpdate()
    {
        if (Application.isPlaying) RefreshNow();
    }

    private void Cache()
    {
        if (icon == null) icon = (RectTransform)transform;
        if (image == null) image = GetComponent<Image>();
        if (rootCanvas == null)
        {
            // Inactive prefab contents need not be registered with Unity's
            // native canvas system yet. Resolve the outermost authored Canvas
            // structurally instead of relying on Canvas.rootCanvas being ready.
            for (Transform cursor = transform.parent; cursor != null; cursor = cursor.parent)
            {
                Canvas canvas = cursor.GetComponent<Canvas>();
                if (canvas != null) rootCanvas = canvas;
            }
        }
    }

    /// <summary>Also callable by detached Editor previews; no scene searches.</summary>
    public bool RefreshNow()
    {
        Cache();
        LastFailureReason = null;
        if (fitBox == null || icon.parent == null || image.sprite == null || rootCanvas == null)
            return Fail("Missing fit box, icon parent, sprite or authored ancestor Canvas.");
        Transform canvas = rootCanvas.transform;
        if (!TryCanvasAxes(icon.parent, canvas, out Vector3 parentRight, out Vector3 parentUp) ||
            !TryCanvasAxes(fitBox, canvas, out Vector3 boxRight, out Vector3 boxUp))
            return Fail("Icon/fit box does not descend from the authored root Canvas.");
        float parentX = parentRight.magnitude, parentY = parentUp.magnitude;
        float boxX = boxRight.magnitude, boxY = boxUp.magnitude;
        if (parentX < .00001f || parentY < .00001f || boxX < .00001f || boxY < .00001f)
            return Fail("A transform below the Canvas has a degenerate axis: parent=" + parentRight + "/" + parentUp +
                "; fit=" + boxRight + "/" + boxUp + ". The Canvas's own scale is deliberately excluded.");

        // MaterialButtonTrigger normally lifts its whole RectTransform. Route that
        // motion to the icon only, retaining a stationary full-cell touch surface.
        if (selectionMotionRoot != null && selectionMotionRoot.anchoredPosition != Vector2.zero)
            selectionMotionRoot.anchoredPosition = Vector2.zero;
        bool selected = selectedSprite != null && image.sprite == selectedSprite;
        // Treat the configured lift as canvas units too. Otherwise a legacy
        // Y-scale of 3.34 would turn a modest 10-unit selection lift into 33.
        float lift = Mathf.Max(0f, selectedUpOffset);

        float width = Mathf.Min(maximumCanvasSize.x, fitBox.rect.width * boxX - canvasPadding.x * 2f);
        float height = Mathf.Min(maximumCanvasSize.y, fitBox.rect.height * boxY - canvasPadding.y * 2f - lift);
        if (width <= 0f || height <= 0f)
            return Fail("The fit box has no positive padded area: rect=" + fitBox.rect + "; axes=" + boxX + "/" + boxY +
                "; available=" + width + "/" + height + ".");
        Rect spriteRect = image.sprite.rect;
        if (spriteRect.width <= 0f || spriteRect.height <= 0f) return Fail("Sprite has no positive rect.");
        Vector2 fitted;
        Vector2 center = new Vector2(0f, selected ? lift * .5f : -lift * .5f);
        bool registeredPair = registrationDefaultSprite != null && selectedSprite != null &&
            (image.sprite == registrationDefaultSprite || image.sprite == selectedSprite) &&
            (registeredPixelCanvasSize.x != 0f || registeredPixelCanvasSize.y != 0f);
        if (registeredPair)
        {
            if (!Finite(registeredPixelCanvasSize) || registeredPixelCanvasSize.x <= 0f || registeredPixelCanvasSize.y <= 0f)
                return Fail("The registered sprite pair requires a positive finite shared pixel canvas.");
            Vector2 pixelOffset = selected ? selectedPixelCenterOffset : defaultPixelCenterOffset;
            if (!Finite(pixelOffset)) return Fail("The registered sprite center offset is not finite.");
            float imagePixelScale = selected ? selectedRegistrationScale : 1f;
            if (float.IsNaN(imagePixelScale) || float.IsInfinity(imagePixelScale) || imagePixelScale <= 0f)
                return Fail("The registered selected artwork scale must be positive and finite.");
            Vector2 registeredImageSize = spriteRect.size * imagePixelScale;
            Vector2 halfImage = registeredImageSize * .5f;
            Vector2 halfFrame = registeredPixelCanvasSize * .5f;
            if (Mathf.Abs(pixelOffset.x) + halfImage.x > halfFrame.x + .01f ||
                Mathf.Abs(pixelOffset.y) + halfImage.y > halfFrame.y + .01f)
                return Fail("The registered image leaves its shared pixel canvas; check the frame and center offsets.");

            // Fit one virtual canvas, not each crop independently. Both states
            // use the same registered-frame scale; an authored source-art scale
            // correction remains uniform, and the outline gets its own margin.
            float pixelsToCanvas = Mathf.Min(width / registeredPixelCanvasSize.x, height / registeredPixelCanvasSize.y);
            fitted = registeredImageSize * pixelsToCanvas;
            center += pixelOffset * pixelsToCanvas;
        }
        else
        {
            float ratio = spriteRect.width / spriteRect.height;
            fitted = width / height > ratio
                ? new Vector2(height * ratio, height)
                : new Vector2(width, width / ratio);
        }
        Vector2 position = new Vector2(center.x / parentX, center.y / parentY);
        if (icon.anchoredPosition != position) icon.anchoredPosition = position;
        Vector3 scale = new Vector3(1f / parentX, 1f / parentY, 1f);
        if (icon.localScale != scale) icon.localScale = scale;
        if (icon.sizeDelta != fitted) icon.sizeDelta = fitted;
        if (!image.preserveAspect) image.preserveAspect = true;
        return true;
    }

    private bool Fail(string reason) { LastFailureReason = reason; return false; }

    private static bool Finite(Vector2 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y);

    private static bool TryCanvasAxes(Transform child, Transform canvas, out Vector3 right, out Vector3 up)
    {
        // Do not use worldToLocal * localToWorld: a dormant Screen Space Canvas
        // can legitimately have serialized scale zero in prefab contents. Local
        // ancestry matrices exclude that root entirely, without modifying it.
        Matrix4x4 basis = Matrix4x4.identity;
        Transform cursor = child;
        while (cursor != null && cursor != canvas)
        {
            basis = Matrix4x4.TRS(Vector3.zero, cursor.localRotation, cursor.localScale) * basis;
            cursor = cursor.parent;
        }
        right = basis.MultiplyVector(Vector3.right);
        up = basis.MultiplyVector(Vector3.up);
        return cursor == canvas;
    }
}
