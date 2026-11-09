#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Detached tests of actual pier/deck/girder triangles, including both outboard load paths.</summary>
[InitializeOnLoad]
public static class PierSupportVisualValidation
{
    public const string RequestPath = "Temp/pier-support-visual-validation.request";
    public const string ReportPath = "Logs/pier-support-visual-validation.txt";
    private const float ContactTolerance = .001f;
    private static bool running;
    private static readonly string[] Names = { "Wood Road", "Concrete Road", "Pier", "Concrete Pier", "SteelPier", "Beam", "ConcreteBeam", "SteelBeam" };
    static PierSupportVisualValidation() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            BuildPipeline.isBuildingPlayer || !File.Exists(RequestPath)) return;
        File.Delete(RequestPath); Run();
    }

    [MenuItem("Tools/Civil Craft/Bridge Visuals/Validate Full Pier Support Contact (Detached)")]
    public static void Run()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        running = true; var report = new StringBuilder("Actual mesh contact across Wood, Concrete and Steel piers, roads and paired rigid beams\n");
        Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] loaded = scenes.Select(s => s.isLoaded).ToArray(), dirty = scenes.Select(s => s.isDirty).ToArray();
        GameObject[][] roots = scenes.Select(s => s.isLoaded ? s.GetRootGameObjects() : Array.Empty<GameObject>()).ToArray();
        Scene active = SceneManager.GetActiveScene(); Object[] selection = Selection.objects; Point[] registry = Point.AllPoints.ToArray();
        var hashes = AssetDatabase.GetDependencies(Names.Select(n => "Assets/BridgeBuilder/Data/Resources/" + n + ".asset").ToArray(), true)
            .Concat(scenes.Select(s => s.path)).Where(File.Exists).Distinct().ToDictionary(p => p, Hash);
        PlayerDataManager manager = PlayerDataManager.Instance; PlayerData data = manager != null ? manager.CurrentData : null;
        string json = data != null ? JsonUtility.ToJson(data) : null;
        RenderTexture previous = RenderTexture.active; Scene preview = default;
        int cases = 0, failures = 0, failureCaptures = 0;
        try
        {
            var materials = Names.ToDictionary(n => n, n => Resources.Load<BridgeMaterialSO>(n));
            Require(materials.All(p => p.Value != null), "A required authored road, pier or beam material is missing.");
            preview = EditorSceneManager.NewPreviewScene();
            var grades = new[] { Vector2.zero, new Vector2(15f, 15f), new Vector2(-15f, -15f), new Vector2(30f, 5f),
                new Vector2(5f, 0f), new Vector2(25f, -25f), new Vector2(-25f, 25f) };
            foreach (float width in new[] { 0f, 16f, 19f })
                foreach (string pierName in new[] { "Pier", "Concrete Pier", "SteelPier" })
                {
                    foreach (string roadName in new[] { "Wood Road", "Concrete Road" })
                        foreach (Vector2 grade in grades)
                            using (var fixture = new Fixture(preview, materials[pierName], materials[roadName], materials["ConcreteBeam"], width, grade))
                            {
                                bool passed = VerifyCase(fixture);
                                if (passed && pierName == "Concrete Pier" && roadName == "Concrete Road" &&
                                    (grade == Vector2.zero || grade == grades[1] || grade == grades[5] || grade == grades[6])) Capture(fixture, report);
                            }
                    foreach (string beamName in new[] { "Beam", "ConcreteBeam", "SteelBeam" })
                        foreach (Vector2 grade in new[] { grades[0], grades[1], grades[5], grades[6] })
                            using (var fixture = new Fixture(preview, materials[pierName], null, materials[beamName], width, grade))
                            {
                                bool passed = VerifyCase(fixture);
                                if (width == 0f && beamName == "ConcreteBeam" && grade == Vector2.zero)
                                {
                                    foreach (Bar carrier in fixture.Carriers)
                                        carrier.transform.SetPositionAndRotation(carrier.transform.position - Vector3.up * .2f,
                                            Quaternion.AngleAxis(-4f, Vector3.forward) * carrier.transform.rotation);
                                    fixture.Pier.transform.position += Vector3.up * .15f;
                                    fixture.Pier.RefreshPierCapSeating(); VerifyCase(fixture, " displaced carrier and pier bodies");
                                }
                                if (passed && pierName == "Concrete Pier" && beamName == "ConcreteBeam" && grade == grades[5]) Capture(fixture, report);
                            }
                }
            report.Insert(0, failures == 0 ? "PASS\n" : "FAILED\n");
            report.AppendLine($"{cases - failures}/{cases} detached actual-mesh support cases passed across normal, 16 m and 19 m widths, both roads, all piers and all paired rigid beams.");
        }
        catch (Exception error) { report.Insert(0, "FAILED\n"); report.AppendLine(error.ToString()); }
        finally
        {
            RenderTexture.active = previous;
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            try
            {
                Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene() == active && Selection.objects.SequenceEqual(selection), "Live scene setup or selection changed.");
                for (int i = 0; i < scenes.Length; i++)
                    Require(SceneManager.GetSceneAt(i) == scenes[i] && scenes[i].isLoaded == loaded[i] && scenes[i].isDirty == dirty[i] &&
                        (!loaded[i] || scenes[i].GetRootGameObjects().SequenceEqual(roots[i])), "A live scene changed.");
                Require(hashes.All(p => Hash(p.Key) == p.Value), "A saved scene or authored source asset changed.");
                Require(Point.AllPoints.SequenceEqual(registry) && PlayerDataManager.Instance == manager &&
                    (data == null || ReferenceEquals(data, manager.CurrentData) && JsonUtility.ToJson(data) == json), "Live point registry or player data changed.");
                report.AppendLine("PASS: live scenes, dirty flags, saved files, source assets, player data and point registry preserved. No scene saved or simulation run.");
            }
            catch (Exception error) { report.Insert(0, "FAILED SAFETY\n"); report.AppendLine(error.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, report.ToString()); running = false;
        }

        bool VerifyCase(Fixture fixture, string suffix = "")
        {
            cases++;
            var details = new StringBuilder();
            try { Verify(fixture, details, suffix); report.Append(details); return true; }
            catch (Exception error)
            {
                failures++;
                if (failures <= 8) report.Append(details);
                report.AppendLine("FAIL: " + fixture.Label + suffix + " — " + error.Message);
                if (failureCaptures < 3 && (fixture.Pier.materialData.name == "Concrete Pier" || failureCaptures == 0))
                {
                    failureCaptures++;
                    try { Capture(fixture, report, "-contact-failure-" + failureCaptures); }
                    catch (Exception captureError) { report.AppendLine("Preview capture failed: " + captureError.Message); }
                }
                return false;
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root;
        public readonly ContractSO Contract;
        public readonly BuildLocation Site;
        public readonly Point Top, Bottom, Left, Right;
        public readonly Bar Pier;
        public readonly List<Bar> Carriers = new List<Bar>();
        public readonly string Label;
        public Fixture(Scene scene, BridgeMaterialSO pier, BridgeMaterialSO road, BridgeMaterialSO beam, float width, Vector2 grade)
        {
            Label = pier.name + "/" + (road != null ? road.name + "+" : "beam-only-") + beam.name + "/" + width.ToString("0") + "m/" + grade.x + "-to-" + grade.y;
            Root = Owned(scene, "Detached " + Label); Site = Root.AddComponent<BuildLocation>(); Site.enabled = false;
            Contract = ScriptableObject.CreateInstance<ContractSO>(); Contract.twoWayRoad = width > 0f; Contract.twoWayRoadWidth = width > 0f ? width : 16f;
            Site.activeContract = Contract;
            Top = Node("Top", Vector3.zero); Bottom = Node("Foundation", Vector3.down * 12f);
            Left = Node("Left", new Vector3(-8f, -8f * Mathf.Tan(grade.x * Mathf.Deg2Rad), 0f));
            Right = Node("Right", new Vector3(8f, 8f * Mathf.Tan(grade.y * Mathf.Deg2Rad), 0f));
            Pier = Member(pier, Bottom, Top);
            if (road != null) { Carriers.Add(Member(road, Left, Top)); Carriers.Add(Member(road, Top, Right)); }
            Carriers.Add(Member(beam, Left, Top)); Carriers.Add(Member(beam, Top, Right));
            Pier.RefreshPierCapSeating();
        }
        private Point Node(string name, Vector3 position)
        {
            GameObject item = Owned(Root.scene, name); item.transform.SetParent(Root.transform, false); item.transform.position = position;
            Point point = item.AddComponent<Point>(); point.enabled = false; point.Runtime = true; point.AssignOwner(Site); return point;
        }
        private Bar Member(BridgeMaterialSO material, Point a, Point b)
        {
            GameObject item = Owned(Root.scene, material.name); item.transform.SetParent(Root.transform, false);
            Bar bar = item.AddComponent<Bar>(); bar.enabled = false; bar.AssignOwner(Site); bar.startPoint = a; bar.endPoint = b;
            bar.StartPosition = a.transform.position; bar.Initialize(material); bar.NormalizeEndpointOrder();
            foreach (MonoBehaviour behaviour in item.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            return bar;
        }
        public void Dispose() { Object.DestroyImmediate(Root); Object.DestroyImmediate(Contract); }
    }

    private sealed class ActualMesh
    {
        public readonly string Name;
        public readonly Bar Carrier;
        public readonly Vector3[] Vertices;
        public readonly int[] Triangles;
        public readonly Bounds Bounds;
        public readonly Bounds MemberBounds;
        public ActualMesh(MeshFilter filter, Bar carrier)
        {
            Name = filter.name + "/" + filter.sharedMesh.name;
            Carrier = carrier;
            Vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray(); Triangles = filter.sharedMesh.triangles;
            Bounds value = new Bounds(Vertices[0], Vector3.zero); foreach (Vector3 v in Vertices) value.Encapsulate(v); Bounds = value;
            if (carrier == null) return;
            Transform frame = carrier.transform;
            var local = new Bounds(frame.InverseTransformPoint(Vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in Vertices) local.Encapsulate(frame.InverseTransformPoint(vertex));
            MemberBounds = local;
        }

        public bool TryBottomPlane(float x, float z, out float y)
        {
            Transform frame = Carrier.transform;
            Vector3 normal = frame.up;
            Vector3 bottom = frame.TransformPoint(new Vector3(0f, MemberBounds.min.y, 0f));
            y = bottom.y - (normal.x * (x - bottom.x) + normal.z * (z - bottom.z)) / normal.y;
            Vector3 local = frame.InverseTransformPoint(new Vector3(x, y, z));
            return local.x >= MemberBounds.min.x - .00001f && local.x <= MemberBounds.max.x + .00001f &&
                local.z >= MemberBounds.min.z - .00001f && local.z <= MemberBounds.max.z + .00001f;
        }

        public bool TryLowerEnvelope(float x, float z, out float entry)
        {
            Transform frame = Carrier.transform;
            Vector3 origin = frame.InverseTransformPoint(new Vector3(x, 0f, z));
            Vector3 direction = frame.InverseTransformVector(Vector3.up);
            Vector3 min = MemberBounds.min, max = MemberBounds.max;
            entry = float.NegativeInfinity;
            float exit = float.PositiveInfinity;
            // A vertical line first meets the local box through either its
            // bottom or a cut end. Actual triangles can sit above this envelope.
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < .0000005f)
                {
                    if (origin[axis] < min[axis] - .0000005f || origin[axis] > max[axis] + .0000005f) return false;
                    continue;
                }
                float a = (min[axis] - origin[axis]) / direction[axis];
                float b = (max[axis] - origin[axis]) / direction[axis];
                entry = Mathf.Max(entry, Mathf.Min(a, b));
                exit = Mathf.Min(exit, Mathf.Max(a, b));
                if (entry > exit + .0000005f) return false;
            }
            return !float.IsInfinity(entry);
        }
    }
    private static ActualMesh[] Meshes(Transform root, Bar carrier = null) => root.GetComponentsInChildren<MeshFilter>(true)
        .Where(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 0 && f.gameObject.activeInHierarchy && f.GetComponent<Renderer>() != null && f.GetComponent<Renderer>().enabled)
        .Select(f => new ActualMesh(f, carrier)).ToArray();

    private static void Verify(Fixture fixture, StringBuilder report, string suffix = "")
    {
        Point[] nodes = { fixture.Top, fixture.Bottom, fixture.Left, fixture.Right };
        Vector3[] positions = nodes.Select(n => n.transform.position).ToArray();
        string material = JsonUtility.ToJson(fixture.Pier.materialData); float cost = fixture.Pier.GetCost(), mass = fixture.Pier.PlacedMassPerMeter;
        Transform cap = fixture.Pier.transform.Find("PierCap");
        Transform shaft = fixture.Pier.transform.Cast<Transform>().First(t => t.name.StartsWith("VisualSegment", StringComparison.Ordinal));
        float originalBase = Meshes(shaft).Min(m => m.Bounds.min.y);
        BoxCollider rootCollision = fixture.Pier.GetComponent<BoxCollider>();
        if (rootCollision == null)
        {
            rootCollision = fixture.Pier.gameObject.AddComponent<BoxCollider>(); rootCollision.center = new Vector3(.1f, .2f, .3f); rootCollision.size = new Vector3(.5f, 7f, .7f);
        }
        Vector3 collisionCenter = rootCollision.center, collisionSize = rootCollision.size; Bounds collisionBounds = rootCollision.bounds;
        bool collisionEnabled = rootCollision.enabled, collisionTrigger = rootCollision.isTrigger; PhysicMaterial collisionMaterial = rootCollision.sharedMaterial;
        Vector3 rootPosition = fixture.Pier.transform.position; Quaternion rootRotation = fixture.Pier.transform.rotation;
        fixture.Pier.RefreshPierCapSeating();
        Require(nodes.Select(n => n.transform.position).SequenceEqual(positions) && JsonUtility.ToJson(fixture.Pier.materialData) == material &&
            fixture.Pier.GetCost() == cost && fixture.Pier.PlacedMassPerMeter == mass && fixture.Pier.transform.position == rootPosition && fixture.Pier.transform.rotation == rootRotation &&
            rootCollision.center == collisionCenter && rootCollision.size == collisionSize && rootCollision.bounds == collisionBounds &&
            rootCollision.enabled == collisionEnabled && rootCollision.isTrigger == collisionTrigger && rootCollision.sharedMaterial == collisionMaterial,
            "Seating changed nodes, cost, mass, material, body or collision: " + fixture.Label + suffix);
        Require(Mathf.Abs(Meshes(shaft).Min(m => m.Bounds.min.y) - originalBase) <= .0003f, "Seating moved the pier foundation: " + fixture.Label);
        foreach (PierCapBearing bearing in fixture.Pier.GetComponentsInChildren<PierCapBearing>(true))
            Require(bearing.GetComponentsInChildren<Collider>(true).Length == 0, "A fitted visual bearing added a collider: " + fixture.Label);
        ActualMesh[] support = Meshes(fixture.Pier.transform);
        Transform head = cap != null ? cap : shaft;
        ActualMesh[] heads = Meshes(head); float minX = heads.Min(m => m.Bounds.min.x), maxX = heads.Max(m => m.Bounds.max.x);
        // Steel's separate visual crosshead can extend past the original shaft
        // without changing the shaft geometry used for collision.
        ActualMesh[] widthParts = cap != null ? heads : support;
        float minZ = widthParts.Min(m => m.Bounds.min.z), maxZ = widthParts.Max(m => m.Bounds.max.z);
        if (cap != null && PierCapSeatingProfiles.TryGetTopFace(fixture.Pier.materialData.pierCapPrefab, out Bounds face))
        {
            // A tapered shoulder may project past the actual top seat. Probe
            // the seat's footprint without asking its lower shoulder to touch.
            Vector3 a = cap.TransformPoint(new Vector3(face.min.x, face.max.y, face.min.z));
            Vector3 b = cap.TransformPoint(new Vector3(face.max.x, face.max.y, face.max.z));
            minX = Mathf.Min(a.x, b.x); maxX = Mathf.Max(a.x, b.x);
        }
        Require(maxZ - minZ >= BridgeWidthGeometry.GetPierSupportWidth(fixture.Pier.WideRoadWidth) - .002f,
            "The actual support mesh does not span both outboard sides: " + fixture.Label);
        int samples = 0, recesses = 0, conservativeClearances = 0, beamLeft = 0, beamRight = 0;
        float beamLeftMinX = float.PositiveInfinity, beamLeftMaxX = float.NegativeInfinity;
        float beamRightMinX = float.PositiveInfinity, beamRightMaxX = float.NegativeInfinity;
        float worstGap = 0f, worstPenetration = 0f, maximumConservativeClearance = 0f;
        (Bar carrier, ActualMesh part, float x, float z, float underside, float supportTop) gapSample = default, penetrationSample = default;
        ActualMesh[] allCarriers = fixture.Carriers.SelectMany(c => Meshes(c.transform, c)).ToArray();
        foreach (ActualMesh part in allCarriers)
        {
            float lo = Mathf.Max(minX, part.Bounds.min.x), hi = Mathf.Min(maxX, part.Bounds.max.x);
            if (hi - lo < .0001f) continue;
            int before = samples;
            foreach (float tx in new[] { .1f, .3f, .5f, .7f, .9f })
                foreach (float tz in new[] { .1f, .5f, .9f })
                    Probe(part, Mathf.Lerp(lo, hi, tx), Mathf.Lerp(part.Bounds.min.z, part.Bounds.max.z, tz));
            // Narrow plank bottoms can lie between the grid samples. Clip the
            // actual low triangles to the cap's X range and probe their interiors.
            if (samples - before < 3)
                foreach (Vector3 point in BottomTriangleSamples(part, lo, hi))
                {
                    Probe(part, point.x, point.z);
                    if (samples - before >= 3) break;
                }
        }
        float leftLength = beamLeftMaxX - beamLeftMinX, rightLength = beamRightMaxX - beamRightMinX;
        float minimumLength = (maxX - minX) * .35f;
        bool adequateCoverage = beamLeft >= 3 && beamRight >= 3 && leftLength >= minimumLength && rightLength >= minimumLength;
        (ActualMesh part, Vector3 point, float underside) interiorOverlap = default;
        float interiorPenetration = 0f;
        foreach (ActualMesh part in support)
            foreach (Vector3 point in UpwardTriangleSamples(part))
                if (Height(allCarriers, point.x, point.z, false, out float underside) && point.y - underside > interiorPenetration)
                {
                    interiorPenetration = point.y - underside;
                    interiorOverlap = (part, point, underside);
                }
        if (!adequateCoverage || worstGap > ContactTolerance || worstPenetration > ContactTolerance || interiorPenetration > ContactTolerance)
        {
            report.AppendLine("CONTACT FAILURE: " + fixture.Label + suffix);
            report.AppendLine($"  Actual contacts left/right: {beamLeft}/{beamRight}, length {leftLength:F4}/{rightLength:F4} m, minimum length {minimumLength:F4} m");
            report.AppendLine($"  Conservative end clearances: {conservativeClearances}, max {maximumConservativeClearance:F6} m");
            if (worstGap > .001f) DescribeSample("gap", gapSample);
            if (worstPenetration > .001f) DescribeSample("penetration", penetrationSample);
            if (interiorPenetration > ContactTolerance)
                report.AppendLine($"  Interior penetration: {interiorOverlap.part.Name} at {interiorOverlap.point.ToString("R")}, underside={interiorOverlap.underside:R}, overlap={interiorPenetration:F6} m");
            foreach (ActualMesh part in allCarriers)
                report.AppendLine("  carrier mesh " + part.Name + " bounds " + part.Bounds);
            foreach (ActualMesh part in support)
                report.AppendLine("  support mesh " + part.Name + " bounds " + part.Bounds);
            foreach (PierCapBearing bearing in fixture.Pier.GetComponentsInChildren<PierCapBearing>(true))
            {
                Vector3[] shape = bearing.CaptureVertices();
                report.AppendLine("  bearing active=" + bearing.gameObject.activeInHierarchy + ", prisms=" + shape.Length / 8);
                for (int i = 0; i < shape.Length; i += 8)
                    report.AppendLine("    world corners " + string.Join(", ", shape.Skip(i).Take(8).Select(v => bearing.transform.TransformPoint(v).ToString("R"))));
            }
        }
        float overlap = Mathf.Max(worstPenetration, interiorPenetration);
        Require(worstGap <= ContactTolerance && overlap <= ContactTolerance,
            $"Actual mesh contact failed: {fixture.Label}; gap={worstGap:F6} m; penetration={overlap:F6} m.");
        Require(adequateCoverage, $"Contact sampling missed a paired girder's seating length: {fixture.Label}; samples left/right={beamLeft}/{beamRight}, length={leftLength:F4}/{rightLength:F4} m, minimum={minimumLength:F4} m.");
        report.AppendLine("PASS: " + fixture.Label + suffix + "; actual contact samples=" + samples + "; paired sides=" + beamLeft + "/" + beamRight +
            "; recessed surfaces checked for clearance=" + recesses + "; max gap/penetration=" + worstGap.ToString("F6") + "/" + overlap.ToString("F6") +
            "m; conservative end clearances=" + conservativeClearances + " (max " + maximumConservativeClearance.ToString("F6") + " m)" +
            "; actual support depth=" + (maxZ - minZ).ToString("F3") + "m; nodes/rates/body/collider/base preserved.");

        void Probe(ActualMesh part, float x, float z)
        {
            if (!Height(new[] { part }, x, z, false, out float partBottom) || !Height(allCarriers, x, z, false, out float underside)) return;
            float lowestPlane = float.PositiveInfinity;
            foreach (ActualMesh carrier in allCarriers)
                if (carrier.TryBottomPlane(x, z, out float y)) lowestPlane = Mathf.Min(lowestPlane, y);
            // A genuine downward triangle must reach the lowest bearing plane.
            // Voids between wood planks and higher decorative faces can remain open.
            bool contact = Mathf.Abs(underside - lowestPlane) <= ContactTolerance;
            if (!Height(support, x, z, true, out float actual))
            {
                Require(!contact, "No actual pier surface below a bearing face at X/Z=" + x + "/" + z + ": " + fixture.Label);
                return;
            }
            float difference = underside - actual;
            if (-difference > worstPenetration) { worstPenetration = -difference; penetrationSample = (part.Carrier, part, x, z, underside, actual); }
            if (!contact || Mathf.Abs(partBottom - underside) > ContactTolerance) { recesses++; return; }
            if (difference > ContactTolerance)
            {
                float envelope = float.PositiveInfinity;
                foreach (ActualMesh carrier in allCarriers)
                    if (carrier.TryLowerEnvelope(x, z, out float y)) envelope = Mathf.Min(envelope, y);
                // Count a conservative cut-end pocket only when the support
                // really reaches that envelope. It cannot count as girder contact.
                if (underside - envelope > ContactTolerance && Mathf.Abs(actual - envelope) <= ContactTolerance)
                {
                    conservativeClearances++;
                    maximumConservativeClearance = Mathf.Max(maximumConservativeClearance, difference);
                    return;
                }
            }
            if (difference > worstGap) { worstGap = difference; gapSample = (part.Carrier, part, x, z, underside, actual); }
            samples++;
            if (!part.Carrier.materialData.isDualBeam) return;
            if (z < -.05f) { beamLeft++; beamLeftMinX = Mathf.Min(beamLeftMinX, x); beamLeftMaxX = Mathf.Max(beamLeftMaxX, x); }
            if (z > .05f) { beamRight++; beamRightMinX = Mathf.Min(beamRightMinX, x); beamRightMaxX = Mathf.Max(beamRightMaxX, x); }
        }

        void DescribeSample(string kind, (Bar carrier, ActualMesh part, float x, float z, float underside, float supportTop) sample)
        {
            report.AppendLine($"  Worst {kind}: {sample.carrier.materialData.name}/{sample.part.Name}, X/Z={sample.x:R}/{sample.z:R}, underside/support top={sample.underside:R}/{sample.supportTop:R}");
            foreach (ActualMesh part in allCarriers)
                if (Height(new[] { part }, sample.x, sample.z, false, out float y)) report.AppendLine($"    underside {part.Name} y={y:R}");
            foreach (ActualMesh part in support)
                if (Height(new[] { part }, sample.x, sample.z, true, out float y)) report.AppendLine($"    support top {part.Name} y={y:R}");
        }
    }

    private static IEnumerable<Vector3> BottomTriangleSamples(ActualMesh part, float minX, float maxX)
    {
        Transform frame = part.Carrier.transform;
        float bottom = part.MemberBounds.min.y;
        for (int i = 0; i < part.Triangles.Length; i += 3)
        {
            Vector3 a = part.Vertices[part.Triangles[i]], b = part.Vertices[part.Triangles[i + 1]], c = part.Vertices[part.Triangles[i + 2]];
            if (Vector3.Cross(b - a, c - a).y >= -.000001f ||
                Mathf.Abs(frame.InverseTransformPoint(a).y - bottom) > ContactTolerance ||
                Mathf.Abs(frame.InverseTransformPoint(b).y - bottom) > ContactTolerance ||
                Mathf.Abs(frame.InverseTransformPoint(c).y - bottom) > ContactTolerance) continue;
            List<Vector3> polygon = ClipX(new List<Vector3> { a, b, c }, minX, true);
            polygon = ClipX(polygon, maxX, false);
            if (polygon.Count < 3) continue;
            Vector3 center = Vector3.zero;
            foreach (Vector3 point in polygon) center += point;
            center /= polygon.Count;
            if (center.x <= minX + .00001f || center.x >= maxX - .00001f) continue;
            yield return center;
            foreach (Vector3 point in polygon)
                yield return Vector3.Lerp(center, point, .55f);
        }
    }

    private static IEnumerable<Vector3> UpwardTriangleSamples(ActualMesh mesh)
    {
        for (int i = 0; i < mesh.Triangles.Length; i += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Triangles[i]], b = mesh.Vertices[mesh.Triangles[i + 1]], c = mesh.Vertices[mesh.Triangles[i + 2]];
            if (Vector3.Cross(b - a, c - a).y <= .000001f) continue;
            // A vertex shared by two adjacent support plates can legitimately
            // sit above the next plate's seat. Test the rendered interior on
            // each side of that boundary to detect actual overlapping surfaces.
            Vector3 center = (a + b + c) / 3f;
            yield return center;
            yield return Vector3.Lerp(center, a, .98f);
            yield return Vector3.Lerp(center, b, .98f);
            yield return Vector3.Lerp(center, c, .98f);
        }
    }

    private static List<Vector3> ClipX(List<Vector3> polygon, float edge, bool keepGreater)
    {
        var clipped = new List<Vector3>();
        if (polygon.Count == 0) return clipped;
        Vector3 before = polygon[polygon.Count - 1];
        bool wasInside = keepGreater ? before.x >= edge : before.x <= edge;
        foreach (Vector3 after in polygon)
        {
            bool inside = keepGreater ? after.x >= edge : after.x <= edge;
            if (inside != wasInside) clipped.Add(Vector3.LerpUnclamped(before, after, (edge - before.x) / (after.x - before.x)));
            if (inside) clipped.Add(after);
            before = after;
            wasInside = inside;
        }
        return clipped;
    }

    private static bool Height(IEnumerable<ActualMesh> meshes, float x, float z, bool top, out float height)
    {
        height = top ? float.NegativeInfinity : float.PositiveInfinity;
        foreach (ActualMesh mesh in meshes)
        {
            if (x < mesh.Bounds.min.x - .000001f || x > mesh.Bounds.max.x + .000001f || z < mesh.Bounds.min.z - .000001f || z > mesh.Bounds.max.z + .000001f) continue;
            for (int i = 0; i < mesh.Triangles.Length; i += 3)
            {
                Vector3 a = mesh.Vertices[mesh.Triangles[i]], b = mesh.Vertices[mesh.Triangles[i + 1]], c = mesh.Vertices[mesh.Triangles[i + 2]];
                float normalY = Vector3.Cross(b - a, c - a).y;
                if (top ? normalY < .000001f : normalY > -.000001f) continue;
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < .000001f) continue;
                float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                float w = 1f - u - v;
                if (u < -.000001f || v < -.000001f || w < -.000001f) continue;
                float y = u * a.y + v * b.y + w * c.y;
                height = top ? Mathf.Max(height, y) : Mathf.Min(height, y);
            }
        }
        return !float.IsInfinity(height);
    }

    private static void Capture(Fixture fixture, StringBuilder report, string suffix = "")
    {
        GameObject cameraObject = Owned(fixture.Root.scene, "Detached support close-up camera"); Camera camera = cameraObject.AddComponent<Camera>();
        GameObject lightObject = Owned(fixture.Root.scene, "Detached support close-up light"); Light light = lightObject.AddComponent<Light>();
        camera.scene = fixture.Root.scene; camera.useOcclusionCulling = false; camera.orthographic = true; camera.orthographicSize = 5.2f;
        camera.nearClipPlane = .1f; camera.farClipPlane = 100f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(87, 121, 157, 255);
        light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        RenderTexture previous = RenderTexture.active; var target = new RenderTexture(1280, 800, 24); target.Create(); camera.targetTexture = target;
        try
        {
            string stem = fixture.Label.ToLowerInvariant().Replace(' ', '-').Replace('/', '_').Replace('+', '-') + suffix; Directory.CreateDirectory("Temp/PierSupportVisual");
            for (int view = 0; view < 3; view++)
            {
                string label = view == 0 ? "oblique" : view == 1 ? "underside" : "front";
                camera.transform.position = view == 0 ? new Vector3(11f, 3.5f, -27f) : view == 1 ? new Vector3(9f, -5f, -27f) : new Vector3(0f, -1f, -30f);
                camera.transform.LookAt(new Vector3(0f, -1f, 0f));
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
                else UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                try { image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0); image.Apply(); string path = "Temp/PierSupportVisual/" + stem + "-" + label + ".png"; File.WriteAllBytes(path, image.EncodeToPNG()); report.AppendLine("PREVIEW: " + path); }
                finally { Object.DestroyImmediate(image); }
            }
        }
        finally { RenderTexture.active = previous; camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); }
    }
    private static GameObject Owned(Scene scene, string name) { var item = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave); SceneManager.MoveGameObjectToScene(item, scene); return item; }
    private static string Hash(string path) { using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
