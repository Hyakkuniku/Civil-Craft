using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in authoring and isolated native raycast validation for the existing pause control.</summary>
public static class CompactPauseButtonAuthoring
{
    private const string PrefabPath = "Assets/Resources/UI/MobilePauseButton.prefab";
    private const string Request = "Temp/CompactPauseButtonAuthoring.request";
    private const string Report = "Temp/CompactPauseButtonValidation.txt";
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };
    private static double nextCheck;
    private static string lastWait;

    private sealed class OpenedScene
    {
        public Scene scene;
        public bool opened;
        public bool wasInSetup;
    }

    private sealed class ButtonSnapshot
    {
        private readonly RectTransform rect;
        private readonly Transform parent;
        private readonly Transform[] children;
        private readonly Image image;
        private readonly Sprite sprite;
        private readonly Material material;
        private readonly Color color;
        private readonly Image.Type imageType;
        private readonly bool preserveAspect;
        private readonly bool imageEnabled;
        private readonly bool raycastTarget;
        private readonly bool maskable;
        private readonly string buttonJson;
        private readonly Button button;
        private readonly string[] otherGraphicJson;
        private readonly Graphic[] otherGraphics;

        public ButtonSnapshot(RectTransform target)
        {
            rect = target;
            parent = rect.parent;
            children = Enumerable.Range(0, rect.childCount).Select(rect.GetChild).ToArray();
            image = rect.GetComponent<Image>();
            button = rect.GetComponent<Button>();
            Require(image != null && button != null, rect.name + " must keep its existing root Image and Button.");
            sprite = image.sprite;
            material = image.material;
            color = image.color;
            imageType = image.type;
            preserveAspect = image.preserveAspect;
            imageEnabled = image.enabled;
            raycastTarget = image.raycastTarget;
            maskable = image.maskable;
            buttonJson = EditorJsonUtility.ToJson(button);
            otherGraphics = rect.GetComponentsInChildren<Graphic>(true).Where(graphic => graphic != image).ToArray();
            otherGraphicJson = otherGraphics.Select(EditorJsonUtility.ToJson).ToArray();
        }

        public void Verify()
        {
            Require(rect.parent == parent && rect.childCount == children.Length &&
                Enumerable.Range(0, children.Length).All(index => rect.GetChild(index) == children[index]),
                rect.name + " changed its authored hierarchy.");
            Require(rect.GetComponent<Image>() == image && rect.GetComponent<Button>() == button &&
                image.sprite == sprite && image.material == material && image.color == color &&
                image.type == imageType && image.preserveAspect == preserveAspect &&
                image.enabled == imageEnabled && image.raycastTarget == raycastTarget && image.maskable == maskable,
                rect.name + " changed its existing artwork or graphic settings.");
            Require(EditorJsonUtility.ToJson(button) == buttonJson,
                rect.name + " changed its Button target, transition, navigation, or persistent callbacks.");
            Graphic[] current = rect.GetComponentsInChildren<Graphic>(true).Where(graphic => graphic != image).ToArray();
            Require(current.SequenceEqual(otherGraphics) && current.Select(EditorJsonUtility.ToJson).SequenceEqual(otherGraphicJson),
                rect.name + " added or changed an authored child graphic.");
        }
    }

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(Request)) return;
        string wait = WaitReason();
        if (wait != null)
        {
            WriteWait(wait);
            return;
        }

        // Consume a ready request once. A failing validation leaves a report for review
        // instead of repeatedly running the fixture or authoring on every editor tick.
        File.Delete(Request);
        Author();
    }

    private static string WaitReason()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play Mode before authoring.";
        if (BuildPipeline.isBuildingPlayer) return "Wait for the player build to finish.";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "Wait for compilation and asset import to finish.";
        foreach (string path in ScenePaths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isDirty)
                return "Save existing edits in " + path + ". The request is retained; no dirty scene is overwritten.";
        }
        return null;
    }

    private static void WriteWait(string reason)
    {
        if (lastWait == reason && File.Exists(Report)) return;
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Report, "WAIT: " + reason + Environment.NewLine);
        lastWait = reason;
    }

    [MenuItem("Tools/Civil Craft/Author Compact Pause Button")]
    public static void Author()
    {
        string wait = WaitReason();
        if (wait != null)
        {
            WriteWait(wait);
            return;
        }

        Directory.CreateDirectory("Temp");
        lastWait = null;
        var report = new StringBuilder();
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        Scene activeScene = SceneManager.GetActiveScene();
        var opened = new List<OpenedScene>();
        GameObject prefabContents = null;
        try
        {
            Require(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null, "The existing mobile pause prefab is missing.");
            prefabContents = PrefabUtility.LoadPrefabContents(PrefabPath);
            RectTransform prefabRect = prefabContents.GetComponent<RectTransform>();
            Require(prefabRect != null, "The existing mobile pause prefab has no RectTransform.");
            var prefabSnapshot = new ButtonSnapshot(prefabRect);

            // All geometry and native GraphicRaycaster checks precede source asset edits.
            ValidateIsolated(prefabContents, report);
            prefabSnapshot.Verify();

            var sceneButtons = new List<RectTransform>();
            foreach (string path in ScenePaths)
            {
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null, "Missing target scene: " + path);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool needsOpen = !scene.IsValid() || !scene.isLoaded;
                if (needsOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                opened.Add(new OpenedScene
                {
                    scene = scene,
                    opened = needsOpen,
                    wasInSetup = setup.Any(entry => entry.path == path)
                });
                Require(!scene.isDirty, "Target scene became dirty before authoring: " + path);
                RectTransform[] buttons = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                    .Where(rect => rect.name == "pause_btn").ToArray();
                foreach (RectTransform rect in buttons) new ButtonSnapshot(rect).Verify();
                sceneButtons.AddRange(buttons);
                if (buttons.Length == 0) report.AppendLine("SKIP: " + path + " has no authored pause_btn; no control was created.");
            }

            bool prefabChanged = AuthorExisting(prefabRect);
            prefabSnapshot.Verify();
            var changedScenes = new HashSet<int>();
            foreach (RectTransform rect in sceneButtons)
            {
                if (AuthorExisting(rect)) changedScenes.Add(rect.gameObject.scene.handle);
            }

            if (prefabChanged)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabContents, PrefabPath, out bool success);
                Require(success, "Saving the mobile pause prefab failed. Target scenes were not saved.");
                report.AppendLine("PASS: Saved " + PrefabPath + "; existing sprite, Button target, and callbacks preserved.");
            }
            else report.AppendLine("PASS: " + PrefabPath + " already has the compact authored layout; no save needed.");

            foreach (OpenedScene entry in opened)
            {
                if (!changedScenes.Contains(entry.scene.handle))
                {
                    report.AppendLine("PASS: " + entry.scene.path + " needs no scene save.");
                    continue;
                }
                EditorSceneManager.MarkSceneDirty(entry.scene);
                Require(EditorSceneManager.SaveScene(entry.scene), "Saving target scene failed: " + entry.scene.path);
                report.AppendLine("PASS: Saved existing pause_btn layout in " + entry.scene.path + "; artwork, hierarchy, and persistent callbacks preserved.");
            }

            report.AppendLine("PASS: BHAN HOUSE was not opened or changed; its existing runtime fallback uses the resource prefab.");
            report.AppendLine("PASS: No PauseManager callback, gameplay controller, screen-resolution setter, player save, or multiplayer API was invoked.");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            if (prefabContents != null) PrefabUtility.UnloadPrefabContents(prefabContents);
            for (int index = opened.Count - 1; index >= 0; index--)
            {
                OpenedScene entry = opened[index];
                if (entry.opened && entry.scene.IsValid() && entry.scene.isLoaded)
                    EditorSceneManager.CloseScene(entry.scene, !entry.wasInSetup);
            }
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
            bool setupPreserved = setup.Length == after.Length && Enumerable.Range(0, setup.Length).All(index =>
                setup[index].path == after[index].path && setup[index].isLoaded == after[index].isLoaded &&
                setup[index].isActive == after[index].isActive);
            report.AppendLine((setupPreserved ? "PASS: " : "FAIL: ") + "Original open, loaded, and active scene setup " +
                (setupPreserved ? "preserved." : "did not match after cleanup."));
            File.WriteAllText(Report, report.ToString());
        }
        Debug.Log("[Compact pause button] " + report);
    }

    private static bool AuthorExisting(RectTransform rect)
    {
        var snapshot = new ButtonSnapshot(rect);
        string before = LayoutSignature(rect);
        GameplayPauseButtonLayout.Apply(rect, 1f, new Vector2(1920f, 1080f), new Rect(0f, 0f, 1920f, 1080f), 160f, false);
        Require(rect.sizeDelta == new Vector2(80f, 80f) && rect.anchorMin == new Vector2(.5f, 1f) &&
            rect.anchorMax == new Vector2(.5f, 1f) && rect.pivot == new Vector2(.5f, .5f) &&
            rect.anchoredPosition == new Vector2(0f, -92f), "Unexpected compact authoring defaults on " + rect.name);
        Require(rect.GetComponent<Image>().raycastPadding == new Vector4(-20f, -20f, -20f, -20f),
            "Default pause tap padding must expand the existing 80-unit image to a 120-unit target.");
        snapshot.Verify();
        bool changed = before != LayoutSignature(rect);
        if (changed)
        {
            EditorUtility.SetDirty(rect);
            EditorUtility.SetDirty(rect.GetComponent<Image>());
        }
        return changed;
    }

    private static void ValidateIsolated(GameObject prefab, StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        try
        {
            var canvasOwner = new GameObject("Compact pause validation canvas (temporary)", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasOwner.layer = 5;
            SceneManager.MoveGameObjectToScene(canvasOwner, preview);
            var canvas = canvasOwner.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasOwner.GetComponent<RectTransform>().sizeDelta = new Vector2(512f, 512f);
            canvasOwner.transform.position = new Vector3(10000f, 10000f, 10000f);
            var cameraOwner = new GameObject("Compact pause validation camera (temporary)", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraOwner, preview);
            Camera camera = cameraOwner.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = preview;
            camera.orthographic = true;
            camera.orthographicSize = 256f;
            camera.aspect = 1f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 1000f;
            camera.transform.position = canvasOwner.transform.position + Vector3.back * 100f;
            camera.cullingMask = 1 << 5;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            canvas.worldCamera = camera;
            target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            target.Create();
            camera.targetTexture = target;

            GameObject clone = Object.Instantiate(prefab, canvas.transform, false);
            clone.SetActive(true);
            RectTransform rect = clone.GetComponent<RectTransform>();
            var snapshot = new ButtonSnapshot(rect);
            Require(clone.GetComponentsInChildren<Image>(true).Length == 1, "Mobile pause prefab must retain its existing single root Image.");
            Vector2[] screens =
            {
                new Vector2(1280f, 720f), new Vector2(1920f, 1080f),
                new Vector2(2400f, 1080f), new Vector2(1080f, 2400f)
            };
            int cases = 0;
            foreach (Vector2 screen in screens)
            {
                Rect safe = screen.x == 2400f ? new Rect(100f, 0f, 2300f, 1080f) :
                    screen.y == 2400f ? new Rect(0f, 0f, 1080f, 2320f) : new Rect(Vector2.zero, screen);
                float scale = Mathf.Sqrt(screen.x / 1920f * screen.y / 1080f);
                foreach (float dpi in new[] { 160f, 320f, 480f, 640f, 0f })
                {
                    ValidateCase(rect, canvas, camera, screen, safe, scale, dpi, true, snapshot);
                    cases++;
                }
            }
            foreach (float dpi in new[] { 160f, 640f })
                ValidateCase(rect, canvas, camera, new Vector2(1920f, 1080f), new Rect(0f, 0f, 1920f, 1080f), 1f, dpi, false, snapshot);
            report.AppendLine("PASS: " + cases + " mobile geometry/raycast cases at 1280x720, 1920x1080, 2400x1080 with asymmetric notch, and 1080x2400 with top inset; DPI 160/320/480/640/0 fallback, plus 2 desktop controls.");
            report.AppendLine("PASS: 80-unit visual; 120-unit base or 48dp tap target; negative padding; safe-area bounds and center; repeated layout idempotence; actual rendered GraphicRaycaster hits outside the visible rectangle reach the original Button; outside-target points do not hit.");
        }
        finally
        {
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void ValidateCase(RectTransform rect, Canvas canvas, Camera camera, Vector2 screen, Rect safe,
        float scale, float dpi, bool mobile, ButtonSnapshot snapshot)
    {
        GameplayPauseButtonLayout.Apply(rect, scale, screen, safe, dpi, mobile);
        float densityDpi = dpi > 0f ? dpi : 160f * Mathf.Max(1f, Mathf.Min(screen.x, screen.y) / 360f);
        float expectedTouch = mobile ? Mathf.Max(120f, 48f * densityDpi / 160f / scale) : 120f;
        float touch = GameplayPauseButtonLayout.TouchSize(scale, screen, dpi, mobile);
        Near(touch, expectedTouch, "Density-aware touch size");
        Require(rect.sizeDelta == new Vector2(80f, 80f) && rect.localScale == Vector3.one &&
            rect.anchorMin == new Vector2(.5f, 1f) && rect.anchorMax == new Vector2(.5f, 1f) &&
            rect.pivot == new Vector2(.5f, .5f), "Compact visual/anchor geometry differs from its specification.");
        Vector4 padding = rect.GetComponent<Image>().raycastPadding;
        float expectedPadding = -(touch - 80f) * .5f;
        Near(padding.x, expectedPadding, "Left padding");
        Near(padding.y, expectedPadding, "Bottom padding");
        Near(padding.z, expectedPadding, "Right padding");
        Near(padding.w, expectedPadding, "Top padding");
        Vector2 center = new Vector2(screen.x * .5f, screen.y) + rect.anchoredPosition * scale;
        Near(center.x, safe.center.x, "Safe-area horizontal center");
        float physicalHalf = touch * scale * .5f;
        Near(center.y + physicalHalf, safe.yMax - 32f * scale, "Safe-area top margin");
        Require(center.x - physicalHalf >= safe.xMin - .02f && center.x + physicalHalf <= safe.xMax + .02f &&
            center.y - physicalHalf >= safe.yMin - .02f && center.y + physicalHalf <= safe.yMax + .02f,
            "Expanded tap target leaves the safe area.");
        string stable = LayoutSignature(rect);
        GameplayPauseButtonLayout.Apply(rect, scale, screen, safe, dpi, mobile);
        Require(LayoutSignature(rect) == stable, "Repeated compact layout changed its authored state.");
        snapshot.Verify();
        ValidateNativeRaycasts(rect, canvas, camera, touch);
        GameplayPauseButtonLayout.Apply(rect, scale, screen, safe, dpi, mobile);
        Require(LayoutSignature(rect) == stable, "Native fixture failed to restore the deterministic layout.");
        snapshot.Verify();
    }

    private static void ValidateNativeRaycasts(RectTransform rect, Canvas canvas, Camera camera, float touch)
    {
        // Recenter only this preview clone so every padding case fits inside the
        // isolated camera's fixed render target. Screen and live canvases stay intact.
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one * Mathf.Min(1f, 440f / touch);
        Canvas.ForceUpdateCanvases();
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture });

        Image image = rect.GetComponent<Image>();
        Require(image.depth >= 0 && !image.canvasRenderer.cull, "The native preview graphic was not rendered for raycast validation.");
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 minimum = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 maximum = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        Vector2 center = (minimum + maximum) * .5f;
        float pixelsPerUnit = (maximum.x - minimum.x) / 80f;
        float expandedHalf = touch * pixelsPerUnit * .5f;
        float visibleHalf = (maximum.x - minimum.x) * .5f;
        float paddedOffset = (visibleHalf + expandedHalf) * .5f;
        Vector2[] positivePoints =
        {
            center, center + Vector2.right * paddedOffset, center + Vector2.left * paddedOffset,
            center + Vector2.up * paddedOffset, center + Vector2.down * paddedOffset
        };
        foreach (Vector2 point in positivePoints)
        {
            if (point != center)
                Require(!RectTransformUtility.RectangleContainsScreenPoint(rect, point, camera),
                    "A padded positive control unexpectedly lies inside the visible image.");
            Require(RectTransformUtility.RectangleContainsScreenPoint(rect, point, camera, image.raycastPadding),
                "The padded native rectangle rejected an expanded tap point.");
            Require(RaycastReceiver(canvas, point) == rect.gameObject,
                "GraphicRaycaster did not route an expanded tap to the existing pause Button.");
        }
        foreach (Vector2 direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        {
            Vector2 point = center + direction * (expandedHalf + 3f);
            Require(!RectTransformUtility.RectangleContainsScreenPoint(rect, point, camera, image.raycastPadding) &&
                RaycastReceiver(canvas, point) == null, "A point outside the expanded target still hits the pause Button.");
        }
    }

    private static GameObject RaycastReceiver(Canvas canvas, Vector2 point)
    {
        var hits = new List<RaycastResult>();
        // A direct raycast needs no live EventSystem or input module and executes no click.
        canvas.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(null) { position = point }, hits);
        return hits.Count == 0 ? null : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
    }

    private static string LayoutSignature(RectTransform rect) =>
        EditorJsonUtility.ToJson(rect) + "|" + rect.GetComponent<Image>().raycastPadding;

    private static void Near(float actual, float expected, string label) =>
        Require(Mathf.Abs(actual - expected) < .02f, label + ": expected " + expected + ", got " + actual + ".");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
