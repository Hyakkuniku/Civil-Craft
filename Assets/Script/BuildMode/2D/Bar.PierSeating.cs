using System.Collections.Generic;
using UnityEngine;

public partial class Bar
{
    private const float PierCapNormalClearance = 0f;
    private const float OnePiecePierSeamClearance = .004f;
    private const float MinimumSeatingRoadCosine = 0.05f;
    private static readonly Dictionary<GameObject, PierSeatingGeometry> seatingGeometry = new Dictionary<GameObject, PierSeatingGeometry>();
    private GameObject seatingCapSource;
    private Vector3 seatingCapScale;
    private Bounds seatingCapBounds;
    private bool seatingCapBoundsValid;
    private PierCapBearing seatingBearing;
    private bool bearingRestored;
    private int bearingSignature;
    private bool bearingFitCached;
    private bool collectBearingPatches;
    private readonly List<SeatingRoadPatch> bearingPatches = new List<SeatingRoadPatch>();

    private struct SeatingRoadPatch
    {
        public float minX, maxX, minZ, maxZ, slope, intercept;
        public float Height(float x) => slope * x + intercept;
        public override int GetHashCode() => unchecked(((((minX.GetHashCode() * 397 ^ maxX.GetHashCode()) * 397 ^ minZ.GetHashCode()) * 397 ^
            maxZ.GetHashCode()) * 397 ^ slope.GetHashCode()) * 397 ^ intercept.GetHashCode());
    }

    private sealed class PierSeatingGeometry
    {
        public readonly Vector3[] corners;
        public readonly int firstRendererStart;
        public PierSeatingGeometry(Vector3[] corners, int firstRendererStart)
        { this.corners = corners; this.firstRendererStart = firstRendererStart; }
    }

    private void ClearPierSeatingCache()
    {
        seatingCapSource = null; seatingCapBoundsValid = false; bearingSignature = 0; bearingFitCached = false;
        seatingBearing = null; bearingRestored = true;
    }

    // Bounds are read once from source meshes, without instantiating or modifying
    // their prefabs. Nested offsets/rotations are retained in source-root space.
    private static PierSeatingGeometry SeatingGeometry(GameObject source)
    {
        if (source == null) return null;
        if (seatingGeometry.TryGetValue(source, out PierSeatingGeometry cached)) return cached;
        var points = new List<Vector3>();
        Renderer first = source.GetComponentInChildren<Renderer>(true);
        int firstStart = -1;
        foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null) continue;
            Matrix4x4 toRoot = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds bounds = filter.sharedMesh.bounds;
            if (filter.GetComponent<Renderer>() == first) firstStart = points.Count;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                points.Add(toRoot.MultiplyPoint3x4(point));
            }
        }
        cached = new PierSeatingGeometry(points.ToArray(), firstStart);
        seatingGeometry[source] = cached;
        return cached;
    }

    private bool TryGetAuthoredPierCapOffsets(out float top, out float bottom)
    {
        top = bottom = 0f;
        PierSeatingGeometry geometry = materialData != null ? SeatingGeometry(materialData.pierCapPrefab) : null;
        if (geometry == null || geometry.firstRendererStart < 0) return false;
        Vector3 scale = Vector3.Scale(Abs(originalCapScale), Abs(transform.lossyScale));
        bottom = float.PositiveInfinity; top = float.NegativeInfinity;
        for (int index = geometry.firstRendererStart; index < geometry.firstRendererStart + 8; index++)
        {
            float y = Vector3.Scale(geometry.corners[index], scale).y;
            bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
        }
        return IsFinite(top) && IsFinite(bottom);
    }

    private bool TryGetPierSeatingBounds(out Bounds bounds)
    {
        bounds = default;
        GameObject source = materialData.pierCapPrefab;
        Vector3 sourceScale = originalCapScale;
        if (source == null)
        {
            source = materialData.segmentPrefab;
            if (source == null) return false;
            sourceScale = source.transform.localScale;
            sourceScale.z *= SegmentDepthScale();
        }
        Vector3 scale = Vector3.Scale(Abs(sourceScale), Abs(transform.lossyScale));
        if (seatingCapBoundsValid && seatingCapSource == source && seatingCapScale == scale)
        { bounds = seatingCapBounds; return true; }
        PierSeatingGeometry geometry = SeatingGeometry(source);
        if (geometry == null || geometry.corners.Length == 0) return false;
        bounds = new Bounds(Vector3.Scale(geometry.corners[0], scale), Vector3.zero);
        for (int index = 1; index < geometry.corners.Length; index++) bounds.Encapsulate(Vector3.Scale(geometry.corners[index], scale));
        if (WideRoadWidth > 0f) bounds.center = new Vector3(bounds.center.x, bounds.center.y, 0f);
        seatingCapSource = source; seatingCapScale = scale; seatingCapBounds = bounds; seatingCapBoundsValid = true;
        return true;
    }

    internal Vector4 CaptureWidePierSeat()
    {
        if (WideRoadWidth <= 0f || materialData == null || !materialData.isPier ||
            startPoint == null || endPoint == null || visualSegments.Count == 0)
            return Vector4.zero;

        RefreshPierCapSeating();
        Transform shaft = visualSegments[0].transform;
        float capOffset = 0f;
        float capAngle = 0f;
        if (pierCapInstance != null)
        {
            Transform cap = pierCapInstance.transform;
            float top = Mathf.Max(startPoint.transform.position.y, endPoint.transform.position.y);
            capOffset = cap.position.y - top;
            capAngle = Mathf.DeltaAngle(0f, cap.eulerAngles.z);
        }
        return new Vector4(shaft.localPosition.y, shaft.localScale.y, capOffset, capAngle);
    }

    /// <summary>Seats the cap or one-piece pier visual without moving the structural endpoints.</summary>
    public void RefreshPierCapSeating()
    {
        if (materialData == null || !materialData.isPier || !gameObject.activeInHierarchy ||
            IsBrokenForPierSeating(this)) return;
        Point topPoint = null;
        Vector3 top = StartPosition.y > EndPosition.y ? StartPosition : EndPosition;
        if (startPoint != null && endPoint != null)
        {
            topPoint = startPoint.transform.position.y > endPoint.transform.position.y ? startPoint : endPoint;
            top = topPoint.transform.position;
        }
        if (pierCapInstance == null)
        {
            if (materialData.pierCapPrefab == null) SeatOnePiecePier(topPoint);
            return;
        }
        float firstSlope = 0f; bool found = false, uniform = true;
        if (topPoint != null)
            foreach (Bar road in topPoint.ConnectedBars)
                if (TryPierSeatingRoad(road, topPoint, top, out float slope, out _, out _, out _, out _))
                {
                    if (!found) { firstSlope = slope; found = true; }
                    else if (Mathf.Abs(slope - firstSlope) > 0.001f) uniform = false;
                }
        // Match a continuous grade. At a crest/valley the horizontal cap is
        // lowered against each finite road half, rather than extending a slope
        // infinitely across the opposite side of the joint.
        float angle = found && uniform ? Mathf.Atan(firstSlope) * Mathf.Rad2Deg : 0f;
        float drop = 0f;
        if (found && TryGetPierSeatingBounds(out Bounds capBounds))
        {
            foreach (Bar road in topPoint.ConnectedBars)
                if (TryPierSeatingRoad(road, topPoint, top, out float slope, out float underside, out float cosine, out float minX, out float maxX))
                    drop = Mathf.Max(drop, RequiredPierCapSeatingDrop(capBounds, angle, capTopOffset, slope, underside, cosine, minX, maxX));
        }
        else angle = 0f;
        Vector3 seat = top - Vector3.up * (capTopOffset + drop);
        seat.z += PierCapDepthPosition();
        pierCapInstance.transform.SetPositionAndRotation(seat, Quaternion.Euler(0f, 0f, angle));
        RefreshPierBearing(topPoint, top, found);
    }

    private void SeatOnePiecePier(Point topPoint)
    {
        float height = Mathf.Abs(EndPosition.y - StartPosition.y);
        if (visualSegments.Count == 0 || height < .01f || !TryGetPierSeatingBounds(out Bounds segmentBounds) ||
            segmentBounds.size.y < .001f) return;

        // The integral top stays horizontal. Seat its lowest contact against
        // the finite deck underside and keep the bottom at the original base.
        var topFace = new Bounds(new Vector3(segmentBounds.center.x, 0f, segmentBounds.center.z),
            new Vector3(segmentBounds.size.x, 0f, segmentBounds.size.z));
        float yScale = Mathf.Max(.001f, Mathf.Abs(transform.lossyScale.y));
        // During and after a test the bodies can be offset from the nodes. Use
        // the unseated mesh top as the reference while the node owns connectivity.
        Vector3 top = transform.TransformPoint(Vector3.up * (height * .5f / yScale));
        Vector3 up = transform.up;
        float tilt = Mathf.Atan2(-up.x, up.y) * Mathf.Rad2Deg;
        float drop = 0f;
        if (topPoint != null)
            foreach (Bar road in topPoint.ConnectedBars)
            {
                if (TryPierSeatingRoad(road, topPoint, top, out float slope, out float underside, out float cosine, out float minX, out float maxX))
                {
                    float needed = RequiredPierCapSeatingDrop(topFace, tilt, 0f, slope, underside, cosine, minX, maxX);
                    float dropPerMeter = up.y - slope * up.x;
                    if (needed > 0f && dropPerMeter > MinimumSeatingRoadCosine)
                        drop = Mathf.Max(drop, (needed + OnePiecePierSeamClearance) / dropPerMeter);
                }
            }

        drop = Mathf.Min(drop, Mathf.Max(0f, height - .05f));
        float localShift = -drop * .5f / yScale;
        Vector3 scale = Abs(materialData.segmentPrefab.transform.localScale);
        scale.y *= (height - drop) / segmentBounds.size.y;
        scale.z *= SegmentDepthScale();
        foreach (GameObject segment in visualSegments)
        {
            Transform visual = segment.transform;
            Vector3 position = visual.localPosition;
            position.y = localShift;
            if (visual.localPosition != position) visual.localPosition = position;
            if (visual.localScale != scale) visual.localScale = scale;
        }
    }

    private bool TryPierSeatingRoad(Bar road, Point topPoint, Vector3 top, out float slope, out float underside,
        out float cosine, out float minX, out float maxX)
    {
        slope = underside = cosine = minX = maxX = 0f;
        if (road == null || road == this || !road.gameObject.activeInHierarchy || road.IsConstructionPreview ||
            road.materialData == null || !road.materialData.isRoad || road.materialData.isPier ||
            road.gameObject.scene != gameObject.scene || IsBrokenForPierSeating(road) ||
            (road.startPoint != topPoint && road.endPoint != topPoint)) return false;
        BuildLocation mine = OwnerLocation != null ? OwnerLocation : topPoint.OwnerLocation;
        BuildLocation theirs = road.OwnerLocation != null ? road.OwnerLocation : topPoint.OwnerLocation;
        if (mine != theirs) return false;
        Vector3 start = road.startPoint != null ? road.startPoint.transform.position : road.StartPosition;
        Vector3 end = road.endPoint != null ? road.endPoint.transform.position : road.EndPosition;
        if (start.x > end.x) { Vector3 swap = start; start = end; end = swap; }
        Vector3 span = end - start; span.z = 0f;
        float length = span.magnitude;
        if (length < 0.01f || span.x < 0.01f) return false;
        cosine = span.x / length;
        float sine = span.y / length;
        Vector3 midpoint = (start + end) * .5f;
        midpoint.z = start.z;
        PierSeatingGeometry geometry = SeatingGeometry(road.materialData.segmentPrefab);
        if (geometry == null || geometry.corners.Length == 0 || !TryGetPierSeatingBounds(out Bounds capBounds)) return false;
        Vector3 scale = Abs(road.originalScale); scale.x *= length / Mathf.Max(Mathf.Abs(road.baseLength), 0.0001f);
        Transform visual = road.visualSegments.Count > 0 && road.visualSegments[0] != null
            ? road.visualSegments[0].transform : null;
        Quaternion rotation = visual != null ? visual.localRotation : road.materialData.segmentPrefab.transform.localRotation;
        Vector3 offset = visual != null ? visual.localPosition : Vector3.zero;
        bool useRenderedPose = materialData.pierCapPrefab == null && visual != null;
        if (useRenderedPose)
        {
            midpoint = road.transform.position;
            Vector3 right = road.transform.right;
            cosine = right.x;
            sine = right.y;
            scale = Abs(visual.localScale);
        }
        if (cosine < MinimumSeatingRoadCosine) return false;
        slope = sine / cosine;
        Vector3 worldScale = Abs(road.transform.lossyScale);
        underside = float.PositiveInfinity;
        minX = float.PositiveInfinity; maxX = float.NegativeInfinity;
        float midpointX = midpoint.x - top.x;
        float undersideOffset = useRenderedPose ? (midpoint.y - top.y - slope * midpointX) * cosine : 0f;
        for (int part = 0; part < geometry.corners.Length; part += 8)
        {
            float partBottom = float.PositiveInfinity, partMinX = float.PositiveInfinity, partMaxX = float.NegativeInfinity;
            float partLocalMinX = float.PositiveInfinity, partLocalMaxX = float.NegativeInfinity;
            float partMinZ = float.PositiveInfinity, partMaxZ = float.NegativeInfinity;
            for (int corner = part; corner < part + 8; corner++)
            {
                Vector3 point = Vector3.Scale(rotation * Vector3.Scale(geometry.corners[corner], scale) + offset, worldScale);
                partBottom = Mathf.Min(partBottom, point.y);
                partLocalMinX = Mathf.Min(partLocalMinX, point.x); partLocalMaxX = Mathf.Max(partLocalMaxX, point.x);
                float worldX = midpointX + cosine * point.x - sine * point.y;
                partMinX = Mathf.Min(partMinX, worldX); partMaxX = Mathf.Max(partMaxX, worldX);
                float worldZ = midpoint.z + point.z;
                partMinZ = Mathf.Min(partMinZ, worldZ); partMaxZ = Mathf.Max(partMaxZ, worldZ);
            }
            // Outboard road beams do not determine the height of a narrower
            // cap beneath the centre deck. Keep their bounds separate until
            // actual overlap across the road's width has been established.
            if (!PierSeatingDepthOverlaps(top.z + capBounds.min.z, top.z + capBounds.max.z, partMinZ, partMaxZ)) continue;
            underside = Mathf.Min(underside, partBottom + undersideOffset);
            minX = Mathf.Min(minX, partMinX); maxX = Mathf.Max(maxX, partMaxX);
            if (collectBearingPatches)
                bearingPatches.Add(new SeatingRoadPatch {
                    minX = top.x + midpointX + cosine * partLocalMinX - sine * partBottom,
                    maxX = top.x + midpointX + cosine * partLocalMaxX - sine * partBottom,
                    minZ = partMinZ, maxZ = partMaxZ, slope = span.y / span.x,
                    intercept = top.y - span.y / span.x * top.x + partBottom / cosine
                });
        }
        return IsFinite(underside) && IsFinite(slope);
    }

    private void RefreshPierBearing(Point topPoint, Vector3 top, bool hasRoad)
    {
        if (!bearingRestored)
        {
            bearingRestored = true;
            Transform existing = transform.Find("PierBearing");
            if (existing != null) seatingBearing = existing.GetComponent<PierCapBearing>();
        }
        if (!hasRoad || !PierCapSeatingProfiles.TryGetTopFace(materialData.pierCapPrefab, out Bounds face))
        { HidePierBearing(); return; }
        bearingPatches.Clear(); collectBearingPatches = true;
        try
        {
            foreach (Bar road in topPoint.ConnectedBars)
                TryPierSeatingRoad(road, topPoint, top, out _, out _, out _, out _, out _);
        }
        finally { collectBearingPatches = false; }
        Transform cap = pierCapInstance.transform;
        Vector3 first = cap.TransformPoint(new Vector3(face.min.x, face.max.y, face.min.z));
        Vector3 last = cap.TransformPoint(new Vector3(face.max.x, face.max.y, face.max.z));
        if (last.x - first.x < .001f || last.z - first.z < .001f) { HidePierBearing(); return; }
        int signature = unchecked(first.GetHashCode() * 397 ^ last.GetHashCode());
        signature = unchecked(signature * 397 ^ transform.position.GetHashCode());
        signature = unchecked(signature * 397 ^ transform.rotation.GetHashCode());
        signature = unchecked(signature * 397 ^ transform.lossyScale.GetHashCode());
        foreach (SeatingRoadPatch patch in bearingPatches) signature = unchecked(signature * 397 ^ patch.GetHashCode());
        if (bearingFitCached && bearingSignature == signature) return;
        bearingSignature = signature; bearingFitCached = true;
        // A rectangular cap cannot follow two different grades. Fill only the
        // air above its measured top face with a finite, piecewise planar seat.
        // Split at road ends, depth edges and plane intersections: no wedge may
        // extend an incoming road plane into the opposite half of a joint.
        var xs = new List<float> { first.x, last.x }; var zs = new List<float> { first.z, last.z };
        float bottomSlope = (last.y - first.y) / (last.x - first.x);
        float bottomIntercept = first.y - bottomSlope * first.x;
        foreach (SeatingRoadPatch patch in bearingPatches)
        {
            AddSeatingCut(xs, patch.minX, first.x, last.x); AddSeatingCut(xs, patch.maxX, first.x, last.x);
            AddSeatingCut(zs, patch.minZ, first.z, last.z); AddSeatingCut(zs, patch.maxZ, first.z, last.z);
            if (Mathf.Abs(patch.slope - bottomSlope) > .00001f)
                AddSeatingCut(xs, (bottomIntercept - patch.intercept) / (patch.slope - bottomSlope), first.x, last.x);
        }
        for (int i = 0; i < bearingPatches.Count; i++)
            for (int j = i + 1; j < bearingPatches.Count; j++)
            {
                SeatingRoadPatch a = bearingPatches[i], b = bearingPatches[j];
                if (Mathf.Abs(a.slope - b.slope) > .00001f)
                    AddSeatingCut(xs, (b.intercept - a.intercept) / (a.slope - b.slope), first.x, last.x);
            }
        xs.Sort(); zs.Sort(); var vertices = new List<Vector3>();
        for (int x = 0; x < xs.Count - 1; x++)
            for (int z = 0; z < zs.Count - 1; z++)
            {
                float lo = xs[x], hi = xs[x + 1], near = zs[z], far = zs[z + 1];
                if (hi - lo < .00001f || far - near < .00001f) continue;
                float midX = (lo + hi) * .5f, midZ = (near + far) * .5f, low = float.PositiveInfinity;
                SeatingRoadPatch chosen = default;
                foreach (SeatingRoadPatch patch in bearingPatches)
                    if (midX > patch.minX && midX < patch.maxX && midZ > patch.minZ && midZ < patch.maxZ && patch.Height(midX) < low)
                    { low = patch.Height(midX); chosen = patch; }
                if (!IsFinite(low) || low - (bottomSlope * midX + bottomIntercept) < .00005f) continue;
                float b0 = bottomSlope * lo + bottomIntercept, b1 = bottomSlope * hi + bottomIntercept;
                float t0 = chosen.Height(lo), t1 = chosen.Height(hi);
                if (t0 < b0 - .0002f || t1 < b1 - .0002f) continue;
                vertices.Add(transform.InverseTransformPoint(new Vector3(lo, b0, near)));
                vertices.Add(transform.InverseTransformPoint(new Vector3(hi, b1, near)));
                vertices.Add(transform.InverseTransformPoint(new Vector3(hi, b1, far)));
                vertices.Add(transform.InverseTransformPoint(new Vector3(lo, b0, far)));
                vertices.Add(transform.InverseTransformPoint(new Vector3(lo, t0, near)));
                vertices.Add(transform.InverseTransformPoint(new Vector3(hi, t1, near)));
                vertices.Add(transform.InverseTransformPoint(new Vector3(hi, t1, far)));
                vertices.Add(transform.InverseTransformPoint(new Vector3(lo, t0, far)));
            }
        if (vertices.Count == 0 || vertices.Count > 512)
        {
            if (seatingBearing != null) seatingBearing.gameObject.SetActive(false);
            return;
        }
        if (seatingBearing == null)
        {
            var bearing = new GameObject("PierBearing"); bearing.layer = pierCapInstance.layer;
            bearing.transform.SetParent(transform, false); seatingBearing = bearing.AddComponent<PierCapBearing>();
        }
        seatingBearing.SetPrisms(vertices.ToArray(), PierCapBearing.Appearance(materialData));
        seatingBearing.gameObject.SetActive(true);
    }

    private void HidePierBearing()
    {
        bearingSignature = 0; bearingFitCached = false;
        if (seatingBearing != null) seatingBearing.gameObject.SetActive(false);
    }

    private static void AddSeatingCut(List<float> cuts, float value, float min, float max)
    {
        if (!IsFinite(value) || value <= min + .00001f || value >= max - .00001f) return;
        foreach (float old in cuts) if (Mathf.Abs(old - value) < .00001f) return;
        cuts.Add(value);
    }

    private static bool IsBrokenForPierSeating(Bar bar) => bar.TryGetComponent(out BarStressHandler stress) && stress.isBroken;
    private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static bool PierSeatingDepthOverlaps(float capMinZ, float capMaxZ, float roadMinZ, float roadMaxZ) =>
        Mathf.Min(capMaxZ, roadMaxZ) - Mathf.Max(capMinZ, roadMinZ) > 0.0001f;

    internal static float RequiredPierCapSeatingDrop(Bounds capBounds, float capAngleDegrees, float originalCapTopOffset,
        float roadSlope, float roadNormalUnderside, float roadCosine, float minRoadXRelativeToNode, float maxRoadXRelativeToNode)
    {
        if (roadCosine < MinimumSeatingRoadCosine || minRoadXRelativeToNode > maxRoadXRelativeToNode) return 0f;
        float radians = capAngleDegrees * Mathf.Deg2Rad, sine = Mathf.Sin(radians), cosine = Mathf.Cos(radians), drop = 0f;
        for (int edge = 0; edge < 4; edge++)
        {
            Vector2 first = PierCapCorner(capBounds, edge, sine, cosine, originalCapTopOffset);
            Vector2 second = PierCapCorner(capBounds, (edge + 1) % 4, sine, cosine, originalCapTopOffset);
            if (first.x >= minRoadXRelativeToNode && first.x <= maxRoadXRelativeToNode)
                drop = Mathf.Max(drop, PierCapCornerDrop(first, roadSlope, roadNormalUnderside, roadCosine));
            float dx = second.x - first.x;
            if (Mathf.Abs(dx) < 0.000001f) continue;
            float lowT = (minRoadXRelativeToNode - first.x) / dx, highT = (maxRoadXRelativeToNode - first.x) / dx;
            if (lowT >= 0f && lowT <= 1f)
                drop = Mathf.Max(drop, PierCapCornerDrop(first + (second - first) * lowT, roadSlope, roadNormalUnderside, roadCosine));
            if (highT >= 0f && highT <= 1f)
                drop = Mathf.Max(drop, PierCapCornerDrop(first + (second - first) * highT, roadSlope, roadNormalUnderside, roadCosine));
        }
        return Mathf.Max(0f, drop);
    }
    private static Vector2 PierCapCorner(Bounds bounds, int index, float sine, float cosine, float topOffset)
    {
        float x = index == 0 || index == 3 ? bounds.min.x : bounds.max.x;
        float y = index < 2 ? bounds.min.y : bounds.max.y;
        return new Vector2(cosine * x - sine * y, sine * x + cosine * y - topOffset);
    }
    private static float PierCapCornerDrop(Vector2 point, float roadSlope, float underside, float cosine) =>
        point.y - roadSlope * point.x - (underside - PierCapNormalClearance) / cosine;
}
