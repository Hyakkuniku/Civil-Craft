#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit asset-only Concrete Pier setup. Never edits a contract or user scene.</summary>
[InitializeOnLoad]
public static class ConcretePierAssetAuthoring
{
    public const string RequestPath = "Temp/concrete-pier-assets.request";
    public const string ReportPath = "Logs/concrete-pier-assets.txt";
    private const string BaselinePath = "Logs/concrete-pier-assets-baseline.json";
    private const string MaterialPath = "Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset";
    private const string MaterialGuid = "7fbcf5a7e459d434f970d2c976504d5c";
    private const string WoodPierGuid = "b98aa9ba752ac28449feebc76f256bf2";
    private const string MarshContractPath = "Assets/BridgeBuilder/Data/Contract/TownRiver/Marsh Contract.asset";
    private const string Models = "Assets/BridgeBuilder/Prefabs/materials/map2/";
    private const string BottomPath = Models + "SLAB_LOW.Bottom.prefab";
    private const string TopPath = Models + "SLAB_LOW.Top.prefab";
    private const string ShaftPath = Models + "ConcretePier_Shaft.prefab";
    private const string CapPath = Models + "ConcretePier_Cap.prefab";
    private const string FbxPath = Models + "Concreate_Pie2_V2.fbx";
    private const string OriginalDefaultSprite = "Assets/Elements/UI/bm_ui/concrete pier2 not selected.png";
    private const string OriginalSelectedSprite = "Assets/Elements/UI/bm_ui/concrete pier2 selected.png";
    private const string DefaultSprite = "Assets/Elements/UI/bm_ui/pier concrete not selected.png";
    private const string SelectedSprite = "Assets/Elements/UI/bm_ui/concrete pier2 selected (1).png";
    private static readonly string[] SourcePaths =
        { BottomPath, TopPath, FbxPath, OriginalDefaultSprite, OriginalSelectedSprite, DefaultSprite, SelectedSprite };
    private static bool running;
    private static double nextPoll;

    [Serializable] private sealed class Entry { public string path; public string value; }
    [Serializable] private sealed class Baseline
    {
        public int version = 1;
        public string materialGuid;
        public string backupDirectory;
        public string status;
        public List<Entry> sources;
        public List<Entry> contracts;
        public List<Entry> numbers;
        public List<Entry> wrappers;
    }

    static ConcretePierAssetAuthoring() { EditorApplication.update += Poll; }
    private static bool Ready => !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
        !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1d;
        if (!Ready || !File.Exists(RequestPath)) return;
        string command = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        Execute(command);
    }

    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/1. Inspect Assets (Read Only)")]
    public static void Inspect() => Execute("inspect");
    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/2. Configure Asset and New Model Wrappers")]
    public static void Apply() => Execute("apply");
    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/3. Validate Assets (Read Only)")]
    public static void Validate() => Execute("validate");
    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/4. Preview Models (Read Only)")]
    public static void Preview() => Execute("preview");
    [MenuItem("Tools/Civil Craft/Materials/Concrete Pier/5. Inspect Scene State (Read Only)")]
    public static void Diagnose() => Execute("diagnose");

    private static void Execute(string command)
    {
        if (running) return;
        var report = new StringBuilder("Concrete Pier asset-only authoring: " + command + "\n");
        if (!Ready) { Write("NOT_RUN\nStop Play Mode, finish imports/compilation and close Prefab Mode. No assets changed.\n"); return; }
        running = true;
        Scene[] originalScenes = LoadedScenes();
        bool[] dirtyBefore = originalScenes.Select(scene => scene.isDirty).ToArray();
        int activeBefore = SceneManager.GetActiveScene().handle;
        Dictionary<Object, string> sceneObjectsBefore = SnapshotSceneObjects(originalScenes);
        List<Entry> sources = null, contracts = null;
        Scene preview = default;
        try
        {
            Require(command == "inspect" || command == "apply" || command == "validate" || command == "preview" || command == "diagnose", "Unknown Concrete Pier command.");
            sources = HashFiles(SourceFiles());
            contracts = HashFiles(ContractFiles());
            foreach (Entry sourceHash in sources) report.AppendLine("Original SHA256 " + sourceHash.path + " = " + sourceHash.value);
            report.AppendLine("Protected ContractSO asset/meta files: " + contracts.Count + ".");
            RequireNoContractReferences(contracts);
            Require(AssetDatabase.AssetPathToGUID(MaterialPath) == MaterialGuid, "Concrete Pier identity changed; the existing asset was not replaced.");
            BridgeMaterialSO material = Load<BridgeMaterialSO>(MaterialPath);
            Require(!EditorUtility.IsDirty(material), "Concrete Pier has unsaved Inspector edits. Save that asset before running asset setup; no user changes were overwritten.");
            var numericBefore = NumericValues(material);
            DescribeMaterial(material, report, numericBefore);
            GameObject bottom = Load<GameObject>(BottomPath), top = Load<GameObject>(TopPath);
            ValidateSource(bottom, 9060654051168416219L, command != "inspect", report);
            ValidateSource(top, 3050060251537468696L, command != "inspect", report);
            Load<Sprite>(DefaultSprite); Load<Sprite>(SelectedSprite);
            preview = EditorSceneManager.NewPreviewScene();
            Require(SceneManager.GetActiveScene().handle == activeBefore, "Creating an isolated preview unexpectedly changed the active scene.");
            Bounds bottomBounds = SourceBounds(bottom, preview), topBounds = SourceBounds(top, preview);
            DescribeBounds("Bottom at origin, preserving supplied rotation/scale", bottomBounds, report);
            DescribeBounds("Top at origin, preserving supplied rotation/scale", topBounds, report);
            Require(Finite(bottomBounds.center) && Finite(bottomBounds.size) && bottomBounds.size.y > .05f &&
                Finite(topBounds.center) && Finite(topBounds.size) && topBounds.size.y > .05f,
                "Supplied model bounds are empty or invalid.");

            if (command == "inspect" || command == "diagnose")
                report.AppendLine("INSPECTED: no asset writes. The bottom mesh needs a centered wrapper; the existing Bar cap offsets handle the top mesh pivot.");
            else if (command == "apply")
                ApplyAssets(material, bottom, top, bottomBounds, topBounds, preview, sources, contracts, numericBefore, report);
            else if (command == "preview")
                PreviewModels(material, bottom, top, bottomBounds, topBounds, preview, report);
            else
                ValidateConfigured(material, bottom, top, bottomBounds, topBounds, preview, report, true);

            if (command == "diagnose")
                foreach (Scene scene in originalScenes)
                    report.AppendLine("Loaded scene " + scene.path + ": dirty=" + scene.isDirty + ", roots=" + scene.rootCount +
                        "; saved file SHA256=" + (File.Exists(scene.path) ? Hash(scene.path) : "<not saved to disk>") + ".");

            RequireSameNumbers(numericBefore, NumericValues(material), "This command changed a numeric material value.");
            report.AppendLine("PASS: all current numeric balance values were preserved exactly; no costs, strengths, length limits, mass or rewards were inferred.");
        }
        catch (Exception error)
        {
            report.Insert(0, "FAILED\n");
            report.AppendLine(error.ToString());
            Debug.LogException(error);
        }
        finally
        {
            try
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                DescribeSceneObjectChanges(sceneObjectsBefore, report);
                if (sources != null) RequireHashes(sources, "Supplied prefab/FBX/sprite source changed.");
                if (contracts != null)
                {
                    RequireSamePaths(contracts, ContractFiles(), "Contract asset set changed.");
                    RequireHashes(contracts, "An existing contract asset changed.");
                }
                RequireSceneState(originalScenes, dirtyBefore, activeBefore);
                report.AppendLine("PASS: supplied sources and every existing ContractSO asset/meta hash unchanged; original loaded/active scenes and dirty flags unchanged.");
                report.AppendLine("No user scene was opened/saved, no shared UI or contract was edited, no player data or unlock/reward callbacks were accessed.");
            }
            catch (Exception safetyError)
            {
                report.Insert(0, "FAILED SAFETY CHECK\n");
                report.AppendLine(safetyError.ToString());
                Debug.LogException(safetyError);
            }
            Write(report.ToString());
            running = false;
        }
    }

    private static void ApplyAssets(BridgeMaterialSO material, GameObject bottom, GameObject top,
        Bounds bottomBounds, Bounds topBounds, Scene preview, List<Entry> sources, List<Entry> contracts,
        List<Entry> numbers, StringBuilder report)
    {
        bool shaftExists = File.Exists(ShaftPath) || File.Exists(ShaftPath + ".meta");
        bool capExists = File.Exists(CapPath) || File.Exists(CapPath + ".meta");
        if (shaftExists || capExists)
        {
            Require(shaftExists && capExists, "Only one proposed wrapper path exists. No existing file was overwritten; inspect the partial setup and backup report.");
            ValidateConfigured(material, bottom, top, bottomBounds, topBounds, preview, report, true);
            report.AppendLine("ALREADY_CONFIGURED: read-only reapply; existing wrappers/material were not rewritten.");
            return;
        }
        Require(!File.Exists(BaselinePath), "A prior Concrete Pier setup baseline exists without both wrappers. Review its recoverable backup before proceeding; it was not overwritten.");
        Require(material.segmentPrefab == null && material.pierCapPrefab == null,
            "Concrete Pier already has custom model references. No user references were overwritten.");
        Require(material.materialIcon == null || material.materialIcon == Load<Sprite>(DefaultSprite),
            "Concrete Pier already has a different icon. Review it before authoring; no user icon was overwritten.");
        var serialized = new SerializedObject(material);
        Require(serialized.FindProperty("requiresExplicitContractAllowance") != null,
            "Import the explicit-contract-allowance field before configuring Concrete Pier.");
        string backup = Path.Combine(Path.GetTempPath(), "CivilCraft-ConcretePierAssets-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.Copy(MaterialPath, Path.Combine(backup, "Concrete Pier.asset"));
        File.Copy(MaterialPath + ".meta", Path.Combine(backup, "Concrete Pier.asset.meta"));
        var baseline = new Baseline { materialGuid = MaterialGuid, backupDirectory = backup, status = "PREPARING",
            sources = sources, contracts = contracts, numbers = numbers, wrappers = new List<Entry>() };
        WriteBaseline(baseline);
        report.AppendLine("Recoverable original material and meta: " + backup);
        try
        {
            GameObject shaft = CreateWrapper("ConcretePier_Shaft", ShaftPath, bottom, true, preview);
            GameObject cap = CreateWrapper("ConcretePier_Cap", CapPath, top, false, preview);
            ValidateWrapper(shaft, bottom, bottomBounds, true, preview);
            ValidateWrapper(cap, top, topBounds, false, preview);
            Undo.RecordObject(material, "Configure Concrete Pier asset only");
            serialized.Update();
            SerializedProperty id = serialized.FindProperty("materialId");
            if (string.IsNullOrWhiteSpace(id.stringValue)) id.stringValue = "Concrete Pier";
            SerializedProperty title = serialized.FindProperty("displayName");
            if (string.IsNullOrWhiteSpace(title.stringValue)) title.stringValue = "Concrete Pier";
            serialized.FindProperty("isPier").boolValue = true;
            serialized.FindProperty("isRoad").boolValue = false;
            serialized.FindProperty("isRope").boolValue = false;
            serialized.FindProperty("isDualBeam").boolValue = false;
            serialized.FindProperty("requiresExplicitContractAllowance").boolValue = true;
            serialized.FindProperty("materialIcon").objectReferenceValue = Load<Sprite>(DefaultSprite);
            serialized.FindProperty("segmentPrefab").objectReferenceValue = shaft;
            serialized.FindProperty("pierCapPrefab").objectReferenceValue = cap;
            serialized.ApplyModifiedProperties();
            RequireSameNumbers(numbers, NumericValues(material), "Unexpected numeric balance change; do not save this asset.");
            // Save only this intended asset, never all dirty assets or scenes.
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            baseline.wrappers = HashFiles(new[] { ShaftPath, ShaftPath + ".meta", CapPath, CapPath + ".meta" });
            baseline.status = "COMPLETE";
            WriteBaseline(baseline);
            ValidateConfigured(material, bottom, top, bottomBounds, topBounds, preview, report, true);
            report.AppendLine("APPLIED: configured only Concrete Pier and created two new wrapper prefabs. No source models were edited.");
        }
        catch
        {
            report.AppendLine("Setup stopped. Existing files were not deleted or overwritten. Partial NEW wrappers may remain; inspect the baseline and original asset backup before retrying.");
            throw;
        }
    }

    private static GameObject CreateWrapper(string name, string path, GameObject source, bool center, Scene preview)
    {
        Require((path == ShaftPath || path == CapPath) && !File.Exists(path) && !File.Exists(path + ".meta"),
            "New wrapper target already exists or is outside the exact approved paths.");
        GameObject root = NewPreviewRoot(name, preview);
        try
        {
            GameObject child = InstantiateSource(source, root.transform);
            if (center)
            {
                Renderer renderer = child.GetComponentInChildren<Renderer>();
                Vector3 localCenter = root.transform.InverseTransformPoint(renderer.bounds.center);
                child.transform.localPosition -= localCenter;
            }
            root.hideFlags = HideFlags.None;
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Require(success && saved != null && AssetDatabase.GetAssetPath(saved) == path,
                "Could not save the new wrapper at its exact approved path: " + path);
            return saved;
        }
        finally { if (root != null) Object.DestroyImmediate(root); }
    }

    private static void ValidateConfigured(BridgeMaterialSO material, GameObject bottom, GameObject top,
        Bounds bottomBounds, Bounds topBounds, Scene preview, StringBuilder report, bool requireBaseline)
    {
        Require(material.name == "Concrete Pier" && AssetDatabase.AssetPathToGUID(MaterialPath) == MaterialGuid,
            "Concrete Pier asset name/GUID must be preserved.");
        Require(material.isPier && !material.isRoad && !material.isRope && !material.isDualBeam,
            "Concrete Pier must use only the existing single-pier behavior.");
        SerializedProperty explicitAllowance = new SerializedObject(material).FindProperty("requiresExplicitContractAllowance");
        Require(explicitAllowance != null && explicitAllowance.boolValue, "Concrete Pier must remain gated by an explicit contract allowance.");
        Require(material.materialIcon == Load<Sprite>(DefaultSprite), "Concrete Pier must use the provided unselected icon.");
        Require(!string.IsNullOrWhiteSpace(material.Id) && !string.IsNullOrWhiteSpace(material.displayName),
            "Concrete Pier needs nonempty stable identity and display text.");
        Require(material.segmentPrefab != null && AssetDatabase.GetAssetPath(material.segmentPrefab) == ShaftPath &&
            material.pierCapPrefab != null && AssetDatabase.GetAssetPath(material.pierCapPrefab) == CapPath,
            "Concrete Pier must reference the exact two new wrapper assets.");
        ValidateWrapper(material.segmentPrefab, bottom, bottomBounds, true, preview);
        ValidateWrapper(material.pierCapPrefab, top, topBounds, false, preview);
        if (requireBaseline)
        {
            Require(File.Exists(BaselinePath), "No setup baseline found. Existing wrapper paths were not overwritten; review the unknown files manually.");
            Baseline baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(BaselinePath));
            Require(baseline != null && baseline.version == 1 && baseline.materialGuid == MaterialGuid && baseline.status == "COMPLETE",
                "The setup baseline is incomplete or belongs to a different asset. Inspect its backup before retrying.");
            RequireHashes(baseline.sources, "An original source changed since setup.");
            RequireSamePaths(baseline.contracts, ContractFiles(), "Contract file set changed since setup; inspect the separate story changes before revalidating this baseline.");
            RequireContractBaseline(baseline.contracts);
            RequireHashes(baseline.wrappers, "A generated wrapper differs from its setup baseline; no custom file was overwritten.");
            bool numbersUnchanged = SameValues(baseline.numbers, NumericValues(material));
            report.AppendLine(numbersUnchanged ? "Numeric settings match their original setup backup exactly." :
                "Numeric settings were edited after setup; current user values are retained, not reset to the backup.");
            report.AppendLine("Original asset backup: " + baseline.backupDirectory);
        }
        report.AppendLine("VALIDATED: shaft mesh centered; cap keeps authored shape/pivot handling; source meshes/materials/scales preserved; no colliders/scripts added.");
        report.AppendLine("Concrete Pier remains opt-in. Only Marsh's explicitly requested replacement allowance may reference it; this tool performs no discovery/unlock action.");
    }

    private static void ValidateSource(GameObject source, long meshId, bool requireOpaque, StringBuilder report)
    {
        Require(source.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
            source.GetComponentsInChildren<Collider>(true).Length == 0,
            "Source wrappers must contain plain meshes only, without scripts or colliders.");
        MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
        Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
        Require(filters.Length == 1 && filters[0].sharedMesh != null && renderers.Length == 1 &&
            filters[0].gameObject == source && renderers[0].gameObject == source,
            "The supplied split prefab hierarchy differs from the audited single-root mesh.");
        Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(filters[0].sharedMesh, out string guid, out long localId) &&
            guid == AssetDatabase.AssetPathToGUID(FbxPath) && localId == meshId,
            "Supplied split prefab points to an unexpected FBX mesh.");
        Vector3 scale = source.transform.localScale;
        Require(Finite(scale) && scale.x > 0f && scale.y > 0f && scale.z > 0f &&
            Quaternion.Angle(source.transform.localRotation, Quaternion.identity) < .01f,
            "Supplied split mesh needs positive finite scale and the audited upright Y orientation.");
        foreach (Material material in renderers[0].sharedMaterials)
            report.AppendLine("Source " + source.name + " material: " + (material != null ? material.name : "<missing>") +
                "; shader=" + (material != null && material.shader != null ? material.shader.name : "<missing>") +
                "; opaque=" + IsOpaque(material) + ".");
        bool opaque = renderers[0].sharedMaterials.Length > 0 && renderers[0].sharedMaterials.All(IsOpaque);
        if (requireOpaque) Require(opaque,
            "Supplied split mesh has missing, unsupported or transparent materials. Inspect the model before setup.");
        else if (!opaque) report.AppendLine("INSPECT WARNING: resolve source material opacity/support before applying; no source material was changed.");
    }

    private static bool IsOpaque(Material material)
    {
        if (material == null || material.shader == null || !material.shader.isSupported) return false;
        string type = material.GetTag("RenderType", false, string.Empty);
        return (type.Length == 0 || type == "Opaque") && material.renderQueue < 2500 &&
            (!material.HasProperty("_Surface") || material.GetFloat("_Surface") < .5f) &&
            (!material.HasProperty("_Mode") || material.GetFloat("_Mode") < .5f);
    }

    private static void ValidateWrapper(GameObject asset, GameObject source, Bounds expected, bool centered, Scene preview)
    {
        string expectedName = centered ? "ConcretePier_Shaft" : "ConcretePier_Cap";
        Require(asset != null && asset.name == expectedName && asset.transform.childCount == 1 &&
            asset.GetComponents<Component>().Length == 1 && asset.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
            asset.GetComponentsInChildren<Collider>(true).Length == 0,
            "Generated wrapper has unexpected custom components, children or colliders; it was not overwritten.");
        Require(asset.transform.localPosition.sqrMagnitude < .000001f &&
            Quaternion.Angle(asset.transform.localRotation, Quaternion.identity) < .001f &&
            (asset.transform.localScale - Vector3.one).sqrMagnitude < .000001f, "Wrapper root must have an identity transform.");
        Transform child = asset.transform.GetChild(0);
        Require(PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == source &&
            (child.localScale - source.transform.localScale).sqrMagnitude < .000001f &&
            Quaternion.Angle(child.localRotation, source.transform.localRotation) < .001f,
            "Wrapper must preserve the supplied nested prefab, rotation and scale.");
        MeshFilter filter = child.GetComponent<MeshFilter>();
        Renderer renderer = child.GetComponent<Renderer>(), sourceRenderer = source.GetComponent<Renderer>();
        Require(filter != null && filter.sharedMesh == source.GetComponent<MeshFilter>().sharedMesh && renderer != null &&
            renderer.sharedMaterials.SequenceEqual(sourceRenderer.sharedMaterials), "Wrapper changed a supplied mesh or material.");
        Bounds actual = SourceBounds(asset, preview);
        Require((actual.size - expected.size).sqrMagnitude < .00001f, "Wrapper changed model dimensions.");
        if (centered) Require(actual.center.sqrMagnitude < .00001f, "Shaft mesh is not centered for existing Y-axis pier stretching.");
        else Require(child.localPosition.sqrMagnitude < .000001f && (actual.center - expected.center).sqrMagnitude < .00001f,
            "Cap must keep the supplied mesh shape and zero child position; existing Bar handles its offsets.");
    }

    private static Bounds SourceBounds(GameObject source, Scene preview)
    {
        GameObject root = NewPreviewRoot("Concrete Pier Read-only Bounds", preview);
        try
        {
            GameObject instance = InstantiateSource(source, root.transform);
            return instance.GetComponentInChildren<Renderer>().bounds;
        }
        finally { if (root != null) Object.DestroyImmediate(root); }
    }

    private static GameObject InstantiateSource(GameObject source, Transform parent)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(source, parent) as GameObject;
        Require(instance != null, "Could not instantiate the plain source prefab in the isolated preview.");
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = source.transform.localRotation;
        instance.transform.localScale = source.transform.localScale;
        return instance;
    }

    private static GameObject NewPreviewRoot(string name, Scene preview)
    {
        GameObject root = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
        SceneManager.MoveGameObjectToScene(root, preview);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;
        return root;
    }

    private static void PreviewModels(BridgeMaterialSO material, GameObject bottom, GameObject top,
        Bounds bottomBounds, Bounds topBounds, Scene scene, StringBuilder report)
    {
        // Plain disposable meshes only. These equations mirror Bar.Initialize /
        // UpdateCreatingBar, but do not execute Bar, Point or gameplay lifecycles.
        string folder = "Temp/ConcretePierPreviews";
        Directory.CreateDirectory(folder);
        GameObject prototypeShaft = null, prototypeCap = null, cameraRoot = null, lightRoot = null;
        RenderTexture target = null;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            prototypeShaft = NewPreviewRoot("Concrete Pier Preview Shaft Prototype", scene);
            prototypeCap = NewPreviewRoot("Concrete Pier Preview Cap Prototype", scene);
            bool configured = material.segmentPrefab != null && material.pierCapPrefab != null &&
                AssetDatabase.GetAssetPath(material.segmentPrefab) == ShaftPath && AssetDatabase.GetAssetPath(material.pierCapPrefab) == CapPath;
            if (configured)
            {
                ValidateWrapper(material.segmentPrefab, bottom, bottomBounds, true, scene);
                ValidateWrapper(material.pierCapPrefab, top, topBounds, false, scene);
                InstantiateSource(material.segmentPrefab, prototypeShaft.transform);
                InstantiateSource(material.pierCapPrefab, prototypeCap.transform);
                report.AppendLine("Previewing the actual configured new shaft/cap wrapper assets; source meshes/materials retained.");
            }
            else
            {
                Require(material.segmentPrefab == null && material.pierCapPrefab == null,
                    "Preview cannot substitute models over custom/incomplete Concrete Pier references.");
                GameObject shaftChild = InstantiateSource(bottom, prototypeShaft.transform);
                shaftChild.transform.localPosition -= shaftChild.GetComponentInChildren<Renderer>().bounds.center;
                InstantiateSource(top, prototypeCap.transform);
                report.AppendLine("Unconfigured asset: previewing proposed disposable wrappers only; no asset was applied or saved.");
            }
            prototypeShaft.SetActive(false); prototypeCap.SetActive(false);
            cameraRoot = NewPreviewRoot("Concrete Pier Detached Preview Camera", scene);
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene; camera.useOcclusionCulling = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(38, 49, 61, 255);
            camera.nearClipPlane = .05f; camera.farClipPlane = 1000f; camera.aspect = 1280f / 720f;
            camera.allowHDR = false; camera.allowMSAA = false;
            lightRoot = NewPreviewRoot("Concrete Pier Detached Preview Light", scene);
            Light light = lightRoot.AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            target = new RenderTexture(1280, 720, 24) { hideFlags = HideFlags.HideAndDontSave };
            Require(target.Create(), "Could not create the detached Concrete Pier preview texture.");
            camera.targetTexture = target;
            int magentaTotal = 0;
            foreach (float height in new[] { 3f, 6f })
            {
                GameObject fixture = NewPreviewRoot("Concrete Pier " + height + "m Geometry Fixture", scene);
                try
                {
                    GameObject shaft = Object.Instantiate(prototypeShaft, fixture.transform, false);
                    GameObject cap = Object.Instantiate(prototypeCap, fixture.transform, false);
                    shaft.SetActive(true); cap.SetActive(true);
                    float pillarHeight = Mathf.Max(.05f, height - topBounds.size.y);
                    shaft.transform.localPosition = Vector3.up * (pillarHeight * .5f);
                    shaft.transform.localScale = new Vector3(1f, pillarHeight / bottomBounds.size.y, 1f);
                    cap.transform.localPosition = Vector3.up * (height - topBounds.max.y);
                    cap.transform.localScale = Vector3.one;
                    Bounds shaftBounds = shaft.GetComponentInChildren<Renderer>().bounds;
                    Bounds capBounds = cap.GetComponentInChildren<Renderer>().bounds;
                    Require(Mathf.Abs(shaftBounds.min.y) < .001f && Mathf.Abs(capBounds.max.y - height) < .001f &&
                        Mathf.Abs(shaftBounds.max.y - capBounds.min.y) < .001f,
                        "Shaft foundation, shaft/cap seam, or cap top is misaligned at " + height + " metres.");
                    Bounds whole = shaftBounds; whole.Encapsulate(capBounds);
                    float radius = whole.extents.magnitude;
                    camera.orthographic = true; camera.orthographicSize = Mathf.Max(whole.size.y * .65f, whole.size.x * .65f / camera.aspect);
                    camera.transform.SetPositionAndRotation(whole.center + Vector3.back * (radius * 4f), Quaternion.identity);
                    string stem = Path.Combine(folder, "concrete-pier-" + ((int)height) + "m");
                    magentaTotal += RenderModelPreview(camera, target, stem + "-front.png", report);
                    camera.orthographic = false; camera.fieldOfView = 35f;
                    Vector3 direction = new Vector3(1f, .3f, -1f).normalized;
                    Vector3 position = whole.center + direction * (radius / Mathf.Tan(17.5f * Mathf.Deg2Rad) * 1.3f);
                    camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(whole.center - position));
                    magentaTotal += RenderModelPreview(camera, target, stem + "-angled.png", report);
                    report.AppendLine("Geometry fixture " + height + "m: shaft bottom=" + shaftBounds.min.y.ToString("F6") +
                        ", seam gap=" + (capBounds.min.y - shaftBounds.max.y).ToString("F6") + ", cap top=" + capBounds.max.y.ToString("F6") + ".");
                }
                finally { Object.DestroyImmediate(fixture); }
            }
            report.AppendLine("Preview uses the supplied plain meshes/materials and the production Bar placement equations, not live simulation or Bar lifecycle callbacks.");
            report.AppendLine("Preview heights are visual fixtures, not changes to the preserved maxLength=" + material.maxLength + " setting.");
            Require(magentaTotal < 32, "Potential unsupported/pink model material detected: " + magentaTotal + " magenta pixels. Inspect the saved preview PNGs; no source material was modified.");
            report.AppendLine("PASS: two heights, foundation/cap seam checks and four detached 1280x720 previews; no substantial pink error-shader pixels detected.");
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (cameraRoot != null) cameraRoot.GetComponent<Camera>().targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (cameraRoot != null) Object.DestroyImmediate(cameraRoot);
            if (lightRoot != null) Object.DestroyImmediate(lightRoot);
            if (prototypeShaft != null) Object.DestroyImmediate(prototypeShaft);
            if (prototypeCap != null) Object.DestroyImmediate(prototypeCap);
        }
    }

    private static int RenderModelPreview(Camera camera, RenderTexture target, string path, StringBuilder report)
    {
        Texture2D pixels = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels = new Texture2D(1280, 720, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0f, 0f, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            int magenta = pixels.GetPixels32().Count(pixel => pixel.r > 200 && pixel.b > 200 && pixel.g < 100);
            report.AppendLine("Rendered " + path + "; possible error-shader pixels=" + magenta + ".");
            return magenta;
        }
        finally { RenderTexture.active = previous; if (pixels != null) Object.DestroyImmediate(pixels); }
    }

    private static Dictionary<Object, string> SnapshotSceneObjects(IEnumerable<Scene> scenes)
    {
        var snapshot = new Dictionary<Object, string>();
        foreach (Scene scene in scenes)
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    if (component != null && (component is MonoBehaviour || component is Camera))
                        snapshot[component] = EditorJsonUtility.ToJson(component);
        return snapshot;
    }

    private static void DescribeSceneObjectChanges(Dictionary<Object, string> before, StringBuilder report)
    {
        int changes = 0;
        foreach (var entry in before)
            if (entry.Key == null || EditorJsonUtility.ToJson(entry.Key) != entry.Value)
            {
                changes++;
                if (changes <= 25) report.AppendLine("SCENE DIAGNOSTIC changed pre-existing camera/script: " +
                    (entry.Key != null ? entry.Key.GetType().Name + " / " + entry.Key.name : "<destroyed>"));
            }
        report.AppendLine("Scene diagnostic: changed pre-existing camera/script snapshots=" + changes + ". No dirty flags were cleared or scene values restored.");
        report.AppendLine("The current-scene diagnostic is not a full native-versus-saved-YAML comparison; it cannot reconstruct changes that predate this command.");
    }

    private static void DescribeMaterial(BridgeMaterialSO material, StringBuilder report, List<Entry> numbers)
    {
        report.AppendLine("Asset: " + MaterialPath + "; GUID: " + MaterialGuid + "; name: " + material.name + "; stable ID: " + material.Id + ".");
        report.AppendLine("Models: " + AssetDatabase.GetAssetPath(material.segmentPrefab) + " / " + AssetDatabase.GetAssetPath(material.pierCapPrefab) + "; icon: " + AssetDatabase.GetAssetPath(material.materialIcon));
        foreach (Entry number in numbers) report.AppendLine("Preserved " + number.path + " = " + number.value);
    }
    private static void DescribeBounds(string name, Bounds bounds, StringBuilder report) =>
        report.AppendLine(name + ": center=" + bounds.center.ToString("F6") + ", size=" + bounds.size.ToString("F6") + ".");

    private static List<Entry> NumericValues(BridgeMaterialSO material)
    {
        var values = new List<Entry>();
        SerializedProperty property = new SerializedObject(material).GetIterator();
        while (property.NextVisible(true))
            if (property.propertyType == SerializedPropertyType.Float || property.propertyType == SerializedPropertyType.Integer)
                values.Add(new Entry { path = property.propertyPath, value = property.propertyType == SerializedPropertyType.Float ?
                    property.doubleValue.ToString("R", CultureInfo.InvariantCulture) : property.longValue.ToString(CultureInfo.InvariantCulture) });
        return values;
    }
    private static bool SameValues(List<Entry> left, List<Entry> right) => left != null && right != null &&
        left.Count == right.Count && left.All(value => right.Any(other => value.path == other.path && value.value == other.value));
    private static void RequireSameNumbers(List<Entry> left, List<Entry> right, string message) => Require(SameValues(left, right), message);

    private static IEnumerable<string> SourceFiles() => SourcePaths.SelectMany(path => new[] { path, path + ".meta" });
    private static string[] ContractFiles() => AssetDatabase.FindAssets("t:ContractSO", new[] { "Assets" })
        .Select(AssetDatabase.GUIDToAssetPath).Distinct(StringComparer.Ordinal)
        .SelectMany(path => new[] { path, path + ".meta" }).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    private static List<Entry> HashFiles(IEnumerable<string> paths) => paths.OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => new Entry { path = path, value = Hash(path) }).ToList();
    private static string Hash(string path)
    {
        Require(File.Exists(path), "Missing source asset/meta: " + path);
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty);
    }
    private static void RequireHashes(List<Entry> expected, string message)
    {
        Require(expected != null && expected.Count > 0, "Hash baseline is missing.");
        foreach (Entry entry in expected) Require(Hash(entry.path) == entry.value, message + " " + entry.path);
    }
    private static void RequireSamePaths(List<Entry> baseline, IEnumerable<string> current, string message) =>
        Require(baseline != null && baseline.Select(entry => entry.path).OrderBy(path => path, StringComparer.Ordinal)
            .SequenceEqual(current.OrderBy(path => path, StringComparer.Ordinal)), message);
    private static void RequireNoContractReferences(List<Entry> contracts)
    {
        foreach (Entry contract in contracts.Where(entry => entry.path.EndsWith(".asset", StringComparison.Ordinal)))
        {
            string text = File.ReadAllText(contract.path);
            if (!text.Contains(MaterialGuid)) continue;
            ContractSO allowed = contract.path == MarshContractPath ? Load<ContractSO>(contract.path) : null;
            Require(allowed != null && allowed.allowedMaterials != null &&
                allowed.allowedMaterials.Count(item => item != null && item.material != null &&
                    AssetDatabase.GetAssetPath(item.material) == MaterialPath) == 1 &&
                (allowed.hiddenMaterials == null || !allowed.hiddenMaterials.Contains(Load<BridgeMaterialSO>(MaterialPath))) &&
                text.IndexOf(MaterialGuid, StringComparison.Ordinal) == text.LastIndexOf(MaterialGuid, StringComparison.Ordinal),
                "Concrete Pier has an unexpected contract reference outside Marsh's requested allowance: " + contract.path);
        }
    }

    private static void RequireContractBaseline(List<Entry> expected)
    {
        Require(expected != null && expected.Count > 0, "Contract baseline is missing.");
        foreach (Entry entry in expected)
        {
            string currentHash = Hash(entry.path);
            if (currentHash == entry.value) continue;
            // Unity's native save also reflows/quotes YAML strings. This exact
            // approved outcome was checked by ConcretePierMarshAuthoring against
            // every protected serialized field before and after saving. Keep the
            // original baseline and authorize no other file/content variation.
            if (entry.path == MarshContractPath &&
                entry.value == "4FBE5D5DC66A9BCC440389E9029FB98592689C091C3AC24A441B7170CD21FA2A" &&
                currentHash == "8549F67E22391A5777F6D97B661513D838D29BEA930444C972B2214E073E801D") continue;
            // Keep the original baseline. Authorize only the later user-requested
            // one-reference Wood Pier -> Concrete Pier swap in Marsh's allowance.
            string normalized = entry.path == MarshContractPath
                ? File.ReadAllText(entry.path).Replace(MaterialGuid, WoodPierGuid) : null;
            string normalizedHash = null;
            if (normalized != null)
                using (SHA256 sha = SHA256.Create())
                    normalizedHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-", string.Empty);
            Require(normalizedHash == entry.value,
                "A contract differs beyond Marsh's authorized pier-reference swap; no contract was overwritten: " + entry.path);
        }
    }
    private static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Require(asset != null, "Missing or unimported asset: " + path);
        return asset;
    }
    private static Scene[] LoadedScenes()
    {
        var result = new List<Scene>();
        for (int index = 0; index < SceneManager.sceneCount; index++) result.Add(SceneManager.GetSceneAt(index));
        return result.ToArray();
    }
    private static void RequireSceneState(Scene[] before, bool[] dirty, int active)
    {
        Scene[] after = LoadedScenes();
        Require(after.Length == before.Length && after.Select(scene => scene.handle).SequenceEqual(before.Select(scene => scene.handle)) &&
            SceneManager.GetActiveScene().handle == active, "Loaded/active user scene setup changed.");
        for (int index = 0; index < before.Length; index++) Require(before[index].isDirty == dirty[index], "User scene dirty state changed: " + before[index].path);
    }
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void WriteBaseline(Baseline baseline) { Directory.CreateDirectory("Logs"); File.WriteAllText(BaselinePath, JsonUtility.ToJson(baseline, true)); }
    private static void Write(string report) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report); }
}
#endif
