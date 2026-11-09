using System.Collections.Generic;
using UnityEngine;

public partial class Bar
{
    private const float PierCapNormalClearance = 0f;
    private const float SteelCrossheadThickness = .22f;
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

    private struct BearingStrip
    {
        public float left, right, near, far;
        public SeatingRoadPatch surface;
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
        sourceScale.y *= materialData.PierCapThicknessScale;
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
        if (materialData.pierCapPrefab == null)
        {
            // Keep the one-piece shaft width for its existing collider. Only
            // the separate visual crosshead reaches the outboard girders.
            bounds.size = new Vector3(bounds.size.x, bounds.size.y,
                Mathf.Max(bounds.size.z, BridgeWidthGeometry.GetPierSupportWidth(WideRoadWidth)));
        }
        if (WideRoadWidth > 0f) bounds.center = new Vector3(bounds.center.x, bounds.center.y, 0f);
        seatingCapSource = source; seatingCapScale = scale; seatingCapBounds = bounds; seatingCapBoundsValid = true;
        return true;
    }

    internal Vector4 CaptureWidePierSeat()
    {
        if (materialData == null || !materialData.isPier ||
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
        float visualTopOffset = capTopOffset * materialData.PierCapThicknessScale;
        Vector3 capScale = pierCapInstance.transform.localScale;
        if (capScale.y <= 0f) return;
        capScale.y = Mathf.Abs(originalCapScale.y) * materialData.PierCapThicknessScale;
        if (pierCapInstance.transform.localScale != capScale) pierCapInstance.transform.localScale = capScale;
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
                    drop = Mathf.Max(drop, RequiredPierCapSeatingDrop(capBounds, angle, visualTopOffset, slope, underside, cosine, minX, maxX));
        }
        else angle = 0f;
        Vector3 seat = top - Vector3.up * (visualTopOffset + drop);
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
        bool found = false;
        if (topPoint != null)
            foreach (Bar road in topPoint.ConnectedBars)
            {
                if (TryPierSeatingRoad(road, topPoint, top, out float slope, out float underside, out float cosine, out float minX, out float maxX))
                {
                    found = true;
                    float needed = RequiredPierCapSeatingDrop(topFace, tilt, 0f, slope, underside, cosine, minX, maxX);
                    float dropPerMeter = up.y - slope * up.x;
                    if (needed > 0f && dropPerMeter > MinimumSeatingRoadCosine)
                        drop = Mathf.Max(drop, needed / dropPerMeter);
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
        RefreshOnePieceBearing(topPoint, top, found);
    }

    private bool TryPierSeatingRoad(Bar road, Point topPoint, Vector3 top, out float slope, out float underside,
        out float cosine, out float minX, out float maxX)
    {
        slope = underside = cosine = minX = maxX = 0f;
        if (road == null || road == this || !road.gameObject.activeInHierarchy || road.IsConstructionPreview ||
            road.materialData == null || road.materialData.isPier || road.materialData.isRope || road.materialData.useSpring ||
            (!road.materialData.isRoad && !road.materialData.isDualBeam) ||
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
        Vector3 worldScale = Abs(road.transform.lossyScale);
        underside = float.PositiveInfinity;
        minX = float.PositiveInfinity; maxX = float.NegativeInfinity;
        int strandCount = road.materialData.isDualBeam ? 2 : 1;
        for (int strand = 0; strand < strandCount; strand++)
        {
            Vector3 scale = Abs(road.originalScale); scale.x *= length / Mathf.Max(Mathf.Abs(road.baseLength), 0.0001f);
            Transform visual = road.visualSegments.Count > strand && road.visualSegments[strand] != null
                ? road.visualSegments[strand].transform : null;
            Quaternion rotation = visual != null ? visual.localRotation : road.materialData.segmentPrefab.transform.localRotation;
            Vector3 offset = visual != null ? visual.localPosition : new Vector3(0f, 0f, road.SegmentDepthPosition(strand));
            // Settling can separate a body's rendered pose from its graph nodes.
            // Fit every pier type to the visible member, including baked poses.
            bool useRenderedPose = visual != null;
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
            float midpointX = midpoint.x - top.x;
            float undersideOffset = useRenderedPose ? (midpoint.y - top.y - slope * midpointX) * cosine : 0f;
            for (int part = 0; part < geometry.corners.Length; part += 8)
            {
                float partBottom = float.PositiveInfinity, partTop = float.NegativeInfinity;
                float partMinX = float.PositiveInfinity, partMaxX = float.NegativeInfinity;
                float partLocalMinX = float.PositiveInfinity, partLocalMaxX = float.NegativeInfinity;
                float partMinZ = float.PositiveInfinity, partMaxZ = float.NegativeInfinity;
                for (int corner = part; corner < part + 8; corner++)
                {
                    Vector3 point = Vector3.Scale(rotation * Vector3.Scale(geometry.corners[corner], scale) + offset, worldScale);
                    partBottom = Mathf.Min(partBottom, point.y);
                    partTop = Mathf.Max(partTop, point.y);
                    partLocalMinX = Mathf.Min(partLocalMinX, point.x); partLocalMaxX = Mathf.Max(partLocalMaxX, point.x);
                    float worldX = midpointX + cosine * point.x - sine * point.y;
                    partMinX = Mathf.Min(partMinX, worldX); partMaxX = Mathf.Max(partMaxX, worldX);
                    float worldZ = midpoint.z + point.z;
                    partMinZ = Mathf.Min(partMinZ, worldZ); partMaxZ = Mathf.Max(partMaxZ, worldZ);
                }
                // Inspect every actual strand independently. A pier below a
                // truss chord must reach both side girders, not just the deck.
                if (!PierSeatingDepthOverlaps(top.z + capBounds.min.z, top.z + capBounds.max.z, partMinZ, partMaxZ)) continue;
                underside = Mathf.Min(underside, partBottom + undersideOffset);
                minX = Mathf.Min(minX, partMinX); maxX = Mathf.Max(maxX, partMaxX);
                if (collectBearingPatches)
                {
                    bearingPatches.Add(new SeatingRoadPatch {
                        minX = midpoint.x + cosine * partLocalMinX - sine * partBottom,
                        maxX = midpoint.x + cosine * partLocalMaxX - sine * partBottom,
                        minZ = partMinZ, maxZ = partMaxZ, slope = slope,
                        intercept = midpoint.y - slope * midpoint.x + partBottom / cosine
                    });
                    // On a grade, one cut end faces partly downward. Include
                    // its finite plane so a neighboring pad stays below it.
                    if (Mathf.Abs(sine) > .001f && partTop - partBottom > .001f)
                    {
                        float endX = sine > 0f ? partLocalMinX : partLocalMaxX;
                        float bottomX = midpoint.x + cosine * endX - sine * partBottom;
                        float topX = midpoint.x + cosine * endX - sine * partTop;
                        float endSlope = -cosine / sine;
                        float bottomY = midpoint.y + sine * endX + cosine * partBottom;
                        bearingPatches.Add(new SeatingRoadPatch {
                            minX = Mathf.Min(bottomX, topX), maxX = Mathf.Max(bottomX, topX),
                            minZ = partMinZ, maxZ = partMaxZ, slope = endSlope,
                            intercept = bottomY - endSlope * bottomX
                        });
                    }
                }
            }
        }
        return IsFinite(underside) && IsFinite(slope);
    }

    private void RefreshPierBearing(Point topPoint, Vector3 top, bool hasRoad)
    {
        RestorePierBearing();
        if (!hasRoad || !PierCapSeatingProfiles.TryGetTopFace(materialData.pierCapPrefab, out Bounds face))
        { HidePierBearing(); return; }
        CollectPierBearingPatches(topPoint, top);
        Transform cap = pierCapInstance.transform;
        Vector3 first = cap.TransformPoint(new Vector3(face.min.x, face.max.y, face.min.z));
        Vector3 last = cap.TransformPoint(new Vector3(face.max.x, face.max.y, face.max.z));
        FitPierBearing(first, last, pierCapInstance.layer);
    }

    private void RefreshOnePieceBearing(Point topPoint, Vector3 top, bool hasMember)
    {
        RestorePierBearing();
        PierSeatingGeometry geometry = SeatingGeometry(materialData.segmentPrefab);
        if (!hasMember || geometry == null || geometry.corners.Length == 0)
        { HidePierBearing(); return; }
        CollectPierBearingPatches(topPoint, top);
        Bounds face = new Bounds(geometry.corners[0], Vector3.zero);
        foreach (Vector3 corner in geometry.corners) face.Encapsulate(corner);
        Transform visual = visualSegments[0].transform;
        Vector3 first = visual.TransformPoint(new Vector3(face.min.x, face.max.y, face.min.z));
        Vector3 last = visual.TransformPoint(new Vector3(face.max.x, face.max.y, face.max.z));
        float halfWidth = Mathf.Max((last.z - first.z) * .5f, BridgeWidthGeometry.GetPierSupportWidth(WideRoadWidth) * .5f);
        float center = (first.z + last.z) * .5f;
        first.z = center - halfWidth;
        last.z = center + halfWidth;
        FitPierBearing(first, last, visual.gameObject.layer, SteelCrossheadThickness);
    }

    private void RestorePierBearing()
    {
        if (!bearingRestored)
        {
            bearingRestored = true;
            Transform existing = transform.Find("PierBearing");
            if (existing != null) seatingBearing = existing.GetComponent<PierCapBearing>();
        }
    }

    private void CollectPierBearingPatches(Point topPoint, Vector3 top)
    {
        bearingPatches.Clear(); collectBearingPatches = true;
        try
        {
            foreach (Bar road in topPoint.ConnectedBars)
                TryPierSeatingRoad(road, topPoint, top, out _, out _, out _, out _, out _);
        }
        finally { collectBearingPatches = false; }
    }

    private void FitPierBearing(Vector3 first, Vector3 last, int layer, float crossheadThickness = 0f)
    {
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
        var zs = new List<float> { first.z, last.z };
        float bottomSlope = (last.y - first.y) / (last.x - first.x);
        float bottomIntercept = first.y - bottomSlope * first.x;
        foreach (SeatingRoadPatch patch in bearingPatches)
        {
            AddSeatingCut(zs, patch.minZ, first.z, last.z); AddSeatingCut(zs, patch.maxZ, first.z, last.z);
        }
        zs.Sort();
        var vertices = new List<Vector3>();
        var strips = new List<BearingStrip>();
        var depthPatches = new List<SeatingRoadPatch>();
        var xs = new List<float>();
        if (crossheadThickness > 0f)
            AddBearingPrism(vertices, first.x, last.x, first.z, last.z,
                first.y - crossheadThickness, last.y - crossheadThickness, first.y, last.y);
        // Use only the road ends and intersections in this depth strip. A
        // nearby plank or girder must not subdivide an unrelated support pad.
        for (int z = 0; z < zs.Count - 1; z++)
        {
            float near = zs[z], far = zs[z + 1], midZ = (near + far) * .5f;
            if (far - near < .00001f) continue;
            depthPatches.Clear();
            xs.Clear();
            xs.Add(first.x); xs.Add(last.x);
            foreach (SeatingRoadPatch patch in bearingPatches)
            {
                if (midZ <= patch.minZ || midZ >= patch.maxZ) continue;
                foreach (SeatingRoadPatch other in depthPatches)
                {
                    if (Mathf.Abs(patch.slope - other.slope) <= .00001f) continue;
                    float crossing = (other.intercept - patch.intercept) / (patch.slope - other.slope);
                    if (crossing > Mathf.Max(patch.minX, other.minX) && crossing < Mathf.Min(patch.maxX, other.maxX))
                        AddSeatingCut(xs, crossing, first.x, last.x);
                }
                depthPatches.Add(patch);
                AddSeatingCut(xs, patch.minX, first.x, last.x);
                AddSeatingCut(xs, patch.maxX, first.x, last.x);
                if (Mathf.Abs(patch.slope - bottomSlope) > .00001f)
                    AddSeatingCut(xs, (bottomIntercept - patch.intercept) / (patch.slope - bottomSlope), first.x, last.x);
            }
            xs.Sort();
            BearingStrip strip = default;
            for (int x = 0; x < xs.Count - 1; x++)
            {
                float lo = xs[x], hi = xs[x + 1];
                if (hi - lo < .00001f) continue;
                float midX = (lo + hi) * .5f, low = float.PositiveInfinity;
                SeatingRoadPatch chosen = default;
                foreach (SeatingRoadPatch patch in depthPatches)
                    if (midX > patch.minX && midX < patch.maxX && patch.Height(midX) < low)
                    { low = patch.Height(midX); chosen = patch; }
                if (!IsFinite(low) || low - (bottomSlope * midX + bottomIntercept) < .00005f) continue;
                float b0 = bottomSlope * lo + bottomIntercept, b1 = bottomSlope * hi + bottomIntercept;
                float t0 = chosen.Height(lo), t1 = chosen.Height(hi);
                if (t0 < b0 - .0002f || t1 < b1 - .0002f) continue;
                if (strip.right > strip.left && Mathf.Abs(strip.right - lo) < .00001f &&
                    SameBearingPlane(strip.surface, chosen))
                {
                    strip.right = hi;
                    continue;
                }
                if (strip.right > strip.left) MergeBearingStrip(strips, strip);
                strip = new BearingStrip { left = lo, right = hi, near = near, far = far, surface = chosen };
            }
            if (strip.right > strip.left) MergeBearingStrip(strips, strip);
        }
        foreach (BearingStrip strip in strips)
            AddBearingPrism(vertices, strip.left, strip.right, strip.near, strip.far,
                bottomSlope * strip.left + bottomIntercept, bottomSlope * strip.right + bottomIntercept,
                strip.surface.Height(strip.left), strip.surface.Height(strip.right));
        if (vertices.Count == 0 || vertices.Count > 512)
        {
            if (seatingBearing != null) seatingBearing.gameObject.SetActive(false);
            return;
        }
        if (seatingBearing == null)
        {
            var bearing = new GameObject("PierBearing"); bearing.layer = layer;
            bearing.transform.SetParent(transform, false); seatingBearing = bearing.AddComponent<PierCapBearing>();
        }
        seatingBearing.SetPrisms(vertices.ToArray(), PierCapBearing.Appearance(materialData));
        seatingBearing.gameObject.SetActive(true);
    }

    private static bool SameBearingPlane(SeatingRoadPatch first, SeatingRoadPatch second) =>
        Mathf.Abs(first.slope - second.slope) < .00001f && Mathf.Abs(first.intercept - second.intercept) < .00001f;

    private static void MergeBearingStrip(List<BearingStrip> strips, BearingStrip strip)
    {
        // Merge adjacent coplanar seats before the snapshot size limit. Preserve
        // steps between different undersides and gaps between separate members.
        for (int i = strips.Count - 1; i >= 0; i--)
        {
            BearingStrip previous = strips[i];
            if (Mathf.Abs(previous.far - strip.near) >= .00001f ||
                Mathf.Abs(previous.left - strip.left) >= .00001f || Mathf.Abs(previous.right - strip.right) >= .00001f ||
                !SameBearingPlane(previous.surface, strip.surface)) continue;
            previous.far = strip.far;
            strips[i] = previous;
            return;
        }
        strips.Add(strip);
    }

    private void AddBearingPrism(List<Vector3> vertices, float left, float right, float near, float far,
        float bottomLeft, float bottomRight, float topLeft, float topRight)
    {
        vertices.Add(transform.InverseTransformPoint(new Vector3(left, bottomLeft, near)));
        vertices.Add(transform.InverseTransformPoint(new Vector3(right, bottomRight, near)));
        vertices.Add(transform.InverseTransformPoint(new Vector3(right, bottomRight, far)));
        vertices.Add(transform.InverseTransformPoint(new Vector3(left, bottomLeft, far)));
        vertices.Add(transform.InverseTransformPoint(new Vector3(left, topLeft, near)));
        vertices.Add(transform.InverseTransformPoint(new Vector3(right, topRight, near)));
        vertices.Add(transform.InverseTransformPoint(new Vector3(right, topRight, far)));
        vertices.Add(transform.InverseTransformPoint(new Vector3(left, topLeft, far)));
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
