using UnityEngine;

/// <summary>Frames the complete construction area in an orthographic front view.</summary>
public static class BuildCameraFraming
{
    // Keep the banks and drawing area clear of the top HUD and bottom toolbar.
    public static readonly Rect ConstructionViewport = new Rect(.07f, .18f, .86f, .64f);

    public static bool TryGetBounds(BuildLocation location, out Bounds bounds)
    {
        if (!BridgePhotoCapture.TryGetBounds(location, out bounds)) return false;
        if (location.gridImage != null)
        {
            Canvas canvas = location.gridImage.GetComponentInParent<Canvas>(true);
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                Vector3[] corners = new Vector3[4];
                location.gridImage.rectTransform.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                    if (float.IsFinite(corner.x) && float.IsFinite(corner.y) && float.IsFinite(corner.z))
                        bounds.Encapsulate(corner);
            }
        }
        // Show a landing on each bank, beyond the red anchor itself.
        bounds.Expand(new Vector3(Mathf.Max(8f, bounds.size.x * .12f), 4f, 0f));
        return true;
    }

    public static bool TryFrame(BuildLocation location)
    {
        if (location == null || location.locationCamera == null || !TryGetBounds(location, out Bounds bounds))
            return false;
        Frame(location.locationCamera, bounds);
        return true;
    }

    public static void Frame(Camera camera, Bounds bounds)
    {
        float aspect = camera.aspect;
        if (!float.IsFinite(aspect) || aspect <= 0f) return;
        float size = Mathf.Max(2.5f, Mathf.Max(bounds.extents.y / ConstructionViewport.height,
            bounds.extents.x / (aspect * ConstructionViewport.width)));
        Vector2 offset = ConstructionViewport.center - Vector2.one * .5f;
        Vector3 position = bounds.center - new Vector3(offset.x * size * 2f * aspect, offset.y * size * 2f, 0f);
        float distance = Mathf.Max(100f, bounds.size.z + 30f);
        position.z = bounds.min.z - distance;
        camera.transform.SetPositionAndRotation(position, Quaternion.identity);
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance + bounds.size.z + 100f);
        camera.useOcclusionCulling = false;
        camera.ResetProjectionMatrix();
    }
}
