using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        wireframe = GetComponent<RuntimeWireframe>();

        // Safely cache materials and their starting colors
        materials = meshRenderer.materials;
        originalColors = new Color[materials.Length];
        
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i].HasProperty("_Color"))
            {
                originalColors[i] = materials[i].color;
            }
            else
            {
                originalColors[i] = Color.white; 
            }
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
            SetMaterialsToOpaque(); // Ensure it starts solid
        }
        if (wireframe != null)
        {
            wireframe.transitionAlpha = 0f;
            wireframe.enabled = false;
        }
    }

    // Helper method to apply alpha to all materials on the mesh
    private void SetMeshAlpha(float alphaMultiplier)
    {
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i].HasProperty("_Color"))
            {
                Color c = originalColors[i];
                c.a *= alphaMultiplier; 
                materials[i].color = c;
            }
        }
    }

    private IEnumerator AnimateTransition(bool enteringBuildMode)
    {
        float elapsed = 0f;
        
        float startWireAlpha = enteringBuildMode ? 0f : 1f;
        float targetWireAlpha = enteringBuildMode ? 1f : 0f;

        float startMeshAlpha = enteringBuildMode ? 1f : 0f;
        float targetMeshAlpha = enteringBuildMode ? 0f : 1f;

        if (meshRenderer != null) 
        {
            meshRenderer.enabled = true;
            SetMaterialsToTransparent(); // Unlock transparency for the fade
        }

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / transitionDuration;
            
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
            SetMaterialsToOpaque(); // Lock it back to solid Opaque mode for better performance
        }
    }

    // ────────────────────────────────────────────────
    // Standard Shader Render Mode Switchers
    // ────────────────────────────────────────────────

    private void SetMaterialsToTransparent()
    {
        foreach (Material mat in materials)
        {
            // Forces the Standard Shader into "Transparent" mode
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
    }

    private void SetMaterialsToOpaque()
    {
        foreach (Material mat in materials)
        {
            // Forces the Standard Shader back into "Opaque" mode
            mat.SetFloat("_Mode", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = -1;
        }
    }
}