#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in isolated ghost/grid regression. No authored scene writes or Play Mode.</summary>
public static class TutorialGhostRenderValidation
{
    private const string RequestPath = "Temp/TutorialGhostRenderValidation.request";
    private const string ReportPath = "Temp/TutorialGhostRenderValidation.txt";
    private static bool running;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool SafeToRun()
    {
        return !running && !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            !BuildPipeline.isBuildingPlayer && PrefabStageUtility.GetCurrentPrefabStage() == null;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(RequestPath) || !SafeToRun()) return;
        File.Delete(RequestPath);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Tutorial Ghost Rendering")]
    public static void Validate()
    {
        if (!SafeToRun())
        {
            Debug.LogWarning("[Tutorial ghost rendering] Stay in idle Edit Mode outside Prefab Mode.");
            return;
        }
        running = true;
        var report = new StringBuilder("RUNNING: Isolated authored ghost and selection indicator / blueprint render regression.\n");
        var scenes = new Dictionary<int, SceneState>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(scene.handle, new SceneState(scene));
        }
        var graphics = new Dictionary<Graphic, GraphicState>();
        foreach (Graphic graphic in Resources.FindObjectsOfTypeAll<Graphic>())
            if (graphic != null && scenes.ContainsKey(graphic.gameObject.scene.handle) && !EditorUtility.IsPersistent(graphic))
                graphics.Add(graphic, new GraphicState(graphic));
        Scene active = SceneManager.GetActiveScene();
        Object[] selected = Selection.objects;
        Object activeSelection = Selection.activeObject;
        RenderTexture previousTarget = RenderTexture.active;
        Scene preview = default;
        var helpers = new List<Component>();
        var temporaryMaterials = new List<Material>();
        var originals = new Dictionary<Material, string>();
        bool passed = false;
        Directory.CreateDirectory("Temp");
        File.WriteAllText(ReportPath, report.ToString());
        try
        {
            Type runtime = typeof(BuildLocation).Assembly.GetType("TutorialGhostRendering");
            Check(runtime != null, "TutorialGhostRendering has not compiled.");
            MethodInfo ensure = runtime.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static);
            MethodInfo configure = runtime.GetMethod("Configure", BindingFlags.Public | BindingFlags.Instance);
            Check(ensure != null && configure != null, "Ghost Ensure/Configure API missing.");
            Check(ensure.Invoke(null, new object[] { null }) == null, "Ensure(null) must be safe.");
            Material source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_GhostBlue.mat");
            Check(source != null && source.shader != null, "Authored Mat_GhostBlue is missing.");
            originals.Add(source, EditorJsonUtility.ToJson(source));
            Check(source.renderQueue == 3000 && source.GetFloat("_Surface") == 1f && source.GetFloat("_ZWrite") == 0f,
                "Fixture expects the existing transparent queue-3000 ghost material.");

            preview = EditorSceneManager.NewPreviewScene();
            GameObject root = NewRoot("Ghost rendering fixture (temporary)", preview);
            Camera camera = NewRoot("Ghost fixture camera", preview).AddComponent<Camera>();
            camera.scene = preview;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 4f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta;
            camera.allowHDR = camera.allowMSAA = false;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            Light light = NewRoot("Ghost fixture light", preview).AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.shadows = LightShadows.None;

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Check(unlit != null, "URP Unlit missing for opaque-occlusion check.");
            Material opaque = Temporary(new Material(unlit), temporaryMaterials);
            opaque.SetColor("_BaseColor", Color.green);
            Material later = Temporary(new Material(source), temporaryMaterials);
            later.renderQueue = 3200;
            Canvas canvas = NewRoot("Blueprint fixture", preview).AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 20f;
            GameObject backgroundObject = new GameObject("Solid blueprint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backgroundObject.transform.SetParent(canvas.transform, false);
            RectTransform backgroundRect = (RectTransform)backgroundObject.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
            Image background = backgroundObject.GetComponent<Image>();
            background.color = new Color(48f / 255f, 87f / 255f, 225f / 255f, 1f);
            background.raycastTarget = false;
            Material blueprint = Temporary(new Material(background.material), temporaryMaterials);
            blueprint.renderQueue = BuildGridRendering.GridRenderQueue;
            background.material = blueprint;
            backgroundRect.ForceUpdateRectTransforms();
            background.Rebuild(CanvasUpdate.PreRender);

            GameObject first = NewRoot("Bhan 1 ghost fixture", preview);
            Renderer left = Cube(preview, first.transform, "Left ghost", new Vector3(-3f, 0f, 0f), source);
            Renderer right = Cube(preview, first.transform, "Right ghost", new Vector3(3f, 0f, 0f), source);
            Color32[] before = Render(camera, canvas, "Temp/TutorialGhostRender_before.png");
            Check(Distance(Pixel(before, 185, 180), Pixel(before, 320, 180)) < 8,
                "Baseline does not reproduce transparent ghosts hidden by the later blueprint backdrop.");
            Component owner = ensure.Invoke(null, new object[] { first }) as Component;
            helpers.Add(owner);
            Check(owner != null && owner.gameObject == first, "Ghost owner must be scoped to its container.");
            Check(ensure.Invoke(null, new object[] { first }) as Component == owner, "Repeated Ensure duplicated the owner.");
            configure.Invoke(owner, new object[] { left });
            configure.Invoke(owner, new object[] { right });
            Material owned = left.sharedMaterial;
            Check(owned != null && owned != source && !EditorUtility.IsPersistent(owned), "Ghost must own a transient material copy.");
            Check(right.sharedMaterial == owned, "Renderers sharing a source should reuse one owned material.");
            Check(owned.renderQueue == BuildGridRendering.GridRenderQueue + 10 && owned.shader == source.shader,
                "Ghost must keep its shader and render just after the blueprint.");
            Check(new HashSet<string>(source.shaderKeywords).SetEquals(owned.shaderKeywords), "Ghost keywords changed.");
            foreach (string property in new[] { "_ZWrite", "_ZTest", "_Cull", "_Surface", "_SrcBlend", "_DstBlend" })
                if (source.HasProperty(property))
                    Check(owned.HasProperty(property) && Mathf.Approximately(source.GetFloat(property), owned.GetFloat(property)), "Ghost property changed: " + property);
            Check(owned.GetColor("_BaseColor") == source.GetColor("_BaseColor") &&
                owned.GetTexture("_BaseMap") == source.GetTexture("_BaseMap"), "Authored ghost color/alpha/texture changed.");
            for (int i = 0; i < 4; i++)
            {
                first.SetActive(false);
                first.SetActive(true);
                configure.Invoke(owner, new object[] { left });
                configure.Invoke(owner, new object[] { right });
                Check(left.sharedMaterial == owned && right.sharedMaterial == owned, "Replay allocated nested material clones.");
            }
            Color32[] after = Render(camera, canvas, "Temp/TutorialGhostRender_after.png");
            Check(Distance(Pixel(before, 185, 180), Pixel(after, 185, 180)) > 15 &&
                Distance(Pixel(before, 455, 180), Pixel(after, 455, 180)) > 15,
                "Queue fix did not make both authored-material ghosts visible in the actual orthographic URP render.");
            Check(Distance(Pixel(before, 320, 180), Pixel(after, 320, 180)) < 8, "Ghost fix changed uncovered blue background.");
            Cube(preview, root.transform, "Opaque foreground", new Vector3(3f, 0f, -2f), opaque);
            Color32[] occluded = Render(camera, canvas, "Temp/TutorialGhostRender_occlusion.png");
            Color32 foreground = Pixel(occluded, 455, 180);
            Check(foreground.g > foreground.r * 1.5f && foreground.g > foreground.b * 1.5f && foreground.g > 80,
                "Ghost incorrectly draws through closer opaque geometry.");
            Check(Distance(Pixel(after, 185, 180), Pixel(occluded, 185, 180)) < 8, "Unoccluded ghost unexpectedly changed.");
            report.AppendLine("PASS: Actual orthographic URP pixels reproduce the hidden queue-3000 ghost, show both fixed ghosts above the queue-3100 solid blueprint, and retain opaque foreground depth occlusion. Three diagnostic PNGs saved in Temp.");

            GameObject second = NewRoot("Bhan 2 ghost fixture", preview);
            second.SetActive(false);
            Renderer slots = Cube(preview, second.transform, "Multi-slot ghost", Vector3.zero, source);
            slots.sharedMaterials = new[] { source, opaque, later, null, source };
            Component otherOwner = ensure.Invoke(null, new object[] { second }) as Component;
            helpers.Add(otherOwner);
            configure.Invoke(otherOwner, new object[] { slots });
            Material[] configured = slots.sharedMaterials;
            Material secondOwned = configured[0];
            Check(secondOwned != source && secondOwned != owned && configured[4] == secondOwned,
                "The second container must independently own/reuse its ghost copy.");
            Check(configured[1] == opaque && configured[2] == later && configured[3] == null,
                "Opaque, already-later transparent, or null slots were changed.");
            configured[4] = later; // A newer external assignment must survive teardown.
            slots.sharedMaterials = configured;
            DestroyOwner(otherOwner);
            helpers.Remove(otherOwner);
            Check(slots.sharedMaterials[0] == source && slots.sharedMaterials[4] == later && secondOwned == null,
                "Teardown must restore only still-owned slots and release the material.");
            DestroyOwner(owner);
            helpers.Remove(owner);
            Check(left.sharedMaterial == source && right.sharedMaterial == source && owned == null,
                "Teardown failed to restore original renderer materials or release copies.");
            report.AppendLine("PASS: Bhan 1/2-style containers are isolated; repeated/replayed Configure reuses copies, source shader/keywords/alpha/depth/texture remain intact, non-ghost slots untouched, and teardown restores only owned slots.");
            first.SetActive(false);
            root.SetActive(false);
            ValidateSelectionIndicator(preview, camera, canvas, originals, report);
            passed = true;
        }
        catch (Exception exception)
        {
            if (exception is TargetInvocationException invocation && invocation.InnerException != null) exception = invocation.InnerException;
            report.AppendLine("FAIL: " + exception);
            Debug.LogException(exception);
        }
        finally
        {
            try
            {
                foreach (Component helper in helpers) if (helper != null) DestroyOwner(helper);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                foreach (Material material in temporaryMaterials) if (material != null) Object.DestroyImmediate(material);
                RenderTexture.active = previousTarget;
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Selection.objects = selected;
                Selection.activeObject = activeSelection;
                foreach (KeyValuePair<Material, string> original in originals)
                    Check(original.Key != null && EditorJsonUtility.ToJson(original.Key) == original.Value, "Authored ghost asset changed.");
                Check(SceneManager.sceneCount == scenes.Count, "Authored scene count changed.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    Check(scenes.TryGetValue(scene.handle, out SceneState state), "Authored scene replaced.");
                    state.Verify(scene);
                }
                foreach (KeyValuePair<Graphic, GraphicState> graphic in graphics) graphic.Value.Verify(graphic.Key);
                report.AppendLine("PASS: Authored material/Graphics, loaded scenes, roots, dirty flags, selection and active scene preserved; preview and owned resources removed; no scene/prefab saves or Play Mode.");
            }
            catch (Exception exception)
            {
                passed = false;
                report.AppendLine("FAIL isolation/cleanup: " + exception);
                Debug.LogException(exception);
            }
            running = false;
            report.AppendLine(passed ? "RESULT: PASS" : "RESULT: FAIL");
            File.WriteAllText(ReportPath, report.ToString());
            if (passed) Debug.Log("[Tutorial ghost rendering] PASS. Report: " + ReportPath);
        }
    }

    private static void ValidateSelectionIndicator(Scene scene, Camera camera, Canvas canvas,
        Dictionary<Material, string> originals, StringBuilder report)
    {
        Material source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/LOCKED.mat");
        Check(source != null && source.shader != null && source.renderQueue == 3000,
            "The authored selection indicator material must exist at queue 3000.");
        originals.Add(source, EditorJsonUtility.ToJson(source));
        FieldInfo activeField = typeof(TutorialDragSelectionAnim).GetField("activeIndicator", BindingFlags.NonPublic | BindingFlags.Static);
        Check(activeField != null, "Selection indicator active-instance field missing.");
        object originalActive = activeField.GetValue(null);
        GameObject parent = NewRoot("Selection indicator fixture", scene);
        Component rendering = null;
        try
        {
            Renderer visual = Cube(scene, parent.transform, "Authored selection cube", new Vector3(-3f, 0f, 0f), source);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
            Vector3 position = visual.transform.localPosition, scale = visual.transform.localScale;
            Quaternion rotation = visual.transform.localRotation;
            Renderer authoredHidden = Cube(scene, visual.transform, "Intentionally hidden child", Vector3.zero, source);
            authoredHidden.enabled = false;
            // Physics is not under test. Check only colliders retained by the
            // preview primitives; do not mix 2D/3D colliders on one object.
            Collider[] fixtureColliders = visual.GetComponentsInChildren<Collider>(true);
            Collider2D[] fixtureColliders2D = visual.GetComponentsInChildren<Collider2D>(true);
            Color32[] before = Render(camera, canvas, "Temp/TutorialSelectionRender_before.png");
            Check(Distance(Pixel(before, 185, 180), Pixel(before, 320, 180)) < 8,
                "Selection fixture does not reproduce the hidden queue-3000 indicator.");
            TutorialDragSelectionAnim indicator = visual.gameObject.AddComponent<TutorialDragSelectionAnim>();
            InvokePrivate(indicator, "OnEnable");
            InvokePrivate(indicator, "Start"); // Calls the real CaptureAuthoredTransform integration.
            rendering = indicator.GetComponent<TutorialGhostRendering>();
            Material owned = visual.sharedMaterial;
            Check(rendering != null && owned != source && owned.renderQueue == TutorialGhostRendering.GhostRenderQueue,
                "Selection animation Start did not configure its renderer above the blueprint.");
            Check(owned.shader == source.shader && owned.GetColor("_BaseColor") == source.GetColor("_BaseColor") &&
                owned.GetFloat("_ZWrite") == source.GetFloat("_ZWrite"), "Selection shader/alpha/depth settings changed.");
            Check(indicator.FinalSceneScale == scale && visual.transform.localScale == scale * indicator.startScaleMultiplier,
                "Selection animation lost the authored scale or initial growth multiplier.");
            foreach (Collider collider in fixtureColliders)
                Check(collider == null || !collider.enabled, "Selection demonstration 3D collider remains enabled.");
            foreach (Collider2D collider in fixtureColliders2D)
                Check(collider == null || !collider.enabled, "Selection demonstration 2D collider remains enabled.");
            Check(!authoredHidden.enabled && authoredHidden.sharedMaterial == owned,
                "Selection integration did not preserve authored hidden visuals/shared-copy reuse.");
            InvokePrivate(indicator, "ApplyGrowth", 1f);
            Color32[] shown = Render(camera, canvas, "Temp/TutorialSelectionRender_after.png");
            Check(Distance(Pixel(before, 185, 180), Pixel(shown, 185, 180)) > 15,
                "The real selection animation remains hidden in the orthographic URP render.");
            TutorialDragSelectionAnim.NotifySelectionDragStarted();
            Check(!visual.enabled && !authoredHidden.enabled, "The selection example did not hide while the player drags.");
            Color32[] dragging = Render(camera, canvas, "Temp/TutorialSelectionRender_dragging.png");
            Check(Distance(Pixel(before, 185, 180), Pixel(dragging, 185, 180)) < 8,
                "Selection example remains visible while the player draws their own selection.");
            // Exercise only the failed-drag recovery, without touching a real
            // BarCreator, tutorial singleton, selection graph, or step events.
            InvokePrivate(indicator, "RestoreAfterFailedSelection");
            Check(visual.enabled && !authoredHidden.enabled && visual.transform.localScale == scale * indicator.startScaleMultiplier,
                "Failed selection did not restart the example and restore only authored-visible renderers.");
            InvokePrivate(indicator, "ApplyGrowth", 1f);
            Color32[] restored = Render(camera, canvas, "Temp/TutorialSelectionRender_restored.png");
            Check(Distance(Pixel(shown, 185, 180), Pixel(restored, 185, 180)) < 8,
                "Failed-selection recovery did not restore the visible animated reference.");
            for (int i = 0; i < 3; i++)
            {
                indicator.Hide();
                Check(!indicator.gameObject.activeSelf && visual.transform.localPosition == position &&
                    visual.transform.localScale == scale && Quaternion.Angle(visual.transform.localRotation, rotation) < .001f,
                    "Hide did not restore the selection cube's authored final transform.");
                indicator.Show();
                Check(visual.enabled && !authoredHidden.enabled && visual.sharedMaterial == owned &&
                    indicator.GetComponents<TutorialGhostRendering>().Length == 1, "Selection replay changed visibility or duplicated its render owner/material.");
                InvokePrivate(indicator, "ApplyGrowth", 1f);
                Check(visual.transform.localScale == scale && visual.transform.localPosition == position,
                    "Selection replay growth no longer reaches the authored final volume.");
            }
            DestroyOwner(rendering);
            rendering = null;
            Check(visual.sharedMaterial == source && authoredHidden.sharedMaterial == source && owned == null,
                "Selection render owner teardown did not restore source material slots/release its copy.");
            report.AppendLine("PASS: Actual LOCKED-material selection animation reproduces hidden-before, visible-after, hidden-during-player-drag and visible-after-failed-drag renders. Show/Hide replay retains one material copy and authored final volume; authored-disabled visuals remain hidden and any existing fixture colliders are disabled.");
        }
        finally
        {
            try
            {
                if (rendering == null) rendering = parent.GetComponentInChildren<TutorialGhostRendering>(true);
                if (rendering != null) DestroyOwner(rendering);
                Object.DestroyImmediate(parent);
            }
            finally { activeField.SetValue(null, originalActive); }
        }
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        Check(method != null, "Selection animation method missing: " + methodName);
        method.Invoke(target, arguments);
    }

    private static GameObject NewRoot(string name, Scene scene)
    {
        var value = new GameObject(name);
        SceneManager.MoveGameObjectToScene(value, scene);
        return value;
    }

    private static Renderer Cube(Scene scene, Transform parent, string name, Vector3 position, Material material)
    {
        GameObject value = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(value, scene);
        value.name = name;
        value.transform.SetParent(parent, false);
        value.transform.localPosition = position;
        value.transform.localScale = new Vector3(2.8f, 2.8f, .5f);
        Renderer renderer = value.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private static Material Temporary(Material material, List<Material> owned)
    {
        material.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(material);
        return material;
    }

    private static Color32[] Render(Camera camera, Canvas canvas, string path)
    {
        var target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var pixels = new Texture2D(640, 360, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        target.Create();
        camera.targetTexture = target;
        try
        {
            // Only update the preview canvas: global ForceUpdateCanvases can
            // invoke real HUD/TMP callbacks in the user's loaded scenes.
            ((RectTransform)canvas.transform).ForceUpdateRectTransforms();
            foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true))
                if (graphic.isActiveAndEnabled) graphic.Rebuild(CanvasUpdate.PreRender);
            Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "This regression requires the project's active URP renderer.");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Check(RenderPipeline.SupportsRenderRequest(camera, request), "URP cannot render the isolated camera.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            return pixels.GetPixels32();
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
        }
    }

    private static Color32 Pixel(Color32[] pixels, int x, int y) => pixels[y * 640 + x];
    private static int Distance(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void DestroyOwner(Component owner)
    {
        MethodInfo destroy = owner.GetType().GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(destroy != null, "Ghost runtime teardown missing.");
        // Regular MonoBehaviours do not run lifecycle callbacks in edit-mode previews.
        destroy.Invoke(owner, null);
        Object.DestroyImmediate(owner);
    }

    private sealed class SceneState
    {
        private readonly bool dirty, loaded;
        private readonly string path;
        private readonly HashSet<int> roots = new HashSet<int>();
        public SceneState(Scene scene)
        {
            dirty = scene.isDirty; loaded = scene.isLoaded; path = scene.path;
            foreach (GameObject root in scene.GetRootGameObjects()) roots.Add(root.GetInstanceID());
        }
        public void Verify(Scene scene)
        {
            var current = new HashSet<int>();
            foreach (GameObject root in scene.GetRootGameObjects()) current.Add(root.GetInstanceID());
            Check(dirty == scene.isDirty && loaded == scene.isLoaded && path == scene.path && roots.SetEquals(current), "Authored scene changed: " + scene.name);
        }
    }

    private sealed class GraphicState
    {
        private readonly Material material, fallbackSource;
        private readonly string contents;
        private readonly bool enabled, fallback;
        private readonly Color color;
        public GraphicState(Graphic graphic)
        {
            material = Source(graphic); enabled = graphic.enabled; color = graphic.color;
            contents = material != null ? EditorJsonUtility.ToJson(material) : null;
            TMP_SubMeshUI sub = graphic as TMP_SubMeshUI;
            fallbackSource = sub != null ? sub.fallbackSourceMaterial : null;
            fallback = sub != null && material != null && !EditorUtility.IsPersistent(material) && (sub.fallbackMaterial != null || fallbackSource != null);
        }
        public void Verify(Graphic graphic)
        {
            Check(graphic != null, "An authored Graphic was destroyed.");
            Material current = Source(graphic);
            bool same = current == material || (fallback && current != null && !EditorUtility.IsPersistent(current) &&
                current.shader == material.shader && EditorJsonUtility.ToJson(current) == contents && ((TMP_SubMeshUI)graphic).fallbackSourceMaterial == fallbackSource);
            Check(same && graphic.enabled == enabled && graphic.color == color, "Authored Graphic changed: " + graphic.name);
        }
        private static Material Source(Graphic graphic) => graphic is TMP_SubMeshUI sub ? sub.sharedMaterial : graphic.material;
    }
}
#endif
