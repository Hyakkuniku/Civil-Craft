using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Shared elevated side view for completion, saved-bridge and guest photos.</summary>
public static class BridgePhotoCapture
{
    public const float ElevationDegrees = 25f;
    private const float FramingPadding = 1.12f;

    public static Camera GetCamera(BuildLocation location)
    {
        if (location == null) return null;
        Camera camera = location.cinematicCamera;
        if (camera != null && camera.gameObject.activeInHierarchy) return camera;
        camera = location.locationCamera;
        return camera != null && camera.gameObject.activeInHierarchy ? camera : null;
    }

    public static bool TryGetBounds(BuildLocation location, out Bounds bounds, IEnumerable<Bar> members = null)
    {
        bounds = default;
        if (location == null) return false;

        bool found = false;
        Bounds collected = default;
        var included = new HashSet<Bar>();
        foreach (Point point in location.startingAnchors) IncludePoint(point);
        foreach (Point point in location.endingAnchors) IncludePoint(point);
        foreach (Bar bar in location.bakedBars) IncludeMember(bar);
        foreach (Bar bar in members ?? Object.FindObjectsOfType<Bar>(true))
            IncludeMember(bar);
        if (!found) return false;
        bounds = collected;
        return true;

        void IncludeMember(Bar bar)
        {
            if (!BridgeConstructionReceipt.IsLogicalMember(bar) || !location.Owns(bar) ||
                !bar.gameObject.activeInHierarchy || bar.gameObject.scene != location.gameObject.scene ||
                !included.Add(bar))
                return;
            IncludePoint(bar.startPoint);
            IncludePoint(bar.endPoint);
            // Renderer bounds include the paired rails, rope sag and the fitted
            // pier heads, which endpoint positions alone cannot describe.
            foreach (Renderer renderer in bar.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer)
                    continue;
                Include(renderer.bounds);
            }
        }
        // Capture the local bounds instead of the out parameter in these functions.
        void IncludePoint(Point point)
        {
            if (point == null) return;
            Include(new Bounds(point.transform.position, Vector3.one));
        }

        void Include(Bounds next)
        {
            if (!Finite(next.center) || !Finite(next.extents)) return;
            if (found) collected.Encapsulate(next);
            else { collected = next; found = true; }
        }
    }

    /// <summary>Fits all eight corners in the capture's actual aspect ratio.</summary>
    public static void Frame(Camera camera, Bounds bounds, float aspect)
    {
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        if (!Finite(bounds.center) || !Finite(bounds.extents) || !float.IsFinite(aspect) || aspect <= 0f)
            throw new ArgumentException("Bridge photo bounds and aspect must be finite.");

        Quaternion rotation = Quaternion.Euler(ElevationDegrees, 0f, 0f);
        Quaternion inverse = Quaternion.Inverse(rotation);
        Vector3 extent = Vector3.zero;
        foreach (Vector3 corner in Corners(bounds))
        {
            Vector3 projected = inverse * (corner - bounds.center);
            extent.x = Mathf.Max(extent.x, Mathf.Abs(projected.x));
            extent.y = Mathf.Max(extent.y, Mathf.Abs(projected.y));
            extent.z = Mathf.Max(extent.z, Mathf.Abs(projected.z));
        }
        float size = Mathf.Max(2.5f, Mathf.Max(extent.y, extent.x / aspect) * FramingPadding + .5f);
        float distance = Mathf.Max(40f, extent.z + size + 10f);
        camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        camera.aspect = aspect;
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = distance + extent.z + 20f;
        camera.useOcclusionCulling = false;
        camera.ResetProjectionMatrix();
    }

    public static Texture2D Capture(BuildLocation location, int width, int height)
    {
        Camera camera = GetCamera(location);
        if (camera == null || !TryGetBounds(location, out Bounds bounds)) return null;
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));

        Transform transform = camera.transform;
        Vector3 previousPosition = transform.position;
        Quaternion previousRotation = transform.rotation;
        bool previousOrthographic = camera.orthographic;
        float previousSize = camera.orthographicSize;
        float previousAspect = camera.aspect;
        float previousNear = camera.nearClipPlane;
        float previousFar = camera.farClipPlane;
        bool previousOcclusion = camera.useOcclusionCulling;
        Rect previousRect = camera.rect;
        Matrix4x4 previousProjection = camera.projectionMatrix;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        bool gridEnabled = location.gridImage != null && location.gridImage.enabled;
        RenderTexture render = null;
        Texture2D photo = null;
        bool customProjection = true;
        try
        {
            camera.ResetProjectionMatrix();
            customProjection = camera.projectionMatrix != previousProjection;
            render = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = render;
            if (gridEnabled) location.gridImage.enabled = false;
            Frame(camera, bounds, (float)width / height);
            // Camera.Render also works for a disabled capture camera.
            camera.Render();
            RenderTexture.active = render;
            photo = new Texture2D(width, height, TextureFormat.RGB24, false);
            photo.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            photo.Apply(false, false);
            return photo;
        }
        catch (Exception exception)
        {
            if (photo != null) Object.Destroy(photo);
            Debug.LogWarning($"[Bridge Photo] Could not capture '{location.name}': {exception.Message}", location);
            return null;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            transform.SetPositionAndRotation(previousPosition, previousRotation);
            camera.orthographic = previousOrthographic;
            camera.orthographicSize = previousSize;
            camera.rect = previousRect;
            camera.aspect = previousAspect;
            camera.nearClipPlane = previousNear;
            camera.farClipPlane = previousFar;
            camera.useOcclusionCulling = previousOcclusion;
            if (customProjection) camera.projectionMatrix = previousProjection;
            else camera.ResetProjectionMatrix();
            if (gridEnabled) location.gridImage.enabled = true;
            if (render != null) RenderTexture.ReleaseTemporary(render);
        }
    }

    public static IEnumerable<Vector3> Corners(Bounds bounds)
    {
        Vector3 min = bounds.min, max = bounds.max;
        for (int corner = 0; corner < 8; corner++)
            yield return new Vector3((corner & 1) == 0 ? min.x : max.x,
                (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
