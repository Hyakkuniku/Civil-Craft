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

/// <summary>Opt-in mesh seating tests on detached graph fixtures; no live bridge or profile is changed.</summary>
[InitializeOnLoad]
public static class PierRoadSeatingValidation
{
    public const string RequestPath = "Temp/pier-road-bearing-v2.request";
    public const string ReportPath = "Logs/pier-road-seating.txt";
    private static readonly string[] Assets = { "Assets/BridgeBuilder/Data/Resources/Wood Road.asset", "Assets/BridgeBuilder/Data/Resources/Concrete Road.asset", "Assets/BridgeBuilder/Data/Resources/Pier.asset", "Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset" };
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool running;
    private static double nextPoll;
    static PierRoadSeatingValidation() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            BuildPipeline.isBuildingPlayer || EditorApplication.timeSinceStartup < nextPoll || !File.Exists(RequestPath)) return;
        nextPoll = EditorApplication.timeSinceStartup + 1; File.Delete(RequestPath); Run();
    }

    [MenuItem("Tools/Civil Craft/Bridge Visuals/Validate Pier Road Seating (Detached)")]
    public static void Run()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        running = true; var report = new StringBuilder("Pier/deck seating: finite road surfaces, fitted bearings, graph changes and visual-only preservation\n");
        try { PierCapSeatingProfileAuthoring.EnsureProfiles(); }
        catch (Exception error)
        {
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, "FAILED\n" + error); running = false; return;
        }
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] loaded = scenes.Select(scene => scene.isLoaded).ToArray();
        bool[] dirty = scenes.Select(scene => scene.isDirty).ToArray();
        GameObject[][] roots = scenes.Select(scene => scene.isLoaded ? scene.GetRootGameObjects() : Array.Empty<GameObject>()).ToArray();
        Scene active = SceneManager.GetActiveScene(); Object[] selected = Selection.objects; Point[] points = Point.AllPoints.ToArray();
        var assetHashes = AssetDatabase.GetDependencies(Assets, true).Where(File.Exists).Distinct().ToDictionary(path => path, Hash);
        var sceneHashes = scenes.Where(scene => File.Exists(scene.path)).ToDictionary(scene => scene.path, scene => Hash(scene.path));
        PlayerDataManager manager = PlayerDataManager.Instance; PlayerData player = manager != null ? manager.CurrentData : null;
        string playerJson = player != null ? JsonUtility.ToJson(player) : null;
        RenderTexture previous = RenderTexture.active; Scene preview = default; int cases = 0;
        try
        {
            Require(typeof(Bar).GetMethod("RefreshPierCapSeating", Hidden) != null, "The pier seating method is missing.");
            var materials = Assets.Select(path => AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(path)).ToArray();
            Require(materials.All(material => material != null), "A required existing road/pier material is missing.");
            preview = EditorSceneManager.NewPreviewScene();
            foreach (BridgeMaterialSO material in materials)
            {
                GameObject measure = Owned(preview, "Detached source mesh measurement");
                try
                {
                    if (material.segmentPrefab != null)
                    {
                        GameObject segment = Object.Instantiate(material.segmentPrefab, measure.transform);
                        report.AppendLine("SOURCE: " + material.name + " shaft/deck root scale=" + segment.transform.localScale +
                            "; root-local mesh bounds=" + LocalBounds(segment.transform) + "; scaled bounds=" + LocalBounds(measure.transform));
                        Object.DestroyImmediate(segment);
                    }
                    if (material.pierCapPrefab != null)
                    {
                        GameObject cap = Object.Instantiate(material.pierCapPrefab, measure.transform);
                        report.AppendLine("SOURCE: " + material.name + " cap root scale=" + cap.transform.localScale +
                            "; root-local mesh bounds=" + LocalBounds(cap.transform) + "; scaled bounds=" + LocalBounds(measure.transform));
                    }
                }
                finally { Object.DestroyImmediate(measure); }
            }
            foreach (BridgeMaterialSO road in materials.Take(2))
                foreach (BridgeMaterialSO pier in materials.Skip(2))
                {
                    foreach (float angle in new[] { 0f, 5f, -5f, 15f, -15f, 30f, -30f, 45f, -45f })
                        foreach (bool reverse in new[] { false, true })
                        {
                            using (var fixture = new Fixture(preview, road, pier, angle, angle, reverse, false))
                            {
                                Verify(fixture, report, road.name + "/" + pier.name + " slope " + angle + " reverse " + reverse); cases++;
                                if (!reverse && ((road == materials[0] && (angle == 0f || Mathf.Abs(angle) == 5f || Mathf.Abs(angle) == 15f || Mathf.Abs(angle) == 30f)) ||
                                    (road == materials[1] && pier == materials[3] && (angle == 0f || Mathf.Abs(angle) == 15f))))
                                    Capture(fixture, angle, report);
                            }
                        }
                    foreach (float sign in new[] { -1f, 1f })
                        using (var fixture = new Fixture(preview, road, pier, 30f * sign, -30f * sign, false, true))
                        {
                            Verify(fixture, report, road.name + "/" + pier.name + (sign > 0 ? " crest" : " valley") + "/pier-first"); cases++;
                            Capture(fixture, 0f, report, sign > 0 ? "crest" : "valley");
                        }
                    foreach (var transition in new[] { new Vector2(30f, 5f), new Vector2(5f, 0f), new Vector2(-30f, -5f), new Vector2(-5f, 0f) })
                        foreach (bool reverse in new[] { false, true })
                            using (var fixture = new Fixture(preview, road, pier, transition.x, transition.y, reverse, true))
                            {
                                string transitionLabel = transition.x.ToString("0") + "-to-" + transition.y.ToString("0");
                                Verify(fixture, report, road.name + "/" + pier.name + " transition " + transitionLabel + " reverse " + reverse); cases++;
                                if (!reverse && transition.x == 30f) VerifyBearingLifecycle(fixture, report);
                                if (!reverse) Capture(fixture, 0f, report, transitionLabel);
                            }
                    using (var fixture = new Fixture(preview, road, pier, 0f, 0f, false, true))
                    {
                        fixture.Roads[0].gameObject.SetActive(false); fixture.Roads[1].gameObject.SetActive(false); Refresh(fixture.Pier);
                        VerifyNoRoad(fixture); fixture.Roads[0].gameObject.SetActive(true); fixture.Roads[1].gameObject.SetActive(true);
                        Refresh(fixture.Pier); Verify(fixture, report, road.name + "/" + pier.name + " restored roads");
                        VerifyIgnoredNeighbors(fixture, report); VerifyBrokenCap(fixture); cases += 2;
                        fixture.Left.transform.position = new Vector3(-8f, -4f, 0f); fixture.Right.transform.position = new Vector3(8f, 4f, 0f);
                        foreach (Bar bar in fixture.Roads) { bar.StartPosition = bar.startPoint.transform.position; bar.UpdateCreatingBar(bar.endPoint.transform.position); }
                        Refresh(fixture.Pier); Verify(fixture, report, road.name + "/" + pier.name + " moved endpoints"); cases += 3;
                        fixture.Bottom.transform.position += Vector3.down * 4f;
                        fixture.Pier.StartPosition = fixture.Pier.startPoint.transform.position;
                        fixture.Pier.UpdateCreatingBar(fixture.Pier.endPoint.transform.position);
                        Verify(fixture, report, road.name + "/" + pier.name + " moved pier foundation with fixed upper joint"); cases++;
                    }
                }
            report.Insert(0, "PASS\n"); report.AppendLine("PASS: " + cases + " graph/mesh cases; structural nodes, shaft/root/collider poses and material values preserved.");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            RenderTexture.active = previous;
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            try
            {
                Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active && Selection.objects.SequenceEqual(selected), "Scene setup or selection changed.");
                for (int index = 0; index < scenes.Length; index++)
                    Require(SceneManager.GetSceneAt(index) == scenes[index] && scenes[index].isLoaded == loaded[index] && scenes[index].isDirty == dirty[index] &&
                        (!loaded[index] || scenes[index].GetRootGameObjects().SequenceEqual(roots[index])), "A live scene changed.");
                Require(assetHashes.All(pair => Hash(pair.Key) == pair.Value) && sceneHashes.All(pair => Hash(pair.Key) == pair.Value), "An asset or saved scene changed.");
                Require(Point.AllPoints.SequenceEqual(points) && PlayerDataManager.Instance == manager &&
                    (player == null || ReferenceEquals(player, manager.CurrentData) && JsonUtility.ToJson(player) == playerJson), "Live point registry or profile changed.");
                report.AppendLine("PASS: live scene setup/dirty flags, files, source assets, point registry and player data unchanged. No scene saved or simulation run.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root;
        public readonly Point Top, Bottom, Left, Right;
        public readonly Bar Pier;
        public readonly List<Bar> Roads = new List<Bar>();
        public Fixture(Scene scene, BridgeMaterialSO road, BridgeMaterialSO pier, float leftAngle, float rightAngle, bool reverse, bool pierFirst)
        {
            Root = Owned(scene, "Detached pier seating graph");
            Top = Node("Upper joint", Vector3.zero); Bottom = Node("Foundation", new Vector3(0f, -12f, 0f));
            Left = Node("Left road joint", new Vector3(-8f, -8f * Mathf.Tan(leftAngle * Mathf.Deg2Rad), 0f));
            Right = Node("Right road joint", new Vector3(8f, 8f * Mathf.Tan(rightAngle * Mathf.Deg2Rad), 0f));
            if (pierFirst) Pier = Member(pier, reverse ? Top : Bottom, reverse ? Bottom : Top);
            Roads.Add(Member(road, reverse ? Top : Left, reverse ? Left : Top));
            Roads.Add(Member(road, reverse ? Right : Top, reverse ? Top : Right));
            if (!pierFirst) Pier = Member(pier, reverse ? Top : Bottom, reverse ? Bottom : Top);
            Refresh(Pier);
        }
        private Point Node(string name, Vector3 position)
        {
            GameObject item = Owned(Root.scene, name); item.SetActive(false); item.transform.SetParent(Root.transform, false); item.transform.position = position;
            Point point = item.AddComponent<Point>(); point.enabled = false; point.Runtime = true; item.SetActive(true); return point;
        }
        private Bar Member(BridgeMaterialSO material, Point a, Point b)
        {
            GameObject item = Owned(Root.scene, material.name); item.transform.SetParent(Root.transform, false);
            Bar bar = item.AddComponent<Bar>(); bar.enabled = false; bar.startPoint = a; bar.endPoint = b; bar.StartPosition = a.transform.position;
            bar.Initialize(material); bar.UpdateCreatingBar(b.transform.position);
            a.ConnectedBars.Add(bar); b.ConnectedBars.Add(bar);
            foreach (MonoBehaviour behaviour in item.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            return bar;
        }
        public void Dispose() { Object.DestroyImmediate(Root); }
    }

    private static void Verify(Fixture fixture, StringBuilder report, string label)
    {
        Bar pier = fixture.Pier; Transform cap = pier.transform.Find("PierCap"); Require(cap != null, "The pier cap is missing.");
        Vector3[] nodes = new[] { fixture.Top.transform.position, fixture.Bottom.transform.position, fixture.Left.transform.position, fixture.Right.transform.position };
        Transform[] protectedTransforms = pier.GetComponentsInChildren<Transform>(true).Where(item => item != cap && !item.IsChildOf(cap) && item.GetComponentInParent<PierCapBearing>() == null).ToArray();
        string[] poses = protectedTransforms.Select(Pose).ToArray(); string material = JsonUtility.ToJson(pier.materialData);
        MeshRenderer shaftRenderer = pier.transform.Cast<Transform>().Where(item => item.name.StartsWith("VisualSegment", StringComparison.Ordinal))
            .SelectMany(item => item.GetComponentsInChildren<MeshRenderer>(true)).First();
        BoxCollider shaftCollider = shaftRenderer.GetComponent<BoxCollider>(); if (shaftCollider == null) shaftCollider = shaftRenderer.gameObject.AddComponent<BoxCollider>();
        Bounds colliderBounds = shaftCollider.bounds; Vector3 colliderCenter = shaftCollider.center, colliderSize = shaftCollider.size;
        Refresh(pier);
        Require(protectedTransforms.Select(Pose).SequenceEqual(poses) && JsonUtility.ToJson(pier.materialData) == material &&
            colliderBounds == shaftCollider.bounds && colliderCenter == shaftCollider.center && colliderSize == shaftCollider.size &&
            nodes.SequenceEqual(new[] { fixture.Top.transform.position, fixture.Bottom.transform.position, fixture.Left.transform.position, fixture.Right.transform.position }), "Seating changed structural/root/shaft/collider/material data: " + label);
        Bounds capBounds = LocalBounds(cap); float capWorst = float.NegativeInfinity, shaftWorst = float.NegativeInfinity;
        int capContacts = 0; bool uniformGrade = true; Vector3 firstNormal = Vector3.zero;
        foreach (Bar road in fixture.Roads.Where(item => item.gameObject.activeInHierarchy))
        {
            Vector3 normal = road.transform.up;
            if (firstNormal == Vector3.zero) firstNormal = normal;
            else if (Vector3.Dot(firstNormal, normal) < .99999f) uniformGrade = false;
            // Separate the central planks from the lower edge beams. A beam
            // outside the cap's depth footprint cannot set its contact height.
            foreach (MeshPart part in MeshParts(road.transform))
            {
                Vector3 underside = road.transform.TransformPoint(new Vector3(0f, part.Bounds.min.y, 0f));
                if (DepthOverlaps(cap, capBounds, road.transform, part.Bounds))
                {
                    float overlap = Violation(cap, capBounds, road, part.Bounds, normal, underside);
                    if (!float.IsNegativeInfinity(overlap)) { capContacts++; capWorst = Mathf.Max(capWorst, overlap); }
                }
                foreach (Transform segment in pier.transform)
                    if (segment.name.StartsWith("VisualSegment", StringComparison.Ordinal))
                    {
                        Bounds shaftBounds = LocalBounds(segment);
                        if (DepthOverlaps(segment, shaftBounds, road.transform, part.Bounds))
                            shaftWorst = Mathf.Max(shaftWorst, Violation(segment, shaftBounds, road, part.Bounds, normal, underside));
                    }
            }
        }
        float contactGap = -capWorst;
        report.AppendLine("MEASURE: " + label + "; cap/shaft normal overlap=" + capWorst.ToString("0.0000") + "/" + shaftWorst.ToString("0.0000") +
            "m; nearest actual depth-overlapping contact gap=" + contactGap.ToString("0.0000") + "m; contact parts=" + capContacts +
            "; road-local bounds=" + LocalBounds(fixture.Roads[0].transform) + "; cap-local bounds=" + capBounds);
        Require(capContacts > 0, "No road mesh part actually overlaps the cap footprint: " + label);
        Require(capWorst <= .003f, "Cap protrudes past actual road underside; cap/shaft normal overlap=" + capWorst.ToString("0.0000") + "/" + shaftWorst.ToString("0.0000") + " m: " + label);
        Require(shaftWorst <= .003f, "Unchanged shaft protrudes past road underside; cap/shaft normal overlap=" + capWorst.ToString("0.0000") + "/" + shaftWorst.ToString("0.0000") + " m: " + label);
        if (uniformGrade) Require(Mathf.Abs(contactGap) <= .0002f, "Cap does not touch the actual overlapping roadway; contact gap=" + contactGap.ToString("0.000000") + "m: " + label);
        VerifyBearingContact(fixture, report, label, !uniformGrade);
        HostWorldBridgePose snapshot = HostWorldBridgePose.Local(cap); Vector3 worldPosition = pier.transform.TransformPoint(snapshot.Position);
        Require(Vector3.Distance(worldPosition, cap.position) < .0001f && Quaternion.Angle(pier.transform.rotation * snapshot.Rotation, cap.rotation) < .001f,
            "Corrected cap pose cannot be represented by the existing host visual snapshot.");
        report.AppendLine("PASS: " + label + "; graph, shaft/collider and host cap pose preserved.");
    }

    private static void VerifyBearingContact(Fixture fixture, StringBuilder report, string label, bool required)
    {
        PierCapBearing bearing = fixture.Pier.GetComponentInChildren<PierCapBearing>(true);
        Vector3[] shape = bearing != null && bearing.gameObject.activeInHierarchy ? bearing.CaptureVertices() : Array.Empty<Vector3>();
        Require(!required || shape.Length > 0, "A changed road grade still has no fitted top bearing: " + label);
        VerifyTopFootprintCoverage(fixture, bearing, shape, report, label);
        if (shape.Length == 0) return;
        Require(shape.Length % 8 == 0 && shape.Length <= 512, "Invalid fitted bearing prism geometry: " + label);
        Require(bearing.GetComponentsInChildren<Collider>(true).Length == 0, "A visual bearing added structural collision: " + label);
        MeshFilter filter = bearing.GetComponent<MeshFilter>(); MeshRenderer renderer = bearing.GetComponent<MeshRenderer>();
        Require(filter != null && filter.sharedMesh != null && renderer != null && renderer.enabled && filter.sharedMesh.triangles.Length > 0,
            "The fitted bearing has no visible generated mesh: " + label);
        Vector3[] rendered = filter.sharedMesh.vertices;
        foreach (Vector3 corner in shape)
            Require(rendered.Any(vertex => Vector3.SqrMagnitude(vertex - corner) <= .00000001f), "The rendered bearing omits a measured contact corner: " + label);
        Vector3[] renderedWorld = rendered.Select(bearing.transform.TransformPoint).ToArray(); int[] renderedTriangles = filter.sharedMesh.triangles;
        float worstGap = 0f, worstOverlap = 0f; int samples = 0;
        for (int prism = 0; prism < shape.Length; prism += 8)
        {
            float width = Vector3.Distance(shape[prism + 4], shape[prism + 5]);
            float depth = Vector3.Distance(shape[prism + 4], shape[prism + 7]);
            float xInset = Mathf.Min(.25f, .0001f / Mathf.Max(width, .000001f));
            float zInset = Mathf.Min(.25f, .0001f / Mathf.Max(depth, .000001f));
            // Measure the contact surface throughout each finite prism. Extreme
            // AABB contact alone missed the central gaps at changing grades.
            foreach (float xT in new[] { .001f, .25f, .5f, .75f, .999f })
                foreach (float zT in new[] { .001f, .5f, .999f })
                {
                    // Stay inside the finite face even for very narrow strips.
                    // A fixed domain epsilon can accidentally select a lower
                    // neighbouring beam several decimetres away vertically.
                    float sampleX = Mathf.Lerp(xInset, 1f - xInset, xT);
                    float sampleZ = Mathf.Lerp(zInset, 1f - zInset, zT);
                    Vector3 a = Vector3.Lerp(shape[prism + 4], shape[prism + 5], sampleX);
                    Vector3 b = Vector3.Lerp(shape[prism + 7], shape[prism + 6], sampleX);
                    Vector3 point = bearing.transform.TransformPoint(Vector3.Lerp(a, b, sampleZ));
                    Require(HasUpwardRenderedContact(renderedWorld, renderedTriangles, point), "The bearing's fitted contact surface is missing or faces inward: " + label);
                    Require(TryRoadUndersideAt(fixture, point, out float underside), "A fitted bearing extends outside the real road footprint: " + label);
                    float difference = underside - point.y;
                    if (Mathf.Abs(difference) > .0003f)
                        report.AppendLine("CONTACT FAILURE: prism=" + prism / 8 + "; point=" + point.ToString("R") + "; roadY=" + underside.ToString("R") +
                            "; width/depth=" + width.ToString("R") + "/" + depth.ToString("R") + "; sample=" + sampleX + "/" + sampleZ);
                    worstGap = Mathf.Max(worstGap, difference); worstOverlap = Mathf.Max(worstOverlap, -difference); samples++;
                }
        }
        report.AppendLine("BEARING CONTACT: " + label + "; samples=" + samples + "; max vertical gap=" + worstGap.ToString("0.000000") +
            "m; max penetration=" + worstOverlap.ToString("0.000000") + "m; prisms=" + shape.Length / 8);
        Require(worstGap <= .0003f && worstOverlap <= .0003f, "Fitted bearing does not meet the road throughout its contact surface: " + label +
            "; gap=" + worstGap.ToString("0.000000") + "; penetration=" + worstOverlap.ToString("0.000000"));
    }

    private static void VerifyTopFootprintCoverage(Fixture fixture, PierCapBearing bearing, Vector3[] shape, StringBuilder report, string label)
    {
        Require(PierCapSeatingProfiles.TryGetTopFace(fixture.Pier.materialData.pierCapPrefab, out Bounds face), "No measured cap top face is available: " + label);
        Transform cap = fixture.Pier.transform.Find("PierCap");
        MeshFilter filter = bearing != null && shape.Length > 0 ? bearing.GetComponent<MeshFilter>() : null;
        Vector3[] renderedWorld = filter != null && filter.sharedMesh != null
            ? filter.sharedMesh.vertices.Select(bearing.transform.TransformPoint).ToArray() : Array.Empty<Vector3>();
        int[] triangles = filter != null && filter.sharedMesh != null ? filter.sharedMesh.triangles : Array.Empty<int>();
        int contactSamples = 0, filledSamples = 0; float unfilledCapGap = 0f;
        // Include the central point and points across the entire real top face.
        // A single touching corner elsewhere must not conceal a central gap.
        foreach (float xT in new[] { .05f, .25f, .5f, .75f, .95f })
            foreach (float zT in new[] { .05f, .5f, .95f })
            {
                Vector3 capPoint = cap.TransformPoint(new Vector3(Mathf.Lerp(face.min.x, face.max.x, xT), face.max.y, Mathf.Lerp(face.min.z, face.max.z, zT)));
                if (!TryRoadUndersideAt(fixture, capPoint, out float underside)) continue;
                contactSamples++; float gap = underside - capPoint.y; unfilledCapGap = Mathf.Max(unfilledCapGap, gap);
                if (gap <= .0003f) continue;
                Vector3 target = new Vector3(capPoint.x, underside, capPoint.z);
                Require(HasUpwardRenderedContact(renderedWorld, triangles, target), "The fitted bearing leaves a gap above the cap's actual top face: " + label +
                    "; X/Z sample=" + xT + "/" + zT + "; original cap gap=" + gap.ToString("0.000000") + "m");
                filledSamples++;
            }
        Require(contactSamples > 0, "The measured cap top face does not overlap any connected road: " + label);
        report.AppendLine("TOP FACE COVERAGE: " + label + "; sampled=" + contactSamples + "; filled=" + filledSamples +
            "; unfilled rigid-cap gap=" + unfilledCapGap.ToString("0.000000") + "m; final gap<=0.000300m.");
    }

    private static void VerifyBearingLifecycle(Fixture fixture, StringBuilder report)
    {
        PierCapBearing original = fixture.Pier.GetComponentInChildren<PierCapBearing>(true);
        Require(original != null && original.gameObject.activeInHierarchy, "The lifecycle fixture has no fitted bearing.");
        MeshFilter originalFilter = original.GetComponent<MeshFilter>(); Mesh originalMesh = originalFilter.sharedMesh;
        Vector3[] originalVertices = originalMesh.vertices; Vector3[] originalShape = original.CaptureVertices();
        bool wasEnabled = original.enabled; GameObject cloned = null, ghost = null; Mesh clonedMesh = null, ghostMesh = null;
        try
        {
            Refresh(fixture.Pier); Refresh(fixture.Pier);
            Require(originalFilter.sharedMesh == originalMesh && originalFilter.sharedMesh.vertices.SequenceEqual(originalVertices),
                "Unchanged seating refresh replaced or rewrote its owned mesh.");
            original.enabled = false; original.enabled = true;
            original.gameObject.SetActive(false); original.gameObject.SetActive(true);
            Require(originalFilter.sharedMesh == originalMesh && originalFilter.sharedMesh.vertices.SequenceEqual(originalVertices),
                "Re-enabling a surviving bearing lost or replaced its owned mesh.");
            cloned = Object.Instantiate(original.gameObject, fixture.Root.transform, false);
            PierCapBearing copy = cloned.GetComponent<PierCapBearing>(); copy.enabled = true;
            clonedMesh = copy.GetComponent<MeshFilter>().sharedMesh;
            Require(clonedMesh != null && clonedMesh != originalMesh && clonedMesh.vertices.SequenceEqual(originalVertices),
                "A cloned bearing still shares its source's generated mesh.");
            Vector3[] changed = copy.CaptureVertices();
            for (int i = 0; i < changed.Length; i++) changed[i].x += .125f;
            copy.SetPrisms(changed, PierCapBearing.Appearance(fixture.Pier.materialData));
            Require(originalMesh.vertices.SequenceEqual(originalVertices) && original.CaptureVertices().SequenceEqual(originalShape),
                "Editing a cloned bearing changed its source's mesh or shape.");
            Object.DestroyImmediate(cloned); cloned = null;
            Require(clonedMesh == null && originalMesh != null && originalMesh.vertices.SequenceEqual(originalVertices),
                "Destroying a clone either leaked its mesh or destroyed its source's mesh.");
            ghost = Owned(fixture.Root.scene, "Detached ghost bearing copy"); ghost.transform.SetParent(fixture.Root.transform, false);
            PierCapBearing ghostCopy = ghost.AddComponent<PierCapBearing>(); ghostCopy.SetPrisms(originalShape, PierCapBearing.Appearance(fixture.Pier.materialData));
            ghostMesh = ghost.GetComponent<MeshFilter>().sharedMesh;
            Require(ghostMesh != null && ghostMesh != originalMesh && ghostMesh.vertices.SequenceEqual(originalVertices),
                "The manual ghost-copy pattern did not create an independent fitted mesh.");
            Object.DestroyImmediate(ghost); ghost = null;
            Require(ghostMesh == null && originalMesh != null && originalFilter.sharedMesh == originalMesh,
                "Destroying a ghost copy either leaked its mesh or destroyed its source's mesh.");
            report.AppendLine("PASS: " + fixture.Roads[0].materialData.name + "/" + fixture.Pier.materialData.name +
                "; unchanged refresh, disable/re-enable, private clone/ghost ownership, independent edits and owned-mesh teardown.");
        }
        finally
        {
            if (cloned != null) Object.DestroyImmediate(cloned); if (ghost != null) Object.DestroyImmediate(ghost);
            if (original != null) original.enabled = wasEnabled;
        }
    }

    private static bool HasUpwardRenderedContact(Vector3[] vertices, int[] triangles, Vector3 point)
    {
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            if (Vector3.Cross(b - a, c - a).y <= .000001f) continue;
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < .000001f) continue;
            float u = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / denominator;
            float v = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / denominator;
            float w = 1f - u - v;
            if (u < -.00001f || v < -.00001f || w < -.00001f) continue;
            if (Mathf.Abs(u * a.y + v * b.y + w * c.y - point.y) <= .0003f) return true;
        }
        return false;
    }

    private static bool TryRoadUndersideAt(Fixture fixture, Vector3 point, out float y)
    {
        y = float.PositiveInfinity;
        foreach (Bar road in fixture.Roads.Where(item => item.gameObject.activeInHierarchy))
            foreach (MeshPart part in MeshParts(road.transform))
            {
                Vector3 normal = road.transform.up;
                if (normal.y <= .05f) continue;
                Vector3 onPlane = road.transform.TransformPoint(new Vector3(0f, part.Bounds.min.y, 0f));
                float candidate = (Vector3.Dot(normal, onPlane) - normal.x * point.x - normal.z * point.z) / normal.y;
                Vector3 roadLocal = road.transform.InverseTransformPoint(new Vector3(point.x, candidate, point.z));
                if (roadLocal.x < part.Bounds.min.x - .0000005f || roadLocal.x > part.Bounds.max.x + .0000005f ||
                    roadLocal.z < part.Bounds.min.z - .0000005f || roadLocal.z > part.Bounds.max.z + .0000005f) continue;
                y = Mathf.Min(y, candidate);
            }
        return !float.IsPositiveInfinity(y);
    }

    private static void VerifyNoRoad(Fixture fixture)
    {
        Transform cap = fixture.Pier.transform.Find("PierCap");
        float topOffset = (float)typeof(Bar).GetField("capTopOffset", Hidden).GetValue(fixture.Pier);
        Require(Vector3.Distance(cap.position, fixture.Top.transform.position - Vector3.up * topOffset) < .0002f && Quaternion.Angle(cap.rotation, Quaternion.identity) < .001f,
            "Removing roads did not restore the original unsupported cap pose.");
        PierCapBearing bearing = fixture.Pier.GetComponentInChildren<PierCapBearing>(true);
        Require(bearing == null || !bearing.gameObject.activeInHierarchy || bearing.CaptureVertices().Length == 0, "Removing every road left a floating bearing.");
    }

    private static void VerifyIgnoredNeighbors(Fixture fixture, StringBuilder report)
    {
        Bar road = fixture.Roads[0]; fixture.Roads[1].gameObject.SetActive(false);
        BridgeMaterialSO original = road.materialData;
        BridgeMaterialSO nonRoad = Object.Instantiate(original); nonRoad.hideFlags = HideFlags.HideAndDontSave; nonRoad.isRoad = false;
        try { road.materialData = nonRoad; Refresh(fixture.Pier); VerifyNoRoad(fixture); }
        finally { road.materialData = original; Object.DestroyImmediate(nonRoad); }
        Point a = road.startPoint, b = road.endPoint;
        try
        {
            // Deliberately leave the stale ConnectedBars entry: identity, not
            // nearby mesh coordinates, decides whether a road is connected.
            if (road.startPoint == fixture.Top) road.startPoint = fixture.Bottom;
            if (road.endPoint == fixture.Top) road.endPoint = fixture.Bottom;
            Refresh(fixture.Pier); VerifyNoRoad(fixture);
        }
        finally { road.startPoint = a; road.endPoint = b; fixture.Roads[1].gameObject.SetActive(true); Refresh(fixture.Pier); }
        Verify(fixture, report, original.name + "/" + fixture.Pier.materialData.name + " non-road/disconnected neighbors ignored");
    }

    private static void VerifyBrokenCap(Fixture fixture)
    {
        BarStressHandler state = fixture.Pier.gameObject.AddComponent<BarStressHandler>(); state.enabled = false; state.isBroken = true;
        Transform cap = fixture.Pier.transform.Find("PierCap"); Vector3 position = cap.position; Quaternion rotation = cap.rotation;
        cap.SetPositionAndRotation(position + new Vector3(1f, -2f, 0f), Quaternion.Euler(0f, 0f, 17f));
        string fallenPose = Pose(cap);
        try { Refresh(fixture.Pier); Require(Pose(cap) == fallenPose, "Seating snapped a broken/falling cap back to its old structural node."); }
        finally { state.isBroken = false; Object.DestroyImmediate(state); cap.SetPositionAndRotation(position, rotation); Refresh(fixture.Pier); }
    }

    private readonly struct MeshPart
    {
        public readonly Bounds Bounds;
        public MeshPart(Bounds bounds) { Bounds = bounds; }
    }
    private static IEnumerable<MeshPart> MeshParts(Transform root)
    {
        foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Renderer renderer = mesh.GetComponent<Renderer>();
            if (mesh.sharedMesh == null || renderer == null || !renderer.enabled || !mesh.gameObject.activeInHierarchy) continue;
            Bounds source = mesh.sharedMesh.bounds, bounds = default; bool found = false;
            for (int bits = 0; bits < 8; bits++)
            {
                Vector3 corner = new Vector3((bits & 1) == 0 ? source.min.x : source.max.x,
                    (bits & 2) == 0 ? source.min.y : source.max.y, (bits & 4) == 0 ? source.min.z : source.max.z);
                Vector3 point = root.InverseTransformPoint(mesh.transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
            }
            yield return new MeshPart(bounds);
        }
    }
    private static void DepthRange(Transform root, Bounds bounds, out float min, out float max)
    {
        min = float.PositiveInfinity; max = float.NegativeInfinity;
        for (int bits = 0; bits < 8; bits++)
        {
            Vector3 corner = new Vector3((bits & 1) == 0 ? bounds.min.x : bounds.max.x,
                (bits & 2) == 0 ? bounds.min.y : bounds.max.y, (bits & 4) == 0 ? bounds.min.z : bounds.max.z);
            float z = root.TransformPoint(corner).z; min = Mathf.Min(min, z); max = Mathf.Max(max, z);
        }
    }
    private static bool DepthOverlaps(Transform a, Bounds aBounds, Transform b, Bounds bBounds)
    {
        DepthRange(a, aBounds, out float aMin, out float aMax); DepthRange(b, bBounds, out float bMin, out float bMax);
        return Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin) > .0001f;
    }

    // Independent convex bounds check: clip the cap/shaft's oriented XY rectangle
    // to each actual road's local X span before evaluating its underside plane.
    private static float Violation(Transform item, Bounds bounds, Bar road, Bounds roadBounds, Vector3 normal, Vector3 underside)
    {
        Vector3 min = bounds.min, max = bounds.max;
        var polygon = new List<Vector3> { item.TransformPoint(new Vector3(min.x, min.y, 0f)), item.TransformPoint(new Vector3(max.x, min.y, 0f)),
            item.TransformPoint(new Vector3(max.x, max.y, 0f)), item.TransformPoint(new Vector3(min.x, max.y, 0f)) };
        Vector3 tangent = road.transform.right; float origin = Vector3.Dot(tangent, road.transform.position);
        polygon = Clip(polygon, tangent, origin + roadBounds.min.x, true);
        polygon = Clip(polygon, tangent, origin + roadBounds.max.x, false);
        float worst = float.NegativeInfinity;
        foreach (Vector3 point in polygon) worst = Mathf.Max(worst, Vector3.Dot(normal, point - underside));
        return worst;
    }
    private static List<Vector3> Clip(List<Vector3> input, Vector3 axis, float edge, bool lower)
    {
        var result = new List<Vector3>(); if (input.Count == 0) return result;
        Vector3 before = input[input.Count - 1]; float a = Vector3.Dot(axis, before) - edge; bool inA = lower ? a >= 0f : a <= 0f;
        foreach (Vector3 after in input)
        {
            float b = Vector3.Dot(axis, after) - edge; bool inB = lower ? b >= 0f : b <= 0f;
            if (inA != inB) result.Add(Vector3.LerpUnclamped(before, after, a / (a - b)));
            if (inB) result.Add(after); before = after; a = b; inA = inB;
        }
        return result;
    }
    private static Bounds LocalBounds(Transform root)
    {
        bool found = false; Bounds result = default;
        foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mesh.sharedMesh == null) continue; Bounds bounds = mesh.sharedMesh.bounds;
            for (int bits = 0; bits < 8; bits++)
            {
                Vector3 corner = new Vector3((bits & 1) == 0 ? bounds.min.x : bounds.max.x, (bits & 2) == 0 ? bounds.min.y : bounds.max.y, (bits & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 local = root.InverseTransformPoint(mesh.transform.TransformPoint(corner));
                if (!found) { result = new Bounds(local, Vector3.zero); found = true; } else result.Encapsulate(local);
            }
        }
        Require(found, "A required existing prefab has no mesh bounds: " + root.name); return result;
    }
    private static void Refresh(Bar bar) => typeof(Bar).GetMethod("RefreshPierCapSeating", Hidden).Invoke(bar, null);
    private static string Pose(Transform transform) => transform.localPosition.ToString("R") + transform.localRotation.ToString("R") + transform.localScale.ToString("R");
    private static GameObject Owned(Scene scene, string name, params Type[] components)
    { GameObject item = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components); SceneManager.MoveGameObjectToScene(item, scene); return item; }

    private static void Capture(Fixture fixture, float angle, StringBuilder report, string label = null)
    {
        Scene scene = fixture.Root.scene; GameObject cameraObject = Owned(scene, "Detached seating camera", typeof(Camera));
        GameObject lightObject = Owned(scene, "Detached seating light", typeof(Light));
        Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.useOcclusionCulling = false;
        camera.orthographic = true; camera.orthographicSize = 8f; camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
        camera.transform.position = new Vector3(11f, 5f, -22f); camera.transform.LookAt(new Vector3(0f, -3f, 0f));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(154, 188, 204, 255);
        Light light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        var target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
        Transform cap = fixture.Pier.transform.Find("PierCap"); Vector3 position = cap.position; Quaternion rotation = cap.rotation;
        PierCapBearing bearing = fixture.Pier.GetComponentInChildren<PierCapBearing>(true);
        bool bearingVisible = bearing != null && bearing.gameObject.activeSelf;
        RenderTexture previous = RenderTexture.active;
        try
        {
            for (int view = 0; view < 2; view++)
            {
                bool front = view == 1;
                camera.orthographicSize = front ? 3.3f : 8f;
                camera.transform.position = front ? new Vector3(0f, -.8f, -25f) : new Vector3(11f, 5f, -22f);
                camera.transform.LookAt(front ? new Vector3(0f, -.8f, 0f) : new Vector3(0f, -3f, 0f));
                for (int stage = 0; stage < 2; stage++)
                {
                    if (stage == 0) SetPreviousAggregatePose(fixture, angle);
                    else cap.SetPositionAndRotation(position, rotation);
                    if (bearing != null) bearing.gameObject.SetActive(stage != 0 && bearingVisible);
                    if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
                    else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                    try
                    {
                        image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0); image.Apply(); Directory.CreateDirectory("Temp/PierRoadSeating");
                        string pierName = fixture.Pier.materialData.name.ToLowerInvariant().Replace(' ', '-');
                        string roadName = fixture.Roads[0].materialData.name.ToLowerInvariant().Replace(' ', '-');
                        string path = "Temp/PierRoadSeating/" + roadName + "-" + pierName + "-" + (label ?? angle.ToString("0")) +
                            (front ? "-front" : string.Empty) + (stage == 0 ? "-gap-before.png" : "-after.png");
                        File.WriteAllBytes(path, image.EncodeToPNG()); report.AppendLine("PREVIEW: " + path);
                    }
                    finally { Object.DestroyImmediate(image); }
                }
            }
        }
        finally
        {
            cap.SetPositionAndRotation(position, rotation); RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            if (bearing != null) bearing.gameObject.SetActive(bearingVisible);
            Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
        }
    }
    private static void SetPreviousAggregatePose(Fixture fixture, float angle)
    {
        Transform cap = fixture.Pier.transform.Find("PierCap");
        float topOffset = (float)typeof(Bar).GetField("capTopOffset", Hidden).GetValue(fixture.Pier);
        Vector3 baseline = fixture.Top.transform.position - Vector3.up * topOffset;
        cap.SetPositionAndRotation(baseline, Quaternion.Euler(0f, 0f, angle));
        Bounds capBounds = LocalBounds(cap); float lower = 0f;
        // Recreate the previous full-width-box fit only for the comparison
        // image. It intentionally includes edge beams outside the cap depth.
        foreach (Bar road in fixture.Roads)
        {
            Bounds bounds = LocalBounds(road.transform); Vector3 normal = road.transform.up;
            Vector3 underside = road.transform.TransformPoint(new Vector3(0f, bounds.min.y, 0f));
            float violation = Violation(cap, capBounds, road, bounds, normal, underside);
            if (!float.IsNegativeInfinity(violation)) lower = Mathf.Max(lower, (violation + .015f) / normal.y);
        }
        cap.position = baseline - Vector3.up * lower;
    }
    private static string Hash(string path) { using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
#endif
