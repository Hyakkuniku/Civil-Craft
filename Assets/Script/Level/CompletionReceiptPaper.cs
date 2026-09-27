using UnityEngine;
using UnityEngine.UI;

// A UI mesh rather than a bitmap, so the receipt edge stays crisp when scaled.
public sealed class CompletionReceiptPaper : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float tooth = Mathf.Min(10f, r.height * .02f);
        AddQuad(vh, new Vector2(r.xMin, r.yMin + tooth), new Vector2(r.xMax, r.yMax - tooth));
        int teeth = Mathf.Max(2, Mathf.RoundToInt(r.width / 22f));
        for (int i = 0; i < teeth; i++)
        {
            float left = Mathf.Lerp(r.xMin, r.xMax, (float)i / teeth);
            float right = Mathf.Lerp(r.xMin, r.xMax, (float)(i + 1) / teeth);
            AddTriangle(vh, new Vector2(left, r.yMax - tooth),
                new Vector2((left + right) * .5f, r.yMax), new Vector2(right, r.yMax - tooth));
            AddTriangle(vh, new Vector2(left, r.yMin + tooth),
                new Vector2(right, r.yMin + tooth), new Vector2((left + right) * .5f, r.yMin));
        }
    }
    private void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c)
    {
        int index = vh.currentVertCount;
        vh.AddVert(a, color, Vector2.zero);
        vh.AddVert(b, color, Vector2.zero);
        vh.AddVert(c, color, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
    }
    private void AddQuad(VertexHelper vh, Vector2 min, Vector2 max)
    {
        AddTriangle(vh, min, new Vector2(min.x, max.y), max);
        AddTriangle(vh, min, max, new Vector2(max.x, min.y));
    }
}
