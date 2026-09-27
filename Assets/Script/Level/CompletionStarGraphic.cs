using UnityEngine;
using UnityEngine.UI;

// Vector stars do not depend on a font containing the star glyph on Android.
[DefaultExecutionOrder(-30)]
public sealed class CompletionStarGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * .48f;
        vh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i < 10; i++)
        {
            float angle = (90f - i * 36f) * Mathf.Deg2Rad;
            float r = radius * (i % 2 == 0 ? 1f : .45f);
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r, color,
                Vector2.zero);
        }
        for (int i = 0; i < 10; i++) vh.AddTriangle(0, i + 1, (i + 1) % 10 + 1);
    }
}
