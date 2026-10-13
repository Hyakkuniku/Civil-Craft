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
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in native raycast regression. Never enters Play Mode or sends pointer/input/UI events.</summary>
public static class MobileJoystickGridRaycastValidation
{
    private const string Request = "Temp/mobile-joystick-grid-raycast-validation.request";
    private const string Report = "Temp/mobile-joystick-grid-raycast-validation-report.txt";
    private static bool running;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool SafeToRun() => !running && !EditorApplication.isPlayingOrWillChangePlaymode &&
        !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(Request) || !SafeToRun()) return;
        File.Delete(Request); // Consume only this explicitly requested, editor-only diagnostic marker.
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Movement Grid Raycasts")]
    public static void Validate()
    {
        if (!SafeToRun()) return;
        running = true;
        Scene active = SceneManager.GetActiveScene();
        var scenes = new Dictionary<int, SceneSnapshot>();
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            scenes.Add(scene.handle, new SceneSnapshot(scene));
        }
        EventSystem previousEventSystem = EventSystem.current;
        BaseRaycaster[] previousRaycasters = RaycasterManager.GetRaycasters().ToArray();
        var fixtureRaycasters = new List<BaseRaycaster>();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        Scene preview = default;
        BuildGridRendering fixtureRendering = null;
        var report = new StringBuilder("Native detached Edit Mode build-grid raycast regression.\n");
        bool passed = false;
        bool renderedFixture = false;
        try
        {
            preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Detached movement grid raycast fixture");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, preview);
            root.hideFlags = HideFlags.HideAndDontSave;
            // Mimic the real construction owner above GridCanvas without
            // enabling BarCreator or executing any construction/gameplay code.
            var outerHandler = root.AddComponent<MobileJoystickValidationPointerSentinel>();
            outerHandler.enabled = false;

            var eventObject = Child("Inactive fixture EventSystem", root.transform, false);
            EventSystem eventSystem = eventObject.AddComponent<EventSystem>();
            Check(!eventSystem.isActiveAndEnabled && EventSystem.current == previousEventSystem,
                "Fixture must never replace or activate the live EventSystem.");

            var cameraObject = Child("Disabled location camera", root.transform, true);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = preview;
            camera.transform.position = new Vector3(0f, 0f, -100f);
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 2000f;
            camera.cullingMask = 1 << 30;
            int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            camera.pixelRect = new Rect(0f, 0f, width, height);
            camera.aspect = (float)width / height;
            camera.orthographicSize = height / 2f;

            var canvasObject = Child("GridCanvas", root.transform, true, typeof(RectTransform), typeof(Canvas));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 20f;
            fixtureRaycasters.Add(canvasObject.AddComponent<GraphicRaycaster>());
            Image backdrop = ImageChild("Decorative blue backdrop", canvasObject.transform, Vector2.zero, Vector2.one);
            backdrop.color = new Color(.2f, .35f, .9f, 1f);
            Image grid = ImageChild("Decorative grid", canvasObject.transform, Vector2.zero, Vector2.one);
            grid.color = new Color(1f, 1f, 1f, .1f);

            Image sameCanvasButtonImage = ImageChild("Same-canvas foreground button", canvasObject.transform,
                new Vector2(.35f, .1f), new Vector2(.45f, .3f));
            Button sameCanvasButton = sameCanvasButtonImage.gameObject.AddComponent<Button>();
            sameCanvasButton.targetGraphic = sameCanvasButtonImage;
            sameCanvasButton.onClick = new Button.ButtonClickedEvent();
            var handlerParent = Child("Disabled custom pointer handler parent", canvasObject.transform, true,
                typeof(RectTransform));
            Stretch((RectTransform)handlerParent.transform, new Vector2(.05f, .1f), new Vector2(.15f, .3f));
            var handler = handlerParent.AddComponent<MobileJoystickValidationPointerSentinel>();
            handler.enabled = false;
            Image handlerImage = ImageChild("Custom handler child graphic", handlerParent.transform,
                Vector2.zero, Vector2.one);

            // Real controls have a separate nested HUD canvas. The production
            // grid rendering policy explicitly leaves nested HUD ownership alone.
            var hudObject = Child("Nested foreground HUD", canvasObject.transform, true, typeof(RectTransform), typeof(Canvas));
            Stretch((RectTransform)hudObject.transform, Vector2.zero, Vector2.one);
            Canvas hud = hudObject.GetComponent<Canvas>();
            hud.overrideSorting = true;
            hud.sortingOrder = 10;
            fixtureRaycasters.Add(hudObject.AddComponent<GraphicRaycaster>());
            Image buttonImage = ImageChild("Foreground left-side button", hudObject.transform,
                new Vector2(.15f, .65f), new Vector2(.35f, .85f));
            Button button = buttonImage.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick = new Button.ButtonClickedEvent();
            var inactiveBase = Child("Inactive joystick base", canvasObject.transform, false, typeof(RectTransform));
            var inactiveHandle = Child("Inactive joystick handle", inactiveBase.transform, true, typeof(RectTransform));
            MobileMovementJoystick joystick = inactiveHandle.AddComponent<MobileMovementJoystick>();
            joystick.Configure((RectTransform)inactiveBase.transform, 100f, "<Gamepad>/leftStick");
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            root.SetActive(true);
            // Graphic is ExecuteAlways, but GraphicRaycaster/BaseRaycaster is
            // not. Register only these detached fixture raycasters explicitly;
            // EventSystem itself stays inactive and receives no Update/events.
            foreach (BaseRaycaster raycaster in fixtureRaycasters)
                if (!RaycasterManager.GetRaycasters().Contains(raycaster))
                    RaycasterLifecycle(raycaster, "OnEnable");
            PrepareGraphics(root);

            Vector2 emptyPoint = new Vector2(width * .25f, height * .4f);
            Vector2 buttonPoint = new Vector2(width * .25f, height * .75f);
            Vector2 sameCanvasButtonPoint = new Vector2(width * .4f, height * .2f);
            Vector2 handlerPoint = new Vector2(width * .1f, height * .2f);
            List<RaycastResult> before = Hits(eventSystem, preview, emptyPoint);
            DescribeGraphics(report, "Initial native geometry", camera, emptyPoint,
                new[] { backdrop, grid, sameCanvasButtonImage, handlerImage, buttonImage }, fixtureRaycasters);
            if (!before.Any(hit => hit.gameObject == grid.gameObject) ||
                !before.Any(hit => hit.gameObject == backdrop.gameObject))
            {
                // A preview scene has no normal camera render loop. Unity may
                // leave CanvasRenderer.absoluteDepth at -1 until an actual
                // isolated render; GraphicRaycaster correctly rejects that
                // not-yet-renderable geometry. Populate native batches without
                // enabling the camera or rendering the user's scene/Game view.
                RenderFixture(camera, width, height);
                renderedFixture = true;
                PrepareGraphics(root);
                before = Hits(eventSystem, preview, emptyPoint);
                DescribeGraphics(report, "After isolated native render", camera, emptyPoint,
                    new[] { backdrop, grid, sameCanvasButtonImage, handlerImage, buttonImage }, fixtureRaycasters);
            }
            report.AppendLine("Fixture: viewport=" + width + "x" + height + "; camera enabled=" + camera.enabled +
                "; canvas mode=" + canvas.renderMode + "; before=" + Names(before));
            Check(!camera.enabled && canvas.worldCamera == camera && canvas.isActiveAndEnabled,
                "Fixture must have an active ScreenSpaceCamera Canvas with a disabled location camera.");
            Check(before.Any(hit => hit.gameObject == backdrop.gameObject) &&
                before.Any(hit => hit.gameObject == grid.gameObject),
                "Native precondition failed: disabled-camera decorative grid must raycast in empty left-side space.");
            Check(!joystick.isActiveAndEnabled && joystick.control == null && Blocked(joystick, before),
                "Native grid precondition must block actual joystick routing before grid ownership is configured.");
            report.AppendLine("PASS: Actual EventSystem.RaycastAll hits both screen-filling decorative Images even though their assigned render camera is disabled.");

            List<RaycastResult> buttonBefore = Hits(eventSystem, preview, buttonPoint);
            Check(buttonBefore.Count > 0 && buttonBefore[0].gameObject == button.gameObject,
                "Foreground HUD button is not the first native fixture hit.");

            BuildGridRendering rendering = BuildGridRendering.Ensure(grid);
            fixtureRendering = rendering;
            Check(rendering != null && rendering.gameObject == canvasObject, "Actual production grid Configure did not run.");
            Canvas.ForceUpdateCanvases();
            List<RaycastResult> after = Hits(eventSystem, preview, emptyPoint);
            report.AppendLine("After=" + Names(after));
            Check(backdrop.raycastTarget && grid.raycastTarget &&
                after.Any(hit => hit.gameObject == grid.gameObject) &&
                after.Any(hit => hit.gameObject == backdrop.gameObject),
                "Joystick-only policy must preserve native grid raycasts for bridge construction.");
            Check(!Blocked(joystick, after), "Configured decorative grid still blocks actual empty-space joystick routing.");
            Check(grid.enabled && backdrop.enabled && canvas.enabled && !camera.enabled,
                "Input-only policy changed graphic visibility, Canvas enablement or the location camera.");
            Check(grid.material != null && grid.material.renderQueue == BuildGridRendering.GridRenderQueue &&
                backdrop.material != null && backdrop.material.renderQueue == BuildGridRendering.GridRenderQueue,
                "Input-only policy broke the existing build-grid material/render-order treatment.");
            report.AppendLine("PASS: Actual joystick predicate excludes configured grid/backdrop hits while native construction raycasts, Images/Canvas/camera and existing material queue remain intact; an outer construction pointer handler above Canvas does not turn scenery into a HUD blocker.");

            List<RaycastResult> buttonAfter = Hits(eventSystem, preview, buttonPoint);
            Check(buttonImage.raycastTarget && button.interactable && button.onClick.GetPersistentEventCount() == 0 &&
                buttonAfter.Count > 0 && buttonAfter[0].gameObject == button.gameObject,
                "Real foreground HUD button lost its movement-start protection.");
            Check(Blocked(joystick, buttonAfter), "Actual joystick predicate must protect the nested foreground Button.");
            List<RaycastResult> sameCanvasHits = Hits(eventSystem, preview, sameCanvasButtonPoint);
            Check(sameCanvasButtonImage.raycastTarget && sameCanvasButton.interactable &&
                sameCanvasHits.FirstOrDefault().gameObject == sameCanvasButton.gameObject && Blocked(joystick, sameCanvasHits),
                "Actual same-canvas Button lost its native raycast target.");
            List<RaycastResult> handlerHits = Hits(eventSystem, preview, handlerPoint);
            Check(handlerImage.raycastTarget && !handler.enabled && handler.calls == 0 &&
                handlerHits.FirstOrDefault().gameObject == handlerImage.gameObject && Blocked(joystick, handlerHits),
                "A disabled custom ancestor handler lost its native raycast target or was invoked.");
            rendering.Configure(grid);
            Canvas.ForceUpdateCanvases();
            Check(!Blocked(joystick, Hits(eventSystem, preview, emptyPoint)) && buttonImage.raycastTarget &&
                Hits(eventSystem, preview, buttonPoint).FirstOrDefault().gameObject == button.gameObject &&
                outerHandler.calls == 0 && !outerHandler.enabled && !joystick.isActiveAndEnabled && joystick.control == null,
                "Repeated grid Configure restored blockers or damaged nested HUD controls.");
            report.AppendLine("PASS: Actual nested and same-canvas foreground Buttons and a disabled custom ancestor handler retain their native raycast targets; no pointer/UI callbacks were invoked.");
            ReleaseRendering(rendering);
            fixtureRendering = null;
            Canvas.ForceUpdateCanvases();
            Check(backdrop.raycastTarget && grid.raycastTarget && buttonImage.raycastTarget &&
                sameCanvasButtonImage.raycastTarget && handlerImage.raycastTarget &&
                Hits(eventSystem, preview, emptyPoint).Any(hit => hit.gameObject == backdrop.gameObject) &&
                Blocked(joystick, Hits(eventSystem, preview, emptyPoint)),
                "Removing only the detached grid ownership marker must leave original raycasts intact and restore ordinary UI blocking.");
            report.AppendLine("PASS: Removing detached grid ownership leaves all original raycasts intact and restores ordinary blocking, without enabling joystick input/device creation.");
            passed = true;
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            try
            {
                if (fixtureRendering != null) ReleaseRendering(fixtureRendering);
                foreach (BaseRaycaster raycaster in fixtureRaycasters)
                    if (raycaster != null) RaycasterLifecycle(raycaster, "OnDisable");
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                Check(EventSystem.current == previousEventSystem, "Live EventSystem changed.");
                Check(RaycasterManager.GetRaycasters().SequenceEqual(previousRaycasters),
                    "Live raycaster registry sequence changed after detached cleanup.");
                Check(SceneManager.sceneCount == scenes.Count, "Authored loaded scene count changed.");
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene scene = SceneManager.GetSceneAt(index);
                    Check(scenes.TryGetValue(scene.handle, out SceneSnapshot snapshot), "Authored scene was replaced.");
                    snapshot.Verify(scene);
                }
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Selection.objects = selection;
                Selection.activeObject = selected;
                report.AppendLine("PASS: Detached resources removed; authored scenes/roots/dirty flags, active scene, selection, live EventSystem and raycaster registry preserved. Isolated fixture render=" + renderedFixture +
                    "; no Game view/user-scene render, Play Mode, device input, UI callbacks, preferences, player data, account/network operations or scene saves.");
            }
            catch (Exception error)
            {
                passed = false;
                report.AppendLine("FAIL isolation/cleanup: " + error);
                Debug.LogException(error);
            }
            running = false;
            report.AppendLine(passed ? "RESULT: PASS" : "RESULT: FAIL");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Report, report.ToString());
            if (passed) Debug.Log("[Movement grid raycasts] PASS. Report: " + Report);
        }
    }

    private static void ReleaseRendering(BuildGridRendering rendering)
    {
        // Normal runtime MonoBehaviours need not receive OnDestroy in a detached
        // Edit Mode preview. Release owned materials explicitly and idempotently.
        typeof(BuildGridRendering).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(rendering, null);
        Object.DestroyImmediate(rendering);
    }

    private static void RaycasterLifecycle(BaseRaycaster raycaster, string method) =>
        typeof(BaseRaycaster).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(raycaster, null);

    private static void PrepareGraphics(GameObject root)
    {
        Canvas.ForceUpdateCanvases();
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>())
        {
            graphic.SetAllDirty();
            graphic.Rebuild(CanvasUpdate.PreRender);
        }
        Canvas.ForceUpdateCanvases();
    }

    private static void RenderFixture(Camera camera, int width, int height)
    {
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        var target = new RenderTexture(width, height, 24) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            Check(target.Create(), "Could not create detached native raycast render target.");
            camera.targetTexture = target;
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static void DescribeGraphics(StringBuilder report, string phase, Camera camera, Vector2 position,
        IEnumerable<Graphic> graphics, IEnumerable<BaseRaycaster> raycasters)
    {
        report.AppendLine(phase + ": camera pixelRect=" + camera.pixelRect + "; viewportPoint=" +
            camera.ScreenToViewportPoint(position) + "; enabled=" + camera.enabled + "; native raycasters=" +
            string.Join(", ", raycasters.Select(raycaster => raycaster.name + "/registered=" +
                RaycasterManager.GetRaycasters().Contains(raycaster))));
        var corners = new Vector3[4];
        foreach (Graphic graphic in graphics)
        {
            graphic.rectTransform.GetWorldCorners(corners);
            report.AppendLine("  " + graphic.name + ": active=" + graphic.isActiveAndEnabled + "; depth=" +
                graphic.depth + "; cull=" + graphic.canvasRenderer.cull + "; target=" + graphic.raycastTarget +
                "; rect=" + graphic.rectTransform.rect + "; hit=" +
                RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, position, camera) +
                "; corner0=" + corners[0] + "; corner2=" + corners[2] + "; owner=" +
                (graphic.canvas != null ? graphic.canvas.name : "none") + "; registeredGraphic=" +
                (graphic.canvas != null && GraphicRegistry.GetRaycastableGraphicsForCanvas(graphic.canvas).Contains(graphic)));
        }
    }

    private static List<RaycastResult> Hits(EventSystem system, Scene preview, Vector2 position)
    {
        var results = new List<RaycastResult>();
        system.RaycastAll(new PointerEventData(system) { position = position }, results);
        return results.Where(hit => hit.module is GraphicRaycaster && hit.gameObject != null &&
            hit.gameObject.scene == preview).ToList();
    }

    private static string Names(IEnumerable<RaycastResult> hits) =>
        string.Join(", ", hits.Select(hit => hit.gameObject.name + " (depth=" + hit.depth + ", order=" + hit.sortingOrder + ")"));

    private static bool Blocked(MobileMovementJoystick joystick, List<RaycastResult> hits)
    {
        MethodInfo predicate = typeof(MobileMovementJoystick).GetMethod("AreStartRaycastsBlocked",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Check(predicate != null, "Actual joystick native-hit routing predicate is not imported yet.");
        // Invoke only the production pure hit classifier. The fixture control
        // stays inactive, so no preference/focus polling, input devices, touch
        // support or EventSystem globals are enabled or changed.
        return (bool)predicate.Invoke(joystick, new object[] { hits });
    }

    private static GameObject Child(string name, Transform parent, bool active, params Type[] components)
    {
        var child = new GameObject(name, components);
        child.SetActive(false);
        child.transform.SetParent(parent, false);
        child.hideFlags = HideFlags.HideAndDontSave;
        child.SetActive(active);
        return child;
    }

    private static Image ImageChild(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var child = Child(name, parent, true, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Stretch((RectTransform)child.transform, min, max);
        Image image = child.GetComponent<Image>();
        image.raycastTarget = true;
        return image;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private sealed class SceneSnapshot
    {
        private readonly bool dirty;
        private readonly string path;
        private readonly int[] roots;
        public SceneSnapshot(Scene scene)
        {
            dirty = scene.isDirty;
            path = scene.path;
            roots = scene.GetRootGameObjects().Select(root => root.GetInstanceID()).ToArray();
        }
        public void Verify(Scene scene) => Check(scene.isDirty == dirty && scene.path == path &&
            scene.GetRootGameObjects().Select(root => root.GetInstanceID()).SequenceEqual(roots),
            "Authored scene roots/path/dirty state changed: " + path);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

/// <summary>Detached fixture sentinel only; receives no events and remains disabled.</summary>
public sealed class MobileJoystickValidationPointerSentinel : MonoBehaviour, IPointerDownHandler
{
    public int calls;
    public void OnPointerDown(PointerEventData eventData) => calls++;
}
#endif
