#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class SpawnedRoadSurfaceValidation
{
    private const string Request = "Temp/spawned-road-surface.request";
    private const string Report = "Logs/spawned-road-surface.txt";
    private const string PlayReport = "Logs/spawned-road-play-check.txt";
    private const string SnapshotKey = "CivilCraft.IndustrialAnchors.ExpectedPlayPose";
    [Serializable] private sealed class Pose { public string path; public Vector3 position; }
    [Serializable] private sealed class Snapshot { public Pose[] anchors; }
    private static double checkAfter;
    static SpawnedRoadSurfaceValidation() { EditorApplication.update += Poll; EditorApplication.playModeStateChanged += PlayState; }
    private static string PathOf(Transform transform) => transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;
    private static void PlayState(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
            if (!scene.IsValid() || !scene.isLoaded) { SessionState.EraseString(SnapshotKey); return; }
            var anchors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BuildLocation>(true))
                .Where(location => location.name == "workbenchClark1" || location.name == "workbenchClark2" || location.name == "workbenchMills1" || location.name == "workbenchMills2" || location.name == "workbenchMills3")
                .SelectMany(location => location.startingAnchors.Concat(location.endingAnchors)).Where(point => point != null && (point.name == "LeftAnchor" || point.name == "RightAnchor")).Distinct()
                .Select(point => new Pose { path = PathOf(point.transform), position = point.transform.position }).ToArray();
            SessionState.SetString(SnapshotKey, JsonUtility.ToJson(new Snapshot { anchors = anchors }));
        }
        else if (state == PlayModeStateChange.EnteredPlayMode) checkAfter = EditorApplication.timeSinceStartup + 1.5;
        else if (state == PlayModeStateChange.ExitingPlayMode) checkAfter = 0;
    }
    private static void CheckPlayPoses()
    {
        if (!Application.isPlaying || checkAfter <= 0 || EditorApplication.timeSinceStartup < checkAfter) return;
        checkAfter = 0; string json = SessionState.GetString(SnapshotKey, ""); if (string.IsNullOrEmpty(json)) return;
        var expected = JsonUtility.FromJson<Snapshot>(json); var report = new StringBuilder("REAL PLAY MODE ANCHOR CHECK\n");
        try
        {
            if (expected.anchors.Length != 10) throw new InvalidOperationException("Expected ten Industrial Zone road anchors before Play Mode.");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("CanyonCrossing is not loaded during this Play Mode check.");
            var actual = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Point>(true)).ToArray();
            foreach (var pose in expected.anchors)
            {
                var point = actual.Single(value => PathOf(value.transform) == pose.path);
                if (Vector3.Distance(point.transform.position, pose.position) > .0005f) throw new InvalidOperationException($"Anchor moved after startup: {pose.path} expected={pose.position:F5} actual={point.transform.position:F5}");
                report.AppendLine($"PASS {point.name}: retained {point.transform.position:F5}");
            }
            report.AppendLine("PASS: all ten road anchors retain their authored fractional coordinates after real Play Mode startup.");
        }
        catch (Exception error) { report.Insert(0, "FAIL\n"); report.AppendLine(error.ToString()); }
        File.WriteAllText(PlayReport, report.ToString());
    }
    private static void Poll()
    {
        CheckPlayPoses();
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        try { Inspect(); } catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); }
    }
    internal static void Inspect()
    {
        Scene source = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        var report = new StringBuilder("ACTUAL SPAWNED ROAD SURFACES play=" + Application.isPlaying + "\n");
        if (source.IsValid() && source.isLoaded)
        {
            foreach (var bar in source.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Bar>(true)).Where(bar => bar.materialData != null && bar.materialData.isRoad && bar.transform.position.x > 2200f))
            {
                report.AppendLine($"LIVE {bar.name} owner={bar.OwnerLocation?.name} material={bar.materialData.name} active={bar.gameObject.activeInHierarchy} origin={bar.transform.position:F5} scale={bar.transform.lossyScale:F5} Start={bar.StartPosition:F5} End={bar.EndPosition:F5} anchorA={bar.startPoint?.transform.position:F5} anchorB={bar.endPoint?.transform.position:F5}");
                foreach (var col in bar.GetComponentsInChildren<Collider>(true)) report.AppendLine($"  COLLIDER {col.name} {col.GetType().Name} enabled={col.enabled} trigger={col.isTrigger} bounds={col.bounds}");
                foreach (var mesh in bar.GetComponentsInChildren<MeshFilter>(true)) report.AppendLine($"  MESH {mesh.name} at={mesh.transform.position:F5} scale={mesh.transform.lossyScale:F5} bounds={mesh.GetComponent<Renderer>()?.bounds}");
            }
        }
        if (!Application.isPlaying)
        {
            Scene preview = EditorSceneManager.NewPreviewScene(); var contract = ScriptableObject.CreateInstance<ContractSO>();
            try
            {
                foreach (var materialName in new[] { "Wood Road", "Concrete Road" })
                    foreach (float width in new[] { 0f, 16f })
                    {
                        var owner = New(preview, "Surface test owner").AddComponent<BuildLocation>(); owner.enabled = false;
                        contract.twoWayRoad = width > 0; contract.twoWayRoadWidth = width; owner.activeContract = contract;
                        var a = New(preview, "Start").AddComponent<Point>(); a.enabled = false; a.transform.position = Vector3.zero;
                        var b = New(preview, "End").AddComponent<Point>(); b.enabled = false; b.transform.position = Vector3.right * 10f;
                        var bar = New(preview, "Actual generated road").AddComponent<Bar>(); bar.enabled = false; bar.AssignOwner(owner, true);
                        bar.startPoint = a; bar.endPoint = b; bar.StartPosition = a.transform.position;
                        bar.Initialize(AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/" + materialName + ".asset"));
                        bar.UpdateCreatingBar(b.transform.position);
                        var meshes = bar.GetComponentsInChildren<MeshFilter>(true); var probes = meshes.Select(mesh => { var collider = mesh.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh.sharedMesh; return collider; }).ToArray();
                        Physics.SyncTransforms();
                        report.AppendLine($"PRODUCTION {materialName} width={width} root={bar.transform.position:F5} meshBounds={string.Join("; ", meshes.Select(mesh => mesh.GetComponent<Renderer>().bounds.ToString()))}");
                        foreach (float x in new[] { .01f, .1f, 5f, 9.9f, 9.99f })
                            foreach (float z in new[] { -6f, -3f, 0f, 3f, 6f })
                            {
                                var ray = new Ray(new Vector3(x, 3f, z), Vector3.down);
                                var hits = probes.Select(collider => collider.Raycast(ray, out RaycastHit hit, 6f) ? (RaycastHit?)hit : null).Where(hit => hit.HasValue).Select(hit => hit.Value).OrderByDescending(hit => hit.point.y).ToArray();
                                report.AppendLine($"  SURFACE x={x:F2} z={z:F2}: " + (hits.Length == 0 ? "none" : $"y={hits[0].point.y:F6} normal={hits[0].normal:F5}"));
                                if (materialName == "Concrete Road" && Mathf.Abs(z) <= (width > 0 ? width * .5f - 1f : 3f) &&
                                    (hits.Length == 0 || Mathf.Abs(hits[0].point.y - .025f) > .0005f)) throw new InvalidOperationException("The spawned Concrete Road driving surface does not match the physical deck at +0.025m.");
                            }
                        UnityEngine.Object.DestroyImmediate(bar.gameObject); UnityEngine.Object.DestroyImmediate(a.gameObject); UnityEngine.Object.DestroyImmediate(b.gameObject); UnityEngine.Object.DestroyImmediate(owner.gameObject);
                    }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); UnityEngine.Object.DestroyImmediate(contract); }
        }
        File.WriteAllText(Report, report.ToString());
    }
    private static GameObject New(Scene scene, string name) { var value = new GameObject(name); SceneManager.MoveGameObjectToScene(value, scene); return value; }
}
#endif
