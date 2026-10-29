using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A fitted visual seat, not a structural member or a collision surface.</summary>
[ExecuteAlways]
public sealed class PierCapBearing : MonoBehaviour
{
    [SerializeField, HideInInspector] private Vector3[] prisms = Array.Empty<Vector3>();
    [SerializeField, HideInInspector] private Material appearance;
    [SerializeField, HideInInspector] private Mesh ownedMesh;
    [SerializeField, HideInInspector] private int meshOwner;

    public Vector3[] CaptureVertices() => (Vector3[])prisms.Clone();

    public static Material Appearance(BridgeMaterialSO material)
    {
        Renderer source = material != null && material.pierCapPrefab != null
            ? material.pierCapPrefab.GetComponentInChildren<Renderer>(true) : null;
        return source != null ? source.sharedMaterial : null;
    }

    public void SetPrisms(Vector3[] shape, Material material)
    {
        if (shape == null || shape.Length % 8 != 0 || shape.Length > 512)
            throw new ArgumentException("Invalid pier bearing geometry.");
        bool same = prisms.Length == shape.Length;
        for (int i = 0; same && i < shape.Length; i++) same = prisms[i] == shape[i];
        if (!same) prisms = (Vector3[])shape.Clone();
        appearance = material;
        if (meshOwner != GetInstanceID()) ownedMesh = null;
        if (!same || ownedMesh == null) Rebuild();
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = appearance;
    }

    private void OnEnable()
    {
        // Instantiate may copy the generated Mesh reference. A clone owns its
        // own mesh; destroying or changing either bridge must not affect the other.
        if (meshOwner != GetInstanceID()) ownedMesh = null;
        if (ownedMesh == null && prisms.Length > 0) Rebuild();
    }
    private void Rebuild()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
        if (ownedMesh == null) { ownedMesh = new Mesh { name = "Fitted pier bearing (owned)" }; meshOwner = GetInstanceID(); }
        else ownedMesh.Clear();
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
        // Each prism: bottom 0..3 around X/Z, top 4..7 in the same order.
        int[] faces = { 0,3,2,1, 4,5,6,7, 0,1,5,4, 1,2,6,5, 2,3,7,6, 3,0,4,7 };
        for (int prism = 0; prism < prisms.Length; prism += 8)
            for (int face = 0; face < faces.Length; face += 4)
            {
                int first = vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    vertices.Add(prisms[prism + faces[face + i]]);
                    uv.Add(new Vector2(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0));
                }
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
                triangles.Add(first); triangles.Add(first + 3); triangles.Add(first + 2);
            }
        ownedMesh.SetVertices(vertices); ownedMesh.SetTriangles(triangles, 0); ownedMesh.SetUVs(0, uv);
        ownedMesh.RecalculateNormals(); ownedMesh.RecalculateBounds();
        filter.sharedMesh = ownedMesh; renderer.sharedMaterial = appearance;
    }

    private void OnDestroy()
    {
        if (ownedMesh == null || meshOwner != GetInstanceID()) return;
        if (Application.isPlaying) Destroy(ownedMesh); else DestroyImmediate(ownedMesh);
        ownedMesh = null;
    }
}
