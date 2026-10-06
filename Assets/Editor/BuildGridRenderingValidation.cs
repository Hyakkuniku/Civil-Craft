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

/// <summary>Explicit, isolated blueprint rendering checks. Never saves or enters Play Mode.</summary>
public static class BuildGridRenderingValidation
{
    private const string RequestPath = "Temp/BuildGridRenderingValidation.request";
    private const string ReportPath = "Temp/BuildGridRenderingValidation.txt";
    private const int BlueprintQueue = 3100;
    private static readonly Color BlueprintBlue = new Color(48f / 255f, 87f / 255f, 225f / 255f, 1f);
    private static bool running;
    private static double nextCheck;

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
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(RequestPath) || UnsafeReason() != null) return;
        File.Delete(RequestPath);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Build Grid Rendering")]
    public static void Validate()
    {
        string unsafeReason = UnsafeReason();
        if (unsafeReason != null)
        {
            Debug.LogWarning("[Build grid rendering] " + unsafeReason + ". No scene or material changes made.");
            return;
        }

        running = true;
        var report = new StringBuilder("RUNNING: Isolated build grid material and tilted-camera render validation.\n");
        var scenes = new Dictionary<int, SceneState>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(scene.handle, new SceneState(scene));
        }
        var liveGraphics = new Dictionary<Graphic, GraphicState>();
        foreach (Graphic graphic in Resources.FindObjectsOfTypeAll<Graphic>())
            if (graphic != null && scenes.ContainsKey(graphic.gameObject.scene.handle) && !EditorUtility.IsPersistent(graphic))
                liveGraphics.Add(graphic, new GraphicState(graphic));
        var originals = new Dictionary<Material, string>();
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        RenderTexture previousTarget = RenderTexture.active;
        Scene preview = default;
        Component fixtureHelper = null;
        var temporaryMaterials = new List<Material>();
        bool passed = false;
        Directory.CreateDirectory("Temp");
        File.WriteAllText(ReportPath, report.ToString());

        try
        {
            Type runtime = typeof(BuildLocation).Assembly.GetType("BuildGridRendering");
            Check(runtime != null, "BuildGridRendering has not compiled yet.");
            MethodInfo ensure = runtime.GetMethod("Ensure", BindingFlags.Static | BindingFlags.Public);
            MethodInfo configure = runtime.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public);
            Check(ensure != null && configure != null, "BuildGridRendering public Ensure/Configure API is missing.");
            Check(ensure.Invoke(null, new object[] { null }) == null, "Ensure(null) should be a safe no-op.");

            Material gridSource = LoadGridMaterial();
            Material waterSource = AssetDatabase.LoadAssetAtPath<Material>("Assets/Input System/Water.mat");
            Check(gridSource != null && waterSource != null, "The authored grid or water source material is missing.");
            Check(gridSource.shader != null && waterSource.shader != null, "The authored grid or water shader is missing.");
            Snapshot(originals, gridSource);
            Snapshot(originals, waterSource);
            Material explicitGrid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Grid.mat");
            if (explicitGrid != null) Snapshot(originals, explicitGrid);

            preview = EditorSceneManager.NewPreviewScene();
            GameObject root = NewRoot("Build grid validation (temporary)", preview);
            GameObject cameraObject = NewRoot("Build grid validation camera (temporary)", preview);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.scene = preview;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 12f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;

            Canvas gridCanvas = NewCanvas(root.transform, "GridCanvas", camera, 150f);
            Image blueA = NewImage(gridCanvas.transform, "Solid blue backdrop A", BlueprintBlue);
            Image blueB = NewImage(gridCanvas.transform, "Solid blue backdrop B", BlueprintBlue);
            Image grid = NewImage(gridCanvas.transform, "Procedural grid lines", Color.white);
            grid.material = gridSource; // Matches the Shader Graph embedded material used by the authored scenes.
            Image disabled = NewImage(gridCanvas.transform, "Initially disabled blueprint Graphic", Color.clear);
            disabled.enabled = false;
            Graphic[] blueprint = gridCanvas.GetComponentsInChildren<Graphic>(true);
            var fixtureOriginals = new Dictionary<Graphic, Material>();
            foreach (Graphic graphic in blueprint)
            {
                fixtureOriginals.Add(graphic, graphic.material);
                Snapshot(originals, graphic.material);
            }

            Canvas hudCanvas = NewCanvas(root.transform, "HUD outside blueprint", camera, 1f);
            hudCanvas.gameObject.SetActive(false);
            Image hud = NewImage(hudCanvas.transform, "HUD sentinel", Color.red);
            Material hudOriginal = hud.material;
            bool hudEnabled = hud.enabled;
            Canvas nestedHudCanvas = NewCanvas(gridCanvas.transform, "Nested HUD with independent canvas", camera, 1f);
            nestedHudCanvas.gameObject.SetActive(false);
            Image nestedHud = NewImage(nestedHudCanvas.transform, "Nested HUD sentinel", Color.yellow);
            Material nestedHudOriginal = nestedHud.material;
            Component helper = ensure.Invoke(null, new object[] { grid }) as Component;
            fixtureHelper = helper;
            Check(helper != null && helper.gameObject == gridCanvas.gameObject,
                "Ensure must own materials on the nearest GridCanvas, not the HUD or grid Image.");
            Check(ensure.Invoke(null, new object[] { grid }) as Component == helper, "Repeated Ensure added another helper.");
            var owned = new Dictionary<Graphic, Material>();
            foreach (Graphic graphic in blueprint)
            {
                Material original = fixtureOriginals[graphic];
                Material material = graphic.material;
                Check(material != null && material != original && !EditorUtility.IsPersistent(material),
                    "Blueprint Graphic does not own a transient material: " + graphic.name);
                Check(material.renderQueue == BlueprintQueue, "Wrong blueprint queue on " + graphic.name);
                Check(material.shader == original.shader, "The helper replaced the original shader on " + graphic.name);
                Check(SameKeywords(material, original), "The helper changed shader keywords on " + graphic.name);
                PreserveProperty(material, original, "_ZWrite");
                PreserveProperty(material, original, "_ZTest");
                PreserveProperty(material, original, "_Cull");
                if (material.HasProperty("_QueueControl"))
                    Check(material.GetFloat("_QueueControl") == 1f, "URP material queue remains under automatic control.");
                owned.Add(graphic, material);
            }
            configure.Invoke(helper, new object[] { grid });
            foreach (Graphic graphic in blueprint)
                Check(graphic.material == owned[graphic], "Repeated Configure allocated/replaced an owned material.");
            Check(hud.material == hudOriginal && hud.enabled == hudEnabled, "The helper modified unrelated HUD UI.");
            Check(nestedHud.material == nestedHudOriginal, "The helper modified UI owned by a nested HUD canvas.");
            Check(!disabled.enabled, "The helper enabled an intentionally disabled Graphic.");
            Check(blueA.color == BlueprintBlue && blueB.color == BlueprintBlue,
                "The helper changed the solid blue backdrop colors/alpha.");
            report.AppendLine("PASS: All 4 blueprint Graphics, including default-UI blue backdrops and disabled Graphics, own queue-3100 materials; original shaders, depth/cull settings, keywords, visibility, colors and external/nested HUD preserved. Reconfiguration is idempotent.");

            ValidateRenders(preview, root.transform, camera, gridCanvas, waterSource, temporaryMaterials, report);

            // A normal runtime MonoBehaviour does not receive its lifecycle
            // callbacks in an edit-mode preview scene. Exercise the actual
            // teardown method explicitly before removing the fixture.
            InvokeRuntimeDestroy(helper);
            Object.DestroyImmediate(helper);
            fixtureHelper = null;
            foreach (Graphic graphic in blueprint)
                Check(graphic.material == fixtureOriginals[graphic], "Destroy did not restore the original material on " + graphic.name);
            foreach (Material material in owned.Values)
                Check(material == null, "Destroy did not release a helper-owned transient material.");
            Check(hud.material == hudOriginal, "Destroy modified unrelated HUD material.");
            Check(nestedHud.material == nestedHudOriginal, "Destroy modified nested HUD material.");
            report.AppendLine("PASS: Explicit runtime OnDestroy teardown in the edit-mode fixture restores all source material references, including default UI materials, and releases owned copies. HUD materials remain untouched.");
            AssertOriginalsUnchanged(originals);
            passed = true;
        }
        catch (Exception exception)
        {
            exception = Unwrap(exception);
            report.AppendLine("FAIL: " + exception);
            Debug.LogException(exception);
        }
        finally
        {
            try
            {
                if (fixtureHelper != null) InvokeRuntimeDestroy(fixtureHelper);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                foreach (Material material in temporaryMaterials)
                    if (material != null) Object.DestroyImmediate(material);
                RenderTexture.active = previousTarget;
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Selection.objects = selection;
                Selection.activeObject = selected;
                AssertOriginalsUnchanged(originals);
                Check(SceneManager.sceneCount == scenes.Count, "The loaded authored scene count changed.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    Check(scenes.TryGetValue(scene.handle, out SceneState state), "An authored scene was replaced.");
                    state.CheckUnchanged(scene);
                }
                foreach (KeyValuePair<Graphic, GraphicState> entry in liveGraphics)
                    entry.Value.CheckUnchanged(entry.Key);
                report.AppendLine("PASS: Original material assets/defaults and authored Graphic source materials/visibility, scene roots, active scene and dirty flags unchanged. TMP fallback caches retain identical shader/serialized contents/source if regenerated. Preview fixture removed; no scene/prefab saves or Play Mode.");
            }
            catch (Exception exception)
            {
                passed = false;
                report.AppendLine("FAIL cleanup/isolation: " + exception);
                Debug.LogException(exception);
            }
            report.AppendLine(passed ? "RESULT: PASS" : "RESULT: FAIL");
            File.WriteAllText(ReportPath, report.ToString());
            running = false;
            if (passed) Debug.Log("[Build grid rendering] PASS. Report: " + ReportPath);
        }
    }

    private static void ValidateRenders(Scene preview, Transform root, Camera camera, Canvas canvas,
        Material waterSource, List<Material> temporaryMaterials, StringBuilder report)
    {
        Material water = new Material(waterSource) { name = "Validation water (temporary)", hideFlags = HideFlags.HideAndDontSave };
        temporaryMaterials.Add(water);
        Shader foregroundShader = Shader.Find("Universal Render Pipeline/Unlit");
        Check(foregroundShader != null, "URP Unlit shader is unavailable for the foreground fixture.");
        Material foreground = new Material(foregroundShader) { name = "Validation foreground (temporary)", hideFlags = HideFlags.HideAndDontSave };
        foreground.SetColor("_BaseColor", Color.green);
        temporaryMaterials.Add(foreground);
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(cube, preview);
        cube.name = "Opaque foreground (temporary)";
        cube.transform.SetParent(root, false);
        cube.transform.localScale = Vector3.one * 8f;
        cube.GetComponent<Renderer>().sharedMaterial = foreground;
        GameObject waterObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(waterObject, preview);
        waterObject.name = "Large transparent world water (temporary)";
        waterObject.transform.SetParent(root, false);
        waterObject.transform.localPosition = new Vector3(0f, 0f, 100f);
        waterObject.transform.localScale = new Vector3(220f, 160f, 140f);
        waterObject.GetComponent<Renderer>().sharedMaterial = water;

        var target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        target.Create();
        camera.targetTexture = target;
        try
        {
            foreach (float pitch in new[] { 0f, 30f, -30f, 60f, -60f })
            {
                Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
                camera.transform.SetPositionAndRotation(rotation * new Vector3(0f, 0f, -40f), rotation);
                canvas.gameObject.SetActive(false);
                Color32[] without = Render(camera, target, null);
                canvas.gameObject.SetActive(true);
                RebuildFixtureCanvas(canvas);
                string capturePath = "Temp/BuildGridRendering_pitch_" + pitch.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ".png";
                Color32[] with = Render(camera, target, capturePath);
                Color32 baseline = without[180 * 640 + 480];
                Color32 blue = with[180 * 640 + 480];
                Color32 green = with[180 * 640 + 320];
                Check(blue.b > blue.r * 1.5f && blue.b > blue.g * 1.5f && blue.b > 80,
                    "Blue blueprint backdrop disappears over transparent water at pitch " + pitch + ": " + blue);
                Check(green.g > green.r * 1.5f && green.g > green.b * 1.5f && green.g > 80,
                    "Blueprint covers opaque foreground at pitch " + pitch + ": " + green);
                Check(ColorDistance(baseline, blue) > 40,
                    "Blueprint rendering did not change the world-water background at pitch " + pitch + ".");
                report.AppendLine("PASS: Actual URP render at pitch " + pitch + " degrees keeps solid blue over transparent world water and opaque green foreground in front; PNG " + capturePath + ".");
            }
        }
        finally
        {
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static Color32[] Render(Camera camera, RenderTexture target, string path)
    {
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Check(RenderPipeline.SupportsRenderRequest(camera, request), "The active render pipeline cannot render the isolated camera.");
            RenderPipeline.SubmitRenderRequest(camera, request);
        }
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            if (path != null) File.WriteAllBytes(path, image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
        }
    }

    private static GameObject NewRoot(string name, Scene scene)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root;
    }

    private static void RebuildFixtureCanvas(Canvas canvas)
    {
        // ForceUpdateCanvases dispatches callbacks for all loaded scenes,
        // including TMP fallback-material regeneration on the user's HUD.
        // Update only the isolated fixture's transforms and Graphic meshes.
        ((RectTransform)canvas.transform).ForceUpdateRectTransforms();
        foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true))
            if (graphic.isActiveAndEnabled && graphic.GetComponentInParent<Canvas>(true) == canvas)
                graphic.Rebuild(CanvasUpdate.PreRender);
    }

    private static Canvas NewCanvas(Transform parent, string name, Camera camera, float distance)
    {
        var owner = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        owner.transform.SetParent(parent, false);
        Canvas canvas = owner.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = distance;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        return canvas;
    }

    private static Image NewImage(Transform parent, string name, Color color)
    {
        var owner = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        owner.transform.SetParent(parent, false);
        RectTransform rect = owner.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = owner.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Material LoadGridMaterial()
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Materials/Grid.shadergraph"))
            if (asset is Material material) return material;
        return AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Grid.mat");
    }

    private static void Snapshot(Dictionary<Material, string> materials, Material material)
    {
        if (material != null && !materials.ContainsKey(material)) materials.Add(material, EditorJsonUtility.ToJson(material));
    }

    private static void AssertOriginalsUnchanged(Dictionary<Material, string> materials)
    {
        foreach (KeyValuePair<Material, string> material in materials)
            Check(material.Key != null && EditorJsonUtility.ToJson(material.Key) == material.Value,
                "An original material asset/default was changed or destroyed.");
    }

    private static void PreserveProperty(Material copy, Material original, string property)
    {
        if (original.HasProperty(property))
            Check(copy.HasProperty(property) && Mathf.Approximately(copy.GetFloat(property), original.GetFloat(property)),
                "The helper changed " + property + " on " + original.name);
    }

    private static bool SameKeywords(Material first, Material second)
    {
        var firstKeywords = new HashSet<string>(first.shaderKeywords);
        return firstKeywords.SetEquals(second.shaderKeywords);
    }

    private static int ColorDistance(Color32 first, Color32 second)
    {
        return Mathf.Abs(first.r - second.r) + Mathf.Abs(first.g - second.g) + Mathf.Abs(first.b - second.b);
    }

    private static Exception Unwrap(Exception exception)
    {
        return exception is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : exception;
    }

    private static void InvokeRuntimeDestroy(Component helper)
    {
        MethodInfo teardown = helper.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(teardown != null, "BuildGridRendering runtime teardown method is missing.");
        teardown.Invoke(helper, null);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class GraphicState
    {
        private readonly Material material;
        private readonly bool enabled;
        private readonly Color color;
        private readonly string description;
        private readonly string materialDescription;
        private readonly bool derivedFallback;
        private readonly string materialContents;
        private readonly Shader shader;
        private readonly Material fallbackSource;

        public GraphicState(Graphic graphic)
        {
            material = ReadSourceMaterial(graphic);
            enabled = graphic.enabled;
            color = graphic.color;
            description = graphic.name + " (" + graphic.GetType().Name + ", scene " + graphic.gameObject.scene.name +
                ", id " + graphic.GetInstanceID() + ")";
            materialDescription = DescribeMaterial(material);
            TMP_SubMeshUI subMesh = graphic as TMP_SubMeshUI;
            derivedFallback = subMesh != null && material != null && !EditorUtility.IsPersistent(material) &&
                (subMesh.fallbackMaterial != null || subMesh.fallbackSourceMaterial != null);
            materialContents = material != null ? EditorJsonUtility.ToJson(material) : null;
            shader = material != null ? material.shader : null;
            fallbackSource = subMesh != null ? subMesh.fallbackSourceMaterial : null;
        }

        public void CheckUnchanged(Graphic graphic)
        {
            Check(graphic != null, "Validation destroyed an authored/HUD Graphic: " + description);
            Material current = ReadSourceMaterial(graphic);
            bool sameMaterial = current == material;
            // TMP's material getter creates an instance and dirties its mesh.
            // Read sharedMaterial instead. Rendering can also regenerate a
            // nonpersistent fallback cache; only an equivalent serialized
            // copy with the same shader and exact fallback source is allowed.
            if (!sameMaterial && derivedFallback)
                sameMaterial = current != null && !EditorUtility.IsPersistent(current) && current.shader == shader &&
                    EditorJsonUtility.ToJson(current) == materialContents &&
                    ((TMP_SubMeshUI)graphic).fallbackSourceMaterial == fallbackSource;
            Check(sameMaterial && graphic.enabled == enabled && graphic.color == color,
                "Validation changed an authored/HUD Graphic: " + description +
                "; original material=" + materialDescription + ", current material=" + DescribeMaterial(current) +
                "; enabled " + enabled + " -> " + graphic.enabled + "; color " + color + " -> " + graphic.color + ".");
        }

        private static Material ReadSourceMaterial(Graphic graphic)
        {
            return graphic is TMP_SubMeshUI subMesh ? subMesh.sharedMaterial : graphic.material;
        }

        private static string DescribeMaterial(Material value)
        {
            return value == null ? "null" : value.name + " [id " + value.GetInstanceID() + ", asset " + AssetDatabase.GetAssetPath(value) + "]";
        }
    }

    private sealed class SceneState
    {
        private readonly bool loaded;
        private readonly bool dirty;
        private readonly string path;
        private readonly HashSet<int> roots = new HashSet<int>();

        public SceneState(Scene scene)
        {
            loaded = scene.isLoaded;
            dirty = scene.isDirty;
            path = scene.path;
            foreach (GameObject root in scene.GetRootGameObjects()) roots.Add(root.GetInstanceID());
        }

        public void CheckUnchanged(Scene scene)
        {
            var currentRoots = new HashSet<int>();
            foreach (GameObject root in scene.GetRootGameObjects()) currentRoots.Add(root.GetInstanceID());
            Check(scene.isLoaded == loaded && scene.isDirty == dirty && scene.path == path && roots.SetEquals(currentRoots),
                "Validation changed authored scene roots/loaded state/dirty flag: " + scene.name);
        }
    }
}
#endif
