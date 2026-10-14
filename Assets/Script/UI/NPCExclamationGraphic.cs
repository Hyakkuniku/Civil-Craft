using UnityEngine;
using UnityEngine.UI;

/// <summary>High-contrast vector badge: no font glyph, texture or world depth dependency.</summary>
[DisallowMultipleComponent]
public sealed class NPCExclamationGraphic : MaskableGraphic
{
    private static readonly Color32 Ink = new Color32(65, 40, 25, 255);
    private static readonly Color32 Gold = new Color32(255, 198, 55, 255);
    protected override void Awake() { base.Awake(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect area = rectTransform.rect;
        float radius = Mathf.Min(area.width, area.height) * .5f;
        if (radius <= 0f) return;
        Vector2 center = area.center;
        Disc(mesh, center, radius, Ink);
        Disc(mesh, center, radius * .84f, Gold);
        // Wide bar plus separated dot stays readable on a compact minimap.
        Quad(mesh, center + new Vector2(-radius * .14f, -radius * .12f),
            center + new Vector2(radius * .14f, radius * .58f), Ink);
        Disc(mesh, center + Vector2.down * radius * .43f, radius * .15f, Ink);
    }

    private static void Disc(VertexHelper mesh, Vector2 center, float radius, Color32 tint)
    {
        const int segments = 28;
        int start = mesh.currentVertCount;
        mesh.AddVert(center, tint, Vector2.zero);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
            if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    private static void Quad(VertexHelper mesh, Vector2 min, Vector2 max, Color32 tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector2(min.x, min.y), tint, Vector2.zero);
        mesh.AddVert(new Vector2(min.x, max.y), tint, Vector2.zero);
        mesh.AddVert(new Vector2(max.x, max.y), tint, Vector2.zero);
        mesh.AddVert(new Vector2(max.x, min.y), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
