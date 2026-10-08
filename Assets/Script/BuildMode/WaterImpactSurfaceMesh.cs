using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Disposable, bounded subdivision of a rectangular water surface. Only its
/// top face is replaced; the authored mesh and any collider remain untouched.
/// Vertex animation is supplied by the water shader, not by this helper.
/// </summary>
internal sealed class WaterImpactSurfaceMesh : IDisposable
{
    private const int MaximumImpacts = 8;
    private const int MaximumAxisCoordinates = 2 + MaximumImpacts * 9;
    private const int MaximumOutputVertices = 5500;
    private static readonly float[] RadiusOffsets = { -4f, -2f, -1f, -.5f, 0f, .5f, 1f, 2f, 4f };
    private readonly MeshFilter filter;
    private readonly Mesh source;
    private readonly Mesh workingMesh;
    private readonly Vector3[] sourceVertices;
    private readonly Vector3[] sourceNormals;
    private readonly Vector4[] sourceTangents;
    private readonly Color[] sourceColors;
    private readonly bool[] topVertexMask;
    private readonly List<int> sideTriangles;
    private readonly List<Vector4>[] sourceUvs = new List<Vector4>[8];
    private readonly List<Vector4>[] workingUvs = new List<Vector4>[8];
    private readonly int[] uvDimensions = new int[8];
    private readonly List<Vector3> vertices = new List<Vector3>(MaximumOutputVertices);
    private readonly List<Vector3> normals = new List<Vector3>(MaximumOutputVertices);
    private readonly List<Vector4> tangents = new List<Vector4>(MaximumOutputVertices);
    private readonly List<Color> colors = new List<Color>(MaximumOutputVertices);
    private readonly List<int> triangles = new List<int>(MaximumOutputVertices * 6);
    private readonly List<Vector2> uv2Scratch = new List<Vector2>(MaximumOutputVertices);
    private readonly List<Vector3> uv3Scratch = new List<Vector3>(MaximumOutputVertices);
    private readonly float[] xCoordinates = new float[MaximumAxisCoordinates];
    private readonly float[] zCoordinates = new float[MaximumAxisCoordinates];
    private readonly Bounds sourceBounds;
    private readonly float minX, maxX, minZ, maxZ, topY, coordinateTolerance;
    private readonly Vector3 uvA, uvB, uvC;
    private readonly int indexA, indexB, indexC;
    private readonly float barycentricDenominator;
    private bool disposed;

    internal Mesh Mesh => workingMesh;
    internal MeshFilter MeshFilter => filter;

    private WaterImpactSurfaceMesh(MeshFilter meshFilter, Mesh original,
        Vector3[] originalVertices, bool[] topVertices, List<int> nonTopTriangles,
        int a, int b, int c, float lowX, float highX, float lowZ, float highZ, float highestY)
    {
        filter = meshFilter;
        source = original;
        sourceVertices = originalVertices;
        topVertexMask = topVertices;
        sideTriangles = nonTopTriangles;
        sourceBounds = source.bounds;
        sourceNormals = source.normals;
        sourceTangents = source.tangents;
        sourceColors = source.colors;
        minX = lowX; maxX = highX; minZ = lowZ; maxZ = highZ; topY = highestY;
        coordinateTolerance = Mathf.Max(0.000001f, Mathf.Max(maxX - minX, maxZ - minZ) * 0.000001f);
        indexA = a; indexB = b; indexC = c;
        uvA = sourceVertices[a]; uvB = sourceVertices[b]; uvC = sourceVertices[c];
        barycentricDenominator = (uvB.z - uvC.z) * (uvA.x - uvC.x) +
            (uvC.x - uvB.x) * (uvA.z - uvC.z);
        for (int channel = 0; channel < 8; channel++)
        {
            sourceUvs[channel] = new List<Vector4>();
            workingUvs[channel] = new List<Vector4>();
            source.GetUVs(channel, sourceUvs[channel]);
            if (sourceUvs[channel].Count == sourceVertices.Length)
                uvDimensions[channel] = source.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0 + channel));
        }
        workingMesh = UnityEngine.Object.Instantiate(source);
        workingMesh.name = source.name + " (localized water impacts)";
        workingMesh.hideFlags = HideFlags.HideAndDontSave;
        workingMesh.MarkDynamic();
    }

    internal static bool TryCreate(MeshFilter meshFilter, Renderer renderer, out WaterImpactSurfaceMesh helper)
    {
        helper = null;
        if (meshFilter == null || renderer == null || renderer.gameObject != meshFilter.gameObject) return false;
        Mesh original = meshFilter.sharedMesh;
        if (original == null || !original.isReadable || original.subMeshCount != 1 ||
            renderer.sharedMaterials.Length != 1 || original.vertexCount > MaximumOutputVertices - 4) return false;
        Transform transform = meshFilter.transform;
        Vector3 up = transform.TransformVector(Vector3.up);
        if (!Finite(up) || up.sqrMagnitude < 0.000001f || Vector3.Dot(up.normalized, Vector3.up) < .99f) return false;
        try
        {
            Vector3[] points = original.vertices;
            int[] indices = original.triangles;
            if (points.Length < 4 || indices.Length < 6 || indices.Length % 3 != 0) return false;
            float lowX = float.PositiveInfinity, highX = float.NegativeInfinity;
            float lowZ = float.PositiveInfinity, highZ = float.NegativeInfinity;
            float highestY = float.NegativeInfinity;
            foreach (Vector3 point in points)
            {
                if (!Finite(point)) return false;
                lowX = Mathf.Min(lowX, point.x); highX = Mathf.Max(highX, point.x);
                lowZ = Mathf.Min(lowZ, point.z); highZ = Mathf.Max(highZ, point.z);
                highestY = Mathf.Max(highestY, point.y);
            }
            float area = (highX - lowX) * (highZ - lowZ);
            if (!Finite(area) || area <= 0.000001f) return false;
            float topTolerance = Mathf.Max(0.000001f, Mathf.Max(highX - lowX, highZ - lowZ) * 0.000001f);
            if (highX - lowX <= topTolerance || highZ - lowZ <= topTolerance) return false;
            var nonTopTriangles = new List<int>(indices.Length);
            var topVertices = new bool[points.Length];
            double topArea = 0d;
            int firstA = -1, firstB = -1, firstC = -1;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= points.Length || b >= points.Length || c >= points.Length) return false;
                bool top = Mathf.Abs(points[a].y - highestY) <= topTolerance &&
                    Mathf.Abs(points[b].y - highestY) <= topTolerance && Mathf.Abs(points[c].y - highestY) <= topTolerance;
                if (!top)
                {
                    nonTopTriangles.Add(a); nonTopTriangles.Add(b); nonTopTriangles.Add(c);
                    continue;
                }
                Vector3 cross = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                if (cross.sqrMagnitude <= 0.000000000001f || cross.y <= 0f) return false;
                Vector3 worldCross = Vector3.Cross(transform.TransformVector(points[b] - points[a]),
                    transform.TransformVector(points[c] - points[a]));
                if (!Finite(worldCross) || worldCross.sqrMagnitude < 0.000000000001f ||
                    Vector3.Dot(worldCross.normalized, Vector3.up) < .99f) return false;
                topArea += cross.y * .5d;
                topVertices[a] = topVertices[b] = topVertices[c] = true;
                if (firstA < 0) { firstA = a; firstB = b; firstC = c; }
            }
            if (firstA < 0 || Math.Abs(topArea - area) > Math.Max(0.00001d, area * 0.0001d)) return false;
            helper = new WaterImpactSurfaceMesh(meshFilter, original, points, topVertices, nonTopTriangles,
                firstA, firstB, firstC, lowX, highX, lowZ, highZ, highestY);
            if (!helper.TopUvsAreAffine())
            {
                helper.Dispose();
                helper = null;
                return false;
            }
            meshFilter.sharedMesh = helper.workingMesh;
            return true;
        }
        catch (Exception)
        {
            if (helper != null) helper.Dispose();
            helper = null;
            return false; // Unsupported mesh data keeps the fragment-only effect.
        }
    }

    /// <summary>Rebuild only for new impacts; the shader animates their lifetime.</summary>
    internal void Rebuild(Vector3[] impactWorldCenters, float[] worldRadii, int count, float maxDipDepth)
    {
        if (disposed || filter == null || filter.sharedMesh != workingMesh) return;
        int xCount = 2, zCount = 2;
        xCoordinates[0] = minX; xCoordinates[1] = maxX;
        zCoordinates[0] = minZ; zCoordinates[1] = maxZ;
        Transform transform = filter.transform;
        float xScale = transform.TransformVector(Vector3.right).magnitude;
        float zScale = transform.TransformVector(Vector3.forward).magnitude;
        if (!Finite(xScale) || !Finite(zScale) || xScale <= 0.000001f || zScale <= 0.000001f) return;
        int available = impactWorldCenters == null || worldRadii == null ? 0 :
            Mathf.Min(MaximumImpacts, Mathf.Min(Mathf.Max(count, 0), Mathf.Min(impactWorldCenters.Length, worldRadii.Length)));
        for (int i = 0; i < available; i++)
        {
            if (!Finite(impactWorldCenters[i]) || !Finite(worldRadii[i]) || worldRadii[i] <= 0f) continue;
            Vector3 local = transform.InverseTransformPoint(impactWorldCenters[i]);
            if (!Finite(local)) continue;
            float radiusX = worldRadii[i] / xScale, radiusZ = worldRadii[i] / zScale;
            for (int offset = 0; offset < RadiusOffsets.Length; offset++)
            {
                xCoordinates[xCount++] = Mathf.Clamp(local.x + RadiusOffsets[offset] * radiusX, minX, maxX);
                zCoordinates[zCount++] = Mathf.Clamp(local.z + RadiusOffsets[offset] * radiusZ, minZ, maxZ);
            }
        }
        xCount = SortUnique(xCoordinates, xCount);
        zCount = SortUnique(zCoordinates, zCount);
        int gridBudget = MaximumOutputVertices - sourceVertices.Length;
        // Retaining original non-top vertex data is part of the same hard cap.
        while (xCount * zCount > gridBudget)
        {
            if (xCount >= zCount && xCount > 2) xCount = RemoveClosestInterior(xCoordinates, xCount);
            else if (zCount > 2) zCount = RemoveClosestInterior(zCoordinates, zCount);
            else return;
        }
        vertices.Clear(); vertices.AddRange(sourceVertices);
        bool hasNormals = sourceNormals.Length == sourceVertices.Length;
        bool hasTangents = sourceTangents.Length == sourceVertices.Length;
        bool hasColors = sourceColors.Length == sourceVertices.Length;
        normals.Clear(); if (hasNormals) normals.AddRange(sourceNormals);
        tangents.Clear(); if (hasTangents) tangents.AddRange(sourceTangents);
        colors.Clear(); if (hasColors) colors.AddRange(sourceColors);
        for (int channel = 0; channel < 8; channel++)
        {
            workingUvs[channel].Clear();
            if (uvDimensions[channel] > 0) workingUvs[channel].AddRange(sourceUvs[channel]);
        }
        for (int z = 0; z < zCount; z++) for (int x = 0; x < xCount; x++)
        {
            Vector3 point = new Vector3(xCoordinates[x], topY, zCoordinates[z]);
            Vector3 bary = Barycentric(point);
            vertices.Add(point);
            if (hasNormals) normals.Add(Vector3.up);
            if (hasTangents) tangents.Add(sourceTangents[indexA] * bary.x + sourceTangents[indexB] * bary.y + sourceTangents[indexC] * bary.z);
            if (hasColors) colors.Add(sourceColors[indexA] * bary.x + sourceColors[indexB] * bary.y + sourceColors[indexC] * bary.z);
            for (int channel = 0; channel < 8; channel++)
                if (uvDimensions[channel] > 0) workingUvs[channel].Add(UvAt(channel, bary));
        }
        triangles.Clear(); triangles.AddRange(sideTriangles);
        int first = sourceVertices.Length;
        for (int z = 0; z < zCount - 1; z++) for (int x = 0; x < xCount - 1; x++)
        {
            int a = first + z * xCount + x, b = a + 1, c = a + xCount, d = c + 1;
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(b); triangles.Add(c); triangles.Add(d);
        }
        workingMesh.Clear();
        workingMesh.SetVertices(vertices);
        if (hasNormals) workingMesh.SetNormals(normals);
        if (hasTangents) workingMesh.SetTangents(tangents);
        if (hasColors) workingMesh.SetColors(colors);
        for (int channel = 0; channel < 8; channel++) WriteUvChannel(channel);
        workingMesh.SetTriangles(triangles, 0, false);
        Bounds bounds = sourceBounds;
        float localDip = Finite(maxDipDepth) ? Mathf.Max(0f, maxDipDepth) /
            Mathf.Max(Mathf.Abs(transform.TransformVector(Vector3.up).y), 0.000001f) : 0f;
        Vector3 minimum = bounds.min;
        Vector3 maximum = bounds.max;
        minimum.y = Mathf.Min(minimum.y, topY - localDip);
        maximum.y = Mathf.Max(maximum.y, topY + localDip * .3f);
        bounds.SetMinMax(minimum, maximum);
        workingMesh.bounds = bounds;
    }

    private int SortUnique(float[] coordinates, int count)
    {
        Array.Sort(coordinates, 0, count);
        float last = coordinates[count - 1];
        int unique = 1;
        for (int i = 1; i < count; i++)
            if (coordinates[i] - coordinates[unique - 1] > coordinateTolerance) coordinates[unique++] = coordinates[i];
        // The boundary must not be lost when a clamped impact is very close to it.
        coordinates[unique - 1] = last;
        return unique;
    }

    private static int RemoveClosestInterior(float[] coordinates, int count)
    {
        int remove = 1;
        float closest = float.PositiveInfinity;
        for (int i = 1; i < count - 1; i++)
        {
            float gap = Mathf.Min(coordinates[i] - coordinates[i - 1], coordinates[i + 1] - coordinates[i]);
            if (gap < closest) { closest = gap; remove = i; }
        }
        for (int i = remove; i < count - 1; i++) coordinates[i] = coordinates[i + 1];
        return count - 1;
    }

    private Vector3 Barycentric(Vector3 point)
    {
        float a = ((uvB.z - uvC.z) * (point.x - uvC.x) + (uvC.x - uvB.x) * (point.z - uvC.z)) / barycentricDenominator;
        float b = ((uvC.z - uvA.z) * (point.x - uvC.x) + (uvA.x - uvC.x) * (point.z - uvC.z)) / barycentricDenominator;
        return new Vector3(a, b, 1f - a - b);
    }

    private Vector4 UvAt(int channel, Vector3 bary)
    {
        List<Vector4> uv = sourceUvs[channel];
        return uv[indexA] * bary.x + uv[indexB] * bary.y + uv[indexC] * bary.z;
    }

    private bool TopUvsAreAffine()
    {
        for (int i = 0; i < sourceVertices.Length; i++)
        {
            if (!topVertexMask[i]) continue; // Side-face UV seams on cubes remain intact.
            Vector3 bary = Barycentric(sourceVertices[i]);
            for (int channel = 0; channel < 8; channel++)
                if (uvDimensions[channel] > 0 && (UvAt(channel, bary) - sourceUvs[channel][i]).sqrMagnitude > 0.00001f) return false;
        }
        return true;
    }

    private void WriteUvChannel(int channel)
    {
        if (uvDimensions[channel] <= 0) return;
        List<Vector4> uv = workingUvs[channel];
        if (uvDimensions[channel] == 2)
        {
            uv2Scratch.Clear(); foreach (Vector4 value in uv) uv2Scratch.Add(new Vector2(value.x, value.y));
            workingMesh.SetUVs(channel, uv2Scratch);
        }
        else if (uvDimensions[channel] == 3)
        {
            uv3Scratch.Clear(); foreach (Vector4 value in uv) uv3Scratch.Add(new Vector3(value.x, value.y, value.z));
            workingMesh.SetUVs(channel, uv3Scratch);
        }
        else workingMesh.SetUVs(channel, uv);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (filter != null && filter.sharedMesh == workingMesh) filter.sharedMesh = source;
        if (workingMesh == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(workingMesh);
        else UnityEngine.Object.DestroyImmediate(workingMesh);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
}
