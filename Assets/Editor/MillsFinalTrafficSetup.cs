#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class MillsFinalTrafficSetup
{
    private const string Request = "Temp/mills-final-traffic.request";
    private const string Report = "Logs/mills-final-traffic.txt";
    private const string ContractPath = "Assets/BridgeBuilder/Data/Contract/IndustrialZone/Mills Reconnecting Arcadia Contract.asset";
    static MillsFinalTrafficSetup() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var report = new StringBuilder();
        try
        {
            string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
            Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode.");
            var root = scene.GetRootGameObjects().Single(go => go.name == "Mills Final Temporary Vehicle");
            var contract = AssetDatabase.LoadAssetAtPath<ContractSO>(ContractPath);
            var location = MillsFinalSiteAuthoring.FindLocation(scene);
            report.AppendLine("Scene dirty=" + scene.isDirty + " contract=" + contract.name + " mode=" + contract.vehicleTestMode);
            foreach (var t in scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true)).Where(t =>
                t.name.StartsWith("mills final", StringComparison.OrdinalIgnoreCase) || t.GetComponent<LiveLoadVehicle>()?.assignedContract == contract))
            {
                var v = t.GetComponent<LiveLoadVehicle>();
                report.AppendLine(t.name + " position=" + t.position.ToString("F3") + " scale=" + t.lossyScale.ToString("F3") +
                    " components=" + string.Join(",", t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
                if (v == null) continue;
                v.TryGetMultiLoadStartBounds(out Bounds bounds);
                report.AppendLine("  mass=" + v.vehicleMass + " torque=" + v.engineTorque + " bounds=" + bounds.ToString("F3") +
                    " contract=" + v.assignedContract?.name + " wheels=" + v.wheelObjects.Length +
                    " start=" + v.startPoint?.position.ToString("F3") + " finish=" + v.endPoint?.position.ToString("F3"));
            }
            if (command == "apply") Apply(scene, root, location, contract, report);
            else if (command == "validate") Check(scene, location, contract, report);
            else if (command == "drive-check")
            {
                Check(scene, location, contract, report);
                // Reuse the isolated production-wheel probe; the authored scene
                // never enters Play Mode or participates in this physics test.
                var probe = typeof(ClarkOffsetVehicleValidation).GetMethod("Drive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                foreach (var vehicle in Vehicles(scene, contract)) probe.Invoke(null, new object[] { vehicle, 0f, report, true });
            }
            else Require(command == "inspect", "Unknown Mills traffic command.");
        }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        File.WriteAllText(Report, report.ToString());
    }
    private static bool NamedTruck(Transform t) => string.Equals(t.name.Trim(), "mills final", StringComparison.OrdinalIgnoreCase) ||
        t.name.StartsWith("mills final (", StringComparison.OrdinalIgnoreCase);
    private static LiveLoadVehicle[] Vehicles(Scene scene, ContractSO contract) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<LiveLoadVehicle>(true))
        .Where(v => v.assignedContract == contract || NamedTruck(v.transform)).ToArray();
    private static void Mark(UnityEngine.Object value)
    {
        EditorUtility.SetDirty(value);
        if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }
    private static void Apply(Scene scene, GameObject root, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        var placed = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true))
            .Where(t => NamedTruck(t) || t.GetComponent<LiveLoadVehicle>()?.assignedContract == contract).Distinct().ToArray();
        Require(placed.Length == 4, "Expected exactly the four placed final-contract vehicles.");
        var vehicles = Vehicles(scene, contract);
        if (contract.HasMultipleLiveLoads) { Check(scene, location, contract, report); return; }
        Require(!EditorUtility.IsDirty(contract), "Save the contract Inspector edits before configuring its roster.");
        Require(contract.liveLoadTests.Count == 0 && (location.liveLoadVehicles == null || location.liveLoadVehicles.Count == 0), "Review existing entries before replacing the roster.");
        var anchors = MillsFinalSiteAuthoring.GetRoadAnchors(location);
        float middle = (anchors.start.transform.position.x + anchors.end.transform.position.x) * .5f;
        var suv = vehicles.Single(v => v.wheelObjects.Length == 4);
        var template = vehicles.Single(v => NamedTruck(v.transform));
        var finishTemplate = suv.endPoint;
        Require(finishTemplate != null && finishTemplate.GetComponent<FinishLineTrigger>() != null, "Missing Mills finish template.");
        string backup = "LocalBackups/MillsFinalTraffic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path, backup + "/CanyonCrossing.unity"); File.Copy(ContractPath, backup + "/MillsFinal.asset");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Wire Mills final four-vehicle test");
        try
        {
            foreach (var t in placed.Where(t => t.GetComponent<LiveLoadVehicle>() == null))
            {
                var v = Undo.AddComponent<LiveLoadVehicle>(t.gameObject);
                EditorUtility.CopySerialized(template, v);
                var body = v.GetComponent<Rigidbody>(); Undo.RecordObject(body, "Configure Mills truck body");
                EditorUtility.CopySerialized(template.GetComponent<Rigidbody>(), body);
                var renderers = v.GetComponentsInChildren<Renderer>(true);
                v.wheelObjects = renderers.Where(r => r.name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.name.IndexOf("tire", StringComparison.OrdinalIgnoreCase) >= 0).Select(r => r.gameObject).Distinct().ToArray();
                Require(v.wheelObjects.Length >= 4, "Identify the wheels on " + t.name + " before binding it.");
                var shapes = renderers.Where(r => !v.wheelObjects.Contains(r.gameObject)).ToArray();
                Require(shapes.Length > 0, "Missing truck chassis model.");
                var bounds = shapes[0].bounds; foreach (var r in shapes.Skip(1)) bounds.Encapsulate(r.bounds);
                var box = Undo.AddComponent<BoxCollider>(t.gameObject);
                Vector3 min = bounds.min, max = bounds.max;
                Bounds local = new Bounds(t.InverseTransformPoint(min), Vector3.zero);
                for (int corner = 1; corner < 8; corner++) local.Encapsulate(t.InverseTransformPoint(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z)));
                box.center = local.center; box.size = local.size;
                Undo.RecordObject(t.gameObject, "Tag Mills truck"); t.gameObject.tag = template.gameObject.tag; t.gameObject.layer = template.gameObject.layer;
                v.assignedContract = contract; Mark(v); Mark(body); Mark(box); Mark(t.gameObject);
                report.AppendLine("WIRED placed model " + t.name + " at " + t.position.ToString("F3") + " wheels=" + v.wheelObjects.Length + " mass=" + v.vehicleMass);
            }
            vehicles = Vehicles(scene, contract).OrderBy(v => v.transform.position.x < middle ? 0 : 1)
                .ThenBy(v => v.transform.position.x < middle ? -v.transform.position.x : v.transform.position.x).ToArray();
            Require(vehicles.Length == 4, "Expected four independent vehicle scripts after wiring placed models.");
            Undo.RecordObjects(new UnityEngine.Object[] { contract, location }, "Bind Mills final roster");
            var routes = new GameObject("Mills Final Traffic Routes"); SceneManager.MoveGameObjectToScene(routes, scene);
            routes.transform.SetParent(root.transform, false); Undo.RegisterCreatedObjectUndo(routes, "Create Mills traffic routes");
            foreach (var v in vehicles)
            {
                Undo.RecordObjects(new UnityEngine.Object[] { v, v.transform }, "Configure Mills vehicle");
                v.assignedContract = contract;
                Require(v.GetComponent<Rigidbody>() != null && v.physicsManager != null && v.wheelObjects.Length >= 4 &&
                    v.wheelObjects.All(w => w != null && w.transform.IsChildOf(v.transform)), "Missing independent vehicle physics or wheels.");
                var scale = v.transform.localScale;
                Require(Mathf.Abs(scale.x - scale.y) < .001f && Mathf.Abs(scale.x - scale.z) < .001f && scale.x > 0f, "Use a positive uniform physics root scale.");
                if ((scale - Vector3.one).sqrMagnitude > .000001f)
                {
                    foreach (Transform child in v.transform)
                    {
                        Undo.RecordObject(child, "Normalize Mills physics root"); child.localPosition *= scale.x; child.localScale *= scale.x; Mark(child);
                    }
                    foreach (var box in v.GetComponents<BoxCollider>())
                    {
                        Undo.RecordObject(box, "Preserve Mills chassis dimensions"); box.center *= scale.x; box.size *= scale.x; Mark(box);
                    }
                    v.transform.localScale = Vector3.one;
                }
                v.wheelRadius = v.wheelObjects.Average(w => w.GetComponentInChildren<Renderer>().bounds.extents.y);
                var data = new SerializedObject(v); data.FindProperty("useWheelMeshRadius").boolValue = true; data.ApplyModifiedProperties();
                if (v != suv) v.engineTorque = Mathf.Max(6000f, v.engineTorque);
                // Settle every wheel onto the authored approach before saving the route.
                Require(TryGround(scene, v, v.transform.position, anchors.start.transform.position.y + .025f, out float ground), "No level approach under " + v.name);
                float bottom = v.wheelObjects.Min(w => w.GetComponentInChildren<Renderer>().bounds.min.y);
                v.transform.position += Vector3.up * (ground + .025f - bottom);
                var start = new GameObject(v.name + " Traffic Start"); SceneManager.MoveGameObjectToScene(start, scene);
                start.transform.SetParent(routes.transform); start.transform.SetPositionAndRotation(v.transform.position, Quaternion.identity);
                Undo.RegisterCreatedObjectUndo(start, "Create Mills start marker");
                var finish = UnityEngine.Object.Instantiate(finishTemplate.gameObject, routes.transform);
                finish.name = v.name + " Traffic Finish"; Undo.RegisterCreatedObjectUndo(finish, "Create Mills finish marker");
                finish.GetComponent<FinishLineTrigger>().assignedContract = contract;
                var zone = finish.GetComponent<BoxCollider>(); zone.isTrigger = true; zone.center = new Vector3(0f, 1.8f, 0f); zone.size = new Vector3(1.25f, 5f, 5f);
                v.startPoint = start.transform; v.endPoint = finish.transform; Mark(v); Mark(v.transform);
            }
            Physics.SyncTransforms();
            var parking = new System.Collections.Generic.List<Bounds>();
            // Rear vehicles stop nearest the bank; leaders clear farther into the approach.
            foreach (var v in vehicles.Reverse())
            {
                Require(v.TryGetMultiLoadStartBounds(out Bounds bounds), "Missing Mills vehicle envelope.");
                bool fromLeft = v.transform.position.x < middle;
                float direction = fromLeft ? 1f : -1f;
                float tail = fromLeft ? bounds.min.x - v.transform.position.x : bounds.max.x - v.transform.position.x;
                float first = (fromLeft ? anchors.end.transform.position.x : anchors.start.transform.position.x) - tail + direction * 2f;
                bool found = false;
                for (int step = 0; step < 160 && !found; step++)
                {
                    Vector3 target = v.transform.position; target.x = first + direction * step * .5f;
                    if (!TryGround(scene, v, target, anchors.start.transform.position.y + .025f, out float ground)) continue;
                    float bottomOffset = v.wheelObjects.Min(w => w.GetComponentInChildren<Renderer>().bounds.min.y) - v.transform.position.y;
                    target.y = ground + .025f - bottomOffset;
                    Bounds final = bounds; final.center += target - v.transform.position; final.Expand(new Vector3(2f, 0f, .2f));
                    if (parking.Any(other => final.Intersects(other))) continue;
                    parking.Add(final); v.endPoint.SetPositionAndRotation(target, Quaternion.identity); Mark(v.endPoint); found = true;
                }
                Require(found, "No grounded parking space for " + v.name);
            }
            var entries = vehicles.Select((v, index) => new ContractLiveLoadEntry { displayName = v.name, emptyWeightKg = v.vehicleMass, departureDelay = index * 3f }).ToList();
            contract.vehicleTestMode = ContractSO.VehicleTestMode.Together; contract.liveLoadTests = entries;
            location.liveLoadVehicles = vehicles.Select((v, i) => new ContractLiveLoadBinding { entryId = entries[i].entryId, vehicle = v }).ToList();
            Check(scene, location, contract, report);
            Mark(location); Mark(contract); Undo.FlushUndoRecordObjects(); EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Could not save Mills traffic bindings."); AssetDatabase.SaveAssetIfDirty(contract);
            Undo.CollapseUndoOperations(group); report.AppendLine("SAVED: Mills final roster, routes, and contract. Backup=" + backup);
        }
        catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
    }
    private static bool TryGround(Scene scene, LiveLoadVehicle v, Vector3 target, float referenceY, out float height)
    {
        var heights = new System.Collections.Generic.List<float>();
        foreach (var wheel in v.wheelObjects)
        {
            var center = wheel.GetComponentInChildren<Renderer>().bounds.center + target - v.transform.position;
            var hits = Physics.RaycastAll(new Vector3(center.x, referenceY + 3f, center.z), Vector3.down, 7f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.collider.gameObject.scene == scene && hit.collider.GetComponentInParent<LiveLoadVehicle>() == null &&
                    hit.normal.y > .7f && Mathf.Abs(hit.point.y - referenceY) < .6f).OrderBy(hit => Mathf.Abs(hit.point.y - referenceY)).ToArray();
            if (hits.Length == 0) { height = 0f; return false; } heights.Add(hits[0].point.y);
        }
        height = heights.Average(); return heights.Max() - heights.Min() < .1f;
    }
    internal static void Check(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        var previous = location.activeContract;
        try
        {
            location.activeContract = contract;
            Require(LiveLoadTestRoster.TryCreate(location, contract, out var roster, out string reason, false), reason);
            Require(roster.Participants.Count == 4 && roster.Mode == ContractSO.VehicleTestMode.Together, "Expected four Together vehicles.");
            Require(roster.Participants.Count(p => NamedTruck(p.Vehicle.transform)) == 3, "Expected one SUV and three named Mills trucks.");
            Require(Vehicles(scene, contract).Length == 4, "Unexpected extra final-contract vehicles.");
            Require(roster.Participants.Select(p => p.Vehicle.startPoint).Distinct().Count() == 4 &&
                roster.Participants.Select(p => p.Vehicle.endPoint).Distinct().Count() == 4, "Each vehicle needs independent markers.");
            var anchor = MillsFinalSiteAuthoring.GetRoadAnchors(location).start.transform.position;
            var parked = new System.Collections.Generic.List<Bounds>();
            foreach (var p in roster.Participants)
            {
                var v = p.Vehicle; v.GetMultiLoadRoutePoses(out Vector3 from, out Vector3 to);
                Require(v.TryGetMultiLoadStartBounds(out Bounds bounds), "Missing Mills envelope.");
                Require(Mathf.Abs(from.z - to.z) < .01f && Mathf.Abs(bounds.center.z - anchor.z) + bounds.extents.z < contract.twoWayRoadWidth * .5f - .1f,
                    "Keep " + v.name + " inside its straight traffic lane.");
                bounds.center += to - from; Require(!parked.Any(other => bounds.Intersects(other)), "Mills parking positions overlap."); parked.Add(bounds);
                Require(v.endPoint.GetComponent<FinishLineTrigger>()?.assignedContract == contract && v.endPoint.GetComponent<BoxCollider>().isTrigger, "Missing Mills finish zone.");
                Require(TryGround(scene, v, from, anchor.y + .025f, out _) && TryGround(scene, v, to, anchor.y + .025f, out _), "Unsupported start or parking wheels.");
                report.AppendLine("PASS: " + p.DisplayName + " " + p.WeightKg + "kg delay=" + p.DepartureDelay + "s route=" + from.ToString("F3") + " -> " + to.ToString("F3"));
            }
        }
        finally { location.activeContract = previous; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
