#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-only setup for the explicitly authored Clark second site and SUV.</summary>
internal static class ClarkOffsetSiteAuthoring
{
    private const string SiteRoot = "Clark  MAIN CONTRACT (2)";
    private const string VehicleRoot = "Clark second main contract";
    private const string Action = "Set up Clark Offset Road SUV";

    internal static BuildLocation FindLocation(Scene scene) => All<BuildLocation>(scene).Single(site =>
        HasAncestor(site.transform, SiteRoot) && (site.name == "workbenchClark1" || site.name == "workbenchClark2"));
    internal static LiveLoadVehicle FindVehicle(Scene scene) => All<LiveLoadVehicle>(scene).Single(vehicle =>
        HasAncestor(vehicle.transform, VehicleRoot) && vehicle.name == "suvv");
    internal static (Point start, Point end) GetRoadAnchors(BuildLocation location)
    {
        Point start = location.startingAnchors.Single(point => point != null && point.name == "LeftAnchor");
        Point end = location.endingAnchors.Single(point => point != null && point.name == "RightAnchor");
        Require(start.originalIsAnchor && end.originalIsAnchor && end.transform.position.x > start.transform.position.x &&
            Mathf.Abs(start.transform.position.y - end.transform.position.y) < .05f && Mathf.Abs(start.transform.position.z - end.transform.position.z) < .05f,
            "Offset Road needs its two original level X-axis road anchors.");
        return (start, end);
    }

    internal static void Inspect(Scene scene, StringBuilder report)
    {
        var vehicle = FindVehicle(scene);
        report.AppendLine($"SUV ROOT {vehicle.name} position={vehicle.transform.position:F4} rotation={vehicle.transform.eulerAngles:F4} scale={vehicle.transform.lossyScale:F4}");
        foreach (GameObject wheel in vehicle.wheelObjects)
        {
            report.AppendLine($"WHEEL {wheel.name} position={wheel.transform.position:F4} rotation={wheel.transform.eulerAngles:F4} scale={wheel.transform.lossyScale:F4}");
            foreach (MeshFilter mesh in wheel.GetComponentsInChildren<MeshFilter>(true))
                report.AppendLine($"  Mesh {mesh.name} centerWorld={MeshCenter(mesh):F4} localBounds={mesh.sharedMesh.bounds} rotation={mesh.transform.eulerAngles:F4} scale={mesh.transform.lossyScale:F4} guide={wheel.transform.InverseTransformDirection(Vector3.forward):F4}");
        }
        foreach (MeshFilter mesh in vehicle.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.sharedMesh != null))
        {
            Renderer renderer = mesh.GetComponent<Renderer>();
            if (renderer == null) continue;
            Material[] materials = renderer.sharedMaterials;
            report.AppendLine($"BODY MESH {mesh.name} mesh={mesh.sharedMesh.name} worldCenter={MeshCenter(mesh):F4} bounds={mesh.sharedMesh.bounds} rotation={mesh.transform.eulerAngles:F4} scale={mesh.transform.lossyScale:F4}");
            for (int submesh = 0; submesh < mesh.sharedMesh.subMeshCount; submesh++)
            {
                string material = submesh < materials.Length && materials[submesh] != null ? materials[submesh].name : "none";
                var descriptor = mesh.sharedMesh.GetSubMesh(submesh);
                report.AppendLine($"  SUBMESH {submesh} material={material} count={descriptor.indexCount} bounds={descriptor.bounds} centerWorld={mesh.transform.TransformPoint(descriptor.bounds.center):F4}");
                if (material.IndexOf("grille", StringComparison.OrdinalIgnoreCase) >= 0 || material.IndexOf("headlight", StringComparison.OrdinalIgnoreCase) >= 0 || material.IndexOf("taillight", StringComparison.OrdinalIgnoreCase) >= 0)
                    report.AppendLine($"    ACTUAL INDEXED VERTEX CENTROID={MaterialCentroid(mesh, submesh):F4}");
            }
        }
    }

    private static Vector3 MaterialCentroid(MeshFilter mesh, int submesh)
    {
        using (var snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh.sharedMesh))
        using (var vertices = new NativeArray<Vector3>(snapshot[0].vertexCount, Allocator.Temp))
        using (var indices = new NativeArray<int>(snapshot[0].GetSubMesh(submesh).indexCount, Allocator.Temp))
        {
            snapshot[0].GetVertices(vertices);
            snapshot[0].GetIndices(indices, submesh, true);
            Vector3 sum = Vector3.zero;
            for (int index = 0; index < indices.Length; index++) sum += vertices[indices[index]];
            Require(indices.Length > 0, "Empty SUV material geometry.");
            return mesh.transform.TransformPoint(sum / indices.Length);
        }
    }

    internal static void Apply(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        Require(scene.IsValid() && scene.isLoaded && location == FindLocation(scene), "Use Clark's explicitly authored second workbench.");
        Require(contract != null && contract.ContractID == "ClarkOffsetRoad" && contract.vehicleTestMode == ContractSO.VehicleTestMode.Single &&
            contract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle && contract.winCondition == ContractSO.WinCondition.FinishLine &&
            contract.liveLoadWeight > 0f && !contract.allowVehicleCargo && contract.twoWayRoad && Mathf.Abs(contract.twoWayRoadWidth - 16f) < .01f,
            "Offset Road requires one positive SUV finish-line load and its 16 m deck.");
        Require(location.activeContract == null || location.activeContract == contract, "The second site belongs to another contract.");
        var (start, end) = GetRoadAnchors(location);
        Require(Mathf.Abs(contract.bridgeSpan - (end.transform.position.x - start.transform.position.x)) < .05f, "Offset Road contract span must match the authored 80 m crossing.");
        LiveLoadVehicle vehicle = FindVehicle(scene);
        Require(vehicle.assignedContract == null || vehicle.assignedContract == contract, "The new SUV belongs to another contract.");
        Require(vehicle.GetComponent<Rigidbody>() != null && vehicle.wheelObjects != null && vehicle.wheelObjects.Length == 4 &&
            vehicle.wheelObjects.Distinct().Count() == 4 && vehicle.wheelObjects.All(wheel => wheel != null && wheel.transform.IsChildOf(vehicle.transform)),
            "The SUV requires its four distinct imported wheels and Rigidbody.");
        Transform route = vehicle.transform.parent.Find("Clark Suv Route");
        Require(route != null && route.Find("Start") != null && route.Find("End") != null &&
            !route.IsChildOf(vehicle.transform), "Preserve the authored Clark Suv Route with Start and End markers.");
        Transform startMarker = route.Find("Start"), finishMarker = route.Find("End");
        Require((vehicle.startPoint == null && vehicle.endPoint == null) ||
            (vehicle.startPoint == startMarker && vehicle.endPoint == finishMarker), "The SUV already has a custom route; review before replacing it.");
        MeshFilter[] wheels = vehicle.wheelObjects.Select(wheel => wheel.GetComponentInChildren<MeshFilter>(true)).ToArray();
        Require(wheels.All(mesh => mesh != null && mesh.sharedMesh != null), "SUV wheel meshes are missing.");
        Vector3[] centers = wheels.Select(MeshCenter).ToArray();
        // Resolve the actual nose from grille/headlight geometry, preserving an
        // already-correct heading while aligning any rotated model to the route.
        Vector3 forward = FindNose(vehicle) - centers.Aggregate(Vector3.zero, (sum, point) => sum + point) / centers.Length;
        forward.y = 0f;
        Require(forward.sqrMagnitude > .1f, "Could not identify the SUV grille/headlights to align its front safely.");
        Undo.RecordObject(vehicle.transform, Action);
        vehicle.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(forward, Vector3.right, Vector3.up), Vector3.up) * vehicle.transform.rotation;
        centers = wheels.Select(MeshCenter).ToArray();
        float wheelbaseX = centers.Max(p => p.x) - centers.Min(p => p.x);
        float wheelbaseZ = centers.Max(p => p.z) - centers.Min(p => p.z);
        Require(wheelbaseX > wheelbaseZ * 1.05f, "The SUV's axle layout must align along the X-axis crossing.");
        float radius = wheels.Select(WheelRadius).Average();
        Require(radius > .15f && radius < 1f, "Unexpected imported SUV wheel radius.");
        Vector3[] axes = vehicle.wheelObjects.Select(wheel => wheel.transform.InverseTransformDirection(Vector3.forward).normalized).ToArray();
        if (!axes.All(axis => Vector3.Dot(axis, axes[0]) > .9f))
        {
            // Some imports mirror tire transforms. Uniform visual wrappers keep
            // one authored spin axis while preserving each imported mesh pose.
            Undo.RecordObject(vehicle, Action);
            vehicle.wheelObjects = vehicle.wheelObjects.Select(wheel =>
            {
                GameObject wrapper = new GameObject(wheel.name + " Axle Visual");
                SceneManager.MoveGameObjectToScene(wrapper, scene);
                Undo.RegisterCreatedObjectUndo(wrapper, Action);
                wrapper.transform.SetPositionAndRotation(wheel.transform.position, Quaternion.identity);
                Undo.SetTransformParent(wrapper.transform, vehicle.transform, Action);
                Undo.SetTransformParent(wheel.transform, wrapper.transform, Action);
                return wrapper;
            }).ToArray();
            axes = vehicle.wheelObjects.Select(wheel => wheel.transform.InverseTransformDirection(Vector3.forward).normalized).ToArray();
        }
        Vector3 lane = vehicle.transform.position;
        Bounds chassis = MeasureChassis(vehicle, centers, radius);
        Undo.RecordObjects(new UnityEngine.Object[] { vehicle, vehicle.transform, startMarker, finishMarker, location, location.gameObject,
            vehicle.GetComponent<Rigidbody>() }, Action);
        BoxCollider body = vehicle.GetComponent<BoxCollider>();
        if (body == null) body = Undo.AddComponent<BoxCollider>(vehicle.gameObject);
        else Undo.RecordObject(body, Action);
        body.center = chassis.center; body.size = chassis.size; body.enabled = true; body.isTrigger = false;
        Physics.SyncTransforms();
        Bounds envelope = body.bounds;
        foreach (MeshFilter wheel in wheels) envelope.Encapsulate(new Bounds(MeshCenter(wheel), Vector3.one * (radius * 2f)));
        float frontOffset = envelope.max.x - lane.x;
        float fromX = Mathf.Min(lane.x, start.transform.position.x - frontOffset - .75f);
        float wheelBottom = centers.Min(p => p.y) - radius;
        float deckSurface = start.transform.position.y + .025f;
        Require(TryGround(scene, new Vector3(fromX, deckSurface, lane.z), out float startY), "No level road under the authored SUV lane at its starting bank.");
        vehicle.transform.position = new Vector3(fromX, lane.y + startY - wheelBottom + .025f, lane.z);
        float finishX = end.transform.position.x + Mathf.Max(8f, envelope.size.x) + 2f;
        Require(TryGround(scene, new Vector3(finishX, deckSurface, lane.z), out float finishY), "No level parking road at the far bank of the authored SUV lane.");
        startMarker.SetPositionAndRotation(vehicle.transform.position, Quaternion.identity);
        finishMarker.SetPositionAndRotation(new Vector3(finishX, vehicle.transform.position.y + finishY - startY, lane.z), Quaternion.identity);
        FinishLineTrigger finish = finishMarker.GetComponent<FinishLineTrigger>();
        if (finish == null) finish = Undo.AddComponent<FinishLineTrigger>(finishMarker.gameObject);
        else Undo.RecordObject(finish, Action);
        finish.assignedContract = contract;
        BoxCollider zone = finish.GetComponent<BoxCollider>();
        Undo.RecordObject(zone, Action);
        zone.center = finishMarker.InverseTransformPoint(new Vector3(finishX, finishY + 2f, lane.z));
        zone.size = new Vector3(1.25f, 5f, 4f); zone.isTrigger = true; zone.enabled = true;
        vehicle.assignedContract = contract; vehicle.vehicleName = "SUV"; vehicle.vehicleMass = contract.liveLoadWeight;
        vehicle.wheelRadius = radius; vehicle.spinAxis = axes[0];
        var vehicleData = new SerializedObject(vehicle);
        vehicleData.FindProperty("alignWheelAxisToMesh").boolValue = true;
        vehicleData.ApplyModifiedProperties();
        vehicle.startPoint = startMarker; vehicle.endPoint = finishMarker;
        vehicle.physicsManager = All<BridgePhysicsManager>(scene).Single(manager => manager.isActiveAndEnabled);
        vehicle.GetComponent<Rigidbody>().mass = contract.liveLoadWeight;
        vehicle.advancesTutorial = false;
        location.gameObject.name = "workbenchClark2";
        location.liveLoadVehicles.Clear(); // Remove only this copied workbench's first-contract bindings.
        location.advancesTutorial = false;
        Undo.RecordObject(vehicle.gameObject, Action);
        vehicle.gameObject.layer = LayerMask.NameToLayer("Interactable");
        foreach (UnityEngine.Object target in new UnityEngine.Object[] { vehicle, vehicle.gameObject, vehicle.transform, startMarker, finishMarker,
            finish, zone, body, vehicle.GetComponent<Rigidbody>(), location, location.gameObject })
        { PrefabUtility.RecordPrefabInstancePropertyModifications(target); EditorUtility.SetDirty(target); }
        Physics.SyncTransforms();
        Validate(scene, location, contract, report);
    }

    internal static void Validate(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        var (start, end) = GetRoadAnchors(location);
        var vehicle = FindVehicle(scene);
        Require(location == FindLocation(scene) && location.name == "workbenchClark2" && location.liveLoadVehicles.Count == 0 &&
            vehicle.assignedContract == contract && vehicle.vehicleMass == contract.liveLoadWeight && !location.advancesTutorial &&
            vehicle.physicsManager != null && vehicle.startPoint != null && vehicle.endPoint != null &&
            !vehicle.startPoint.IsChildOf(vehicle.transform) && !vehicle.endPoint.IsChildOf(vehicle.transform), "Offset Road SUV contract/route binding is incomplete.");
        Require(vehicle.TryGetMultiLoadStartBounds(out Bounds bounds), "SUV needs a solid chassis collider.");
        vehicle.GetMultiLoadRoutePoses(out Vector3 from, out Vector3 to);
        Require(bounds.max.x < start.transform.position.x && Mathf.Abs(from.z - to.z) < .01f &&
            to.x + bounds.min.x - from.x > end.transform.position.x + .75f, "The SUV must start and park wholly off the bridge, without lane drift.");
        float offset = vehicle.transform.position.z - start.transform.position.z;
        Require(Mathf.Abs(offset) > 1f && Mathf.Abs(offset) + bounds.extents.z < contract.twoWayRoadWidth * .5f - .1f,
            "Keep the off-center SUV and its full width on the configured deck.");
        FinishLineTrigger finish = vehicle.endPoint.GetComponent<FinishLineTrigger>();
        Require(finish != null && finish.assignedContract == contract && finish.GetComponent<BoxCollider>().isTrigger,
            "SUV finish trigger is not assigned to Offset Road.");
        Vector3 wheelMean = vehicle.wheelObjects.Select(wheel => MeshCenter(wheel.GetComponentInChildren<MeshFilter>(true))).Aggregate(Vector3.zero, (sum, point) => sum + point) / 4f;
        Vector3 visualForward = FindNose(vehicle) - wheelMean;
        visualForward.y = 0f;
        Require(Vector3.Dot(visualForward.normalized, Vector3.right) > .99f, "The SUV grille must face its positive-X route.");
        foreach (GameObject wheel in vehicle.wheelObjects)
        {
            MeshFilter mesh = wheel.GetComponentInChildren<MeshFilter>(true);
            Vector3 center = MeshCenter(mesh);
            Vector3 worldAxis = wheel.transform.TransformDirection(vehicle.spinAxis).normalized;
            Require(Vector3.Dot(worldAxis, Vector3.forward) > .9f, "SUV wheel motor axis does not drive along positive X.");
            foreach (Vector3 shift in new[] { Vector3.zero, to - from })
            {
                Vector3 point = center + shift;
                Require(TryGround(scene, new Vector3(point.x, point.y - vehicle.wheelRadius, point.z), out float y) &&
                    Mathf.Abs(y - (point.y - vehicle.wheelRadius)) < .12f, "SUV wheel is not seated on a solid approach or parking road.");
            }
        }
        report.AppendLine($"PASS: Offset Road has one {contract.liveLoadWeight:0.##} kg SUV with 4 correctly aligned driven wheels (radius {vehicle.wheelRadius:0.###} m), 80 m span, lane offset {offset:0.###} m, straight fixed route {from:F2} -> {to:F2}, full finish clearance and grounded parking.");
        report.AppendLine($"PASS: SUV front identified from grille/headlight geometry faces +X; imported model yaw {vehicle.transform.eulerAngles.y:0.##} degrees, motor spin axis {vehicle.spinAxis:F2}.");
        report.AppendLine("Torsion is taught conceptually; the planar axial solver does not measure transverse twisting.");
    }

    private static Bounds MeasureChassis(LiveLoadVehicle vehicle, Vector3[] wheels, float radius)
    {
        var meshes = vehicle.GetComponentsInChildren<MeshFilter>(true).Where(mesh => mesh.sharedMesh != null &&
            !vehicle.wheelObjects.Any(wheel => mesh.transform.IsChildOf(wheel.transform))).ToArray();
        Require(meshes.Length > 0, "SUV body meshes are missing.");
        var points = meshes.SelectMany(mesh => Enumerable.Range(0, 8).Select(corner =>
            vehicle.transform.InverseTransformPoint(mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center + Vector3.Scale(mesh.sharedMesh.bounds.extents,
                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)))))).ToArray();
        Bounds result = new Bounds(points[0], Vector3.zero); foreach (Vector3 point in points) result.Encapsulate(point);
        Vector3 min = result.min;
        min.y = Mathf.Max(min.y, vehicle.transform.InverseTransformPoint(new Vector3(vehicle.transform.position.x,
            wheels.Min(p => p.y) - radius + .18f, vehicle.transform.position.z)).y);
        result.SetMinMax(min, result.max);
        return result;
    }
    private static Vector3 FindNose(LiveLoadVehicle vehicle)
    {
        foreach (string materialName in new[] { "grille", "headlight" })
        {
            var points = vehicle.GetComponentsInChildren<MeshFilter>(true).Where(mesh => mesh.sharedMesh != null)
                .SelectMany(mesh =>
                {
                    Renderer renderer = mesh.GetComponent<Renderer>();
                    if (renderer == null) return Array.Empty<Vector3>();
                    Material[] materials = renderer.sharedMaterials;
                    return Enumerable.Range(0, Mathf.Min(materials.Length, mesh.sharedMesh.subMeshCount))
                        .Where(index => materials[index] != null && materials[index].name.IndexOf(materialName, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(index => mesh.transform.TransformPoint(mesh.sharedMesh.GetSubMesh(index).bounds.center)).ToArray();
                }).ToArray();
            if (points.Length > 0) return points.Aggregate(Vector3.zero, (sum, point) => sum + point) / points.Length;
        }
        throw new InvalidOperationException("The imported SUV must retain its grille or headlight material so its front can be identified.");
    }
    private static bool TryGround(Scene scene, Vector3 point, out float height)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 7f, ~0, QueryTriggerInteraction.Ignore)
            .Where(hit => hit.collider.gameObject.scene == scene && hit.collider.GetComponentInParent<LiveLoadVehicle>() == null &&
                hit.normal.y >= .7f && Mathf.Abs(hit.point.y - point.y) < .6f).OrderBy(hit => Mathf.Abs(hit.point.y - point.y)).ToArray();
        height = hits.Length > 0 ? hits[0].point.y : 0f; return hits.Length > 0;
    }
    private static float WheelRadius(MeshFilter mesh)
    {
        Vector3 size = Vector3.Scale(mesh.sharedMesh.bounds.size, mesh.transform.lossyScale);
        float[] axes = { Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z) }; Array.Sort(axes);
        return (axes[1] + axes[2]) * .25f;
    }
    private static Vector3 MeshCenter(MeshFilter mesh) => mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center);
    private static bool HasAncestor(Transform transform, string name)
    { for (Transform value = transform; value != null; value = value.parent) if (value.name == name) return true; return false; }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
#endif
