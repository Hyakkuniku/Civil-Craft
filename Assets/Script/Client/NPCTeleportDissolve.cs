using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Temporarily replaces an NPC's visible mesh materials with the Civil Craft
/// dissolve shader, then restores the exact original materials after the effect.
/// </summary>
[DisallowMultipleComponent]
public sealed class NPCTeleportDissolve : MonoBehaviour
{
    private const string ShaderName = "CivilCraft/NPC Teleport Dissolve";
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int AlphaCutoffId = Shader.PropertyToID("_AlphaCutoff");
    private static readonly int CullId = Shader.PropertyToID("_Cull");

    private sealed class RendererState
    {
        public Renderer renderer;
        public Material[] originalMaterials;
        public Material[] dissolveMaterials;
    }

    private readonly List<RendererState> states = new List<RendererState>();
    private bool prepared;

    public bool Prepare(Transform visualRoot, Color edgeColor, float noiseScale, float edgeWidth)
    {
        RestoreOriginalMaterials();

        Shader dissolveShader = Resources.Load<Shader>("Shaders/NPCTeleportDissolve");
        if (dissolveShader == null) dissolveShader = Shader.Find(ShaderName);
        if (dissolveShader == null)
        {
            Debug.LogWarning(
                $"[NPCTeleportDissolve] Shader '{ShaderName}' was not found. " +
                "The NPC will still teleport without the visual effect.", this);
            return false;
        }

        if (visualRoot == null) visualRoot = transform;
        foreach (Renderer renderer in visualRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer is ParticleSystemRenderer ||
                renderer is TrailRenderer || renderer is LineRenderer ||
                renderer is SpriteRenderer || renderer.GetComponent<TMP_Text>() != null)
            {
                continue;
            }

            Material[] originals = renderer.sharedMaterials;
            if (originals == null || originals.Length == 0) continue;

            Material[] replacements = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
                replacements[i] = CreateDissolveMaterial(
                    dissolveShader, originals[i], edgeColor, noiseScale, edgeWidth);

            states.Add(new RendererState
            {
                renderer = renderer,
                originalMaterials = originals,
                dissolveMaterials = replacements
            });
            renderer.sharedMaterials = replacements;
        }

        prepared = states.Count > 0;
        SetDissolveAmount(0f);
        return prepared;
    }

    public IEnumerator DissolveOut(float duration)
    {
        yield return AnimateDissolve(0f, 1f, duration);
    }

    public IEnumerator DissolveIn(float duration)
    {
        yield return AnimateDissolve(1f, 0f, duration);
        RestoreOriginalMaterials();
    }

    private IEnumerator AnimateDissolve(float from, float to, float duration)
    {
        if (!prepared) yield break;

        duration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;
        SetDissolveAmount(from);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetDissolveAmount(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)));
            yield return null;
        }

        SetDissolveAmount(to);
    }

    private void SetDissolveAmount(float amount)
    {
        foreach (RendererState state in states)
        {
            if (state == null || state.dissolveMaterials == null) continue;
            foreach (Material material in state.dissolveMaterials)
                if (material != null) material.SetFloat(DissolveAmountId, amount);
        }
    }

    private static Material CreateDissolveMaterial(
        Shader shader,
        Material original,
        Color edgeColor,
        float noiseScale,
        float edgeWidth)
    {
        Material material = new Material(shader)
        {
            name = original != null
                ? original.name + " (Teleport Dissolve)"
                : "NPC Teleport Dissolve"
        };

        Texture texture = null;
        Vector2 textureScale = Vector2.one;
        Vector2 textureOffset = Vector2.zero;
        Color baseColor = Color.white;
        float alphaCutoff = 0.03f;
        float cull = 2f;

        if (original != null)
        {
            string textureProperty = original.HasProperty("_BaseMap")
                ? "_BaseMap"
                : original.HasProperty("_MainTex") ? "_MainTex" : null;
            if (!string.IsNullOrEmpty(textureProperty))
            {
                texture = original.GetTexture(textureProperty);
                textureScale = original.GetTextureScale(textureProperty);
                textureOffset = original.GetTextureOffset(textureProperty);
            }

            if (original.HasProperty("_BaseColor")) baseColor = original.GetColor("_BaseColor");
            else if (original.HasProperty("_Color")) baseColor = original.GetColor("_Color");
            if (original.HasProperty("_Cutoff")) alphaCutoff = original.GetFloat("_Cutoff");
            if (original.HasProperty("_Cull")) cull = original.GetFloat("_Cull");
        }

        material.SetTexture(BaseMapId, texture != null ? texture : Texture2D.whiteTexture);
        material.SetTextureScale("_BaseMap", textureScale);
        material.SetTextureOffset("_BaseMap", textureOffset);
        material.SetColor(BaseColorId, baseColor);
        material.SetColor(EdgeColorId, edgeColor);
        material.SetFloat(NoiseScaleId, noiseScale);
        material.SetFloat(EdgeWidthId, edgeWidth);
        material.SetFloat(AlphaCutoffId, Mathf.Clamp01(alphaCutoff));
        material.SetFloat(CullId, cull);
        material.SetFloat(DissolveAmountId, 0f);
        return material;
    }

    private void RestoreOriginalMaterials()
    {
        foreach (RendererState state in states)
        {
            if (state == null) continue;
            if (state.renderer != null)
                state.renderer.sharedMaterials = state.originalMaterials;

            if (state.dissolveMaterials == null) continue;
            foreach (Material material in state.dissolveMaterials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }

        states.Clear();
        prepared = false;
    }

    private void OnDisable()
    {
        RestoreOriginalMaterials();
    }

    private void OnDestroy()
    {
        RestoreOriginalMaterials();
    }
}
