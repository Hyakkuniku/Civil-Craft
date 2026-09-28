using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Moves only visual-only generated props to a dedicated layer in mobile players.
/// Cameras can then distance-cull that layer without disabling gameplay objects,
/// colliders, scripts, or any of the bridge/vehicle renderers.
/// </summary>
public sealed class MobileSceneryCullingRuntime : MonoBehaviour
{
    private const string SettingsResourceName = "MobileSceneryCullingSettings";
    private const string SceneryLayerName = "MobileScenery";

    private static MobileSceneryCullingRuntime instance;

    private readonly HashSet<int> processingScenes = new HashSet<int>();
    private readonly HashSet<int> processedScenes = new HashSet<int>();
    private readonly List<Component> components = new List<Component>(4);

    private MobileSceneryCullingSettings settings;
    private int sceneryLayer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance != null) return;

        MobileSceneryCullingSettings profile =
            Resources.Load<MobileSceneryCullingSettings>(SettingsResourceName);
        if (profile == null || !profile.enableCulling) return;

#if UNITY_EDITOR
        if (!profile.previewInEditor) return;
#else
        if (!Application.isMobilePlatform) return;
#endif

        new GameObject("Mobile Scenery Culling").AddComponent<MobileSceneryCullingRuntime>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        settings = Resources.Load<MobileSceneryCullingSettings>(SettingsResourceName);
        sceneryLayer = LayerMask.NameToLayer(SceneryLayerName);
        if (settings == null || sceneryLayer < 0)
        {
            Debug.LogError("[MobileSceneryCulling] Settings or MobileScenery layer is missing.", this);
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;

        for (int index = 0; index < SceneManager.sceneCount; index++)
            ScheduleScene(SceneManager.GetSceneAt(index));
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ScheduleScene(scene);
    }

    private void OnSceneUnloaded(Scene scene)
    {
        processingScenes.Remove(scene.handle);
        processedScenes.Remove(scene.handle);
    }

    private void ScheduleScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded ||
            processedScenes.Contains(scene.handle) || !processingScenes.Add(scene.handle))
            return;

        StartCoroutine(ConfigureScene(scene));
    }

    private IEnumerator ConfigureScene(Scene scene)
    {
        if (!string.IsNullOrEmpty(settings.targetSceneName) &&
            scene.name != settings.targetSceneName)
        {
            processingScenes.Remove(scene.handle);
            processedScenes.Add(scene.handle);
            yield break;
        }

        // Canyon Crossing's generated props are nested under a map root, not
        // direct Scene roots. Prefer the active object lookup; the fallback
        // supports a temporarily inactive root or additive Scenes with the
        // same hierarchy name. Neither lookup runs every frame.
        GameObject activeRoot = GameObject.Find(settings.generatedPropsRootName);
        Transform generatedProps = activeRoot != null && activeRoot.scene == scene
            ? activeRoot.transform : null;
        if (generatedProps == null)
        {
            Transform[] transforms = FindObjectsOfType<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform candidate = transforms[index];
                if (candidate.gameObject.scene == scene &&
                    candidate.name == settings.generatedPropsRootName)
                {
                    generatedProps = candidate;
                    break;
                }
            }
        }

        if (generatedProps != null)
        {
            // Preserve visibility in capture, map, and magnifier cameras before
            // changing any renderer's layer. Only gameplay cameras get a distance.
            ConfigureCamerasAndLights();

            // Scene-owned Start methods may create more decorative children.
            yield return null;
            if (!scene.isLoaded || generatedProps == null)
            {
                processingScenes.Remove(scene.handle);
                yield break;
            }

            MeshRenderer[] renderers = generatedProps.GetComponentsInChildren<MeshRenderer>(true);
            int renderersPerFrame = Mathf.Max(1, settings.renderersPerFrame);
            var propBounds = new Dictionary<Transform, Bounds>();
            var excludedProps = new HashSet<Transform>();

            // Decide eligibility for each whole prop, not each sub-mesh. A
            // multi-mesh cactus or rock must never disappear in pieces.
            for (int index = 0; index < renderers.Length; index++)
            {
                if (!scene.isLoaded || generatedProps == null)
                {
                    processingScenes.Remove(scene.handle);
                    yield break;
                }

                MeshRenderer renderer = renderers[index];
                if (renderer != null)
                {
                    Transform propRoot = GetPropRoot(renderer.transform, generatedProps);
                    if (propRoot != null)
                    {
                        if (!IsVisualOnlyRenderer(renderer))
                            excludedProps.Add(propRoot);
                        else if (propBounds.TryGetValue(propRoot, out Bounds bounds))
                        {
                            bounds.Encapsulate(renderer.bounds);
                            propBounds[propRoot] = bounds;
                        }
                        else
                            propBounds.Add(propRoot, renderer.bounds);
                    }
                }

                if ((index + 1) % renderersPerFrame == 0)
                    yield return null;
            }

            int changed = 0;
            for (int index = 0; index < renderers.Length; index++)
            {
                if (!scene.isLoaded || generatedProps == null)
                {
                    processingScenes.Remove(scene.handle);
                    yield break;
                }

                MeshRenderer renderer = renderers[index];
                if (renderer != null)
                {
                    Transform propRoot = GetPropRoot(renderer.transform, generatedProps);
                    if (propRoot != null && !excludedProps.Contains(propRoot) &&
                        propBounds.TryGetValue(propRoot, out Bounds bounds))
                    {
                        Vector3 size = bounds.size;
                        if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) <= settings.maximumPropSize)
                        {
                            renderer.gameObject.layer = sceneryLayer;
                            changed++;
                        }
                    }
                }

                if ((index + 1) % renderersPerFrame == 0)
                    yield return null;
            }

            // Covers cameras created by scene Start methods without doing any
            // camera search or distance calculation every frame.
            ConfigureCamerasAndLights();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"[MobileSceneryCulling] Prepared {changed} decorative renderers in '{scene.name}'.", this);
#endif
        }

        processingScenes.Remove(scene.handle);
        processedScenes.Add(scene.handle);
    }

    private static Transform GetPropRoot(Transform rendererTransform, Transform generatedProps)
    {
        Transform current = rendererTransform;
        while (current != null && current.parent != generatedProps)
            current = current.parent;
        return current;
    }

    private bool IsVisualOnlyRenderer(MeshRenderer renderer)
    {
        GameObject prop = renderer.gameObject;
        if (prop.layer != 0 || prop.isStatic) return false;

        components.Clear();
        prop.GetComponents(components);
        if (components.Count != 3) return false;

        bool hasTransform = false;
        bool hasMeshFilter = false;
        bool hasMeshRenderer = false;
        for (int index = 0; index < components.Count; index++)
        {
            Component component = components[index];
            if (component is Transform) hasTransform = true;
            else if (component is MeshFilter) hasMeshFilter = true;
            else if (component is MeshRenderer) hasMeshRenderer = true;
            else return false;
        }

        return hasTransform && hasMeshFilter && hasMeshRenderer;
    }

    private void ConfigureCamerasAndLights()
    {
        int sceneryMask = 1 << sceneryLayer;
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        foreach (Camera camera in cameras)
        {
            // These cameras previously rendered Default-layer props. Keep that
            // behavior after moving the props to the dedicated scenery layer.
            if ((camera.cullingMask & 1) == 0) continue;
            camera.cullingMask |= sceneryMask;

            if (camera.name == settings.overworldCameraName)
                SetCullDistance(camera, settings.overworldDistance);
            else if (camera.name == settings.buildCameraName)
                SetCullDistance(camera, settings.buildDistance);
        }

        Light[] lights = FindObjectsOfType<Light>(true);
        foreach (Light light in lights)
        {
            if ((light.cullingMask & 1) != 0)
                light.cullingMask |= sceneryMask;
        }
    }

    private void SetCullDistance(Camera camera, float distance)
    {
        if (distance <= 0f) return;

        float[] distances = camera.layerCullDistances;
        if (distances == null || distances.Length != 32)
            distances = new float[32];

        distances[sceneryLayer] = distance;
        camera.layerCullDistances = distances;
        camera.layerCullSpherical = true;
    }
}
