#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// <summary>Explicitly requested, isolated render checks; never edits scenes, prefabs or player saves.</summary>
public static class LoadingPreviewVisibilityValidation
{
    private const string Request = "Temp/LoadingPreviewVisibilityValidation.request";
    private const string Report = "Temp/LoadingPreviewVisibilityValidation.txt";
    private const string Images = "Temp/LoadingPreviewVisibilityPreviews";
    private const int Layer = 31;
    private const BindingFlags Methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            File.WriteAllText(Report, "WAIT: Stop Play Mode and wait for compilation/import/build. No scenes or saves changed.");
            return;
        }
        File.Delete(Request);
        Validate();
    }

    [MenuItem("Tools/Civil Craft/Validate Loading Cosmetic Visibility")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var report = new StringBuilder("RUNNING: Isolated loading cosmetics render validation.\n");
        File.WriteAllText(Report, report.ToString());
        Scene preview = default;
        RenderTexture target = null;
        Scene original = SceneManager.GetActiveScene();
        var sourceSnapshots = new Dictionary<Object, string>();
        var fileSnapshots = new Dictionary<string, Hash128>();
        LoadingScreenManager originalManager = LoadingScreenManager.Instance;
        SkinWeights originalSkinWeights = QualitySettings.skinWeights;
        RenderPipelineAsset originalQualityPipeline = QualitySettings.renderPipeline;
        try
        {
            LoadingScreenAssets assets = Resources.Load<LoadingScreenAssets>("Loading/LoadingScreenAssets");
            Check(assets != null && assets.fallbackPlayerPrefab != null, "Loading fallback prefab is not configured.");
            Check(typeof(LoadingPlayerPreview).GetMethod("RefreshVisibility", Methods) != null &&
                typeof(LoadingPlayerPreview).GetMethod("TryGetPreviewBounds", Methods) != null,
                "Loading visibility fix has not been compiled yet.");
            sourceSnapshots.Add(assets, EditorJsonUtility.ToJson(assets));
            sourceSnapshots.Add(assets.fallbackPlayerPrefab, EditorJsonUtility.ToJson(assets.fallbackPlayerPrefab));
            foreach (string path in new[] { AssetDatabase.GetAssetPath(assets), AssetDatabase.GetAssetPath(assets.fallbackPlayerPrefab) })
                fileSnapshots.Add(path, Hash128.Compute(File.ReadAllText(path)));
            foreach (Material material in assets.fallbackPlayerPrefab.GetComponentsInChildren<Renderer>(true)
                         .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct())
            {
                sourceSnapshots.Add(material, EditorJsonUtility.ToJson(material));
                string path = AssetDatabase.GetAssetPath(material);
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && !fileSnapshots.ContainsKey(path))
                    fileSnapshots.Add(path, Hash128.Compute(File.ReadAllText(path)));
            }

            preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Loading visibility validation (temporary)");
            SceneManager.MoveGameObjectToScene(root, preview);
            root.transform.position = new Vector3(10000f, 10000f, 10000f);
            target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            target.Create();
            var cameraObject = new GameObject("Loading preview camera (temporary)", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview; camera.enabled = false; camera.aspect = 1f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << Layer; camera.fieldOfView = 30f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 50f; camera.useOcclusionCulling = false;
            camera.allowHDR = false; camera.allowMSAA = false; camera.targetTexture = target;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var lightObject = new GameObject("Loading preview light (temporary)", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, preview);
            lightObject.transform.SetParent(root.transform, false);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f; light.color = Color.white;
            light.cullingMask = 1 << Layer; light.shadows = LightShadows.None;

            Directory.CreateDirectory(Images);
            ValidateOutfit(assets, root, camera, light, target, "PoloCargo", "Hair_Base", "Shirt_Polo", "Pants_Cargo", "Shoes_Base", report);
            ValidateOutfit(assets, root, camera, light, target, "BlouseHair8Boots2", "Hair_8", "Shirt_Blouse", "Pants_Straight", "Shoes_Boots2", report);
            ValidatePresentation(assets, root, target, report);
            foreach (var snapshot in sourceSnapshots)
            {
                string after = EditorJsonUtility.ToJson(snapshot.Key);
                if (after != snapshot.Value)
                {
                    File.WriteAllText(Images + "/SourceChange_" + snapshot.Key.GetInstanceID() + "_Before.json", snapshot.Value);
                    File.WriteAllText(Images + "/SourceChange_" + snapshot.Key.GetInstanceID() + "_After.json", after);
                }
                Check(after == snapshot.Value, "A source asset changed: " + snapshot.Key.name);
            }
            foreach (var snapshot in fileSnapshots)
                Check(Hash128.Compute(File.ReadAllText(snapshot.Key)) == snapshot.Value, "Source asset file changed: " + snapshot.Key);
            Check(LoadingScreenManager.Instance == originalManager, "The live loading singleton changed during isolated validation.");
            report.AppendLine("PASS: Source loading assets, source prefab and shared materials unchanged; no save, purchase, network or scene-authoring API was used.");
            Debug.Log("[Loading cosmetic visibility] Actual fallback outfits, run poses and isolated cosmetic renders passed.");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            QualitySettings.skinWeights = originalSkinWeights;
            QualitySettings.renderPipeline = originalQualityPipeline;
            report.AppendLine("RESTORED: Global skin weights and quality render pipeline restored in finally.");
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            File.WriteAllText(Report, report.ToString());
        }
    }

    private static void ValidateOutfit(LoadingScreenAssets assets, GameObject stage, Camera camera, Light light,
        RenderTexture target, string label, string hair, string shirt, string pants, string shoes, StringBuilder report)
    {
        GameObject model = Object.Instantiate(assets.fallbackPlayerPrefab, stage.transform);
        var localMaterials = new Dictionary<Material, Material>();
        model.name = label + " (temporary loading clone)";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        model.transform.localScale = Vector3.one;
        var managerObject = new GameObject("Loading framing fixture (temporary)");
        SceneManager.MoveGameObjectToScene(managerObject, stage.scene);
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<LoadingScreenManager>();
        try
        {
            // URP may initialize material keywords on first render. Keep those
            // render-time changes on throwaway copies, never shared source assets.
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] copies = renderer.sharedMaterials;
                for (int index = 0; index < copies.Length; index++)
                {
                    Material original = copies[index];
                    if (original == null) continue;
                    if (!localMaterials.TryGetValue(original, out Material copy))
                    {
                        copy = new Material(original) { name = original.name, hideFlags = HideFlags.HideAndDontSave };
                        localMaterials.Add(original, copy);
                    }
                    copies[index] = copy;
                }
                renderer.sharedMaterials = copies;
            }
            PlayerCosmeticMirror mirror = model.GetComponentInChildren<PlayerCosmeticMirror>(true);
            Check(mirror != null, "The actual fallback has no cosmetic mirror.");
            var loadout = new CosmeticLoadoutData { hairID = hair, shirtID = shirt, pantsID = pants, shoesID = shoes };
            loadout.SetAccessoryEquipped("EngineeringHardHat", true);
            var selected = mirror.cosmeticBindings.Where(binding => binding != null &&
                (loadout.IsAccessoryEquipped(binding.cosmeticID) ||
                string.Equals(loadout.GetID(binding.category), binding.cosmeticID, StringComparison.OrdinalIgnoreCase))).ToArray();
            Check(selected.Any(binding => binding.cosmeticID == shirt) && selected.Any(binding => binding.cosmeticID == pants) &&
                selected.Any(binding => binding.cosmeticID == hair) && selected.Any(binding => binding.cosmeticID == shoes),
                "The actual fallback is missing the requested outfit: " + label);
            foreach (CosmeticModelBinding binding in selected)
            {
                int index = 0;
                foreach (CosmeticBindingUtility.MaterialSlot slot in CosmeticBindingUtility.GetMaterialSlots(binding))
                {
                    Color color = index++ % 2 == 0 ? new Color(.85f, .22f, .11f, 1f) : new Color(.12f, .42f, .83f, 1f);
                    loadout.SetMaterialColor(binding.cosmeticID, slot.Key, color);
                }
            }
            mirror.BeginPreview(loadout);
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = Layer;
            model.SetActive(true);
            var activeSnapshot = model.GetComponentsInChildren<Transform>(true)
                .ToDictionary(node => node.gameObject, node => node.gameObject.activeSelf);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds authoredEnvelope = default;
            bool hasAuthoredEnvelope = false;
            foreach (Renderer renderer in renderers.Where(renderer => renderer.gameObject.activeInHierarchy))
            {
                if (!hasAuthoredEnvelope) { authoredEnvelope = renderer.bounds; hasAuthoredEnvelope = true; }
                else authoredEnvelope.Encapsulate(renderer.bounds);
            }
            foreach (Renderer renderer in renderers.Where(renderer => renderer.gameObject.activeInHierarchy))
            {
                renderer.enabled = false; renderer.forceRenderingOff = true;
                renderer.allowOcclusionWhenDynamic = true;
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                if (renderer is SkinnedMeshRenderer skinned)
                { skinned.updateWhenOffscreen = false; skinned.localBounds = new Bounds(new Vector3(999f, 999f, 999f), Vector3.one * .001f); }
            }
            var preview = model.GetComponent<LoadingPlayerPreview>() ?? model.AddComponent<LoadingPlayerPreview>();
            SetPreviewEnabled(preview, true);
            var setupTimer = System.Diagnostics.Stopwatch.StartNew();
            preview.BeginRunning(assets.playerAnimatorController);
            typeof(LoadingPlayerPreview).GetMethod("BindPreviewCamera", Methods).Invoke(preview, new object[] { camera });
            setupTimer.Stop();
            var skinnedMeshes = renderers.OfType<SkinnedMeshRenderer>().Where(renderer => renderer.gameObject.activeInHierarchy && renderer.sharedMesh != null).ToArray();
            report.AppendLine($"MEASURE: {label}: BeginRunning setup {setupTimer.Elapsed.TotalMilliseconds:F2} ms in the editor, {skinnedMeshes.Length} active skinned meshes, {skinnedMeshes.Sum(renderer => renderer.sharedMesh.vertexCount)} vertices; initial geometry sampled once, no per-frame production BakeMesh.");
            SetField(manager, "previewStage", stage.transform);
            SetField(manager, "previewModel", model);
            SetField(manager, "previewCamera", camera);
            Frame(manager, light, camera);
            Bounds initialGeometry = GetGeometryBounds(preview);
            if (initialGeometry.size.y <= .1f || initialGeometry.size.y >= 100f)
            {
                report.AppendLine($"DIAGNOSTIC {label}: sampled envelope={initialGeometry}, authored envelope={authoredEnvelope}");
                DescribeBaking(skinnedMeshes, report);
                camera.transform.position = authoredEnvelope.center + Vector3.back * (Mathf.Max(authoredEnvelope.size.x, authoredEnvelope.size.y) / (2f * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad)) * 1.2f + authoredEnvelope.extents.z);
                camera.transform.LookAt(authoredEnvelope.center);
                camera.farClipPlane = 200f;
                light.transform.rotation = camera.transform.rotation * Quaternion.Euler(10f, -15f, 0f);
                foreach (Renderer renderer in renderers.Where(renderer => renderer.gameObject.activeInHierarchy)) renderer.bounds = authoredEnvelope;
                Color32[] diagnostic = Capture(camera, target, Images + "/" + label + "_AuthoredBoundsControl.png");
                report.AppendLine("DIAGNOSTIC authored-envelope control render has " + diagnostic.Count(pixel => pixel.a > 12) + " pixels.");
            }
            Check(initialGeometry.size.y > .1f && initialGeometry.size.y < 100f, "Framing bounds are empty or oversized.");
            Animator animator = model.GetComponentInChildren<Animator>(true);
            int run = Animator.StringToHash("Sprint");
            if (!animator.HasState(0, run)) run = Animator.StringToHash("walk");
            var poseHashes = new HashSet<Hash128>();
            int minPixels = int.MaxValue, maxPixels = 0;
            for (int phase = 0; phase < 12; phase++)
            {
                animator.Play(run, 0, phase / 12f); animator.Update(0f);
                typeof(LoadingPlayerPreview).GetMethod("LateUpdate", Methods).Invoke(preview, null);
                Refresh(preview);
                ValidateRenderFlags(renderers);
                CheckHierarchy(activeSnapshot); CheckColors(selected, loadout);
                CheckGeometryClipping(model, camera, label + " run phase " + phase);
                Color32[] pixels = Capture(camera, target, phase % 3 == 0 ? Images + "/" + label + "_Run" + phase + ".png" : null);
                int count = CheckSilhouette(pixels, target.width, target.height, label + " run phase " + phase, true);
                minPixels = Mathf.Min(minPixels, count); maxPixels = Mathf.Max(maxPixels, count);
                poseHashes.Add(Hash128.Compute(pixels.Where(pixel => pixel.a > 12).Count().ToString() + ":" + PixelHash(pixels)));
            }
            Check(poseHashes.Count >= 3, "Run-cycle render remained frozen rather than showing different poses.");
            report.AppendLine($"PASS: {label}: 12 actual URP run poses, {poseHashes.Count} distinct images, {minPixels}–{maxPixels} visible pixels; full model remains inside transparent margins after inherited hidden/culling flags.");

            animator.Play(run, 0, .375f); animator.Update(0f); Refresh(preview);
            foreach (CosmeticModelBinding binding in selected)
            {
                var wanted = new HashSet<Renderer>((binding.models ?? new List<GameObject>()).Where(item => item != null)
                    .SelectMany(item => item.GetComponentsInChildren<Renderer>(true)).Where(renderer => renderer.gameObject.activeInHierarchy));
                if (wanted.Count == 0) continue;
                foreach (Renderer renderer in renderers) renderer.forceRenderingOff = !wanted.Contains(renderer);
                SetPreviewEnabled(preview, false);
                try
                {
                    Color32[] pixels = Capture(camera, target, Images + "/" + label + "_Isolated_" + binding.cosmeticID + ".png");
                    int count = CheckSilhouette(pixels, target.width, target.height, binding.cosmeticID + " isolated", false);
                    Check(count >= 12, "Selected cosmetic has no actual rendered pixels: " + binding.cosmeticID);
                    report.AppendLine($"PASS: {label}/{binding.cosmeticID}: {count} isolated cosmetic pixels; selected mesh is rendered, not merely enabled or in the frustum.");
                }
                finally { SetPreviewEnabled(preview, true); Refresh(preview); }
            }

            ValidateQualityAndRenderOrdering(preview, model, animator, run, selected, renderers,
                camera, target, label, report);

            Vector3 previousPosition = stage.transform.position;
            try
            {
                stage.transform.position += new Vector3(345f, -123f, 246f);
                model.transform.localScale = Vector3.one * .75f;
                foreach (Renderer renderer in renderers.Where(renderer => renderer.gameObject.activeInHierarchy))
                { renderer.enabled = false; renderer.forceRenderingOff = true; renderer.bounds = new Bounds(Vector3.zero, Vector3.one * .001f); }
                Refresh(preview); Frame(manager, light, camera);
                Check((GetGeometryBounds(preview).center - initialGeometry.center).magnitude > 100f,
                    "Geometry framing envelope did not follow the moved preview stage.");
                CheckSilhouette(Capture(camera, target, Images + "/" + label + "_MovedStage.png"), target.width, target.height, label + " moved stage", true);
                CheckGeometryClipping(model, camera, label + " moved stage");
                CheckHierarchy(activeSnapshot); CheckColors(selected, loadout);
                preview.BeginRunning(assets.playerAnimatorController); Frame(manager, light, camera);
                CheckSilhouette(Capture(camera, target, Images + "/" + label + "_Restarted.png"), target.width, target.height, label + " repeat initialization", true);
                report.AppendLine("PASS: " + label + ": moved/rescaled stage and repeated initialization repair inherited hidden/world-bounds state without changing selected items or independent material colors.");
            }
            finally { stage.transform.position = previousPosition; }
        }
        finally
        {
            LoadingPlayerPreview preview = model.GetComponent<LoadingPlayerPreview>();
            if (preview != null) SetPreviewEnabled(preview, false);
            Object.DestroyImmediate(model); Object.DestroyImmediate(managerObject);
            foreach (Material material in localMaterials.Values) Object.DestroyImmediate(material);
        }
    }

    private static void Frame(LoadingScreenManager manager, Light light, Camera camera)
    {
        typeof(LoadingScreenManager).GetMethod("FramePreviewModel", Methods).Invoke(manager, null);
        light.transform.rotation = camera.transform.rotation * Quaternion.Euler(10f, -15f, 0f);
    }

    private static void ValidateQualityAndRenderOrdering(LoadingPlayerPreview preview, GameObject model,
        Animator animator, int run, CosmeticModelBinding[] selected, Renderer[] renderers, Camera camera,
        RenderTexture target, string label, StringBuilder report)
    {
        SkinWeights savedWeights = QualitySettings.skinWeights;
        RenderPipelineAsset savedPipeline = QualitySettings.renderPipeline;
        UniversalRenderPipelineAsset mobilePipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
            "Assets/Settings/URP-Performant.asset");
        Check(mobilePipeline != null, "Android's Performant render pipeline is missing.");
        string mobileJson = EditorJsonUtility.ToJson(mobilePipeline);
        string qualityFile = File.ReadAllText("ProjectSettings/QualitySettings.asset");
        Renderer[] body = renderers.Where(renderer => renderer.name == "Body" && renderer.gameObject.activeInHierarchy).ToArray();
        var pants = new HashSet<Renderer>(selected.Where(binding => binding.category == CosmeticCategory.Pants)
            .SelectMany(binding => binding.models ?? new List<GameObject>()).Where(item => item != null)
            .SelectMany(item => item.GetComponentsInChildren<Renderer>(true)).Where(renderer => renderer.gameObject.activeInHierarchy));
        Check(body.Length > 0 && pants.Count > 0, "Body or selected pants are absent from the actual fallback outfit.");
        var otherCameraObject = new GameObject("Unrelated camera guard (temporary)", typeof(Camera));
        SceneManager.MoveGameObjectToScene(otherCameraObject, model.scene);
        Camera otherCamera = otherCameraObject.GetComponent<Camera>();
        otherCamera.enabled = false;
        try
        {
            QualitySettings.renderPipeline = mobilePipeline;
            foreach (SkinWeights weights in Enum.GetValues(typeof(SkinWeights)).Cast<SkinWeights>().Where(weights => (int)weights > 0))
            {
                QualitySettings.skinWeights = weights;
                foreach (SkinnedMeshRenderer renderer in renderers.OfType<SkinnedMeshRenderer>()) renderer.quality = SkinQuality.Auto;
                animator.Play(run, 0, .375f); animator.Update(0f);
                typeof(LoadingPlayerPreview).GetMethod("LateUpdate", Methods).Invoke(preview, null);
                ValidateRenderFlags(renderers);

                // Removing the world override must retain a conservative local
                // envelope in the skinned renderer's root-bone coordinate space.
                Bounds envelope = GetGeometryBounds(preview);
                foreach (SkinnedMeshRenderer renderer in renderers.OfType<SkinnedMeshRenderer>()
                    .Where(renderer => renderer.gameObject.activeInHierarchy))
                {
                    renderer.ResetBounds();
                    CheckBoundsContain(renderer.bounds, envelope, "local envelope after ResetBounds: " + label + "/" + renderer.name);
                }
                Color32[] resetPixels = Capture(camera, target, Images + "/" + label + "_" + weights + "_ResetBounds.png");
                CheckSilhouette(resetPixels, target.width, target.height, label + " " + weights + " ResetBounds", true);

                // Simulate bounds/visibility invalidation after LateUpdate. The
                // callback must repair this at the actual URP camera boundary.
                foreach (Renderer renderer in renderers.Where(renderer => renderer.gameObject.activeInHierarchy))
                {
                    renderer.enabled = false; renderer.forceRenderingOff = true;
                    renderer.ResetBounds();
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.localBounds = new Bounds(Vector3.one * 999f, Vector3.one * .001f);
                        skinned.quality = SkinQuality.Auto;
                        skinned.updateWhenOffscreen = true;
                    }
                    renderer.bounds = new Bounds(Vector3.zero, Vector3.one * .001f);
                }
                typeof(LoadingPlayerPreview).GetMethod("PrepareForCamera", Methods).Invoke(preview, new object[] { otherCamera });
                Check(body.All(renderer => !renderer.enabled && renderer.forceRenderingOff),
                    "A different camera repaired loading renderer state despite the preview-camera guard.");
                bool reachedCameraBoundary = false;
                bool repairedAtCameraBoundary = false;
                Action<ScriptableRenderContext, Camera> observe = (context, renderingCamera) =>
                {
                    if (renderingCamera != camera) return;
                    reachedCameraBoundary = true;
                    repairedAtCameraBoundary = body.Concat(pants).All(renderer => renderer.enabled && !renderer.forceRenderingOff &&
                        renderer.bounds.Contains(envelope.min) && renderer.bounds.Contains(envelope.max));
                };
                RenderPipelineManager.beginCameraRendering += observe;
                Color32[] repairedPixels;
                try { repairedPixels = Capture(camera, target, Images + "/" + label + "_" + weights + "_LateBoundsReset.png"); }
                finally { RenderPipelineManager.beginCameraRendering -= observe; }
                Check(reachedCameraBoundary && repairedAtCameraBoundary,
                    "Late bounds reset was not repaired at URP beginCameraRendering for " + label + "/" + weights +
                    "; boundary reached=" + reachedCameraBoundary + ", repaired=" + repairedAtCameraBoundary +
                    ", preview active=" + preview.isActiveAndEnabled + ", body=" + body[0].bounds);
                ValidateRenderFlags(renderers);
                CheckSilhouette(repairedPixels, target.width, target.height, label + " " + weights + " late reset", true);
                foreach (SkinnedMeshRenderer renderer in renderers.OfType<SkinnedMeshRenderer>()
                    .Where(renderer => renderer.gameObject.activeInHierarchy))
                {
                    renderer.ResetBounds();
                    CheckBoundsContain(renderer.bounds, envelope, "camera-restored local envelope: " + label + "/" + renderer.name);
                }
                Refresh(preview);
                int bodyPixels = CaptureIsolated(preview, renderers, new HashSet<Renderer>(body), camera, target,
                    Images + "/" + label + "_" + weights + "_Body.png", label + " " + weights + " body");
                int pantsPixels = CaptureIsolated(preview, renderers, pants, camera, target,
                    Images + "/" + label + "_" + weights + "_Pants.png", label + " " + weights + " pants");
                report.AppendLine($"PASS: {label}/{weights}: Android Performant URP, inherited Auto repaired to Bone4; ResetBounds and post-LateUpdate bounds invalidation repaired at actual beginCameraRendering; body={bodyPixels}, pants={pantsPixels} rendered pixels; unrelated camera ignored.");
            }
            Check(EditorJsonUtility.ToJson(mobilePipeline) == mobileJson, "Mobile render pipeline asset was modified.");
            Check(File.ReadAllText("ProjectSettings/QualitySettings.asset") == qualityFile, "Project quality settings file was modified.");
        }
        finally
        {
            QualitySettings.skinWeights = savedWeights;
            QualitySettings.renderPipeline = savedPipeline;
            Object.DestroyImmediate(otherCameraObject);
            Refresh(preview);
        }
    }

    private static void CheckBoundsContain(Bounds actual, Bounds expected, string label)
    {
        // Avoid false negatives from floating-point roundoff at the remote stage.
        actual.Expand(.025f);
        Check(actual.Contains(expected.min) && actual.Contains(expected.max),
            "Common loading culling envelope was lost: " + label + "; actual=" + actual + "; expected=" + expected);
    }

    private static int CaptureIsolated(LoadingPlayerPreview preview, Renderer[] renderers, HashSet<Renderer> wanted,
        Camera camera, RenderTexture target, string path, string label)
    {
        SetPreviewEnabled(preview, false);
        try
        {
            foreach (Renderer renderer in renderers) renderer.forceRenderingOff = !wanted.Contains(renderer);
            return CheckSilhouette(Capture(camera, target, path), target.width, target.height, label, false);
        }
        finally { SetPreviewEnabled(preview, true); Refresh(preview); }
    }

    private static void SetPreviewEnabled(LoadingPlayerPreview preview, bool enabled)
    {
        // Runtime MonoBehaviours do not receive lifecycle messages in an edit
        // preview scene. Exercise the exact player subscriptions explicitly.
        typeof(LoadingPlayerPreview).GetMethod("OnDisable", Methods).Invoke(preview, null);
        preview.enabled = enabled;
        if (enabled) typeof(LoadingPlayerPreview).GetMethod("OnEnable", Methods).Invoke(preview, null);
    }

    private static void ValidatePresentation(LoadingScreenAssets assets, GameObject stage, RenderTexture runnerTexture,
        StringBuilder report)
    {
        var managerObject = new GameObject("Complete loading UI fixture (temporary)");
        SceneManager.MoveGameObjectToScene(managerObject, stage.scene);
        managerObject.SetActive(false);
        LoadingScreenManager manager = managerObject.AddComponent<LoadingScreenManager>();
        SetField(manager, "assets", assets);
        SetField(manager, "loadingFont", Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF"));
        var cameraObject = new GameObject("Complete loading UI camera (temporary)", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, stage.scene);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.scene = stage.scene; camera.enabled = false;
        camera.cullingMask = 1 << Layer;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.magenta;
        camera.nearClipPlane = .01f; camera.farClipPlane = 100f;
        camera.useOcclusionCulling = false; camera.allowHDR = false; camera.allowMSAA = false;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        GameObject presentation = null;
        RenderTexture uiTarget = null;
        var localFontMaterials = new List<Material>();
        try
        {
            typeof(LoadingScreenManager).GetMethod("BuildPresentation", Methods).Invoke(manager, null);
            presentation = GetField<GameObject>(manager, "presentationRoot");
            foreach (TextMeshProUGUI text in presentation.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text.fontSharedMaterial == null) continue;
                var material = new Material(text.fontSharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
                localFontMaterials.Add(material); text.fontSharedMaterial = material;
            }
            // The manager remains inactive, so neither Awake nor any live
            // singleton/save/loading flow is touched. Render its real UI child.
            presentation.transform.SetParent(null, false);
            Canvas canvas = presentation.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            CanvasScaler scaler = presentation.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            GetField<RawImage>(manager, "playerImage").texture = runnerTexture;
            GetField<CanvasGroup>(manager, "canvasGroup").alpha = 1f;
            foreach (Transform child in presentation.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = Layer;
            RectTransform runner = GetField<RectTransform>(manager, "playerRect");
            RectTransform track = GetField<RectTransform>(manager, "progressTrack");
            var sizes = new[] { new Vector2Int(2340, 1080), new Vector2Int(2048, 1280) };
            foreach (Vector2Int size in sizes)
            {
                uiTarget = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                uiTarget.Create(); camera.targetTexture = uiTarget; camera.aspect = (float)size.x / size.y;
                // Match the production CanvasScaler's geometric interpolation
                // using this isolated camera's actual pixel dimensions.
                float scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(size.x / scaler.referenceResolution.x, 2f),
                    Mathf.Log(size.y / scaler.referenceResolution.y, 2f), scaler.matchWidthOrHeight));
                canvas.scaleFactor = scale;
                Canvas.ForceUpdateCanvases();
                typeof(LoadingScreenManager).GetMethod("UpdatePresentationLayout", Methods).Invoke(manager, null);
                foreach (float progress in new[] { 0f, 1f })
                {
                    typeof(LoadingScreenManager).GetMethod("SetProgress", Methods).Invoke(manager, new object[] { progress });
                    Canvas.ForceUpdateCanvases();
                    Rect runnerViewport = CheckUiViewport(runner, camera, "runner", size);
                    Rect trackViewport = CheckUiViewport(track, camera, "track", size);
                    Check(runnerViewport.xMin >= trackViewport.xMin && runnerViewport.xMax <= trackViewport.xMax,
                        "Runner RawImage crosses a progress-track end cap at " + size + "/" + progress);
                    string path = Images + "/LoadingUI_" + size.x + "x" + size.y + "_" + (progress * 100f).ToString("F0") + "Percent.png";
                    Color32[] pixels = Capture(camera, uiTarget, path);
                    Check(pixels.Any(pixel => pixel.g > 100 && pixel.r > 100 && pixel.b < 240),
                        "Complete loading UI did not render in the isolated camera viewport.");
                    int runnerPixels = CountBluePixels(pixels, size, runnerViewport);
                    Check(runnerPixels >= 40, "The complete loading UI has no rendered runner pixels: " + path);
                    Check(GetField<TextMeshProUGUI>(manager, "percentageText").text == (progress >= 1f ? "100%" : "0%"),
                        "The actual loading percentage does not match the tested endpoint.");
                    report.AppendLine($"PASS: complete loading UI {size.x}x{size.y} at {progress:P0}: runner={runnerViewport}, track={trackViewport}; both inside viewport margins and runner inside progress end caps; actual PNG captured.");
                }
                camera.targetTexture = null; uiTarget.Release(); Object.DestroyImmediate(uiTarget); uiTarget = null;
            }
        }
        finally
        {
            if (uiTarget != null) { uiTarget.Release(); Object.DestroyImmediate(uiTarget); }
            if (presentation != null) Object.DestroyImmediate(presentation);
            foreach (Material material in localFontMaterials) Object.DestroyImmediate(material);
            foreach (string field in new[] { "previewTexture", "roundedUiSprite", "roundedUiTexture" })
            {
                Object temporary = GetField<Object>(manager, field);
                if (temporary is RenderTexture texture) texture.Release();
                if (temporary != null) Object.DestroyImmediate(temporary);
                SetField(manager, field, null);
            }
            Object.DestroyImmediate(managerObject); Object.DestroyImmediate(cameraObject);
        }
    }

    private static Rect CheckUiViewport(RectTransform rect, Camera camera, string label, Vector2Int size)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector3 minimum = camera.WorldToViewportPoint(corners[0]);
        Vector3 maximum = camera.WorldToViewportPoint(corners[2]);
        Rect viewport = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        const float margin = .02f;
        Check(viewport.xMin >= margin && viewport.xMax <= 1f - margin &&
            viewport.yMin >= margin && viewport.yMax <= 1f - margin,
            "Loading " + label + " crosses viewport margins at " + size + ": " + viewport);
        return viewport;
    }

    private static int CountBluePixels(Color32[] pixels, Vector2Int size, Rect viewport)
    {
        int count = 0;
        for (int y = Mathf.Max(0, Mathf.FloorToInt(viewport.yMin * size.y)); y < Mathf.Min(size.y, Mathf.CeilToInt(viewport.yMax * size.y)); y++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(viewport.xMin * size.x)); x < Mathf.Min(size.x, Mathf.CeilToInt(viewport.xMax * size.x)); x++)
            {
                Color32 pixel = pixels[y * size.x + x];
                if (pixel.b > 40 && pixel.b > pixel.r * 1.2f && pixel.b > pixel.g * 1.15f) count++;
            }
        return count;
    }

    private static T GetField<T>(object target, string name) => (T)target.GetType().GetField(name, Methods).GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, Methods).SetValue(target, value);

    private static Bounds GetGeometryBounds(LoadingPlayerPreview preview)
    {
        object[] arguments = { default(Bounds) };
        Check((bool)typeof(LoadingPlayerPreview).GetMethod("TryGetPreviewBounds", Methods).Invoke(preview, arguments),
            "Loading run geometry envelope was not captured.");
        return (Bounds)arguments[0];
    }

    private static void Refresh(LoadingPlayerPreview preview) =>
        typeof(LoadingPlayerPreview).GetMethod("RefreshVisibility", Methods).Invoke(preview, null);

    private static void ValidateRenderFlags(Renderer[] renderers)
    {
        foreach (Renderer renderer in renderers.Where(renderer => renderer.gameObject.activeInHierarchy))
        {
            Check(renderer.enabled && !renderer.forceRenderingOff && !renderer.allowOcclusionWhenDynamic &&
                renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly, "Preview is hidden by renderer flags: " + renderer.name);
            if (renderer is SkinnedMeshRenderer skinned)
                Check(!skinned.updateWhenOffscreen && skinned.forceMatrixRecalculationPerRender &&
                    skinned.quality == SkinQuality.Bone4,
                    "Preview skinned mesh can overwrite its culling envelope or inherit global skin quality: " + renderer.name);
        }
    }

    private static void CheckHierarchy(Dictionary<GameObject, bool> snapshot)
    {
        foreach (var item in snapshot) Check(item.Key.activeSelf == item.Value,
            "Visibility repair changed equipped/unequipped model activation: " + item.Key.name);
    }

    private static void CheckColors(IEnumerable<CosmeticModelBinding> bindings, CosmeticLoadoutData loadout)
    {
        foreach (CosmeticModelBinding binding in bindings)
            foreach (CosmeticBindingUtility.MaterialSlot slot in CosmeticBindingUtility.GetMaterialSlots(binding))
                foreach (CosmeticBindingUtility.MaterialTarget target in slot.Targets)
                {
                    var block = new MaterialPropertyBlock(); target.Renderer.GetPropertyBlock(block, target.Index);
                    Color wanted = loadout.GetMaterialColor(binding.cosmeticID, slot.Key, binding.category);
                    Color actual = block.GetColor(slot.Material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
                    Check(Mathf.Abs(actual.r - wanted.r) < .005f && Mathf.Abs(actual.g - wanted.g) < .005f &&
                        Mathf.Abs(actual.b - wanted.b) < .005f && Mathf.Abs(actual.a - wanted.a) < .005f,
                        "Loading visibility repair changed an independent material color: " + binding.cosmeticID + "/" + slot.Key);
                }
    }

    private static Color32[] Capture(Camera camera, RenderTexture target, string path)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        Check(RenderPipeline.SupportsRenderRequest(camera, request), "The actual URP loading render path is unavailable.");
        RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            if (path != null) File.WriteAllBytes(path, texture.EncodeToPNG());
            return texture.GetPixels32();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }

    private static void CheckGeometryClipping(GameObject model, Camera camera, string label)
    {
        var scratch = new Mesh();
        var vertices = new List<Vector3>();
        try
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.gameObject.activeInHierarchy))
            {
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                { skinned.BakeMesh(scratch, true); mesh = scratch; }
                else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                mesh.GetVertices(vertices);
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 view = camera.WorldToViewportPoint(renderer.transform.TransformPoint(vertex));
                    Check(view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f &&
                        view.z > camera.nearClipPlane && view.z < camera.farClipPlane,
                        "Actual posed geometry crosses camera clipping planes: " + label + "/" + renderer.name + " viewport=" + view);
                }
                vertices.Clear();
            }
        }
        finally { Object.DestroyImmediate(scratch); }
    }

    private static void DescribeBaking(IEnumerable<SkinnedMeshRenderer> renderers, StringBuilder report)
    {
        var scratch = new Mesh();
        try
        {
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                renderer.BakeMesh(scratch, false); scratch.RecalculateBounds();
                Bounds unscaled = scratch.bounds;
                renderer.BakeMesh(scratch, true); scratch.RecalculateBounds();
                Bounds scaled = scratch.bounds;
                report.AppendLine($"DIAGNOSTIC baked {renderer.name}: false={unscaled}, true={scaled}, transform.world={ConvertBounds(renderer.transform.localToWorldMatrix, unscaled)}, renderer.world={ConvertBounds(renderer.localToWorldMatrix, unscaled)}, bounds={renderer.bounds}, transform.lossyScale={renderer.transform.lossyScale}, renderer.lossyScale={renderer.localToWorldMatrix.lossyScale}, renderer.position={renderer.localToWorldMatrix.GetColumn(3)}, transform.position={renderer.transform.position}, renderer.matrix={renderer.localToWorldMatrix}, transform.matrix={renderer.transform.localToWorldMatrix}");
            }
        }
        finally { Object.DestroyImmediate(scratch); }
    }

    private static Bounds ConvertBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Bounds result = new Bounds(matrix.MultiplyPoint3x4(bounds.min), Vector3.zero);
        for (int index = 0; index < 8; index++)
            result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3((index & 1) == 0 ? bounds.min.x : bounds.max.x,
                (index & 2) == 0 ? bounds.min.y : bounds.max.y, (index & 4) == 0 ? bounds.min.z : bounds.max.z)));
        return result;
    }

    private static int CheckSilhouette(Color32[] pixels, int width, int height, string label, bool requireFullBody)
    {
        int count = 0, minX = width, maxX = -1, minY = height, maxY = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (pixels[y * width + x].a > 12)
                { count++; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
        Check(count >= (requireFullBody ? 300 : 12), "Blank/missing rendered geometry: " + label + " (" + count + " pixels)");
        Check(minX > 0 && maxX < width - 1 && minY > 0 && maxY < height - 1,
            "Rendered head, feet or cosmetics touch the image edge: " + label + $" [{minX},{minY}–{maxX},{maxY}]");
        if (requireFullBody) Check(maxY - minY > height * .25f, "Full-body loading preview is too small: " + label);
        return count;
    }

    private static string PixelHash(Color32[] pixels)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (Color32 pixel in pixels) { hash = (hash ^ pixel.r) * 16777619; hash = (hash ^ pixel.g) * 16777619; hash = (hash ^ pixel.b) * 16777619; hash = (hash ^ pixel.a) * 16777619; }
            return hash.ToString("X8");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
