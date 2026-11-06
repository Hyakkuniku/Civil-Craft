#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a scene-local bank mesh whose visible lip meets the existing road
/// endpoint. The caller owns the returned mesh and assigns it to both the
/// renderer and the bank's existing MeshCollider. No scene objects are moved.
/// </summary>
internal static class RegionalBankMeshFitter
{
    internal static void ReplaceMesh(Mesh target, Mesh source)
    {
        // CopySerialized updates the serialized Mesh fields without reliably
        // invalidating its existing GPU buffers. Rebuild through Mesh APIs so
        // the saved asset and live renderer use the same geometry immediately.
        target.Clear(false); target.indexFormat = source.indexFormat; target.name = source.name;
        target.vertices = source.vertices; target.normals = source.normals;
        if (source.tangents.Length == source.vertexCount) target.tangents = source.tangents;
        if (source.colors.Length == source.vertexCount) target.colors = source.colors;
        for (int channel = 0; channel < 8; channel++)
        {
            var uv = new List<Vector4>(); source.GetUVs(channel, uv);
            if (uv.Count == source.vertexCount) target.SetUVs(channel, uv);
        }
        target.subMeshCount = source.subMeshCount;
        for (int submesh = 0; submesh < source.subMeshCount; submesh++) target.SetTriangles(source.GetTriangles(submesh), submesh, false);
        target.bounds = source.bounds; target.UploadMeshData(false);
    }
    internal sealed class Join
    {
        public Vector3 anchor;
        public float landDirection;
        public float halfWidth;
        public float extension;
        public bool preserveUpperSides;
    }

    private const float FlatLength = 2f;
    private const float BlendLength = 6f;
    private const float FlatWidthMargin = .2f;
    private const float BlendWidthMargin = 1.5f;
    private const float PlaneEpsilon = .00001f;
    // The lip overlaps the road horizontally. Keep its stone/grass half a
    // millimetre below asphalt to prevent coplanar flicker without a gap.
    private const float BankSurfaceAboveAnchor = .0245f;

    private sealed class Vertex
    {
        internal Vector3 local, world, normal;
        internal Vector4 tangent;
        internal Color color;
        internal Vector4[] uv = new Vector4[8];

        internal static Vertex Between(Vertex a, Vertex b, float t)
        {
            var value = new Vertex
            {
                local = Vector3.LerpUnclamped(a.local, b.local, t),
                world = Vector3.LerpUnclamped(a.world, b.world, t),
                normal = Vector3.LerpUnclamped(a.normal, b.normal, t),
                tangent = Vector4.LerpUnclamped(a.tangent, b.tangent, t),
                color = Color.LerpUnclamped(a.color, b.color, t)
            };
            for (int channel = 0; channel < 8; channel++) value.uv[channel] = Vector4.LerpUnclamped(a.uv[channel], b.uv[channel], t);
            return value;
        }
    }

    private readonly struct Cut
    {
        internal readonly int axis;
        internal readonly float coordinate;
        internal Cut(int axis, float coordinate) { this.axis = axis; this.coordinate = coordinate; }
        internal float Distance(Vertex value) => (axis == 0 ? value.world.x : value.world.z) - coordinate;
    }

    internal static Mesh Build(Mesh original, Matrix4x4 meshToWorld, IReadOnlyList<Join> joins)
    {
        Require(original != null && original.vertexCount > 0, "The bank requires an existing static mesh.");
        Require(joins != null && joins.Count > 0, "At least one road join is required.");
        Require(Mathf.Abs(meshToWorld.determinant) > 1e-10f, "The bank transform must be invertible.");
        Require(original.blendShapeCount == 0, "Do not reshape an animated or blend-shape bank mesh.");
        Require(original.bindposes.Length == 0, "Only non-skinned static banks are supported.");
        foreach (Join join in joins)
        {
            Require(join != null && Finite(join.anchor.x) && Finite(join.anchor.y) && Finite(join.anchor.z) &&
                    Finite(join.landDirection) && Mathf.Abs(Mathf.Abs(join.landDirection) - 1f) < .001f &&
                    Finite(join.halfWidth) && join.halfWidth > .1f && join.halfWidth < 30f &&
                    Finite(join.extension) && join.extension >= 0f && join.extension <= 2f,
                "Invalid bank join width, direction, position or extension.");
        }

        Mesh readable = ReadableCopy(original);
        try
        {
            Require(readable.bindposes.Length == 0, "Only non-skinned static banks are supported.");
            Vector3[] positions = readable.vertices;
            Vector3[] sourceNormals = readable.normals;
            Vector4[] sourceTangents = readable.tangents;
            Color[] sourceColors = readable.colors;
            bool hasNormals = sourceNormals.Length == positions.Length;
            bool hasTangents = sourceTangents.Length == positions.Length;
            bool hasColors = sourceColors.Length == positions.Length;
            var sourceUvs = new List<Vector4>[8];
            var uvDimensions = new int[8];
            for (int channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                sourceUvs[channel] = new List<Vector4>();
                if (!readable.HasVertexAttribute(attribute)) continue;
                readable.GetUVs(channel, sourceUvs[channel]);
                uvDimensions[channel] = readable.GetVertexAttributeDimension(attribute);
                Require(sourceUvs[channel].Count == positions.Length, "A bank UV channel has inconsistent vertex data.");
            }
            var vertices = new Vertex[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var value = new Vertex
                {
                    local = positions[i], world = meshToWorld.MultiplyPoint3x4(positions[i]),
                    normal = hasNormals ? sourceNormals[i] : Vector3.zero,
                    tangent = hasTangents ? sourceTangents[i] : Vector4.zero,
                    color = hasColors ? sourceColors[i] : Color.white
                };
                Require(Finite(value.world.x) && Finite(value.world.y) && Finite(value.world.z), "The bank contains a non-finite vertex.");
                for (int channel = 0; channel < 8; channel++)
                    if (uvDimensions[channel] > 0) value.uv[channel] = sourceUvs[channel][i];
                vertices[i] = value;
            }
            if (joins.Count == 1 && joins[0].preserveUpperSides)
                return FitIntactBank(readable, vertices, meshToWorld, joins[0]);

            // Split before moving vertices. Without these boundaries, an old
            // low-poly triangle could carry the correction far inland or leave
            // its interpolated top sloping through the road endpoint.
            var cuts = new List<Cut>();
            foreach (Join join in joins)
            {
                // The extended flat slice maps back to exactly FlatLength
                // after its lip is stretched toward the bridge.
                cuts.Add(new Cut(0, join.anchor.x + join.landDirection * (FlatLength + join.extension)));
                cuts.Add(new Cut(0, join.anchor.x + join.landDirection * BlendLength));
                foreach (float sign in new[] { -1f, 1f })
                {
                    cuts.Add(new Cut(1, join.anchor.z + sign * (join.halfWidth + FlatWidthMargin)));
                    cuts.Add(new Cut(1, join.anchor.z + sign * (join.halfWidth + BlendWidthMargin)));
                }
            }
            cuts = cuts.GroupBy(cut => cut.axis + ":" + Mathf.RoundToInt(cut.coordinate * 10000f)).Select(group => group.First()).ToList();
            Matrix4x4 worldToMesh = meshToWorld.inverse;
            Matrix4x4 normalToMesh = meshToWorld.transpose;
            float normalOrientation = Mathf.Sign(meshToWorld.determinant);
            var resultPositions = new List<Vector3>();
            var resultNormals = new List<Vector3>();
            var resultTangents = new List<Vector4>();
            var resultColors = new List<Color>();
            var resultUvs = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray();
            var indices = new List<int>[readable.subMeshCount];
            for (int submesh = 0; submesh < readable.subMeshCount; submesh++)
            {
                Require(readable.GetTopology(submesh) == MeshTopology.Triangles, "The bank must contain triangle submeshes.");
                indices[submesh] = new List<int>();
                int[] sourceIndices = readable.GetIndices(submesh, true);
                Require(sourceIndices.Length % 3 == 0, "A bank triangle submesh has incomplete indices.");
                for (int offset = 0; offset < sourceIndices.Length; offset += 3)
                {
                    var polygon = new List<Vertex> { vertices[sourceIndices[offset]], vertices[sourceIndices[offset + 1]], vertices[sourceIndices[offset + 2]] };
                    var pieces = new List<List<Vertex>> { polygon };
                    if (CouldChange(polygon, joins))
                        foreach (Cut cut in cuts)
                        {
                            var next = new List<List<Vertex>>();
                            foreach (List<Vertex> piece in pieces) Split(piece, cut, next);
                            pieces = next;
                        }
                    foreach (List<Vertex> piece in pieces)
                        for (int i = 1; i + 1 < piece.Count; i++)
                        {
                            Vertex[] triangle = { piece[0], piece[i], piece[i + 1] };
                            Vector3[] changedWorld = triangle.Select(value => Deform(value.world, joins)).ToArray();
                            Vector3[] changedLocal = changedWorld.Select(worldToMesh.MultiplyPoint3x4).ToArray();
                            // Imported banks have very small local coordinates
                            // under large scene scales. Local-space area tests
                            // can discard real, visible world-space triangles.
                            Vector3 worldNormal = Vector3.Cross(changedWorld[1] - changedWorld[0], changedWorld[2] - changedWorld[0]);
                            if (worldNormal.sqrMagnitude < 1e-18f) continue;
                            // Derive the normal in world space as well. Taking
                            // another cross after float world/local conversion
                            // makes thin cut faces acquire arbitrary directions.
                            Vector3 faceNormal = normalToMesh.MultiplyVector(worldNormal) * normalOrientation;
                            float normalLength = Mathf.Sqrt(faceNormal.sqrMagnitude);
                            Require(normalLength > 0f && Finite(normalLength), "A bank triangle lost its local-space normal.");
                            // Vector3.Normalize's fixed epsilon zeroes these
                            // small cross products; explicit division is safe.
                            faceNormal /= normalLength;
                            bool changed = Enumerable.Range(0, 3).Any(index => (changedWorld[index] - triangle[index].world).sqrMagnitude > 1e-12f);
                            for (int vertex = 0; vertex < 3; vertex++)
                            {
                                Vertex value = triangle[vertex];
                                indices[submesh].Add(resultPositions.Count);
                                // Retain exact original coordinates away from
                                // the lip, avoiding world/local round-trip drift.
                                resultPositions.Add(changed ? changedLocal[vertex] : value.local);
                                resultNormals.Add(changed || !hasNormals || value.normal.sqrMagnitude < 1e-12f ? faceNormal : value.normal.normalized);
                                if (hasTangents)
                                {
                                    Vector3 tangent = new Vector3(value.tangent.x, value.tangent.y, value.tangent.z);
                                    Vector3 normal = resultNormals[resultNormals.Count - 1];
                                    if (changed) tangent = Vector3.ProjectOnPlane(tangent, normal);
                                    tangent.Normalize();
                                    resultTangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, value.tangent.w < 0f ? -1f : 1f));
                                }
                                if (hasColors) resultColors.Add(value.color);
                                for (int channel = 0; channel < 8; channel++)
                                    if (uvDimensions[channel] > 0) resultUvs[channel].Add(value.uv[channel]);
                            }
                        }
                }
            }
            Require(resultPositions.Count > 0 && resultPositions.Count < 2000000, "Bank fitting produced an empty or unexpectedly large mesh.");
            Require(resultNormals.All(normal => Finite(normal.x) && Finite(normal.y) && Finite(normal.z) && Mathf.Abs(normal.sqrMagnitude - 1f) < .001f), "Bank fitting produced an invalid lighting normal.");
            var result = new Mesh { name = original.name + "_RoadJoin", indexFormat = resultPositions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                result.SetVertices(resultPositions); result.SetNormals(resultNormals);
                if (hasTangents) result.SetTangents(resultTangents);
                if (hasColors) result.SetColors(resultColors);
                for (int channel = 0; channel < 8; channel++)
                {
                    if (uvDimensions[channel] == 0) continue;
                    if (uvDimensions[channel] <= 2) result.SetUVs(channel, resultUvs[channel].Select(value => new Vector2(value.x, value.y)).ToList());
                    else if (uvDimensions[channel] == 3) result.SetUVs(channel, resultUvs[channel].Select(value => new Vector3(value.x, value.y, value.z)).ToList());
                    else result.SetUVs(channel, resultUvs[channel]);
                }
                result.subMeshCount = indices.Length;
                for (int submesh = 0; submesh < indices.Length; submesh++) result.SetTriangles(indices[submesh], submesh, false);
                result.RecalculateBounds();
                return result;
            }
            catch { UnityEngine.Object.DestroyImmediate(result); throw; }
        }
        finally { UnityEngine.Object.DestroyImmediate(readable); }
    }

    private static bool CouldChange(List<Vertex> triangle, IReadOnlyList<Join> joins)
    {
        float minX = triangle.Min(value => value.world.x), maxX = triangle.Max(value => value.world.x);
        float minZ = triangle.Min(value => value.world.z), maxZ = triangle.Max(value => value.world.z);
        float maxY = triangle.Max(value => value.world.y);
        foreach (Join join in joins)
        {
            if (maxY <= join.anchor.y + BankSurfaceAboveAnchor - 3f ||
                maxZ < join.anchor.z - join.halfWidth - BlendWidthMargin || minZ > join.anchor.z + join.halfWidth + BlendWidthMargin) continue;
            if (join.landDirection > 0f ? minX < join.anchor.x + BlendLength : maxX > join.anchor.x - BlendLength) return true;
        }
        return false;
    }

    private static Mesh FitIntactBank(Mesh readable, Vertex[] vertices, Matrix4x4 toWorld, Join join)
    {
        // Shopkeeper's small standalone banks have a separate coloured cap and
        // faceted shell. Preserve every original face and shared edge instead
        // of cutting those two material surfaces into different triangulations.
        Matrix4x4 toLocal = toWorld.inverse;
        var positions = new Vector3[vertices.Length];
        float target = join.anchor.y + BankSurfaceAboveAnchor;
        Require(readable.subMeshCount == 2, "Shopkeeper's bank must retain its separate cap material.");
        int[] capIndices = readable.GetTriangles(1).Distinct().ToArray();
        var cap = capIndices.Select(index => vertices[index].world).ToArray();
        double cx = cap.Average(point => (double)point.x), cy = cap.Average(point => (double)point.y), cz = cap.Average(point => (double)point.z);
        double xx = 0d, xz = 0d, zz = 0d, xy = 0d, zy = 0d;
        foreach (Vector3 point in cap)
        {
            double x = point.x - cx, z = point.z - cz, y = point.y - cy;
            xx += x*x; xz += x*z; zz += z*z; xy += x*y; zy += z*y;
        }
        double determinant = xx*zz-xz*xz; Require(Math.Abs(determinant) > .0001d, "The bank cap does not define a stable surface plane.");
        double slopeX = (xy*zz-zy*xz)/determinant, slopeZ = (zy*xx-xy*xz)/determinant;
        var capLocal = capIndices.Select(index => vertices[index].local).ToArray();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = vertices[i].world;
            world.y += target - (float)(cy + slopeX*(world.x-cx) + slopeZ*(world.z-cz));
            // Level the original cap and its coincident shell vertices as one
            // welded surface, keeping every other face's depth and topology.
            if (capLocal.Any(point => (point - vertices[i].local).sqrMagnitude < 1e-14f)) world.y = target;
            // The coloured cap ends a little before the outer stone bevel.
            // Seat that intact cap beneath the road as well as the bevel.
            world.x -= join.landDirection * (join.extension + .6f);
            // The original sloping shoulder is narrower than the allowed deck.
            // Widen the intact bank across the road, keeping its closed shell.
            world.z = join.anchor.z + (world.z - join.anchor.z) * 1.35f;
            positions[i] = toLocal.MultiplyPoint3x4(world);
        }
        Mesh result = new Mesh { name = readable.name.Replace("_EditorCopy", "_RoadJoin"), indexFormat = readable.indexFormat };
        result.vertices = positions; result.normals = readable.normals;
        if (readable.tangents.Length == positions.Length) result.tangents = readable.tangents;
        if (readable.colors.Length == positions.Length) result.colors = readable.colors;
        for (int channel = 0; channel < 8; channel++)
        {
            var uv = new List<Vector4>(); readable.GetUVs(channel, uv); if (uv.Count == positions.Length) result.SetUVs(channel, uv);
        }
        result.subMeshCount = readable.subMeshCount;
        for (int submesh = 0; submesh < readable.subMeshCount; submesh++) result.SetTriangles(readable.GetTriangles(submesh), submesh, false);
        // Keep the imported hard-edge and cap lighting; the cap is only levelled
        // by a small amount and retains the authored material/normal seams.
        result.RecalculateBounds();
        return result;
    }

    private static Vector3 Deform(Vector3 point, IReadOnlyList<Join> joins)
    {
        Vector3 value = point;
        float xOffset = 0f;
        foreach (Join join in joins)
        {
            float target = join.anchor.y + BankSurfaceAboveAnchor;
            float height = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(target - 3f, target - 1.5f, point.y));
            float transverse = 1f - Smooth(join.halfWidth + FlatWidthMargin, join.halfWidth + BlendWidthMargin, Mathf.Abs(point.z - join.anchor.z));
            float land = 1f - Smooth(FlatLength + join.extension, BlendLength, (point.x - join.anchor.x) * join.landDirection);
            xOffset -= join.landDirection * join.extension * height * transverse * land;
        }
        value.x += xOffset;
        float yOffset = 0f, total = 0f;
        foreach (Join join in joins)
        {
            float target = join.anchor.y + BankSurfaceAboveAnchor;
            float height = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(target - 3f, target - 1.5f, point.y));
            float transverse = 1f - Smooth(join.halfWidth + FlatWidthMargin, join.halfWidth + BlendWidthMargin, Mathf.Abs(point.z - join.anchor.z));
            float land = 1f - Smooth(FlatLength, BlendLength, (value.x - join.anchor.x) * join.landDirection);
            float weight = height * transverse * land;
            yOffset += (target - point.y) * weight; total += weight;
        }
        value.y += total > 1f ? yOffset / total : yOffset;
        return value;
    }

    private static float Smooth(float minimum, float maximum, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(minimum, maximum, value));

    private static void Split(List<Vertex> polygon, Cut cut, List<List<Vertex>> result)
    {
        float min = polygon.Min(cut.Distance), max = polygon.Max(cut.Distance);
        if (min >= -PlaneEpsilon || max <= PlaneEpsilon) { result.Add(polygon); return; }
        var negative = new List<Vertex>(); var positive = new List<Vertex>();
        for (int i = 0; i < polygon.Count; i++)
        {
            Vertex a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            float da = cut.Distance(a), db = cut.Distance(b);
            if (da <= PlaneEpsilon) AddDistinct(negative, a);
            if (da >= -PlaneEpsilon) AddDistinct(positive, a);
            if ((da < -PlaneEpsilon && db > PlaneEpsilon) || (da > PlaneEpsilon && db < -PlaneEpsilon))
            {
                Vertex crossing = Vertex.Between(a, b, da / (da - db));
                AddDistinct(negative, crossing); AddDistinct(positive, crossing);
            }
        }
        TrimClosing(negative); TrimClosing(positive);
        if (negative.Count >= 3) result.Add(negative);
        if (positive.Count >= 3) result.Add(positive);
    }
    private static void AddDistinct(List<Vertex> values, Vertex value)
    {
        if (values.Count == 0 || (values[values.Count - 1].world - value.world).sqrMagnitude > 1e-12f) values.Add(value);
    }
    private static void TrimClosing(List<Vertex> values)
    {
        if (values.Count > 1 && (values[values.Count - 1].world - values[0].world).sqrMagnitude <= 1e-12f) values.RemoveAt(values.Count - 1);
    }

    private static Mesh ReadableCopy(Mesh original)
    {
        var result = new Mesh { name = original.name + "_EditorCopy", indexFormat = original.indexFormat };
        try
        {
            using (var input = MeshUtility.AcquireReadOnlyMeshData(original))
            {
                var output = Mesh.AllocateWritableMeshData(1);
                try
                {
                    var writable = output[0];
                    writable.SetVertexBufferParams(original.vertexCount, original.GetVertexAttributes());
                    for (int stream = 0; stream < original.vertexBufferCount; stream++) writable.GetVertexData<byte>(stream).CopyFrom(input[0].GetVertexData<byte>(stream));
                    int count = original.indexFormat == IndexFormat.UInt16 ? input[0].GetIndexData<ushort>().Length : input[0].GetIndexData<uint>().Length;
                    writable.SetIndexBufferParams(count, original.indexFormat);
                    if (original.indexFormat == IndexFormat.UInt16) writable.GetIndexData<ushort>().CopyFrom(input[0].GetIndexData<ushort>());
                    else writable.GetIndexData<uint>().CopyFrom(input[0].GetIndexData<uint>());
                    writable.subMeshCount = original.subMeshCount;
                    for (int submesh = 0; submesh < original.subMeshCount; submesh++) writable.SetSubMesh(submesh, input[0].GetSubMesh(submesh), MeshUpdateFlags.DontRecalculateBounds);
                    Mesh.ApplyAndDisposeWritableMeshData(output, result, MeshUpdateFlags.DontRecalculateBounds);
                    output = default;
                }
                finally { if (output.Length > 0) output.Dispose(); }
            }
            result.bounds = original.bounds;
            return result;
        }
        catch { UnityEngine.Object.DestroyImmediate(result); throw; }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
#endif
