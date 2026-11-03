using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

internal static class Map3SandSurface
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string DataPath = "Assets/New Terrain 3.asset";
    private const string SandPath = "Assets/Elements/Canyon Crossing - MAP 1/Material.002.mat";
    private const string SurfacePath = "Assets/Elements/Map3/Map 3 Sand Surface.asset";
    private const string SurfaceName = "Map 3 Low Poly Sand";
    private const string Request = "Temp/apply-map3-sand.request";
    private const string Report = "Logs/map3-sand-surface.txt";

    [InitializeOnLoadMethod]
    private static void WatchRequest() => EditorApplication.update += CheckRequest;

    private static void CheckRequest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { ApplySurface(command == "refine"); }
        catch (Exception error) { File.WriteAllText(Report, error.ToString()); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Map 3/Apply Low Poly Sand")]
    private static void Apply() => ApplySurface(false);

    private static void ApplySurface(bool refine)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath);
        Material sand = AssetDatabase.LoadAssetAtPath<Material>(SandPath);
        if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.isLoaded || data == null || sand == null)
            throw new InvalidOperationException("Open CanyonCrossing in Edit Mode with the Map 3 terrain and SAND5 material available.");

        Terrain[] matches = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Terrain>(true))
            .Where(terrain => terrain.terrainData == data).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Expected exactly one Terrain using New Terrain 3 in CanyonCrossing.");
        Terrain terrain = matches[0];
        TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
        if (collider == null || !collider.enabled || collider.terrainData != data)
            throw new InvalidOperationException("Map 3 needs its original TerrainCollider.");
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SurfacePath);
        Material lighter;
        Material darker;
        if (mesh == null)
        {
            mesh = BuildMesh(terrain, collider);
            lighter = new Material(sand) { name = "Sunlit sand", color = Tint(sand.color, 1.04f) };
            darker = new Material(sand) { name = "Warm sand", color = Tint(sand.color, .955f) };
            AssetDatabase.CreateAsset(mesh, SurfacePath);
            AssetDatabase.AddObjectToAsset(lighter, mesh);
            AssetDatabase.AddObjectToAsset(darker, mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
        }
        else
        {
            var tones = AssetDatabase.LoadAllAssetsAtPath(SurfacePath).OfType<Material>().ToArray();
            lighter = tones.Single(material => material.name == "Sunlit sand");
            darker = tones.Single(material => material.name == "Warm sand");
        }
        var materials = new[] { sand, lighter, darker };
        Transform existing = terrain.transform.Find(SurfaceName);
        if (existing != null)
        {
            if (terrain.drawHeightmap || existing.GetComponent<MeshFilter>()?.sharedMesh != mesh ||
                existing.GetComponent<MeshRenderer>() is not MeshRenderer existingRenderer ||
                !existingRenderer.sharedMaterials.SequenceEqual(materials))
                throw new InvalidOperationException("The existing Map 3 sand surface needs review before applying again.");
            if (refine)
            {
                Mesh replacement = BuildMesh(terrain, collider);
                Undo.RegisterCompleteObjectUndo(mesh, "Fit Map 3 sand to the terrain collider");
                EditorUtility.CopySerialized(replacement, mesh);
                UnityEngine.Object.DestroyImmediate(replacement);
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }
            RenderPreview(mesh, materials);
            File.WriteAllText(Report, $"PASS: verified Map 3 sand mesh, {mesh.triangles.Length / 3} triangles, original TerrainCollider and three sand tones.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply Map 3 low poly sand");
        var surface = new GameObject(SurfaceName, typeof(MeshFilter), typeof(MeshRenderer));
        SceneManager.MoveGameObjectToScene(surface, scene);
        surface.transform.SetParent(terrain.transform, false);
        surface.layer = terrain.gameObject.layer;
        surface.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = surface.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = terrain.shadowCastingMode;
        renderer.receiveShadows = true;
        Undo.RegisterCreatedObjectUndo(surface, "Create Map 3 sand surface");
        Undo.RecordObject(terrain, "Hide original Map 3 terrain surface");
        terrain.drawHeightmap = false;
        Undo.FlushUndoRecordObjects();
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        string result = $"PASS: {SurfaceName}, {mesh.GetIndexCount(0) / 3 + mesh.GetIndexCount(1) / 3 + mesh.GetIndexCount(2) / 3} triangles. " +
            $"Terrain size {data.size}. Sculpted areas and holes use native samples. Original collider and TerrainData retained. Scene left unsaved.";
        File.WriteAllText(Report, result);
        Debug.Log(result, surface);
        try { RenderPreview(mesh, renderer.sharedMaterials); }
        catch (Exception error) { Debug.LogWarning("Map 3 sand preview: " + error.Message); }
    }

    private static void RenderPreview(Mesh mesh, Material[] materials)
    {
        var preview = new PreviewRenderUtility();
        Texture2D pixels = null;
        try
        {
            var surface = EditorUtility.CreateGameObjectWithHideFlags("Sand preview", HideFlags.HideAndDontSave,
                typeof(MeshFilter), typeof(MeshRenderer));
            preview.AddSingleGO(surface);
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            surface.GetComponent<MeshRenderer>().sharedMaterials = materials;
            preview.lights[0].intensity = 1.1f;
            preview.lights[0].transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            preview.lights[1].intensity = .25f;
            preview.lights[1].transform.rotation = Quaternion.Euler(65f, 140f, 0f);
            preview.ambientColor = new Color(.4f, .37f, .34f);
            Camera camera = preview.camera;
            float width = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
            Vector3 look = new Vector3(.4f, -1.65f, 1f).normalized;
            camera.transform.SetPositionAndRotation(mesh.bounds.center - look * width * 1.6f, Quaternion.LookRotation(look));
            camera.orthographic = true;
            camera.orthographicSize = width * .62f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = width * 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.22f, .29f, .35f);
            preview.BeginStaticPreview(new Rect(0, 0, 1200, 900));
            preview.Render(allowScriptableRenderPipeline: true, updatefov: false);
            pixels = preview.EndStaticPreview();
            File.WriteAllBytes("Logs/map3-sand-preview.png", pixels.EncodeToPNG());
        }
        finally
        {
            preview.Cleanup();
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
        }
    }

    private static Color Tint(Color color, float factor) =>
        new Color(Mathf.Clamp01(color.r * factor), Mathf.Clamp01(color.g * factor), Mathf.Clamp01(color.b * factor), color.a);

    private static Mesh BuildMesh(Terrain terrain, TerrainCollider collider)
    {
        TerrainData data = terrain.terrainData;
        int cells = data.heightmapResolution - 1;
        float[,] heights = data.GetHeights(0, 0, cells + 1, cells + 1);
        bool[,] solid = data.GetHoles(0, 0, cells, cells);
        int step = Math.Max(1, cells / 64);
        int patches = cells / step;
        var detailed = new bool[patches, patches];
        for (int pz = 0; pz < patches; pz++)
        for (int px = 0; px < patches; px++)
        {
            int x = px * step, z = pz * step;
            float height = heights[z, x];
            bool hasGround = solid[z, x];
            for (int j = 0; j <= step; j++)
            for (int i = 0; i <= step; i++)
            {
                if (heights[z + j, x + i] != height ||
                    (i < step && j < step && solid[z + j, x + i] != hasGround))
                    detailed[pz, px] = true;
            }
        }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new[] { new List<int>(), new List<int>(), new List<int>() };
        var random = new System.Random(31);
        Vector3 scale = data.heightmapScale;
        Vector3 Point(int x, int z) => new Vector3(x * scale.x, heights[z, x] * scale.y, z * scale.z);
        void AddFace(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            int tone = random.Next(8);
            List<int> indices = triangles[tone < 4 ? 0 : tone < 6 ? 1 : 2];
            for (int i = 0; i < 3; i++)
            {
                indices.Add(vertices.Count);
                normals.Add(normal);
                vertices.Add(i == 0 ? a : i == 1 ? b : c);
            }
        }
        void AddQuad(int x, int z, int stride)
        {
            if (!solid[z, x]) return;
            Vector3 a = Point(x, z), b = Point(x, z + stride);
            Vector3 c = Point(x + stride, z), d = Point(x + stride, z + stride);
            bool acrossAD = ((x / stride + z / stride) & 1) != 0;
            float heightAD = (a.y + d.y) * .5f;
            float heightBC = (b.y + c.y) * .5f;
            if (stride == 1 && Mathf.Abs(heightAD - heightBC) > .025f)
            {
                // Match the collider's diagonal where the two splits would
                // put the visible surface at meaningfully different heights.
                Vector3 center = (a + d) * .5f;
                center.y = data.size.y + 2f;
                Transform frame = terrain.transform;
                Vector3 origin = frame.TransformPoint(center);
                if (!collider.Raycast(new Ray(origin, -frame.up), out RaycastHit hit, data.size.y + 4f))
                    throw new InvalidOperationException("Could not sample the original Map 3 terrain collider.");
                float actual = frame.InverseTransformPoint(hit.point).y;
                acrossAD = Mathf.Abs(actual - heightAD) < Mathf.Abs(actual - heightBC);
                if (Mathf.Abs(actual - (acrossAD ? heightAD : heightBC)) > .08f)
                    throw new InvalidOperationException("The visible terrain could not be aligned with its collider.");
            }
            if (!acrossAD) { AddFace(a, b, c); AddFace(c, b, d); }
            else { AddFace(a, b, d); AddFace(a, d, c); }
        }
        bool IsDetailed(int x, int z) => x >= 0 && z >= 0 && x < patches && z < patches && detailed[z, x];
        for (int pz = 0; pz < patches; pz++)
        for (int px = 0; px < patches; px++)
        {
            int x = px * step, z = pz * step;
            if (detailed[pz, px])
            {
                for (int j = 0; j < step; j++)
                for (int i = 0; i < step; i++) AddQuad(x + i, z + j, 1);
                continue;
            }
            if (!solid[z, x]) continue;
            bool left = IsDetailed(px - 1, pz), top = IsDetailed(px, pz + 1);
            bool right = IsDetailed(px + 1, pz), bottom = IsDetailed(px, pz - 1);
            if (!left && !top && !right && !bottom) { AddQuad(x, z, step); continue; }

            // Share every perimeter sample with a detailed neighbor. The flat
            // patch stays flush with its neighbor without cracks or extra height.
            Vector3 center = (Point(x, z) + Point(x + step, z + step)) * .5f;
            void AddEdge(int sx, int sz, int dx, int dz, bool subdivide)
            {
                int stride = subdivide ? 1 : step;
                for (int i = 0; i < step; i += stride)
                    AddFace(center, Point(sx + dx * i, sz + dz * i),
                        Point(sx + dx * (i + stride), sz + dz * (i + stride)));
            }
            AddEdge(x, z, 0, 1, left);
            AddEdge(x, z + step, 1, 0, top);
            AddEdge(x + step, z + step, 0, -1, right);
            AddEdge(x + step, z, -1, 0, bottom);
        }
        if (vertices.Count == 0) throw new InvalidOperationException("Map 3 has no terrain surface to render.");
        if (vertices.Count / 3 > 131072)
            throw new InvalidOperationException("This terrain needs more detail than the low poly surface budget allows.");
        var mesh = new Mesh { name = SurfaceName, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateBounds();
        return mesh;
    }

}
