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

/// <summary>Opt-in, isolated Water2 boat-ripple rendering check. Never saves or enters Play Mode.</summary>
public static class BoatWaterWakeValidation
{
    private const string ShaderPath = "Assets/Simple Water/Simple Water.shadergraph";
    private const string MaterialPath = "Assets/Simple Water/Water2.mat";
    private const string RequestPath = "Temp/BoatWaterWakeValidation.request";
    private const string ReportPath = "Temp/BoatWaterWakeValidation.txt";
    private const string Count = "_CCBoatWakeCount";
    private const string Positions = "_CCBoatWakePosition";
    private const string Motions = "_CCBoatWakeMotion";
    private const string Profile = "_CCMinimapContactFoam";
    private const string FoamColor = "Color_AFFD055F";
    private static bool running;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool Safe() => !running && !EditorApplication.isPlayingOrWillChangePlaymode &&
        !EditorApplication.isCompiling && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
        !ShaderUtil.anythingCompiling && PrefabStageUtility.GetCurrentPrefabStage() == null;

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2d;
        if (!File.Exists(RequestPath) || !Safe()) return;
        File.Delete(RequestPath);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Water2 Boat Ripples")]
    public static void Validate()
    {
        if (!Safe()) { Debug.LogWarning("[Water2 boat ripples] Wait for Edit Mode and completed imports."); return; }
        running = true;
        Directory.CreateDirectory("Temp");
        var report = new StringBuilder("RUNNING: Isolated Water2 boat ripples; no open-water foam.\nUTC: " + DateTime.UtcNow.ToString("O") + "\n");
        var scenes = new Dictionary<int, SceneSnapshot>();
        for (int i = 0; i < SceneManager.sceneCount; i++) { Scene s = SceneManager.GetSceneAt(i); scenes.Add(s.handle, new SceneSnapshot(s)); }
        var cameraTargets = new Dictionary<Camera, RenderTexture>();
        foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            if (c != null && !EditorUtility.IsPersistent(c)) cameraTargets.Add(c, c.targetTexture);
        var sourceFiles = new Dictionary<string, string>();
        var temporary = new List<Object>();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        Scene active = SceneManager.GetActiveScene(), preview = default;
        RenderTexture priorTarget = RenderTexture.active, target = null;
        float priorCount = Shader.GetGlobalFloat(Count);
        Vector4[] priorPositions = Shader.GetGlobalVectorArray(Positions), priorMotions = Shader.GetGlobalVectorArray(Motions);
        Vector4 priorProfile = Shader.GetGlobalVector(Profile);
        Texture priorOpaque = Shader.GetGlobalTexture("_CameraOpaqueTexture");
        Vector4 priorOpaqueTexel = Shader.GetGlobalVector("_CameraOpaqueTexture_TexelSize");
        Camera camera = null;
        Material source = null;
        string sourceJson = null;
        bool sourceDirty = false, passed = false;
        try
        {
            foreach (string path in new[] { ShaderPath, MaterialPath, "Assets/Simple Water/BoatWake.hlsl", "Assets/Simple Water/MapContactFoam.hlsl", "Assets/Simple Water/Depth Fade.shadersubgraph" })
                sourceFiles.Add(path, File.ReadAllText(path));
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Check(shader != null && source != null && source.shader == shader, "Current Water2 shader/material is not imported.");
            Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "This fixture requires the current URP pipeline.");
            sourceJson = EditorJsonUtility.ToJson(source); sourceDirty = EditorUtility.IsDirty(source);
            foreach (string property in new[] { "_BoatRippleStrength", "_BoatWakeWidth", "_BoatWakeLength", "_BoatRippleSpacing", "_BoatRippleSpeed", FoamColor })
                Check(source.HasProperty(property), "Missing imported boat ripple property: " + property);
            Material water = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            temporary.Add(water);
            Material authored = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            temporary.Add(authored);
            authored.SetFloat("_BoatRippleSpeed", 0f); authored.SetFloat("Vector1_E23798DE", 0f); authored.SetFloat("Vector1_29A8EAB", 0f);
            Check(authored.GetFloat("_BoatRippleStrength") > 0f, "Water2 is not opted in to the boat ripple effect.");
            // Only disposable copies use a fixed animation phase and a uniform blue depth color.
            Color blue = new Color(.025f, .18f, .38f, 1f);
            water.SetColor("Color_A271762B", blue); water.SetColor("Color_E0824245", blue);
            water.SetColor(FoamColor, Color.clear);
            water.SetFloat("Vector1_E23798DE", 0f); water.SetFloat("Vector1_29A8EAB", 0f);
            water.SetFloat("_BoatRippleStrength", .65f); water.SetFloat("_BoatWakeWidth", 3f);
            water.SetFloat("_BoatWakeLength", 18f); water.SetFloat("_BoatRippleSpacing", 2.5f); water.SetFloat("_BoatRippleSpeed", 0f);
            Shader.SetGlobalVector(Profile, Vector4.zero);
            ShaderUtil.CompilePass(water, 0, true);
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
            {
                report.AppendLine("Shader " + message.severity + ": " + message.message);
                Check(message.severity != ShaderCompilerMessageSeverity.Error, "Source shader compile error: " + message.message);
            }
            Check(shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Water2 shader is unsupported or has compiler errors.");

            preview = EditorSceneManager.NewPreviewScene();
            ValidateRuntimeRegistry(preview, report);
            GameObject owner = NewObject("Disposable boat ripple camera", preview);
            camera = owner.AddComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.nearClipPlane = .3f; camera.farClipPlane = 5000f; camera.orthographicSize = 17f; camera.fieldOfView = 52f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.015f, .02f, .025f);
            camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
            UniversalAdditionalCameraData data = owner.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; data.requiresDepthOption = CameraOverrideOption.On; data.requiresColorOption = CameraOverrideOption.Off;
            target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            target.Create(); camera.targetTexture = target;
            Shader opaque = Shader.Find("Universal Render Pipeline/Unlit"); Check(opaque != null, "Opaque bed fixture shader unavailable.");
            var bed = new Material(opaque) { hideFlags = HideFlags.HideAndDontSave };
            bed.SetColor("_BaseColor", camera.backgroundColor); temporary.Add(bed);
            Primitive("Disposable riverbed (well below contact foam)", preview, new Vector3(0f, -25f, 0f), new Vector3(80f, .2f, 80f), bed);
            Mesh mesh = PlaneMesh(); temporary.Add(mesh);
            GameObject surface = NewObject("Disposable Water2 surface", preview);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = surface.AddComponent<MeshRenderer>(); renderer.sharedMaterial = water;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;

            for (int view = 0; view < 2; view++)
            {
                camera.orthographic = view == 0;
                camera.transform.SetPositionAndRotation(view == 0 ? new Vector3(0f, 40f, -5f) : new Vector3(0f, 36f, -30f),
                    view == 0 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.LookRotation(new Vector3(0f, -36f, 25f)));
                string mode = view == 0 ? "orthographic" : "perspective";
                PublishBoat(0f, new Vector3(0f, 0f, 3f), 0f);
                Color32[] baseline = Capture(camera, target, mode + "_no_boat");
                PublishBoat(1f, new Vector3(0f, 0f, 3f), 0f);
                Color32[] stationary = Capture(camera, target, mode + "_stationary_boat");
                Check(SamePixels(baseline, stationary), "A zero-strength/stationary boat changed water in " + mode + ".");
                PublishBoat(1f, new Vector3(0f, 0f, 3f), 1f);
                Color32[] wake = Capture(camera, target, mode + "_moving_boat");
                CheckLocalized(camera, target, baseline, wake, mode, report);
                PublishBoat(1f, new Vector3(4f, 0f, 3f), 1f);
                Color32[] translated = Capture(camera, target, mode + "_boat_moved");
                Check(DifferentPixels(wake, translated, 4) > 100, "Boat motion did not move the ripple pattern in " + mode + ".");
                report.AppendLine("PASS " + mode + ": boat translation moves the bounded ripple pattern; zero contribution is pixel-identical to no boat.");
                water.SetFloat("_BoatRippleStrength", 0f);
                Check(SamePixels(baseline, Capture(camera, target, mode + "_material_disabled")), "Material strength 0 failed to restore baseline in " + mode + ".");
                water.SetFloat("_BoatRippleStrength", .65f);
                PublishBoat(1f, new Vector3(0f, 100f, 3f), 1f);
                Check(SamePixels(baseline, Capture(camera, target, mode + "_boat_above_water")), "A boat on a different vertical surface affected Water2 in " + mode + ".");
                renderer.sharedMaterial = authored;
                PublishBoat(0f, new Vector3(0f, 0f, 3f), 0f);
                Color32[] authoredBaseline = Capture(camera, target, mode + "_authored_water2_no_boat");
                PublishBoat(1f, new Vector3(0f, 0f, 3f), 1f);
                Color32[] authoredWake = Capture(camera, target, mode + "_authored_water2_moving_boat");
                int authoredChanges = DifferentPixels(authoredBaseline, authoredWake, 4);
                Check(authoredChanges > 50, "The authored Water2 colors/alpha hide the boat disturbance in " + mode + ".");
                report.AppendLine(mode + " authored Water2: " + authoredChanges + " changed pixels at strength " + authored.GetFloat("_BoatRippleStrength") + "; original depth colors/alpha/foam retained, only existing speeds frozen on the disposable copy.");
                renderer.sharedMaterial = water;
            }
            ValidateExistingFoam(preview, camera, target, water, bed, report);
            ValidateAndroid(shader, report);
            report.AppendLine("PASS: Native orthographic and perspective Water2 renders contain localized blue boat ripples, not white foam across open water. Movement follows the boat; material strength 0, idle strength 0 and a distant vertical surface preserve baseline. Existing foam is verified separately. Temporal animation wiring is inspected in the graph/HLSL; no timed-frame claim is made by this deterministic fixture.");
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
                Shader.SetGlobalFloat(Count, priorCount);
                Shader.SetGlobalVectorArray(Positions, priorPositions != null && priorPositions.Length > 0 ? priorPositions : new Vector4[4]);
                Shader.SetGlobalVectorArray(Motions, priorMotions != null && priorMotions.Length > 0 ? priorMotions : new Vector4[4]);
                Shader.SetGlobalVector(Profile, priorProfile);
                Shader.SetGlobalTexture("_CameraOpaqueTexture", priorOpaque); Shader.SetGlobalVector("_CameraOpaqueTexture_TexelSize", priorOpaqueTexel);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                Selection.objects = selection; Selection.activeObject = selected;
                foreach (KeyValuePair<string, string> file in sourceFiles) Check(File.ReadAllText(file.Key) == file.Value, "Source asset changed during fixture: " + file.Key);
                if (sourceJson != null) Check(source != null && EditorJsonUtility.ToJson(source) == sourceJson && EditorUtility.IsDirty(source) == sourceDirty, "Source Water2 material changed.");
                Check(SceneManager.sceneCount == scenes.Count, "Loaded scene count changed.");
                for (int i = 0; i < SceneManager.sceneCount; i++) { Scene s = SceneManager.GetSceneAt(i); Check(scenes.ContainsKey(s.handle), "Loaded scene replaced."); scenes[s.handle].Assert(s); }
                foreach (KeyValuePair<Camera, RenderTexture> entry in cameraTargets) Check(entry.Key != null && entry.Key.targetTexture == entry.Value, "Live camera target changed.");
                Check(SceneManager.GetActiveScene() == active && RenderTexture.active == priorTarget && Same(Selection.objects, selection) && Selection.activeObject == selected, "Editor active scene/selection/target changed.");
                Check(Shader.GetGlobalFloat(Count) == priorCount && Shader.GetGlobalVector(Profile) == priorProfile && Shader.GetGlobalTexture("_CameraOpaqueTexture") == priorOpaque && Shader.GetGlobalVector("_CameraOpaqueTexture_TexelSize") == priorOpaqueTexel, "Camera/wake shader globals were not restored.");
                if (priorPositions != null && priorPositions.Length > 0) Check(SameVectors(priorPositions, Shader.GetGlobalVectorArray(Positions)), "Prior boat positions not restored.");
                if (priorMotions != null && priorMotions.Length > 0) Check(SameVectors(priorMotions, Shader.GetGlobalVectorArray(Motions)), "Prior boat motions not restored.");
                report.AppendLine("PASS isolation: source graph/HLSL/subgraph/material bytes and material dirty flag, authored scenes/roots/dirty flags, live camera targets, shader globals and editor selection preserved. Disposable resources released; no saves or Play Mode.");
            }
            catch (Exception e) { passed = false; report.AppendLine("FAIL isolation: " + e); Debug.LogException(e); }
            report.AppendLine(passed ? "RESULT: PASS" : "RESULT: FAIL");
            File.WriteAllText(ReportPath, report.ToString()); running = false;
            if (passed) Debug.Log("[Water2 boat ripples] PASS. Report: " + ReportPath);
        }
    }

    private static void PublishBoat(float count, Vector3 position, float strength)
    {
        var positions = new Vector4[4]; var motions = new Vector4[4];
        positions[0] = new Vector4(position.x, position.y, position.z, strength);
        motions[0] = new Vector4(0f, 1f, 1.8f, 3f);
        Shader.SetGlobalVectorArray(Positions, positions); Shader.SetGlobalVectorArray(Motions, motions); Shader.SetGlobalFloat(Count, count);
    }

    private static void CheckLocalized(Camera camera, RenderTexture target, Color32[] baseline, Color32[] wake, string mode, StringBuilder report)
    {
        int nearSamples = 0, nearChanged = 0, farSamples = 0, farChanged = 0, white = 0, magenta = 0;
        var plane = new Plane(Vector3.up, Vector3.zero);
        for (int y = 0; y < target.height; y++) for (int x = 0; x < target.width; x++)
        {
            int i = y * target.width + x;
            Ray ray = camera.ViewportPointToRay(new Vector3((x + .5f) / target.width, (y + .5f) / target.height, 0f));
            if (!plane.Raycast(ray, out float distance)) continue;
            Vector3 point = ray.GetPoint(distance);
            if (Mathf.Abs(point.x) > 30f || Mathf.Abs(point.z) > 30f) continue;
            int delta = Delta(baseline[i], wake[i]);
            if (Mathf.Abs(point.x) < 8f && point.z > -18f && point.z < 7f) { nearSamples++; if (delta > 4) nearChanged++; }
            if (Mathf.Abs(point.x) > 13f && Mathf.Abs(point.x) < 27f && Mathf.Abs(point.z) < 22f) { farSamples++; if (delta > 0) farChanged++; }
            if (wake[i].r > 150 && Mathf.Abs(wake[i].r - wake[i].g) < 25 && Mathf.Abs(wake[i].r - wake[i].b) < 25) white++;
            if (wake[i].r > 200 && wake[i].b > 200 && wake[i].g < 80) magenta++;
        }
        report.AppendLine(mode + ": local ripple pixels " + nearChanged + "/" + nearSamples + ", distant-water changed " + farChanged + "/" + farSamples + ", white foam pixels " + white + ", shader-error magenta " + magenta + ".");
        Check(nearSamples > 1000 && nearChanged > 100, "No noticeable blue boat disturbance in " + mode + ".");
        Check(farSamples > 1000 && farChanged == 0, "Boat disturbance modified distant open water in " + mode + ".");
        Check(white == 0 && magenta == 0, "Boat disturbance produced white open-water foam or a shader error in " + mode + ".");
    }

    private static void ValidateExistingFoam(Scene scene, Camera camera, RenderTexture target, Material water, Material bed, StringBuilder report)
    {
        Primitive("Disposable existing shoreline", scene, Vector3.zero, new Vector3(4f, 3f, 4f), bed);
        camera.orthographic = true; camera.orthographicSize = 8f;
        camera.transform.position = new Vector3(0f, 16f, -16f); camera.transform.LookAt(Vector3.zero);
        water.SetFloat("Vector1_C5543ECF", 4.79f);
        water.SetColor(FoamColor, new Color(.7454045f, .7454045f, .7454045f, 1f));
        PublishBoat(0f, new Vector3(-6f, 0f, 3f), 0f);
        Color32[] oldFoam = Capture(camera, target, "existing_contact_foam");
        PublishBoat(1f, new Vector3(-6f, 0f, 3f), 1f);
        Color32[] withWake = Capture(camera, target, "existing_contact_foam_with_boat");
        int preservedWhite = 0;
        for (int i = 0; i < oldFoam.Length; i++)
            if (oldFoam[i].r > 165 && Mathf.Abs(oldFoam[i].r - oldFoam[i].g) < 20 && Mathf.Abs(oldFoam[i].r - oldFoam[i].b) < 20 && Delta(oldFoam[i], withWake[i]) == 0) preservedWhite++;
        report.AppendLine("Existing shoreline/contact-foam pixels retained exactly with active boat: " + preservedWhite + ".");
        Check(preservedWhite > 50, "Existing contact foam was not visible/preserved beside the boat ripple effect.");
    }

    private static void ValidateAndroid(Shader shader, StringBuilder report)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
        { report.AppendLine("NOTE: Android module unavailable; native GLES3 compilation not performed."); return; }
        var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
        var result = pass.CompileVariant(ShaderType.Vertex, Array.Empty<string>(), ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
        foreach (ShaderMessage message in result.Messages)
        {
            report.AppendLine("Android GLES3 " + message.severity + ": " + message.message);
            Check(message.severity != ShaderCompilerMessageSeverity.Error, "Android GLES3 compile error: " + message.message);
        }
        Check(result.Success && result.ShaderData != null && result.ShaderData.Length > 0, "Android GLES3 failed to emit its combined program.");
        File.WriteAllBytes("Temp/BoatWaterWake_AndroidGLES3.bin", result.ShaderData);
        report.AppendLine("PASS: Actual Android GLES3 combined vertex/fragment compilation emitted " + result.ShaderData.Length + " bytes; no active build-target change.");
    }

    private static GameObject NewObject(string name, Scene scene)
    { var obj = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave }; SceneManager.MoveGameObjectToScene(obj, scene); return obj; }
    private static GameObject Primitive(string name, Scene scene, Vector3 position, Vector3 scale, Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.hideFlags = HideFlags.HideAndDontSave;
        SceneManager.MoveGameObjectToScene(obj, scene); obj.name = name; obj.transform.position = position; obj.transform.localScale = scale;
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        Renderer r = obj.GetComponent<Renderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return obj;
    }

    private static void ValidateRuntimeRegistry(Scene scene, StringBuilder report)
    {
        Type type = typeof(BoatBridgeCrossing).Assembly.GetType("BoatWaterWake");
        Check(type != null, "BoatWaterWake runtime sampler is not imported.");
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var listField = type.GetField("emitters", flags);
        var positionsField = type.GetField("positions", flags);
        var motionsField = type.GetField("motions", flags);
        var countField = type.GetField("outputCount", flags);
        var quittingField = type.GetField("quitting", flags);
        var samplerField = type.GetField("sampler", flags);
        var register = type.GetMethod("Register", flags);
        var unregister = type.GetMethod("Unregister", flags);
        var sample = type.GetMethod("SampleNow", flags);
        var clear = type.GetMethod("ClearGlobals", flags);
        Check(listField != null && positionsField != null && motionsField != null && countField != null && quittingField != null && samplerField != null && register != null && unregister != null && sample != null && clear != null,
            "Current BoatWaterWake registry contract is incomplete.");
        var entries = (System.Collections.IList)listField.GetValue(null);
        var priorEntries = new object[entries.Count]; entries.CopyTo(priorEntries, 0);
        var positions = (Vector4[])positionsField.GetValue(null);
        var motions = (Vector4[])motionsField.GetValue(null);
        var oldPositions = (Vector4[])positions.Clone(); var oldMotions = (Vector4[])motions.Clone();
        object oldCount = countField.GetValue(null), oldQuitting = quittingField.GetValue(null), oldSampler = samplerField.GetValue(null);
        float oldShaderCount = Shader.GetGlobalFloat(Count);
        Vector4[] oldShaderPositions = Shader.GetGlobalVectorArray(Positions), oldShaderMotions = Shader.GetGlobalVectorArray(Motions);
        var boats = new List<GameObject>();
        var visible = new List<Renderer>();
        Action<float> tick = dt => sample.Invoke(null, new object[] { dt });
        Action<GameObject> add = boat => register.Invoke(null, new object[] { boat.transform, new[] { boat.GetComponent<Renderer>() } });
        Action<GameObject> remove = boat => unregister.Invoke(null, new object[] { boat.transform });
        try
        {
            // Existing entries remain untouched; only this disposable list is sampled.
            entries.Clear(); quittingField.SetValue(null, false); clear.Invoke(null, null);
            GameObject boat = Primitive("Disposable mesh-only hull", scene, new Vector3(0f, 2f, 0f), new Vector3(3f, 2f, 6f), null);
            boats.Add(boat); Renderer renderer = boat.GetComponent<Renderer>(); visible.Add(renderer);
            add(boat); tick(.1f);
            Check(Shader.GetGlobalFloat(Count) == 0f, "Static/first-sample boat emitted a wake.");
            Check(Equals(samplerField.GetValue(null), oldSampler) && boat.GetComponents<MonoBehaviour>().Length == 0,
                "Edit Mode registry created a runtime sampler or added guest boat scripts.");
            for (int i = 0; i < 25; i++) { boat.transform.position += new Vector3(0f, 0f, .3f); tick(.1f); }
            Vector4[] emittedPositions = Shader.GetGlobalVectorArray(Positions), emittedMotions = Shader.GetGlobalVectorArray(Motions);
            Check(Shader.GetGlobalFloat(Count) == 1f && emittedPositions[0].w > .2f && emittedMotions[0].y > .99f && Mathf.Abs(emittedMotions[0].x) < .01f,
                "Actual transform motion did not emit a forward boat wake.");
            Check(Mathf.Abs(emittedPositions[0].x - renderer.bounds.center.x) < .001f &&
                Mathf.Abs(emittedPositions[0].y - (renderer.bounds.min.y + renderer.bounds.size.y * .12f)) < .001f,
                "Wake position does not use the actual hull/waterline bounds.");
            for (int i = 0; i < 100; i++) tick(.1f);
            Check(Shader.GetGlobalFloat(Count) == 0f, "Stopped boat wake failed to decay.");
            for (int i = 0; i < 25; i++) { boat.transform.position += new Vector3(0f, 0f, .3f); tick(.1f); }
            renderer.enabled = false;
            for (int i = 0; i < 60; i++) tick(.1f);
            Check(Shader.GetGlobalFloat(Count) == 0f, "Hidden guest/finished boat continued emitting wake data.");
            renderer.enabled = true; tick(.1f);
            for (int i = 0; i < 25; i++) { boat.transform.position += new Vector3(0f, 0f, .3f); tick(.1f); }
            Check(Shader.GetGlobalFloat(Count) == 1f, "Resumed visible boat did not emit a wake.");
            boat.transform.position += new Vector3(200f, 0f, 0f); tick(.1f);
            Check(Shader.GetGlobalFloat(Count) == 0f, "Teleport/reset produced a map-spanning wake.");
            for (int i = 0; i < 25; i++) { boat.transform.position += new Vector3(0f, 0f, .3f); tick(.1f); }
            remove(boat);
            for (int i = 0; i < 60; i++) tick(.1f);
            Check(Shader.GetGlobalFloat(Count) == 0f && entries.Count == 0, "Unregistered boat wake failed to fade/prune.");
            Check(boat.GetComponents<MonoBehaviour>().Length == 0, "Registry changed script-free spectator boat presentation.");
            Object.DestroyImmediate(boat); boats.Clear(); visible.Clear();

            for (int i = 0; i < 6; i++)
            {
                GameObject guest = Primitive("Disposable guest mesh hull " + i, scene, new Vector3(i * 20f, 2f, 0f), new Vector3(3f, 2f, 6f), null);
                boats.Add(guest); visible.Add(guest.GetComponent<Renderer>()); add(guest);
            }
            tick(.1f);
            for (int frame = 0; frame < 25; frame++)
            {
                for (int i = 0; i < boats.Count; i++) boats[i].transform.position += new Vector3(0f, 0f, .2f + i * .05f);
                tick(.1f);
            }
            emittedPositions = Shader.GetGlobalVectorArray(Positions); emittedMotions = Shader.GetGlobalVectorArray(Motions);
            Check(Shader.GetGlobalFloat(Count) == 4f && emittedPositions.Length == 4 && emittedMotions.Length == 4, "Shader emitter budget is not bounded to four boats.");
            for (int i = 0; i < 4; i++) Check(emittedPositions[i].w > .1f && emittedPositions[i].w <= 1f && emittedMotions[i].y > .99f,
                "Bounded shader slot contains invalid strength/direction.");
            for (int i = 1; i < 4; i++) Check(emittedPositions[i - 1].w >= emittedPositions[i].w, "Four strongest wakes are not prioritized.");
            foreach (GameObject guest in boats) remove(guest);
            for (int i = 0; i < 60; i++) tick(.1f);
            Check(Shader.GetGlobalFloat(Count) == 0f && entries.Count == 0, "Registry cleanup left active wake data.");
            foreach (Vector4 vector in Shader.GetGlobalVectorArray(Positions)) Check(vector == Vector4.zero, "Registry cleanup left stale positions.");
            foreach (Vector4 vector in Shader.GetGlobalVectorArray(Motions)) Check(vector == Vector4.zero, "Registry cleanup left stale directions.");
            Check(Equals(samplerField.GetValue(null), oldSampler), "Edit Mode test created a persistent runtime sampler.");
            foreach (GameObject guest in boats) Check(guest.GetComponents<MonoBehaviour>().Length == 0, "Guest mesh-only presentation gained a script.");
            report.AppendLine("PASS runtime registry: first/static sample 0, actual hull transform movement/direction, idle/hidden fade, teleport immediate reset, unregister/prune, strongest four of six boats, cleared arrays, script-free guest meshes and no Edit Mode runtime sampler.");
        }
        finally
        {
            foreach (GameObject boat in boats) if (boat != null) Object.DestroyImmediate(boat);
            Object currentSampler = samplerField.GetValue(null) as Object;
            if (currentSampler != null && !Equals(currentSampler, oldSampler))
            {
                if (currentSampler is Component component) Object.DestroyImmediate(component.gameObject);
                else Object.DestroyImmediate(currentSampler);
            }
            entries.Clear(); foreach (object entry in priorEntries) entries.Add(entry);
            Array.Copy(oldPositions, positions, positions.Length); Array.Copy(oldMotions, motions, motions.Length);
            countField.SetValue(null, oldCount); quittingField.SetValue(null, oldQuitting);
            samplerField.SetValue(null, oldSampler);
            Shader.SetGlobalFloat(Count, oldShaderCount);
            Shader.SetGlobalVectorArray(Positions, oldShaderPositions != null && oldShaderPositions.Length > 0 ? oldShaderPositions : new Vector4[4]);
            Shader.SetGlobalVectorArray(Motions, oldShaderMotions != null && oldShaderMotions.Length > 0 ? oldShaderMotions : new Vector4[4]);
            Check(entries.Count == priorEntries.Length && SameVectors(positions, oldPositions) && SameVectors(motions, oldMotions) && Equals(countField.GetValue(null), oldCount) && Equals(quittingField.GetValue(null), oldQuitting) && Equals(samplerField.GetValue(null), oldSampler),
                "Prior runtime registry state was not restored.");
            for (int i = 0; i < priorEntries.Length; i++) Check(ReferenceEquals(entries[i], priorEntries[i]), "Existing runtime emitter was mutated/replaced.");
        }
    }
    private static Mesh PlaneMesh()
    {
        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { new Vector3(-40, 0, -40), new Vector3(-40, 0, 40), new Vector3(40, 0, 40), new Vector3(40, 0, -40) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }; mesh.RecalculateBounds(); return mesh;
    }
    private static Color32[] Capture(Camera camera, RenderTexture target, string name)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        Check(RenderPipeline.SupportsRenderRequest(camera, request), "URP render request unsupported."); RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes("Temp/BoatWaterWake_" + name + ".png", image.EncodeToPNG()); return image.GetPixels32(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private static int Delta(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
    private static int DifferentPixels(Color32[] a, Color32[] b, int threshold) { int count = 0; for (int i = 0; i < a.Length; i++) if (Delta(a[i], b[i]) > threshold) count++; return count; }
    private static bool SamePixels(Color32[] a, Color32[] b) => a.Length == b.Length && DifferentPixels(a, b, 0) == 0;
    private static bool SameVectors(Vector4[] a, Vector4[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static bool Same(Object[] a, Object[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class SceneSnapshot
    {
        private readonly bool dirty; private readonly GameObject[] roots;
        public SceneSnapshot(Scene scene) { dirty = scene.isDirty; roots = scene.GetRootGameObjects(); }
        public void Assert(Scene scene) => Check(scene.isLoaded && scene.isDirty == dirty && Same(scene.GetRootGameObjects(), roots), "Authored scene roots/dirty flag changed: " + scene.path);
    }
}
#endif
