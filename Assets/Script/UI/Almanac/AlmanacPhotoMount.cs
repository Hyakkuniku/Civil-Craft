using UnityEngine;
using UnityEngine.UI;

/// <summary>Lightweight scrapbook tape on book photos, using the existing UI material.</summary>
[DisallowMultipleComponent]
public sealed class AlmanacPhotoMount : MaskableGraphic
{
    public const string DecorationName = "AlmanacPhotoTape";

    public static AlmanacPhotoMount Ensure(RectTransform frame)
    {
        if (frame == null) return null;
        Transform existing = frame.Find(DecorationName);
        AlmanacPhotoMount tape = existing != null ? existing.GetComponent<AlmanacPhotoMount>() : null;
        if (tape == null)
        {
            var child = new GameObject(DecorationName, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(AlmanacPhotoMount));
            child.layer = frame.gameObject.layer;
            child.transform.SetParent(frame, false);
            tape = child.GetComponent<AlmanacPhotoMount>();
        }

        RectTransform rect = tape.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.SetAsLastSibling();
        tape.color = new Color(.94f, .81f, .57f, .72f);
        tape.raycastTarget = false;

        // The existing photo/card geometry stays intact, including portrait fit,
        // snapshot cropping and page-turn animation. No textures are generated.
        Image paper = frame.GetComponent<Image>();
        if (paper != null)
        {
            paper.color = new Color(.98f, .955f, .89f, 1f);
            Outline outline = paper.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(.33f, .22f, .13f, .18f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
            Shadow shadow = null;
            foreach (Shadow candidate in paper.GetComponents<Shadow>())
                if (!(candidate is Outline)) { shadow = candidate; break; }
            if (shadow == null) shadow = paper.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.27f, .17f, .09f, .17f);
            shadow.effectDistance = new Vector2(3f, -4f);
            shadow.useGraphicAlpha = true;
        }
        tape.SetVerticesDirty();
        return tape;
    }

    public static void ApplyProfile(AlmanacManager book)
    {
        if (book == null || book.categories == null) return;
        foreach (AlmanacCategory category in book.categories)
        {
            if (category == null || category.tabType != AlmanacTabType.General ||
                category.leftPageZone == null) continue;
            foreach (RectTransform candidate in category.leftPageZone.GetComponentsInChildren<RectTransform>(true))
                if (candidate.name == "PortraitArea") Ensure(candidate);
        }
        // Never parent tape to the shared portrait RawImage: that image also
        // moves into the wardrobe. Its stable book card owns the decoration.
    }

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        float width = Mathf.Clamp(rect.width * .24f, 40f, 104f);
        float height = Mathf.Clamp(rect.height * .10f, 16f, 29f);
        // Overlap the paper edge by only 8px, keeping nearby titles/captions clear.
        float topInset = TapeHalfHeight(width, height, -17f) - 8f;
        float bottomInset = TapeHalfHeight(width * .92f, height, -13f) - 8f;
        DrawTape(vertices, new Vector2(rect.xMin + rect.width * .15f, rect.yMax - topInset), width, height, -17f);
        DrawTape(vertices, new Vector2(rect.xMax - rect.width * .15f, rect.yMin + bottomInset), width * .92f, height, -13f);
    }

    private static float TapeHalfHeight(float width, float height, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return .5f * (width * Mathf.Abs(Mathf.Sin(radians)) + height * Mathf.Abs(Mathf.Cos(radians)));
    }

    private void DrawTape(VertexHelper vertices, Vector2 center, float width, float height, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        Vector2 across = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 down = new Vector2(-across.y, across.x);
        DrawStrip(vertices, center + new Vector2(1f, -2f), across, down, width, height,
            new Color(.30f, .20f, .10f, .12f));
        DrawStrip(vertices, center, across, down, width, height, color);
        DrawQuad(vertices, center + down * height * .14f, across * width * .45f, down * height * .17f,
            new Color(1f, .98f, .86f, .17f));
        // Two faint paper fibres keep the translucent tape from looking like a button.
        for (int i = 0; i < 2; i++)
            DrawQuad(vertices, center + down * height * (-.23f + i * .48f), across * width * .44f,
                down * .4f, new Color(1f, .97f, .82f, .15f));
    }

    private static void DrawStrip(VertexHelper vertices, Vector2 center, Vector2 across, Vector2 down,
        float width, float height, Color tint)
    {
        int start = vertices.currentVertCount;
        vertices.AddVert(center, tint, Vector2.zero);
        // Small irregular notches at the torn ends, no raster texture or extra material.
        for (int i = 0; i < 10; i++)
        {
            Vector2 p;
            switch (i)
            {
                case 0: p = new Vector2(-.50f, -.50f); break;
                case 1: p = new Vector2(.48f, -.50f); break;
                case 2: p = new Vector2(.50f, -.22f); break;
                case 3: p = new Vector2(.47f, .02f); break;
                case 4: p = new Vector2(.50f, .27f); break;
                case 5: p = new Vector2(.48f, .50f); break;
                case 6: p = new Vector2(-.49f, .50f); break;
                case 7: p = new Vector2(-.47f, .24f); break;
                case 8: p = new Vector2(-.50f, -.02f); break;
                default: p = new Vector2(-.47f, -.26f); break;
            }
            vertices.AddVert(center + across * (p.x * width) + down * (p.y * height), tint, Vector2.zero);
        }
        for (int i = 0; i < 10; i++) vertices.AddTriangle(start, start + i + 1, start + (i + 1) % 10 + 1);
    }

    private static void DrawQuad(VertexHelper vertices, Vector2 center, Vector2 across, Vector2 down, Color tint)
    {
        int start = vertices.currentVertCount;
        vertices.AddVert(center - across - down, tint, Vector2.zero);
        vertices.AddVert(center - across + down, tint, Vector2.zero);
        vertices.AddVert(center + across + down, tint, Vector2.zero);
        vertices.AddVert(center + across - down, tint, Vector2.zero);
        vertices.AddTriangle(start, start + 1, start + 2);
        vertices.AddTriangle(start + 2, start + 3, start);
    }
}
