using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering; 

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class BuildableVisual : MonoBehaviour
{
    private MeshRenderer meshRenderer;
    private RuntimeWireframe wireframe;
    private Coroutine transitionCoroutine;

    [Header("Transition Settings")]
    public float transitionDuration = 0.5f; 

    private Material[] materials;
    private Color[] originalColors;
    
    // NEW: Track the active alpha so rapid toggling doesn't snap visually
    private float currentAlphaMultiplier = 1f;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        wireframe = GetComponent<RuntimeWireframe>();

        materials = meshRenderer.materials;
        originalColors = new Color[materials.Length];
        
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i].HasProperty("_BaseColor"))
                originalColors[i] = materials[i].GetColor("_BaseColor");
            else if (materials[i].HasProperty("_Color"))
                originalColors[i] = materials[i].color;
            else
                originalColors[i] = Color.white; 
        }

        SetNormalModeImmediate();
    }

    public void SetBuildMode()
    {
        if (wireframe == null)
            wireframe = gameObject.AddComponent<RuntimeWireframe>();

        wireframe.enabled = true;

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateTransition(true));
    }

    public void SetNormalMode()
    {
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(AnimateTransition(false));
    }

    private void SetNormalModeImmediate()
    {
        if (meshRenderer != null) 
        {
            meshRenderer.enabled = true;
            SetMeshAlpha(1f); 
            SetMaterialsToOpaque(); 
        }
        if (wireframe != null)
        {
            wireframe.transitionAlpha = 0f;
            wireframe.enabled = false;
        }
    }

    private void SetMeshAlpha(float alphaMultiplier)
    {
        currentAlphaMultiplier = alphaMultiplier; // Keep track for seamless interruptions

        for (int i = 0; i < materials.Length; i++)
        {
            Color c = originalColors[i];
            c.a *= alphaMultiplier; 

            if (materials[i].HasProperty("_BaseColor"))
                materials[i].SetColor("_BaseColor", c);
            else if (materials[i].HasProperty("_Color"))
                materials[i].color = c;
        }
    }

    private IEnumerator AnimateTransition(bool enteringBuildMode)
    {
        float elapsed = 0f;
        
        // FIX: Start from the EXACT current alpha so it doesn't jerk if interrupted
        float startWireAlpha = wireframe != null ? wireframe.transitionAlpha : (enteringBuildMode ? 0f : 1f);
        float targetWireAlpha = enteringBuildMode ? 1f : 0f;

        float startMeshAlpha = currentAlphaMultiplier;
        float targetMeshAlpha = enteringBuildMode ? 0f : 1f;

        // FIX: Shorten the duration if we are already partially transitioned
        float transitionDistance = Mathf.Abs(targetMeshAlpha - startMeshAlpha);
        float actualDuration = transitionDuration * transitionDistance;
        if (actualDuration <= 0.01f) actualDuration = 0.01f; // Prevent divide by zero

        if (meshRenderer != null) 
        {
            meshRenderer.enabled = true;
            SetMaterialsToTransparent(); 
        }

        while (elapsed < actualDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / actualDuration;
            
            t = t * t * (3f - 2f * t); // Smoothstep

            if (wireframe != null)
                wireframe.transitionAlpha = Mathf.Lerp(startWireAlpha, targetWireAlpha, t);

            SetMeshAlpha(Mathf.Lerp(startMeshAlpha, targetMeshAlpha, t));

            yield return null;
        }

        // Finalize states
        if (wireframe != null) wireframe.transitionAlpha = targetWireAlpha;
        SetMeshAlpha(targetMeshAlpha);
        
        if (enteringBuildMode)
        {
            if (meshRenderer != null) meshRenderer.enabled = false;
        }
        else
        {
            if (wireframe != null) wireframe.enabled = false;
            SetMaterialsToOpaque(); 
        }
    }

    // ────────────────────────────────────────────────
    // URP Shader Render Mode Switchers
    // ────────────────────────────────────────────────

    private void SetMaterialsToTransparent()
    {
        foreach (Material mat in materials)
        {
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            
            // SEAMLESS FIX: Keep Depth Writing ON during the fade. 
            // This stops the mesh from showing its hollow backfaces while it fades out.
            mat.SetInt("_ZWrite", 1); 
            
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent"); // Tells URP to properly sort it
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }

    private void SetMaterialsToOpaque()
    {
        foreach (Material mat in materials)
        {
            mat.SetFloat("_Surface", 0); // Opaque
            mat.SetInt("_SrcBlend", (int)BlendMode.One);
            mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;
        }
    }
}