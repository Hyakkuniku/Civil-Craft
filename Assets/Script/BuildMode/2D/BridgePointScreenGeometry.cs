using UnityEngine;

/// <summary>
/// Projects a renderer's local bounds into a conservative touch footprint.
/// Including the bounds around the visible mesh intentionally gives fingers
/// a little forgiveness without adding colliders or making meshes readable.
/// </summary>
public static class BridgePointScreenGeometry
{
    // Unity camera/renderer APIs are main-thread only. Reusing these buffers
    // keeps repeated bridge-node picking allocation-free.
    private static readonly Vector3[] WorldCorners = new Vector3[8];
    private static readonly float[] CornerDepths = new float[8];
    private static readonly Vector2[] ProjectedVertices = new Vector2[32];
    private static readonly Vector2[] Hull = new Vector2[64];
    private static readonly Vector2[] ClipA = new Vector2[40];
    private static readonly Vector2[] ClipB = new Vector2[40];
    private static readonly int[] Edges =
    {
        0, 1, 2, 3, 4, 5, 6, 7,
        0, 2, 1, 3, 4, 6, 5, 7,
        0, 4, 1, 5, 2, 6, 3, 7
    };

    /// <summary>
    /// Returns false when the renderer has no visible bounds footprint in this
    /// camera's viewport. Distance is zero inside the footprint, otherwise it
    /// is the shortest screen-pixel distance to its visible edge.
    /// </summary>
    public static bool TryGetDistance(Camera camera, Renderer renderer,
        Vector2 screenPoint, out float edgeDistancePixels)
    {
        return TryGetProjectedDistance(camera, renderer, screenPoint, false, 0f, out edgeDistancePixels);
    }

    /// <summary>
    /// Removes visual depth before projecting the footprint onto the logical
    /// bridge plane. A long node viewed obliquely must not become a long magnet
    /// for a destination whose actual connection stays on that plane.
    /// </summary>
    public static bool TryGetPlaneDistance(Camera camera, Renderer renderer,
        Vector2 screenPoint, float planeZ, out float edgeDistancePixels)
    {
        edgeDistancePixels = float.PositiveInfinity;
        return IsFinite(planeZ) &&
            TryGetProjectedDistance(camera, renderer, screenPoint, true, planeZ, out edgeDistancePixels);
    }

    private static bool TryGetProjectedDistance(Camera camera, Renderer renderer,
        Vector2 screenPoint, bool flattenDepth, float planeZ, out float edgeDistancePixels)
    {
        edgeDistancePixels = float.PositiveInfinity;
        if (camera == null || renderer == null || !renderer.enabled ||
            renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
            (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
            !IsFinite(screenPoint))
            return false;

        Rect viewport = camera.pixelRect;
        if (!IsFinite(viewport.xMin) || !IsFinite(viewport.xMax) ||
            !IsFinite(viewport.yMin) || !IsFinite(viewport.yMax) ||
            viewport.width <= 0f || viewport.height <= 0f ||
            !viewport.Contains(screenPoint))
            return false;

        float near = Mathf.Max(0.0001f, camera.nearClipPlane);
        float far = camera.farClipPlane;
        if (!IsFinite(near) || !IsFinite(far) || far <= near) return false;

        Bounds bounds = renderer.localBounds;
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Matrix4x4 localToWorld = renderer.localToWorldMatrix;
        Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
        int projectedCount = 0;
        for (int i = 0; i < 8; i++)
        {
            Vector3 local = new Vector3((i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y,
                (i & 4) == 0 ? min.z : max.z);
            Vector3 world = localToWorld.MultiplyPoint3x4(local);
            if (!IsFinite(world)) return false;
            if (flattenDepth) world.z = planeZ;
            WorldCorners[i] = world;
            // Unity's view matrix looks along negative Z; WorldToScreenPoint
            // exposes the corresponding positive forward distance.
            float depth = -worldToCamera.MultiplyPoint3x4(world).z;
            if (!IsFinite(depth)) return false;
            CornerDepths[i] = depth;
            if (depth >= near && depth <= far &&
                !AppendProjected(camera, world, ref projectedCount))
                return false;
        }

        // Clipping the box's edges before projecting avoids mirrored or
        // unbounded screen hulls when a node crosses the camera's near plane.
        for (int i = 0; i < Edges.Length; i += 2)
        {
            int a = Edges[i];
            int b = Edges[i + 1];
            if (!AppendDepthIntersection(camera, a, b, near, ref projectedCount) ||
                !AppendDepthIntersection(camera, a, b, far, ref projectedCount))
                return false;
        }
        if (projectedCount == 0) return false;

        int hullCount = BuildConvexHull(projectedCount);
        for (int i = 0; i < hullCount; i++) ClipA[i] = Hull[i];
        int clippedCount = hullCount;
        Vector2[] input = ClipA;
        Vector2[] output = ClipB;
        for (int boundary = 0; boundary < 4; boundary++)
        {
            clippedCount = ClipToViewport(input, clippedCount, output, viewport, boundary);
            if (clippedCount == 0) return false;
            Vector2[] swap = input;
            input = output;
            output = swap;
        }

        edgeDistancePixels = DistanceToConvexPolygon(screenPoint, input, clippedCount);
        return IsFinite(edgeDistancePixels);
    }

    /// <summary>
    /// Allocation-free distance for a clockwise or counterclockwise convex
    /// polygon, also accepting a single point or a degenerate line segment.
    /// </summary>
    public static float DistanceToConvexPolygon(Vector2 point, Vector2[] polygon, int count)
    {
        if (polygon == null || count <= 0 || count > polygon.Length || !IsFinite(point))
            return float.PositiveInfinity;
        bool positive = false;
        bool negative = false;
        double signedArea = 0d;
        double closestSquared = double.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % count];
            if (!IsFinite(a) || !IsFinite(b)) return float.PositiveInfinity;
            double cross = Cross(a, b, point);
            positive |= cross > 0d;
            negative |= cross < 0d;
            signedArea += (double)a.x * b.y - (double)b.x * a.y;
            double dx = (double)b.x - a.x;
            double dy = (double)b.y - a.y;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared > 0d
                ? (((double)point.x - a.x) * dx + ((double)point.y - a.y) * dy) / lengthSquared
                : 0d;
            t = System.Math.Max(0d, System.Math.Min(1d, t));
            double offsetX = (double)point.x - a.x - t * dx;
            double offsetY = (double)point.y - a.y - t * dy;
            closestSquared = System.Math.Min(closestSquared, offsetX * offsetX + offsetY * offsetY);
        }
        if (count >= 3 && signedArea != 0d && !(positive && negative)) return 0f;
        return (float)System.Math.Sqrt(closestSquared);
    }

    private static bool AppendDepthIntersection(Camera camera, int a, int b,
        float plane, ref int count)
    {
        float depthA = CornerDepths[a];
        float depthB = CornerDepths[b];
        if (!((depthA < plane && depthB > plane) || (depthA > plane && depthB < plane)))
            return true;
        double t = ((double)plane - depthA) / ((double)depthB - depthA);
        Vector3 worldA = WorldCorners[a];
        Vector3 worldB = WorldCorners[b];
        Vector3 intersection = new Vector3(
            (float)(worldA.x + ((double)worldB.x - worldA.x) * t),
            (float)(worldA.y + ((double)worldB.y - worldA.y) * t),
            (float)(worldA.z + ((double)worldB.z - worldA.z) * t));
        return AppendProjected(camera, intersection, ref count);
    }

    private static bool AppendProjected(Camera camera, Vector3 world, ref int count)
    {
        if (count >= ProjectedVertices.Length) return false;
        Vector3 screen = camera.WorldToScreenPoint(world);
        if (!IsFinite(screen)) return false;
        ProjectedVertices[count++] = new Vector2(screen.x, screen.y);
        return true;
    }

    private static int BuildConvexHull(int count)
    {
        // At most 32 points: insertion sort avoids comparer/delegate allocations.
        for (int i = 1; i < count; i++)
        {
            Vector2 point = ProjectedVertices[i];
            int j = i - 1;
            while (j >= 0 && (ProjectedVertices[j].x > point.x ||
                   (ProjectedVertices[j].x == point.x && ProjectedVertices[j].y > point.y)))
            {
                ProjectedVertices[j + 1] = ProjectedVertices[j];
                j--;
            }
            ProjectedVertices[j + 1] = point;
        }

        int uniqueCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (uniqueCount > 0 && ProjectedVertices[i] == ProjectedVertices[uniqueCount - 1])
                continue;
            ProjectedVertices[uniqueCount++] = ProjectedVertices[i];
        }
        if (uniqueCount == 1)
        {
            Hull[0] = ProjectedVertices[0];
            return 1;
        }

        int hullCount = 0;
        for (int i = 0; i < uniqueCount; i++)
        {
            Vector2 point = ProjectedVertices[i];
            while (hullCount >= 2 && Cross(Hull[hullCount - 2], Hull[hullCount - 1], point) <= 0d)
                hullCount--;
            Hull[hullCount++] = point;
        }
        int upperStart = hullCount + 1;
        for (int i = uniqueCount - 2; i >= 0; i--)
        {
            Vector2 point = ProjectedVertices[i];
            while (hullCount >= upperStart && Cross(Hull[hullCount - 2], Hull[hullCount - 1], point) <= 0d)
                hullCount--;
            Hull[hullCount++] = point;
        }
        return hullCount - 1;
    }

    private static int ClipToViewport(Vector2[] input, int count, Vector2[] output,
        Rect viewport, int boundary)
    {
        int outputCount = 0;
        Vector2 previous = input[count - 1];
        bool previousInside = InsideBoundary(previous, viewport, boundary);
        for (int i = 0; i < count; i++)
        {
            Vector2 current = input[i];
            bool currentInside = InsideBoundary(current, viewport, boundary);
            if (currentInside != previousInside)
                output[outputCount++] = IntersectBoundary(previous, current, viewport, boundary);
            if (currentInside) output[outputCount++] = current;
            previous = current;
            previousInside = currentInside;
        }
        return outputCount;
    }

    private static bool InsideBoundary(Vector2 point, Rect viewport, int boundary)
    {
        switch (boundary)
        {
            case 0: return point.x >= viewport.xMin;
            case 1: return point.x <= viewport.xMax;
            case 2: return point.y >= viewport.yMin;
            default: return point.y <= viewport.yMax;
        }
    }

    private static Vector2 IntersectBoundary(Vector2 a, Vector2 b, Rect viewport, int boundary)
    {
        if (boundary < 2)
        {
            float x = boundary == 0 ? viewport.xMin : viewport.xMax;
            double t = ((double)x - a.x) / ((double)b.x - a.x);
            return new Vector2(x, (float)(a.y + ((double)b.y - a.y) * t));
        }
        float y = boundary == 2 ? viewport.yMin : viewport.yMax;
        double fraction = ((double)y - a.y) / ((double)b.y - a.y);
        return new Vector2((float)(a.x + ((double)b.x - a.x) * fraction), y);
    }

    private static double Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        return ((double)b.x - a.x) * ((double)c.y - a.y) -
               ((double)b.y - a.y) * ((double)c.x - a.x);
    }

    private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
