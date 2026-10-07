using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps transparent tutorial references in front of the blueprint background.
/// Owns material copies per ghost container; authored materials remain unchanged.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialGhostRendering : MonoBehaviour
{
    public const int GhostRenderQueue = BuildGridRendering.GridRenderQueue + 10;

    private readonly Dictionary<Material, Material> ownedBySource = new Dictionary<Material, Material>();
    private readonly Dictionary<Material, Material> sourceByOwned = new Dictionary<Material, Material>();
    private readonly HashSet<Renderer> configuredRenderers = new HashSet<Renderer>();

    public static TutorialGhostRendering Ensure(GameObject ghostContainer)
    {
        if (ghostContainer == null) return null;
        TutorialGhostRendering rendering = ghostContainer.GetComponent<TutorialGhostRendering>();
        return rendering != null ? rendering : ghostContainer.AddComponent<TutorialGhostRendering>();
    }

    public void Configure(Renderer visual)
    {
        if (visual == null || !visual.transform.IsChildOf(transform)) return;

        Material[] materials = visual.sharedMaterials;
        bool changed = false;
        for (int index = 0; index < materials.Length; index++)
        {
            Material source = materials[index];
            // Opaque visuals already write depth and remain visible over the
            // grid. Preserve them, and any deliberately later-ordered material.
            if (source == null || source.renderQueue < (int)RenderQueue.Transparent ||
                source.renderQueue > BuildGridRendering.GridRenderQueue) continue;

            if (!ownedBySource.TryGetValue(source, out Material owned))
            {
                owned = new Material(source)
                {
                    name = source.name + " (Tutorial Ghost)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (owned.HasProperty("_QueueControl")) owned.SetFloat("_QueueControl", 1f);
                if (owned.HasProperty("_QueueOffset"))
                    owned.SetFloat("_QueueOffset", GhostRenderQueue - (int)RenderQueue.Transparent);
                owned.renderQueue = GhostRenderQueue;
                // Do not change transparency, depth testing, depth writes,
                // culling or shader keywords. Real bridge parts still occlude it.
                ownedBySource.Add(source, owned);
                sourceByOwned.Add(owned, source);
            }

            materials[index] = owned;
            changed = true;
        }

        if (!changed) return;
        visual.sharedMaterials = materials;
        configuredRenderers.Add(visual);
    }

    private void OnDestroy()
    {
        foreach (Renderer visual in configuredRenderers)
        {
            if (visual == null) continue;
            Material[] materials = visual.sharedMaterials;
            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                Material owned = materials[index];
                if (owned == null || !sourceByOwned.TryGetValue(owned, out Material source)) continue;
                materials[index] = source;
                changed = true;
            }
            if (changed) visual.sharedMaterials = materials;
        }

        foreach (Material owned in ownedBySource.Values)
        {
            if (owned == null) continue;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }
        configuredRenderers.Clear();
        sourceByOwned.Clear();
        ownedBySource.Clear();
    }
}
