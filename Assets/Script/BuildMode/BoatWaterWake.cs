using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cosmetic boat-motion data for opted-in water shaders. Never changes boat
/// transforms, colliders, physics, or multiplayer motion/visibility packets.
/// </summary>
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class BoatWaterWake : MonoBehaviour
{
    public const int MaxShaderEmitters = 4;
    private const float FadeRate = 4f;
    private const float VelocitySmoothing = 8f;
    private static readonly int CountId = Shader.PropertyToID("_CCBoatWakeCount");
    private static readonly int PositionId = Shader.PropertyToID("_CCBoatWakePosition");
    private static readonly int MotionId = Shader.PropertyToID("_CCBoatWakeMotion");
    private static readonly Vector4[] positions = new Vector4[MaxShaderEmitters];
    private static readonly Vector4[] motions = new Vector4[MaxShaderEmitters];
    private static readonly List<Emitter> emitters = new List<Emitter>(16);
    private static BoatWaterWake sampler;
    private static bool quitting;
    private static int outputCount;

    private sealed class Emitter
    {
        public Transform transform;
        public Renderer[] renderers;
        public Renderer hull;
        public bool registered, previousValid;
        public Vector3 previous, position;
        public Vector2 velocity, direction;
        public float halfWidth, halfLength, strength;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        emitters.Clear();
        sampler = null;
        quitting = false;
        ClearGlobals();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeSceneLoad() => ClearGlobals();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AfterSceneLoad() => EnsureSampler();

    private static void EnsureSampler()
    {
        // Edit Mode regression fixtures can sample data explicitly, but must
        // never create a persistent runtime object in the authored scene.
        if (!Application.isPlaying || quitting || sampler != null) return;
        var owner = new GameObject("Boat water wakes (runtime)");
        owner.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(owner);
        sampler = owner.AddComponent<BoatWaterWake>();
    }

    internal static void Register(Transform boat, Renderer[] renderers)
    {
        if (boat == null || renderers == null || renderers.Length == 0 || quitting) return;
        EnsureSampler();
        foreach (Emitter existing in emitters)
        {
            if (existing.transform != boat) continue;
            existing.renderers = renderers;
            existing.hull = FindHullRenderer(renderers);
            if (!existing.registered)
            {
                existing.previousValid = false;
                existing.velocity = Vector2.zero;
                existing.strength = 0f;
            }
            existing.registered = true;
            return;
        }
        emitters.Add(new Emitter
        {
            transform = boat, renderers = renderers,
            hull = FindHullRenderer(renderers), registered = true
        });
    }

    internal static void Unregister(Transform boat)
    {
        if (boat == null) return;
        for (int i = emitters.Count - 1; i >= 0; i--)
        {
            Emitter emitter = emitters[i];
            if (emitter.transform != boat) continue;
            emitter.registered = false;
            emitter.previousValid = false;
            if (emitter.strength <= .001f) emitters.RemoveAt(i);
            return;
        }
    }

    private void LateUpdate() => SampleNow(Time.deltaTime);

    internal static void SampleNow(float deltaTime)
    {
        if (!Finite(deltaTime) || deltaTime <= 0f) return;
        int count = 0;
        for (int i = 0; i < MaxShaderEmitters; i++) positions[i] = motions[i] = Vector4.zero;
        for (int i = emitters.Count - 1; i >= 0; i--)
        {
            Emitter emitter = emitters[i];
            if (emitter.transform == null) emitter.registered = false;
            bool visible = emitter.registered && emitter.transform.gameObject.activeInHierarchy &&
                AnyVisible(emitter.renderers) && emitter.hull != null;
            if (!visible)
            {
                emitter.previousValid = false;
                emitter.velocity = Vector2.zero;
                emitter.strength = Mathf.Lerp(emitter.strength, 0f, 1f - Mathf.Exp(-FadeRate * deltaTime));
            }
            else
            {
                Bounds bounds = emitter.hull.bounds;
                Vector3 center = bounds.center;
                if (!Finite(center) || !Finite(bounds.extents))
                {
                    emitter.previousValid = false;
                    emitter.strength = 0f;
                    continue;
                }
                float maxHalfLength = Mathf.Max(.25f, Mathf.Max(bounds.extents.x, bounds.extents.z));
                Vector2 velocity = Vector2.zero;
                bool continuous = emitter.previousValid &&
                    TryGetHorizontalVelocity(emitter.previous, center, deltaTime, maxHalfLength, out velocity);
                emitter.previous = center;
                emitter.previousValid = true;
                if (!continuous)
                {
                    emitter.velocity = Vector2.zero;
                    emitter.strength = 0f; // First sample/teleport must not leave a map-spanning wake.
                }
                else
                {
                    emitter.velocity = Vector2.Lerp(emitter.velocity, velocity,
                        1f - Mathf.Exp(-VelocitySmoothing * deltaTime));
                    float speed = emitter.velocity.magnitude;
                    if (speed > .01f) emitter.direction = emitter.velocity / speed;
                    float target = Mathf.InverseLerp(.15f, 4f, speed);
                    emitter.strength = Mathf.Lerp(emitter.strength, target, 1f - Mathf.Exp(-FadeRate * deltaTime));
                }
                // Imported boat roots are often offset far from the hull. Use
                // actual hull bounds, not root Y or the authored travel direction.
                emitter.position = new Vector3(center.x, bounds.min.y + bounds.size.y * .12f, center.z);
                Vector2 direction = emitter.direction;
                if (direction.sqrMagnitude < .5f) direction = Vector2.up;
                emitter.halfLength = Mathf.Clamp(Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.y) * bounds.extents.z, .25f, 100f);
                emitter.halfWidth = Mathf.Clamp(Mathf.Abs(direction.y) * bounds.extents.x + Mathf.Abs(direction.x) * bounds.extents.z, .25f, 100f);
            }
            if (emitter.strength <= .001f)
            {
                emitter.strength = 0f;
                if (!emitter.registered) emitters.RemoveAt(i);
                continue;
            }
            // Emit only the four strongest boats. Fixed arrays are reused and
            // no sorting, renderer lookup, scene search or allocation runs here.
            int slot = count;
            for (int j = 0; j < count; j++)
                if (emitter.strength > positions[j].w) { slot = j; break; }
            if (slot >= MaxShaderEmitters) continue;
            int end = Mathf.Min(count, MaxShaderEmitters - 1);
            for (int j = end; j > slot; j--)
            {
                positions[j] = positions[j - 1];
                motions[j] = motions[j - 1];
            }
            positions[slot] = new Vector4(emitter.position.x, emitter.position.y, emitter.position.z, emitter.strength);
            motions[slot] = new Vector4(emitter.direction.x, emitter.direction.y, emitter.halfWidth, emitter.halfLength);
            count = Mathf.Min(count + 1, MaxShaderEmitters);
        }
        if (count == 0 && outputCount == 0) return;
        outputCount = count;
        Shader.SetGlobalFloat(CountId, count);
        Shader.SetGlobalVectorArray(PositionId, positions);
        Shader.SetGlobalVectorArray(MotionId, motions);
    }

    internal static bool TryGetHorizontalVelocity(Vector3 previous, Vector3 current, float deltaTime,
        float halfLength, out Vector2 velocity)
    {
        velocity = Vector2.zero;
        if (!Finite(previous) || !Finite(current) || !Finite(deltaTime) || !Finite(halfLength) || deltaTime <= 0f) return false;
        Vector2 displacement = new Vector2(current.x - previous.x, current.z - previous.z);
        float maximumStep = Mathf.Max(4f, Mathf.Max(halfLength * 2f, deltaTime * 25f));
        if (displacement.sqrMagnitude > maximumStep * maximumStep) return false;
        velocity = displacement / deltaTime;
        return Finite(velocity.x) && Finite(velocity.y);
    }

    private static Renderer FindHullRenderer(Renderer[] renderers)
    {
        Renderer best = null;
        int bestPriority = -1;
        float bestVolume = -1f;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            Material material = renderer.sharedMaterial;
            string materialName = material != null ? material.name : string.Empty;
            string name = renderer.name;
            int priority = Contains(name, "hull") || Contains(materialName, "hull") ? 3 :
                Contains(name, "body") ? 2 : Contains(name, "boat") ? 1 : 0;
            Vector3 size = renderer.bounds.size;
            float volume = size.x * size.y * size.z;
            if (priority < bestPriority || priority == bestPriority && volume <= bestVolume) continue;
            best = renderer;
            bestPriority = priority;
            bestVolume = volume;
        }
        return best;
    }

    private static bool AnyVisible(Renderer[] renderers)
    {
        foreach (Renderer renderer in renderers)
            if (renderer != null && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy) return true;
        return false;
    }

    private static bool Contains(string value, string part) => value.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

    private static void ClearGlobals()
    {
        outputCount = 0;
        for (int i = 0; i < MaxShaderEmitters; i++) positions[i] = motions[i] = Vector4.zero;
        Shader.SetGlobalFloat(CountId, 0f);
        Shader.SetGlobalVectorArray(PositionId, positions);
        Shader.SetGlobalVectorArray(MotionId, motions);
    }

    private void OnDisable() { if (sampler == this) ClearGlobals(); }
    private void OnApplicationQuit() { quitting = true; ClearGlobals(); }
    private void OnDestroy()
    {
        if (sampler != this) return;
        sampler = null;
        emitters.Clear();
        ClearGlobals();
    }
}
