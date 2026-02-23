using System.Collections;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class BuildableVisual : MonoBehaviour
{
    private MeshRenderer meshRenderer;
    private RuntimeWireframe wireframe;
    private Coroutine transitionCoroutine;

    public float transitionDuration = 0.5f; // How long the fade takes

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        wireframe = GetComponent<RuntimeWireframe>();

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
        if (meshRenderer != null) meshRenderer.enabled = true;
        if (wireframe != null)
        {
            wireframe.transitionAlpha = 0f;
            wireframe.enabled = false;
        }
    }

    private IEnumerator AnimateTransition(bool enteringBuildMode)
    {
        float elapsed = 0f;
        float startAlpha = enteringBuildMode ? 0f : 1f;
        float targetAlpha = enteringBuildMode ? 1f : 0f;

        // Ensure both are visible during the crossfade
        if (meshRenderer != null) meshRenderer.enabled = true;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / transitionDuration;
            
            // Optional: Use smoothstep for a nicer easing curve
            t = t * t * (3f - 2f * t);

            if (wireframe != null)
                wireframe.transitionAlpha = Mathf.Lerp(startAlpha, targetAlpha, t);

            yield return null;
        }

        // Finalize states
        if (wireframe != null) wireframe.transitionAlpha = targetAlpha;
        
        if (enteringBuildMode)
        {
            if (meshRenderer != null) meshRenderer.enabled = false;
        }
        else
        {
            if (wireframe != null) wireframe.enabled = false;
        }
    }
}