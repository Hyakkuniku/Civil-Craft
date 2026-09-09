using System;
using UnityEngine;
using UnityEngine.Rendering;

// A visual-only surface treatment. Never modifies the imported mesh, materials or navigation.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class CanyonDirtPaths : MonoBehaviour
{
    [Serializable]
    public struct Street
    {
        public Vector2 from;
        public Vector2 to;
        [Tooltip("0 keeps standard width; use smaller values for side lanes.")]
        [Range(0, 2)] public float widthMultiplier;
        public Street(Vector2 a, Vector2 b) { from = a; to = b; widthMultiplier = 1; }
    }

    public Transform canyon;
    public Shader surfaceShader;
    [Tooltip("Road width as a fraction of the shorter canyon dimension.")]
    [Range(.01f, .2f)] public float width = .075f;
    [Range(.05f, .8f)] public float edgeSoftness = .35f;
    [Range(0, 1)] public float strength = .65f;
    [Tooltip("Multiplies the existing sand color, preserving its lighting and shadows.")]
    public Color dirtTint = new Color(.78f, .70f, .60f, 1);
    [Tooltip("Coordinates run from 0 to 1 across the canyon's world X/Z bounds. Up to 16 segments.")]
    public Street[] streets = {
        new Street(new Vector2(.02f, .46f), new Vector2(.35f, .46f)),
        new Street(new Vector2(.35f, .46f), new Vector2(.65f, .50f)),
        new Street(new Vector2(.65f, .50f), new Vector2(.98f, .52f)),
        new Street(new Vector2(.50f, .12f), new Vector2(.49f, .48f)),
        new Street(new Vector2(.49f, .48f), new Vector2(.55f, .87f))
    };

    private GameObject surface;
    private MeshRenderer surfaceRenderer;
    private MeshRenderer sourceRenderer;
    private MeshFilter sourceFilter;
    private Material material;
    private readonly Vector4[] segments = new Vector4[16];
    private readonly Vector4[] segmentWidths = new Vector4[16];
    private Matrix4x4 lastMatrix;
    private bool dirty = true;
    private bool surfaceCreatedForPlay;

    public Bounds SurfaceBounds => sourceRenderer != null ? sourceRenderer.bounds :
        canyon != null && canyon.TryGetComponent(out MeshRenderer r) ? r.bounds : new Bounds(transform.position, Vector3.one);

    private void OnEnable() { dirty = true; }
    private void OnValidate() { dirty = true; } // Unity may invoke this off the main thread.
    public void Refresh() { dirty = true; }

    // Called after editor save serialization has finished. Recreate transient
    // objects and shader arrays without modifying any authored street settings.
    public void RebuildGeneratedSurface()
    {
        if (!isActiveAndEnabled) return;
        Release();
        dirty = true;
        LateUpdate();
    }

    private void LateUpdate()
    {
        if (canyon == null || surfaceShader == null)
        {
            if (surface != null) Release();
            return;
        }
        bool playing = Application.IsPlaying(gameObject);
        if (surface == null || surfaceRenderer == null || sourceRenderer == null ||
            surfaceCreatedForPlay != playing ||
            sourceFilter == null || sourceFilter.transform != canyon || material == null || material.shader != surfaceShader)
        {
            Release();
            sourceFilter = canyon.GetComponent<MeshFilter>();
            sourceRenderer = canyon.GetComponent<MeshRenderer>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null || sourceRenderer == null) return;
            material = new Material(surfaceShader) { name = "Main City Dirt (instance only)", hideFlags = HideFlags.HideAndDontSave };
            // Game cameras need a normal scene renderer, not an editor preview.
            // Also rebuild on mode changes when domain/scene reload is disabled.
            surfaceCreatedForPlay = playing;
            surface = new GameObject("Dirt Surface (generated, no collider)")
            {
                hideFlags = playing ? HideFlags.None : HideFlags.HideAndDontSave
            };
            surface.transform.SetParent(canyon, false);
            surface.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            surfaceRenderer = surface.AddComponent<MeshRenderer>();
            var materials = new Material[sourceFilter.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            surfaceRenderer.sharedMaterials = materials;
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = false; // Underlying canyon already supplies lighting/shadows.
            surfaceRenderer.lightProbeUsage = LightProbeUsage.Off;
            surfaceRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            dirty = true;
        }
        surface.layer = canyon.gameObject.layer;
        surfaceRenderer.enabled = sourceRenderer.enabled;
        surfaceRenderer.forceRenderingOff = sourceRenderer.forceRenderingOff;
        surfaceRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        if (!dirty && lastMatrix == canyon.localToWorldMatrix) return;
        lastMatrix = canyon.localToWorldMatrix;
        dirty = false;
        Bounds b = SurfaceBounds;
        float size = Mathf.Max(.001f, Mathf.Min(b.size.x, b.size.z));
        int count = Mathf.Min(16, streets == null ? 0 : streets.Length);
        for (int i = 0; i < count; i++)
        {
            Vector2 a = streets[i].from, z = streets[i].to;
            segments[i] = new Vector4(a.x * b.size.x / size, a.y * b.size.z / size,
                z.x * b.size.x / size, z.y * b.size.z / size);
            segmentWidths[i] = new Vector4(streets[i].widthMultiplier > 0 ? Mathf.Clamp(streets[i].widthMultiplier, .1f, 2) : 1, 0, 0, 0);
        }
        material.SetVector("_PathBounds", new Vector4(b.min.x, b.min.z, 1 / size, 0));
        material.SetVectorArray("_Segments", segments);
        material.SetVectorArray("_SegmentWidths", segmentWidths);
        material.SetInt("_SegmentCount", count);
        material.SetFloat("_Width", Mathf.Clamp(width, .01f, .2f));
        material.SetFloat("_Softness", Mathf.Clamp(edgeSoftness, .05f, .8f));
        material.SetFloat("_Strength", Mathf.Clamp01(strength));
        material.SetColor("_DirtTint", dirtTint);
    }

    private void OnDisable() { Release(); }
    private void OnDestroy() { Release(); }
    private void Release()
    {
        // Runtime destruction is deferred; hide the old overlay immediately.
        if (surfaceRenderer != null) surfaceRenderer.enabled = false;
        Dispose(surface);
        Dispose(material);
        surface = null;
        surfaceRenderer = null;
        material = null;
        sourceRenderer = null;
        sourceFilter = null;
    }
    private static void Dispose(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
