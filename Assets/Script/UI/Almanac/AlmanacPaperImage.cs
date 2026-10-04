using UnityEngine;
using UnityEngine.UI;

/// <summary>Paper shading drawn as UI vertices, without a texture or material allocation.</summary>
public sealed class AlmanacPaperImage : Image
{
    [SerializeField] private bool leftPage;
    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        base.OnPopulateMesh(vertices);
        Rect rect = GetPixelAdjustedRect();
        float width = Mathf.Min(36f, rect.width * .06f);
        float outer = leftPage ? rect.xMax : rect.xMin;
        float inner = outer + (leftPage ? -width : width);
        Color seam = new Color(color.r * .76f, color.g * .72f, color.b * .66f, color.a);
        Color[] colors = leftPage ? new[] { color, color, seam, seam } : new[] { seam, seam, color, color };
        float x0 = Mathf.Min(inner, outer), x1 = Mathf.Max(inner, outer);
        var quad = new UIVertex[4];
        Vector2[] points = { new Vector2(x0, rect.yMin), new Vector2(x0, rect.yMax),
            new Vector2(x1, rect.yMax), new Vector2(x1, rect.yMin) };
        for (int i = 0; i < 4; i++)
        {
            quad[i] = UIVertex.simpleVert; quad[i].position = points[i]; quad[i].color = colors[i];
        }
        vertices.AddUIVertexQuad(quad);
    }
}
