#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in Canyon-only setup and asynchronous bake. Never closes or saves unrelated/dirty scenes.</summary>
public static class CanyonOcclusionSetup
{
    public const string SetupRequest = "Temp/canyon-occlusion-setup-v1.request";
    public const string AuditRequest = "Temp/canyon-occlusion-audit-v1.request";
    public const string ValidateRequest = "Temp/canyon-occlusion-validate-v1.request";
    public const string BakeRequest = "Temp/canyon-occlusion-bake-v1.request";
    public const string ReportPath = "Temp/CanyonOcclusionSetup.txt";
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string RootName = "Camera-Safe Occlusion";
    private const string GeneratedRootName = "---- GENERATED PROPS ------";
    private const string BakeAssetPath = "Assets/Scenes/CanyonCrossing/OcclusionCullingData.asset";
    private const StaticEditorFlags OcclusionBits = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static bool running;
    private static double nextRequestCheck;
    private static volatile BakeJob bake;

    // These are scene GameObjects, not prefab assets or recursive hierarchy roots.
    private static readonly Candidate[] Approved =
    {
        new Candidate("Environment/CanyonCliff_8(Clone)", 1714115610L),
        new Candidate("Environment/MAIN CITY/House", 1940678866728890966L),
        new Candidate("Environment/MAIN CITY/medieval1", 8130418773711994616L),
        new Candidate("Environment/MAIN CITY/medieval1 (1)", 8130418773220224147L),
        new Candidate("Environment/MAIN CITY/medieval2", 4848488864532632100L),
        new Candidate("Environment/MAIN CITY/medieval2 (1)", 4848488865931512088L),
        new Candidate("Environment/MAIN CITY/medieval2 (2)", 4848488864617566007L),
        new Candidate("Environment/MAIN CITY/medieval2 (3)", 4848488864264695380L),
        new Candidate("Environment/MAIN CITY/medieval2 (4)", 4848488864838324691L),
        new Candidate("Environment/MAIN CITY/medieval3", 1709339707852818222L),
        new Candidate("Environment/MAIN CITY/medieval3 (1)", 1709339706456344616L),
        new Candidate("Environment/MAIN CITY/medieval3 (2)", 1709339706447495230L),
        new Candidate("Environment/MAIN CITY/medieval7", 5561852482710785309L)
    };

    private sealed class Candidate
    {
        public readonly string path;
        public readonly long localId;
        public Candidate(string hierarchyPath, long id) { path = hierarchyPath; localId = id; }
    }

    private sealed class Inspection
    {
        public Scene scene;
        public GameObject[] objects;
        public MeshRenderer[] approved;
        public Camera[] cameras;
        public Camera gameplay;
        public CanyonOcclusionProfile profile;
        public Bounds cityBounds, cliffBounds;
        public readonly StringBuilder report = new StringBuilder();
    }

    private sealed class BakeJob
    {
        public Inspection inspection;
        public string contentHash, sceneFileHash;
        public string backupDirectory;
        public bool interrupted;
        public volatile bool nativeFailureObserved;
        public volatile string nativeFailureMessage;
        public string previousBakeHash;
        public DateTime previousBakeWriteUtc, startUtc;
        public double startTime, nativeFinishedTime;
    }

    private sealed class CameraSnapshot
    {
        private readonly bool active, enabled, orthographic, spherical;
        private readonly int mask;
        private readonly float near, far, size, depth;
        private readonly Rect rect;
        private readonly RenderTexture target;
        private readonly float[] distances;
        public CameraSnapshot(Camera camera)
        {
            active = camera.gameObject.activeSelf; enabled = camera.enabled;
            orthographic = camera.orthographic; spherical = camera.layerCullSpherical;
            mask = camera.cullingMask; near = camera.nearClipPlane; far = camera.farClipPlane;
            size = camera.orthographicSize; depth = camera.depth; rect = camera.rect;
            target = camera.targetTexture; distances = camera.layerCullDistances;
        }
        public bool Matches(Camera camera) => camera.gameObject.activeSelf == active && camera.enabled == enabled &&
            camera.orthographic == orthographic && camera.layerCullSpherical == spherical && camera.cullingMask == mask &&
            camera.nearClipPlane == near && camera.farClipPlane == far && camera.orthographicSize == size &&
            camera.depth == depth && camera.rect == rect && camera.targetTexture == target &&
            camera.layerCullDistances.SequenceEqual(distances);
    }

    private static bool Idle => !running && bake == null && !StaticOcclusionCulling.isRunning &&
        !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
        !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void Poll()
    {
        if (bake != null) { PollBake(); return; }
        if (!Idle || EditorApplication.timeSinceStartup < nextRequestCheck) return;
        nextRequestCheck = EditorApplication.timeSinceStartup + 2d;
        // One explicit operation per request; setup never silently queues a bake or scene save.
        if (Consume(AuditRequest)) Audit();
        else if (Consume(SetupRequest)) Setup();
        else if (Consume(ValidateRequest)) Validate();
        else if (Consume(BakeRequest)) Bake();
    }

    private static bool Consume(string request)
    {
        if (!File.Exists(request)) return false;
        File.Delete(request);
        return true;
    }

    [MenuItem("Tools/Civil Craft/Performance/Canyon Occlusion/0. Read-Only Preflight")]
    public static void Audit()
    {
        Run("PREFLIGHT", () =>
        {
            Inspection inspected = Inspect(false, false);
            NativeSettings();
            inspected.report.AppendLine("PASS: all exact native scene identities, opaque mesh/material and stable-component checks passed. No authoring, save or bake ran.");
            return inspected.report.ToString();
        });
    }

    [MenuItem("Tools/Civil Craft/Performance/Canyon Occlusion/1. Setup Saved Canyon Scene")]
    public static void Setup()
    {
        Run("SETUP", () =>
        {
            Inspection inspected = Inspect(false, true);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure Camera-Safe Canyon Occlusion");
            try
            {
                var originalStaticFlags = inspected.objects.ToDictionary(go => go, GameObjectUtility.GetStaticEditorFlags);
                var cameraSnapshots = inspected.cameras.ToDictionary(camera => camera, camera => new CameraSnapshot(camera));
                CanyonOcclusionProfile profile = inspected.profile;
                if (profile == null)
                {
                    Check(!inspected.objects.Any(o => o.name == RootName && o.transform.parent == null),
                        "An unrelated root already uses '" + RootName + "'. Rename it before setup.");
                    GameObject root = new GameObject(RootName);
                    SceneManager.MoveGameObjectToScene(root, inspected.scene);
                    Undo.RegisterCreatedObjectUndo(root, "Create Canyon occlusion profile");
                    profile = Undo.AddComponent<CanyonOcclusionProfile>(root);
                }
                Undo.RecordObject(profile, "Configure Canyon occlusion references");
                profile.enableGameplayOcclusion = false;
                profile.bakeVerified = false;
                profile.bakedData = null;
                profile.gameplayCamera = inspected.gameplay;
                profile.approvedOccluders = inspected.approved;
                profile.viewVolumes = new[]
                {
                    SetArea(profile, "City View Volume", inspected.cityBounds),
                    SetArea(profile, "Cliff View Volume", inspected.cliffBounds)
                };
                var approvedObjects = new HashSet<GameObject>(inspected.approved.Select(r => r.gameObject));
                int cleared = 0;
                foreach (GameObject go in inspected.objects)
                {
                    if (IsGeneratedProp(go.transform)) continue;
                    StaticEditorFlags original = GameObjectUtility.GetStaticEditorFlags(go);
                    StaticEditorFlags wanted = approvedObjects.Contains(go) ? original | OcclusionBits : original & ~OcclusionBits;
                    if (wanted == original) continue;
                    Undo.RecordObject(go, "Set explicit occlusion flags only");
                    GameObjectUtility.SetStaticEditorFlags(go, wanted);
                    if (!approvedObjects.Contains(go)) cleared++;
                }
                foreach (Camera camera in inspected.cameras)
                {
                    CanyonCameraOcclusionPolicy policy = camera.GetComponent<CanyonCameraOcclusionPolicy>();
                    if (policy == null) policy = Undo.AddComponent<CanyonCameraOcclusionPolicy>(camera.gameObject);
                    Undo.RecordObjects(new Object[] { policy, camera }, "Configure per-camera occlusion role");
                    policy.Configure(profile, camera == inspected.gameplay ?
                        CanyonCameraOcclusionPolicy.CameraRole.Gameplay : CanyonCameraOcclusionPolicy.CameraRole.Excluded);
                    // Start fail-open; the runtime policy alone can enable the explicitly approved player camera.
                    camera.useOcclusionCulling = false;
                    EditorUtility.SetDirty(policy);
                    Check(cameraSnapshots[camera].Matches(camera), "Camera activation, projection, culling mask/distance or texture changed: " + camera.name);
                }
                SetBakeParameter("smallestOccluder", 5f);
                SetBakeParameter("smallestHole", .25f);
                SetBakeParameter("backfaceThreshold", 100f);
                EditorUtility.SetDirty(profile);
                EditorSceneManager.MarkSceneDirty(inspected.scene);
                foreach (var original in originalStaticFlags)
                    Check((GameObjectUtility.GetStaticEditorFlags(original.Key) & ~OcclusionBits) == (original.Value & ~OcclusionBits),
                        "Unrelated static flags changed: " + HierarchyPath(original.Key));
                Undo.CollapseUndoOperations(group);
                inspected.report.AppendLine("SETUP: 13 explicit opaque mesh occluders; only occlusion static bits changed. Cleared excluded objects: " + cleared + ".");
                inspected.report.AppendLine("SETUP: " + inspected.cameras.Length + " cameras: one explicit Gameplay; every other camera Excluded. Camera activation, masks, distances, textures and projections preserved.");
                inspected.report.AppendLine("SETUP: City/cliff view volumes expanded by 30 m per side; bake parameters 5 / 0.25 / 100.");
                inspected.report.AppendLine("ACTION REQUIRED: Save CanyonCrossing with your changes, then use '3. Bake Saved Canyon Scene'. No scene was saved automatically.");
                inspected.report.AppendLine("ROLLOUT: Developer master switch remains OFF until visual and Android frame-time comparisons pass.");
                return inspected.report.ToString();
            }
            catch { Undo.RevertAllDownToGroup(group); throw; }
            finally { Undo.IncrementCurrentGroup(); }
        });
    }

    [MenuItem("Tools/Civil Craft/Performance/Canyon Occlusion/2. Validate Open Canyon Scene")]
    public static void Validate()
    {
        Run("VALIDATE", () =>
        {
            Inspection inspected = Inspect(true, false);
            ValidateConfigured(inspected);
            Object linked = ReadBakedData();
            inspected.report.AppendLine("DIAGNOSTIC: native link=" + (linked == null ? "null" : AssetDatabase.GetAssetPath(linked)) +
                "; reported PVS bytes=" + StaticOcclusionCulling.umbraDataSize + "; dirty=" + inspected.scene.isDirty +
                "; native settings=" + EditorJsonUtility.ToJson(NativeSettings()));
            if (inspected.profile.bakeVerified)
            {
                Check(linked != null && linked == inspected.profile.bakedData && ReadPvsDataSize(linked) > 0,
                    "The profile's verified bake does not match native scene occlusion data. Re-bake safely.");
                inspected.report.AppendLine("PASS: native scene/profile share " + AssetDatabase.GetAssetPath(linked) + "; asset PVS bytes=" + ReadPvsDataSize(linked) + ".");
            }
            else inspected.report.AppendLine("NOTE: No verified linked bake yet; runtime stays fail-open.");
            inspected.report.AppendLine("PASS: one Gameplay camera; all additional cameras Excluded; generated props and non-occlusion flags preserved.");
            inspected.report.AppendLine("NOTE: This read-only validation does not save, bake, change account/player data, or establish Android performance improvement.");
            return inspected.report.ToString();
        });
    }

    [MenuItem("Tools/Civil Craft/Performance/Canyon Occlusion/3. Bake Saved Canyon Scene")]
    public static void Bake()
    {
        Run("BAKE", () =>
        {
            Inspection inspected = Inspect(true, true);
            ValidateConfigured(inspected);
            Check(!inspected.profile.enableGameplayOcclusion, "Turn the developer master switch off before rebaking.");
            string backup = BackupCurrentBake(inspected.scene);
            // An old successful bake must not be treated as the new result after failure or cancellation.
            inspected.profile.bakeVerified = false;
            inspected.profile.bakedData = null;
            EditorUtility.SetDirty(inspected.profile);
            var job = new BakeJob
            {
                inspection = inspected,
                backupDirectory = backup,
                contentHash = HashSceneContent(inspected.scene),
                sceneFileHash = HashFile(ScenePath),
                previousBakeHash = File.Exists(BakeAssetPath) ? HashFile(BakeAssetPath) : null,
                previousBakeWriteUtc = File.Exists(BakeAssetPath) ? File.GetLastWriteTimeUtc(BakeAssetPath) : DateTime.MinValue,
                startUtc = DateTime.UtcNow,
                startTime = EditorApplication.timeSinceStartup
            };
            bake = job;
            Application.logMessageReceivedThreaded -= ObserveBakeLog;
            Application.logMessageReceivedThreaded += ObserveBakeLog;
            try
            {
                Check(StaticOcclusionCulling.GenerateInBackground(), "Unity did not start background occlusion generation. Existing bake was not cleared.");
            }
            catch
            {
                Application.logMessageReceivedThreaded -= ObserveBakeLog;
                bake = null;
                throw;
            }
            inspected.report.AppendLine("RUNNING: CanyonCrossing only. Do not edit/save the scene or enter Play Mode while Unity bakes.");
            if (!string.IsNullOrEmpty(backup)) inspected.report.AppendLine("BACKUP: " + backup);
            inspected.report.AppendLine("The scene is saved only after successful data-link verification, and only if no scene content changed during the bake.");
            return inspected.report.ToString();
        });
    }

    [MenuItem("Tools/Civil Craft/Performance/Canyon Occlusion/Cancel Owned Bake")]
    public static void CancelBake()
    {
        if (bake == null) { Debug.LogWarning("[Canyon Occlusion] No bake owned by this tool."); return; }
        bake.interrupted = true;
        StaticOcclusionCulling.Cancel();
    }

    private static void PollBake()
    {
        BakeJob job = bake;
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1 ||
            !job.inspection.scene.IsValid() || !job.inspection.scene.isLoaded ||
            SceneManager.GetActiveScene() != job.inspection.scene)
        {
            job.interrupted = true;
            if (StaticOcclusionCulling.isRunning) StaticOcclusionCulling.Cancel();
        }
        if (StaticOcclusionCulling.isRunning || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        // Wait for the imported asset/link. umbraDataSize belongs to Unity's
        // current renderer scene and can remain zero until a view renders.
        // Validate the linked asset's PVS rather than depending on Editor focus.
        if (job.nativeFinishedTime == 0d) job.nativeFinishedTime = EditorApplication.timeSinceStartup;
        double elapsedSinceFinish = EditorApplication.timeSinceStartup - job.nativeFinishedTime;
        if (!job.interrupted && !job.nativeFailureObserved && elapsedSinceFinish < 30d &&
            (elapsedSinceFinish < 2d || ReadBakedData() == null || ReadPvsDataSize(ReadBakedData()) <= 0))
            return;
        bake = null;
        Application.logMessageReceivedThreaded -= ObserveBakeLog;
        try
        {
            Check(!job.interrupted, "Bake cancelled or interrupted. No scene was auto-saved; the profile remains unverified.");
            Check(!job.nativeFailureObserved, "Unity reported bake failure/cancellation: " + job.nativeFailureMessage + ". Nothing was auto-saved.");
            Check(HashFile(ScenePath) == job.sceneFileHash && HashSceneContent(job.inspection.scene) == job.contentHash,
                "Scene content changed during the bake. No auto-save was performed; inspect the result and save/re-bake manually.");
            Object linked = ReadBakedData();
            Check(linked != null && ReadPvsDataSize(linked) > 0,
                "Unity finished without linked nonempty occlusion data. The old bake was not explicitly cleared.");
            string assetPath = AssetDatabase.GetAssetPath(linked);
            Check(assetPath == BakeAssetPath && File.Exists(assetPath) && new FileInfo(assetPath).Length > 0,
                "Generated bake is not the expected CanyonCrossing-local asset: " + assetPath);
            DateTime generatedWriteUtc = File.GetLastWriteTimeUtc(assetPath);
            bool freshlyWritten = generatedWriteUtc >= job.startUtc.AddSeconds(-2d) &&
                (job.previousBakeHash == null || generatedWriteUtc > job.previousBakeWriteUtc || HashFile(assetPath) != job.previousBakeHash);
            Check(freshlyWritten,
                "No fresh native bake output was written during this owned job. A retained old bake is not proof of success; nothing was auto-saved.");
            CanyonOcclusionProfile profile = job.inspection.profile;
            profile.bakedData = linked;
            profile.bakeVerified = true;
            profile.enableGameplayOcclusion = false;
            EditorUtility.SetDirty(profile);
            EditorSceneManager.MarkSceneDirty(job.inspection.scene);
            Check(EditorSceneManager.SaveScene(job.inspection.scene), "Verified bake linked, but Unity could not save CanyonCrossing. Save manually.");
            Check(ReadSavedBakeGuid(ScenePath) == AssetDatabase.AssetPathToGUID(assetPath),
                "Saved scene does not reference the verified baked asset.");
            job.inspection.report.AppendLine("PASS: background bake completed; linked asset PVS bytes=" + ReadPvsDataSize(linked) + ".");
            job.inspection.report.AppendLine("PASS: fresh native output write verified (UTC " + generatedWriteUtc.ToString("O") + "), with no observed bake failure/cancellation.");
            job.inspection.report.AppendLine("PASS: scene/profile/asset link verified and only the initially saved, unchanged CanyonCrossing scene was saved.");
            job.inspection.report.AppendLine("ROLLOUT: Master remains OFF. Use controlled on/off visual checks and Android CPU/GPU frame-time measurements before enabling by default.");
            WriteReport("BAKE COMPLETE", job.inspection.report.ToString());
        }
        catch (Exception error) { WriteReport("BAKE FAILED", error.ToString()); Debug.LogException(error); }
    }

    private static void BeforeReload()
    {
        if (bake == null) return;
        bake.interrupted = true;
        if (StaticOcclusionCulling.isRunning) StaticOcclusionCulling.Cancel();
        Application.logMessageReceivedThreaded -= ObserveBakeLog;
        WriteReport("BAKE INTERRUPTED", "Assembly reload interrupted the owned bake. Nothing was auto-saved. Validate/re-bake the saved scene; master stays off.");
    }

    private static void ObserveBakeLog(string message, string stackTrace, LogType type)
    {
        // Unity can report native bake results on worker threads. Do not use Unity object APIs here.
        BakeJob job = bake;
        if (job == null || string.IsNullOrEmpty(message) || message.StartsWith("[Canyon Occlusion]", StringComparison.Ordinal)) return;
        string lower = message.ToLowerInvariant();
        bool relatesToBake = lower.Contains("occlusion") || lower.Contains("umbra") || lower.Contains("visibility bake");
        bool cancelled = lower.Contains("cancelled") || lower.Contains("canceled") || lower.Contains("cancellation");
        bool failed = type == LogType.Error || type == LogType.Exception || type == LogType.Assert ||
            lower.Contains("failed") || lower.Contains("failure") || lower.Contains("could not") || lower.Contains("unable to");
        if (!relatesToBake || !cancelled && !failed) return;
        job.nativeFailureMessage = message;
        job.nativeFailureObserved = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (bake != null && state == PlayModeStateChange.ExitingEditMode) CancelBake();
    }

    private static void Run(string action, Func<string> operation)
    {
        try
        {
            Check(Idle, "Wait for idle Edit Mode, finished imports, no Prefab Mode and no other occlusion bake.");
            running = true;
            WriteReport(action, operation());
        }
        catch (Exception error) { WriteReport(action + " FAILED", error.ToString()); Debug.LogException(error); }
        finally { running = false; }
    }

    private static Inspection Inspect(bool requireProfile, bool requireSaved)
    {
        Check(SceneManager.sceneCount == 1, "Open CanyonCrossing alone. This tool never closes other scenes or bakes additive scenes together.");
        Scene scene = SceneManager.GetActiveScene();
        Check(scene.IsValid() && scene.isLoaded && scene.name == CanyonOcclusionProfile.TargetSceneName && scene.path == ScenePath,
            "Open the authored Assets/Scenes/CanyonCrossing.unity scene in Edit Mode.");
        Check(!requireSaved || !scene.isDirty, "CanyonCrossing has unsaved edits. Save it yourself before setup/bake; this tool will not save them.");
        GameObject[] objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToArray();
        var result = new Inspection { scene = scene, objects = objects };
        var byPath = objects.GroupBy(HierarchyPath).ToDictionary(g => g.Key, g => g.ToArray());
        result.approved = new MeshRenderer[Approved.Length];
        for (int i = 0; i < Approved.Length; i++)
        {
            Candidate candidate = Approved[i];
            Check(byPath.TryGetValue(candidate.path, out GameObject[] matches) && matches.Length == 1,
                "Missing or ambiguous audited occluder: " + candidate.path);
            GameObject go = matches[0];
            long actualId = LocalFileId(go);
            Check(actualId == candidate.localId, "Audited scene object was replaced: " + candidate.path + " (expected " +
                candidate.localId + ", actual " + actualId + "). Re-audit instead of substituting similarly named geometry.");
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            ValidateOpaqueStable(renderer, candidate.path);
            result.approved[i] = renderer;
            if (i == 0) result.cliffBounds = renderer.bounds;
            else if (i == 1) result.cityBounds = renderer.bounds;
            else result.cityBounds.Encapsulate(renderer.bounds);
            result.report.AppendLine("PASS approved: " + candidate.path);
        }
        result.cityBounds.Expand(60f);
        result.cliffBounds.Expand(60f);
        foreach (GameObject go in objects)
            Check(!IsGeneratedProp(go.transform) || (GameObjectUtility.GetStaticEditorFlags(go) & OcclusionBits) == 0,
                "Generated props have occlusion static flags: " + HierarchyPath(go) + ". Their flags are intentionally unchanged; re-audit before baking.");
        GameManager[] managers = objects.SelectMany(o => o.GetComponents<GameManager>()).ToArray();
        Check(managers.Length == 1 && managers[0].MainCamera != null && managers[0].MainCamera.gameObject.scene == scene,
            "Expected exactly one scene GameManager with its explicit MainCamera reference.");
        result.gameplay = managers[0].MainCamera;
        Check(LocalFileId(result.gameplay.gameObject) == 8648618671580156816L && !result.gameplay.orthographic && result.gameplay.targetTexture == null,
            "GameManager.MainCamera is not the audited perspective PlayerCamera.");
        result.cameras = objects.SelectMany(o => o.GetComponents<Camera>()).ToArray();
        Check(result.cameras.Contains(result.gameplay), "Gameplay camera was not found in CanyonCrossing's scene hierarchy.");
        CanyonOcclusionProfile[] profiles = objects.SelectMany(o => o.GetComponents<CanyonOcclusionProfile>()).ToArray();
        Check(profiles.Length <= 1, "More than one Canyon occlusion profile exists.");
        result.profile = profiles.FirstOrDefault();
        Check(!requireProfile || result.profile != null, "Run Setup first, then save CanyonCrossing.");
        Check(result.profile == null || result.profile.gameObject.name == RootName && result.profile.transform.parent == null,
            "Existing profile must remain on its scene-local '" + RootName + "' root.");
        result.report.AppendLine("PASS: CanyonCrossing-only scope; exact audited object identities; " + result.cameras.Length + " scene cameras.");
        return result;
    }

    private static void ValidateOpaqueStable(MeshRenderer renderer, string path)
    {
        Check(renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy,
            "Audited renderer is absent, disabled or inactive: " + path);
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        Check(filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0,
            "Audited renderer has no stable mesh: " + path);
        Check(Finite(renderer.bounds.center) && Finite(renderer.bounds.size) && renderer.bounds.size.sqrMagnitude > .01f,
            "Invalid audited bounds: " + path);
        foreach (Component component in renderer.GetComponents<Component>())
        {
            Check(component != null, "Missing script on audited occluder: " + path);
            if (component is Transform || component is MeshFilter || component is MeshRenderer || component is Collider) continue;
            Check(component.GetType().FullName == "Unity.AI.Navigation.NavMeshModifier",
                "Unapproved/dynamic component on audited occluder: " + path + " (" + component.GetType().FullName + ")");
        }
        for (Transform ancestor = renderer.transform; ancestor != null; ancestor = ancestor.parent)
        {
            Check(ancestor.GetComponent<Rigidbody>() == null && ancestor.GetComponent<Animator>() == null &&
                ancestor.GetComponent<Animation>() == null,
                "An audited occluder/ancestor can be moved by physics or animation: " + path);
            foreach (MonoBehaviour behaviour in ancestor.GetComponents<MonoBehaviour>())
            {
                Check(behaviour != null, "Missing ancestor script above audited occluder: " + path);
                string name = behaviour.GetType().FullName;
                // CanyonDirtPaths authors only transient road overlays, never these house meshes.
                // Unknown behaviour is not evidence of stable geometry: stop and re-audit it.
                Check(name == "Unity.AI.Navigation.NavMeshModifier" || behaviour is CanyonDirtPaths,
                    "Audited occluder has an unapproved potentially dynamic ancestor: " + path + " (" + name + ")");
            }
        }
        Material[] materials = renderer.sharedMaterials;
        Check(materials.Length > 0 && materials.Length >= filter.sharedMesh.subMeshCount,
            "Audited mesh has missing material slots: " + path);
        foreach (Material material in materials)
        {
            Check(material != null && material.shader != null && material.renderQueue <= 2500,
                "Audited mesh contains a missing, alpha-tested or transparent material: " + path);
            string renderType = material.GetTag("RenderType", false, "");
            Check(renderType == "Opaque" && (!material.HasProperty("_Surface") || material.GetFloat("_Surface") == 0f) &&
                (!material.HasProperty("_AlphaClip") || material.GetFloat("_AlphaClip") == 0f) &&
                (!material.HasProperty("_Mode") || material.GetFloat("_Mode") == 0f),
                "Audited mesh is not proven opaque: " + path + " (" + material.name + ")");
        }
    }

    private static void ValidateConfigured(Inspection inspected)
    {
        CanyonOcclusionProfile profile = inspected.profile;
        Check(profile.isActiveAndEnabled && profile.gameplayCamera == inspected.gameplay &&
            profile.approvedOccluders != null && profile.approvedOccluders.SequenceEqual(inspected.approved),
            "Profile references no longer match the exact approved camera/occluders. Run Setup and save.");
        Check(profile.viewVolumes != null && profile.viewVolumes.Length == 2 && profile.viewVolumes.Distinct().Count() == 2,
            "Expected exactly two distinct explicit view volumes.");
        ValidateArea(profile.viewVolumes[0], profile, "City View Volume", inspected.cityBounds);
        ValidateArea(profile.viewVolumes[1], profile, "Cliff View Volume", inspected.cliffBounds);
        var approvedObjects = new HashSet<GameObject>(inspected.approved.Select(r => r.gameObject));
        foreach (GameObject go in inspected.objects)
        {
            if (IsGeneratedProp(go.transform)) continue;
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go) & OcclusionBits;
            Check(flags == (approvedObjects.Contains(go) ? OcclusionBits : 0),
                "Occlusion static flags do not match explicit allowlist: " + HierarchyPath(go));
            foreach (OcclusionArea area in go.GetComponents<OcclusionArea>())
                Check(!GetIsViewVolume(area) || profile.viewVolumes.Contains(area),
                    "Unapproved extra view volume would broaden bake coverage: " + HierarchyPath(go));
        }
        foreach (Camera camera in inspected.cameras)
        {
            CanyonCameraOcclusionPolicy policy = camera.GetComponent<CanyonCameraOcclusionPolicy>();
            Check(policy != null && policy.enabled && policy.Profile == profile && policy.Role ==
                (camera == inspected.gameplay ? CanyonCameraOcclusionPolicy.CameraRole.Gameplay : CanyonCameraOcclusionPolicy.CameraRole.Excluded),
                "Missing or wrong per-camera policy: " + HierarchyPath(camera.gameObject));
            Check(!camera.useOcclusionCulling, "Saved camera switch must be off; runtime policy selectively enables only gameplay: " + camera.name);
        }
        Check(Mathf.Approximately(GetBakeParameter("smallestOccluder"), 5f) &&
            Mathf.Approximately(GetBakeParameter("smallestHole"), .25f) &&
            Mathf.Approximately(GetBakeParameter("backfaceThreshold"), 100f), "Unexpected bake parameters; expected 5 / 0.25 / 100.");
    }

    private static OcclusionArea SetArea(CanyonOcclusionProfile profile, string name, Bounds bounds)
    {
        Transform existing = profile.transform.Find(name);
        GameObject go;
        OcclusionArea area;
        if (existing == null)
        {
            go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, profile.gameObject.scene);
            go.transform.SetParent(profile.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create explicit view volume");
            area = Undo.AddComponent<OcclusionArea>(go);
        }
        else
        {
            go = existing.gameObject;
            area = go.GetComponent<OcclusionArea>();
            Check(area != null && go.GetComponents<Component>().Length == 2 && existing.childCount == 0,
                "An unrelated object occupies the owned view-volume name: " + name);
        }
        Undo.RecordObjects(new Object[] { go, go.transform, area }, "Configure bounded occlusion view volume");
        go.SetActive(true);
        go.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        area.center = Vector3.zero;
        area.size = bounds.size;
        var serialized = new SerializedObject(area);
        SerializedProperty property = serialized.FindProperty("m_IsViewVolume");
        Check(property != null, "Unity no longer exposes the expected m_IsViewVolume property. Setup stopped safely.");
        property.boolValue = true;
        serialized.ApplyModifiedProperties();
        return area;
    }

    private static void ValidateArea(OcclusionArea area, CanyonOcclusionProfile profile, string name, Bounds wanted)
    {
        Check(area != null && area.gameObject.activeInHierarchy && area.gameObject.scene == profile.gameObject.scene &&
            area.transform.parent == profile.transform && area.name == name && GetIsViewVolume(area),
            "Missing/inactive/unowned view volume: " + name);
        Check((area.transform.position - wanted.center).sqrMagnitude < .0001f && area.center.sqrMagnitude < .0001f &&
            (area.size - wanted.size).sqrMagnitude < .0001f && Quaternion.Angle(area.transform.rotation, Quaternion.identity) < .01f &&
            (area.transform.lossyScale - Vector3.one).sqrMagnitude < .0001f,
            "View volume no longer matches approved bounds plus 30 m per side: " + name);
        Check(profile.ContainsCameraPosition(wanted.center), "Runtime coverage cannot find the validated view-volume center.");
    }

    private static bool GetIsViewVolume(OcclusionArea area)
    {
        SerializedProperty property = new SerializedObject(area).FindProperty("m_IsViewVolume");
        Check(property != null, "Cannot validate Unity's Inspector-only Is View Volume switch.");
        return property.boolValue;
    }

    private static Object ReadBakedData()
    {
        Object settings = NativeSettings();
        SerializedProperty property = new SerializedObject(settings).FindProperty("m_OcclusionCullingData");
        Check(property != null, "Cannot validate Unity's native scene occlusion data link.");
        return property.objectReferenceValue;
    }

    public static int ReadPvsDataSize(Object linked)
    {
        if (linked == null) return 0;
        using (var serialized = new SerializedObject(linked))
        {
            SerializedProperty data = serialized.FindProperty("m_PVSData");
            return data != null && data.isArray ? data.arraySize : 0;
        }
    }

    private static Object NativeSettings()
    {
        PropertyInfo property = typeof(StaticOcclusionCulling).GetProperty("occlusionCullingSettings", StaticFlags);
        Object settings = property != null ? property.GetValue(null) as Object : null;
        Check(settings != null, "Unity 2022.3 native occlusion settings interface is unavailable.");
        return settings;
    }

    private static float GetBakeParameter(string name)
    {
        PropertyInfo property = typeof(StaticOcclusionCulling).GetProperty(name, StaticFlags);
        Check(property != null && property.PropertyType == typeof(float), "Cannot inspect bake parameter: " + name);
        return (float)property.GetValue(null);
    }

    private static void SetBakeParameter(string name, float value)
    {
        PropertyInfo property = typeof(StaticOcclusionCulling).GetProperty(name, StaticFlags);
        Check(property != null && property.CanWrite && property.PropertyType == typeof(float), "Cannot configure bake parameter: " + name);
        Undo.RecordObject(NativeSettings(), "Configure Canyon occlusion bake parameters");
        property.SetValue(null, value);
    }

    private static string BackupCurrentBake(Scene scene)
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Object linked = ReadBakedData();
        if (linked != null) sources.Add(AssetDatabase.GetAssetPath(linked));
        if (File.Exists(BakeAssetPath)) sources.Add(BakeAssetPath);
        if (sources.Count == 0) return null;
        string root = Path.GetFullPath(".");
        string destination = Path.GetFullPath(Path.Combine(root, "LocalBackups", "Occlusion",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        Check(destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !destination.StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "Unsafe occlusion backup destination.");
        Directory.CreateDirectory(destination);
        foreach (string source in sources)
        {
            Check(!string.IsNullOrEmpty(source) && source.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(source),
                "Current linked bake cannot be backed up safely: " + source);
            string uniqueName = AssetDatabase.AssetPathToGUID(source) + "-" + Path.GetFileName(source);
            File.Copy(source, Path.Combine(destination, uniqueName), false);
            if (File.Exists(source + ".meta")) File.Copy(source + ".meta", Path.Combine(destination, uniqueName + ".meta"), false);
        }
        File.Copy(scene.path, Path.Combine(destination, "CanyonCrossing.before-bake.unity"), false);
        File.WriteAllText(Path.Combine(destination, "README.txt"), "Recoverable pre-bake files and original scene link. No source file was deleted. Restore only after reviewing current scene work.\n" + string.Join("\n", sources));
        return destination;
    }

    private static long LocalFileId(Object value)
    {
        // This API returns the serialized scene instance ID, including prefab instances.
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string _, out long localId) && localId != 0) return localId;
        GlobalObjectId global = GlobalObjectId.GetGlobalObjectIdSlow(value);
        return unchecked((long)global.targetObjectId);
    }

    private static string HierarchyPath(GameObject go)
    {
        var names = new List<string>();
        for (Transform parent = go.transform; parent != null; parent = parent.parent) names.Add(parent.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static bool IsGeneratedProp(Transform transform)
    {
        for (Transform ancestor = transform; ancestor != null; ancestor = ancestor.parent)
            if (ancestor.name == GeneratedRootName) return true;
        return false;
    }

    private static string HashSceneContent(Scene scene)
    {
        var content = new StringBuilder();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = transform.gameObject;
                content.Append(go.GetInstanceID()).Append(EditorJsonUtility.ToJson(go));
                foreach (Component component in go.GetComponents<Component>())
                    content.Append(component == null ? "missing" : component.GetInstanceID() + EditorJsonUtility.ToJson(component));
            }
        using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(content.ToString())));
    }

    private static string HashFile(string path)
    {
        Check(File.Exists(path), "Expected saved scene file is missing: " + path);
        using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(path))
            return Convert.ToBase64String(sha.ComputeHash(stream));
    }

    private static string ReadSavedBakeGuid(string path)
    {
        using (var reader = new StreamReader(path))
            for (int lineNumber = 0; lineNumber < 100 && !reader.EndOfStream; lineNumber++)
            {
                string line = reader.ReadLine();
                if (!line.Contains("m_OcclusionCullingData:")) continue;
                int start = line.IndexOf("guid: ", StringComparison.Ordinal);
                return start >= 0 && line.Length >= start + 38 ? line.Substring(start + 6, 32) : null;
            }
        return null;
    }

    private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static void WriteReport(string status, string text)
    {
        Directory.CreateDirectory("Temp");
        File.WriteAllText(ReportPath, status + "\nUTC: " + DateTime.UtcNow.ToString("O") + "\n" + text);
        Debug.Log("[Canyon Occlusion] " + status + ": " + ReportPath);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
