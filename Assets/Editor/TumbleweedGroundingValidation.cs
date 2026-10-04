#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Grounding regressions in a temporary additive scene; never saves or modifies authored scenes.</summary>
[InitializeOnLoad]
public static class TumbleweedGroundingValidation
{
    private const string SessionVersion = "CivilCraft.Tumbleweed.Grounding.v2";
    private const string PrefabPath = "Assets/Elements/Canyon Crossing - MAP 1/Map 1/PROPS/tumbleweed.prefab";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(0f, 1000f, 0f);

    static TumbleweedGroundingValidation()
    {
        EditorApplication.delayCall += TryRunOnce;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryRunOnce;
        };
    }

    private static void TryRunOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(SessionVersion, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryRunOnce;
            return;
        }
        // Attempt only once per editor session, including failures. The menu permits an explicit rerun.
        SessionState.SetBool(SessionVersion, true);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Validate Tumbleweed Grounding")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene originalActiveScene = SceneManager.GetActiveScene();
        var originalScenes = new Dictionary<Scene, bool>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            originalScenes.Add(scene, scene.isDirty);
        }

        Scene temporaryScene = default;
        GameObject fixture = null;
        bool passed = false;
        try
        {
            // Default Physics queries span loaded scenes. Keep all temporary probes far above
            // the authored map and use short rays, without changing its colliders or layers.
            temporaryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            fixture = new GameObject("Tumbleweed Grounding Validation (Temporary)");
            SceneManager.MoveGameObjectToScene(fixture, temporaryScene);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Check(prefab != null, "The authored tumbleweed prefab is missing.");
            Check(typeof(TumbleweedActor).GetMethod("Tick", PrivateInstance) != null, "Missing deterministic actor Tick.");

            ValidateGroundFilters(fixture.transform);
            ValidateRollingContact(fixture.transform, prefab);
            ValidateSlopedContact(fixture.transform, prefab);
            ValidateGroundLoss(fixture.transform, prefab);
            ValidateScaledSpawnArea(fixture.transform);
            passed = true;
        }
        catch (Exception error)
        {
            Debug.LogError("[Tumbleweed Grounding] FAIL: " + error);
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                SceneManager.SetActiveScene(originalActiveScene);
            if (temporaryScene.IsValid() && temporaryScene.isLoaded)
                EditorSceneManager.CloseScene(temporaryScene, true);
            Physics.SyncTransforms();
        }

        foreach (var original in originalScenes)
        {
            if (!original.Key.IsValid() || !original.Key.isLoaded || original.Key.isDirty != original.Value)
            {
                Debug.LogError("[Tumbleweed Grounding] Authored scene state changed during validation.");
                return;
            }
        }
        if (passed)
            Debug.Log("[Tumbleweed Grounding] PASS: Default static terrain; player, water, interactable, bridge, UI, trigger, dynamic, character, steep and self filtering; pooled authored mesh contact and centered rolling at three scales on flat and sloped ground; unsupported launch and immediate recycle on missing ground, large drops and gaps during slow frames; scaled/rotated spawn area. Temporary scene removed; authored scenes preserved.");
    }

    private static void ValidateGroundFilters(Transform fixture)
    {
        Vector3 point = Origin + Vector3.forward * 20f;
        BoxCollider floor = Box(fixture, "Default Static Floor", point - Vector3.up * 0.5f, new Vector3(12f, 1f, 12f));
        Vector3 probe = point + Vector3.up * 5f;
        Physics.SyncTransforms();
        Check(TumbleweedGroundProbe.TryFindGround(probe, 10f, ~0, null, out RaycastHit hit) && hit.collider == floor,
            "A Default-layer static floor was not accepted.");

        foreach (string layerName in new[] { "Player", "Water", "Interactable", "Bridge", "UI" })
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) continue;
            BoxCollider excluded = Box(fixture, "Excluded " + layerName, point + Vector3.up, new Vector3(2f, 0.2f, 2f));
            excluded.gameObject.layer = layer;
            ExpectFloor(probe, floor, null, layerName + " geometry was mistaken for ground.");
            Object.DestroyImmediate(excluded.gameObject);
        }

        BoxCollider obstacle = Box(fixture, "Ignored Trigger", point + Vector3.up, new Vector3(2f, 0.2f, 2f));
        obstacle.isTrigger = true;
        ExpectFloor(probe, floor, null, "A trigger was mistaken for ground.");
        obstacle.isTrigger = false;
        Rigidbody body = obstacle.gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        ExpectFloor(probe, floor, null, "A dynamic body was mistaken for ground.");
        body.isKinematic = true;
        ExpectFloor(probe, floor, null, "A kinematic gameplay body was mistaken for ground.");
        Object.DestroyImmediate(body);
        ExpectFloor(probe, floor, obstacle.transform, "The ignored actor hierarchy became its own ground.");
        Object.DestroyImmediate(obstacle.gameObject);

        GameObject characterObject = Child(fixture, "Ignored Character");
        characterObject.transform.position = point + Vector3.up;
        CharacterController controller = characterObject.AddComponent<CharacterController>();
        controller.height = 1f;
        controller.radius = 0.4f;
        ExpectFloor(probe, floor, null, "A CharacterController was mistaken for ground.");
        Object.DestroyImmediate(characterObject);

        BoxCollider steep = Box(fixture, "Steep Surface", point + Vector3.up * 2f, new Vector3(8f, 0.1f, 8f));
        steep.transform.rotation = Quaternion.Euler(0f, 0f, 70f);
        ExpectFloor(probe, floor, null, "A surface steeper than the supported slope was mistaken for ground.");
        floor.enabled = false;
        Physics.SyncTransforms();
        Check(!TumbleweedGroundProbe.TryFindGround(probe, 10f, ~0, null, out _),
            "A steep surface was accepted when there was no supporting floor.");
        Object.DestroyImmediate(steep.gameObject);
        Object.DestroyImmediate(floor.gameObject);
    }

    private static void ExpectFloor(Vector3 origin, Collider floor, Transform ignoreRoot, string reason)
    {
        Physics.SyncTransforms();
        Check(TumbleweedGroundProbe.TryFindGround(origin, 10f, ~0, ignoreRoot, out RaycastHit hit) && hit.collider == floor, reason);
    }

    private static void ValidateRollingContact(Transform fixture, GameObject prefab)
    {
        BoxCollider floor = Box(fixture, "Rolling Floor", Origin - Vector3.up * 0.5f, new Vector3(30f, 1f, 30f));
        Physics.SyncTransforms();
        TumbleweedActor actor = Actor(fixture, prefab);
        foreach (float scale in new[] { 0.5f, 1f, 1.5f })
        {
            Launch(actor, Origin, scale, 0.2f);
            Vector3 localCenter = (Vector3)typeof(TumbleweedActor).GetField("localVisualCenter", PrivateInstance).GetValue(actor);
            Vector3 expectedCenter = actor.transform.TransformPoint(localCenter);
            AssertContact(actor, Origin.y + 0.02f);
            for (int frame = 0; frame < 240; frame++)
            {
                Tick(actor, 0.01f);
                expectedCenter.x += 0.02f;
                Check(actor.gameObject.activeSelf, "A supported tumbleweed recycled during flat-floor rolling.");
                AssertContact(actor, Origin.y + 0.02f);
                Vector3 center = actor.transform.TransformPoint(localCenter);
                Check(Mathf.Abs(center.x - expectedCenter.x) < 0.001f && Mathf.Abs(center.z - expectedCenter.z) < 0.001f,
                    "The mesh center orbited away from its horizontal travel path during rolling.");
            }
            // Relaunch the same pooled instance at another scale after a full roll.
            actor.gameObject.SetActive(false);
        }
        Object.DestroyImmediate(actor.gameObject);
        Object.DestroyImmediate(floor.gameObject);
    }

    private static void ValidateGroundLoss(Transform fixture, GameObject prefab)
    {
        BoxCollider floor = Box(fixture, "Ground Loss Floor", Origin - Vector3.up * 0.5f, new Vector3(10f, 1f, 10f));
        Physics.SyncTransforms();
        TumbleweedActor actor = Actor(fixture, prefab);
        Launch(actor, Origin, 1f, 0f);
        floor.enabled = false;
        Physics.SyncTransforms();
        Tick(actor, 0.01f);
        Check(!actor.gameObject.activeSelf, "A tumbleweed continued flying after its support disappeared.");
        Launch(actor, Origin, 1f, 0f);
        Check(!actor.gameObject.activeSelf, "An unsupported launch activated a floating tumbleweed.");
        Object.DestroyImmediate(actor.gameObject);

        floor.enabled = true;
        Physics.SyncTransforms();
        actor = Actor(fixture, prefab);
        Launch(actor, Origin, 1f, 0f);
        floor.transform.position -= Vector3.up * 4f;
        Physics.SyncTransforms();
        Tick(actor, 0.01f);
        Check(!actor.gameObject.activeSelf, "A tumbleweed hovered down a large terrain drop instead of recycling.");
        Object.DestroyImmediate(actor.gameObject);

        floor.transform.position = Origin - Vector3.up * 0.5f;
        floor.size = new Vector3(0.4f, 1f, 10f);
        Physics.SyncTransforms();
        actor = Actor(fixture, prefab);
        Launch(actor, Origin, 1f, 0f);
        Tick(actor, 0.25f);
        Check(!actor.gameObject.activeSelf, "A slow frame carried a tumbleweed across a ground gap.");
        Vector3 localCenter = (Vector3)typeof(TumbleweedActor).GetField("localVisualCenter", PrivateInstance).GetValue(actor);
        Check(actor.transform.TransformPoint(localCenter).x <= 0.201f,
            "An unsupported slow-frame substep moved the tumbleweed beyond the last supporting ground.");
        Object.DestroyImmediate(actor.gameObject);
        Object.DestroyImmediate(floor.gameObject);
    }

    private static void ValidateSlopedContact(Transform fixture, GameObject prefab)
    {
        Vector3 point = Origin + Vector3.forward * 40f;
        Quaternion rotation = Quaternion.Euler(0f, 0f, 30f);
        Vector3 normal = rotation * Vector3.up;
        BoxCollider floor = Box(fixture, "Continuous Sloped Floor", point - normal * 0.05f, new Vector3(20f, 0.1f, 20f));
        floor.transform.rotation = rotation;
        Physics.SyncTransforms();
        foreach (float scale in new[] { 0.5f, 1f, 1.5f })
        {
            TumbleweedActor actor = Actor(fixture, prefab);
            Launch(actor, point, scale, 0f);
            for (int frame = 0; frame < 90; frame++)
            {
                Tick(actor, 0.01f);
                Check(actor.gameObject.activeSelf, "A tumbleweed could not follow a continuous supported slope.");
                float distance = MinimumMeshDistance(actor, point, normal);
                Check(Mathf.Abs(distance) < 0.001f,
                    "The actual rolling mesh hovered over or intersected its supporting slope: distance=" + distance);
            }
            Object.DestroyImmediate(actor.gameObject);
        }
        Object.DestroyImmediate(floor.gameObject);
    }

    private static void ValidateScaledSpawnArea(Transform fixture)
    {
        Vector3 point = Origin + Vector3.right * 60f;
        BoxCollider floor = Box(fixture, "Area Floor", point - Vector3.up * 0.5f, new Vector3(80f, 1f, 80f));
        GameObject areaObject = Child(fixture, "Scaled and Rotated Area");
        areaObject.transform.position = point;
        areaObject.transform.localScale = new Vector3(2f, 3f, 1.5f);
        areaObject.transform.rotation = Quaternion.Euler(8f, 27f, 6f);
        TumbleweedSpawnArea area = areaObject.AddComponent<TumbleweedSpawnArea>();
        GameObject cameraObject = Child(fixture, "Area Visibility Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.aspect = 1.5f;
        camera.farClipPlane = 200f;
        camera.transform.position = point + new Vector3(0f, 60f, -40f);
        camera.transform.LookAt(point);
        Physics.SyncTransforms();
        Check(area.TryGetVisibleGroundPoint(camera, ~0, 3f, 8f, 0f, 100, out Vector3 ground) &&
              Mathf.Abs(ground.y - point.y) < 0.001f,
            "A scaled and rotated authored volume could not find its visible ground.");
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(areaObject);
        Object.DestroyImmediate(floor.gameObject);
    }

    private static TumbleweedActor Actor(Transform fixture, GameObject prefab)
    {
        GameObject instance = Object.Instantiate(prefab, fixture);
        instance.name = "Authored Tumbleweed Regression";
        TumbleweedActor actor = instance.GetComponent<TumbleweedActor>();
        if (actor == null) actor = instance.AddComponent<TumbleweedActor>();
        actor.enabled = false;
        instance.SetActive(false);
        return actor;
    }

    private static void Launch(TumbleweedActor actor, Vector3 point, float scale, float clearance)
    {
        typeof(TumbleweedActor).GetMethod("Launch", PrivateInstance).Invoke(actor, new object[]
        {
            null, null, null, point, Vector3.right, (LayerMask)(~0),
            2f, 240f, scale, 10000f, clearance, 3f, 8f, 8f, 1f, 0f
        });
        typeof(TumbleweedActor).GetField("nextVisibilityCheck", PrivateInstance).SetValue(actor, float.PositiveInfinity);
        typeof(TumbleweedActor).GetField("recycleAt", PrivateInstance).SetValue(actor, float.PositiveInfinity);
    }

    private static void Tick(TumbleweedActor actor, float delta)
    {
        typeof(TumbleweedActor).GetMethod("Tick", PrivateInstance).Invoke(actor, new object[] { delta });
    }

    private static void AssertContact(TumbleweedActor actor, float expectedY)
    {
        float distance = MinimumMeshDistance(actor, new Vector3(0f, expectedY, 0f), Vector3.up);
        Check(Mathf.Abs(distance) < 0.001f,
            "The actual scaled/rolling mesh lost visual ground contact: distance=" + distance);
    }

    private static float MinimumMeshDistance(TumbleweedActor actor, Vector3 point, Vector3 normal)
    {
        MeshFilter[] filters = actor.GetComponentsInChildren<MeshFilter>(true);
        float minimum = float.PositiveInfinity;
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            Check(mesh.isReadable, "The authored grounding mesh does not expose its actual support vertices.");
            foreach (Vector3 vertex in mesh.vertices)
                minimum = Mathf.Min(minimum, Vector3.Dot(filter.transform.TransformPoint(vertex) - point, normal));
        }
        Check(minimum < float.PositiveInfinity, "The authored prefab has no testable mesh geometry.");
        return minimum;
    }

    private static BoxCollider Box(Transform fixture, string name, Vector3 position, Vector3 size)
    {
        GameObject instance = Child(fixture, name);
        instance.transform.position = position;
        BoxCollider collider = instance.AddComponent<BoxCollider>();
        collider.size = size;
        return collider;
    }

    private static GameObject Child(Transform fixture, string name)
    {
        GameObject instance = new GameObject(name);
        instance.transform.SetParent(fixture, false);
        return instance;
    }

    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }
}
#endif
