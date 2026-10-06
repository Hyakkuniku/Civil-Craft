using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Keeps the blueprint background and lines after transparent water while
/// retaining their depth test against the bridge and surrounding scenery.
/// Material copies belong to this canvas; shared materials are never changed.
/// </summary>
[DisallowMultipleComponent]
public sealed class BuildGridRendering : MonoBehaviour
{
    public const int GridRenderQueue = (int)RenderQueue.Transparent + 100;

    private sealed class GraphicMaterial
    {
        public Graphic graphic;
        public Material original;
        public Material owned;
    }

    private readonly List<GraphicMaterial> materials = new List<GraphicMaterial>();
    private readonly List<Graphic> graphics = new List<Graphic>();

    public static BuildGridRendering Ensure(Image gridImage)
    {
        if (gridImage == null) return null;

        Canvas canvas = gridImage.GetComponentInParent<Canvas>(true);
        if (canvas == null) return null;

        BuildGridRendering rendering = canvas.GetComponent<BuildGridRendering>();
        if (rendering == null) rendering = canvas.gameObject.AddComponent<BuildGridRendering>();
        rendering.Configure(gridImage);
        return rendering;
    }

    public void Configure(Image gridImage)
    {
        if (gridImage == null) return;
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null || gridImage.GetComponentInParent<Canvas>(true) != canvas) return;

        // The authored GridCanvas also contains solid blue backdrop Images.
        // Fixing only gridImage leaves those backdrops covered by the water.
        graphics.Clear();
        canvas.GetComponentsInChildren(true, graphics);
        foreach (Graphic graphic in graphics)
        {
            // A nested HUD canvas owns its own ordering and materials.
            if (graphic.GetComponentInParent<Canvas>(true) != canvas) continue;

            GraphicMaterial entry = materials.Find(item => item.graphic == graphic);
            if (entry != null && graphic.material == entry.owned) continue;

            if (entry != null)
            {
                Release(entry.owned);
                materials.Remove(entry);
            }

            Material original = graphic.material;
            if (original == null) continue;
            Material owned = new Material(original)
            {
                name = original.name + " (Build Grid)",
                hideFlags = HideFlags.HideAndDontSave
            };

            // Shader Graph Images use the graph's embedded default material,
            // so changing the separate Grid.mat asset cannot fix their queue.
            if (owned.HasProperty("_QueueControl")) owned.SetFloat("_QueueControl", 1f);
            if (owned.HasProperty("_QueueOffset")) owned.SetFloat("_QueueOffset", 100f);
            owned.renderQueue = GridRenderQueue;

            // Preserve the source shader, stencil/clip support, and LEqual
            // depth test. Never make the blueprint draw over opaque geometry.
            graphic.material = owned;
            materials.Add(new GraphicMaterial { graphic = graphic, original = original, owned = owned });
        }
        graphics.Clear();
    }

    private void OnDestroy()
    {
        foreach (GraphicMaterial entry in materials)
        {
            if (entry.graphic != null && entry.graphic.material == entry.owned)
                entry.graphic.material = entry.original;
            Release(entry.owned);
        }
        materials.Clear();
    }

    private static void Release(Material material)
    {
        if (material == null) return;
        if (Application.isPlaying) Destroy(material);
        else DestroyImmediate(material);
    }
}
