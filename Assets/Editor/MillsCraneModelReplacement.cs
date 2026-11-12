#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in replacement of Mills's temporary crane with the authored Crane1 1 model.</summary>
[InitializeOnLoad]
internal static class MillsCraneModelReplacement
{
    internal const string ModelPath = "Assets/Elements/Vehicles/Crane1 1.fbx";
    private const string Request = "Temp/mills-crane-model.request";
    private const string Report = "Logs/mills-crane-model.txt";

    static MillsCraneModelReplacement() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try
        {
            var report = new StringBuilder("Mills Crane1 1 replacement\n");
            if (command == "inspect") Inspect(report);
            else if (command == "apply") Apply(report);
            else if (command == "validate") Validate(Canyon(), report);
            else throw new InvalidOperationException("Unknown crane replacement command.");
            File.WriteAllText(Report, report.ToString());
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    private static void Inspect(StringBuilder report)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (asset == null) throw new InvalidOperationException("Crane1 1 model is not imported.");
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
            model.transform.position = Vector3.zero;
            report.AppendLine($"ROOT local rotation={model.transform.localEulerAngles:F3}; scale={model.transform.localScale:F3}");
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                report.AppendLine($"NODE {child.name}: position={child.position:F4}, localRotation={child.localEulerAngles:F3}, scale={child.lossyScale:F4}");
            foreach (MeshFilter mesh in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = mesh.GetComponent<Renderer>();
                if (mesh.sharedMesh == null || renderer == null) continue;
                report.AppendLine($"MESH {mesh.name}: worldBounds={renderer.bounds}; localBounds={mesh.sharedMesh.bounds}; materials={string.Join(",", renderer.sharedMaterials.Select(m => m != null ? m.name : "none"))}");
                for (int sub = 0; sub < mesh.sharedMesh.subMeshCount && sub < renderer.sharedMaterials.Length; sub++)
                    report.AppendLine($"  SUBMESH {sub} {renderer.sharedMaterials[sub]?.name}: center={mesh.transform.TransformPoint(mesh.sharedMesh.GetSubMesh(sub).bounds.center):F4}");
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static Scene Canyon()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode.");
        return scene;
    }

    private static void Apply(StringBuilder report)
    {
        Scene scene = Canyon();
        LiveLoadVehicle vehicle = MillsHeavyEquipmentSiteAuthoring.FindVehicle(scene);
        BuildLocation site = MillsHeavyEquipmentSiteAuthoring.FindLocation(scene);
        ContractSO contract = vehicle.assignedContract;
        var anchors = MillsHeavyEquipmentSiteAuthoring.GetRoadAnchors(site);
        Require(contract != null && contract.ContractID == "MillsMainContract", "Keep Mills's heavy equipment contract.");
        Require(vehicle.startPoint != null && vehicle.endPoint != null && vehicle.physicsManager != null, "Crane route/system references are missing.");
        Require(Quaternion.Angle(vehicle.transform.rotation, Quaternion.identity) < .1f &&
            Vector3.Distance(vehicle.transform.lossyScale, Vector3.one) < .001f, "Use the authored unit-scale Mills physics root.");
        string backup = "Temp/MillsCraneReplacement-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
        File.Copy(scene.path, backup);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace Mills temporary crane with Crane1 1");
        try
        {
            var candidates = vehicle.GetComponentsInChildren<Transform>(true).Where(value => value != vehicle.transform &&
                PrefabUtility.IsAnyPrefabInstanceRoot(value.gameObject) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(value.gameObject) == ModelPath).ToArray();
            Require(candidates.Length <= 1, "More than one Crane1 1 model exists under Mills's vehicle.");
            Transform visual;
            if (candidates.Length == 1) visual = candidates[0];
            else
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Require(asset != null, "Missing Crane1 1 asset.");
                GameObject created = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                Undo.RegisterCreatedObjectUndo(created, "Add Crane1 1 model");
                Undo.SetTransformParent(created.transform, vehicle.transform, "Parent Crane1 1 model");
                visual = created.transform;
                visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.Euler(0, 90, 0);
            }
            Undo.RecordObjects(new UnityEngine.Object[] { vehicle, vehicle.gameObject, vehicle.transform, visual, visual.gameObject,
                vehicle.startPoint, vehicle.endPoint, vehicle.GetComponent<Rigidbody>(), vehicle.GetComponent<BoxCollider>(),
                vehicle.endPoint.GetComponent<BoxCollider>() }, "Configure Crane1 1 vehicle");
            visual.name = "Crane1 1 Visual";
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.Euler(0, 90, 0);
            visual.gameObject.SetActive(true);
            GameObject[] tires = visual.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.name.StartsWith("Wheel_", StringComparison.Ordinal))
                .OrderBy(renderer => renderer.name, StringComparer.Ordinal).Select(renderer => renderer.gameObject).ToArray();
            Require(tires.Length == 6, "Crane1 1 must provide its six separate wheel meshes.");
            Require(tires.All(tire => tire.GetComponent<Renderer>().enabled), "Visible Crane1 1 wheels must be enabled.");
            GameObject[] oldParts = vehicle.transform.Cast<Transform>().Where(child => child != visual &&
                (child.name == "Temporary Mobile Crane Visual" || child.name.StartsWith("Temporary crane wheel", StringComparison.Ordinal)))
                .Select(child => child.gameObject).ToArray();
            Vector3 baseScale = visual.localScale;
            Layout layout = null;
            foreach (float scale in new[] { 1f, .95f, .90f, .85f, .80f })
            {
                visual.localScale = baseScale * scale;
                Physics.SyncTransforms();
                layout = MeasureAndFit(scene, vehicle, visual, tires, anchors.start.transform.position, anchors.end.transform.position);
                if (layout != null) { report.AppendLine($"Model scale factor: {scale:0.##}; root visual scale={visual.localScale:F3}"); break; }
            }
            Require(layout != null, "The new crane cannot fit the approach and parking area with all six tires supported.");
            // Delete only the superseded visual/proxies. Preserve the live-load
            // component, contract, route markers, finish event and physics manager.
            foreach (GameObject oldPart in oldParts) Undo.DestroyObjectImmediate(oldPart);
            vehicle.gameObject.name = "Mills Crane1 1";
            vehicle.vehicleName = "Mobile Crane";
            vehicle.wheelObjects = tires;
            vehicle.wheelRadius = tires.Average(tire => tire.GetComponent<Renderer>().bounds.extents.y);
            vehicle.spinAxis = Vector3.right;
            var settings = new SerializedObject(vehicle);
            settings.FindProperty("useWheelMeshRadius").boolValue = true;
            settings.FindProperty("alignWheelAxisToMesh").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            vehicle.transform.position = layout.start;
            vehicle.startPoint.position = layout.start;
            vehicle.endPoint.position = layout.finish;
            BoxCollider chassis = vehicle.GetComponent<BoxCollider>();
            chassis.center = layout.chassis.center; chassis.size = layout.chassis.size;
            vehicle.GetComponent<Rigidbody>().mass = contract.liveLoadWeight;
            var zone = vehicle.endPoint.GetComponent<BoxCollider>();
            Require(zone != null && vehicle.endPoint.GetComponent<FinishLineTrigger>()?.assignedContract == contract,
                "Mills finish trigger must retain its contract.");
            zone.isTrigger = true;
            zone.center = new Vector3(layout.full.max.x + .525f, layout.finishGround + 2f - layout.finish.y, 0);
            zone.size = new Vector3(1.25f, 6f, contract.twoWayRoadWidth);
            foreach (var changed in new UnityEngine.Object[] { vehicle, vehicle.gameObject, vehicle.transform, visual,
                visual.gameObject, vehicle.startPoint, vehicle.endPoint, chassis, zone, vehicle.GetComponent<Rigidbody>() })
            {
                EditorUtility.SetDirty(changed);
                PrefabUtility.RecordPrefabInstancePropertyModifications(changed);
            }
            Physics.SyncTransforms();
            Validate(scene, report);
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Could not save the new crane setup.");
            Undo.CollapseUndoOperations(group);
            report.AppendLine($"REMOVED: {oldParts.Length} obsolete temporary visual/proxy objects.");
            report.AppendLine("SAVED: CanyonCrossing; existing scene edits preserved. Backup: " + backup);
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    private sealed class Layout
    {
        public Bounds full, chassis;
        public Vector3 start, finish;
        public float finishGround;
    }

    private static Layout MeasureAndFit(Scene scene, LiveLoadVehicle vehicle, Transform visual, GameObject[] tires, Vector3 left, Vector3 right)
    {
        Bounds full = LocalBounds(vehicle.transform, visual.GetComponentsInChildren<Renderer>(true));
        var body = visual.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.name == "Crane_Body").ToArray();
        Require(body.Length == 1, "Crane1 1 body mesh is missing.");
        Bounds chassis = LocalBounds(vehicle.transform, body);
        var wheels = tires.Select(tire => new {
            center = vehicle.transform.InverseTransformPoint(tire.GetComponent<Renderer>().bounds.center),
            radius = tire.GetComponent<Renderer>().bounds.extents.y }).ToArray();
        float lowest = wheels.Min(wheel => wheel.center.y - wheel.radius);
        float rootX = left.x - full.max.x - 1f;
        float rootZ = left.z - full.center.z;
        if (full.size.z > vehicle.assignedContract.twoWayRoadWidth - .5f) return null;
        var startHeights = new List<float>();
        foreach (var wheel in wheels)
        {
            if (!Ground(scene, new Vector3(rootX + wheel.center.x, left.y + .025f, rootZ + wheel.center.z), out float height)) return null;
            startHeights.Add(height);
        }
        if (startHeights.Max() - startHeights.Min() > .07f) return null;
        float startGround = startHeights.Average();
        float stopX = 0, stopGround = 0;
        bool found = false;
        float brakeDistance = vehicle.maxSpeed * vehicle.maxSpeed * vehicle.vehicleMass * wheels.Average(w => w.radius) /
            Mathf.Max(1f, 2f * vehicle.brakeTorque * wheels.Length) + .15f;
        for (int sample = 0; sample < 12 && !found; sample++)
        {
            float candidate = right.x - full.min.x + 1.5f + sample * .25f;
            var heights = new List<float>();
            foreach (var wheel in wheels)
            {
                if (!Ground(scene, new Vector3(candidate + wheel.center.x, right.y + .025f, rootZ + wheel.center.z), out float height) ||
                    !Ground(scene, new Vector3(candidate + wheel.center.x + brakeDistance, right.y + .025f, rootZ + wheel.center.z), out float afterBrake))
                { heights.Clear(); break; }
                heights.Add(height); heights.Add(afterBrake);
            }
            if (heights.Count == wheels.Length * 2 && heights.Max() - heights.Min() < .07f)
            { found = true; stopX = candidate; stopGround = heights.Average(); }
        }
        if (!found) return null;
        Vector3 minimum = chassis.min; minimum.y = Mathf.Max(minimum.y, lowest + .20f); chassis.SetMinMax(minimum, chassis.max);
        return new Layout { full = full, chassis = chassis,
            start = new Vector3(rootX, startGround - lowest + .025f, rootZ),
            finish = new Vector3(stopX, stopGround - lowest + .025f, rootZ), finishGround = stopGround };
    }

    private static Bounds LocalBounds(Transform frame, Renderer[] renderers)
    {
        Require(renderers.Length > 0, "The new crane has no rendered geometry.");
        bool found = false; Bounds result = default;
        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = frame.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (!found) { result = new Bounds(point, Vector3.zero); found = true; } else result.Encapsulate(point);
            }
        }
        return result;
    }

    private static bool Ground(Scene scene, Vector3 point, out float height)
    {
        var hits = new RaycastHit[64];
        int count = scene.GetPhysicsScene().Raycast(point + Vector3.up * 3, Vector3.down, hits, 7, ~0, QueryTriggerInteraction.Ignore);
        var valid = hits.Take(count).Where(hit => hit.collider != null && hit.normal.y > .7f &&
            hit.collider.GetComponentInParent<LiveLoadVehicle>() == null && Mathf.Abs(hit.point.y - point.y) < .75f)
            .OrderBy(hit => Mathf.Abs(hit.point.y - point.y)).ToArray();
        height = valid.Length > 0 ? valid[0].point.y : 0;
        return valid.Length > 0;
    }

    internal static void Validate(Scene scene, StringBuilder report)
    {
        var vehicle = MillsHeavyEquipmentSiteAuthoring.FindVehicle(scene);
        var visual = vehicle.transform.Find("Crane1 1 Visual");
        Require(visual != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual.gameObject) == ModelPath,
            "Mills must use the newly imported Crane1 1 asset.");
        Require(!vehicle.GetComponentsInChildren<Transform>(true).Any(value => value.name.StartsWith("Temporary", StringComparison.Ordinal)),
            "The old temporary visual or wheel proxies are still present.");
        Require(vehicle.wheelObjects.Length == 6 && vehicle.wheelObjects.Distinct().Count() == 6 &&
            vehicle.wheelObjects.All(wheel => wheel != null && wheel.transform.IsChildOf(visual) && wheel.name.StartsWith("Wheel_")),
            "Assign all six real wheels from the new crane.");
        Require(new SerializedObject(vehicle).FindProperty("useWheelMeshRadius").boolValue,
            "The crane's unequal tire sizes need individual physical wheel radii.");
        Require(vehicle.vehicleMass == vehicle.assignedContract.liveLoadWeight && vehicle.vehicleName == "Mobile Crane",
            "Preserve the Mills live-load mass and remove the temporary inspection label.");
        MillsHeavyEquipmentSiteAuthoring.Validate(scene, MillsHeavyEquipmentSiteAuthoring.FindLocation(scene), vehicle.assignedContract, report);
        report.AppendLine("PASS: Crane1 1 model, six visible driven wheels with individual tire radii, Mills contract/route/finish references retained.");
        foreach (GameObject tire in vehicle.wheelObjects)
            report.AppendLine($"WHEEL {tire.name}: center={tire.GetComponent<Renderer>().bounds.center:F3}, radius={tire.GetComponent<Renderer>().bounds.extents.y:F4}");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
#endif
