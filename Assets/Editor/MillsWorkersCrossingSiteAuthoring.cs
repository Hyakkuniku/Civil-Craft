#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene authoring and validation for the Workers' Crossing live loads.</summary>
internal static class MillsWorkersCrossingSiteAuthoring
{
    private const string SiteRoot = "Mills MAIN CONTRACT (1)";
    private const string VehicleRoot = "Mills 2nd Contract Bus";
    private const string Action = "Author Mills Workers Crossing live load";
    private const string ContractId = "MillsWorkersCrossing";

    internal static BuildLocation FindLocation(Scene scene) => All<BuildLocation>(scene).Single(site => HasAncestor(site.transform, SiteRoot));
    private static LiveLoadVehicle FindVehicle(Scene scene) => All<LiveLoadVehicle>(scene).Single(vehicle => HasAncestor(vehicle.transform, VehicleRoot));
    internal static LiveLoadVehicle[] FindVehicles(Scene scene) => All<LiveLoadVehicle>(scene)
        .Where(vehicle => HasAncestor(vehicle.transform, VehicleRoot) ||
            vehicle.transform.root.name.Trim().StartsWith("mills worker bus ", StringComparison.OrdinalIgnoreCase))
        .OrderBy(vehicle => vehicle.transform.root.name, StringComparer.OrdinalIgnoreCase).ToArray();
    internal static (Point start, Point end) GetRoadAnchors(BuildLocation location)
    {
        Point start = location.startingAnchors.Single(point => point != null && point.name == "LeftAnchor");
        Point end = location.endingAnchors.Single(point => point != null && point.name == "RightAnchor");
        Require(start.originalIsAnchor && end.originalIsAnchor && end.transform.position.x > start.transform.position.x &&
            Mathf.Abs(start.transform.position.y - end.transform.position.y) < .05f && Mathf.Abs(start.transform.position.z - end.transform.position.z) < .05f,
            "Workers' Crossing needs its authored level X-axis road anchors.");
        return (start, end);
    }

    internal static void Inspect(Scene scene, StringBuilder report)
    {
        BuildLocation location = FindLocation(scene); var (start, end) = GetRoadAnchors(location);
        report.AppendLine($"WORKERS SITE {location.name} position={location.transform.position:F3}, anchors {start.transform.position:F3} -> {end.transform.position:F3}, span={end.transform.position.x - start.transform.position.x:0.###}m, contract={location.activeContract?.name}");
        foreach (LiveLoadVehicle vehicle in FindVehicles(scene))
            InspectVehicle(scene, vehicle, start, end, report);
    }

    private static void InspectVehicle(Scene scene, LiveLoadVehicle vehicle, Point start, Point end, StringBuilder report)
    {
        report.AppendLine($"LIVE LOAD {vehicle.name} position={vehicle.transform.position:F3}, rotation={vehicle.transform.eulerAngles:F2}, mass={vehicle.vehicleMass}, contract={vehicle.assignedContract?.name}, radius={vehicle.wheelRadius:0.###}, axis={vehicle.spinAxis:F3}");
        report.AppendLine($"  Start={(vehicle.startPoint != null ? vehicle.startPoint.position.ToString("F3") : "unassigned")}, End={(vehicle.endPoint != null ? vehicle.endPoint.position.ToString("F3") : "unassigned")}");
        vehicle.TryGetMultiLoadEnvelopeBounds(out Bounds envelope); report.AppendLine("  Physical envelope: " + envelope);
        foreach (var wheel in vehicle.wheelObjects ?? Array.Empty<GameObject>())
        {
            if (wheel == null) { report.AppendLine("  Missing wheel reference."); continue; }
            Vector3 center = WheelCenter(wheel);
            report.AppendLine($"  Wheel {wheel.name}: center={center:F3}, axisWorld={wheel.transform.TransformDirection(vehicle.spinAxis):F3}");
        }
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
            report.AppendLine($"  Mesh {filter.name} source={filter.sharedMesh?.name}, bounds={(filter.GetComponent<Renderer>() != null ? filter.GetComponent<Renderer>().bounds.ToString() : "none")}");
        foreach (float x in new[] { vehicle.transform.position.x, start.transform.position.x - 2f, end.transform.position.x + 2f, end.transform.position.x + 5f, end.transform.position.x + 10f })
            report.AppendLine($"  Ground X={x:F2}, Z={vehicle.transform.position.z:F2}: " + (TryGround(scene, new Vector3(x, start.transform.position.y + .025f, vehicle.transform.position.z), out float y) ? y.ToString("F3") : "none"));
        if (IsSUVPlaceholder(vehicle)) report.AppendLine("TEMPORARY: this exact authored live load still uses the copied SUV visual; it has not been replaced with a bus model.");
    }

    internal static void Apply(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        Require(scene.IsValid() && scene.isLoaded && location == FindLocation(scene), "Use Mills's exact second workbench.");
        Require(contract != null && contract.ContractID == ContractId && contract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle &&
            contract.vehicleTestMode == ContractSO.VehicleTestMode.Single && contract.winCondition == ContractSO.WinCondition.FinishLine &&
            contract.liveLoadWeight > 0f && !contract.allowVehicleCargo && contract.twoWayRoad && contract.twoWayRoadWidth >= 16f,
            "Workers' Crossing requires its positive single-vehicle Finish Line contract and authored wide deck.");
        Require(location.activeContract == null || location.activeContract == contract, "The workers' workbench belongs to another contract.");
        Require(location.liveLoadVehicles == null || location.liveLoadVehicles.Count == 0, "Review existing multi-load bindings before replacing them.");
        var (start, end) = GetRoadAnchors(location);
        Require(Mathf.Abs(contract.bridgeSpan - (end.transform.position.x - start.transform.position.x)) < .05f, "Match Workers' Crossing to its actual 100 m span.");
        LiveLoadVehicle vehicle = FindVehicle(scene);
        Require(vehicle.assignedContract == null || vehicle.assignedContract == contract || vehicle.assignedContract.ContractID == "ClarkOffsetRoad",
            "Only the explicitly copied Clark SUV or this workers' vehicle may be rebound.");
        Require(vehicle.startPoint != null && vehicle.endPoint != null && vehicle.startPoint != vehicle.endPoint &&
            !vehicle.startPoint.IsChildOf(vehicle.transform) && !vehicle.endPoint.IsChildOf(vehicle.transform) &&
            HasAncestor(vehicle.startPoint, VehicleRoot) && HasAncestor(vehicle.endPoint, VehicleRoot), "Preserve the workers' vehicle's own fixed route markers.");
        Require(vehicle.wheelObjects != null && vehicle.wheelObjects.Length >= 4 && vehicle.wheelObjects.Length <= 16 &&
            vehicle.wheelObjects.Distinct().Count() == vehicle.wheelObjects.Length && vehicle.wheelObjects.All(wheel => wheel != null && wheel.transform.IsChildOf(vehicle.transform)),
            "The workers' live load needs distinct authored wheels.");
        Require(vehicle.wheelRadius > .1f && vehicle.wheelRadius < 1.5f && vehicle.GetComponent<Rigidbody>() != null,
            "Keep the working wheel radius and Rigidbody on the workers' live load.");
        Require(vehicle.wheelObjects.All(wheel => Vector3.Dot(wheel.transform.TransformDirection(vehicle.spinAxis).normalized, Vector3.forward) > .9f),
            "Align the workers' wheel motor axes for positive-X travel before binding it.");
        Require(vehicle.TryGetMultiLoadEnvelopeBounds(out Bounds envelope), "The workers' vehicle needs a solid chassis.");
        float nose = envelope.max.x - vehicle.transform.position.x;
        float tail = envelope.min.x - vehicle.transform.position.x;
        var wheelOffsets = vehicle.wheelObjects.Select(wheel => WheelCenter(wheel) - vehicle.transform.position).ToArray();
        float bottomOffset = wheelOffsets.Min(point => point.y) - vehicle.wheelRadius;
        float laneZ = vehicle.transform.position.z;
        float deckY = start.transform.position.y + .025f;
        float startX = Mathf.Min(vehicle.transform.position.x, start.transform.position.x - nose - .75f);
        Require(FindLevelWheelGround(scene, wheelOffsets, startX, laneZ, deckY, out float startGround),
            "All workers' vehicle tires must start on a solid, level approach.");
        float stopX = 0f, stopGround = 0f; bool foundParking = false;
        for (int sample = 0; sample < 20 && !foundParking; sample++)
        {
            float candidate = end.transform.position.x - tail + 1.5f + sample * .5f;
            if (!FindLevelWheelGround(scene, wheelOffsets, candidate, laneZ, deckY, out float ground)) continue;
            stopX = candidate; stopGround = ground; foundParking = true;
        }
        Require(foundParking, "No level parking area supports all workers' vehicle tires beyond the crossing.");
        Transform startMarker = vehicle.startPoint, endMarker = vehicle.endPoint;
        FinishLineTrigger finish = endMarker.GetComponent<FinishLineTrigger>();
        Require(finish == null || finish.assignedContract == null || finish.assignedContract == contract || finish.assignedContract.ContractID == "ClarkOffsetRoad",
            "The copied finish belongs to an unrelated contract.");
        Undo.RecordObjects(new UnityEngine.Object[] { vehicle, vehicle.transform, vehicle.GetComponent<Rigidbody>(), startMarker, endMarker, location, location.gameObject }, Action);
        vehicle.transform.position = new Vector3(startX, startGround - bottomOffset + .025f, laneZ);
        startMarker.SetPositionAndRotation(vehicle.transform.position, Quaternion.identity);
        endMarker.SetPositionAndRotation(new Vector3(stopX, stopGround - bottomOffset + .025f, laneZ), Quaternion.identity);
        if (finish == null) finish = Undo.AddComponent<FinishLineTrigger>(endMarker.gameObject); else Undo.RecordObject(finish, Action);
        finish.assignedContract = contract;
        BoxCollider zone = finish.GetComponent<BoxCollider>(); Undo.RecordObject(zone, Action);
        zone.isTrigger = true; zone.enabled = true;
        zone.center = new Vector3(nose + .525f, stopGround + 2f - endMarker.position.y, 0f);
        zone.size = new Vector3(1.25f, 5f, Mathf.Min(contract.twoWayRoadWidth, 5f));
        vehicle.assignedContract = contract; vehicle.vehicleMass = contract.liveLoadWeight;
        vehicle.vehicleName = IsSUVPlaceholder(vehicle) ? "Bus (Temporary SUV)" : "Bus";
        vehicle.advancesTutorial = false; vehicle.GetComponent<Rigidbody>().mass = contract.liveLoadWeight;
        if (vehicle.physicsManager == null) vehicle.physicsManager = All<BridgePhysicsManager>(scene).Single(manager => manager.isActiveAndEnabled);
        Require(vehicle.physicsManager.gameObject.scene == scene, "Use this scene's Bridge Physics Manager.");
        location.gameObject.name = "workbenchMills2"; location.advancesTutorial = false;
        location.startingAnchors.RemoveAll(point => point == null); location.endingAnchors.RemoveAll(point => point == null);
        if (startMarker.parent == endMarker.parent && startMarker.parent != null)
        {
            Undo.RecordObject(startMarker.parent.gameObject, Action);
            startMarker.parent.gameObject.name = "Mills Workers Crossing Route";
            Mark(startMarker.parent.gameObject);
        }
        foreach (var target in new UnityEngine.Object[] { vehicle, vehicle.transform, vehicle.GetComponent<Rigidbody>(), startMarker, endMarker, location, location.gameObject, finish, zone }) Mark(target);
        Physics.SyncTransforms(); Validate(scene, location, contract, report);
    }

    internal static void Validate(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        if (contract.HasMultipleLiveLoads)
        {
            ValidateBusRoutes(scene, location, contract, report);
            return;
        }

        var vehicle = FindVehicle(scene); var (start, end) = GetRoadAnchors(location);
        Require(location == FindLocation(scene) && location.name == "workbenchMills2" && !location.advancesTutorial &&
            vehicle.assignedContract == contract && vehicle.vehicleMass == contract.liveLoadWeight && vehicle.physicsManager != null &&
            vehicle.startPoint != null && vehicle.endPoint != null, "Workers' Crossing scene bindings are incomplete.");
        Require(vehicle.TryGetMultiLoadStartBounds(out Bounds bounds), "The workers' vehicle needs a physical envelope.");
        vehicle.GetMultiLoadRoutePoses(out Vector3 from, out Vector3 to);
        Require(bounds.max.x < start.transform.position.x && to.x + bounds.min.x - from.x > end.transform.position.x + .5f && Mathf.Abs(from.z - to.z) < .01f,
            "The workers' vehicle must start before and fully park beyond the bridge without lane drift.");
        Require(Mathf.Abs(bounds.center.z - start.transform.position.z) + bounds.extents.z < contract.twoWayRoadWidth * .5f - .1f,
            "Keep the complete workers' vehicle within the deck edges.");
        var finish = vehicle.endPoint.GetComponent<FinishLineTrigger>();
        Require(finish != null && finish.assignedContract == contract, "The workers' finish still points to another contract.");
        BoxCollider zone = finish.GetComponent<BoxCollider>();
        Require(zone != null && zone.isTrigger && zone.enabled, "The workers' finish must use an enabled trigger.");
        float firstContactRoot = zone.bounds.min.x - (bounds.max.x - from.x);
        Require(firstContactRoot + bounds.min.x - from.x > end.transform.position.x + .5f, "Workers' Crossing would complete before the rear clears the bridge.");
        foreach (var wheel in vehicle.wheelObjects)
        {
            Vector3 center = WheelCenter(wheel);
            foreach (var shift in new[] { Vector3.zero, to - from })
            {
                Vector3 p = center + shift;
                bool ground = TryGround(scene, new Vector3(p.x, p.y - vehicle.wheelRadius, p.z), out float y);
                Require(ground && Mathf.Abs(y - (p.y - vehicle.wheelRadius)) < .12f, $"Workers' wheel {wheel.name} needs ground at {p:F3}.");
            }
        }
        report.AppendLine($"PASS: Workers' Crossing single {contract.liveLoadWeight:0.##} kg live load, {vehicle.wheelObjects.Length} wheels, 100 m site, lane offset {vehicle.transform.position.z - start.transform.position.z:0.###} m, route {from:F2} -> {to:F2}; grounded approaches, full-envelope finish and correct Mills contract.");
        if (IsSUVPlaceholder(vehicle)) report.AppendLine("TEMPORARY VISUAL: the scene's copied SUV represents the Bus test. Replace it with the final bus model/rig before claiming the bus visual is complete.");
    }

    private static void ValidateBusRoutes(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        Require(location == FindLocation(scene) && !location.advancesTutorial, "Use the Workers' Crossing workbench.");
        var (left, right) = GetRoadAnchors(location);
        var buses = FindVehicles(scene);
        Require(buses.Length == 2, "Workers' Crossing needs its two placed buses.");

        ContractSO previous = location.activeContract;
        LiveLoadTestRoster roster;
        try
        {
            location.activeContract = contract;
            Require(LiveLoadTestRoster.TryCreate(location, contract, out roster, out string reason), reason);
        }
        finally { location.activeContract = previous; }

        Require(roster.Mode == ContractSO.VehicleTestMode.Together && roster.Participants.Count == buses.Length &&
            buses.All(bus => roster.Participants.Any(entry => entry.Vehicle == bus)),
            "Bind both worker buses in the same test.");
        Require(buses.Select(bus => bus.startPoint).Distinct().Count() == 2 &&
            buses.Select(bus => bus.endPoint).Distinct().Count() == 2, "Each bus needs its own route markers.");
        Require(buses.Count(bus => bus.startPoint.position.x < bus.endPoint.position.x) == 1, "Start the buses on opposite banks.");

        var lanes = new List<Bounds>();
        foreach (var entry in roster.Participants)
        {
            LiveLoadVehicle bus = entry.Vehicle;
            bus.GetMultiLoadRoutePoses(out Vector3 from, out Vector3 to);
            Require(!bus.startPoint.IsChildOf(bus.transform) && !bus.endPoint.IsChildOf(bus.transform) &&
                Vector3.Distance(from, bus.startPoint.position) < .01f, $"Align the fixed route markers for {entry.DisplayName}.");
            Require(bus.TryGetMultiLoadStartBounds(out Bounds bounds), "Give each bus a solid chassis.");
            Require(Mathf.Abs(from.z - to.z) < .01f &&
                Mathf.Abs(bounds.center.z - left.transform.position.z) + bounds.extents.z < contract.twoWayRoadWidth * .5f,
                $"Keep {entry.DisplayName} in a straight lane within the deck.");
            Require(lanes.All(lane => bounds.max.z < lane.min.z || bounds.min.z > lane.max.z), "Keep the bus lanes clear of one another.");
            lanes.Add(bounds);

            var finish = bus.endPoint.GetComponent<FinishLineTrigger>();
            var zone = bus.endPoint.GetComponent<BoxCollider>();
            Require(finish != null && finish.enabled && finish.assignedContract == contract &&
                zone != null && zone.enabled && zone.isTrigger, $"Bind the {entry.DisplayName} finish trigger to Workers' Crossing.");
            Require(bus.GetComponentsInChildren<Collider>().Any(collider => collider.enabled && !collider.isTrigger &&
                !Physics.GetIgnoreLayerCollision(collider.gameObject.layer, zone.gameObject.layer)),
                $"Allow {entry.DisplayName} to collide with its finish trigger.");
            Bounds parked = bounds;
            parked.center += to - from;
            Require(zone.bounds.Intersects(parked), $"Place the {entry.DisplayName} trigger within reach of the bus at the end marker.");
            float length = bounds.size.x;
            Require(to.x > from.x ? zone.bounds.min.x - length > right.transform.position.x :
                zone.bounds.max.x + length < left.transform.position.x, $"Give {entry.DisplayName} room to clear the bridge before its finish trigger.");

            Require(bus.wheelObjects != null && bus.wheelObjects.Length == 4 && bus.wheelObjects.Distinct().Count() == 4,
                $"Bind all four wheels for {entry.DisplayName}.");
            foreach (var wheel in bus.wheelObjects)
            {
                var renderer = wheel != null ? wheel.GetComponentInChildren<Renderer>(true) : null;
                Require(renderer != null && wheel.transform.IsChildOf(bus.transform), $"Bind the physical wheels for {entry.DisplayName}.");
                float radius = renderer.bounds.extents.y;
                foreach (var shift in new[] { Vector3.zero, to - from })
                {
                    Vector3 contact = renderer.bounds.center + shift - Vector3.up * radius;
                    Require(TryGround(scene, contact, out float height) && Mathf.Abs(height - contact.y) < .12f,
                        $"Give {entry.DisplayName} wheel support at {contact:F3}.");
                }
            }
            report.AppendLine($"PASS: {entry.DisplayName}, {entry.WeightKg:0.##} kg, grounded route {from:F3} -> {to:F3} and clear finish.");
        }
    }

    private static bool FindLevelWheelGround(Scene scene, Vector3[] offsets, float x, float z, float referenceY, out float ground)
    {
        var heights = new List<float>();
        foreach (var offset in offsets)
        {
            if (!TryGround(scene, new Vector3(x + offset.x, referenceY, z + offset.z), out float y)) { ground = 0f; return false; }
            heights.Add(y);
        }
        ground = heights.Count > 0 ? heights.Average() : 0f;
        return heights.Count == offsets.Length && heights.Count > 0 && heights.Max() - heights.Min() <= .09f;
    }
    private static bool TryGround(Scene scene, Vector3 point, out float height)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 7f, ~0, QueryTriggerInteraction.Ignore)
            .Where(hit => hit.collider.gameObject.scene == scene && hit.collider.GetComponentInParent<LiveLoadVehicle>() == null &&
                hit.normal.y > .7f && Mathf.Abs(hit.point.y - point.y) < .6f).OrderBy(hit => Mathf.Abs(hit.point.y - point.y)).ToArray();
        height = hits.Length > 0 ? hits[0].point.y : 0f; return hits.Length > 0;
    }
    private static Vector3 WheelCenter(GameObject wheel)
    {
        var renderer = wheel.GetComponentInChildren<Renderer>(true);
        Require(renderer != null, "A workers' wheel has no visible mesh for its physical axle."); return renderer.bounds.center;
    }
    private static bool IsSUVPlaceholder(LiveLoadVehicle vehicle) => vehicle.GetComponentsInChildren<MeshFilter>(true)
        .Any(filter => filter.sharedMesh != null && filter.sharedMesh.name.IndexOf("SUV", StringComparison.OrdinalIgnoreCase) >= 0);
    private static bool HasAncestor(Transform value, string name) { for (; value != null; value = value.parent) if (value.name == name) return true; return false; }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Mark(UnityEngine.Object value) { EditorUtility.SetDirty(value); PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
#endif
