#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

[InitializeOnLoad]
internal static class WoodRoadVisualAuthoring
{
    private const string PrefabPath = "Assets/BridgeBuilder/Prefabs/materials/map1/WoodRoad.prefab";
    private const string MaterialPath = "Assets/BridgeBuilder/Data/Resources/Wood Road.asset";
    private const string DeckPath = "Assets/BridgeBuilder/Prefabs/materials/map1/WoodRoadDeck_Aligned.asset";
    private const string Request = "Temp/wood-road-visual.request";
    private const string Report = "Logs/wood-road-visual.txt";
    static WoodRoadVisualAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request); var report = new StringBuilder();
        try { if (command == "apply") Apply(report); else if (command != "validate") throw new InvalidOperationException("Use apply or validate."); Validate(report); }
        catch (Exception error) { report.Insert(0, "FAIL\n"); report.AppendLine(error.ToString()); }
        File.WriteAllText(Report, report.ToString());
    }
    private static void Apply(StringBuilder report)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(asset != null && !EditorUtility.IsDirty(asset), "Save any WoodRoad prefab edits before aligning its visuals.");
        Require(PrefabStageUtility.GetCurrentPrefabStage() == null, "Leave Prefab Mode before aligning the WoodRoad asset.");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var deck = root.GetComponentInChildren<MeshFilter>(true); Require(deck != null && deck.name == "mat_wood", "Keep the authored wood deck as the first renderer.");
            float center = root.transform.InverseTransformPoint(deck.transform.TransformPoint(deck.sharedMesh.bounds.center)).x;
            Require(Mathf.Abs(center) < 1f && root.transform.childCount == 3, "Unexpected WoodRoad shape; inspect before changing it.");
            string backup = "LocalBackups/WoodRoadVisual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); Directory.CreateDirectory(backup);
            File.Copy(PrefabPath, backup + "/WoodRoad.prefab");
            if (Mathf.Abs(center) >= .00001f)
                foreach (Transform child in root.transform) { Vector3 position = child.localPosition; position.x -= center; child.localPosition = position; }
            AlignPlankEnds(root.transform, deck, report);
            Require(PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) != null, "Could not save the centered WoodRoad prefab.");
            report.AppendLine($"FIXED: visual centering correction {-center:F7}m on prefab-local X. Y/Z, scales, materials and relative rail placement preserved. Original FBX untouched. Backup: {backup}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        // Seating reads prefab geometry. Clear only its measurement cache so a
        // disabled domain reload cannot retain the pre-fix road pivot.
        var geometry = typeof(Bar).GetField("seatingGeometry", BindingFlags.NonPublic | BindingFlags.Static);
        if (geometry?.GetValue(null) is IDictionary cache) cache.Clear();
    }
    private static void AlignPlankEnds(Transform root, MeshFilter deck, StringBuilder report)
    {
        Mesh original = AssetDatabase.LoadAllAssetsAtPath("Assets/Elements/mat_wood.fbx").OfType<Mesh>().Single();
        Mesh aligned = new Mesh { name = "WoodRoadDeck_Aligned", indexFormat = original.indexFormat };
        using (var source = MeshUtility.AcquireReadOnlyMeshData(original))
        {
            var output = Mesh.AllocateWritableMeshData(1);
            var writable = output[0];
            writable.SetVertexBufferParams(original.vertexCount, original.GetVertexAttributes());
            for (int stream = 0; stream < original.vertexBufferCount; stream++)
                writable.GetVertexData<byte>(stream).CopyFrom(source[0].GetVertexData<byte>(stream));
            int indexCount = original.indexFormat == IndexFormat.UInt16 ? source[0].GetIndexData<ushort>().Length : source[0].GetIndexData<uint>().Length;
            writable.SetIndexBufferParams(indexCount, original.indexFormat);
            if (original.indexFormat == IndexFormat.UInt16) writable.GetIndexData<ushort>().CopyFrom(source[0].GetIndexData<ushort>());
            else writable.GetIndexData<uint>().CopyFrom(source[0].GetIndexData<uint>());
            writable.subMeshCount = original.subMeshCount;
            for (int i = 0; i < original.subMeshCount; i++) writable.SetSubMesh(i, source[0].GetSubMesh(i), MeshUpdateFlags.DontRecalculateBounds);
            Mesh.ApplyAndDisposeWritableMeshData(output, aligned, MeshUpdateFlags.DontRecalculateBounds);
        }
        Matrix4x4 toRoot = root.worldToLocalMatrix * deck.transform.localToWorldMatrix;
        Matrix4x4 toMesh = toRoot.inverse;
        Vector3[] positions = aligned.vertices;
        Vector3[] local = positions.Select(toRoot.MultiplyPoint3x4).ToArray();
        float min = local.Min(point => point.x), max = local.Max(point => point.x); int changed = 0;
        for (int i = 0; i < local.Length; i++)
        {
            Vector3 point = local[i];
            if (point.x < min + .15f) point.x = min;
            else if (point.x > max - .15f) point.x = max;
            if (Mathf.Abs(point.x - local[i].x) > .000001f) changed++;
            positions[i] = toMesh.MultiplyPoint3x4(point);
        }
        aligned.vertices = positions; aligned.RecalculateBounds();
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(DeckPath);
        if (saved == null) { AssetDatabase.CreateAsset(aligned, DeckPath); saved = aligned; }
        else { Require(!EditorUtility.IsDirty(saved), "Save existing aligned wood mesh edits first."); EditorUtility.CopySerialized(aligned, saved); UnityEngine.Object.DestroyImmediate(aligned); EditorUtility.SetDirty(saved); }
        AssetDatabase.SaveAssetIfDirty(saved); deck.sharedMesh = saved;
        report.AppendLine($"Aligned {changed} end vertices to two deck end planes; all height/depth coordinates, UVs, normals, indices and materials retained.");
    }
    private static void Validate(StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene(); var contract = ScriptableObject.CreateInstance<ContractSO>();
        var material = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(MaterialPath); Require(material != null, "Wood Road material is missing.");
        try
        {
            foreach (float width in new[] { 0f, 16f })
                foreach (float length in new[] { 2f, 5f, 10f })
                    foreach (float angle in new[] { 0f, 20f, -20f })
                        foreach (bool reverse in new[] { false, true })
                        {
                            var fixture = New(preview, "Wood road endpoint fixture");
                            try
                            {
                                var owner = fixture.AddComponent<BuildLocation>(); owner.enabled = false; contract.twoWayRoad = width > 0; contract.twoWayRoadWidth = width; owner.activeContract = contract;
                                var from = Vector3.zero; var to = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0f) * length;
                                var bar = New(preview, "Production wood road", fixture.transform).AddComponent<Bar>(); bar.enabled = false; bar.AssignOwner(owner, true);
                                bar.StartPosition = reverse ? to : from; bar.Initialize(material); bar.UpdateCreatingBar(reverse ? from : to);
                                var deck = bar.GetComponentsInChildren<MeshFilter>(true).Single(mesh => mesh.name == "mat_wood");
                                Bounds bounds = InBarBounds(bar.transform, deck);
                                float first = bounds.min.x + length * .5f, last = bounds.max.x - length * .5f;
                                Require(Mathf.Abs(first) < .0002f && Mathf.Abs(last) < .0002f, $"Deck endpoints do not reach their joints: {length}m slope={angle} reversed={reverse}; errors={first}/{last}");
                                var collider = deck.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = deck.sharedMesh; Physics.SyncTransforms();
                                float maximumStartGap = 0f, maximumEndGap = 0f;
                                foreach (float lane in new[] { -.65f, 0f, .65f })
                                {
                                    float z = lane * bounds.extents.z;
                                    float startGap = Coverage(collider, bar.transform, -length * .5f, 1f, z, length);
                                    float endGap = Coverage(collider, bar.transform, length * .5f, -1f, z, length);
                                    maximumStartGap = Mathf.Max(maximumStartGap, startGap); maximumEndGap = Mathf.Max(maximumEndGap, endGap);
                                }
                                Require(maximumStartGap <= .0061f && maximumEndGap <= .0061f, "A plank end still leaves a visible gap across a driving lane.");
                                report.AppendLine($"PASS {length}m slope={angle} reverse={reverse} width={width}: deck endpoint errors {first:F6}/{last:F6}m; natural plank-edge inset across lanes {maximumStartGap:F4}/{maximumEndGap:F4}m.");
                            }
                            finally { UnityEngine.Object.DestroyImmediate(fixture); }
                        }
            report.AppendLine("PASS: 36 production road configurations reach both joints. No scene anchors, colliders, material cost/strength, player saves or contract assignments changed.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); UnityEngine.Object.DestroyImmediate(contract); }
    }
    private static float Coverage(MeshCollider collider, Transform bar, float edge, float direction, float z, float length)
    {
        for (float distance = .001f; distance <= Mathf.Min(.6f, length * .3f); distance += .005f)
        {
            Vector3 origin = bar.TransformPoint(new Vector3(edge + direction * distance, 2f, z));
            if (collider.Raycast(new Ray(origin, bar.TransformDirection(Vector3.down)), out RaycastHit hit, 4f) && Vector3.Dot(hit.normal, bar.up) > .5f) return distance;
        }
        throw new InvalidOperationException("No real wood plank surface near a road endpoint.");
    }
    private static Bounds InBarBounds(Transform bar, MeshFilter mesh)
    {
        Matrix4x4 matrix = bar.worldToLocalMatrix * mesh.transform.localToWorldMatrix; Bounds source = mesh.sharedMesh.bounds; Bounds result = default;
        for (int i = 0; i < 8; i++)
        {
            Vector3 point = matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? source.min.x : source.max.x, (i & 2) == 0 ? source.min.y : source.max.y, (i & 4) == 0 ? source.min.z : source.max.z));
            if (i == 0) result = new Bounds(point, Vector3.zero); else result.Encapsulate(point);
        }
        return result;
    }
    private static GameObject New(Scene scene, string name, Transform parent = null) { var value = new GameObject(name); SceneManager.MoveGameObjectToScene(value, scene); if (parent != null) value.transform.SetParent(parent, false); return value; }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
#endif
