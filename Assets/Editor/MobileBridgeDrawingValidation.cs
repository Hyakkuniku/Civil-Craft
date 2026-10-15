#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem.EnhancedTouch;

/// <summary>Opt-in checks in an isolated preview scene; never saves an open scene.</summary>
public static class MobileBridgeDrawingValidation
{
    private const string Request = "Temp/mobile-bridge-drawing-validation.request";
    private const string Report = "Temp/mobile-bridge-drawing-validation.txt";
    private const int GeometryLayer = 29;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double nextPoll;
    private static int checks;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        nextPoll = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Build/Validate Touch Drawing")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        checks = 0;
        var report = new StringBuilder();
        var originals = new List<SceneSnapshot>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
            originals.Add(new SceneSnapshot(SceneManager.GetSceneAt(i)));
        Scene active = SceneManager.GetActiveScene();
        Point[] originalPoints = Point.AllPoints.ToArray();
        GameManager originalManager = GameManager.Instance;
        BuildUIController originalBuildUI = BuildUIController.Instance;
        BuildTutorialDirector originalTutorial = BuildTutorialDirector.Instance;
        RenderTexture previous = RenderTexture.active;
        Scene preview = default;
        RenderTexture target = null;
        Material material = null;
        bool passed = false;
        bool touchEnabledForCheck = false;
        try
        {
            EnhancedTouchSupport.Enable();
            touchEnabledForCheck = true;
            Point.AllPoints.Clear();
            preview = EditorSceneManager.NewPreviewScene();
            Camera camera = NewObject(preview, "Detached build camera", true).AddComponent<Camera>();
            camera.scene = preview;
            camera.enabled = true;
            camera.cullingMask = 1 << GeometryLayer;
            camera.useOcclusionCulling = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(82, 100, 94, 255);
            camera.nearClipPlane = .3f;
            camera.farClipPlane = 100f;
            target = new RenderTexture(1280, 720, 24) { hideFlags = HideFlags.HideAndDontSave };
            target.Create();
            camera.targetTexture = target;
            camera.aspect = 1280f / 720f;
            Oblique(camera);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Check(shader != null, "A built-in preview shader exists");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.color = new Color32(237, 205, 134, 255);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color32(237, 205, 134, 255));
            Point point = NewPoint(preview, "Long bridge node", Vector3.zero, material, true);
            Renderer renderer = point.GetComponentInChildren<Renderer>();
            ValidatePolygonDistance();
            ValidateGeometry(camera, point, renderer);
            ValidatePlaneGeometry(camera, point, renderer);
            report.AppendLine("PASS: actual camera projection covers both ends of the long node at an oblique angle, flat view, zoomed-out view and perspective; clipping and render visibility guards passed.");
            report.AppendLine("PASS: destination footprints ignore visual node depth at 35/60-degree orthographic and perspective views; increasing cylinder depth does not widen the endpoint snap region.");

            GameManager manager = NewObject(preview, "Inactive manager fixture", false).AddComponent<GameManager>();
            manager.OnEnterBuildMode = new UnityEvent();
            manager.OnExitBuildMode = new UnityEvent();
            BuildLocation location = NewObject(preview, "Inactive build site", false).AddComponent<BuildLocation>();
            BuildLocation foreign = NewObject(preview, "Inactive foreign site", false).AddComponent<BuildLocation>();
            location.locationCamera = camera;
            point.AssignOwner(location);
            location.startingAnchors.Add(point);
            SetProperty(typeof(GameManager), "Instance", null, manager);
            SetProperty(typeof(GameManager), "CurrentState", manager, GameManager.GameState.Building);
            SetProperty(typeof(GameManager), "ActiveBuildLocation", manager, location);
            SetProperty(typeof(BuildUIController), "Instance", null, null);
            SetProperty(typeof(BuildTutorialDirector), "Instance", null, null);
            BarCreator creator = NewObject(preview, "Inactive builder fixture", false).AddComponent<BarCreator>();
            ValidatePointAndPicker(preview, camera, point, renderer, creator, location, foreign, material);
            ValidateGesture(camera, point, creator);
            ValidateLogicalAim(preview, camera, point, creator, location, material);
            ValidateEndpointRules(preview, point, creator, material);
            ValidateMagnifierDisable(preview, creator);
            report.AppendLine("PASS: actual Point renderer cache and drawing picker preserve owner/depth/start/ghost/visibility filters, whole-mesh starting priority and list-order-independent ties. Destination picking follows only the logical endpoint, not a second raw-finger target. Whole-mesh grab offset, drag threshold, coherent aim and mismatched pointer release passed.");

            passed = true;
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            Debug.LogError("[MobileBridgeDrawingValidation] " + error);
        }
        finally
        {
            RenderTexture.active = previous;
            if (preview.IsValid())
                foreach (GameObject root in preview.GetRootGameObjects())
                    foreach (Camera detachedCamera in root.GetComponentsInChildren<Camera>(true))
                        detachedCamera.targetTexture = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            SetProperty(typeof(GameManager), "Instance", null, originalManager);
            SetProperty(typeof(BuildUIController), "Instance", null, originalBuildUI);
            SetProperty(typeof(BuildTutorialDirector), "Instance", null, originalTutorial);
            Point.AllPoints.Clear();
            Point.AllPoints.AddRange(originalPoints);
            if (touchEnabledForCheck) EnhancedTouchSupport.Disable();
            bool intact = SceneManager.GetActiveScene() == active;
            foreach (SceneSnapshot original in originals) intact &= original.IsUnchanged();
            passed &= intact;
            report.AppendLine(intact
                ? "PASS: original scene roots, active scene and dirty flags preserved; player saves untouched."
                : "FAIL: original scene setup changed.");
            report.AppendLine((passed ? "PASS" : "FAIL") + ": " + checks + " native checks completed. Android touch/two-finger and tutorial smoke tests remain manual.");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Report, report.ToString());
            if (passed) Debug.Log("[MobileBridgeDrawingValidation] " + checks + " checks passed. Report: " + Report);
        }
    }

    private static void ValidatePolygonDistance()
    {
        var square = new[] { Vector2.zero, new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        Near(BridgePointScreenGeometry.DistanceToConvexPolygon(new Vector2(5, 5), square, 4), 0f, "Polygon interior");
        Near(BridgePointScreenGeometry.DistanceToConvexPolygon(new Vector2(13, 5), square, 4), 3f, "Polygon edge distance");
        Near(BridgePointScreenGeometry.DistanceToConvexPolygon(new Vector2(13, 14), square, 4), 5f, "Polygon corner distance");
        Array.Reverse(square);
        Near(BridgePointScreenGeometry.DistanceToConvexPolygon(new Vector2(5, 5), square, 4), 0f, "Reversed winding");
        Near(BridgePointScreenGeometry.DistanceToConvexPolygon(new Vector2(13, 4), new[] { Vector2.zero, new Vector2(10, 0) }, 2), 5f, "Degenerate segment");
        Near(BridgePointScreenGeometry.DistanceToConvexPolygon(new Vector2(3, 4), new[] { Vector2.zero }, 1), 5f, "Degenerate point");
    }

    private static void ValidateGeometry(Camera camera, Point point, Renderer renderer)
    {
        Vector2 center = ScreenPoint(camera, point.transform.position);
        Vector3 nearEnd = renderer.transform.TransformPoint(Vector3.up * -.9f);
        Vector3 farEnd = renderer.transform.TransformPoint(Vector3.up * .9f);
        foreach (Vector3 end in new[] { nearEnd, farEnd })
        {
            Vector2 screen = ScreenPoint(camera, end);
            Check(Vector2.Distance(screen, center) > 20f, "Oblique end differs from root center");
            Check(BridgePointScreenGeometry.TryGetDistance(camera, renderer, screen, out float distance), "Whole node end projects");
            Near(distance, 0f, "Whole node end is inside footprint");
        }
        Vector2 outside = center + Vector2.up * 80f;
        Check(BridgePointScreenGeometry.TryGetDistance(camera, renderer, outside, out float outsideDistance) && outsideDistance > 28f,
            "Outside footprint stays outside touch padding");
        renderer.enabled = false;
        Check(!BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out _), "Disabled renderer excluded");
        renderer.enabled = true;
        renderer.forceRenderingOff = true;
        Check(!BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out _), "Force-off renderer excluded");
        renderer.forceRenderingOff = false;
        camera.cullingMask = 0;
        Check(!BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out _), "Masked renderer excluded");
        camera.cullingMask = 1 << GeometryLayer;
        Check(!BridgePointScreenGeometry.TryGetDistance(camera, renderer, new Vector2(-10, -10), out _), "Pointer outside camera excluded");
        renderer.gameObject.SetActive(false);
        Check(!BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out _), "Inactive mesh excluded");
        renderer.gameObject.SetActive(true);
        camera.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity);
        camera.orthographicSize = 15f;
        center = ScreenPoint(camera, point.transform.position);
        Check(BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out float flat) && flat == 0f, "Flat view");
        camera.orthographicSize = 100f;
        Check(BridgePointScreenGeometry.TryGetDistance(camera, renderer, center + Vector2.right * 20f, out float zoom) && zoom < 28f,
            "Zoom-independent touch margin");
        camera.orthographic = false;
        camera.fieldOfView = 60f;
        Check(BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out float perspective) && perspective == 0f, "Perspective view");
        camera.nearClipPlane = 2f;
        point.transform.position = camera.transform.position + camera.transform.forward;
        Check(BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out float nearClip) && IsFinite(nearClip), "Near-plane crossing is finite");
        camera.farClipPlane = 30f;
        point.transform.position = camera.transform.position + camera.transform.forward * 29f;
        Check(BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out float farClip) && IsFinite(farClip), "Far-plane crossing is finite");
        point.transform.position = camera.transform.position - camera.transform.forward * 10f;
        Check(!BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out _), "Fully behind near plane excluded");
        point.transform.position = camera.transform.position + camera.transform.forward * 40f;
        Check(!BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out _), "Fully beyond far plane excluded");
        point.transform.position = camera.transform.position + camera.transform.forward * 10f + camera.transform.right * 1000f;
        Check(!BridgePointScreenGeometry.TryGetDistance(camera, renderer, center, out _), "Fully offscreen bounds excluded");
        point.transform.position = Vector3.zero;
        camera.nearClipPlane = .3f;
        camera.farClipPlane = 100f;
        Oblique(camera);
    }

    private static void ValidatePointAndPicker(Scene preview, Camera camera, Point point, Renderer renderer,
        BarCreator creator, BuildLocation location, BuildLocation foreign, Material material)
    {
        Point.AllPoints.Clear();
        Point.AllPoints.Add(point);
        Vector2 touch = ScreenPoint(camera, renderer.transform.TransformPoint(Vector3.up * .9f));
        object[] pointArgs = { camera, touch, 0f };
        Check((bool)Invoke(point, "TryGetDrawingScreenDistance", pointArgs) && (float)pointArgs[2] == 0f, "Point renderer cache covers deep mesh");
        Check(Pick(creator, touch) == point, "Drawing starts from full visible node");
        creator.currentStartPoint = point;
        Check(Pick(creator, touch) == null, "Current start excluded");
        creator.currentStartPoint = null;
        creator.currentEndPoint = point;
        Check(Pick(creator, touch) == null, "Current ghost excluded");
        creator.currentEndPoint = null;
        point.AssignOwner(foreign, true);
        Check(Pick(creator, touch) == null, "Foreign build owner excluded");
        point.AssignOwner(location, true);
        renderer.enabled = false;
        Check(Pick(creator, touch) == null, "Hidden mesh excluded by picker");
        renderer.enabled = true;

        Vector3 secondaryPosition = creator.GetWorldMousePosition(touch + Vector2.right * 22f);
        Point secondary = NewPoint(preview, "Fuzzy neighbour", secondaryPosition, material, false);
        secondary.AssignOwner(location);
        Check(BridgePointScreenGeometry.TryGetDistance(camera, secondary.GetComponentInChildren<Renderer>(), touch,
            out float neighbourDistance) && neighbourDistance > 0f && neighbourDistance < 12f,
            "Neighbour fixture lies outside its mesh but within touch padding");
        Check(Pick(creator, touch) == point, "Direct deep-mesh hit outranks a fuzzy neighbour");
        Point.AllPoints.Clear();
        Point.AllPoints.Add(secondary);
        Vector2 secondaryTouch = ScreenPoint(camera, secondaryPosition);
        secondary.transform.position += new Vector3(0, 0, creator.nodeSnapDepthTolerance + 2f);
        Check(Pick(creator, ScreenPoint(camera, secondary.transform.position)) == null, "Other bridge depth excluded");
        secondary.transform.position = secondaryPosition;

        Point duplicate = NewPoint(preview, "Identical overlap", point.transform.position, material, true);
        duplicate.AssignOwner(location);
        Point.AllPoints.Clear();
        Point.AllPoints.Add(point);
        Point.AllPoints.Add(duplicate);
        Point first = Pick(creator, touch);
        Point.AllPoints.Reverse();
        Check(first != null && Pick(creator, touch) == first, "Overlapping targets do not depend on list order");
        Point.AllPoints.Clear();
        Point.AllPoints.Add(point);
        duplicate.gameObject.SetActive(false);
        secondary.gameObject.SetActive(false);

        float pixelScale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 720f, .8f, 1.75f);
        Vector2 eightPixelGap = FindScreenGap(camera, renderer, 8f * pixelScale);
        Set(creator, "barCreationStarted", false);
        Check(Pick(creator, eightPixelGap) == point, "Starting a drag retains a modest touch margin");
        Set(creator, "barCreationStarted", true);
        Check(Pick(creator, eightPixelGap) == null, "Drawing endpoint ignores a node eight pixels beyond its mesh");
        Check(Pick(creator, touch) == null, "Slanted deep-mesh side cannot pull the destination to a distant logical joint");
        Vector2 logicalCenter = ScreenPoint(camera, point.transform.position);
        object[] endpointArgs = { camera, logicalCenter, 0f };
        Check((bool)Invoke(point, "TryGetDrawingEndpointScreenDistance", endpointArgs) &&
            (float)endpointArgs[2] < .01f, "Point endpoint cache covers its logical bridge-plane footprint");
        Check(Pick(creator, logicalCenter) == point, "Drawing endpoint still joins the logical joint");
        Vector2 smallPlaneGap = FindScreenGap(camera, renderer, 3f * pixelScale, true, point.transform.position.z);
        Check(Pick(creator, smallPlaneGap) == point, "Logical destination retains a small touch margin");
        Vector2 largePlaneGap = FindScreenGap(camera, renderer, 8f * pixelScale, true, point.transform.position.z);
        Check(Pick(creator, largePlaneGap) == null, "Logical destination rejects the larger eight-pixel gap");
        Set(creator, "barCreationStarted", false);
    }

    private static void ValidatePlaneGeometry(Camera camera, Point point, Renderer renderer)
    {
        Vector3 originalScale = renderer.transform.localScale;
        float planeZ = point.transform.position.z;
        try
        {
            foreach (bool perspective in new[] { false, true })
            {
                camera.orthographic = !perspective;
                camera.orthographicSize = 15f;
                camera.fieldOfView = 60f;
                foreach (float angle in new[] { 35f, 60f })
                {
                    float radians = angle * Mathf.Deg2Rad;
                    camera.transform.position = new Vector3(Mathf.Sin(radians) * 30f, 0f, -Mathf.Cos(radians) * 30f);
                    camera.transform.LookAt(point.transform.position);
                    Vector2 center = ScreenPoint(camera, point.transform.position);
                    Check(BridgePointScreenGeometry.TryGetPlaneDistance(camera, renderer, center, planeZ, out float centerGap) &&
                        centerGap < .01f, "Logical destination center remains selectable at slanted camera angles");
                    Vector2 visualSide = ScreenPoint(camera, renderer.transform.TransformPoint(Vector3.up * .9f));
                    Check(BridgePointScreenGeometry.TryGetDistance(camera, renderer, visualSide, out float visualGap) &&
                        visualGap < .01f, "Slanted visual side remains available for starting a drag");
                    Check(BridgePointScreenGeometry.TryGetPlaneDistance(camera, renderer, visualSide, planeZ, out float sideGap) &&
                        sideGap > 12f, "Visual depth does not extend the destination footprint at slanted angles");

                    Vector2 probe = center + Vector2.right * 50f;
                    Check(BridgePointScreenGeometry.TryGetPlaneDistance(camera, renderer, probe, planeZ, out float originalGap),
                        "Depth-independent endpoint probe projects");
                    renderer.transform.localScale = new Vector3(originalScale.x, originalScale.y * 4f, originalScale.z);
                    Check(BridgePointScreenGeometry.TryGetPlaneDistance(camera, renderer, probe, planeZ, out float deeperGap),
                        "Longer visual depth endpoint probe projects");
                    Near(deeperGap, originalGap, "Increasing node depth cannot make destination snapping more aggressive");
                    renderer.transform.localScale = originalScale;
                }
            }
            Oblique(camera);
            Vector2 logical = ScreenPoint(camera, point.transform.position);
            renderer.enabled = false;
            Check(!BridgePointScreenGeometry.TryGetPlaneDistance(camera, renderer, logical, planeZ, out _),
                "Plane footprint excludes hidden renderers");
            renderer.enabled = true;
            camera.cullingMask = 0;
            Check(!BridgePointScreenGeometry.TryGetPlaneDistance(camera, renderer, logical, planeZ, out _),
                "Plane footprint excludes masked renderers");
            camera.cullingMask = 1 << GeometryLayer;
        }
        finally
        {
            renderer.transform.localScale = originalScale;
            renderer.enabled = true;
            camera.cullingMask = 1 << GeometryLayer;
            Oblique(camera);
        }
    }

    private static void ValidateGesture(Camera camera, Point point, BarCreator creator)
    {
        Renderer renderer = point.GetComponentInChildren<Renderer>();
        Vector2 touch = ScreenPoint(camera, renderer.transform.TransformPoint(Vector3.up * .9f));
        var down = new PointerEventData(null) { pointerId = 77, button = PointerEventData.InputButton.Left, position = touch };
        Invoke(creator, "CaptureDrawingPointer", down, point.transform.position, true);
        Check(Field<bool>(creator, "drawingPointerCaptured"), "Drawing gesture captures its pointer");
        Check(!Property<bool>(creator, "DrawingGestureCanPlace"), "Stationary mesh grab cannot place");
        Vector2 aim = (Vector2)Invoke(creator, "GetDrawingAimPosition", touch);
        Check(Vector2.Distance(aim, ScreenPoint(camera, point.transform.position)) < .01f, "Mesh-side grab preserves logical start");
        Invoke(creator, "UpdateDrawingGesture", touch + Vector2.one);
        Check(!Property<bool>(creator, "DrawingGestureCanPlace"), "Tiny finger jitter cannot place");
        Vector2 release = touch + new Vector2(100, 50);
        Invoke(creator, "UpdateDrawingGesture", release);
        Check(Property<bool>(creator, "DrawingGestureCanPlace"), "Deliberate drag passes threshold");
        Vector2 releaseAim = (Vector2)Invoke(creator, "GetDrawingAimPosition", release);
        Check(Vector2.Distance(releaseAim - aim, release - touch) < .01f, "Release and preview use coherent grab offset");
        creator.OnPointerUp(new PointerEventData(null) { pointerId = 78, button = PointerEventData.InputButton.Left, position = release });
        Check(Field<bool>(creator, "drawingPointerCaptured"), "Another pointer cannot release captured drawing");
        Invoke(creator, "ResetDrawingPointer");
        Check(!Field<bool>(creator, "drawingPointerCaptured"), "Drawing reset releases capture");
    }

    private static void ValidateLogicalAim(Scene preview, Camera camera, Point start, BarCreator creator,
        BuildLocation location, Material material)
    {
        Renderer startRenderer = start.GetComponentInChildren<Renderer>();
        Vector2 grab = ScreenPoint(camera, startRenderer.transform.TransformPoint(Vector3.up * .9f));
        Point destination = NewPoint(preview, "Single logical target fixture", new Vector3(5, 2, 0), material, true);
        destination.AssignOwner(location);
        Renderer destinationRenderer = destination.GetComponentInChildren<Renderer>();
        try
        {
            Point.AllPoints.Clear();
            Point.AllPoints.Add(start);
            Point.AllPoints.Add(destination);
            creator.currentStartPoint = start;
            Set(creator, "barCreationStarted", true);
            Invoke(creator, "CaptureDrawingPointer", new PointerEventData(null)
                { pointerId = 79, button = PointerEventData.InputButton.Left, position = grab }, start.transform.position, true);
            Vector2 rawSide = ScreenPoint(camera, destinationRenderer.transform.TransformPoint(Vector3.down * .9f));
            Invoke(creator, "UpdateDrawingGesture", rawSide);
            Vector2 distantAim = (Vector2)Invoke(creator, "GetDrawingAimPosition", rawSide);
            Check(BridgePointScreenGeometry.TryGetDistance(camera, destinationRenderer, rawSide, out float rawGap) && rawGap < .01f,
                "Dual-target regression places the raw finger directly on the destination's deep visual mesh");
            Check(BridgePointScreenGeometry.TryGetPlaneDistance(camera, destinationRenderer, distantAim,
                destination.transform.position.z, out float aimGap) && aimGap > 12f,
                "Dual-target regression keeps the actual logical endpoint outside the destination region");
            Check(Pick(creator, distantAim) == null,
                "Raw finger cannot override a distant logical endpoint and snap a second target");

            Vector2 logicalCenter = ScreenPoint(camera, destination.transform.position);
            Vector2 alignedFinger = logicalCenter - Field<Vector2>(creator, "drawingScreenOffset");
            Invoke(creator, "UpdateDrawingGesture", alignedFinger);
            Vector2 alignedAim = (Vector2)Invoke(creator, "GetDrawingAimPosition", alignedFinger);
            Check(Vector2.Distance(alignedFinger, logicalCenter) > 20f,
                "Logical-target regression keeps the finger offset from the joint center");
            Check(Vector2.Distance(alignedAim, logicalCenter) < .01f && Pick(creator, alignedAim) == destination,
                "Offset whole-mesh grab connects when its logical endpoint reaches the next joint");
        }
        finally
        {
            Invoke(creator, "ResetDrawingPointer");
            Set(creator, "barCreationStarted", false);
            creator.currentStartPoint = null;
            destination.gameObject.SetActive(false);
            Point.AllPoints.Clear();
            Point.AllPoints.Add(start);
        }
    }

    private static void ValidateEndpointRules(Scene preview, Point start, BarCreator creator, Material visual)
    {
        Point.AllPoints.Clear();
        Point.AllPoints.Add(start);
        start.transform.position = Vector3.zero;
        creator.currentStartPoint = start;
        creator.currentBar = NewObject(preview, "Inactive preview member", false).AddComponent<Bar>();
        creator.currentBar.StartPosition = Vector3.zero;
        creator.isGridSnappingEnabled = false;
        var data = ScriptableObject.CreateInstance<BridgeMaterialSO>();
        data.maxLength = 10f;
        data.costPerMeter = 1f;
        creator.activeMaterial = data;
        try
        {
            Point resolvedNode = null;
            Vector3 resolved = Resolve(creator, new Vector3(20, 0, 0), ref resolvedNode, out _, out _);
            Near(resolved.x, 10f, "Preview resolver enforces maximum length");
            Check(resolvedNode == null, "Free endpoint remains free");

            Point destination = NewPoint(preview, "Join tolerance fixture", new Vector3(10.1f, 0, 0), visual, false);
            resolvedNode = destination;
            resolved = Resolve(creator, destination.transform.position, ref resolvedNode, out _, out _);
            Check(resolvedNode == destination && Vector3.Distance(resolved, destination.transform.position) < .01f,
                "Preview matches release's existing-node length tolerance");
            destination.transform.position = new Vector3(10.3f, 0, 0);
            resolvedNode = destination;
            resolved = Resolve(creator, destination.transform.position, ref resolvedNode, out _, out _);
            Check(resolvedNode == null && Mathf.Abs(resolved.x - 10f) < .01f, "Beyond node length tolerance remains clamped");
            destination.transform.position = new Vector3(9.5f, 0, 0);
            resolvedNode = null;
            resolved = Resolve(creator, new Vector3(20, 0, 0), ref resolvedNode, out _, out _);
            Check(resolvedNode == null && Mathf.Abs(resolved.x - 10f) < .01f,
                "Constrained endpoint does not jump half a metre to a node");
            destination.transform.position = new Vector3(9.9f, 0, 0);
            resolvedNode = null;
            resolved = Resolve(creator, new Vector3(20, 0, 0), ref resolvedNode, out _, out _);
            Check(resolvedNode == destination && Vector3.Distance(resolved, destination.transform.position) < .01f,
                "Constrained endpoint still connects a genuinely nearby node");
            destination.gameObject.SetActive(false);

            BuildUIController budget = NewObject(preview, "Inactive budget fixture", false).AddComponent<BuildUIController>();
            budget.maxBudget = 4f;
            Set(budget, "cachedBaseCost", 1f);
            SetProperty(typeof(BuildUIController), "Instance", null, budget);
            resolvedNode = null;
            resolved = Resolve(creator, new Vector3(20, 0, 0), ref resolvedNode, out _, out _);
            Near(resolved.x, 3f, "Remaining budget limits preview endpoint");
            SetProperty(typeof(BuildUIController), "Instance", null, null);

            Point a = NewPoint(preview, "Interior A", new Vector3(5, -3, 0), visual, false);
            Point b = NewPoint(preview, "Interior B", new Vector3(5, 3, 0), visual, false);
            Bar member = NewObject(preview, "Existing interior member", false).AddComponent<Bar>();
            member.materialData = data;
            member.startPoint = a;
            member.endPoint = b;
            // Bar's normal Play Mode OnEnable owns these connections; this
            // detached Edit Mode fixture never enters Play Mode.
            a.ConnectedBars.Add(member);
            b.ConnectedBars.Add(member);
            member.gameObject.SetActive(true);
            resolvedNode = null;
            resolved = Resolve(creator, new Vector3(5.45f, 1, 0), ref resolvedNode, out Bar split, out _);
            Check(split == member && Mathf.Abs(resolved.x - 5f) < .01f && Mathf.Abs(resolved.y - 1f) < .01f,
                "Bar interior snapping matches release at 0.45 metres");
            member.gameObject.SetActive(false);
            a.gameObject.SetActive(false);
            b.gameObject.SetActive(false);

            creator.isGridSnappingEnabled = true;
            start.transform.position = new Vector3(232.4727f, 0, 0);
            creator.currentBar.StartPosition = start.transform.position;
            resolvedNode = null;
            resolved = Resolve(creator, new Vector3(242.6f, 0, 0), ref resolvedNode, out _, out _);
            Near(resolved.x, 242.4727f, "Grid retains the current node's fractional origin");
        }
        finally
        {
            SetProperty(typeof(BuildUIController), "Instance", null, null);
            creator.currentStartPoint = null;
            creator.currentBar = null;
            creator.activeMaterial = null;
            start.transform.position = Vector3.zero;
            UnityEngine.Object.DestroyImmediate(data);
        }
    }

    private static Vector3 Resolve(BarCreator creator, Vector3 raw, ref Point node, out Bar split, out bool terrain)
    {
        MethodInfo method = typeof(BarCreator).GetMethod("ResolveDrawingEndpoint", PrivateInstance, null,
            new[] { typeof(Vector3), typeof(Point).MakeByRefType(), typeof(Vector3), typeof(Vector3).MakeByRefType(),
                typeof(Bar).MakeByRefType(), typeof(bool).MakeByRefType() }, null);
        object[] args = { raw, node, node != null ? node.transform.position : Vector3.zero, Vector3.zero, null, false };
        Vector3 result = (Vector3)method.Invoke(creator, args);
        node = (Point)args[1];
        split = (Bar)args[4];
        terrain = (bool)args[5];
        return result;
    }

    private static void ValidateMagnifierDisable(Scene preview, BarCreator creator)
    {
        var owner = NewObject(preview, "Inactive magnifier fixture", false).AddComponent<MagnifyingGlassController>();
        GameObject imageObject = new GameObject("Detached magnifier image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        imageObject.transform.SetParent(owner.transform, false);
        RawImage image = imageObject.GetComponent<RawImage>();
        image.rectTransform.sizeDelta = new Vector2(220, 220);
        Set(owner, "magnifierImage", image);
        Set(owner, "barCreator", creator);
        Camera camera = NewObject(preview, "Detached hidden magnifier camera", true).AddComponent<Camera>();
        camera.enabled = true;
        Set(owner, "magnifierCamera", camera);
        Invoke(owner, "InitializeReferences");
        Check(!camera.enabled, "Initializing a hidden magnifier keeps its camera disabled");
        Check(!image.raycastTarget, "Magnifier remains click-through");
        Check(image.transform.childCount == 0, "Magnifier adds no crosshair graphics");
        camera.enabled = true;
        Set(owner, "isVisible", true);
        Invoke(owner, "OnDisable");
        Check(!camera.enabled && !owner.IsVisible, "Disabling magnifier stops its camera");
    }

    private static Vector2 FindScreenGap(Camera camera, Renderer renderer, float gap, bool planeFootprint = false, float planeZ = 0f)
    {
        Vector2 center = ScreenPoint(camera, renderer.bounds.center);
        float low = 0f, high = 256f;
        for (int i = 0; i < 24; i++)
        {
            float candidate = (low + high) * .5f;
            float distance;
            bool projected = planeFootprint
                ? BridgePointScreenGeometry.TryGetPlaneDistance(camera, renderer, center + Vector2.right * candidate, planeZ, out distance)
                : BridgePointScreenGeometry.TryGetDistance(camera, renderer, center + Vector2.right * candidate, out distance);
            Check(projected,
                "Touch-margin fixture remains within the camera viewport");
            if (distance < gap) low = candidate;
            else high = candidate;
        }
        return center + Vector2.right * ((low + high) * .5f);
    }

    private static Point NewPoint(Scene scene, string name, Vector3 position, Material material, bool longNode)
    {
        GameObject root = NewObject(scene, name, false);
        root.transform.position = position;
        root.transform.localScale = longNode ? new Vector3(1, 1, 1.3938f) : Vector3.one;
        GameObject mesh = GameObject.CreatePrimitive(longNode ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
        mesh.hideFlags = HideFlags.HideAndDontSave;
        mesh.layer = GeometryLayer;
        mesh.transform.SetParent(root.transform, false);
        mesh.transform.localRotation = longNode ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
        mesh.transform.localScale = longNode ? new Vector3(1, 3.5422f, 1) : Vector3.one;
        UnityEngine.Object.DestroyImmediate(mesh.GetComponent<Collider>());
        mesh.GetComponent<Renderer>().sharedMaterial = material;
        Point point = root.AddComponent<Point>();
        root.SetActive(true);
        return point;
    }

    private static GameObject NewObject(Scene scene, string name, bool active)
    {
        var gameObject = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave, layer = GeometryLayer };
        gameObject.SetActive(active);
        SceneManager.MoveGameObjectToScene(gameObject, scene);
        return gameObject;
    }

    private static void Oblique(Camera camera)
    {
        camera.orthographic = true;
        camera.orthographicSize = 15f;
        camera.transform.position = new Vector3(12, 10, -24);
        camera.transform.LookAt(Vector3.zero);
    }

    private static Point Pick(BarCreator creator, Vector2 position)
    {
        object[] args = { position, null, Vector3.zero };
        bool picked = (bool)Invoke(creator, "CheckForDrawingPoint", args);
        if (picked) Check((Vector3)args[2] == ((Point)args[1]).transform.position, "Picker returns logical node center");
        return picked ? (Point)args[1] : null;
    }

    private static Vector2 ScreenPoint(Camera camera, Vector3 position)
    {
        Vector3 screen = camera.WorldToScreenPoint(position);
        return new Vector2(screen.x, screen.y);
    }

    private static object Invoke(object owner, string name, params object[] arguments) =>
        owner.GetType().GetMethod(name, PrivateInstance).Invoke(owner, arguments);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);
    private static T Property<T>(object owner, string name) => (T)owner.GetType().GetProperty(name, PrivateInstance).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);
    private static void SetProperty(Type type, string name, object owner, object value) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).SetValue(owner, value);
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .01f, message);
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class SceneSnapshot
    {
        private readonly Scene scene;
        private readonly bool dirty;
        private readonly HashSet<int> roots = new HashSet<int>();
        public SceneSnapshot(Scene scene)
        {
            this.scene = scene;
            dirty = scene.isDirty;
            foreach (GameObject root in scene.GetRootGameObjects()) roots.Add(root.GetInstanceID());
        }
        public bool IsUnchanged()
        {
            if (!scene.IsValid() || scene.isDirty != dirty || scene.rootCount != roots.Count) return false;
            foreach (GameObject root in scene.GetRootGameObjects()) if (!roots.Contains(root.GetInstanceID())) return false;
            return true;
        }
    }
}
#endif
