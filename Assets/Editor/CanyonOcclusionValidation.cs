#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Detached native geometry/policy checks. No GameManager activation, player/account data, real camera movement or scene save.</summary>
public static class CanyonOcclusionValidation
{
    public const string RequestPath = "Temp/canyon-occlusion-native-validation-v1.request";
    public const string ReportPath = "Temp/CanyonOcclusionValidation.txt";
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool running;
    private static double nextCheck;
    private static bool Idle => !running && !StaticOcclusionCulling.isRunning && !EditorApplication.isPlayingOrWillChangePlaymode &&
        !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (!Idle || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Performance/Canyon Occlusion/Validate Detached Native Policies")]
    public static void Validate()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            Check(Idle, "Run in idle Edit Mode, without another bake or Prefab Mode.");
            File.WriteAllText(ReportPath, Run());
            Debug.Log("[Canyon Occlusion] Detached native checks passed: " + ReportPath);
        }
        catch (Exception error)
        {
            File.WriteAllText(ReportPath, "FAIL\nUTC: " + DateTime.UtcNow.ToString("O") + "\n" + error);
            Debug.LogException(error);
        }
    }

    private static string Run()
    {
        running = true;
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        var scenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            scenes.Add(scene, Tuple.Create(scene.isDirty, scene.GetRootGameObjects()));
        }
        GameManager managerBefore = GameManager.Instance;
        FieldInfo registryField = Field(typeof(CanyonCameraOcclusionPolicy), "policies", StaticFlags);
        var registry = (List<CanyonCameraOcclusionPolicy>)registryField.GetValue(null);
        CanyonCameraOcclusionPolicy[] registeredBefore = registry.ToArray();
        FieldInfo cameraCallback = Field(typeof(Camera), "onPreCull", StaticFlags);
        FieldInfo pipelineCallback = Field(typeof(RenderPipelineManager), "beginCameraRendering", StaticFlags);
        object cameraBefore = cameraCallback.GetValue(null), pipelineBefore = pipelineCallback.GetValue(null);
        Scene preview = default;
        RenderTexture target = null;
        var report = new StringBuilder("PASS: detached native occlusion coverage, fail-open policies, owner suspension cleanup and camera preservation.\nUTC: " + DateTime.UtcNow.ToString("O") + "\n");
        try
        {
            preview = EditorSceneManager.NewPreviewScene();
            GameObject root = NewObject("Detached Occlusion Profile", preview);
            CanyonOcclusionProfile profile = root.AddComponent<CanyonOcclusionProfile>();
            Check(!profile.enableGameplayOcclusion && !profile.bakeVerified && profile.bakedData == null,
                "A new profile must default to unverified/off.");
            ValidateCoverage(preview, profile, report);
            target = new RenderTexture(16, 16, 16) { hideFlags = HideFlags.HideAndDontSave };
            ValidatePolicy(preview, profile, target, registry, report);
            ValidateCinematicCancellation(preview, profile, report);
        }
        finally
        {
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (target != null) Object.DestroyImmediate(target);
            // Preserve any pre-existing runtime entries and render callbacks exactly.
            registry.Clear();
            registry.AddRange(registeredBefore);
            cameraCallback.SetValue(null, cameraBefore);
            pipelineCallback.SetValue(null, pipelineBefore);
            Selection.objects = selection;
            Selection.activeObject = selected;
            running = false;
        }
        foreach (var pair in scenes)
            Check(pair.Key.IsValid() && pair.Key.isLoaded && pair.Key.isDirty == pair.Value.Item1 &&
                pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Item2), "Detached validation changed a loaded source scene.");
        Check(SceneManager.GetActiveScene() == active, "Detached validation changed the active scene.");
        Check(GameManager.Instance == managerBefore, "Detached validation changed GameManager ownership.");
        Check(Selection.objects.SequenceEqual(selection) && Selection.activeObject == selected, "Detached validation changed Editor selection.");
        Check(registry.SequenceEqual(registeredBefore) && Equals(cameraCallback.GetValue(null), cameraBefore) &&
            Equals(pipelineCallback.GetValue(null), pipelineBefore), "Native policy registry/render callbacks were not restored.");
        report.AppendLine("PASS: source scene roots/dirtiness, selection, GameManager singleton, camera policy registry and render callbacks preserved.");
        report.AppendLine("NOTE: No live player camera, GameManager lifecycle, Play Mode, progression, player/account save, network, scene save or actual bake ran.");
        report.AppendLine("NOTE: Full eligible-on/state logic is covered by the standalone policy tests; Android performance and actual-bake visual parity require separate checks.");
        return report.ToString();
    }

    private static void ValidateCoverage(Scene preview, CanyonOcclusionProfile profile, StringBuilder report)
    {
        GameObject owner = NewObject("Rotated Scaled Detached View Volume", preview);
        owner.transform.SetPositionAndRotation(new Vector3(15f, -3f, 24f), Quaternion.Euler(17f, 33f, -11f));
        owner.transform.localScale = new Vector3(2f, .5f, 3f);
        OcclusionArea area = owner.AddComponent<OcclusionArea>();
        area.center = new Vector3(3f, -2f, 1f);
        area.size = new Vector3(20f, 12f, 30f);
        profile.viewVolumes = new[] { area };
        Check(profile.ContainsCameraPosition(owner.transform.TransformPoint(area.center)), "Area center is not covered.");
        Check(profile.ContainsCameraPosition(owner.transform.TransformPoint(area.center + new Vector3(9f, 5f, 14f))),
            "Interior position is not covered after rotation/nonuniform scale.");
        Check(!profile.ContainsCameraPosition(owner.transform.TransformPoint(area.center + new Vector3(10.2f, 0f, 0f))) &&
            !profile.ContainsCameraPosition(owner.transform.TransformPoint(area.center + new Vector3(0f, 6.2f, 0f))) &&
            !profile.ContainsCameraPosition(owner.transform.TransformPoint(area.center + new Vector3(0f, 0f, 15.2f))),
            "Position outside a transformed area remained covered.");
        Check(!profile.ContainsCameraPosition(new Vector3(float.NaN, 0f, 0f)) &&
            !profile.ContainsCameraPosition(new Vector3(0f, float.PositiveInfinity, 0f)), "Non-finite camera position was accepted.");
        Vector3 center = owner.transform.TransformPoint(area.center);
        owner.SetActive(false);
        Check(!profile.ContainsCameraPosition(center), "Inactive view volume still provides coverage.");
        owner.SetActive(true);
        Vector3 size = area.size;
        area.size = Vector3.zero;
        Check(!profile.ContainsCameraPosition(center), "Zero-sized area provides coverage.");
        area.size = new Vector3(-1f, 12f, 30f);
        Check(!profile.ContainsCameraPosition(center), "Negative-sized area provides coverage.");
        area.size = size;
        profile.viewVolumes = new OcclusionArea[] { null };
        Check(!profile.ContainsCameraPosition(center), "Missing area provides coverage.");
        profile.viewVolumes = new[] { area };
        report.AppendLine("PASS: native rotated/nonuniformly scaled area center/interior/exterior; inactive, missing, zero/negative size and non-finite camera positions fail open.");
    }

    private static void ValidatePolicy(Scene preview, CanyonOcclusionProfile profile, RenderTexture target,
        List<CanyonCameraOcclusionPolicy> registry, StringBuilder report)
    {
        GameObject cameraObject = NewObject("Detached Map Camera", preview);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 27f;
        camera.cullingMask = 0x24681;
        camera.layerCullSpherical = true;
        float[] distances = Enumerable.Range(0, 32).Select(index => index * 3.5f).ToArray();
        camera.layerCullDistances = distances;
        camera.targetTexture = target;
        camera.rect = new Rect(.1f, .2f, .6f, .7f);
        bool active = cameraObject.activeSelf;
        Rect viewport = camera.rect;
        CanyonCameraOcclusionPolicy policy = cameraObject.AddComponent<CanyonCameraOcclusionPolicy>();
        Invoke(policy, "OnEnable");
        Check(registry.Contains(policy), "Enabled policy was not registered for pre-render enforcement.");
        camera.useOcclusionCulling = true;
        policy.Configure(profile, CanyonCameraOcclusionPolicy.CameraRole.Excluded);
        Check(!camera.useOcclusionCulling, "Explicitly excluded map camera uses occlusion.");
        camera.useOcclusionCulling = true;
        policy.RefreshPolicy();
        Check(!camera.useOcclusionCulling, "Excluded role did not correct an external toggle.");
        profile.gameplayCamera = camera;
        policy.Configure(profile, CanyonCameraOcclusionPolicy.CameraRole.Gameplay);
        Check(!camera.useOcclusionCulling, "Disabled/missing-bake/default-off gameplay camera did not fail open.");
        profile.enableGameplayOcclusion = true;
        profile.bakeVerified = true;
        policy.RefreshPolicy();
        Check(!camera.useOcclusionCulling && profile.bakedData == null, "Missing native data permits gameplay occlusion.");
        profile.enableGameplayOcclusion = false;
        profile.bakeVerified = false;
        policy.Configure(null, CanyonCameraOcclusionPolicy.CameraRole.Gameplay);
        Check(!camera.useOcclusionCulling, "Missing profile permits gameplay occlusion.");
        policy.Configure(profile, CanyonCameraOcclusionPolicy.CameraRole.Excluded);
        GameObject ownerA = NewObject("Detached Recording Owner", preview);
        GameObject ownerB = NewObject("Detached Cinematic Owner", preview);
        var owners = (HashSet<Object>)Field(typeof(CanyonCameraOcclusionPolicy), "suspensionOwners", InstanceFlags).GetValue(policy);
        policy.SetSuspended(ownerA, true);
        policy.SetSuspended(ownerA, true);
        policy.SetSuspended(ownerB, true);
        Check(owners.Count == 2, "Duplicate/nested owners were not isolated.");
        policy.SetSuspended(ownerA, false);
        Check(owners.Count == 1 && owners.Contains(ownerB), "One owner released another owner's suspension.");
        policy.SetSuspended(null, true);
        Check(owners.Count == 1, "Null owner adds a permanent suspension.");
        Object.DestroyImmediate(ownerB);
        policy.RefreshPolicy();
        Check(owners.Count == 0, "Destroyed owner retains a permanent suspension.");
        policy.SetSuspended(ownerA, false);
        Check(owners.Count == 0, "Releasing an absent owner changed suspension state.");
        Check(cameraObject.activeSelf == active && !camera.enabled && camera.orthographic && camera.orthographicSize == 27f &&
            camera.cullingMask == 0x24681 && camera.layerCullSpherical && camera.layerCullDistances.SequenceEqual(distances) &&
            camera.targetTexture == target && camera.rect == viewport, "Policy changed activation, projection, masks, distances or render target.");
        camera.useOcclusionCulling = true;
        Invoke(policy, "OnDisable");
        Check(!camera.useOcclusionCulling && !registry.Contains(policy), "Disabled policy retained occlusion or registry membership.");
        Invoke(policy, "OnEnable");
        camera.useOcclusionCulling = true;
        Invoke(policy, "OnDestroy");
        Check(!camera.useOcclusionCulling && !registry.Contains(policy) && camera != null,
            "Removed policy re-enabled unguarded occlusion on a surviving camera.");
        Object.DestroyImmediate(policy);
        Check(camera != null && !camera.useOcclusionCulling, "Actual component removal left the camera occluded.");
        report.AppendLine("PASS: Excluded/default-off/missing-profile/missing-bake native fail-open policies; camera activation/masks/distances/projection/RT unchanged.");
        report.AppendLine("PASS: duplicate/nested/null suspension owners; independent release, destroyed-owner cleanup, repeated release; disable/destroy fail open with surviving camera.");
    }

    private static void ValidateCinematicCancellation(Scene preview, CanyonOcclusionProfile profile, StringBuilder report)
    {
        GameObject parent = NewObject("Detached Camera Return Parent", preview);
        parent.transform.SetPositionAndRotation(new Vector3(3f, 5f, 7f), Quaternion.Euler(0f, 35f, 0f));
        GameObject cameraObject = NewObject("Detached Cinematic Camera", preview);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        CanyonCameraOcclusionPolicy policy = cameraObject.AddComponent<CanyonCameraOcclusionPolicy>();
        policy.Configure(profile, CanyonCameraOcclusionPolicy.CameraRole.Excluded);
        GameObject owner = NewObject("Detached Disabled Director", preview);
        owner.SetActive(false); // Never start a shot, input lookup, reward, or save.
        CinematicDirector director = owner.AddComponent<CinematicDirector>();
        Vector3 returnPosition = new Vector3(1f, 2f, 3f);
        Quaternion returnRotation = Quaternion.Euler(10f, 20f, 0f);
        Field(typeof(CinematicDirector), "capturedShotCamera", InstanceFlags).SetValue(director, camera);
        Field(typeof(CinematicDirector), "occlusionSuspendedCamera", InstanceFlags).SetValue(director, camera);
        Field(typeof(CinematicDirector), "originalCamParent", InstanceFlags).SetValue(director, parent.transform);
        Field(typeof(CinematicDirector), "originalCamLocalPos", InstanceFlags).SetValue(director, returnPosition);
        Field(typeof(CinematicDirector), "originalCamLocalRot", InstanceFlags).SetValue(director, returnRotation);
        policy.SetSuspended(director, true);
        cameraObject.transform.position = new Vector3(300f, 100f, 200f);
        Invoke(director, "OnDisable");
        var owners = (HashSet<Object>)Field(typeof(CanyonCameraOcclusionPolicy), "suspensionOwners", InstanceFlags).GetValue(policy);
        Check(cameraObject.transform.parent == parent.transform && cameraObject.transform.localPosition == returnPosition &&
            Quaternion.Angle(cameraObject.transform.localRotation, returnRotation) < .001f && owners.Count == 0,
            "Cinematic cancellation did not restore the captured camera pose and release its owner.");
        Invoke(director, "OnDisable");
        Check(owners.Count == 0 && cameraObject.transform.localPosition == returnPosition, "Repeated cancellation changed the return pose.");
        report.AppendLine("PASS: detached native cinematic cancellation restores captured parent/local pose, releases suspension, and tolerates repeated disable; no player controls or saves touched.");
    }

    private static GameObject NewObject(string name, Scene scene)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    private static FieldInfo Field(Type type, string name, BindingFlags flags)
    {
        FieldInfo field = type.GetField(name, flags);
        Check(field != null, "Missing expected native/test field: " + type.Name + "." + name);
        return field;
    }

    private static void Invoke(object target, string method)
    {
        MethodInfo found = target.GetType().GetMethod(method, InstanceFlags);
        Check(found != null, "Missing policy lifecycle method: " + method);
        found.Invoke(target, null);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
