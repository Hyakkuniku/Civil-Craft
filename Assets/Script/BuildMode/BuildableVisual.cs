// filename: BuildableVisual.cs
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]   // usually already present anyway
public class BuildableVisual : MonoBehaviour
{
    private MeshRenderer meshRenderer;
    private RuntimeWireframe wireframe;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        wireframe = GetComponent<RuntimeWireframe>();

        // Optional: start in normal mode (just to be safe)
        SetNormalMode();
    }

    public void SetBuildMode()
    {
        if (meshRenderer != null)
            meshRenderer.enabled = false;

        if (wireframe == null)
        {
            wireframe = gameObject.AddComponent<RuntimeWireframe>();
            // Optional: customize per-object if you want
            // wireframe.lineColor = Color.cyan;
        }
        else
        {
            wireframe.enabled = true;
        }
    }

    public void SetNormalMode()
    {
        if (meshRenderer != null)
            meshRenderer.enabled = true;

        if (wireframe != null)
            wireframe.enabled = false;
        // You can also Destroy(wireframe) if you want to clean up completely,
        // but keeping + disabling is usually fine (cheaper than add/remove)
    }
}