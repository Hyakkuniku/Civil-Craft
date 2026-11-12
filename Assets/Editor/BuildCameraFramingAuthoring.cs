#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors the site's existing build cameras and validates their full construction view.</summary>
[InitializeOnLoad]
public static class BuildCameraFramingAuthoring
{
    private const string Request = "Temp/build-camera-framing.request";
    private const string BrooksRequest = "Temp/brooks-grid-depth.request";
    public const string BrooksReport = "Logs/brooks-grid-depth.txt";
    public const string Report = "Logs/build-camera-framing.txt";
    static BuildCameraFramingAuthoring() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating) return;
        if (File.Exists(BrooksRequest))
        {
            File.Delete(BrooksRequest);
            try { RepairBrooksGrid(); }
            catch (Exception exception) { Directory.CreateDirectory("Logs"); File.WriteAllText(BrooksReport, "FAILED\n" + exception); Debug.LogException(exception); }
        }
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception exception) { File.WriteAllText(Report, "FAILED\n" + exception); Debug.LogException(exception); }
    }

    [MenuItem("Tools/Civil Craft/Build/Repair Brooks Grid Distance (120)")]
    public static void RepairBrooksGrid()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before repairing Brooks's grid.");
        BuildLocation[] matches = LoadedSites().Where(site => site.name == "workbenchbrooks" &&
            site.gameObject.scene.path == "Assets/Scenes/CanyonCrossing.unity").ToArray();
        Require(matches.Length == 1, "Open CanyonCrossing with its single workbenchbrooks before repairing the grid.");
        BuildLocation site = matches[0];
        Canvas canvas = site.gridImage != null ? site.gridImage.GetComponentInParent<Canvas>(true) : null;
        Require(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera &&
            canvas.worldCamera == site.locationCamera, "Brooks's screen-space grid must reference its own build camera.");
        Require(BuildCameraFraming.TryGetBounds(site, out Bounds bounds), "Brooks's construction bounds are missing.");
        Scene scene = site.gameObject.scene;
        bool wasDirty = scene.isDirty;
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Repair Brooks grid distance");
        Undo.RecordObjects(new Object[] { canvas, site.locationCamera, site.locationCamera.transform }, "Repair Brooks grid distance");
        float previousDistance = canvas.planeDistance;
        canvas.planeDistance = 120f;
        BuildCameraFraming.TryFrame(site);
        Check(site.locationCamera, bounds);
        CheckBackdrop(site.locationCamera, bounds, canvas.planeDistance);
        foreach (Object edited in new Object[] { canvas, site.locationCamera, site.locationCamera.transform })
            PrefabUtility.RecordPrefabInstancePropertyModifications(edited);
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(group);
        bool saved = !wasDirty && EditorSceneManager.SaveScene(scene);
        string report = $"PASS: Brooks grid distance {previousDistance:F1} -> {canvas.planeDistance:F1}.\n" +
            $"Both banks and bridge bounds are in front of the grid; camera {site.locationCamera.transform.position:F3}.\n" +
            (saved ? "Saved the scoped repair to the previously clean CanyonCrossing scene.\n" :
                "Scene changes are unsaved; existing unsaved edits were preserved. Save CanyonCrossing when ready.\n");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(BrooksReport, report);
        Debug.Log(report);
    }

    private static void CheckBackdrop(Camera camera, Bounds bounds, float distance)
    {
        foreach (Vector3 corner in BridgePhotoCapture.Corners(bounds))
            Require(camera.WorldToViewportPoint(corner).z < distance - .01f,
                "The blue grid backdrop is in front of part of the construction area.");
        Require(camera.farClipPlane > distance, "The grid backdrop is outside the camera's rendering distance.");
    }

    [MenuItem("Tools/Civil Craft/Build/Frame Authored 2D Build Cameras")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before authoring build cameras.");
        var report = new StringBuilder();
        var edited = new HashSet<Camera>();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Fit 2D build cameras");
        foreach (BuildLocation site in LoadedSites())
        {
            Camera camera = site.locationCamera;
            if (camera == null || !BuildCameraFraming.TryGetBounds(site, out Bounds bounds)) continue;
            if (!edited.Add(camera)) continue;
            Undo.RecordObjects(new Object[] { camera, camera.transform }, "Fit 2D build camera");
            BuildCameraFraming.Frame(camera, bounds,
                site.gridImage != null ? site.gridImage.GetComponentInParent<Canvas>(true) : null);
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera.transform);
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            Check(camera, bounds);
            report.AppendLine($"{site.name}: front 2D, size {camera.orthographicSize:F2}, bounds {bounds.ToString("F2")}");
        }
        Undo.CollapseUndoOperations(group);
        report.Insert(0, $"PASS: {edited.Count} authored build cameras. Scene edits are ready to save.\n");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, report.ToString());
        Debug.Log(report.ToString());
    }

    private static IEnumerable<BuildLocation> LoadedSites() =>
        Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(scene => scene.isLoaded)
            .SelectMany(scene => scene.GetRootGameObjects()).SelectMany(root => root.GetComponentsInChildren<BuildLocation>(true));

    private static void Check(Camera camera, Bounds bounds)
    {
        Require(camera.orthographic && Quaternion.Angle(camera.transform.rotation, Quaternion.identity) < .01f,
            "The build camera must look straight onto the drawing plane.");
        Rect safe = BuildCameraFraming.ConstructionViewport;
        foreach (Vector3 corner in BridgePhotoCapture.Corners(bounds))
        {
            Vector3 point = camera.WorldToViewportPoint(corner);
            Require(point.x >= safe.xMin - .001f && point.x <= safe.xMax + .001f &&
                point.y >= safe.yMin - .001f && point.y <= safe.yMax + .001f &&
                point.z > camera.nearClipPlane && point.z < camera.farClipPlane,
                "The banks or construction area are outside the build camera.");
        }
    }

    [MenuItem("Tools/Civil Craft/Build/Validate 2D Build Camera Framing")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before validating build cameras.");
        Scene originalActive = SceneManager.GetActiveScene();
        Scene source = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        bool opened = !source.IsValid() || !source.isLoaded;
        if (opened) source = EditorSceneManager.OpenScene("Assets/Scenes/CanyonCrossing.unity", OpenSceneMode.Additive);
        Scene preview = EditorSceneManager.NewPreviewScene();
        var report = new StringBuilder();
        try
        {
            var cameraObject = new GameObject("Detached 2D build framing camera");
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var controller = cameraObject.AddComponent<BuildCameraController>();
            var gridObject = new GameObject("Detached grid depth", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
            SceneManager.MoveGameObjectToScene(gridObject, preview);
            Canvas grid = gridObject.GetComponent<Canvas>();
            grid.renderMode = RenderMode.ScreenSpaceCamera;
            grid.worldCamera = camera;
            gridObject.SetActive(false);
            int sites = 0;
            foreach (BuildLocation site in LoadedSites().ToArray())
            {
                if (site.locationCamera == null || !BuildCameraFraming.TryGetBounds(site, out Bounds bounds)) continue;
                foreach (float aspect in new[] { 16f / 9f, 19.5f / 9f, 4f / 3f })
                {
                    camera.aspect = aspect;
                    BuildCameraFraming.Frame(camera, bounds);
                    Check(camera, bounds);
                    foreach (float depth in new[] { 80f, 120f })
                    {
                        grid.planeDistance = depth;
                        BuildCameraFraming.Frame(camera, bounds, grid);
                        Check(camera, bounds);
                        CheckBackdrop(camera, bounds, depth);
                        Require(grid.planeDistance == depth, "Framing overwrote an authored grid distance.");
                    }
                }
                report.AppendLine($"{site.name}: 3 aspect ratios, anchors + construction grid + saved members, {bounds.ToString("F2")}");
                sites++;
            }
            Require(sites >= 16, "The campaign's build sites were not loaded for validation.");

            var fixture = new GameObject("Detached build site").AddComponent<BuildLocation>();
            SceneManager.MoveGameObjectToScene(fixture.gameObject, preview);
            Point AddAnchor(float x)
            {
                var go = new GameObject("Anchor");
                SceneManager.MoveGameObjectToScene(go, preview);
                Point point = go.AddComponent<Point>();
                point.transform.position = new Vector3(x, 8f, 0f);
                fixture.startingAnchors.Add(point);
                return point;
            }
            AddAnchor(-100f); AddAnchor(100f);
            fixture.locationCamera = camera;
            fixture.gridImage = gridObject.GetComponent<UnityEngine.UI.Image>();
            grid.planeDistance = 120f;
            Require(controller.FrameBuildLocation(fixture), "Build entry did not frame the active site.");
            BuildCameraFraming.TryGetBounds(fixture, out Bounds fixtureBounds);
            CheckBackdrop(camera, fixtureBounds, grid.planeDistance);
            float initialSize = camera.orthographicSize;
            Vector3 initialPosition = camera.transform.position;
            camera.orthographicSize = 20f;
            camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(30f, 0f, 0f));
            controller.ResetCameraRotation();
            Require(camera.orthographicSize == initialSize && camera.transform.position == initialPosition &&
                Quaternion.Angle(camera.transform.rotation, Quaternion.identity) < .01f,
                "Reset did not return to the fitted 2D view.");
            CheckBackdrop(camera, fixtureBounds, grid.planeDistance);
            Require(grid.planeDistance == 120f, "Build entry or Reset changed the 120 m grid setting.");
            report.Insert(0, $"PASS: {sites} sites in 3 aspect ratios with 80/120 m grid depths, plus build entry and Reset preserving 120 m.\n");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/build-camera-framing-validation.txt", report.ToString());
            Debug.Log(report.ToString());
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            if (opened) EditorSceneManager.CloseScene(source, true);
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
        }
    }

    public static void ValidateGridDepth()
    {
        Validate();
        BuildGridRenderingValidation.Validate();
        Directory.CreateDirectory("Logs/BuildGridRendering");
        foreach (string artifact in Directory.GetFiles("Temp", "BuildGridRendering*"))
            File.Copy(artifact, Path.Combine("Logs/BuildGridRendering", Path.GetFileName(artifact)), true);
    }

    private static void Require(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }
}
#endif
