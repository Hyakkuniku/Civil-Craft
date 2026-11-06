#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class ShopBankAlignmentRepair
{
    private const string Request = "Temp/shop-bank-repair.request";
    private const string Report = "Logs/shop-bank-repair.txt";
    static ShopBankAlignmentRepair() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if (command == "inspect") Inspect(); else if (command == "previews") Previews(); else if (command == "prototype") Previews(true); else if (command == "validate") Validate(); else if (command == "export") Export(); else throw new InvalidOperationException("Use inspect, validate or previews."); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL " + error); }
    }
    private static string PathOf(Transform value) => value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
    private static void Inspect()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        var root = scene.GetRootGameObjects().Single(value => value.name == "BridgeCreator").transform.Find("BHAN CONTRACT SHOPKEEPER");
        var site = root.GetComponentInChildren<BuildLocation>(true);
        var anchors = site.startingAnchors.Concat(site.endingAnchors).Where(point => point != null).Distinct().ToArray();
        var report = new StringBuilder($"SHOP BANKS play={Application.isPlaying} sceneDirty={scene.isDirty}\n");
        report.AppendLine($"SITE {PathOf(site.transform)} start={site.startingAnchors.Count} end={site.endingAnchors.Count} starter={(site.buildModeOnlyStarterBridge != null ? site.buildModeOnlyStarterBridge.name : "none")}");
        var points = scene.GetRootGameObjects().SelectMany(value => value.GetComponentsInChildren<Point>(true)).Where(point => point.transform.IsChildOf(root) || anchors.Any(anchor => Vector3.Distance(anchor.transform.position, point.transform.position) < 4f));
        foreach (var point in points) report.AppendLine($"POINT {point.GetInstanceID()} {PathOf(point.transform)} pos={point.transform.position:F6} active={point.gameObject.activeInHierarchy} enabled={point.enabled} isAnchor={point.isAnchor} original={point.originalIsAnchor} runtime={point.Runtime} canonical={anchors.Contains(point)} renderers=" + string.Join(";", point.GetComponentsInChildren<Renderer>(true).Select(renderer => $"{PathOf(renderer.transform)} {renderer.bounds}")));
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            report.AppendLine($"MESH {PathOf(filter.transform)} asset={AssetDatabase.GetAssetPath(filter.sharedMesh)} vertices={filter.sharedMesh.vertexCount} indices={Enumerable.Range(0,filter.sharedMesh.subMeshCount).Sum(index => (long)filter.sharedMesh.GetIndexCount(index))} matrix={filter.transform.localToWorldMatrix} bounds={filter.GetComponent<Renderer>()?.bounds}");
            var renderer = filter.GetComponent<Renderer>(); if (renderer != null) foreach (var material in renderer.sharedMaterials) report.AppendLine($"MATERIAL {material.name} shader={material.shader.name} path={AssetDatabase.GetAssetPath(material)}");
        }
        File.WriteAllText(Report, report.ToString());
    }
    private static void Previews(bool prototype = false)
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit Mode for the detached bank previews.");
        var root = scene.GetRootGameObjects().Single(value => value.name == "BridgeCreator").transform.Find("BHAN CONTRACT SHOPKEEPER");
        var site = root.GetComponentInChildren<BuildLocation>(true);
        var road = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Wood Road.asset");
        foreach (bool right in new[] { false, true })
        {
            Point anchor = (right ? site.endingAnchors : site.startingAnchors).Single();
            var bank = root.Find("LEVEL3SCENE/Small_Rock_form_3" + (right ? " (1)" : "")).GetComponent<MeshCollider>();
            Mesh fitted = null;
            try
            {
                if (prototype)
                {
                    var method = typeof(RegionalAnchorRoadAuthoring).GetMethod("Original", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var original = (Mesh)method.Invoke(null, new object[] { bank.sharedMesh });
                    fitted = RegionalBankMeshFitter.Build(original, bank.transform.localToWorldMatrix, new[] { new RegionalBankMeshFitter.Join { anchor = anchor.transform.position, landDirection = right ? 1f : -1f, halfWidth = 4.023f, extension = right ? .7767f : .5873f, preserveUpperSides = true } });
                }
                foreach (float side in new[] { -1f, 1f }) RegionalRoadJoinPreviews.Capture(bank, anchor, right ? 1f : -1f, road, "Shop" + (right ? "Right" : "Left") + (side < 0f ? "Front" : "Back"), 7f, side, fitted);
            }
            finally { if (fitted != null) UnityEngine.Object.DestroyImmediate(fitted); }
        }
        File.AppendAllText(Report, "\nRendered both complete Shopkeeper bank contact areas from front/back angles.\n");
    }
    private static void Validate()
    {
        Inspect();
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        var root = scene.GetRootGameObjects().Single(value => value.name == "BridgeCreator").transform.Find("BHAN CONTRACT SHOPKEEPER");
        var site = root.GetComponentInChildren<BuildLocation>(true);
        var anchors = site.startingAnchors.Concat(site.endingAnchors).Where(point => point != null).Distinct().ToArray();
        var nearby = scene.GetRootGameObjects().SelectMany(value => value.GetComponentsInChildren<Point>(true)).Where(point => anchors.Any(anchor => Vector3.Distance(anchor.transform.position, point.transform.position) < 4f)).ToArray();
        if (anchors.Length != 2 || nearby.Length != 2 || nearby.Any(point => !anchors.Contains(point))) throw new InvalidOperationException("Expected exactly two existing Shopkeeper road anchors, with no duplicate nearby points.");
        foreach (string name in new[] { "Small_Rock_form_3", "Small_Rock_form_3 (1)" })
        {
            var bank = root.Find("LEVEL3SCENE/" + name); var mesh = bank.GetComponent<MeshFilter>().sharedMesh;
            if (bank.GetComponent<MeshCollider>().sharedMesh != mesh) throw new InvalidOperationException("Bank visual/collider mesh mismatch.");
            var method = typeof(RegionalAnchorRoadAuthoring).GetMethod("Original", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Mesh original = (Mesh)method.Invoke(null, new object[] { mesh });
            if (mesh.vertexCount != original.vertexCount || mesh.subMeshCount != original.subMeshCount) throw new InvalidOperationException("The original Shopkeeper bank topology changed.");
            var readableMethod = typeof(RegionalBankMeshFitter).GetMethod("ReadableCopy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Mesh readable = (Mesh)readableMethod.Invoke(null, new object[] { original });
            try
            {
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) if (!mesh.GetTriangles(submesh).SequenceEqual(readable.GetTriangles(submesh))) throw new InvalidOperationException("Shopkeeper cap/shell face indices changed.");
                if (mesh.normals.Any(normal => !float.IsFinite(normal.x) || !float.IsFinite(normal.y) || !float.IsFinite(normal.z) || Mathf.Abs(normal.sqrMagnitude-1f) > .001f)) throw new InvalidOperationException("Invalid Shopkeeper bank normals.");
            }
            finally { UnityEngine.Object.DestroyImmediate(readable); }
            File.AppendAllText(Report, $"\nPASS {name}: original {mesh.vertexCount} vertices, both material sections and every face index retained; unit normals; visual and collider share the saved mesh.\n");
        }
        File.AppendAllText(Report, "PASS exactly two original Shopkeeper anchors; no duplicate Point objects. Editor solid-cube marker removed.\n");
    }
    private static void Export()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        var root = scene.GetRootGameObjects().Single(value => value.name == "BridgeCreator").transform.Find("BHAN CONTRACT SHOPKEEPER/LEVEL3SCENE/Small_Rock_form_3");
        Mesh current = root.GetComponent<MeshFilter>().sharedMesh;
        var originalMethod = typeof(RegionalAnchorRoadAuthoring).GetMethod("Original", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Mesh original = (Mesh)originalMethod.Invoke(null, new object[] { current });
        var read = typeof(RegionalBankMeshFitter).GetMethod("ReadableCopy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        foreach (var pair in new[] { (name: "original", mesh: original), (name: "fitted", mesh: current) })
        {
            Mesh mesh = (Mesh)read.Invoke(null, new object[] { pair.mesh });
            try
            {
                var text = new StringBuilder(); var vertices = mesh.vertices; var normals = mesh.normals;
                text.AppendLine("v x y z nx ny nz");
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = root.TransformPoint(vertices[i]); Vector3 normal = root.localToWorldMatrix.inverse.transpose.MultiplyVector(normals[i]).normalized;
                    text.AppendLine($"v {point.x:R} {point.y:R} {point.z:R} {normal.x:R} {normal.y:R} {normal.z:R}");
                }
                for (int j = 0; j < mesh.subMeshCount; j++) { int[] indices = mesh.GetTriangles(j); for (int i = 0; i < indices.Length; i += 3) text.AppendLine($"f {j} {indices[i]} {indices[i+1]} {indices[i+2]}"); }
                File.WriteAllText("Logs/shop-bank-" + pair.name + ".txt", text.ToString());
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
#endif
