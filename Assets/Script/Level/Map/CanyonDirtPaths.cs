using System;
using System.Collections.Generic;
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
        [Tooltip("Optional surface for this street. Leave empty to use the component's default Surface Shader.")]
        public Shader surfaceShader;
        public Street(Vector2 a, Vector2 b)
        {
            from = a;
            to = b;
            widthMultiplier = 1;
            surfaceShader = null;
        }
    }

    public Transform canyon;
    public Shader surfaceShader;
    [Tooltip("Road width as a fraction of the shorter canyon dimension.")]
    [Range(.01f, .2f)] public float width = .075f;
    [Range(.05f, .8f)] public float edgeSoftness = .35f;
    [Range(0, 1)] public float strength = .65f;
    [Tooltip("Warm dirt color painted over the terrain. The route keeps soft edges and receives the main light's shadows.")]
    public Color dirtTint = new Color(.78f, .70f, .60f, 1);
    [HideInInspector, Tooltip("Neutral road color painted over the underlying terrain.")]
    public Color roadTint = new Color(.30f, .32f, .34f, 1);
    [HideInInspector, Range(0f, .6f), Tooltip("Width of each sidewalk as a fraction of the road's half-width. Set to 0 to hide sidewalks.")]
    public float sidewalkWidth = .22f;
    [HideInInspector] public Color sidewalkTint = new Color(.66f, .65f, .62f, 1);
    [HideInInspector] public Color curbTint = new Color(.82f, .80f, .74f, 1);
    [HideInInspector] public Color stoneTint = new Color(.86f, .75f, .62f, 1);
    [HideInInspector] public Color groutTint = new Color(.69f, .59f, .48f, 1);
    [HideInInspector, Range(0f, .12f)] public float stoneVariation = .05f;
    [HideInInspector, Range(3f, 5f)] public float stonesAcross = 4f;
    [HideInInspector, Range(.6f, 2.5f)] public float stoneAspect = 1.2f;
    [HideInInspector, Range(.01f, .08f)] public float jointWidth = .03f;
    [Tooltip("Coordinates run from 0 to 1 across the canyon's world X/Z bounds. Up to 16 segments.")]
    public Street[] streets = {
        new Street(new Vector2(.02f, .46f), new Vector2(.35f, .46f)),
        new Street(new Vector2(.35f, .46f), new Vector2(.65f, .50f)),
        new Street(new Vector2(.65f, .50f), new Vector2(.98f, .52f)),
        new Street(new Vector2(.50f, .12f), new Vector2(.49f, .48f)),
        new Street(new Vector2(.49f, .48f), new Vector2(.55f, .87f))
    };

    private sealed class SurfaceOverlay
    {
        public Shader shader;
        public GameObject gameObject;
        public MeshRenderer renderer;
        public Material material;
        public readonly Vector4[] segments = new Vector4[16];
        public readonly Vector4[] segmentWidths = new Vector4[16];
        public int count;
    }

    private readonly List<SurfaceOverlay> surfaces = new List<SurfaceOverlay>(4);
    private MeshRenderer sourceRenderer;
    private MeshFilter sourceFilter;
    private Mesh sourceMesh;
    private Shader lastDefaultShader;
    private Matrix4x4 lastMatrix;
    private bool dirty = true;
    private bool surfaceCreatedForPlay;

    public Bounds SurfaceBounds => sourceRenderer != null ? sourceRenderer.bounds :
        canyon != null && canyon.TryGetComponent(out MeshRenderer r) ? r.bounds : new Bounds(transform.position, Vector3.one);

    private void OnEnable() { dirty = true; }
    private void OnValidate() { dirty = true; } // Unity may invoke this off the main thread.
    public void Refresh() { dirty = true; }

    // Pass a referenced Shader asset when changing the default during play.
    // Streets with an override keep their own surface.
    public void SetSurfaceShader(Shader shader)
    {
        if (surfaceShader == shader) return;
        surfaceShader = shader;
        Refresh();
    }

    public void SetStreetSurfaceShader(int streetIndex, Shader shader)
    {
        if (streets == null || streetIndex < 0 || streetIndex >= streets.Length) return;
        Street street = streets[streetIndex];
        if (street.surfaceShader == shader) return;
        street.surfaceShader = shader;
        streets[streetIndex] = street;
        Refresh();
    }

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
        if (canyon == null)
        {
            if (surfaces.Count > 0) Release();
            return;
        }
        MeshFilter selectedFilter = canyon.GetComponent<MeshFilter>();
        MeshRenderer selectedRenderer = canyon.GetComponent<MeshRenderer>();
        if (selectedFilter == null || selectedFilter.sharedMesh == null || selectedRenderer == null)
        {
            if (surfaces.Count > 0) Release();
            return;
        }
        bool playing = Application.IsPlaying(gameObject);
        bool missingOverlay = false;
        foreach (SurfaceOverlay overlay in surfaces)
            if (overlay.gameObject == null || overlay.renderer == null || overlay.material == null)
                missingOverlay = true;
        if (dirty || missingOverlay || sourceFilter != selectedFilter ||
            sourceRenderer != selectedRenderer || sourceMesh != selectedFilter.sharedMesh ||
            surfaceCreatedForPlay != playing || lastDefaultShader != surfaceShader ||
            lastMatrix != canyon.localToWorldMatrix)
        {
            Release();
            sourceFilter = selectedFilter;
            sourceRenderer = selectedRenderer;
            sourceMesh = selectedFilter.sharedMesh;
            surfaceCreatedForPlay = playing;
            lastDefaultShader = surfaceShader;
            lastMatrix = canyon.localToWorldMatrix;
            BuildSurfaces(playing);
            dirty = false;
        }
        foreach (SurfaceOverlay overlay in surfaces)
        {
            overlay.gameObject.layer = canyon.gameObject.layer;
            overlay.renderer.enabled = sourceRenderer.enabled;
            overlay.renderer.forceRenderingOff = sourceRenderer.forceRenderingOff;
            overlay.renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        }
    }

    private void BuildSurfaces(bool playing)
    {
        Bounds b = sourceRenderer.bounds;
        float size = Mathf.Max(.001f, Mathf.Min(b.size.x, b.size.z));
        int count = Mathf.Min(16, streets == null ? 0 : streets.Length);
        for (int i = 0; i < count; i++)
        {
            Street street = streets[i];
            Shader shader = street.surfaceShader != null ? street.surfaceShader : surfaceShader;
            if (shader == null) continue;
            SurfaceOverlay overlay = null;
            foreach (SurfaceOverlay candidate in surfaces)
                if (candidate.shader == shader) { overlay = candidate; break; }
            if (overlay == null)
            {
                overlay = new SurfaceOverlay { shader = shader };
                surfaces.Add(overlay);
            }
            Vector2 a = street.from, z = street.to;
            int slot = overlay.count++;
            overlay.segments[slot] = new Vector4(a.x * b.size.x / size, a.y * b.size.z / size,
                z.x * b.size.x / size, z.y * b.size.z / size);
            Vector2 segmentSpan = new Vector2(overlay.segments[slot].z - overlay.segments[slot].x,
                overlay.segments[slot].w - overlay.segments[slot].y);
            overlay.segmentWidths[slot] = new Vector4(
                street.widthMultiplier > 0 ? Mathf.Clamp(street.widthMultiplier, .1f, 2) : 1,
                segmentSpan.magnitude, 0, 0);
        }
        foreach (SurfaceOverlay overlay in surfaces)
        {
            bool isRoad = overlay.shader.name == "Civil Craft/Canyon Road Surface";
            bool isStone = overlay.shader.name == "Civil Craft/Canyon Stone Path";
            string name = isRoad ? "Road Surface" :
                isStone ? "Stone Path Surface" : "Dirt Surface";
            overlay.material = new Material(overlay.shader)
            {
                name = name + " (instance only)",
                hideFlags = HideFlags.HideAndDontSave
            };
            // One mesh overlay per distinct shader, not one per street.
            overlay.gameObject = new GameObject(name + " (generated, no collider)")
            {
                hideFlags = playing ? HideFlags.None : HideFlags.HideAndDontSave
            };
            overlay.gameObject.transform.SetParent(canyon, false);
            overlay.gameObject.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
            overlay.renderer = overlay.gameObject.AddComponent<MeshRenderer>();
            var materials = new Material[sourceMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = overlay.material;
            overlay.renderer.sharedMaterials = materials;
            overlay.renderer.shadowCastingMode = ShadowCastingMode.Off;
            overlay.renderer.receiveShadows = false; // Each shader samples the main light itself.
            overlay.renderer.lightProbeUsage = LightProbeUsage.Off;
            overlay.renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ConfigureMaterial(overlay, b, size);
        }
    }

    private void ConfigureMaterial(SurfaceOverlay overlay, Bounds b, float size)
    {
        Material material = overlay.material;
        material.SetVector("_PathBounds", new Vector4(b.min.x, b.min.z, 1 / size, 0));
        material.SetVectorArray("_Segments", overlay.segments);
        material.SetVectorArray("_SegmentWidths", overlay.segmentWidths);
        material.SetInt("_SegmentCount", overlay.count);
        material.SetFloat("_Width", Mathf.Clamp(width, .01f, .2f));
        material.SetFloat("_Softness", Mathf.Clamp(edgeSoftness, .05f, .8f));
        material.SetFloat("_Strength", Mathf.Clamp01(strength));
        if (material.HasProperty("_RoadTint"))
        {
            material.SetColor("_RoadTint", roadTint);
            material.SetFloat("_SidewalkWidth", Mathf.Clamp(sidewalkWidth, 0f, .6f));
            material.SetColor("_SidewalkTint", sidewalkTint);
            material.SetColor("_CurbTint", curbTint);
        }
        else if (material.HasProperty("_StoneTint"))
        {
            material.SetColor("_StoneTint", stoneTint);
            material.SetColor("_GroutTint", groutTint);
            material.SetFloat("_StoneVariation", Mathf.Clamp(stoneVariation, 0f, .12f));
            material.SetFloat("_StonesAcross", Mathf.Clamp(stonesAcross, 3f, 5f));
            material.SetFloat("_StoneAspect", Mathf.Clamp(stoneAspect, .6f, 2.5f));
            material.SetFloat("_JointWidth", Mathf.Clamp(jointWidth, .01f, .08f));
        }
        else if (material.HasProperty("_DirtTint"))
            material.SetColor("_DirtTint", dirtTint);
    }

    private void OnDisable() { Release(); }
    private void OnDestroy() { Release(); }
    private void Release()
    {
        // Runtime destruction is deferred; hide the old overlay immediately.
        foreach (SurfaceOverlay overlay in surfaces)
        {
            if (overlay.renderer != null) overlay.renderer.enabled = false;
            Dispose(overlay.gameObject);
            Dispose(overlay.material);
        }
        surfaces.Clear();
        sourceRenderer = null;
        sourceFilter = null;
        sourceMesh = null;
    }
    private static void Dispose(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
