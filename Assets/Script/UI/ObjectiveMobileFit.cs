using UnityEngine;

/// <summary>Fits the existing clipboard as one unit; does not recolor or rebuild it.</summary>
[DisallowMultipleComponent]
public sealed class ObjectiveMobileFit : MonoBehaviour
{
    private Rect lastParent;
    private Rect lastSafe;
    private Vector2Int lastScreen;
    private bool applied;

    private void OnEnable() { applied = false; ApplyLayout(); }
    private void LateUpdate()
    {
        RectTransform rect = transform as RectTransform;
        if (rect == null || !(rect.parent is RectTransform parent)) return;
        if (!applied || parent.rect != lastParent || Screen.safeArea != lastSafe ||
            Screen.width != lastScreen.x || Screen.height != lastScreen.y) ApplyLayout();
    }

    public void ApplyLayout()
    {
        RectTransform rect = transform as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>(true);
        if (rect == null || canvas == null) return;
        // The gameplay utility wrapper is deliberately small. The clipboard
        // remains an independent modal, with all existing controls and callbacks.
        if (rect.parent != canvas.rootCanvas.transform)
            rect.SetParent(canvas.rootCanvas.transform, false);
        RectTransform parent = rect.parent as RectTransform;
        if (parent == null || parent.rect.width < 100f || parent.rect.height < 100f) return;
        Rect available = parent.rect;
        if (Application.isPlaying && Screen.width > 0 && Screen.height > 0)
        {
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.min, camera, out Vector2 min) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.max, camera, out Vector2 max))
            {
                min = Vector2.Max(min, available.min); max = Vector2.Min(max, available.max);
                if (max.x > min.x && max.y > min.y) available = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }
        // Include the decorative clipboard clip above the card in the fit.
        float scale = Mathf.Min(1.15f, Mathf.Min((available.width - 64f) / 1180f, (available.height - 64f) / 993f));
        scale = Mathf.Max(.1f, scale);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(1180f, 960f);
        rect.localScale = Vector3.one * scale;
        rect.anchoredPosition = available.center - parent.rect.center + new Vector2(0f, -16.5f * scale);
        lastParent = parent.rect; lastSafe = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height); applied = true;
    }
}
