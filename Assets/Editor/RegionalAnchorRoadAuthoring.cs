#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class RegionalAnchorRoadAuthoring
{
    private const string Request = "Temp/regional-bank-road-final.request";
    private const string Report = "Logs/regional-anchor-road.txt";
    static RegionalAnchorRoadAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if (command == "refresh") { AssetDatabase.Refresh(); return; } if (command == "inspect") Inspect(); else if (command == "apply") Apply(); else if (command == "validate") Validate(); else if (command == "previews") Previews(); else throw new InvalidOperationException("Use inspect, apply, validate or previews."); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); }
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static string PathOf(Transform value) => value == null ? "none" : value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
    private static Scene Canyon()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Open CanyonCrossing in Edit Mode.");
        return scene;
    }
    private static bool Ground(Collider col, Scene scene) => col.gameObject.scene == scene && col.enabled && col.gameObject.activeInHierarchy && !col.isTrigger && col.attachedRigidbody == null && col.GetComponentInParent<Point>() == null && col.GetComponentInParent<Bar>() == null && col.GetComponentInParent<LiveLoadVehicle>() == null && !PathOf(col.transform).Contains("/GeneratedSmoothApproaches/");

    private sealed class Join
    {
        internal BuildLocation site;
        internal Point anchor;
        internal MeshCollider bank;
        internal float land, halfWidth, extension;
        internal float[] lanes;
    }
    private static Transform Exact(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        var root = scene.GetRootGameObjects().Single(value => value.name == parts[0]);
        var result = parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts.Skip(1)));
        Require(result != null, "Missing audited object: " + path); return result;
    }
    private static List<Join> Joins(Scene scene)
    {
        var result = new List<Join>();
        var roads = new[] { "Wood Road", "Concrete Road" }.Select(name => AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/" + name + ".asset")).ToArray();
        Require(roads.All(road => road != null), "Road material reference is missing.");
        float originalHalf = roads.Max(road => BridgeWidthGeometry.GetPrefabBounds(road.segmentPrefab).size.z) * .5f;
        void Add(string root, string anchor, string bank, float land)
        {
            string prefix = "BridgeCreator/" + root;
            var site = Exact(scene, prefix).GetComponentsInChildren<BuildLocation>(true).Single();
            var point = Exact(scene, prefix + "/" + anchor).GetComponent<Point>();
            Require(point != null && site.startingAnchors.Concat(site.endingAnchors).Contains(point), "Incorrect road anchor binding: " + anchor);
            var collider = Exact(scene, bank.StartsWith("@") ? bank.Substring(1) : prefix + "/" + bank).GetComponent<MeshCollider>();
            Require(collider != null && Ground(collider, scene) && collider.sharedMesh != null && collider.GetComponent<MeshFilter>()?.sharedMesh == collider.sharedMesh, "Use the exact visible bank mesh and its matching collider: " + bank);
            var contracts = Contracts(scene, site);
            float wide = contracts.Select(contract => contract.GetWideRoadWidth()).DefaultIfEmpty(0f).Max();
            var allowedRoads = contracts.SelectMany(contract => contract.allowedMaterials ?? new List<MaterialAllowance>()).Where(allowance => allowance?.material != null && allowance.material.isRoad).Select(allowance => allowance.material).Distinct().ToArray();
            float half = allowedRoads.Length > 0 && contracts.All(contract => contract.allowedMaterials != null && contract.allowedMaterials.Count > 0)
                ? allowedRoads.Max(road => BridgeWidthGeometry.GetPrefabBounds(road.segmentPrefab).size.z) * .5f : originalHalf;
            var join = new Join { site = site, anchor = point, bank = collider, land = land, halfWidth = Mathf.Max(half, wide * .5f) };
            var lanes = new List<float> { -join.halfWidth + .3f, -2f, 0f, 2f, join.halfWidth - .3f };
            foreach (var vehicle in Vehicles(scene, site))
            {
                vehicle.GetMultiLoadRoutePoses(out Vector3 start, out Vector3 end);
                foreach (var wheel in vehicle.wheelObjects.Where(wheel => wheel != null))
                {
                    float z = WheelCenter(wheel).z;
                    lanes.Add(z - point.transform.position.z);
                    lanes.Add(z + end.z - start.z - point.transform.position.z);
                }
            }
            join.lanes = lanes.Where(lane => Mathf.Abs(lane) <= join.halfWidth - .2f).Select(lane => Mathf.Round(lane * 1000f) / 1000f).Distinct().OrderBy(lane => lane).ToArray();
            result.Add(join);
        }
        void Standard(string root)
        {
            Add(root, "CanyonPillarBuildSite/Anchors/LeftAnchor", "CanyonPillarBuildSite/Banks/LeftBank/Canyon_Pillar3 (1)", -1);
            string prefix = "BridgeCreator/" + root + "/CanyonPillarBuildSite/";
            string right = Exact(scene, prefix + "Banks/RightBank").Find("RightAnchor") != null ? "Banks/RightBank/RightAnchor" : "Anchors/RightAnchor";
            Add(root, "CanyonPillarBuildSite/" + right, "CanyonPillarBuildSite/Banks/RightBank/Canyon_Pillar3 (1)", 1);
        }
        Add("BHAN CONTRACT BEAM", "Anchors/Point 1", "@Environment/Cube", -1);
        Add("BHAN CONTRACT BEAM", "Anchors/Point 1 (1)", "@Environment/Canyon_Scenery_Cluster/Cube (1)", 1);
        Add("BHAN CONTRACT TRUSS", "Anchors/Point 1", "@Environment/MainCanyons/Medium_Rock_Form (2)", -1);
        Add("BHAN CONTRACT TRUSS", "Anchors/Point 1 (1)", "@Environment/MainCanyons/pillar/I_pillar_v7", 1);
        Add("BHAN CONTRACT TRUSS", "Anchors/Point 1 (3)", "@Environment/MainCanyons/pillar/I_pillar_v7", -1);
        Add("BHAN CONTRACT TRUSS", "Anchors/Point 1 (2)", "@Environment/MainCanyons/Small_Rock_form_3", 1);
        Add("BHAN CONTRACT SHOPKEEPER", "Anchors/Point 1", "LEVEL3SCENE/Small_Rock_form_3", -1);
        Add("BHAN CONTRACT SHOPKEEPER", "Anchors/Point 1 (1)", "LEVEL3SCENE/Small_Rock_form_3 (1)", 1);
        Add("REED MAIN CONTRACT", "Anchors/Point 1", "RAVINE/LeftBank/Canyon_Pillar3 (1)", -1);
        Add("REED MAIN CONTRACT", "Anchors/Point 1 (1)", "RAVINE/RightBank/Canyon_Pillar3 (2)", 1);
        foreach (string root in new[] { "ReedSideQuest", "VanceSideQuest", "SilasSideQuest", "SilasSideQuest1" })
        {
            Add(root, "Anchors/Point 1", "Level6Scene/LeftRavine/Canyon_Pillar3 (1)", -1);
            Add(root, "Anchors/Point 1 (1)", "Level6Scene/RightRavine/Canyon_Pillar3 (2)", 1);
        }
        foreach (string root in new[] { "VANCE MAIN CONTRACT", "SILAS MAIN CONTRACT", "SILAS MAIN CONTRACT 2", "MarshMainContract", "FordMainContract", "FordSideContract", "BrookMainContract" }) Standard(root);
        Require(result.Count == 32 && result.Select(join => join.site).Distinct().Count() == 15, "Expected fifteen audited sites and thirty-two road joins.");
        return result;
    }
    private static ContractSO[] Contracts(Scene scene, BuildLocation site) => All<NPCProgressionManager>(scene).SelectMany(npc => npc.AuthoredPhases).Where(phase => phase.targetBuildLocation == site && phase.contract != null).Select(phase => phase.contract).Concat(site.activeContract != null ? new[] { site.activeContract } : Array.Empty<ContractSO>()).Distinct().ToArray();
    private static LiveLoadVehicle[] Vehicles(Scene scene, BuildLocation site) { var contracts = Contracts(scene, site); return All<LiveLoadVehicle>(scene).Where(vehicle => vehicle.assignedContract != null && contracts.Contains(vehicle.assignedContract)).ToArray(); }
    private static Vector3 WheelCenter(GameObject wheel) => wheel.GetComponentInChildren<Renderer>(true)?.bounds.center ?? wheel.transform.position;
    private static bool Sample(Join join, float landDistance, float lane, out RaycastHit hit)
    {
        Vector3 point = join.anchor.transform.position + new Vector3(join.land * landDistance, 0f, lane);
        return join.bank.Raycast(new Ray(point + Vector3.up * 2f, Vector3.down), out hit, 4f) && hit.normal.y > .5f;
    }
    private static void Measure(List<Join> joins, StringBuilder report)
    {
        // Always measure the untouched source, making repeated authoring stable.
        Scene preview = EditorSceneManager.NewPreviewScene();
        var banks = joins.ToDictionary(join => join, join => join.bank);
        try
        {
        foreach (var group in joins.GroupBy(join => join.bank).ToArray())
        {
            var source = group.Key; var proxy = new GameObject("Original bank measurement"); SceneManager.MoveGameObjectToScene(proxy, preview);
            proxy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation); proxy.transform.localScale = source.transform.lossyScale;
            var collider = proxy.AddComponent<MeshCollider>(); collider.sharedMesh = Original(source.sharedMesh);
            foreach (var join in group) join.bank = collider;
        }
        Physics.SyncTransforms();
        foreach (Join join in joins)
        {
            float edge = -3f, top = join.anchor.transform.position.y + .025f;
            foreach (float lane in join.lanes)
            {
                Require(Sample(join, 3f, lane, out RaycastHit hit) && Mathf.Abs(hit.point.y - top) < 1.4f, "No audited near-level bank surface three metres inland: " + PathOf(join.anchor.transform) + " lane=" + lane + " hit=" + hit.point.ToString("F5"));
                float outside = -3f, inside = 3f;
                if (!Sample(join, outside, lane, out _))
                    for (int i = 0; i < 19; i++) { float middle = (inside + outside) * .5f; if (Sample(join, middle, lane, out _)) inside = middle; else outside = middle; }
                else inside = outside;
                edge = Mathf.Max(edge, inside);
            }
            join.extension = Mathf.Max(0f, edge + .2f);
            Require(join.extension < 1.6f, "Bank edge is outside the audited correction range: " + PathOf(join.anchor.transform) + " extension=" + join.extension);
            report.AppendLine($"MEASURE {PathOf(join.anchor.transform)}: target surface Y={top:F5}; local lip extension={join.extension:F4}m; {join.lanes.Length} lane samples.");
        }
        }
        finally { foreach (var pair in banks) pair.Key.bank = pair.Value; EditorSceneManager.ClosePreviewScene(preview); }
    }
    private static void Mark(UnityEngine.Object value) { EditorUtility.SetDirty(value); if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
    private const string MeshFolder = "Assets/BridgeBuilder/Generated/RegionalBankJoins";
    private static Mesh Original(Mesh mesh)
    {
        if (!AssetDatabase.GetAssetPath(mesh).StartsWith(MeshFolder, StringComparison.Ordinal)) return mesh;
        string label = AssetDatabase.GetLabels(mesh).Single(value => value.StartsWith("BankSource-", StringComparison.Ordinal));
        string sourceId = label.Substring("BankSource-".Length); string path = AssetDatabase.GUIDToAssetPath(sourceId.Substring(0, 32)); long id = long.Parse(sourceId.Substring(33));
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Single(value => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string _, out long local) && local == id);
    }
    private static string MeshPath(MeshCollider bank)
    {
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return MeshFolder + "/Bank_" + string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(PathOf(bank.transform))).Take(8).Select(value => value.ToString("x2"))) + ".asset";
    }
    private static float? Surface(Scene scene, Vector3 point)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore).Where(hit => Ground(hit.collider, scene) && hit.normal.y > .5f).OrderByDescending(hit => hit.point.y).ToArray();
        return hits.Length == 0 ? (float?)null : hits[0].point.y;
    }
    private sealed class VehicleProbe
    {
        internal LiveLoadVehicle vehicle;
        internal Vector3[] start, end;
        internal float?[] oldStart, oldEnd;
    }
    [MenuItem("Tools/Civil Craft/Build Sites/Canyon and Town River/Align Road Joins")]
    public static void Apply()
    {
        Scene scene = Canyon(); bool wasDirty = scene.isDirty; var report = new StringBuilder(); var joins = Joins(scene); Measure(joins, report);
        Require(PrefabStageUtility.GetCurrentPrefabStage() == null, "Leave Prefab Mode first.");
        var points = All<Point>(scene).ToDictionary(point => point, point => point.transform.position);
        var ghosts = All<GhostSegment>(scene).ToDictionary(ghost => ghost, ghost => (ghost.startPos, ghost.endPos));
        var vehicles = joins.SelectMany(join => Vehicles(scene, join.site)).Distinct().Select(vehicle =>
        {
            vehicle.GetMultiLoadRoutePoses(out Vector3 start, out Vector3 end);
            var wheelPositions = vehicle.wheelObjects.Where(wheel => wheel != null).Select(WheelCenter).ToArray();
            var endPositions = wheelPositions.Select(point => point + end - start).ToArray();
            return new VehicleProbe { vehicle = vehicle, start = wheelPositions, end = endPositions, oldStart = wheelPositions.Select(point => Surface(scene, point)).ToArray(), oldEnd = endPositions.Select(point => Surface(scene, point)).ToArray() };
        }).ToArray();
        string backup = "LocalBackups/RegionalJoins-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); Directory.CreateDirectory(backup); File.Copy(scene.path, backup + "/CanyonCrossing.unity");
        var meshes = new Dictionary<MeshCollider, Mesh>(); var originals = new Dictionary<MeshCollider, Mesh>();
        Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Fit Canyon and Town River bank surfaces");
        try
        {
            var geometryJoins = new List<Join>(joins);
            var beamLeft = joins.Single(join => PathOf(join.anchor.transform) == "BridgeCreator/BHAN CONTRACT BEAM/Anchors/Point 1");
            var overlappingRock = Exact(scene, "Environment/MainCanyons/Medium_Rock_Form").GetComponent<MeshCollider>();
            Require(overlappingRock != null && Ground(overlappingRock, scene) && overlappingRock.GetComponent<MeshFilter>()?.sharedMesh == overlappingRock.sharedMesh, "Bhan's overlapping bank rock changed.");
            geometryJoins.Add(new Join { site = beamLeft.site, anchor = beamLeft.anchor, bank = overlappingRock, halfWidth = beamLeft.halfWidth, land = beamLeft.land });
            foreach (var group in geometryJoins.GroupBy(join => join.bank))
            {
                Mesh original = Original(group.Key.sharedMesh); originals[group.Key] = original;
                Mesh fitted = RegionalBankMeshFitter.Build(original, group.Key.transform.localToWorldMatrix, group.Select(join => new RegionalBankMeshFitter.Join { anchor = join.anchor.transform.position, landDirection = join.land, halfWidth = join.halfWidth, extension = join.extension, preserveUpperSides = PathOf(join.bank.transform).StartsWith("BridgeCreator/BHAN CONTRACT SHOPKEEPER/", StringComparison.Ordinal) }).ToArray());
                meshes.Add(group.Key, fitted);
                Undo.RecordObjects(new UnityEngine.Object[] { group.Key, group.Key.GetComponent<MeshFilter>() }, "Fit visible bank and physical surface");
                group.Key.sharedMesh = fitted; group.Key.GetComponent<MeshFilter>().sharedMesh = fitted; Mark(group.Key); Mark(group.Key.GetComponent<MeshFilter>());
            }
            foreach (var site in joins.Select(join => join.site).Distinct())
            {
                var legacy = site.transform.Find("GeneratedSmoothApproaches"); if (legacy != null) Undo.DestroyObjectImmediate(legacy.gameObject);
                var aligner = site.GetComponent<BridgeAbutmentAligner>(); Require(aligner != null, "Missing existing abutment helper at " + site.name);
                Undo.RecordObject(aligner, "Keep exact road endpoint bank references");
                var data = new SerializedObject(aligner); var sides = joins.Where(join => join.site == site).ToArray();
                data.FindProperty("referenceAnchor").objectReferenceValue = sides[0].anchor; data.FindProperty("buildLocation").objectReferenceValue = site;
                data.FindProperty("roadSurfaceAboveAnchor").floatValue = .025f;
                var list = data.FindProperty("abutments"); list.arraySize = sides.Length;
                for (int i = 0; i < sides.Length; i++)
                {
                    var entry = list.GetArrayElementAtIndex(i); var side = sides[i];
                    entry.FindPropertyRelative("anchor").objectReferenceValue = side.anchor; entry.FindPropertyRelative("approachCollider").objectReferenceValue = side.bank;
                    entry.FindPropertyRelative("movableRoot").objectReferenceValue = side.bank.transform;
                    entry.FindPropertyRelative("surfaceProbeOffset").vector3Value = Vector3.right * side.land * .3f;
                    entry.FindPropertyRelative("additionalSurfaceOffset").floatValue = side.anchor.transform.position.y - sides[0].anchor.transform.position.y;
                }
                data.ApplyModifiedProperties(); Mark(aligner);
            }
            Physics.SyncTransforms(); AdjustVehicleHeights(scene, vehicles, report);
            Check(scene, joins, report, true);
            foreach (var point in points) Require(Vector3.Distance(point.Key.transform.position, point.Value) < .00001f, "An anchor or structural point moved.");
            foreach (var ghost in ghosts) Require(ghost.Key.startPos == ghost.Value.startPos && ghost.Key.endPos == ghost.Value.endPos, "A tutorial blueprint endpoint moved.");
            Directory.CreateDirectory(MeshFolder); AssetDatabase.Refresh();
            foreach (var pair in meshes)
            {
                string path = MeshPath(pair.Key); var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved == null) { AssetDatabase.CreateAsset(pair.Value, path); saved = pair.Value; }
                else { Require(!EditorUtility.IsDirty(saved), "Save existing bank asset edits before regenerating."); File.Copy(path, backup + "/" + Path.GetFileName(path)); Undo.RegisterCompleteObjectUndo(saved, "Update fitted bank surface"); RegionalBankMeshFitter.ReplaceMesh(saved, pair.Value); }
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(originals[pair.Key], out string guid, out long id);
                AssetDatabase.SetLabels(saved, new[] { "BankSource-" + guid + "-" + id }); EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved);
                pair.Key.sharedMesh = saved; pair.Key.GetComponent<MeshFilter>().sharedMesh = saved; Mark(pair.Key); Mark(pair.Key.GetComponent<MeshFilter>());
                if (saved != pair.Value) UnityEngine.Object.DestroyImmediate(pair.Value);
            }
            Undo.FlushUndoRecordObjects(); EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(undo);
            bool savedScene = !wasDirty && EditorSceneManager.SaveScene(scene);
            report.AppendLine("PASS: every anchor, support/tower point, ghost, contract, span and route X/Z preserved. No player saves or material values changed.");
            report.AppendLine(savedScene ? "SAVED: CanyonCrossing" : "NEEDS_SAVE: existing scene edits remain unsaved."); report.AppendLine("Backup: " + backup); File.WriteAllText(Report, report.ToString());
        }
        catch
        {
            Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(undo);
            foreach (var mesh in meshes.Values) if (mesh != null && !AssetDatabase.Contains(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
            throw;
        }
    }
    private static void AdjustVehicleHeights(Scene scene, VehicleProbe[] probes, StringBuilder report)
    {
        var shifts = new Dictionary<Transform, float>();
        void Add(Transform target, float delta) { if (target == null || Mathf.Abs(delta) < .001f) return; Require(!shifts.TryGetValue(target, out float old) || Mathf.Abs(old - delta) < .002f, "Shared vehicle marker needs conflicting corrections."); shifts[target] = delta; }
        float Change(Vector3[] positions, float?[] old)
        {
            var delta = positions.Select((point, index) => (before: old[index], after: Surface(scene, point))).Where(value => value.before.HasValue && value.after.HasValue).Select(value => value.after.Value - value.before.Value).ToArray();
            return delta.Length == 0 ? 0f : delta.Max();
        }
        foreach (var probe in probes)
        {
            float start = Change(probe.start, probe.oldStart), end = Change(probe.end, probe.oldEnd);
            Require(Mathf.Abs(start) < .5f && Mathf.Abs(end) < .5f, "Vehicle height correction is outside the audited range.");
            Add(probe.vehicle.transform, start); Add(probe.vehicle.startPoint, start); Add(probe.vehicle.endPoint, end);
            if (Mathf.Abs(start) >= .001f || Mathf.Abs(end) >= .001f) report.AppendLine($"SEATED {PathOf(probe.vehicle.transform)}: start Y shift={start:F4}m, end marker Y shift={end:F4}m; X/Z, rotation, wheels, cargo and route bindings retained.");
        }
        // Set absolute targets after recording all poses, so nested markers cannot move twice.
        var targets = shifts.ToDictionary(pair => pair.Key, pair => pair.Key.position + Vector3.up * pair.Value);
        Undo.RecordObjects(targets.Keys.Cast<UnityEngine.Object>().ToArray(), "Keep vehicles seated on fitted approaches");
        foreach (var pair in targets.OrderBy(pair => PathOf(pair.Key).Count(character => character == '/'))) { pair.Key.position = pair.Value; Mark(pair.Key); }
        Physics.SyncTransforms();
    }
    [MenuItem("Tools/Civil Craft/Build Sites/Canyon and Town River/Validate Road Joins")]
    public static void Validate() { var scene = Canyon(); var report = new StringBuilder(); Check(scene, Joins(scene), report, true); report.AppendLine("Scene dirty=" + scene.isDirty); File.WriteAllText(Report, report.ToString()); }
    [MenuItem("Tools/Civil Craft/Build Sites/Canyon and Town River/Render Road Join Previews")]
    public static void Previews()
    {
        var scene = Canyon(); var joins = Joins(scene);
        foreach (string suffix in new[] { "REED MAIN CONTRACT/Anchors/Point 1", "BHAN CONTRACT TRUSS/Anchors/Point 1 (3)", "BrookMainContract/CanyonPillarBuildSite/Banks/RightBank/RightAnchor" })
        {
            var join = joins.Single(value => PathOf(value.anchor.transform) == "BridgeCreator/" + suffix);
            var material = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/" + (suffix.StartsWith("Brook") ? "Concrete Road" : "Wood Road") + ".asset");
            RegionalRoadJoinPreviews.Capture(join.bank, join.anchor, join.land, material, suffix.StartsWith("REED") ? "Reed" : suffix.StartsWith("BHAN") ? "TrussIsland" : "RonanRaisedBank");
        }
        File.AppendAllText(Report, "\nRendered detached before/after previews under Logs/RegionalRoadJoins.\n");
    }
    private static void Check(Scene scene, List<Join> joins, StringBuilder report, bool physics)
    {
        Physics.SyncTransforms(); var colliders = All<Collider>(scene);
        var failures = new List<string>();
        foreach (Join join in joins)
        {
            try
            {
            float top = join.anchor.transform.position.y + .025f;
            foreach (float lane in join.lanes)
                foreach (float distance in new[] { -.1f, 0f, .1f, .3f, .5f })
                {
                    Require(Sample(join, distance, lane, out RaycastHit hit) && Mathf.Abs(hit.point.y - top) < .002f && hit.normal.y > .999f, "Gap, step or tilted bank lip at " + PathOf(join.anchor.transform) + " lane=" + lane + " distance=" + distance + " hit=" + hit.point.ToString("F5") + " normal=" + hit.normal.ToString("F5") + " target=" + top);
                    var point = join.anchor.transform.position + new Vector3(join.land * distance, 0f, lane);
                    Require(Surface(scene, point) <= top + .002f, "A second world collider protrudes above the bank lip at " + PathOf(join.anchor.transform));
                }
            if (physics)
            {
                var tracks = new HashSet<string>();
                foreach (var vehicle in Vehicles(scene, join.site))
                    foreach (var wheel in vehicle.wheelObjects.Where(wheel => wheel != null))
                    {
                        float z = WheelCenter(wheel).z; string key = Mathf.RoundToInt(z * 1000) + ":" + Mathf.RoundToInt(vehicle.wheelRadius * 1000);
                        if (Mathf.Abs(z - join.anchor.transform.position.z) >= join.halfWidth - .2f || !tracks.Add(key)) continue;
                        IndustrialRoadSeamPhysicsCheck.Validate(scene, colliders, join.anchor.transform.position, top, z, join.land, vehicle.wheelRadius, report, true);
                    }
                if (tracks.Count == 0) IndustrialRoadSeamPhysicsCheck.Validate(scene, colliders, join.anchor.transform.position, top, join.anchor.transform.position.z, join.land, .4f, report, true);
            }
            report.AppendLine($"PASS {PathOf(join.anchor.transform)}: fixed anchor, visible/physical bank top error <2mm across {join.lanes.Length} lane samples.");
            }
            catch (Exception error) { string detail = PathOf(join.anchor.transform) + ": " + error.Message; failures.Add(detail); report.AppendLine("FAIL " + detail); }
        }
        if (failures.Count > 0) { File.WriteAllText("Logs/regional-anchor-road-diagnostics.txt", report.ToString()); throw new InvalidOperationException(string.Join("\n", failures)); }
        report.AppendLine("PASS all 32 joins at 15 Canyon Crossing / Town River sites, including all four Truss island endpoints and Ronan's raised opposite bank.");
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    [MenuItem("Tools/Civil Craft/Build Sites/Canyon and Town River/Inspect Road Joins")]
    public static void Inspect()
    {
        Scene scene = Canyon(); Physics.SyncTransforms(); var report = new StringBuilder("REGIONAL JOINS scene dirty=" + scene.isDirty + "\n");
        var phases = All<NPCProgressionManager>(scene).SelectMany(npc => npc.AuthoredPhases).ToArray();
        var colliders = All<Collider>(scene).Where(col => Ground(col, scene)).ToArray();
        foreach (var site in All<BuildLocation>(scene).OrderBy(site => site.transform.position.x))
        {
            var contracts = phases.Where(phase => phase.targetBuildLocation == site && phase.contract != null).Select(phase => phase.contract).Distinct().ToArray();
            report.AppendLine($"\nSITE {PathOf(site.transform)} contracts={string.Join(",", contracts.Select(contract => contract.ContractID))} activeContract={site.activeContract?.ContractID} baked={site.bakedBars.Count}/{site.bakedPoints.Count}");
            var anchors = site.startingAnchors.Concat(site.endingAnchors).Where(point => point != null).Distinct().ToArray();
            foreach (var aligner in site.GetComponents<BridgeAbutmentAligner>())
                report.AppendLine($"ALIGNER reference={PathOf(aligner.ReferenceAnchor?.transform)} abutments={aligner.Abutments.Count} valid={aligner.ValidateAlignment(out string detail)} {detail}");
            foreach (var anchor in anchors)
                report.AppendLine($"ANCHOR {PathOf(anchor.transform)} pos={anchor.transform.position:F6} permanent={anchor.originalIsAnchor} runtime={anchor.Runtime}");
            foreach (var anchor in anchors)
            {
                if (anchor.transform.position.x > 2200f) continue;
                float land = anchor.transform.position.x <= anchors.Average(point => point.transform.position.x) ? -1f : 1f;
                var point = anchor.transform.position;
                var region = new Bounds(point, new Vector3(8f, 5f, 12f));
                foreach (var col in colliders.Where(col => col.bounds.Intersects(region)))
                    report.AppendLine($"COLLIDER {anchor.name} {PathOf(col.transform)} type={col.GetType().Name} bounds={col.bounds} scale={col.transform.lossyScale:F4} rotation={col.transform.eulerAngles:F3}");
                foreach (float z in new[] { -2f, 0f, 2f })
                    foreach (float distance in new[] { -.3f, 0f, .15f, .5f, 1f, 2f })
                    {
                        Vector3 sample = point + new Vector3(land * distance, 0f, z);
                        var hits = Physics.RaycastAll(sample + Vector3.up * 3f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore).Where(hit => Ground(hit.collider, scene) && hit.normal.y > .5f).OrderByDescending(hit => hit.point.y).ToArray();
                        report.AppendLine($"SAMPLE {anchor.name} land={distance:F2} z={z:F1}: " + string.Join(" | ", hits.Take(3).Select(hit => $"Y={hit.point.y:F6} delta={hit.point.y - point.y - .025f:F6} normal={hit.normal:F3} {PathOf(hit.collider.transform)}")));
                    }
            }
            foreach (var vehicle in All<LiveLoadVehicle>(scene).Where(vehicle => contracts.Contains(vehicle.assignedContract) || (site.activeContract != null && vehicle.assignedContract == site.activeContract)))
            {
                vehicle.GetMultiLoadRoutePoses(out Vector3 start, out Vector3 end);
                report.AppendLine($"VEHICLE {PathOf(vehicle.transform)} contract={vehicle.assignedContract?.ContractID} start={start:F4} end={end:F4} radius={vehicle.wheelRadius} wheels=" + string.Join(";", vehicle.wheelObjects.Where(wheel => wheel != null).Select(wheel => (wheel.GetComponentInChildren<Renderer>(true)?.bounds.center ?? wheel.transform.position).ToString("F4"))));
            }
        }
        File.WriteAllText(Report, report.ToString());
    }
}
#endif
