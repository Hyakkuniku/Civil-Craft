#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Scene-authored Map 3 scenery. Does not change roads, routes or gameplay.</summary>
[InitializeOnLoad]
internal static class Map3IndustrialDressing
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string AssetRoot = "Assets/Elements/Map3/";
    private const string RootName = "Map 3 - Industrial District Scenery";
    private const string Request = "Temp/map3-industrial-dressing.request";
    private const string Config = "Temp/map3-industrial-dressing.json";
    private const string SavedConfig = "Docs/Map3IndustrialScenery.layout.json";
    private const string Folder = "Logs/Map3Dressing";
    private static bool busy;
    private static ClearArea[] reservedRoads = Array.Empty<ClearArea>();
    [Serializable] private class Model { public string asset; public Vector3 size, center, rotation, scale; public string[] materials; }
    [Serializable] private class ObjectInfo { public string path, asset, type; public Vector3 center, size, position; public string[] materials; }
    [Serializable] private class Survey { public bool sceneDirty; public Model[] models; public ObjectInfo[] objects; public ClearArea[] roads; public GroundGrid[] ground; }
    [Serializable] private class GroundGrid { public string platform; public float x, z, step; public int columns, rows; public float[] heights; }
    [Serializable] private class Layout { public Entry[] entries; public ClearArea[] clear; public View[] views; }
    [Serializable] private class Entry { public string id, district, asset, platform; public float x, z, yaw, longest; public bool collider; }
    [Serializable] private class ClearArea { public string name; public float minX, maxX, minZ, maxZ; }
    [Serializable] private class View { public string name; public float x, y, z, size = 120, yaw = 0, pitch = 55; public int width = 1600, height = 1000; }
    private static readonly string[] Platforms = { "Cliff_5 (5)", "Cliff_4 (3)", "Cliff_5 (4)", "Cliff_4 (4)", "Cliff_4 (5)", "Cliff_4 (6)" };
    static Map3IndustrialDressing() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (busy || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request); Run(command);
    }
    [MenuItem("Tools/Civil Craft/Map 3/Industrial Scenery/1. Inspect")]
    private static void InspectMenu() => Run("inspect");
    [MenuItem("Tools/Civil Craft/Map 3/Industrial Scenery/2. Apply Layout (Keep Unsaved Edits)")]
    private static void ApplyMenu() => Run("apply");
    [MenuItem("Tools/Civil Craft/Map 3/Industrial Scenery/3. Validate and Preview")]
    private static void ValidateMenu() => Run("validate");
    private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    private static T[] All<T>(Scene s) where T : Component => s.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<T>(true)).ToArray();
    private static bool InRegion(Bounds b) => b.max.x >= 2070 && b.min.x <= 3480 && b.max.z >= -180 && b.min.z <= 210 && b.min.y < 150;
    private static bool Own(Transform t) { for (; t != null; t = t.parent) if (t.name == RootName || t.name == RootName + " (pending)") return true; return false; }
    private static void Require(bool test, string message) { if (!test) throw new InvalidOperationException(message); }
    private static void Run(string command)
    {
        busy = true; Directory.CreateDirectory(Folder);
        try
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            Require(scene.IsValid() && scene.isLoaded && !EditorApplication.isPlayingOrWillChangePlaymode, "Open CanyonCrossing in Edit Mode.");
            if (command == "inspect") Inspect(scene);
            else
            {
                var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(File.Exists(Config) ? Config : SavedConfig));
                Require(layout != null && layout.entries != null && layout.entries.Length > 0, "A measured layout is required.");
                reservedRoads = RoadAreas(scene).ToArray();
                if (command == "apply") Apply(scene, layout);
                else if (command == "validate") Validate(scene, layout);
                else if (command == "export")
                {
                    Validate(scene, layout);
                    GameObject root = scene.GetRootGameObjects().Single(o => o.name == RootName);
                    RecordModelOverrides(root);
                    string prefab = AssetRoot + "Prefab/IndustrialDistrictScenery.prefab";
                    PrefabUtility.SaveAsPrefabAsset(root, prefab, out bool saved);
                    Require(saved, "Could not save the authored scenery prefab.");
                    GameObject reopened = PrefabUtility.LoadPrefabContents(prefab);
                    try { ValidateRoot(scene, reopened, layout, "prefab-validation.txt"); }
                    finally { PrefabUtility.UnloadPrefabContents(reopened); }
                }
                else if (command != "preview") throw new InvalidOperationException("Use inspect, apply, validate or preview.");
                Preview(scene, layout.views);
            }
            File.WriteAllText(Folder + "/status.txt", "PASS: " + command + "; " + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception e) { File.WriteAllText(Folder + "/status.txt", "FAIL: " + e); Debug.LogException(e); }
        finally { busy = false; }
    }
    private static bool BoundsOf(GameObject go, out Bounds b)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        b = default; if (renderers.Length == 0) return false;
        b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds); return true;
    }
    private static void Inspect(Scene scene)
    {
        foreach (var roads in All<CanyonDirtPaths>(scene).Where(p => InRegion(p.SurfaceBounds))) roads.RebuildGeneratedSurface();
        var objects = new List<ObjectInfo>();
        foreach (var r in All<MeshRenderer>(scene).Where(r => InRegion(r.bounds)))
            objects.Add(new ObjectInfo { path = PathOf(r.transform), type = "renderer", asset = AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>()?.sharedMesh), center = r.bounds.center, size = r.bounds.size,
                position = r.transform.position, materials = r.sharedMaterials.Select(m => m == null ? "MISSING" : m.name).ToArray() });
        foreach (var c in All<Collider>(scene).Where(c => InRegion(c.bounds) && c.enabled))
            objects.Add(new ObjectInfo { path = PathOf(c.transform), type = c.GetType().Name, center = c.bounds.center, size = c.bounds.size, position = c.transform.position });
        foreach (var root in scene.GetRootGameObjects().Where(o => Platforms.Contains(o.name)))
            objects.Add(new ObjectInfo { path = root.name, type = "platform", position = root.transform.position, center = root.transform.position, size = root.transform.lossyScale });
        var models = new List<Model>(); var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (string asset in AssetDatabase.FindAssets("t:Model", new[] { AssetRoot.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) && !p.Contains("/Materials/") && !p.Contains("Map3_Env")))
            {
                GameObject go = null;
                try
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(asset), preview);
                    go.transform.position = Vector3.zero;
                    if (BoundsOf(go, out Bounds b)) models.Add(new Model { asset = asset, center = b.center, size = b.size,
                        rotation = go.transform.eulerAngles, scale = go.transform.localScale,
                        materials = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Select(m => m == null ? "MISSING" : m.name).Distinct().ToArray() });
                }
                finally { if (go != null) UnityEngine.Object.DestroyImmediate(go); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        var ground = new List<GroundGrid>(); Physics.SyncTransforms();
        foreach (string name in Platforms)
        {
            var platform = Platform(scene, name); BoundsOf(platform, out Bounds b);
            var grid = new GroundGrid { platform = name, x = Mathf.Floor(b.min.x), z = Mathf.Floor(b.min.z), step = 2,
                columns = Mathf.CeilToInt(b.size.x / 2) + 2, rows = Mathf.CeilToInt(b.size.z / 2) + 2 };
            grid.heights = new float[grid.columns * grid.rows];
            for (int row = 0; row < grid.rows; row++) for (int column = 0; column < grid.columns; column++)
                grid.heights[row * grid.columns + column] = TrySurface(platform, grid.x + column * grid.step, grid.z + row * grid.step, out float y) ? y : -999;
            ground.Add(grid);
        }
        File.WriteAllText(Folder + "/survey.json", JsonUtility.ToJson(new Survey { sceneDirty = scene.isDirty, objects = objects.ToArray(), models = models.ToArray(), roads = RoadAreas(scene).ToArray(), ground = ground.ToArray() }, true));
    }
    private static GameObject Platform(Scene s, string name)
    {
        Require(Platforms.Contains(name), "Use an audited raised platform: " + name);
        var found = s.GetRootGameObjects().Where(o => o.name == name).ToArray();
        Require(found.Length == 1, "Expected one platform " + name); return found[0];
    }
    private static float Surface(GameObject platform, float x, float z)
    {
        Require(TrySurface(platform, x, z, out float best), "No flat platform top under " + x + ", " + z + " on " + platform.name);
        return best;
    }
    private static bool TrySurface(GameObject platform, float x, float z, out float best)
    {
        best = float.NegativeInfinity; BoundsOf(platform, out Bounds b);
        foreach (var c in platform.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger))
            if (c.Raycast(new Ray(new Vector3(x, 180, z), Vector3.down), out RaycastHit hit, 300) && hit.normal.y > .98f && Mathf.Abs(hit.point.y - b.max.y) < .2f) best = Mathf.Max(best, hit.point.y);
        return !float.IsNegativeInfinity(best);
    }
    private static bool OverlapXZ(Bounds a, Bounds b, float gap = 0)
        => a.min.x < b.max.x + gap && a.max.x > b.min.x - gap && a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;
    private static IEnumerable<ClearArea> RoadAreas(Scene scene)
    {
        // Use the same world bounds and width formula as the existing road shader.
        // Reserve asphalt, sidewalks and 2 m of walking/setback space around them.
        foreach (var path in All<CanyonDirtPaths>(scene).Where(p => p.isActiveAndEnabled && InRegion(p.SurfaceBounds)))
        {
            Bounds b = path.SurfaceBounds; float size = Mathf.Min(b.size.x, b.size.z);
            foreach (var street in path.streets ?? Array.Empty<CanyonDirtPaths.Street>())
            {
                Vector2 a = new Vector2(b.min.x + street.from.x * b.size.x, b.min.z + street.from.y * b.size.z);
                Vector2 z = new Vector2(b.min.x + street.to.x * b.size.x, b.min.z + street.to.y * b.size.z);
                float radius = Mathf.Clamp(path.width, .01f, .2f) * size * .5f * (street.widthMultiplier > 0 ? Mathf.Clamp(street.widthMultiplier, .1f, 2) : 1)
                    * (1 + Mathf.Clamp(path.sidewalkWidth, 0, .6f)) + 2;
                yield return new ClearArea { name = PathOf(path.transform) + " road/sidewalk", minX = Mathf.Min(a.x, z.x) - radius, maxX = Mathf.Max(a.x, z.x) + radius,
                    minZ = Mathf.Min(a.y, z.y) - radius, maxZ = Mathf.Max(a.y, z.y) + radius };
            }
        }
    }
    private static void Clear(Scene scene, Bounds b, Entry entry, Layout layout, IEnumerable<Bounds> occupied)
    {
        foreach (var c in (layout.clear ?? Array.Empty<ClearArea>()).Concat(reservedRoads))
            Require(!(b.min.x < c.maxX && b.max.x > c.minX && b.min.z < c.maxZ && b.max.z > c.minZ), entry.id + " enters protected area " + c.name);
        foreach (var other in occupied) Require(!OverlapXZ(b, other, .6f), entry.id + " overlaps another scenery footprint.");
    }
    private static float FitSurface(GameObject platform, Bounds b, string id)
    {
        var heights = new List<float>();
        foreach (float x in new[] { b.min.x - .35f, b.center.x, b.max.x + .35f })
            foreach (float z in new[] { b.min.z - .35f, b.center.z, b.max.z + .35f }) heights.Add(Surface(platform, x, z));
        Require(heights.Max() - heights.Min() < .12f, id + " crosses a road curb or uneven platform edge.");
        return heights.Max();
    }
    private static void Apply(Scene scene, Layout layout)
    {
        Require(layout.entries.Select(e => e.id).Distinct().Count() == layout.entries.Length, "Duplicate scenery IDs.");
        Physics.SyncTransforms();
        var originals = All<Transform>(scene).Where(t => !Own(t)).ToDictionary(t => t, t => t.localToWorldMatrix);
        var streetSettings = All<CanyonDirtPaths>(scene).ToDictionary(p => p, p => EditorJsonUtility.ToJson(p));
        var beforeRoots = scene.GetRootGameObjects(); var old = beforeRoots.SingleOrDefault(o => o.name == RootName);
        var created = new GameObject(RootName + " (pending)"); SceneManager.MoveGameObjectToScene(created, scene);
        int undo = Undo.GetCurrentGroup(); Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Dress Industrial Zone platforms");
        Undo.RegisterCreatedObjectUndo(created, "Add Industrial Zone scenery");
        var occupied = new List<Bounds>(); var report = new StringBuilder();
        try
        {
            foreach (var e in layout.entries)
            {
                Require(e.asset != null && e.asset.StartsWith(AssetRoot, StringComparison.Ordinal) && e.asset.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase), "Use a supplied Map 3 FBX: " + e.id);
                Require(e.longest > .1f && e.longest <= 85 && !float.IsNaN(e.longest), "Invalid proportional size: " + e.id);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(e.asset); Require(model != null, "Missing model " + e.asset);
                Transform district = created.transform.Find(e.district);
                if (district == null) { var group = new GameObject(e.district); group.transform.SetParent(created.transform, false); district = group.transform; }
                var wrapper = new GameObject(e.id); wrapper.transform.SetParent(district, false);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene); go.transform.SetParent(wrapper.transform, false);
                go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.AngleAxis(e.yaw, Vector3.up) * model.transform.rotation;
                Require(BoundsOf(go, out Bounds b), "No renderable geometry: " + e.id);
                float scale = e.longest / Mathf.Max(b.size.x, b.size.z);
                Require(scale > 0 && !float.IsInfinity(scale), "Invalid source dimensions: " + e.id);
                go.transform.localScale *= scale; BoundsOf(go, out b);
                go.transform.position += new Vector3(e.x - b.center.x, -b.min.y, e.z - b.center.z); BoundsOf(go, out b);
                Clear(scene, b, e, layout, occupied);
                float y = FitSurface(Platform(scene, e.platform), b, e.id);
                go.transform.position += Vector3.up * (y + .008f); BoundsOf(go, out b);
                Vector3 modelPosition = go.transform.position;
                wrapper.transform.position = new Vector3(e.x, y, e.z);
                go.transform.position = modelPosition;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    Require(r.sharedMaterials.All(m => m != null), "Missing material on " + e.id);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
                }
                // Imported art remains connected to its model; collision is a simple
                // authored footprint instead of adding many heavy mesh colliders.
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                if (e.collider)
                {
                    var collider = wrapper.AddComponent<BoxCollider>(); collider.center = wrapper.transform.InverseTransformPoint(b.center);
                    collider.size = b.size;
                }
                occupied.Add(b);
                report.AppendLine(e.id + " | " + e.asset + " | bounds=" + b + " | uniform multiplier=" + scale.ToString("F5"));
            }
            foreach (var pair in originals) Require(pair.Key != null && pair.Key.localToWorldMatrix == pair.Value, "An existing scene transform changed.");
            foreach (var pair in streetSettings) Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "Existing road settings changed.");
            RecordModelOverrides(created);
            if (old != null) Undo.DestroyObjectImmediate(old);
            created.name = RootName; Undo.CollapseUndoOperations(undo); EditorSceneManager.MarkSceneDirty(scene);
            report.AppendLine("PASS: " + layout.entries.Length + " proportional model placements. Original scene transforms, roads, platforms, routes and story targets unchanged. Scene left unsaved.");
            File.WriteAllText(Folder + "/placements.txt", report.ToString());
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }
    private static void Validate(Scene scene, Layout layout)
    {
        Physics.SyncTransforms(); var root = scene.GetRootGameObjects().Single(o => o.name == RootName);
        ValidateRoot(scene, root, layout, "validation.txt");
    }
    private static void RecordModelOverrides(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (PrefabUtility.IsPartOfPrefabInstance(t))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
            }
    }
    private static void ValidateRoot(Scene scene, GameObject root, Layout layout, string file)
    {
        var occupied = new List<Bounds>(); var report = new StringBuilder();
        var probeScene = EditorSceneManager.NewPreviewScene();
        var probe = new GameObject("Scenery validation volume", typeof(BoxCollider)) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(probe, probeScene); var box = probe.GetComponent<BoxCollider>(); box.enabled = false;
        var existing = All<Collider>(scene).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy && !Own(c.transform)).ToArray();
        try
        {
        foreach (var e in layout.entries)
        {
            var target = root.transform.Find(e.district + "/" + e.id); Require(target != null && target.childCount == 1, "Missing scenery " + e.id);
            var model = target.GetChild(0); Require(BoundsOf(model.gameObject, out Bounds b), "Missing mesh " + e.id);
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(e.asset).transform.localScale;
            Vector3 ratio = new Vector3(model.localScale.x / original.x, model.localScale.y / original.y, model.localScale.z / original.z);
            Require(Mathf.Max(ratio.x, ratio.y, ratio.z) - Mathf.Min(ratio.x, ratio.y, ratio.z) < .0001f, "Non-proportional scale " + e.id);
            Require(Mathf.Abs(b.center.x - e.x) < .01f && Mathf.Abs(b.center.z - e.z) < .01f, "Moved scenery " + e.id);
            Clear(scene, b, e, layout, occupied);
            float surface = FitSurface(Platform(scene, e.platform), b, e.id);
            Require(Mathf.Abs(b.min.y - surface - .008f) < .02f, "Floating/sunken scenery " + e.id);
            var support = new HashSet<Collider>(Platform(scene, e.platform).GetComponentsInChildren<Collider>(true));
            box.size = Vector3.Max(b.size - Vector3.one * .04f, Vector3.one * .01f);
            foreach (var c in existing.Where(c => !support.Contains(c) && c.bounds.Intersects(b)))
                Require(!Physics.ComputePenetration(box, b.center, Quaternion.identity, c, c.transform.position, c.transform.rotation, out _, out float depth) || depth < .025f,
                    e.id + " intersects existing scene geometry " + PathOf(c.transform));
            occupied.Add(b);
        }
        report.AppendLine("PASS: " + occupied.Count + " models, proportional scale, supported footprints, .008 m ground seating, road/NPC/vehicle clearances, existing solid-geometry clearance and non-overlapping scenery.");
        File.WriteAllText(Folder + "/" + file, report.ToString());
        }
        finally { EditorSceneManager.ClosePreviewScene(probeScene); }
    }
    private static void Preview(Scene scene, View[] views)
    {
        if (views == null) return;
        foreach (var roads in All<CanyonDirtPaths>(scene).Where(p => InRegion(p.SurfaceBounds))) roads.RebuildGeneratedSurface();
        foreach (var view in views)
        {
            var preview = new PreviewRenderUtility(); Texture2D pixels = null;
            Vector3 focus = new Vector3(view.x, view.y, view.z);
            try
            {
                foreach (var r in All<MeshRenderer>(scene).Where(r => r.enabled && r.gameObject.activeInHierarchy && InRegion(r.bounds)))
                {
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    if (Mathf.Abs(r.bounds.center.x - focus.x) > view.size * view.width / view.height + r.bounds.extents.x + 30) continue;
                    var copy = EditorUtility.CreateGameObjectWithHideFlags(r.name, HideFlags.HideAndDontSave, typeof(MeshFilter), typeof(MeshRenderer));
                    // Road overlays project their authored stripes in world space.
                    preview.AddSingleGO(copy); copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation); copy.transform.localScale = r.transform.lossyScale;
                    copy.GetComponent<MeshFilter>().sharedMesh = mf.sharedMesh; copy.GetComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                }
                preview.lights[0].intensity = 1.2f; preview.lights[0].transform.rotation = Quaternion.Euler(52, -38, 0);
                preview.lights[1].intensity = .35f; preview.lights[1].transform.rotation = Quaternion.Euler(30, 135, 0);
                preview.ambientColor = new Color(.47f, .48f, .5f);
                Camera camera = preview.camera; camera.orthographic = true; camera.orthographicSize = view.size;
                camera.nearClipPlane = .1f; camera.farClipPlane = 6000; camera.useOcclusionCulling = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.71f, .72f, .69f);
                Quaternion rotation = Quaternion.Euler(view.pitch, view.yaw, 0);
                camera.transform.SetPositionAndRotation(focus + rotation * new Vector3(0, 0, -1800), rotation);
                preview.BeginStaticPreview(new Rect(0, 0, view.width, view.height)); preview.Render(true, false); pixels = preview.EndStaticPreview();
                File.WriteAllBytes(Folder + "/" + view.name + ".png", pixels.EncodeToPNG());
            }
            finally { if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels); preview.Cleanup(); }
        }
    }
}
#endif
