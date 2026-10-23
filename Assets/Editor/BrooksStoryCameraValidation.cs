#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Read-only framing previews of Brooks's authored cinematic, using a disposable camera.</summary>
[InitializeOnLoad]
public static class BrooksStoryCameraValidation
{
    public const string RequestPath = "Temp/brooks-story-camera-v1.request";
    public const string ReportPath = "Logs/brooks-story-camera-v1.txt";
    private const string CanyonPath = "Assets/Scenes/CanyonCrossing.unity";
    private const string CinematicId = "BrooksMarketConnection-v1";
    private const string OutputDirectory = "Temp/BrooksStoryCameraPreviews";
    private const int Width = 1280;
    private const int Height = 720;
    private static double nextPoll;
    private static bool running;

    static BrooksStoryCameraValidation() { EditorApplication.update += Poll; }

    private static bool Ready => !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
        !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!Ready || !File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Story/Brooks/3. Preview Camera Shots")]
    public static void Run()
    {
        if (running) return;
        var report = new StringBuilder("Brooks detached cinematic framing previews\n");
        if (!Ready)
        {
            Write("NOT_RUN\nWait for compilation/import to finish, stop Play Mode and close Prefab Mode. No scene or camera changed.\n");
            return;
        }
        running = true;
        try
        {
            Capture(report);
            report.AppendLine("PASS: detached camera renders completed; original cameras, director, actors, shot poses, scene setup and scene dirty flags unchanged.");
            report.AppendLine("No cinematic playback, HUD changes, player save access, scene save, or gameplay callbacks were requested.");
            report.AppendLine("These still frames verify framing only, not moving-camera timing or native Play Mode behavior.");
            Write("PASS\n" + report);
            Debug.Log("[Brooks camera preview] PASS. " + ReportPath);
        }
        catch (Exception error)
        {
            Write("FAILED\n" + error + "\n" + report);
            Debug.LogException(error);
        }
        finally { running = false; }
    }

    private static void Capture(StringBuilder report)
    {
        Scene[] scenes = LoadedScenes();
        Scene[] matches = scenes.Where(scene => scene.path == CanyonPath).ToArray();
        Require(matches.Length == 1, "Load CanyonCrossing before previewing Brooks's applied camera shots. No scenes were opened or closed.");
        Scene canyon = matches[0];
        CinematicDirector[] directors = canyon.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<CinematicDirector>(true))
            .Where(director => director.cinematicID == CinematicId).ToArray();
        Require(directors.Length == 1, "Expected exactly one applied BrooksMarketConnection-v1 cinematic in CanyonCrossing.");
        CinematicDirector director = directors[0];
        Camera source = director.cinematicCamera;
        Require(source != null && source.gameObject.scene == canyon, "Brooks's cinematic needs its existing Canyon camera reference.");
        Require(!director.IsPlaying && !director.playOnStart, "Brooks's cinematic must not be running or configured to auto-play on scene start.");
        Require(director.shots != null && director.shots.Count == 3,
            "Expected the authored three Brooks market-connection shots.");
        ValidateCamera(source);
        foreach (CinematicShot shot in director.shots)
        {
            Require(shot != null && shot.cameraStartPoint != null && shot.cameraEndPoint != null,
                "Each Brooks shot needs explicit start and end poses; no source-camera fallback is used.");
            Require(shot.cameraStartPoint.gameObject.scene == canyon && shot.cameraEndPoint.gameObject.scene == canyon,
                "Brooks camera poses must belong to loaded CanyonCrossing.");
            Require(Finite(shot.duration) && shot.duration > 0f, "Brooks shot duration must be positive and finite.");
            ValidatePose(shot.cameraStartPoint);
            ValidatePose(shot.cameraEndPoint);
            Require((shot.cameraStartPoint.position - shot.cameraEndPoint.position).sqrMagnitude > .0001f ||
                Quaternion.Angle(shot.cameraStartPoint.rotation, shot.cameraEndPoint.rotation) > .01f,
                "Each Brooks shot must have distinct start/end poses for its authored camera movement.");
        }

        bool[] dirtyBefore = scenes.Select(scene => scene.isDirty).ToArray();
        Scene activeBefore = SceneManager.GetActiveScene();
        var protectedState = new Dictionary<Object, string>();
        foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            if (camera != null && camera.gameObject.scene.IsValid() && camera.gameObject.scene.isLoaded)
            { Snapshot(protectedState, camera); Snapshot(protectedState, camera.transform); }
        Snapshot(protectedState, director);
        Snapshot(protectedState, director.transform);
        Snapshot(protectedState, director.playerActor);
        Snapshot(protectedState, director.playerAnimator);
        foreach (CinematicShot shot in director.shots)
        {
            Snapshot(protectedState, shot.cameraStartPoint);
            Snapshot(protectedState, shot.cameraEndPoint);
            Snapshot(protectedState, shot.playerStartPoint);
            Snapshot(protectedState, shot.playerWalkTarget);
        }
        report.AppendLine("Source camera: " + source.name + "; authored shots: " + director.shots.Count + "; output: 1280 x 720.");
        report.AppendLine("Framing uses the existing market approaches and construction site; no old crossing, barriers or detour geometry is invented.");
        report.AppendLine("Canyon dirty before preview: " + canyon.isDirty + ". Existing unsaved changes are not saved or marked clean.");

        GameObject temporary = null;
        Camera preview = null;
        RenderTexture target = null;
        RenderTexture originalActive = RenderTexture.active;
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            temporary = new GameObject("Brooks Detached Camera Preview") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(temporary, canyon);
            preview = temporary.AddComponent<Camera>();
            preview.hideFlags = HideFlags.HideAndDontSave;
            preview.CopyFrom(source);
            preview.enabled = false;
            preview.useOcclusionCulling = false;
            preview.stereoTargetEye = StereoTargetEyeMask.None;
            preview.rect = new Rect(0f, 0f, 1f, 1f);
            preview.aspect = (float)Width / Height;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) preview.cullingMask &= ~(1 << uiLayer);
            report.AppendLine(uiLayer >= 0 ? "Preview excludes UI layer; original camera culling masks unchanged." : "No named UI layer found; original culling mask copied.");
            target = new RenderTexture(Width, Height, 24)
            { name = "Brooks Detached Camera Preview RT", hideFlags = HideFlags.HideAndDontSave };
            Require(target.Create(), "Could not create Brooks preview render texture.");
            preview.targetTexture = target;
            for (int index = 0; index < director.shots.Count; index++)
            {
                CinematicShot shot = director.shots[index];
                RenderPose(preview, target, shot.cameraStartPoint, index, "start", shot.shotName, report);
                RenderPose(preview, target, shot.cameraEndPoint, index, "end", shot.shotName, report);
            }
        }
        finally
        {
            RenderTexture.active = originalActive;
            if (preview != null) preview.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (temporary != null) Object.DestroyImmediate(temporary);
            VerifyProtectedState(protectedState, scenes, dirtyBefore, activeBefore, report);
        }
    }

    private static void RenderPose(Camera preview, RenderTexture target, Transform pose, int index,
        string endpoint, string shotName, StringBuilder report)
    {
        ValidatePose(pose);
        preview.transform.SetPositionAndRotation(pose.position, pose.rotation);
        ValidateCamera(preview);
        string path = Path.Combine(OutputDirectory, "brooks-shot-" + (index + 1).ToString("00") + "-" + endpoint + ".png");
        Texture2D pixels = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            if (GraphicsSettings.currentRenderPipeline == null) preview.Render();
            else RenderPipeline.SubmitRenderRequest(preview,
                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            pixels.Apply();
            byte[] encoded = pixels.EncodeToPNG();
            Require(encoded != null && encoded.Length > 0, "PNG encoding failed for " + path);
            File.WriteAllBytes(path, encoded);
            report.AppendLine("Rendered " + path + " - " + shotName + " (" + endpoint + "), position " + pose.position.ToString("F2") + ".");
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels != null) Object.DestroyImmediate(pixels);
        }
    }

    private static void VerifyProtectedState(Dictionary<Object, string> protectedState, Scene[] scenes,
        bool[] dirtyBefore, Scene activeBefore, StringBuilder report)
    {
        foreach (var entry in protectedState)
            Require(entry.Key != null && EditorJsonUtility.ToJson(entry.Key) == entry.Value,
                "A source camera/director/actor/shot pose changed while rendering Brooks previews. No scene save was performed.");
        Scene[] after = LoadedScenes();
        Require(after.Length == scenes.Length && after.Select(scene => scene.handle).SequenceEqual(scenes.Select(scene => scene.handle)) &&
            SceneManager.GetActiveScene().handle == activeBefore.handle, "Loaded scene setup changed during the detached preview.");
        for (int index = 0; index < scenes.Length; index++)
            Require(scenes[index].isDirty == dirtyBefore[index], "Scene dirty flag changed during preview: " + scenes[index].path);
        report.AppendLine("Cleanup verified: temporary camera/render texture destroyed; RenderTexture.active restored; source snapshots and dirty flags unchanged.");
    }

    private static Scene[] LoadedScenes()
    {
        var result = new List<Scene>();
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene.IsValid() && scene.isLoaded) result.Add(scene);
        }
        return result.ToArray();
    }

    private static void Snapshot(Dictionary<Object, string> snapshots, Object value)
    { if (value != null && !snapshots.ContainsKey(value)) snapshots.Add(value, EditorJsonUtility.ToJson(value)); }

    private static void ValidateCamera(Camera camera)
    {
        Require(Finite(camera.nearClipPlane) && Finite(camera.farClipPlane) && camera.nearClipPlane > 0f &&
            camera.farClipPlane > camera.nearClipPlane, "Camera near/far clipping planes must be finite, positive and ordered.");
        Require(Finite(camera.fieldOfView) && camera.fieldOfView > 0f && camera.fieldOfView < 180f &&
            Finite(camera.orthographicSize) && camera.orthographicSize > 0f,
            "Camera projection must have a valid field of view and orthographic size.");
        ValidatePose(camera.transform);
    }

    private static void ValidatePose(Transform pose)
    {
        Require(pose != null && Finite(pose.position) && Finite(pose.rotation.x) && Finite(pose.rotation.y) &&
            Finite(pose.rotation.z) && Finite(pose.rotation.w), "Camera pose position/rotation must be finite.");
        Vector3 forward = pose.forward;
        Require(Finite(forward) && Mathf.Abs(forward.sqrMagnitude - 1f) < .01f,
            "Camera forward direction must be finite and normalized.");
    }

    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Write(string report) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report); }
}
#endif
