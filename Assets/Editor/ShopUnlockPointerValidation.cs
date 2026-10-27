#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Detached exact Shop arrow cloning and HUD geometry; never unlocks or opens a live shop.</summary>
[InitializeOnLoad]
public static class ShopUnlockPointerValidation
{
    public const string RequestPath = "Temp/shop-unlock-pointer.request";
    public const string ReportPath = "Logs/shop-unlock-pointer.txt";
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static bool running;
    private static double nextPoll;

    static ShopUnlockPointerValidation() { EditorApplication.update += Poll; }
    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;
    private static void Poll()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath); Run();
    }

    [MenuItem("Tools/Civil Craft/Shop/Validate Unlock HUD Pointer (Read Only)")]
    public static void Run()
    {
        if (Busy) return;
        running = true;
        var report = new StringBuilder("Shop unlock pointer: actual inactive-template Instantiate clone and HUD art\n");
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        GameObject[][] roots = scenes.Select(scene => scene.GetRootGameObjects()).ToArray();
        var hashes = scenes.Where(scene => File.Exists(scene.path)).ToDictionary(scene => scene.path, scene => Hash(scene.path));
        Scene active = SceneManager.GetActiveScene(); Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        float timeScale = Time.timeScale;
        PlayerDataManager save = PlayerDataManager.Instance;
        PlayerData player = save != null ? save.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null;
        try
        {
            ShopButtonTrigger[] triggers = Resources.FindObjectsOfTypeAll<ShopButtonTrigger>().Where(item =>
                item != null && item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(item.gameObject.scene)).ToArray();
            Require(triggers.Length > 0, "Open a gameplay scene containing its authored Shop_btn first.");
            var originals = Resources.FindObjectsOfTypeAll<TutorialPointer>().Where(item => item != null &&
                item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(item.gameObject.scene)).ToDictionary(item => item, item => new {
                    json = EditorJsonUtility.ToJson(item), rect = EditorJsonUtility.ToJson(item.transform), active = item.gameObject.activeSelf });
            foreach (ShopButtonTrigger trigger in triggers)
            {
                var properties = new SerializedObject(trigger);
                Button sourceButton = properties.FindProperty("button").objectReferenceValue as Button;
                if (sourceButton == null) sourceButton = trigger.GetComponent<Button>();
                Require(sourceButton != null && sourceButton.name == "Shop_btn", "Missing exact authored Shop_btn binding in " + trigger.gameObject.scene.name);
                TutorialManager manager = trigger.gameObject.scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<TutorialManager>(true)).SingleOrDefault();
                Require(manager != null && manager.SharedPointer != null, "The scene's authored tutorial arrow artwork is missing.");
                string triggerJson = EditorJsonUtility.ToJson(trigger), buttonJson = EditorJsonUtility.ToJson(sourceButton);
                var sourceArt = sourceButton.GetComponentsInChildren<Component>(true).Cast<Object>()
                    .Concat(manager.SharedPointer.GetComponentsInChildren<Component>(true)).Distinct()
                    .ToDictionary(item => item, EditorJsonUtility.ToJson);
                Capture((RectTransform)sourceButton.transform, manager.SharedPointer, report);
                Require(EditorJsonUtility.ToJson(trigger) == triggerJson && EditorJsonUtility.ToJson(sourceButton) == buttonJson &&
                    sourceArt.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value),
                    "An authored Shop binding, art, tutorial pointer or callback changed.");
            }
            Require(originals.All(pair => pair.Key != null && EditorJsonUtility.ToJson(pair.Key) == pair.Value.json &&
                EditorJsonUtility.ToJson(pair.Key.transform) == pair.Value.rect && pair.Key.gameObject.activeSelf == pair.Value.active),
                "The dedicated preview stole or changed an existing tutorial arrow.");
            report.AppendLine("PASS: every existing tutorial arrow retains its source state; the detached Shop pointer owns a separate component/art tree.");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            try
            {
                Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active, "Loaded scene setup changed.");
                for (int index = 0; index < scenes.Length; index++)
                    Require(SceneManager.GetSceneAt(index) == scenes[index] && scenes[index].isDirty == dirty[index] &&
                        scenes[index].GetRootGameObjects().SequenceEqual(roots[index]), "Scene order/roots/dirty flags changed.");
                Require(hashes.All(pair => Hash(pair.Key) == pair.Value), "Saved scene bytes changed.");
                Require(Selection.objects.SequenceEqual(selection) && Selection.activeObject == selected && Time.timeScale == timeScale,
                    "Selection or time scale changed.");
                Require(PlayerDataManager.Instance == save && (player == null ||
                    (ReferenceEquals(player, save.CurrentData) && JsonUtility.ToJson(player) == playerJson)), "Live player data changed.");
                report.AppendLine("PASS: source scenes/saved bytes/dirty flags, selection, time scale and player data unchanged. No ShopButtonTrigger, ShopManager, unlock/save, Button callback or tutorial event was invoked.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
        }
    }

    private static void Capture(RectTransform sourceButton, TutorialPointer sourcePointer, StringBuilder report)
    {
        Canvas sourceCanvas = sourceButton.GetComponentInParent<Canvas>(true);
        Require(sourceCanvas != null, "Shop HUD button has no Canvas.");
        sourceCanvas = sourceCanvas.rootCanvas;
        CanvasScaler scaler = sourceCanvas.GetComponent<CanvasScaler>();
        Vector2 reference = scaler != null ? scaler.referenceResolution : new Vector2(1920, 1080);
        float match = scaler != null ? scaler.matchWidthOrHeight : .5f;
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        try
        {
            var root = new GameObject("Detached Shop unlock HUD", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, preview);
            var parent = (RectTransform)root.transform; parent.localScale = Vector3.one * .01f;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform buttonParent = parent;
            var ancestors = new Stack<RectTransform>();
            for (Transform item = sourceButton.parent; item != null && item != sourceCanvas.transform; item = item.parent)
            {
                Require(item is RectTransform, "A non-UI Shop HUD ancestor needs explicit preview support.");
                ancestors.Push((RectTransform)item);
            }
            while (ancestors.Count > 0) buttonParent = CopyRect(ancestors.Pop(), buttonParent, "Detached HUD ancestor");
            RectTransform button = CopyArt(sourceButton, buttonParent, "Shop_btn (detached art only)");
            Image hitSurface = button.GetComponent<Image>();
            Require(hitSurface != null && hitSurface.sprite != null, "Actual Shop HUD sprite is missing.");
            foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            hitSurface.raycastTarget = true;
            // Use the exact runtime factory, rather than rebuilding Image art
            // and adding a fresh component. An inactive source prevents any
            // unexpected activation while the clone is being inspected.
            Require(!sourcePointer.gameObject.activeSelf, "Leave the authored shared pointer inactive for the clone lifecycle check.");
            Require(sourcePointer.GetComponentsInChildren<MonoBehaviour>(true).All(component =>
                component is TutorialPointer || component is Graphic),
                "The pointer template has an unexpected script; do not clone a live interaction owner in this detached check.");
            TutorialPointer pointer = Object.Instantiate(sourcePointer, canvas.rootCanvas.transform, false);
            pointer.name = "Shop Unlock Pointer (Runtime validation)";
            pointer.Hide();
            RectTransform arrow = (RectTransform)pointer.transform;
            Require(pointer != sourcePointer && arrow != sourcePointer.transform &&
                pointer.GetComponentsInChildren<MonoBehaviour>(true).All(component => component is TutorialPointer || component is Graphic),
                "Exact pointer cloning reused a source object or copied an interaction owner.");
            Require(!pointer.gameObject.activeSelf && !pointer.IsPointingAt(button),
                "A newly cloned and hidden arrow must not retain an old tutorial target.");
            // Unity does not promise Play Mode callbacks for ordinary scripts
            // in an Edit Mode preview. Initialize the real clone's Awake once
            // if necessary; do not replace its component or its serialized art.
            if (typeof(TutorialPointer).GetField("rectTransform", PrivateInstance).GetValue(pointer) == null)
                typeof(TutorialPointer).GetMethod("Awake", PrivateInstance).Invoke(pointer, null);
            Require(pointer.bounceAmount == sourcePointer.bounceAmount,
                "The exact clone did not retain the authored bounce setting.");
            // Freeze only the detached phase while proving its full reserved
            // bounce envelope, not just one lucky animation sample.
            pointer.bounceSpeed = 0f;
            Require(pointer != sourcePointer && button.GetComponent<Button>() == null && button.GetComponent<ShopButtonTrigger>() == null,
                "The detached preview must not copy a live interaction owner.");
            var cameraObject = new GameObject("Detached Shop HUD camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.useOcclusionCulling = false;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.transform.position = new Vector3(0, 0, -20);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(117, 157, 166, 255);
            canvas.worldCamera = camera;
            Vector2Int[] screens = { new Vector2Int(1280, 720), new Vector2Int(1600, 1200), new Vector2Int(2340, 1080) };
            Vector2 initial = Vector2.zero;
            foreach (Vector2Int screen in screens)
            {
                float scale = Mathf.Pow(screen.x / reference.x, 1f - match) * Mathf.Pow(screen.y / reference.y, match);
                parent.sizeDelta = new Vector2(screen.x, screen.y) / scale; canvas.scaleFactor = scale;
                camera.aspect = (float)screen.x / screen.y; camera.orthographicSize = parent.rect.height * .005f;
                var target = new RenderTexture(screen.x, screen.y, 24); target.Create(); camera.targetTexture = target;
                try
                {
                    Canvas.ForceUpdateCanvases();
                    pointer.PointAtBuildTarget(button, new Vector2(110f, 0f), 90f);
                    // Mirror the real activation callback explicitly as well:
                    // the original inactive clone must retain the new target.
                    typeof(TutorialPointer).GetMethod("OnEnable", PrivateInstance).Invoke(pointer, null);
                    typeof(TutorialPointer).GetMethod("LateUpdate", PrivateInstance).Invoke(pointer, null);
                    Canvas.ForceUpdateCanvases();
                    Require(pointer.IsPointingAt(button) && pointer.GetComponent<Canvas>() != null &&
                        pointer.GetComponent<Canvas>().isActiveAndEnabled, "Exact cloned Shop arrow is not visible/targeted after first activation.");
                    Require(pointer.GetComponentsInChildren<Graphic>(true).Any(graphic => graphic.isActiveAndEnabled &&
                        graphic.color.a > .001f && graphic.canvasRenderer.GetAlpha() > .001f),
                        "Exact cloned Shop arrow has no enabled, nontransparent graphic after activation.");
                    Require(pointer.GetComponentsInParent<CanvasGroup>(true).All(group => !group.isActiveAndEnabled || group.alpha > .001f),
                        "A cloned/ancestor CanvasGroup makes the Shop arrow transparent.");
                    Require(pointer.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), "Arrow captures UI clicks.");
                    Rect arrowBounds = Bounds(arrow, camera), buttonBounds = Bounds(button, camera);
                    Require(!arrowBounds.Overlaps(buttonBounds), "Shop arrow overlaps the actual HUD target.");
                    float radians = arrow.eulerAngles.z * Mathf.Deg2Rad;
                    Vector2 direction = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
                    Require(Vector2.Dot((buttonBounds.center - arrowBounds.center).normalized, direction) > .95f,
                        "Shop arrow does not point toward its target.");
                    Vector2 bounce = new Vector2(Mathf.Abs(direction.x), Mathf.Abs(direction.y)) * pointer.bounceAmount * canvas.scaleFactor;
                    Rect envelope = Rect.MinMaxRect(arrowBounds.xMin - bounce.x, arrowBounds.yMin - bounce.y,
                        arrowBounds.xMax + bounce.x, arrowBounds.yMax + bounce.y);
                    Require(Contains(camera.pixelRect, envelope), "Shop arrow can leave the viewport during its bounce.");
                    Vector2 before = arrow.anchoredPosition;
                    pointer.PointAtBuildTarget(button, new Vector2(110f, 0f), 90f);
                    Require(Vector2.Distance(before, arrow.anchoredPosition) < .01f, "Repeated Shop pointer refresh accumulates offsets.");
                    if (screen == screens[0]) initial = arrow.anchoredPosition;
                    Render(camera, target);
                    var hits = new List<RaycastResult>();
                    root.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null) {
                        position = RectTransformUtility.WorldToScreenPoint(camera, button.TransformPoint(button.rect.center)) }, hits);
                    Require(hits.Any(hit => hit.gameObject.transform == button || hit.gameObject.transform.IsChildOf(button)) &&
                        !hits.Any(hit => hit.gameObject.transform == arrow || hit.gameObject.transform.IsChildOf(arrow)),
                        "Native Shop HUD raycast is blocked by its arrow.");
                    string path = "Temp/ShopUnlockPointer/" + sourceButton.gameObject.scene.name + "-Shop-" + screen.x + "x" + screen.y + ".png";
                    CapturePng(target, path);
                    pointer.Hide();
                    Require(!pointer.gameObject.activeSelf && !pointer.IsPointingAt(button), "Hidden Shop arrow retained its target.");
                    pointer.PointAtBuildTarget(button, new Vector2(110f, 0f), 90f);
                    button.gameObject.SetActive(false);
                    typeof(TutorialPointer).GetMethod("LateUpdate", PrivateInstance).Invoke(pointer, null);
                    Require(!pointer.gameObject.activeSelf, "A hidden HUD target leaves a floating Shop arrow.");
                    button.gameObject.SetActive(true);
                    report.AppendLine("PASS: " + sourceButton.gameObject.scene.name + " " + screen.x + "x" + screen.y +
                        "; exact inactive-template Instantiate clone retains visible art and activates toward Shop without overlap, full bounce envelope in bounds, native click-through, repeat stability and hidden-target cleanup. " + path);
                }
                finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
            }
            Vector2Int first = screens[0];
            float firstScale = Mathf.Pow(first.x / reference.x, 1f - match) * Mathf.Pow(first.y / reference.y, match);
            parent.sizeDelta = new Vector2(first.x, first.y) / firstScale; canvas.scaleFactor = firstScale;
            camera.aspect = (float)first.x / first.y; camera.orthographicSize = parent.rect.height * .005f;
            var roundTrip = new RenderTexture(first.x, first.y, 16);
            try
            {
                camera.targetTexture = roundTrip; Canvas.ForceUpdateCanvases();
                pointer.PointAtBuildTarget(button, new Vector2(110f, 0f), 90f);
                Require(Vector2.Distance(initial, arrow.anchoredPosition) < .01f, "Resize round trip changed Shop pointer placement.");
                pointer.Hide();
            }
            finally { camera.targetTexture = null; roundTrip.Release(); Object.DestroyImmediate(roundTrip); }
            report.AppendLine("PASS: " + sourceButton.gameObject.scene.name + " phone/tablet/wide resize round trip preserves the original Shop pointer placement.");
        }
        finally { RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static RectTransform CopyRect(RectTransform source, Transform parent, string name)
    {
        var owner = new GameObject(name, typeof(RectTransform)); owner.transform.SetParent(parent, false);
        var rect = (RectTransform)owner.transform; rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot; rect.sizeDelta = source.sizeDelta; rect.anchoredPosition = source.anchoredPosition;
        rect.localScale = source.localScale; rect.localRotation = source.localRotation; return rect;
    }
    private static RectTransform CopyArt(RectTransform source, Transform parent, string name)
    {
        RectTransform rect = CopyRect(source, parent, name);
        Image original = source.GetComponent<Image>();
        if (original != null)
        {
            Image image = rect.gameObject.AddComponent<Image>(); image.sprite = original.sprite; image.color = original.color;
            image.type = original.type; image.preserveAspect = original.preserveAspect; image.raycastTarget = original.raycastTarget;
            image.pixelsPerUnitMultiplier = original.pixelsPerUnitMultiplier;
        }
        foreach (Transform child in source)
            if (child is RectTransform childRect && child.gameObject.activeSelf) CopyArt(childRect, rect, child.name);
        rect.gameObject.SetActive(true); return rect;
    }
    private static Rect Bounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
        foreach (Vector3 corner in corners)
        { Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .5f && inner.yMin >= outer.yMin - .5f &&
        inner.xMax <= outer.xMax + .5f && inner.yMax <= outer.yMax + .5f;
    private static void Render(Camera camera, RenderTexture target)
    {
        Canvas.ForceUpdateCanvases();
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
    }
    private static void CapturePng(RenderTexture target, string path)
    {
        RenderTexture old = RenderTexture.active; var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); Directory.CreateDirectory("Temp/ShopUnlockPointer"); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
        finally { RenderTexture.active = old; Object.DestroyImmediate(pixels); }
    }
    private static string Hash(string path) { using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
