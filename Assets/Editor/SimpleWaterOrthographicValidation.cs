#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit existing-foam check; disposable scene/materials, no saves or Play Mode.</summary>
// Current scope: prevent camera-color/minimap feedback while preserving depth and foam.
public static class SimpleWaterOrthographicValidation
{
    private const string ShaderPath = "Assets/Simple Water/Simple Water.shadergraph";
    private const string MaterialPath = "Assets/Simple Water/Water2.mat";
    private const string DepthPath = "Assets/Simple Water/Depth Fade.shadersubgraph";
    private const string FoamColor = "Color_AFFD055F";
    private const string FoamSpeed = "Vector1_E23798DE";
    private const string RefractionSpeed = "Vector1_29A8EAB";
    private const string ContactProfile = "_CCMinimapContactFoam";
    private const string OpaqueTexture = "_CameraOpaqueTexture";
    private const string RequestPath = "Temp/SimpleWaterOrthographicValidation.request";
    private const string ReportPath = "Temp/SimpleWaterOrthographicValidation.txt";
    private static bool running;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool Safe() => !running && !EditorApplication.isPlayingOrWillChangePlaymode &&
        !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer && !ShaderUtil.anythingCompiling;

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(RequestPath) || !Safe()) return;
        File.Delete(RequestPath);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Existing Water Orthographic Foam")]
    public static void Validate()
    {
        if (!Safe()) { Debug.LogWarning("[Existing water foam] Wait for Edit Mode and completed imports before validation."); return; }
        running = true;
        Directory.CreateDirectory("Temp");
        var report = new StringBuilder("RUNNING: Existing Simple Water foam, isolated orthographic/perspective checks.\nUTC: " + DateTime.UtcNow.ToString("O") + "\n");
        var scenes = new Dictionary<int, SceneSnapshot>();
        for (int i = 0; i < SceneManager.sceneCount; i++) { Scene s = SceneManager.GetSceneAt(i); scenes.Add(s.handle, new SceneSnapshot(s)); }
        var cameraTargets = new Dictionary<Camera, RenderTexture>();
        foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            if (c != null && !EditorUtility.IsPersistent(c)) cameraTargets.Add(c, c.targetTexture);
        var files = new Dictionary<string, string>();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        Scene active = SceneManager.GetActiveScene(), preview = default;
        RenderTexture priorTarget = RenderTexture.active, target = null;
        Vector4 priorContactProfile = Shader.GetGlobalVector(ContactProfile);
        Texture priorOpaqueTexture = Shader.GetGlobalTexture(OpaqueTexture);
        Vector4 priorOpaqueTexelSize = Shader.GetGlobalVector("_CameraOpaqueTexture_TexelSize");
        Camera camera = null;
        Material source = null;
        string sourceJson = null;
        bool sourceDirty = false, passed = false;
        var temporary = new List<Object>();
        try
        {
            Shader.SetGlobalVector(ContactProfile, Vector4.zero);
            foreach (string path in new[] { ShaderPath, MaterialPath, DepthPath, "Assets/Simple Water/MapContactFoam.hlsl", "Assets/Script/Player/MinimapFollow.cs" }) files.Add(path, File.ReadAllText(path));
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Check(shader != null && source != null && source.shader == shader, "The current Simple Water graph/Water2 material is not imported.");
            sourceJson = EditorJsonUtility.ToJson(source); sourceDirty = EditorUtility.IsDirty(source);
            Check(source.HasProperty(FoamColor), "The existing foam-color property is unavailable.");
            Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "This fixture requires the current URP pipeline.");
            Material water = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            Material control = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            temporary.Add(water); temporary.Add(control);
            Color foam = source.GetColor(FoamColor);
            control.SetColor(FoamColor, new Color(0f, 0f, 0f, foam.a));
            Check(source.HasProperty(FoamSpeed) && source.HasProperty(RefractionSpeed), "Existing animation-speed properties are unavailable.");
            // URP resets shader time per camera, so keep only disposable copies at a fixed phase.
            water.SetFloat(FoamSpeed, 0f); control.SetFloat(FoamSpeed, 0f);
            water.SetFloat(RefractionSpeed, 0f); control.SetFloat(RefractionSpeed, 0f);
            report.AppendLine("Static comparison: existing Foam Speed and Refraction Speed are zero only on both disposable copies; source speeds remain " + source.GetFloat(FoamSpeed) + " / " + source.GetFloat(RefractionSpeed) + ".");
            ShaderUtil.CompilePass(water, 0, true);
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
            {
                report.AppendLine("Shader " + message.severity + ": " + message.message);
                Check(message.severity != ShaderCompilerMessageSeverity.Error, "Source shader compile error: " + message.message);
            }
            Check(shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Source water shader is unsupported or has compiler errors.");

            preview = EditorSceneManager.NewPreviewScene();
            GameObject owner = NewObject("Existing foam validation camera", preview);
            camera = owner.AddComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.nearClipPlane = .3f; camera.farClipPlane = 5000f; camera.orthographicSize = 8f; camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .1f, .12f);
            camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
            camera.transform.position = new Vector3(0f, 14f, -14f); camera.transform.LookAt(Vector3.zero);
            UniversalAdditionalCameraData data = owner.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; data.requiresDepthOption = CameraOverrideOption.On; data.requiresColorOption = CameraOverrideOption.Off;
            target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            target.Create(); camera.targetTexture = target;
            Shader opaqueShader = Shader.Find("Universal Render Pipeline/Unlit");
            Check(opaqueShader != null, "The opaque shoreline fixture shader is unavailable.");
            var land = new Material(opaqueShader) { hideFlags = HideFlags.HideAndDontSave };
            land.SetColor("_BaseColor", new Color(.32f, .26f, .18f, 1f)); temporary.Add(land);
            Primitive("Opaque bed", preview, new Vector3(0f, -3f, 0f), new Vector3(22f, .4f, 22f), land);
            Primitive("Existing foam shoreline intersection", preview, Vector3.zero, new Vector3(3f, 2f, 3f), land);
            Mesh mesh = PlaneMesh(); temporary.Add(mesh);
            GameObject waterObject = NewObject("Existing water surface", preview);
            waterObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = waterObject.AddComponent<MeshRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            bool[] nearFoam = null;
            for (int view = 0; view < 3; view++)
            {
                bool orthographic = view < 2;
                camera.orthographic = orthographic;
                camera.transform.position = view == 1 ? new Vector3(0f, 28f, -28f) : new Vector3(0f, 14f, -14f);
                camera.transform.LookAt(Vector3.zero);
                string mode = view == 0 ? "orthographic" : view == 1 ? "orthographic_far" : "perspective";
                renderer.sharedMaterial = water;
                Color32[] authored = Capture(camera, target, mode + "_existing_foam");
                renderer.sharedMaterial = control;
                Color32[] suppressed = Capture(camera, target, mode + "_foam_color_control");
                int foamPixels = 0, magenta = 0;
                var foamMask = new bool[authored.Length];
                long difference = 0;
                for (int i = 0; i < authored.Length; i++)
                {
                    int brighter = authored[i].r + authored[i].g + authored[i].b - suppressed[i].r - suppressed[i].g - suppressed[i].b;
                    if (brighter > 30 && authored[i].r > suppressed[i].r + 10) { foamPixels++; foamMask[i] = true; }
                    difference += Mathf.Abs(authored[i].r - suppressed[i].r) + Mathf.Abs(authored[i].g - suppressed[i].g) + Mathf.Abs(authored[i].b - suppressed[i].b);
                    if (authored[i].r > 200 && authored[i].b > 200 && authored[i].g < 80) magenta++;
                }
                report.AppendLine(mode + ": " + foamPixels + " pixels show existing foam-color contribution; mean RGB delta " + ((double)difference / authored.Length).ToString("0.000") + ".");
                Check(foamPixels > 300 && magenta < authored.Length / 100, "Existing foam is absent or shader-error colored in " + mode + ".");
                if (view == 0) nearFoam = foamMask;
                if (view == 1)
                {
                    int changed = 0, union = 0;
                    for (int i = 0; i < foamMask.Length; i++) { if (nearFoam[i] || foamMask[i]) union++; if (nearFoam[i] != foamMask[i]) changed++; }
                    report.AppendLine("Orthographic camera distance doubled with identical extents/angle: " + changed + " foam-mask pixels differ out of " + union + " union pixels.");
                    Check(changed < union * .1f + 20, "Existing orthographic foam changes substantially with camera distance.");
                }
            }
            ValidateDepthOnlyLifecycle(preview, report);
            ValidateOpaqueColorIndependence(preview, camera, target, renderer, water, temporary, report);
            ValidateAndroid(shader, report);
            report.AppendLine("PASS: Existing depth-based foam remains visible in native orthographic (far=5000) and perspective shoreline renders at a fixed animation phase. Disposable controls change Foam Color RGB and freeze existing speeds only; source material contents and camera shader globals are preserved. No stronger-foam or temporal claim is made.");
            passed = true;
        }
        catch (Exception e) { report.AppendLine("FAIL: " + e); Debug.LogException(e); }
        finally
        {
            try
            {
                if (camera != null) camera.targetTexture = null;
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                foreach (Object item in temporary) if (item != null) Object.DestroyImmediate(item);
                RenderTexture.active = priorTarget;
                Shader.SetGlobalVector(ContactProfile, priorContactProfile);
                Shader.SetGlobalTexture(OpaqueTexture, priorOpaqueTexture);
                Shader.SetGlobalVector("_CameraOpaqueTexture_TexelSize", priorOpaqueTexelSize);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Selection.objects = selection; Selection.activeObject = selected;
                foreach (KeyValuePair<string, string> file in files) Check(File.ReadAllText(file.Key) == file.Value, "Source asset changed during fixture: " + file.Key);
                if (sourceJson != null) Check(source != null && EditorJsonUtility.ToJson(source) == sourceJson && EditorUtility.IsDirty(source) == sourceDirty, "Source Water2 material changed.");
                Check(SceneManager.sceneCount == scenes.Count, "Loaded scene count changed.");
                for (int i = 0; i < SceneManager.sceneCount; i++) { Scene s = SceneManager.GetSceneAt(i); Check(scenes.ContainsKey(s.handle), "Loaded scene replaced."); scenes[s.handle].Assert(s); }
                foreach (KeyValuePair<Camera, RenderTexture> entry in cameraTargets) Check(entry.Key != null && entry.Key.targetTexture == entry.Value, "Live camera target changed.");
                Check(SceneManager.GetActiveScene() == active && RenderTexture.active == priorTarget && Same(Selection.objects, selection) && Selection.activeObject == selected, "Editor active scene/selection/target changed.");
                Check(Shader.GetGlobalVector(ContactProfile) == priorContactProfile && Shader.GetGlobalTexture(OpaqueTexture) == priorOpaqueTexture &&
                    Shader.GetGlobalVector("_CameraOpaqueTexture_TexelSize") == priorOpaqueTexelSize, "Prior camera shader globals were not restored.");
                report.AppendLine("PASS: Source graph/subgraph/material bytes and Water2 serialized contents/dirty flag, authored scene roots/dirty flags, live camera targets and editor selection preserved. Preview resources released; no saves, scene assignments or Play Mode.");
            }
            catch (Exception e) { passed = false; report.AppendLine("FAIL isolation: " + e); Debug.LogException(e); }
            report.AppendLine(passed ? "RESULT: PASS" : "RESULT: FAIL");
            File.WriteAllText(ReportPath, report.ToString()); running = false;
            if (passed) Debug.Log("[Existing water foam] PASS. Report: " + ReportPath);
        }
    }

    private static GameObject NewObject(string name, Scene scene)
    {
        var obj = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(obj, scene); return obj;
    }
    private static GameObject Primitive(string name, Scene scene, Vector3 position, Vector3 scale, Material material, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject obj = GameObject.CreatePrimitive(type); SceneManager.MoveGameObjectToScene(obj, scene);
        obj.name = name; obj.transform.position = position; obj.transform.localScale = scale; Object.DestroyImmediate(obj.GetComponent<Collider>());
        Renderer r = obj.GetComponent<Renderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return obj;
    }
    private static void ValidateOpaqueColorIndependence(Scene scene, Camera camera, RenderTexture target,
        Renderer renderer, Material water, List<Object> temporary, StringBuilder report)
    {
        var red = SolidTexture(Color.red); var green = SolidTexture(Color.green); temporary.Add(red); temporary.Add(green);
        camera.orthographic = false; camera.transform.position = new Vector3(0f, 14f, -14f); camera.transform.LookAt(Vector3.zero);
        renderer.sharedMaterial = water;
        Shader.SetGlobalTexture(OpaqueTexture, red);
        Color32[] before = Capture(camera, target, "perspective_opaque_red");
        Check(Shader.GetGlobalTexture(OpaqueTexture) == red, "Perspective camera unexpectedly requested an opaque color copy.");
        GameObject owner = NewObject("Disposable opaque map image camera", scene);
        Camera map = owner.AddComponent<Camera>(); map.scene = scene; map.enabled = false; map.orthographic = true;
        map.orthographicSize = 15f; map.clearFlags = CameraClearFlags.SolidColor; map.backgroundColor = Color.green;
        map.transform.SetPositionAndRotation(new Vector3(0f, 50f, 0f), Quaternion.Euler(90f, 0f, 0f));
        var data = owner.AddComponent<UniversalAdditionalCameraData>();
        // An explicitly configured disposable camera models a stale opaque map image;
        // production MinimapFollow must not request this color copy itself.
        data.requiresDepthOption = CameraOverrideOption.On; data.requiresColorOption = CameraOverrideOption.On;
        var mapTarget = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
        mapTarget.Create(); map.targetTexture = mapTarget;
        try
        {
            Capture(map, mapTarget, "opaque_map_image");
            Check(Shader.GetGlobalTexture(OpaqueTexture) != null && Shader.GetGlobalTexture(OpaqueTexture) != red, "Disposable map did not publish a distinct opaque image.");
            Color32[] afterMap = Capture(camera, target, "perspective_after_map");
            Check(SamePixels(before, afterMap), "Water pixels consumed the preceding map camera image.");
            Shader.SetGlobalTexture(OpaqueTexture, green);
            Color32[] afterGreen = Capture(camera, target, "perspective_opaque_green");
            Check(Shader.GetGlobalTexture(OpaqueTexture) == green && SamePixels(before, afterGreen), "Water pixels depend on red/green opaque-texture sentinels.");
            report.AppendLine("PASS: Perspective water image is pixel-identical before/after an actual opaque-map camera render and with distinct red/green _CameraOpaqueTexture inputs. Original water color/depth/foam remains, without camera-color feedback.");
        }
        finally { map.targetTexture = null; mapTarget.Release(); Object.DestroyImmediate(mapTarget); }
    }
    private static Texture2D SolidTexture(Color color)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color[16]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels); texture.Apply(); return texture;
    }
    private static void ValidateDepthOnlyLifecycle(Scene scene, StringBuilder report)
    {
        GameObject obj = NewObject("Disposable MinimapFollow depth-only camera", scene); obj.SetActive(false);
        obj.AddComponent<Camera>().enabled = false;
        UniversalAdditionalCameraData data = obj.AddComponent<UniversalAdditionalCameraData>();
        MinimapFollow follow = obj.AddComponent<MinimapFollow>(); follow.enabled = false; obj.SetActive(true);
        foreach (CameraOverrideOption color in new[] { CameraOverrideOption.Off, CameraOverrideOption.UsePipelineSettings })
        {
            data.requiresDepthOption = CameraOverrideOption.Off; data.requiresColorOption = color;
            try
            {
                // Undo any editor-triggered callback before one explicit, controlled enable.
                follow.enabled = true; InvokeLifecycle(follow, "OnDisable"); InvokeLifecycle(follow, "OnEnable");
                Check(data.requiresDepthOption == CameraOverrideOption.On && data.requiresColorOption == color, "MinimapFollow changed the authored color-copy option on enable.");
            }
            finally { InvokeLifecycle(follow, "OnDisable"); follow.enabled = false; }
            Check(data.requiresDepthOption == CameraOverrideOption.Off && data.requiresColorOption == color, "MinimapFollow did not preserve color/restore depth on disable.");
        }
        report.AppendLine("PASS: Current MinimapFollow requests depth only and restores prior depth; Color Off and UsePipelineSettings remain unchanged on enable/disable. No authored camera or pipeline settings changed.");
    }
    private static void InvokeLifecycle(MinimapFollow follow, string name)
    {
        var method = typeof(MinimapFollow).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Check(method != null, "Current MinimapFollow lifecycle missing: " + name); method.Invoke(follow, null);
    }
    private static bool SamePixels(Color32[] a, Color32[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
        return true;
    }
    private static void ValidateAndroid(Shader shader, StringBuilder report)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
        { report.AppendLine("NOTE: Android module unavailable; native GLES3 compilation not performed."); return; }
        var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
        // GLES emits a combined vertex/fragment program through the Vertex request.
        foreach (ShaderType type in new[] { ShaderType.Vertex })
        {
            var result = pass.CompileVariant(type, Array.Empty<string>(), ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
            foreach (ShaderMessage message in result.Messages)
            {
                report.AppendLine("Android GLES3 " + message.severity + ": " + message.message);
                Check(message.severity != ShaderCompilerMessageSeverity.Error, "Android GLES3 compile error: " + message.message);
            }
            Check(result.Success && result.ShaderData != null && result.ShaderData.Length > 0, "Android GLES3 failed to emit " + type + " program.");
            File.WriteAllBytes("Temp/ExistingWaterFoam_AndroidGLES3.bin", result.ShaderData);
            string program = Encoding.UTF8.GetString(result.ShaderData);
            report.AppendLine("Android GLES3 emitted " + result.ShaderData.Length + " bytes; VERTEX header " + program.Contains("VERTEX") + ", FRAGMENT header " + program.Contains("FRAGMENT") + ".");
        }
        report.AppendLine("PASS: Actual Android GLES3 merged vertex/fragment variant compilation succeeded and emitted its program, without changing active target/settings.");
    }
    private static Mesh PlaneMesh()
    {
        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { new Vector3(-8, 0, -8), new Vector3(-8, 0, 8), new Vector3(8, 0, 8), new Vector3(8, 0, -8) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }; mesh.RecalculateBounds(); return mesh;
    }
    private static Color32[] Capture(Camera camera, RenderTexture target, string name)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        Check(RenderPipeline.SupportsRenderRequest(camera, request), "URP preview camera render request unsupported."); RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes("Temp/ExistingWaterFoam_" + name + ".png", image.EncodeToPNG()); return image.GetPixels32(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private sealed class SceneSnapshot
    {
        private readonly bool dirty; private readonly GameObject[] roots;
        public SceneSnapshot(Scene scene) { dirty = scene.isDirty; roots = scene.GetRootGameObjects(); }
        public void Assert(Scene scene) => Check(scene.isLoaded && scene.isDirty == dirty && Same(scene.GetRootGameObjects(), roots), "Authored scene roots/dirty flag changed: " + scene.path);
    }
    private static bool Same(Object[] a, Object[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
#endif
