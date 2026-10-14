#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Opt-in rendering check: temporary cameras only, no Play Mode or scene save.
public static class CanyonOcclusionRenderCheck
{
    const string Request = "Temp/canyon-occlusion-render-v1.request";
    const string Output = "Temp/CanyonOcclusionRender";
    static double nextCheck;

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || StaticOcclusionCulling.isRunning) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Performance/Canyon Occlusion/4. Render Fixed-Pose Comparisons")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        var report = new StringBuilder("Fixed-pose native bake rendering check\nUTC: " + DateTime.UtcNow.ToString("O") + "\n");
        Scene scene = SceneManager.GetActiveScene();
        Scene preview = default;
        RenderTexture previousTarget = RenderTexture.active;
        bool dirty = scene.isDirty;
        GameObject[] roots = scene.GetRootGameObjects();
        string sceneHash = File.Exists(scene.path) ? Convert.ToBase64String(File.ReadAllBytes(scene.path)) : null;
        try
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !StaticOcclusionCulling.isRunning &&
                SceneManager.sceneCount == 1 && scene.name == CanyonOcclusionProfile.TargetSceneName, "Open CanyonCrossing alone in idle Edit Mode.");
            CanyonOcclusionProfile profile = roots.SelectMany(root => root.GetComponentsInChildren<CanyonOcclusionProfile>(true)).Single();
            Require(profile.bakeVerified && profile.bakedData != null && CanyonOcclusionSetup.ReadPvsDataSize(profile.bakedData) > 0,
                "A verified loaded bake is required for meaningful on/off comparison.");
            Require(!profile.enableGameplayOcclusion, "Keep the saved master off during the detached check.");
            Camera[] sourceCameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
            string[] originals = sourceCameras.Select(EditorJsonUtility.ToJson).ToArray();
            preview = EditorSceneManager.NewPreviewScene();
            Camera game = NewCamera(profile.gameplayCamera, scene, preview);
            Camera mapSource = sourceCameras.FirstOrDefault(camera => camera.GetComponent<MinimapFollow>() != null);
            Require(mapSource != null, "No authored map camera found.");
            Camera map = NewCamera(mapSource, scene, preview);
            map.useOcclusionCulling = false;
            // The authored map camera may still be at its pre-Play spawn pose.
            // Frame the baked district explicitly for a useful expanded-map check.
            Bounds district = profile.approvedOccluders[0].bounds;
            foreach (MeshRenderer renderer in profile.approvedOccluders) district.Encapsulate(renderer.bounds);
            map.orthographic = true;
            map.orthographicSize = Mathf.Max(district.extents.z, district.extents.x / map.aspect) + 40f;
            map.transform.SetPositionAndRotation(new Vector3(district.center.x, district.max.y + 50f, district.center.z), Quaternion.Euler(90f, 0f, 0f));
            map.farClipPlane = Mathf.Max(map.farClipPlane, district.size.y + 150f);
            Compare(game, map, "authored-player", profile, report);
            foreach (MeshRenderer renderer in profile.approvedOccluders.Where(renderer => renderer.name == "House" || renderer.name == "CanyonCliff_8(Clone)"))
            {
                Bounds bounds = renderer.bounds;
                float height = renderer.name == "House" ? bounds.min.y + 2.5f : bounds.max.y + 8f;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 position = new Vector3(bounds.center.x, height, bounds.center.z + side * (bounds.extents.z + 10f));
                    float targetHeight = renderer.name == "House" ? height : bounds.center.y;
                    game.transform.SetPositionAndRotation(position, Quaternion.LookRotation(new Vector3(bounds.center.x, targetHeight, bounds.center.z) - position));
                    Compare(game, map, renderer.name.Replace("(Clone)", "") + "-" + side, profile, report);
                }
            }
            map.orthographicSize *= .4f;
            map.transform.position = new Vector3(game.transform.position.x, map.transform.position.y, game.transform.position.z);
            Compare(game, map, "zoomed-map", profile, report);
            for (int index = 0; index < sourceCameras.Length; index++)
                Require(EditorJsonUtility.ToJson(sourceCameras[index]) == originals[index], "A source camera changed during capture.");
            report.AppendLine("PASS: all authored cameras unchanged. Saved master remains OFF.");
            report.AppendLine("LIMIT: temporary cameras test native baked rendering, not live input, gameplay transitions, map foam callbacks, or Android performance.");
        }
        catch (Exception error) { report.AppendLine("FAILED: " + error); Debug.LogException(error); }
        finally
        {
            RenderTexture.active = previousTarget;
            if (preview.IsValid())
            {
                foreach (Camera camera in preview.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)))
                {
                    RenderTexture texture = camera.targetTexture;
                    camera.targetTexture = null;
                    if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                }
                EditorSceneManager.ClosePreviewScene(preview);
            }
            if (scene.isDirty != dirty || !scene.GetRootGameObjects().SequenceEqual(roots) ||
                (sceneHash != null && Convert.ToBase64String(File.ReadAllBytes(scene.path)) != sceneHash))
                report.AppendLine("FAILED: source scene state changed during the check; no save or dirty-state reset attempted.");
            else report.AppendLine("PASS: original scene roots, dirty state, and saved file preserved.");
            File.WriteAllText(Output + "/Report.txt", report.ToString());
        }
    }

    static Camera NewCamera(Camera source, Scene scene, Scene preview)
    {
        var go = new GameObject("Temporary Occlusion Comparison") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(go, preview);
        Camera camera = go.AddComponent<Camera>();
        camera.CopyFrom(source);
        camera.enabled = false;
        camera.scene = scene;
        camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        UniversalAdditionalCameraData sourceData = source.GetComponent<UniversalAdditionalCameraData>();
        UniversalAdditionalCameraData data = go.AddComponent<UniversalAdditionalCameraData>();
        if (sourceData != null) EditorUtility.CopySerialized(sourceData, data);
        data.renderType = CameraRenderType.Base;
        data.cameraStack.Clear();
        // Same fixed target and pose for every on/off pair; no source RT touched.
        camera.targetTexture = new RenderTexture(960, 540, 24) { hideFlags = HideFlags.HideAndDontSave };
        camera.targetTexture.Create();
        camera.aspect = 960f / 540f;
        return camera;
    }

    static void Compare(Camera game, Camera map, string name, CanyonOcclusionProfile profile, StringBuilder report)
    {
        bool covered = profile.ContainsCameraPosition(game.transform.position);
        if (!covered) { report.AppendLine("SKIP " + name + ": outside configured coverage."); return; }
        game.useOcclusionCulling = false;
        Capture(game, null); // Warm the temporary render target/pipeline before comparing.
        Color32[] off = Capture(game, name + "-off");
        Color32[] mapOff = Capture(map, name + "-map-off");
        game.useOcclusionCulling = true;
        Color32[] on = Capture(game, name + "-on");
        Require(game.useOcclusionCulling, "A callback overrode the test camera's native occlusion flag.");
        Color32[] mapOn = Capture(map, name + "-map-on");
        game.useOcclusionCulling = false;
        Color32[] repeat = Capture(game, null);
        report.AppendLine(name + " position=" + game.transform.position.ToString("F3") +
            "; changed pixels on/off=" + Difference(off, on) + "; repeated-off noise=" + Difference(off, repeat) +
            "; map difference=" + Difference(mapOff, mapOn) + " / " + off.Length + ". Review PNG pairs.");
    }

    static Color32[] Capture(Camera camera, string name)
    {
        camera.Render();
        RenderTexture.active = camera.targetTexture;
        var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        try
        {
            image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            image.Apply();
            if (name != null) File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally { Object.DestroyImmediate(image); }
    }
    static int Difference(Color32[] left, Color32[] right)
    {
        int count = 0;
        for (int index = 0; index < left.Length; index++)
            if (Math.Abs(left[index].r - right[index].r) > 3 || Math.Abs(left[index].g - right[index].g) > 3 || Math.Abs(left[index].b - right[index].b) > 3) count++;
        return count;
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
