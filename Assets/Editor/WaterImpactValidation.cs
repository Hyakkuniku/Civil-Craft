#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in isolated Water2 impact checks. No Play Mode, authored objects, scene saves or asset edits.</summary>
public static class WaterImpactValidation
{
    private const string ShaderPath = "Assets/Simple Water/Simple Water.shadergraph";
    private const string MaterialPath = "Assets/Simple Water/Water2.mat";
    private const string RequestPath = "Temp/WaterImpactValidation.request";
    private const string ReportPath = "Temp/WaterImpactValidation.txt";
    private const string Count = "_CCWaterImpactCount", Positions = "_CCWaterImpactPosition", Data = "_CCWaterImpactData";
    private const string BoatCount = "_CCBoatWakeCount", BoatPositions = "_CCBoatWakePosition", BoatMotions = "_CCBoatWakeMotion";
    private const string Profile = "_CCMinimapContactFoam", FoamColor = "Color_AFFD055F";
    private const float Waterline = -6.91843f;
    private static bool running;
    private static double nextCheck;
    private static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }
    private static bool Safe() => !running && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
        !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer && !ShaderUtil.anythingCompiling && PrefabStageUtility.GetCurrentPrefabStage() == null;
    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(RequestPath) || !Safe()) return;
        File.Delete(RequestPath); Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Water2 Falling Impacts")]
    public static void Validate()
    {
        if (!Safe()) { Debug.LogWarning("[Water2 impacts] Wait for Edit Mode and completed imports."); return; }
        running = true; Directory.CreateDirectory("Temp");
        var report = new StringBuilder("RUNNING: Isolated falling-object Water2 dips/ripples; no open-water foam.\nUTC: " + DateTime.UtcNow.ToString("O") + "\n");
        var scenes = new Dictionary<int, SceneSnapshot>();
        for (int i = 0; i < SceneManager.sceneCount; i++) { Scene s = SceneManager.GetSceneAt(i); scenes.Add(s.handle, new SceneSnapshot(s)); }
        var cameras = new Dictionary<Camera, RenderTexture>();
        foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>()) if (c != null && !EditorUtility.IsPersistent(c)) cameras.Add(c, c.targetTexture);
        var meshes = new Dictionary<MeshFilter, Mesh>();
        foreach (MeshFilter m in Resources.FindObjectsOfTypeAll<MeshFilter>()) if (m != null && !EditorUtility.IsPersistent(m)) meshes.Add(m, m.sharedMesh);
        var files = new Dictionary<string, string>(); var temporary = new List<Object>();
        var globals = new GlobalSnapshot();
        Object[] selection = Selection.objects; Object selected = Selection.activeObject;
        Scene active = SceneManager.GetActiveScene(), preview = default;
        RenderTexture oldTarget = RenderTexture.active, target = null;
        Camera camera = null; Material source = null; string sourceJson = null; bool sourceDirty = false, passed = false;
        try
        {
            foreach (string path in new[] { ShaderPath, MaterialPath, "Assets/Simple Water/WaterImpact.hlsl", "Assets/Simple Water/BoatWake.hlsl", "Assets/Simple Water/MapContactFoam.hlsl", "Assets/Simple Water/Depth Fade.shadersubgraph" }) files.Add(path, File.ReadAllText(path));
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath); source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Check(shader != null && source != null && source.shader == shader, "Current Water2 shader/material is not imported.");
            Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "This fixture requires current URP.");
            sourceJson = EditorJsonUtility.ToJson(source); sourceDirty = EditorUtility.IsDirty(source);
            foreach (string property in new[] { "_WaterImpactStrength", "_WaterDipDepth", "_WaterImpactRadius", "_WaterImpactDuration", "_BoatRippleStrength", FoamColor })
                Check(source.HasProperty(property), "Missing imported Water2 impact property: " + property);
            var water = new Material(source) { hideFlags = HideFlags.HideAndDontSave }; temporary.Add(water);
            var authored = new Material(source) { hideFlags = HideFlags.HideAndDontSave }; temporary.Add(authored);
            Freeze(water); Freeze(authored);
            Color blue = new Color(.025f, .18f, .38f, 1f);
            water.SetColor("Color_A271762B", blue); water.SetColor("Color_E0824245", blue); water.SetColor(FoamColor, Color.clear);
            water.SetFloat("_WaterImpactStrength", .65f); water.SetFloat("_WaterDipDepth", 0f);
            water.SetFloat("_WaterImpactRadius", 4f); water.SetFloat("_WaterImpactDuration", 1.8f);
            Shader.SetGlobalVector(Profile, Vector4.zero); PublishBoat(false); PublishImpact(false, Vector3.zero, 0f);
            ShaderUtil.CompilePass(water, 0, true);
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader)) { report.AppendLine("Shader " + message.severity + ": " + message.message); Check(message.severity != ShaderCompilerMessageSeverity.Error, "Shader compilation error: " + message.message); }
            Check(shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Water2 shader unsupported or failed to compile.");
            preview = EditorSceneManager.NewPreviewScene();
            ValidateAdaptiveSurface(preview, water, report);
            ValidateRuntime(preview, water, report);
            GameObject owner = NewObject("Disposable falling-water camera", preview);
            camera = owner.AddComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.nearClipPlane = .3f; camera.farClipPlane = 5000f; camera.orthographicSize = 17f; camera.fieldOfView = 52f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.015f, .02f, .025f);
            camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
            UniversalAdditionalCameraData cameraData = owner.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false; cameraData.requiresDepthOption = CameraOverrideOption.On; cameraData.requiresColorOption = CameraOverrideOption.Off;
            target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 }; target.Create(); camera.targetTexture = target;
            Shader opaque = Shader.Find("Universal Render Pipeline/Unlit"); Check(opaque != null, "Opaque fixture shader unavailable.");
            var bed = new Material(opaque) { hideFlags = HideFlags.HideAndDontSave }; bed.SetColor("_BaseColor", camera.backgroundColor); temporary.Add(bed);
            Primitive("Deep disposable riverbed", preview, new Vector3(0f, Waterline - 25f, 0f), new Vector3(100f, .2f, 100f), bed);
            Mesh mesh = GridMesh(40f, 64); temporary.Add(mesh);
            GameObject surface = NewObject("Subdivided disposable Water2 surface", preview); surface.transform.position = new Vector3(0f, Waterline, 0f);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh; Renderer renderer = surface.AddComponent<MeshRenderer>(); renderer.sharedMaterial = water;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            for (int view = 0; view < 2; view++)
            {
                camera.orthographic = view == 0;
                camera.transform.SetPositionAndRotation(view == 0 ? new Vector3(0f, Waterline + 40f, -5f) : new Vector3(0f, Waterline + 36f, -30f),
                    view == 0 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.LookRotation(new Vector3(0f, -36f, 25f)));
                string mode = view == 0 ? "orthographic" : "perspective";
                PublishImpact(false, new Vector3(0f, Waterline, 0f), 0f);
                Color32[] baseline = Capture(camera, target, mode + "_no_impact");
                PublishImpact(true, new Vector3(0f, Waterline, 0f), .15f);
                Color32[] early = Capture(camera, target, mode + "_impact_early");
                Locality(camera, target, baseline, early, mode, report);
                PublishImpact(true, new Vector3(0f, Waterline, 0f), .5f);
                Color32[] later = Capture(camera, target, mode + "_impact_later");
                Check(DifferencePixels(early, later, 4) > 100, "Impact age does not animate localized rings in " + mode + ".");
                PublishImpact(true, new Vector3(0f, Waterline, 0f), 3f);
                Check(SamePixels(baseline, Capture(camera, target, mode + "_impact_expired")), "Expired impact did not return baseline in " + mode + ".");
                water.SetFloat("_WaterImpactStrength", 0f); PublishImpact(true, new Vector3(0f, Waterline, 0f), .15f);
                Check(SamePixels(baseline, Capture(camera, target, mode + "_impact_strength_zero")), "Impact strength 0 did not return baseline in " + mode + ".");
                water.SetFloat("_WaterImpactStrength", .65f);
                PublishImpact(true, new Vector3(0f, Waterline + 100f, 0f), .15f);
                Check(SamePixels(baseline, Capture(camera, target, mode + "_different_water_level")), "Impact changed a vertically unrelated water surface in " + mode + ".");
                PublishBoat(true); PublishImpact(false, Vector3.zero, 0f);
                Color32[] boatOnly = Capture(camera, target, mode + "_boat_only");
                PublishImpact(true, new Vector3(0f, Waterline, 0f), .15f);
                Color32[] combined = Capture(camera, target, mode + "_boat_and_impact");
                Check(DifferencePixels(baseline, boatOnly, 4) > 100 && DifferencePixels(boatOnly, combined, 4) > 100, "Boat wake and falling impact did not coexist in " + mode + ".");
                PublishBoat(false); renderer.sharedMaterial = authored;
                PublishImpact(false, Vector3.zero, 0f); Color32[] authoredBaseline = Capture(camera, target, mode + "_authored_water2_no_impact");
                PublishImpact(true, new Vector3(0f, Waterline, 0f), .15f); Color32[] authoredHit = Capture(camera, target, mode + "_authored_water2_impact");
                Check(DifferencePixels(authoredBaseline, authoredHit, 4) > 50, "Actual Water2 colors hide the dip/ripples in " + mode + ".");
                renderer.sharedMaterial = water;
            }
            ValidateGpuDip(camera, target, water, surface.GetComponent<MeshFilter>(), renderer, report);
            ValidateExistingFoam(preview, camera, target, water, bed, report);
            ValidateAndroid(shader, report);
            report.AppendLine("PASS: Actual URP orthographic/perspective age-animated localized blue impact rings, expiry/disable, unchanged distant water, boat-wake coexistence, existing contact foam and true GPU vertex dip checked. No white open-water foam is introduced."); passed = true;
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
                RenderTexture.active = oldTarget; globals.Restore();
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Selection.objects = selection; Selection.activeObject = selected;
                foreach (KeyValuePair<string, string> file in files) Check(File.ReadAllText(file.Key) == file.Value, "Source asset changed: " + file.Key);
                if (sourceJson != null) Check(source != null && sourceJson == EditorJsonUtility.ToJson(source) && sourceDirty == EditorUtility.IsDirty(source), "Source Water2 material changed.");
                Check(SceneManager.sceneCount == scenes.Count, "Loaded scene count changed.");
                for (int i = 0; i < SceneManager.sceneCount; i++) { Scene s = SceneManager.GetSceneAt(i); Check(scenes.ContainsKey(s.handle), "Loaded scene replaced."); scenes[s.handle].Assert(s); }
                foreach (KeyValuePair<Camera, RenderTexture> pair in cameras) Check(pair.Key != null && pair.Key.targetTexture == pair.Value, "Authored camera target changed.");
                foreach (KeyValuePair<MeshFilter, Mesh> pair in meshes) Check(pair.Key != null && pair.Key.sharedMesh == pair.Value, "Authored mesh reference changed.");
                Check(SceneManager.GetActiveScene() == active && Same(Selection.objects, selection) && Selection.activeObject == selected && RenderTexture.active == oldTarget, "Editor selection/scene/target changed.");
                globals.Assert(); report.AppendLine("PASS isolation: source bytes/material dirty flag, authored scenes/roots/dirty flags/camera targets/mesh references, shader globals and editor selection restored. No saves or Play Mode.");
            }
            catch (Exception e) { passed = false; report.AppendLine("FAIL isolation: " + e); Debug.LogException(e); }
            report.AppendLine(passed ? "RESULT: PASS" : "RESULT: FAIL"); File.WriteAllText(ReportPath, report.ToString()); running = false;
            if (passed) Debug.Log("[Water2 impacts] PASS. Report: " + ReportPath);
        }
    }

    private static void Freeze(Material m) { m.SetFloat("_BoatRippleSpeed", 0f); m.SetFloat("Vector1_E23798DE", 0f); m.SetFloat("Vector1_29A8EAB", 0f); }
    private static void PublishImpact(bool active, Vector3 position, float age)
    {
        var positions = new Vector4[8]; var data = new Vector4[8]; positions[0] = new Vector4(position.x, position.y, position.z, age); data[0] = new Vector4(1f, 1f, 1.8f, 0f);
        Shader.SetGlobalVectorArray(Positions, positions); Shader.SetGlobalVectorArray(Data, data); Shader.SetGlobalFloat(Count, active ? 1f : 0f);
    }
    private static void PublishBoat(bool active)
    {
        var positions = new Vector4[4]; var motions = new Vector4[4]; positions[0] = new Vector4(-6f, Waterline, 3f, 1f); motions[0] = new Vector4(0f, 1f, 1.8f, 3f);
        Shader.SetGlobalVectorArray(BoatPositions, positions); Shader.SetGlobalVectorArray(BoatMotions, motions); Shader.SetGlobalFloat(BoatCount, active ? 1f : 0f);
    }
    private static void Locality(Camera camera, RenderTexture target, Color32[] before, Color32[] after, string mode, StringBuilder report)
    {
        int near = 0, nearDelta = 0, far = 0, farDelta = 0, white = 0, magenta = 0; var plane = new Plane(Vector3.up, new Vector3(0f, Waterline, 0f));
        for (int y = 0; y < target.height; y++) for (int x = 0; x < target.width; x++)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3((x + .5f) / target.width, (y + .5f) / target.height, 0f)); if (!plane.Raycast(ray, out float distance)) continue;
            Vector3 point = ray.GetPoint(distance); if (Mathf.Abs(point.x) > 30f || Mathf.Abs(point.z) > 25f) continue; int i = y * target.width + x;
            int delta = Delta(before[i], after[i]); if (Mathf.Abs(point.x) < 10f && Mathf.Abs(point.z) < 10f) { near++; if (delta > 4) nearDelta++; }
            if (Mathf.Abs(point.x) > 15f && Mathf.Abs(point.x) < 27f) { far++; if (delta > 0) farDelta++; }
            if (after[i].r > 150 && Mathf.Abs(after[i].r - after[i].g) < 25 && Mathf.Abs(after[i].r - after[i].b) < 25) white++;
            if (after[i].r > 200 && after[i].b > 200 && after[i].g < 80) magenta++;
        }
        report.AppendLine(mode + ": nearby blue ripple pixels " + nearDelta + "/" + near + ", distant-water changed " + farDelta + "/" + far + ", white open-water foam " + white + ", magenta " + magenta + ".");
        Check(nearDelta > 100 && far > 1000 && farDelta == 0 && white == 0 && magenta == 0, "Localized no-foam impact render failed in " + mode + ".");
    }
    private static void ValidateGpuDip(Camera camera, RenderTexture target, Material water, MeshFilter filter, Renderer renderer, StringBuilder report)
    {
        // Fragment ripple strength is zero: ONLY a changed GPU vertex silhouette can alter these pixels.
        PublishBoat(false); water.SetFloat("_WaterImpactStrength", 0f); water.SetFloat("_WaterDipDepth", 0f);
        camera.orthographic = true; camera.orthographicSize = 6f;
        camera.transform.position = new Vector3(0f, Waterline + 6f, -52f); camera.transform.LookAt(new Vector3(0f, Waterline, -36f));
        Type type = typeof(BoatBridgeCrossing).Assembly.GetType("WaterImpactSurfaceMesh");
        MethodInfo create = type.GetMethod("TryCreate", StaticPrivate | BindingFlags.Public), rebuild = type.GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), dispose = type.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Mesh original = filter.sharedMesh; object helper = null;
        try
        {
            object[] args = { filter, renderer, null }; Check((bool)create.Invoke(null, args), "Adaptive native plane could not be prepared."); helper = args[2];
            var center = new Vector3(0f, Waterline, -39f);
            rebuild.Invoke(helper, new object[] { new[] { center }, new[] { 4f }, 1, .45f });
            Vector3[] cpuBefore = filter.sharedMesh.vertices;
            PublishImpact(true, center, .15f); Color32[] flat = Capture(camera, target, "gpu_geometry_depth_zero");
            water.SetFloat("_WaterDipDepth", .45f); Color32[] dipped = Capture(camera, target, "gpu_geometry_dip_active");
            int changed = DifferencePixels(flat, dipped, 2); report.AppendLine("GPU vertex-only dip on actual adaptive surface with original .45m depth: " + changed + " silhouette pixels changed (fragment impact strength 0).");
            Check(changed > 30, "The impact does not visibly displace GPU water geometry.");
            Vector3[] cpuAfter = filter.sharedMesh.vertices; Check(cpuBefore.Length == cpuAfter.Length, "GPU render changed the CPU mesh size.");
            for (int i = 0; i < cpuBefore.Length; i++) Check(cpuBefore[i] == cpuAfter[i], "GPU rendering changed CPU water vertices.");
            PublishImpact(false, Vector3.zero, 0f); Check(SamePixels(flat, Capture(camera, target, "gpu_geometry_no_hit")), "GPU dip did not stop when impact count became 0.");
        }
        finally
        {
            if (helper != null) dispose.Invoke(helper, null);
            Check(filter.sharedMesh == original, "Native adaptive dip test did not restore its original mesh reference.");
            water.SetFloat("_WaterDipDepth", 0f); water.SetFloat("_WaterImpactStrength", .65f);
        }
    }
    private static void ValidateExistingFoam(Scene scene, Camera camera, RenderTexture target, Material water, Material bed, StringBuilder report)
    {
        Primitive("Disposable contact foam shoreline", scene, new Vector3(0f, Waterline, 0f), new Vector3(4f, 3f, 4f), bed);
        camera.orthographic = true; camera.orthographicSize = 8f; camera.transform.position = new Vector3(0f, Waterline + 16f, -16f); camera.transform.LookAt(new Vector3(0f, Waterline, 0f));
        water.SetColor(FoamColor, new Color(.7454045f, .7454045f, .7454045f, 1f)); PublishImpact(false, Vector3.zero, 0f);
        Color32[] foam = Capture(camera, target, "existing_contact_foam"); PublishImpact(true, new Vector3(-6f, Waterline, 0f), .15f);
        Color32[] hit = Capture(camera, target, "existing_contact_foam_with_impact"); int preserved = 0;
        for (int i = 0; i < foam.Length; i++) if (foam[i].r > 165 && Mathf.Abs(foam[i].r - foam[i].g) < 20 && Mathf.Abs(foam[i].r - foam[i].b) < 20 && Delta(foam[i], hit[i]) == 0) preserved++;
        Check(preserved > 50, "Existing shoreline foam absent or replaced by falling impact."); report.AppendLine("Existing contact foam retained exactly: " + preserved + " pixels.");
    }
    private static void ValidateAndroid(Shader shader, StringBuilder report)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)) { report.AppendLine("NOTE: Android module unavailable; actual GLES3 variant not compiled."); return; }
        var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0); var result = pass.CompileVariant(ShaderType.Vertex, Array.Empty<string>(), ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
        foreach (ShaderMessage message in result.Messages) { report.AppendLine("Android GLES3 " + message.severity + ": " + message.message); Check(message.severity != ShaderCompilerMessageSeverity.Error, "GLES3 shader error: " + message.message); }
        Check(result.Success && result.ShaderData != null && result.ShaderData.Length > 0, "GLES3 failed to emit its combined vertex/fragment program.");
        File.WriteAllBytes("Temp/WaterImpact_AndroidGLES3.bin", result.ShaderData); report.AppendLine("PASS: Actual GLES3 vertex+fragment program emitted " + result.ShaderData.Length + " bytes, without target/settings changes.");
    }
    private static void ValidateAdaptiveSurface(Scene scene, Material water, StringBuilder report)
    {
        Type type = typeof(BoatBridgeCrossing).Assembly.GetType("WaterImpactSurfaceMesh"); Check(type != null, "Adaptive water surface helper not imported.");
        MethodInfo create = type.GetMethod("TryCreate", StaticPrivate | BindingFlags.Public);
        MethodInfo rebuild = type.GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo dispose = type.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Check(create != null && rebuild != null && dispose != null, "Adaptive water surface helper contract missing.");
        GameObject owner = Primitive("Disposable Town-size Water2 cube", scene, new Vector3(0f, -24f, 0f), new Vector3(1024f, 34f, 2500f), water);
        MeshFilter filter = owner.GetComponent<MeshFilter>(); Renderer renderer = owner.GetComponent<Renderer>();
        Mesh original = Object.Instantiate(filter.sharedMesh); original.hideFlags = HideFlags.HideAndDontSave;
        Vector3[] vertices = original.vertices;
        // Match the requested actual Town waterline while retaining the offset -24m root.
        float localTop = (Waterline + 24f) / 34f;
        for (int i = 0; i < vertices.Length; i++) if (vertices[i].y > 0f) vertices[i].y = localTop;
        original.vertices = vertices; original.RecalculateBounds(); filter.sharedMesh = original;
        Vector2[] uv = original.uv; int[] triangles = original.triangles; Vector3[] normals = original.normals;
        var extraUv = new List<Vector4>(); var colors = new Color[vertices.Length]; var tangents = new Vector4[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) { extraUv.Add(new Vector4(vertices[i].x, vertices[i].z, .25f, 1f)); colors[i] = new Color(.2f + i * .01f, .4f, .6f, 1f); tangents[i] = new Vector4(1f, 0f, 0f, 1f); }
        original.SetUVs(7, extraUv); original.colors = colors; original.tangents = tangents;
        object helper = null;
        try
        {
            object[] args = { filter, renderer, null }; Check((bool)create.Invoke(null, args), "A readable Town-size rectangular Water2 cube was rejected."); helper = args[2];
            Check(helper != null && filter.sharedMesh != original, "Adaptive helper did not provide a disposable runtime mesh.");
            var centers = new Vector3[8]; var radii = new float[8];
            for (int i = 0; i < 8; i++) { centers[i] = new Vector3((i - 3.5f) * 70f, Waterline, i % 2 == 0 ? -400f : 400f); radii[i] = 4f; }
            rebuild.Invoke(helper, new object[] { centers, radii, 8, .45f });
            Mesh adaptive = filter.sharedMesh; Check(adaptive.vertexCount > vertices.Length && adaptive.vertexCount <= 5500, "Adaptive Town water mesh exceeds its bounded geometry budget.");
            Vector3[] grown = adaptive.vertices; Vector3[] grownNormals = adaptive.normals; Vector2[] grownUv = adaptive.uv; Color[] grownColors = adaptive.colors; Vector4[] grownTangents = adaptive.tangents;
            var grownExtra = new List<Vector4>(); adaptive.GetUVs(7, grownExtra);
            Check(grown.Length >= vertices.Length && grownNormals.Length >= normals.Length && grownUv.Length >= uv.Length && grownColors.Length >= colors.Length && grownTangents.Length >= tangents.Length && grownExtra.Count >= extraUv.Count, "Adaptive surface lost original vertex attributes.");
            for (int i = 0; i < vertices.Length; i++) Check(grown[i] == vertices[i] && grownNormals[i] == normals[i] && grownUv[i] == uv[i] && grownColors[i] == colors[i] && grownTangents[i] == tangents[i] && grownExtra[i] == extraUv[i], "Adaptive surface altered an authored vertex/normal/UV/color/tangent.");
            int[] grownTriangles = adaptive.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                bool top = Mathf.Abs(vertices[triangles[i]].y - localTop) < .00001f && Mathf.Abs(vertices[triangles[i + 1]].y - localTop) < .00001f && Mathf.Abs(vertices[triangles[i + 2]].y - localTop) < .00001f;
                if (top) continue; bool retained = false;
                for (int j = 0; j < grownTriangles.Length; j += 3) if (grownTriangles[j] == triangles[i] && grownTriangles[j + 1] == triangles[i + 1] && grownTriangles[j + 2] == triangles[i + 2]) { retained = true; break; }
                Check(retained, "Adaptive surface changed a non-top cube triangle.");
            }
            for (int i = 0; i < centers.Length; i++)
            {
                float nearest = float.MaxValue;
                for (int j = vertices.Length; j < grown.Length; j++) { Vector3 world = owner.transform.TransformPoint(grown[j]); nearest = Mathf.Min(nearest, new Vector2(world.x - centers[i].x, world.z - centers[i].z).sqrMagnitude); }
                Check(nearest < 1f, "Town-size mesh lacks local vertices at an impact footprint.");
            }
            float actualTop = owner.transform.TransformPoint(new Vector3(0f, original.bounds.max.y, 0f)).y;
            float grownTop = float.NegativeInfinity; foreach (Vector3 vertex in grown) grownTop = Mathf.Max(grownTop, owner.transform.TransformPoint(vertex).y);
            Check(Mathf.Abs(actualTop - Waterline) < .001f && Mathf.Abs(grownTop - actualTop) < .001f, "Adaptive helper changed actual Town top-face vertices.");
            Check(renderer.bounds.max.y >= actualTop + .134f, "Adaptive culling bounds do not include the .3*DipDepth positive rebound.");
            report.AppendLine("PASS adaptive Town cube: actual original-mesh top " + actualTop.ToString("0.00000") + "m vs root -24m (renderer bounds include rebound separately); " + adaptive.vertexCount + " vertices for eight separated 4m footprints over 1024x2500m; original non-top triangles, normals, UV0/UV7, colors/tangents retained.");
            dispose.Invoke(helper, null); helper = null; Check(filter.sharedMesh == original, "Adaptive helper did not restore the original mesh reference on Dispose.");
            owner.transform.rotation = Quaternion.Euler(12f, 0f, 0f); args = new object[] { filter, renderer, null };
            Check(!(bool)create.Invoke(null, args) && filter.sharedMesh == original, "Unsupported tilted water was modified rather than safely skipped.");
        }
        finally { if (helper != null) dispose.Invoke(helper, null); if (owner != null) Object.DestroyImmediate(owner); if (original != null) Object.DestroyImmediate(original); }
    }

    private static void ValidateRuntime(Scene scene, Material water, StringBuilder report)
    {
        Type type = typeof(BoatBridgeCrossing).Assembly.GetType("WaterImpact"); Check(type != null, "WaterImpact runtime sampler is not imported.");
        var register = type.GetMethod("Register", StaticPrivate); var unregister = type.GetMethod("Unregister", StaticPrivate);
        var registerSurface = type.GetMethod("RegisterSurface", StaticPrivate); var sample = type.GetMethod("SampleNow", StaticPrivate); var clear = type.GetMethod("ClearGlobals", StaticPrivate);
        Check(register != null && unregister != null && registerSurface != null && sample != null && clear != null, "Current WaterImpact runtime API incomplete.");
        var state = new RuntimeState(type); var globals = new GlobalSnapshot(); var temporary = new List<GameObject>(); var temporaryMeshes = new List<Mesh>();
        Action<float> tick = dt => sample.Invoke(null, new object[] { dt });
        Action<GameObject, Rigidbody> add = (obj, body) => register.Invoke(null, new object[] { obj.transform, new[] { obj.GetComponent<Renderer>() }, body, null, body != null ? body.mass : 1f });
        Action<GameObject> remove = obj => unregister.Invoke(null, new object[] { obj.transform });
        try
        {
            state.Clear(); clear.Invoke(null, null);
            GameObject surface = NewObject("Disposable actual-waterline surface", scene); temporary.Add(surface); surface.transform.position = new Vector3(0f, Waterline, 0f);
            Mesh mesh = GridMesh(40f, 1); temporaryMeshes.Add(mesh); MeshFilter filter = surface.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            Renderer waterRenderer = surface.AddComponent<MeshRenderer>(); waterRenderer.sharedMaterial = water;
            registerSurface.Invoke(null, new object[] { filter, waterRenderer });
            Check(state.List("surfaces").Count == 1, "Opted-in Water2 surface was not registered.");
            GameObject falling = Primitive("Disposable falling vehicle mesh", scene, new Vector3(0f, Waterline + 2f, 0f), new Vector3(1f, 1f, 2f), null); temporary.Add(falling);
            Rigidbody body = falling.AddComponent<Rigidbody>(); body.useGravity = false; body.mass = 1000f; body.velocity = Vector3.zero;
            add(falling, body); tick(.1f);
            Check(state.List("impacts").Count == 0 && Shader.GetGlobalFloat(Count) == 0f, "First/static sample created an impact.");
            body.velocity = new Vector3(0f, -5f, 0f); falling.transform.position += new Vector3(0f, -.5f, 0f); tick(.1f);
            Check(state.List("impacts").Count == 0, "Falling object above water generated an early impact.");
            falling.transform.position = new Vector3(0f, Waterline + .1f, 0f); body.velocity = new Vector3(0f, -14f, 0f); tick(.1f);
            Check(state.List("impacts").Count == 1 && Shader.GetGlobalFloat(Count) == 1f, "Actual downward hull crossing did not generate exactly one impact.");
            Vector4[] output = Shader.GetGlobalVectorArray(Positions), detail = Shader.GetGlobalVectorArray(Data);
            Check(Mathf.Abs(output[0].y - Waterline) < .001f && detail[0].x > 0f && detail[0].x <= 1f, "Impact used the object/root height instead of actual water top, or has invalid severity.");
            float heavySeverity = detail[0].x;
            falling.transform.position += Vector3.down; tick(.1f); falling.transform.position += Vector3.down; tick(.1f);
            Check(state.List("impacts").Count == 1, "Already-submerged object emitted repeated impacts.");
            for (int i = 0; i < 30; i++) tick(.1f);
            Check(state.List("impacts").Count == 0 && Shader.GetGlobalFloat(Count) == 0f, "Impact expiry left shader/event data alive.");
            remove(falling); Object.DestroyImmediate(falling); temporary.Remove(falling);

            GameObject guest = Primitive("Disposable script-free guest bridge part", scene, new Vector3(4f, Waterline + 1f, 0f), Vector3.one, null); temporary.Add(guest);
            add(guest, null); tick(.1f); guest.transform.position = new Vector3(4f, Waterline + .1f, 0f); tick(.1f);
            Check(state.List("impacts").Count == 1 && guest.GetComponents<MonoBehaviour>().Length == 0, "Mesh-only spectator part failed to create an impact or gained scripts.");
            for (int i = 0; i < 30; i++) tick(.1f); remove(guest); Object.DestroyImmediate(guest); temporary.Remove(guest);

            float lightSeverity = CrossingCase(scene, temporary, add, remove, tick, state, new Vector3(0f, Waterline + 1f, 0f), new Vector3(0f, Waterline + .1f, 0f), 20f, 2, true);
            Check(heavySeverity >= lightSeverity, "Heavier/faster falling vehicle produced a weaker impact than a light crossing.");
            CrossingCase(scene, temporary, add, remove, tick, state, new Vector3(0f, Waterline - 1f, 0f), new Vector3(0f, Waterline + 1f, 0f), 20f, 0, false);
            CrossingCase(scene, temporary, add, remove, tick, state, new Vector3(0f, Waterline + 1f, 0f), new Vector3(0f, Waterline + .95f, 0f), 20f, 0, false);
            CrossingCase(scene, temporary, add, remove, tick, state, new Vector3(80f, Waterline + 1f, 0f), new Vector3(80f, Waterline - 1f, 0f), 20f, 0, false);
            CrossingCase(scene, temporary, add, remove, tick, state, new Vector3(0f, Waterline + 1f, 0f), new Vector3(200f, Waterline - 1f, 0f), 20f, 0, false);
            // Ten simultaneous parts must remain bounded to eight cosmetic events.
            for (int i = 0; i < 10; i++) { GameObject part = Primitive("Disposable bounded falling part " + i, scene, new Vector3(i - 4.5f, Waterline + 1f, 3f), Vector3.one, null); temporary.Add(part); add(part, null); }
            tick(.1f); foreach (GameObject part in temporary) if (part.name.StartsWith("Disposable bounded", StringComparison.Ordinal)) part.transform.position += new Vector3(0f, -.9f, 0f); tick(.1f);
            Check(state.List("impacts").Count <= 8 && Shader.GetGlobalFloat(Count) <= 8f && Shader.GetGlobalFloat(Count) > 0f, "Impact event budget exceeded eight entries or omitted all crossings.");
            for (int i = 0; i < 30; i++) tick(.1f);
            Check(state.List("impacts").Count == 0 && Shader.GetGlobalFloat(Count) == 0f, "Bounded impacts did not clear after expiry.");
            foreach (Vector4 vector in Shader.GetGlobalVectorArray(Positions)) Check(vector == Vector4.zero, "Expired impacts left stale positions.");
            foreach (Vector4 vector in Shader.GetGlobalVectorArray(Data)) Check(vector == Vector4.zero, "Expired impacts left stale data.");
            Check(state.SamplerUnchanged, "Edit Mode registry created a persistent sampler.");
            report.AppendLine("PASS runtime crossings: first/static/above/underwater/upward/outside/teleport excluded; downward actual-top crossings emit once; heavy-fast severity " + heavySeverity.ToString("0.000") + " >= light severity " + lightSeverity.ToString("0.000") + "; script-free guests, eight-event cap, expiry and zeroed arrays checked.");
        }
        finally { state.Restore(); foreach (GameObject obj in temporary) if (obj != null) Object.DestroyImmediate(obj); foreach (Mesh mesh in temporaryMeshes) if (mesh != null) Object.DestroyImmediate(mesh); globals.Restore(); }
    }

    private static float CrossingCase(Scene scene, List<GameObject> objects, Action<GameObject, Rigidbody> add, Action<GameObject> remove, Action<float> tick, RuntimeState state,
        Vector3 previous, Vector3 current, float mass, int description, bool expected)
    {
        GameObject obj = Primitive("Disposable crossing case " + description, scene, previous, Vector3.one, null); objects.Add(obj);
        Rigidbody body = obj.AddComponent<Rigidbody>(); body.useGravity = false; body.mass = mass; add(obj, body); tick(.1f);
        obj.transform.position = current; body.velocity = (current - previous) / .1f; tick(.1f);
        Check((state.List("impacts").Count > 0) == expected, "Crossing exclusion/emission case failed: " + description + " (" + previous + " -> " + current + ").");
        float severity = expected ? Shader.GetGlobalVectorArray(Data)[0].x : 0f;
        remove(obj); Object.DestroyImmediate(obj); objects.Remove(obj); for (int i = 0; i < 30; i++) tick(.1f); return severity;
    }

    private sealed class RuntimeState
    {
        private readonly Type type; private readonly Dictionary<string, object[]> lists = new Dictionary<string, object[]>();
        private readonly Vector4[] positions, data, originalPositions, originalData; private readonly object count, quitting, sampler;
        private readonly Vector3[] centers, originalCenters; private readonly float[] radii, originalRadii;
        public RuntimeState(Type type)
        {
            this.type = type;
            foreach (string name in new[] { "sources", "surfaces", "impacts" }) { IList list = List(name); var snapshot = new object[list.Count]; list.CopyTo(snapshot, 0); lists.Add(name, snapshot); }
            positions = (Vector4[])Field("positions").GetValue(null); data = (Vector4[])Field("data").GetValue(null); originalPositions = (Vector4[])positions.Clone(); originalData = (Vector4[])data.Clone();
            centers = (Vector3[])Field("rebuildCenters").GetValue(null); radii = (float[])Field("rebuildRadii").GetValue(null); originalCenters = (Vector3[])centers.Clone(); originalRadii = (float[])radii.Clone();
            count = Field("outputCount").GetValue(null); quitting = Field("quitting").GetValue(null); sampler = Field("sampler").GetValue(null);
        }
        private FieldInfo Field(string name) { FieldInfo f = type.GetField(name, StaticPrivate); Check(f != null, "Runtime field missing: " + name); return f; }
        public IList List(string name) => (IList)Field(name).GetValue(null);
        public bool SamplerUnchanged => Equals(Field("sampler").GetValue(null), sampler);
        public void Clear() { foreach (string name in lists.Keys) List(name).Clear(); Field("quitting").SetValue(null, false); }
        public void Restore()
        {
            Object current = Field("sampler").GetValue(null) as Object;
            if (current != null && !Equals(current, sampler)) { if (current is Component c) Object.DestroyImmediate(c.gameObject); else Object.DestroyImmediate(current); }
            // Dispose ONLY helpers on this fixture's disposable surfaces before restoring untouched prior entries.
            foreach (object surface in List("surfaces"))
            {
                foreach (FieldInfo field in surface.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (field.GetValue(surface) is IDisposable disposable) disposable.Dispose();
            }
            foreach (KeyValuePair<string, object[]> pair in lists) { IList list = List(pair.Key); list.Clear(); foreach (object entry in pair.Value) list.Add(entry); }
            Array.Copy(originalPositions, positions, positions.Length); Array.Copy(originalData, data, data.Length);
            Array.Copy(originalCenters, centers, centers.Length); Array.Copy(originalRadii, radii, radii.Length);
            Field("outputCount").SetValue(null, count); Field("quitting").SetValue(null, quitting); Field("sampler").SetValue(null, sampler);
            foreach (KeyValuePair<string, object[]> pair in lists) { IList list = List(pair.Key); Check(list.Count == pair.Value.Length, "Runtime list not restored."); for (int i = 0; i < list.Count; i++) Check(pair.Value[i].GetType().IsValueType ? Equals(list[i], pair.Value[i]) : ReferenceEquals(list[i], pair.Value[i]), "Prior runtime entry replaced."); }
            Check(SameVectors(positions, originalPositions) && SameVectors(data, originalData) && SamplerUnchanged, "Runtime arrays/sampler not restored.");
            for (int i = 0; i < centers.Length; i++) Check(centers[i] == originalCenters[i] && radii[i] == originalRadii[i], "Runtime mesh scratch arrays not restored.");
        }
    }
    private static GameObject NewObject(string name, Scene scene) { var obj = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(obj, scene); return obj; }
    private static GameObject Primitive(string name, Scene scene, Vector3 position, Vector3 scale, Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(obj, scene);
        obj.name = name; obj.transform.position = position; obj.transform.localScale = scale; Object.DestroyImmediate(obj.GetComponent<Collider>());
        Renderer r = obj.GetComponent<Renderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; return obj;
    }
    private static Mesh GridMesh(float extent, int cells)
    {
        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave }; int row = cells + 1;
        var vertices = new Vector3[row * row]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length]; var triangles = new int[cells * cells * 6];
        for (int z = 0; z < row; z++) for (int x = 0; x < row; x++) { int i = z * row + x; vertices[i] = new Vector3(-extent + 2f * extent * x / cells, 0f, -extent + 2f * extent * z / cells); normals[i] = Vector3.up; uv[i] = new Vector2((float)x / cells, (float)z / cells); }
        int t = 0; for (int z = 0; z < cells; z++) for (int x = 0; x < cells; x++) { int i = z * row + x; triangles[t++] = i; triangles[t++] = i + row; triangles[t++] = i + 1; triangles[t++] = i + 1; triangles[t++] = i + row; triangles[t++] = i + row + 1; }
        mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateBounds(); return mesh;
    }
    private static Color32[] Capture(Camera camera, RenderTexture target, string name)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target }; Check(RenderPipeline.SupportsRenderRequest(camera, request), "URP preview render unsupported."); RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes("Temp/WaterImpact_" + name + ".png", image.EncodeToPNG()); return image.GetPixels32(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private static int Delta(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
    private static int DifferencePixels(Color32[] a, Color32[] b, int threshold) { int n = 0; for (int i = 0; i < a.Length; i++) if (Delta(a[i], b[i]) > threshold) n++; return n; }
    private static bool SamePixels(Color32[] a, Color32[] b) => a.Length == b.Length && DifferencePixels(a, b, 0) == 0;
    private static bool SameVectors(Vector4[] a, Vector4[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static bool Same(Object[] a, Object[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class SceneSnapshot
    {
        private readonly bool dirty; private readonly GameObject[] roots;
        public SceneSnapshot(Scene scene) { dirty = scene.isDirty; roots = scene.GetRootGameObjects(); }
        public void Assert(Scene scene) => Check(scene.isLoaded && scene.isDirty == dirty && Same(scene.GetRootGameObjects(), roots), "Authored roots/dirty flag changed: " + scene.path);
    }
    private sealed class GlobalSnapshot
    {
        private readonly float impactCount = Shader.GetGlobalFloat(Count), boatCount = Shader.GetGlobalFloat(BoatCount);
        private readonly Vector4[] impacts = Shader.GetGlobalVectorArray(Positions), impactData = Shader.GetGlobalVectorArray(Data), boats = Shader.GetGlobalVectorArray(BoatPositions), boatMotion = Shader.GetGlobalVectorArray(BoatMotions);
        private readonly Vector4 profile = Shader.GetGlobalVector(Profile), opaqueTexel = Shader.GetGlobalVector("_CameraOpaqueTexture_TexelSize");
        private readonly Texture opaque = Shader.GetGlobalTexture("_CameraOpaqueTexture");
        public void Restore()
        {
            Shader.SetGlobalFloat(Count, impactCount); Shader.SetGlobalFloat(BoatCount, boatCount);
            RestoreArray(Positions, impacts, 8); RestoreArray(Data, impactData, 8); RestoreArray(BoatPositions, boats, 4); RestoreArray(BoatMotions, boatMotion, 4);
            Shader.SetGlobalVector(Profile, profile); Shader.SetGlobalVector("_CameraOpaqueTexture_TexelSize", opaqueTexel); Shader.SetGlobalTexture("_CameraOpaqueTexture", opaque);
        }
        public void Assert()
        {
            Check(Shader.GetGlobalFloat(Count) == impactCount && Shader.GetGlobalFloat(BoatCount) == boatCount && Shader.GetGlobalVector(Profile) == profile && Shader.GetGlobalVector("_CameraOpaqueTexture_TexelSize") == opaqueTexel && Shader.GetGlobalTexture("_CameraOpaqueTexture") == opaque, "Shader globals not restored.");
            AssertArray(Positions, impacts); AssertArray(Data, impactData); AssertArray(BoatPositions, boats); AssertArray(BoatMotions, boatMotion);
        }
        private static void RestoreArray(string name, Vector4[] value, int size) => Shader.SetGlobalVectorArray(name, value != null && value.Length > 0 ? value : new Vector4[size]);
        private static void AssertArray(string name, Vector4[] value) { if (value != null && value.Length > 0) Check(SameVectors(value, Shader.GetGlobalVectorArray(name)), "Global array not restored: " + name); }
    }
}
#endif
