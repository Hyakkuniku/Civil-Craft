using UnityEngine;

public sealed class CompletionSafeArea : MonoBehaviour
{
    private RectTransform rect;
    private RectTransform parent;
    private Canvas canvas;
    private Rect lastSafeArea;
    private Rect lastParentRect;
    private Camera lastCamera;
    private bool hasApplied;

    private void OnEnable()
    {
        rect = transform as RectTransform;
        parent = rect != null ? rect.parent as RectTransform : null;
        canvas = GetComponentInParent<Canvas>();
        hasApplied = false;
        Apply();
    }
    private void LateUpdate() { Apply(); }
    private void Apply()
    {
        if (rect == null || parent == null || canvas == null) return;
        Rect parentRect = parent.rect;
        if (parentRect.width <= 0 || parentRect.height <= 0) return;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Rect safe = Screen.safeArea;
        if (hasApplied && safe == lastSafeArea && parentRect == lastParentRect && camera == lastCamera)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.min, camera, out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.max, camera, out Vector2 max);
        rect.anchorMin = new Vector2(Mathf.Clamp01((min.x - parentRect.xMin) / parentRect.width),
            Mathf.Clamp01((min.y - parentRect.yMin) / parentRect.height));
        rect.anchorMax = new Vector2(Mathf.Clamp01((max.x - parentRect.xMin) / parentRect.width),
            Mathf.Clamp01((max.y - parentRect.yMin) / parentRect.height));
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        lastSafeArea = safe;
        lastParentRect = parentRect;
        lastCamera = camera;
        hasApplied = true;
    }
}
