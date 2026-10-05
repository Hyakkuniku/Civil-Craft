#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, isolated checks; never starts Play Mode or modifies authored scenes or network state.</summary>
public static class RecordingCameraShortcutValidation
{
    private const string Request = "Temp/RecordingCameraShortcutValidation.request";
    private const string Report = "Temp/RecordingCameraShortcutValidation.txt";
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static double nextCheck;
    private static bool running;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static string UnsafeReason()
    {
        if (running) return "Validation already running";
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play Mode";
        if (BuildPipeline.isBuildingPlayer) return "Wait for the player build";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "Wait for compilation/import";
        return null;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        if (UnsafeReason() != null)
        {
            File.WriteAllText(Report, "WAIT: " + UnsafeReason() + ". No scenes or saves changed.");
            return;
        }
        File.Delete(Request);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Recording Camera Shortcut")]
    public static void Validate()
    {
        if (UnsafeReason() != null) return;
        running = true;
        var report = new StringBuilder("RUNNING: Isolated native recording camera validation.\n");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Report, report.ToString());
        Type runtime = typeof(PlayerLook).Assembly.GetType("RecordingCameraShortcut");
        Scene active = SceneManager.GetActiveScene();
        var scenes = new Dictionary<int, SceneState>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(scene.handle, new SceneState(scene));
        }
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        CursorLockMode cursorLock = Cursor.lockState;
        bool cursorVisible = Cursor.visible;
        bool toolsHidden = Tools.hidden;
        float timeScale = Time.timeScale;
        bool audioPaused = AudioListener.pause;
        FieldInfo singleton = runtime != null ? runtime.GetField("instance", StaticFlags) : null;
        object previousService = singleton != null ? singleton.GetValue(null) : null;
        Scene preview = default;
        Component service = null;
        bool subscribed = false;
        bool passed = false;
        try
        {
            Check(runtime != null && singleton != null, "RecordingCameraShortcut has not been compiled yet.");
            ValidateShortcut(runtime, report);
            preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Recording camera validation (temporary)");
            SceneManager.MoveGameObjectToScene(root, preview);
            root.transform.position = new Vector3(0f, 10000f, 0f);
            GameObject serviceOwner = Child(root.transform, "Service fixture (Awake never invoked)");
            serviceOwner.SetActive(false);
            service = serviceOwner.AddComponent(runtime);
            Check(ReferenceEquals(singleton.GetValue(null), previousService), "Adding the inactive fixture changed the runtime singleton.");
            serviceOwner.SetActive(true);
            singleton.SetValue(null, service);
            Invoke(service, "OnEnable");
            subscribed = true;

            GameObject player = Child(root.transform, "Local player fixture");
            PlayerLook look = player.AddComponent<PlayerLook>();
            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            InputManager input = player.AddComponent<InputManager>();
            CharacterController controller = player.AddComponent<CharacterController>();
            SetField(motor, "controller", controller);
            GameObject follow = Child(player.transform, "Follow target");
            follow.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            look.followTarget = follow.transform;
            look.collisionMask = 0;
            look.canLook = true;
            GameObject cameraOwner = Child(player.transform, "Local camera");
            Camera camera = cameraOwner.AddComponent<Camera>();
            camera.enabled = false; // No render callbacks or live camera replacement.
            camera.nearClipPlane = .3f;
            camera.transform.SetPositionAndRotation(root.transform.position + new Vector3(3f, 2f, -6f), Quaternion.Euler(12f, 35f, 0f));
            look.cam = camera;
            motor.cameraTransform = camera.transform;
            Invoke(input, "EnsureInputInitialized");
            input.SetPlayerInputEnable(true);

            GameObject other = Child(root.transform, "Unowned remote-like controller fixture");
            PlayerLook otherLook = other.AddComponent<PlayerLook>();
            PlayerMotor otherMotor = other.AddComponent<PlayerMotor>();
            InputManager otherInput = other.AddComponent<InputManager>();
            Camera otherCamera = Child(other.transform, "Other camera").AddComponent<Camera>();
            otherCamera.enabled = false;
            otherLook.cam = otherCamera;
            otherLook.followTarget = other.transform;
            Component descendant = Child(player.transform, "Local descendant").AddComponent<BoxCollider>();

            ValidateOwnership(runtime, service, look, motor, input, otherLook, otherMotor, otherInput, descendant, report);
            ValidateMotion(service, look, report);
            ValidateRestoration(runtime, service, look, input, report);
            Check(Mathf.Approximately(Time.timeScale, timeScale) && AudioListener.pause == audioPaused,
                "Recording helpers changed gameplay/audio pause state.");
            passed = true;
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + Unwrap(error));
            Debug.LogException(Unwrap(error));
        }
        finally
        {
            try
            {
                if (service != null)
                {
                    Invoke(service, "ReturnToPlayer", false);
                    if (subscribed) Invoke(service, "OnDisable");
                }
            }
            catch (Exception error) { passed = false; report.AppendLine("FAIL cleanup: " + Unwrap(error)); }
            if (singleton != null) singleton.SetValue(null, previousService);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            Selection.objects = selection;
            Selection.activeObject = selected;
            Tools.hidden = toolsHidden;
            Time.timeScale = timeScale;
            AudioListener.pause = audioPaused;
            Cursor.lockState = cursorLock;
            Cursor.visible = cursorVisible;
            try
            {
                Check(SceneManager.sceneCount == scenes.Count, "The loaded authored scene count changed.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    Check(scenes.TryGetValue(scene.handle, out SceneState state), "An authored scene was replaced.");
                    state.CheckUnchanged(scene);
                }
                if (passed)
                {
                    report.AppendLine("PASS: Fixture removed; authored scene roots, loaded state and dirty flags unchanged. Original singleton, active scene, selection, tools, cursor, time scale and audio pause restored. No scene/prefab saves or multiplayer APIs used.");
                    Debug.Log("[Recording camera shortcut] PASS. Report: " + Report);
                }
            }
            catch (Exception error) { passed = false; report.AppendLine("FAIL isolation: " + error); Debug.LogException(error); }
            File.WriteAllText(Report, report.ToString());
            running = false;
        }
    }

    private static void ValidateShortcut(Type runtime, StringBuilder report)
    {
        foreach (bool control in new[] { false, true })
            foreach (bool pressed in new[] { false, true })
                foreach (bool typing in new[] { false, true })
                    Check((bool)Static(runtime, "ShouldUseShortcut", control, pressed, typing) == (control && pressed && !typing),
                        "The recording shortcut accepts a wrong key combination or text input.");
        report.AppendLine("PASS: All 8 Control/key/text-focus gate combinations; shortcuts never activate while typing.");
    }

    private static void ValidateOwnership(Type runtime, Component service, PlayerLook look, PlayerMotor motor,
        InputManager input, PlayerLook otherLook, PlayerMotor otherMotor, InputManager otherInput,
        Component descendant, StringBuilder report)
    {
        Check(Mode(service) == "FollowPlayer", "The initial camera mode is not FollowPlayer.");
        Check(!(bool)Static(runtime, "BlocksMovement", motor) && !(bool)Static(runtime, "OverridesLook", look),
            "Normal follow mode blocks player controls.");
        Transform originalParent = look.cam.transform.parent;
        Vector3 entryPosition = look.cam.transform.position;
        Quaternion entryRotation = look.cam.transform.rotation;
        Invoke(look, "TrackCameraNearClipPlane");
        look.cam.nearClipPlane = .05f; // Simulates the ordinary orbit's close-obstacle adjustment.
        Check((bool)Invoke(service, "BeginFreeCamera", look), "Free camera did not begin.");
        Check(Mode(service) == "FreeCamera" && look.cam.transform.parent == null,
            "Free camera did not detach from the player.");
        Check(Near(entryPosition, look.cam.transform.position) && Near(entryRotation, look.cam.transform.rotation),
            "Entry changed the captured camera pose.");
        Check(Mathf.Abs(look.cam.nearClipPlane - .3f) < .0001f, "Entry did not release the orbit's temporary near clip.");
        Check(!(bool)Invoke(service, "BeginFreeCamera", otherLook), "An active recording was rebound to another player.");
        Check((bool)Static(runtime, "BlocksMovement", motor) && (bool)Static(runtime, "BlocksMovement", input) &&
            (bool)Static(runtime, "BlocksMovement", descendant) && (bool)Static(runtime, "OwnsFreeCameraCursor", input),
            "Free camera does not own all and only the local player controls/cursor.");
        Check(!(bool)Static(runtime, "BlocksMovement", otherMotor) && !(bool)Static(runtime, "BlocksMovement", otherInput) &&
            !(bool)Static(runtime, "BlocksMovement", (object)null) && !(bool)Static(runtime, "OverridesLook", otherLook),
            "Free camera blocks an unowned/remote player.");
        Check((bool)Static(runtime, "OverridesLook", look), "Free camera does not override the local orbit.");
        Check(look.canLook && look.enabled && motor.enabled && input.enabled && input.IsPlayerInputEnabled,
            "Recording disabled a player component/action or overwrote authored look permissions.");
        AssertOrbitIgnored(service, look, "free");
        Invoke(service, "LockCamera");
        Check(Mode(service) == "LockedShot" && !(bool)Static(runtime, "BlocksMovement", motor) &&
            !(bool)Static(runtime, "OwnsFreeCameraCursor", input) && (bool)Static(runtime, "OverridesLook", look),
            "Locking must release movement and retain camera ownership.");
        AssertOrbitIgnored(service, look, "locked");
        Invoke(service, "ResumeFreeCamera");
        Check(Mode(service) == "FreeCamera" && (bool)Static(runtime, "BlocksMovement", motor), "Locked to Free transition failed.");
        Invoke(service, "ReturnToPlayer", false);
        Check(Mode(service) == "FollowPlayer" && look.cam.transform.parent == originalParent &&
            !(bool)Static(runtime, "BlocksMovement", motor) && !(bool)Static(runtime, "OverridesLook", look),
            "Return did not restore the original parent and release ownership.");
        report.AppendLine("PASS: Follow → Free → Locked → Free → Follow; only local player movement/cursor are captured in Free, local orbit remains captured in Free/Locked, and unowned/remote-like controllers are unaffected. No player components or authored input/look states disabled.");
    }

    private static void AssertOrbitIgnored(Component service, PlayerLook look, string label)
    {
        Vector3 position = look.cam.transform.position;
        Quaternion rotation = look.cam.transform.rotation;
        float yaw = (float)GetField(look, "yaw"), pitch = (float)GetField(look, "pitch");
        look.transform.position += new Vector3(10f, 0f, 4f);
        Invoke(look, "ProcessLook", new Vector2(500f, -500f));
        Invoke(look, "LateUpdate");
        look.SnapToFollowTarget();
        Check(Near(position, look.cam.transform.position) && Near(rotation, look.cam.transform.rotation),
            "Player movement, orbit or teleport snap moved the " + label + " recording camera.");
        Check((float)GetField(look, "yaw") == yaw && (float)GetField(look, "pitch") == pitch,
            "Blocked look input accumulated hidden orbit angles in " + label + " mode.");
        look.cam.transform.SetPositionAndRotation(position + Vector3.one, Quaternion.identity);
        Invoke(service, "ApplyShotPose");
        Check(Near(position, look.cam.transform.position) && Near(rotation, look.cam.transform.rotation),
            "The captured shot pose was not reapplied.");
    }

    private static void ValidateMotion(Component service, PlayerLook look, StringBuilder report)
    {
        SetField(service, "moveSpeed", 8f);
        SetField(service, "fastMultiplier", 4f);
        SetField(service, "mouseSensitivity", .12f);
        SetField(service, "movementSmoothTime", 0f);
        ResetShot(service, look);
        Vector3 start = look.cam.transform.position;
        Invoke(service, "MoveCamera", new Vector3(1f, 0f, 1f), Vector2.zero, false, .1f);
        Check(Mathf.Abs(Vector3.Distance(start, look.cam.transform.position) - .8f) < .0002f,
            "Diagonal camera movement exceeds normalized speed.");
        Invoke(service, "LockCamera");
        Vector3 locked = look.cam.transform.position;
        Quaternion lockedRotation = look.cam.transform.rotation;
        Invoke(service, "MoveCamera", Vector3.one, new Vector2(100f, 100f), true, .1f);
        Check(Near(locked, look.cam.transform.position) && Near(lockedRotation, look.cam.transform.rotation),
            "Locked camera responds to free movement/look commands.");
        ResetShot(service, look);
        start = look.cam.transform.position;
        Invoke(service, "MoveCamera", Vector3.forward, Vector2.zero, true, .1f);
        Check(Mathf.Abs(Vector3.Distance(start, look.cam.transform.position) - 3.2f) < .0002f,
            "Fast movement does not apply the expected multiplier.");

        ResetShot(service, look);
        Invoke(service, "MoveCamera", Vector3.zero, new Vector2(40f, 20f), false, .1f);
        Quaternion first = look.cam.transform.rotation;
        Check(Near(first, Quaternion.Euler(9.6f, 39.8f, 0f)), "Mouse sensitivity or pitch direction is incorrect.");
        ResetShot(service, look);
        Invoke(service, "MoveCamera", Vector3.zero, new Vector2(40f, 20f), false, .02f);
        Check(Near(first, look.cam.transform.rotation), "Mouse pixel sensitivity depends on frame duration.");
        Invoke(service, "MoveCamera", Vector3.zero, new Vector2(0f, 100000f), false, .02f);
        Check(Mathf.Abs(Mathf.DeltaAngle(0f, look.cam.transform.rotation.eulerAngles.x) + 89f) < .001f,
            "Free camera pitch exceeds its vertical limit.");

        ResetShot(service, look);
        SetField(service, "movementSmoothTime", .12f);
        start = look.cam.transform.position;
        Invoke(service, "MoveCamera", Vector3.forward, Vector2.zero, false, .05f);
        float expected = 8f * (1f - Mathf.Exp(-.05f / .12f));
        Vector3 firstVelocity = (Vector3)GetField(service, "velocity");
        Check(Mathf.Abs(firstVelocity.magnitude - expected) < .0002f &&
            Mathf.Abs(Vector3.Distance(start, look.cam.transform.position) - expected * .05f) < .0002f,
            "Movement smoothing is not the expected unscaled exponential blend.");
        Invoke(service, "MoveCamera", Vector3.zero, Vector2.zero, false, .05f);
        Vector3 stoppedVelocity = (Vector3)GetField(service, "velocity");
        Check(stoppedVelocity.magnitude > 0f && stoppedVelocity.magnitude < firstVelocity.magnitude,
            "Free camera velocity does not smoothly settle after release.");
        Vector3 beforeZeroDelta = look.cam.transform.position;
        Invoke(service, "MoveCamera", Vector3.one, Vector2.one, true, 0f);
        Check(Near(beforeZeroDelta, look.cam.transform.position), "A zero-duration camera command moved the shot.");
        Invoke(service, "ReturnToPlayer", false);
        report.AppendLine("PASS: Normalized diagonal speed, Shift multiplier, frame-duration-independent mouse sensitivity, pitch limits, no Locked movement, unscaled exponential acceleration/deceleration and zero-duration safety.");
    }

    private static void ValidateRestoration(Type runtime, Component service, PlayerLook look, InputManager input, StringBuilder report)
    {
        ResetShot(service, look);
        Transform parent = (Transform)GetField(service, "originalParent");
        Invoke(service, "LockCamera");
        look.canLook = false; // Existing cutscene/tutorial locks must remain authored.
        Static(runtime, "ReturnToPlayerForGameplayTransition");
        Check(Mode(service) == "FollowPlayer" && look.cam.transform.parent == parent,
            "Build/cinematic transition did not release recording ownership.");
        Vector3 expectedFollow = look.followTarget.position + Quaternion.Euler(15f, 0f, 0f) * Vector3.back * look.defaultDistance;
        Check(Near(expectedFollow, look.cam.transform.position), "Gameplay transition did not restore the follow pose.");
        Check(!look.canLook && look.enabled && input.enabled && input.IsPlayerInputEnabled,
            "Gameplay handoff overwrote an existing orbit/input lock.");
        look.canLook = true;

        ResetShot(service, look);
        parent = (Transform)GetField(service, "originalParent");
        Invoke(service, "ActiveSceneChanged", look.gameObject.scene, default(Scene));
        Check(Mode(service) == "FollowPlayer" && look.cam.transform.parent == parent, "Scene transition did not restore camera parent.");

        ResetShot(service, look);
        parent = (Transform)GetField(service, "originalParent");
        float savedClip = look.cam.nearClipPlane;
        look.cam.nearClipPlane = .015f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
        Invoke(input, "ApplyCursorState");
        Check(!Cursor.visible, "The player cursor controller fought free camera cursor ownership.");
        Invoke(service, "OnDisable");
        Check(Mode(service) == "FollowPlayer" && look.cam.transform.parent == parent &&
            Mathf.Abs(look.cam.nearClipPlane - savedClip) < .0001f && Cursor.visible,
            "Disable did not restore camera parent, near clip and original cursor.");
        Check(look.canLook && input.IsPlayerInputEnabled, "Cleanup overwrote gameplay input/look permission.");
        Check(GetField(service, "localLook") == null && GetField(service, "recordingCamera") == null,
            "Cleanup retains scene controller/camera references.");
        report.AppendLine("PASS: Build/cinematic and scene-change release, disable cleanup, normal orbit near clip, original camera parent and cursor restored; no hidden orbit angle accumulation or scene controller references retained.");
    }

    private static void ResetShot(Component service, PlayerLook look)
    {
        Invoke(service, "ReturnToPlayer", false);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        look.cam.transform.SetPositionAndRotation(new Vector3(3f, 10002f, -6f), Quaternion.Euler(12f, 35f, 0f));
        Check((bool)Invoke(service, "BeginFreeCamera", look), "A fresh free-camera test could not begin.");
    }

    private static string Mode(Component service) => service.GetType().GetProperty("Mode", InstanceFlags).GetValue(service).ToString();
    private static object GetField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceFlags);
        Check(field != null, "Fixture field missing: " + name);
        return field.GetValue(target);
    }
    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceFlags);
        Check(field != null, "Fixture field missing: " + name);
        field.SetValue(target, value);
    }
    private static object Invoke(object target, string name, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(name, InstanceFlags);
        Check(method != null, "Fixture method missing: " + name);
        return method.Invoke(target, args.Length == 0 ? null : args);
    }
    private static object Static(Type runtime, string name, params object[] args)
    {
        MethodInfo method = runtime.GetMethod(name, StaticFlags);
        Check(method != null, "Static method missing: " + name);
        return method.Invoke(null, args.Length == 0 ? null : args);
    }
    private static GameObject Child(Transform parent, string name)
    {
        var owner = new GameObject(name);
        SceneManager.MoveGameObjectToScene(owner, parent.gameObject.scene);
        owner.transform.SetParent(parent, false);
        return owner;
    }
    private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .000001f;
    private static bool Near(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) < .005f;
    private static Exception Unwrap(Exception error) => error is TargetInvocationException wrapped && wrapped.InnerException != null ? wrapped.InnerException : error;
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class SceneState
    {
        private readonly bool loaded, dirty;
        private readonly string path;
        private readonly HashSet<int> roots = new HashSet<int>();
        public SceneState(Scene scene)
        {
            loaded = scene.isLoaded; dirty = scene.isDirty; path = scene.path;
            foreach (GameObject root in scene.GetRootGameObjects()) roots.Add(root.GetInstanceID());
        }
        public void CheckUnchanged(Scene scene)
        {
            var after = new HashSet<int>();
            foreach (GameObject root in scene.GetRootGameObjects()) after.Add(root.GetInstanceID());
            Check(scene.isLoaded == loaded && scene.isDirty == dirty && scene.path == path && roots.SetEquals(after),
                "Validation changed an authored scene's roots/loaded state/dirty flag: " + scene.name);
        }
    }
}
#endif
