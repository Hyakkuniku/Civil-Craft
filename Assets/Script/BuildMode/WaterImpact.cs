using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Cosmetic, bounded Water2 entry events. No colliders or physics are changed.</summary>
[DefaultExecutionOrder(10010)]
[DisallowMultipleComponent]
public sealed class WaterImpact : MonoBehaviour
{
    public const int MaxImpacts = 8;
    private const int MaxSurfaces = 32;
    private static readonly int StrengthId = Shader.PropertyToID("_WaterImpactStrength");
    private static readonly int RadiusId = Shader.PropertyToID("_WaterImpactRadius");
    private static readonly int DurationId = Shader.PropertyToID("_WaterImpactDuration");
    private static readonly int DepthId = Shader.PropertyToID("_WaterDipDepth");
    private static readonly int CountId = Shader.PropertyToID("_CCWaterImpactCount");
    private static readonly int PositionId = Shader.PropertyToID("_CCWaterImpactPosition");
    private static readonly int DataId = Shader.PropertyToID("_CCWaterImpactData");
    private static readonly List<Source> sources = new List<Source>(128);
    private static readonly List<Surface> surfaces = new List<Surface>(8);
    private static readonly List<Impact> impacts = new List<Impact>(MaxImpacts);
    private static readonly Vector4[] positions = new Vector4[MaxImpacts];
    private static readonly Vector4[] data = new Vector4[MaxImpacts];
    private static readonly Vector3[] rebuildCenters = new Vector3[MaxImpacts];
    private static readonly float[] rebuildRadii = new float[MaxImpacts];
    private static WaterImpact sampler;
    private static bool quitting;
    private static int outputCount;

    private sealed class Source
    {
        public Transform transform;
        public Renderer[] renderers;
        public Rigidbody body;
        public BridgePhysicsManager manager;
        public bool requiresBody, previousValid;
        public Bounds previous;
        public uint armedSurfaces;
        public float mass;
    }

    private sealed class Surface
    {
        public MeshFilter filter;
        public Renderer renderer;
        public Material material;
        public Bounds localBounds;
        public int slot;
        public bool dirty, helperAttempted;
        public WaterImpactSurfaceMesh helper;
    }

    private struct Impact
    {
        public Surface surface;
        public Vector3 position;
        public float age, duration, severity, footprintScale;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        DisposeSurfaces();
        sources.Clear(); impacts.Clear(); sampler = null; quitting = false;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
        ClearGlobals();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeSceneLoad() => ClearGlobals();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AfterSceneLoad()
    {
        EnsureSampler();
        for (int i = 0; i < SceneManager.sceneCount; i++) DiscoverSurfaces(SceneManager.GetSceneAt(i));
    }

    private static void EnsureSampler()
    {
        if (!Application.isPlaying || quitting || sampler != null) return;
        var owner = new GameObject("Water entry effects (runtime)");
        owner.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(owner);
        sampler = owner.AddComponent<WaterImpact>();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => DiscoverSurfaces(scene);

    private static void DiscoverSurfaces(Scene scene)
    {
        if (!Application.isPlaying || !scene.IsValid() || !scene.isLoaded) return;
        // Scene-load only: all sampling later uses these cached renderers/meshes.
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                RegisterSurface(renderer.GetComponent<MeshFilter>(), renderer);
    }

    internal static bool RegisterSurface(MeshFilter filter, Renderer renderer)
    {
        if (quitting || filter == null || renderer == null || filter.sharedMesh == null) return false;
        foreach (Surface existing in surfaces) if (existing.filter == filter) return true;
        if (surfaces.Count >= MaxSurfaces) return false;
        Material material = null;
        foreach (Material candidate in renderer.sharedMaterials)
            if (candidate != null &&
                (candidate.HasProperty(StrengthId) && candidate.GetFloat(StrengthId) > 0f ||
                 candidate.HasProperty(DepthId) && candidate.GetFloat(DepthId) > 0f))
            { material = candidate; break; }
        if (material == null || !Horizontal(filter.transform)) return false;
        Bounds bounds = filter.sharedMesh.bounds;
        if (!Finite(bounds.center) || !Finite(bounds.size) || bounds.size.x <= 0f || bounds.size.z <= 0f) return false;
        uint used = 0;
        foreach (Surface existing in surfaces) used |= 1u << existing.slot;
        int slot = 0;
        while ((used & (1u << slot)) != 0) slot++;
        surfaces.Add(new Surface { filter = filter, renderer = renderer, material = material, localBounds = bounds, slot = slot });
        return true;
    }

    internal static void Register(Transform target, Renderer[] renderers, Rigidbody body,
        BridgePhysicsManager manager = null, float mass = 1f)
    {
        if (quitting || target == null || renderers == null || renderers.Length == 0) return;
        EnsureSampler();
        foreach (Source existing in sources)
            if (existing.transform == target) return; // Idempotent activation; never double-charge an entry.
        sources.Add(new Source { transform = target, renderers = renderers, body = body,
            requiresBody = body != null, manager = manager, mass = Finite(mass) ? Mathf.Max(1f, mass) : 1f });
    }

    internal static void Unregister(Transform target)
    {
        for (int i = sources.Count - 1; i >= 0; i--)
            if (sources[i].transform == target) sources.RemoveAt(i);
    }

    internal static void UnregisterManager(BridgePhysicsManager manager)
    {
        for (int i = sources.Count - 1; i >= 0; i--)
            if (sources[i].manager == manager) sources.RemoveAt(i);
    }

    private void LateUpdate() => SampleNow(Time.deltaTime);

    internal static void SampleNow(float deltaTime)
    {
        if (!Finite(deltaTime) || deltaTime <= 0f) return;
        for (int i = impacts.Count - 1; i >= 0; i--)
        {
            Impact impact = impacts[i]; impact.age += deltaTime;
            if (impact.age >= impact.duration || impact.surface.filter == null) impacts.RemoveAt(i);
            else impacts[i] = impact;
        }
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            Source source = sources[i];
            if (source.transform == null) { sources.RemoveAt(i); continue; }
            if (!source.transform.gameObject.activeInHierarchy ||
                source.manager != null && !source.manager.IsSimulationActive ||
                source.requiresBody && (source.body == null || source.body.isKinematic) ||
                !TryBounds(source.renderers, out Bounds current))
            { source.previousValid = false; source.armedSurfaces = 0; continue; }
            bool continuous = source.previousValid && Continuous(source.previous, current, deltaTime);
            if (!continuous) source.armedSurfaces = 0;
            foreach (Surface surface in surfaces)
            {
                if (!SurfaceHeight(surface, out float height)) continue;
                uint flag = 1u << surface.slot;
                if (current.min.y > height + .3f) source.armedSurfaces |= flag;
                if (!continuous || (source.armedSurfaces & flag) == 0 ||
                    !TryGetDownwardEntry(source.previous, current, height, deltaTime, out Vector3 point, out float speed)) continue;
                Vector3 local = surface.filter.transform.InverseTransformPoint(point);
                Bounds bounds = surface.localBounds;
                if (local.x < bounds.min.x || local.x > bounds.max.x || local.z < bounds.min.z || local.z > bounds.max.z) continue;
                // One event per entry. Remaining underwater/rotating/bobbing cannot retrigger it.
                source.armedSurfaces &= ~flag;
                AddImpact(surface, point, current, speed, source.body != null ? source.body.mass : source.mass);
            }
            source.previous = current; source.previousValid = true;
        }
        foreach (Surface surface in surfaces)
        {
            if (!surface.dirty) continue;
            surface.dirty = false;
            float dipDepth = Property(surface.material, DepthId, .45f, 0f, 3f);
            if (dipDepth <= 0f) continue;
            if (!surface.helperAttempted)
            {
                surface.helperAttempted = true;
                WaterImpactSurfaceMesh.TryCreate(surface.filter, surface.renderer, out surface.helper);
            }
            if (surface.helper == null) continue; // Unsupported mesh retains the fragment-only response.
            int count = 0;
            float radius = Property(surface.material, RadiusId, 4f, .25f, 30f);
            foreach (Impact impact in impacts)
                if (impact.surface == surface)
                { rebuildCenters[count] = impact.position; rebuildRadii[count++] = radius * impact.footprintScale; }
            surface.helper.Rebuild(rebuildCenters, rebuildRadii, count, dipDepth);
        }
        PublishGlobals();
    }

    private static void AddImpact(Surface surface, Vector3 point, Bounds bounds, float speed, float mass)
    {
        if (impacts.Count == MaxImpacts) impacts.RemoveAt(0);
        float weight = Finite(mass) ? Mathf.Max(1f, mass) : 1f;
        impacts.Add(new Impact { surface = surface, position = point,
            duration = Property(surface.material, DurationId, 1.8f, .4f, 4f),
            severity = Mathf.Clamp01(.15f + .55f * Mathf.InverseLerp(1f, 15f, speed) + .3f * Mathf.Clamp01(Mathf.Sqrt(weight) / 75f)),
            footprintScale = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0f, bounds.size.x * bounds.size.z)) / 3f, .6f, 2f) });
        surface.dirty = true;
    }

    internal static bool TryGetDownwardEntry(Bounds previous, Bounds current, float surfaceHeight,
        float deltaTime, out Vector3 point, out float speed)
    {
        point = Vector3.zero; speed = 0f;
        if (!Finite(surfaceHeight) || !Continuous(previous, current, deltaTime)) return false;
        float descent = previous.min.y - current.min.y;
        speed = descent / deltaTime;
        if (speed < 1f || current.center.y > previous.center.y || previous.min.y <= surfaceHeight || current.min.y > surfaceHeight) return false;
        float fraction = Mathf.Clamp01((previous.min.y - surfaceHeight) / descent);
        point = Vector3.Lerp(previous.center, current.center, fraction); point.y = surfaceHeight;
        return Finite(point);
    }

    private static bool Continuous(Bounds previous, Bounds current, float deltaTime)
    {
        if (!Finite(deltaTime) || deltaTime <= 0f || !Finite(previous.center) || !Finite(previous.size) ||
            !Finite(current.center) || !Finite(current.size)) return false;
        float maximumStep = Mathf.Max(8f, Mathf.Max(Mathf.Max(previous.size.magnitude, current.size.magnitude), deltaTime * 60f));
        return (current.center - previous.center).sqrMagnitude <= maximumStep * maximumStep;
    }

    private static bool TryBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default; bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
            Bounds current = renderer.bounds;
            if (!Finite(current.center) || !Finite(current.size)) continue;
            if (!found) { bounds = current; found = true; } else bounds.Encapsulate(current);
        }
        return found;
    }

    private static bool Horizontal(Transform target)
    {
        Vector3 up = target.localToWorldMatrix.MultiplyVector(Vector3.up).normalized;
        return Finite(up) && Vector3.Dot(up, Vector3.up) > .9999f;
    }

    private static bool SurfaceHeight(Surface surface, out float height)
    {
        height = 0f;
        if (surface.filter == null || surface.renderer == null || surface.material == null || !surface.renderer.enabled ||
            surface.renderer.forceRenderingOff || !surface.renderer.gameObject.activeInHierarchy || !Horizontal(surface.filter.transform)) return false;
        Bounds bounds = surface.localBounds;
        height = surface.filter.transform.TransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)).y;
        return Finite(height);
    }

    private static float Property(Material material, int id, float fallback, float min, float max)
    {
        float value = material != null && material.HasProperty(id) ? material.GetFloat(id) : fallback;
        return Finite(value) ? Mathf.Clamp(value, min, max) : fallback;
    }

    private static void PublishGlobals()
    {
        int count = impacts.Count;
        for (int i = 0; i < MaxImpacts; i++)
        {
            if (i >= count) { positions[i] = data[i] = Vector4.zero; continue; }
            Impact impact = impacts[i];
            positions[i] = new Vector4(impact.position.x, impact.position.y, impact.position.z, impact.age);
            data[i] = new Vector4(impact.severity, impact.footprintScale, impact.duration, 0f);
        }
        if (count == 0 && outputCount == 0) return;
        outputCount = count;
        Shader.SetGlobalFloat(CountId, count);
        Shader.SetGlobalVectorArray(PositionId, positions);
        Shader.SetGlobalVectorArray(DataId, data);
    }

    private static void HandleSceneUnloaded(Scene scene)
    {
        for (int i = surfaces.Count - 1; i >= 0; i--)
        {
            Surface surface = surfaces[i];
            if (surface.filter != null && surface.filter.gameObject.scene != scene) continue;
            surface.helper?.Dispose(); surfaces.RemoveAt(i);
            foreach (Source source in sources) source.armedSurfaces &= ~(1u << surface.slot);
            for (int j = impacts.Count - 1; j >= 0; j--) if (impacts[j].surface == surface) impacts.RemoveAt(j);
        }
        PublishGlobals();
    }

    private static void DisposeSurfaces()
    {
        foreach (Surface surface in surfaces) surface.helper?.Dispose();
        surfaces.Clear();
    }

    private static void ClearGlobals()
    {
        outputCount = 0;
        for (int i = 0; i < MaxImpacts; i++) positions[i] = data[i] = Vector4.zero;
        Shader.SetGlobalFloat(CountId, 0f);
        Shader.SetGlobalVectorArray(PositionId, positions);
        Shader.SetGlobalVectorArray(DataId, data);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private void OnDisable() { if (sampler == this) ClearGlobals(); }
    private void OnApplicationQuit() { quitting = true; DisposeSurfaces(); ClearGlobals(); }
    private void OnDestroy()
    {
        if (sampler != this) return;
        sampler = null; sources.Clear(); impacts.Clear(); DisposeSurfaces(); ClearGlobals();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
    }
}
