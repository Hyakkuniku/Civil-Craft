#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PierCapSeatingProfileAuthoring
{
    public const string Path = "Assets/BridgeBuilder/Data/Resources/PierCapSeatingProfiles.asset";

    // Only creates our new measurement catalog. Existing user assets are never saved here.
    public static void EnsureProfiles()
    {
        var entries = new List<PierCapSeatingProfiles.Entry>();
        foreach (string name in new[] { "Pier", "Concrete Pier" })
        {
            BridgeMaterialSO material = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/" + name + ".asset");
            if (material == null || material.pierCapPrefab == null) throw new InvalidOperationException("Missing pier cap source: " + name);
            GameObject source = material.pierCapPrefab; var vertices = new List<Vector3>();
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null) continue;
                Matrix4x4 matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in filter.sharedMesh.vertices) vertices.Add(matrix.MultiplyPoint3x4(vertex));
            }
            if (vertices.Count == 0) throw new InvalidOperationException("No mesh vertices for " + name);
            Bounds all = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) all.Encapsulate(vertex);
            bool found = false; Bounds face = default;
            foreach (Vector3 vertex in vertices)
                if (Mathf.Abs(vertex.y - all.max.y) < .0001f)
                {
                    if (!found) { face = new Bounds(vertex, Vector3.zero); found = true; }
                    else face.Encapsulate(vertex);
                }
            if (!found || face.size.x < .01f || face.size.z < .01f)
                throw new InvalidOperationException("The cap has no measurable flat top face: " + name);
            // The two approved caps have rectangular, planar seating faces.
            foreach (float x in new[] { face.min.x, face.max.x })
                foreach (float z in new[] { face.min.z, face.max.z })
                    if (!vertices.Exists(v => Mathf.Abs(v.y - all.max.y) < .0001f && Mathf.Abs(v.x - x) < .0001f && Mathf.Abs(v.z - z) < .0001f))
                        throw new InvalidOperationException("Cap seating face is not rectangular: " + name);
            entries.Add(new PierCapSeatingProfiles.Entry { source = source, sourceBounds = all, topFace = face });
        }
        var existing = AssetDatabase.LoadAssetAtPath<PierCapSeatingProfiles>(Path);
        if (existing != null)
        {
            if (existing.entries.Length != entries.Count) throw new InvalidOperationException("Pier seating catalog needs an explicit rebuild after cap model changes.");
            for (int i = 0; i < entries.Count; i++)
                if (existing.entries[i].source != entries[i].source || existing.entries[i].sourceBounds != entries[i].sourceBounds || existing.entries[i].topFace != entries[i].topFace)
                    throw new InvalidOperationException("Pier seating catalog does not match its source mesh: " + entries[i].source.name);
            return;
        }
        if (File.Exists(Path)) throw new InvalidOperationException("Cannot overwrite an existing non-profile asset: " + Path);
        var profile = ScriptableObject.CreateInstance<PierCapSeatingProfiles>(); profile.entries = entries.ToArray();
        AssetDatabase.CreateAsset(profile, Path);
    }
}
#endif
