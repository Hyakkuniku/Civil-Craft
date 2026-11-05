using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Author the existing Freight Passage truck and its single-vehicle finish route.</summary>
internal static class ClarkFreightSiteAuthoring
{
    private const string LocationName = "workbenchClark1";
    private const string TruckName = "cargo truck clark 1";
    private const string RouteName = "Clark Freight Truck Route";
    private const float DeckSurfaceOffset = .025f;

    internal static BuildLocation FindLocation(Scene scene) =>
        Unique(scene, (BuildLocation location) => location.name == LocationName, LocationName);

    internal static (Point start, Point end) GetRoadAnchors(BuildLocation location)
    {
        Point[] left = location.startingAnchors.Where(p => p != null && p.name == "LeftAnchor").ToArray();
        Point[] right = location.endingAnchors.Where(p => p != null && p.name == "RightAnchor").ToArray();
        Require(left.Length == 1 && right.Length == 1, "Freight Passage needs one LeftAnchor and one RightAnchor.");
        Vector3 a = left[0].transform.position;
        Vector3 b = right[0].transform.position;
        Require(left[0].originalIsAnchor && right[0].originalIsAnchor && b.x > a.x &&
            Mathf.Abs(b.z - a.z) < .03f && Mathf.Abs(b.y - a.y) < .05f,
            "Clark's road endpoints must share the same X-axis deck line.");
        return (left[0], right[0]);
    }

    internal static void Preflight(Scene scene, BuildLocation location, ContractSO contract)
    {
        Require(scene.IsValid() && scene.isLoaded && location == FindLocation(scene),
            "Open the scene containing Clark's authored workbench.");
        (Point start, Point end) = GetRoadAnchors(location);
        Require(contract != null && contract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle &&
            contract.vehicleTestMode == ContractSO.VehicleTestMode.Single &&
            contract.winCondition == ContractSO.WinCondition.FinishLine &&
            !contract.allowVehicleCargo && contract.minimumLoadedCargo == 0 &&
            !float.IsNaN(contract.liveLoadWeight) && !float.IsInfinity(contract.liveLoadWeight) && contract.liveLoadWeight > 0f &&
            Mathf.Abs(contract.bridgeSpan - (end.transform.position.x - start.transform.position.x)) < .05f,
            "Freight Passage needs a positive single-truck finish-line load and the authored road span.");
        Require(location.activeContract == null || location.activeContract == contract,
            "Clark's workbench already belongs to another contract.");
        Require(location.liveLoadVehicles == null || location.liveLoadVehicles.Count == 0,
            "Remove the unrelated multi-load bindings from Clark's single-truck site before applying.");

        LiveLoadVehicle truck = FindTruck(scene);
        Require(truck.assignedContract == null || truck.assignedContract == contract,
            "The authored Clark truck already belongs to another contract.");
        Require(truck.isActiveAndEnabled && truck.GetComponent<Rigidbody>() != null,
            "Clark's truck must be active with a Rigidbody.");
        Require(truck.wheelObjects != null && truck.wheelObjects.Length == 6 &&
            truck.wheelObjects.Distinct().Count() == 6 && truck.wheelObjects.All(w =>
                w != null && w.transform.IsChildOf(truck.transform) && w.GetComponentInChildren<MeshFilter>(true) != null),
            "Clark's truck requires the six authored wheel meshes.");
        Require(truck.wheelObjects.Count(IsFrontWheel) == 2 && truck.maxSpeed > 0f && truck.engineTorque > 0f,
            "The two front wheels, positive speed, and engine torque are required for Clark's truck.");
        Require(!All<LiveLoadVehicle>(scene).Any(v => v != truck && v.assignedContract == contract),
            "Another scene truck is already assigned to Clark's contract.");
        Require(location.transform.parent != null, "Clark's workbench needs its authored site parent.");
        _ = Unique(scene, (BridgePhysicsManager manager) => manager.isActiveAndEnabled, "scene bridge physics manager");

        Transform route = FindRoute(location);
        FinishLineTrigger[] assignedFinishes = All<FinishLineTrigger>(scene).Where(f => f.assignedContract == contract).ToArray();
        if (route == null)
            Require(truck.startPoint == null && truck.endPoint == null && assignedFinishes.Length == 0,
                "Clark already has a custom route or finish trigger. Preserve and review it before applying.");
        else
            Require(truck.assignedContract == contract && truck.startPoint == route.Find("Start") &&
                truck.endPoint == route.Find("Finish") && assignedFinishes.Length == 1 &&
                assignedFinishes[0].transform == truck.endPoint,
                "Clark's existing route does not match its vehicle and finish binding.");
    }

    internal static void Apply(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        Preflight(scene, location, contract);
        LiveLoadVehicle truck = FindTruck(scene);
        if (FindRoute(location) == null)
        {
            (Point start, Point end) = GetRoadAnchors(location);
            Vector3 deckStart = start.transform.position;
            Vector3 deckEnd = end.transform.position;
            var wheels = truck.wheelObjects.Select(w => w.GetComponentInChildren<MeshFilter>(true)).ToArray();
            Undo.RecordObject(truck.transform, "Align Clark's cargo truck to the freight road");
            Vector3 front = Mean(wheels.Where((_, i) => IsFrontWheel(truck.wheelObjects[i])).Select(MeshCenter).ToArray());
            Vector3 rear = Mean(wheels.Where((_, i) => !IsFrontWheel(truck.wheelObjects[i])).Select(MeshCenter).ToArray());
            Vector3 forward = Vector3.ProjectOnPlane(front - rear, Vector3.up);
            Require(forward.sqrMagnitude > 1f, "The Clark truck's front and rear axles could not be resolved.");
            truck.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(forward, Vector3.right, Vector3.up), Vector3.up) *
                truck.transform.rotation;

            float radius = wheels.Select(WheelRadius).Average();
            Require(radius > .2f && radius < 1.25f, "Unexpected imported wheel size on Clark's truck.");
            Vector3[] centers = wheels.Select(MeshCenter).ToArray();
            Vector3[] axleGuides = truck.wheelObjects.Select(w => w.transform.InverseTransformDirection(Vector3.forward)).ToArray();
            Require(axleGuides.All(axis => Vector3.Dot(axis, axleGuides[0]) > .9f),
                "The imported Clark wheels do not share a consistent axle axis.");

            Bounds bodyBounds = MeasureBody(truck, centers, radius);
            float startZ = truck.transform.position.z + deckStart.z - Mean(centers).z;
            float bodyCenterX = truck.transform.TransformPoint(bodyBounds.center).x;
            float bodyHalfLength = WorldHalfLength(truck.transform, bodyBounds);
            float envelopeMaxX = Mathf.Max(centers.Max(p => p.x) + radius, bodyCenterX + bodyHalfLength);
            float envelopeMinX = Mathf.Min(centers.Min(p => p.x) - radius, bodyCenterX - bodyHalfLength);
            float frontOffset = envelopeMaxX - truck.transform.position.x;
            float startX = Mathf.Min(truck.transform.position.x, deckStart.x - frontOffset - .75f);
            float deckY = deckStart.y + DeckSurfaceOffset;
            float surfaceY = TryGetGround(scene, new Vector3(startX, deckY, deckStart.z), out float ground) ? ground : deckY;
            float restingY = centers.Min(p => p.y) - radius;
            truck.transform.position = new Vector3(startX, truck.transform.position.y + surfaceY - restingY + .025f, startZ);

            BoxCollider body = truck.GetComponent<BoxCollider>();
            if (body == null)
            {
                body = Undo.AddComponent<BoxCollider>(truck.gameObject);
                body.center = bodyBounds.center;
                body.size = bodyBounds.size;
            }
            Undo.RecordObject(truck.gameObject, "Make Clark's truck available for inspection");
            truck.gameObject.layer = LayerMask.NameToLayer("Interactable");
            PrefabUtility.RecordPrefabInstancePropertyModifications(truck.gameObject);

            Transform route = Create(scene, location.transform.parent, RouteName, truck.transform.position, Quaternion.identity);
            // Both markers are world aligned. Matching marker rotations preserve the truck's
            // authored pose while keeping the finish collider's narrow axis along the road.
            Transform startMarker = Create(scene, route, "Start", truck.transform.position, Quaternion.identity);
            float finishX = deckEnd.x + Mathf.Max(8f, envelopeMaxX - envelopeMinX) + 2f;
            float finishSurfaceY = TryGetGround(scene, new Vector3(finishX, deckY, deckStart.z), out ground) ? ground : deckY;
            Transform finishMarker = Create(scene, route, "Finish",
                new Vector3(finishX, truck.transform.position.y + finishSurfaceY - surfaceY, startZ), Quaternion.identity);
            FinishLineTrigger finish = Undo.AddComponent<FinishLineTrigger>(finishMarker.gameObject);
            finish.assignedContract = contract;
            BoxCollider zone = finish.GetComponent<BoxCollider>();
            zone.center = finishMarker.InverseTransformPoint(new Vector3(finishX, finishSurfaceY + 2f, deckStart.z));
            zone.size = new Vector3(1.25f, 5f, 5f);
            zone.isTrigger = true;

            Undo.RecordObject(truck, "Bind Clark's single cargo truck and finish route");
            truck.assignedContract = contract;
            truck.vehicleName = "Cargo Truck";
            truck.vehicleMass = contract.liveLoadWeight;
            truck.wheelRadius = radius;
            // The wheel motor uses a negative hinge speed for positive X travel.
            truck.spinAxis = axleGuides[0].normalized;
            truck.startPoint = startMarker;
            truck.endPoint = finishMarker;
            truck.physicsManager = Unique(scene, (BridgePhysicsManager manager) => manager.isActiveAndEnabled, "scene bridge physics manager");
            PrefabUtility.RecordPrefabInstancePropertyModifications(truck);
            PrefabUtility.RecordPrefabInstancePropertyModifications(truck.transform);
        }

        if (location.advancesTutorial)
        {
            Undo.RecordObject(location, "Use normal build progression for Clark's contract");
            location.advancesTutorial = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(location);
        }
        Validate(scene, location, contract, report);
    }

    internal static void Validate(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        if (contract != null && contract.HasMultipleLiveLoads)
        {
            ClarkFourVehicleSetup.Check(scene, location, contract, report);
            return;
        }
        Preflight(scene, location, contract);
        LiveLoadVehicle truck = FindTruck(scene);
        (Point start, Point end) = GetRoadAnchors(location);
        Require(truck.assignedContract == contract && truck.vehicleName == "Cargo Truck" && truck.physicsManager != null &&
            truck.startPoint != null && truck.endPoint != null && !truck.startPoint.IsChildOf(truck.transform) &&
            !truck.endPoint.IsChildOf(truck.transform) && !location.advancesTutorial &&
            truck.GetComponent<BoxCollider>() is BoxCollider body && body.enabled && !body.isTrigger,
            "Clark's truck needs its contract, physics, solid chassis, and two fixed waypoints.");
        FinishLineTrigger finish = truck.endPoint.GetComponent<FinishLineTrigger>();
        BoxCollider zone = finish != null ? finish.GetComponent<BoxCollider>() : null;
        Physics.SyncTransforms();
        Require(finish != null && finish.isActiveAndEnabled && zone != null && zone.enabled && zone.isTrigger &&
            finish.assignedContract == contract && truck.startPoint.position.x < start.transform.position.x &&
            Quaternion.Angle(truck.startPoint.rotation, truck.endPoint.rotation) < .01f &&
            zone.bounds.min.x > end.transform.position.x + truck.GetComponent<BoxCollider>().bounds.size.x,
            "Clark's finish trigger must be far enough onto the warehouse bank for the whole truck to clear the bridge.");
        report.AppendLine($"PASS: Freight Passage has one {truck.TotalTestWeight:0.##} kg Cargo Truck, fixed waypoints, and a finish trigger {truck.endPoint.position.x - end.transform.position.x:0.##} m beyond the {contract.bridgeSpan:0.##} m span.");
        DescribeApproaches(scene, truck, start.transform.position, end.transform.position, report);
    }

    private static void DescribeApproaches(Scene scene, LiveLoadVehicle truck, Vector3 left, Vector3 right, StringBuilder report)
    {
        Physics.SyncTransforms();
        float deckY = left.y + DeckSurfaceOffset;
        var testPoints = new[] { truck.startPoint.position.x, left.x - 2f, left.x - .2f,
            right.x + .2f, right.x + 2f, truck.endPoint.position.x };
        var groundSamples = testPoints.Select(x => TryGetGround(scene, new Vector3(x, deckY, left.z), out float y)
            ? $"{x:0.##}={y:0.##}m" : $"{x:0.##}=no ground");
        report.AppendLine($"Clark approach samples at deck Y {deckY:0.###} m: {string.Join(", ", groundSamples)}.");
    }

    private static bool TryGetGround(Scene scene, Vector3 position, out float height)
    {
        const float probeHeight = 3f;
        int mask = LayerMask.GetMask("Default", "Environment", "Ground");
        if (scene.GetPhysicsScene().Raycast(position + Vector3.up * probeHeight, Vector3.down,
            out RaycastHit hit, probeHeight + 5f, mask, QueryTriggerInteraction.Ignore) &&
            hit.collider.gameObject.scene == scene && hit.normal.y >= .7f && Mathf.Abs(hit.point.y - position.y) < .6f)
        {
            height = hit.point.y;
            return true;
        }
        height = 0f;
        return false;
    }

    private static Bounds MeasureBody(LiveLoadVehicle truck, Vector3[] wheels, float radius)
    {
        var meshes = truck.GetComponentsInChildren<MeshFilter>(true).Where(mesh => mesh.sharedMesh != null &&
            !truck.wheelObjects.Any(wheel => mesh.transform.IsChildOf(wheel.transform))).ToArray();
        Require(meshes.Length > 0, "Clark's truck has no chassis mesh.");
        var points = meshes.SelectMany(mesh =>
        {
            Bounds bounds = mesh.sharedMesh.bounds;
            return Enumerable.Range(0, 8).Select(corner => truck.transform.InverseTransformPoint(mesh.transform.TransformPoint(new Vector3(
                (corner & 1) == 0 ? bounds.min.x : bounds.max.x, (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                (corner & 4) == 0 ? bounds.min.z : bounds.max.z))));
        }).ToArray();
        Bounds body = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points) body.Encapsulate(point);
        Vector3 min = body.min;
        float tireBottom = wheels.Min(p => p.y) - radius;
        min.y = Mathf.Max(min.y, truck.transform.InverseTransformPoint(new Vector3(truck.transform.position.x,
            tireBottom + .18f, truck.transform.position.z)).y);
        body.SetMinMax(min, body.max);
        return body;
    }

    private static float WorldHalfLength(Transform transform, Bounds bounds)
    {
        Vector3 e = bounds.extents;
        return Mathf.Abs(transform.TransformVector(Vector3.right * e.x).x) +
               Mathf.Abs(transform.TransformVector(Vector3.up * e.y).x) +
               Mathf.Abs(transform.TransformVector(Vector3.forward * e.z).x);
    }

    private static float WheelRadius(MeshFilter wheel)
    {
        Vector3 size = Vector3.Scale(wheel.sharedMesh.bounds.size, wheel.transform.lossyScale);
        float[] axes = { Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z) };
        Array.Sort(axes);
        return (axes[1] + axes[2]) * .25f;
    }

    private static bool IsFrontWheel(GameObject wheel) => wheel.name.StartsWith("Wheel_F_", StringComparison.OrdinalIgnoreCase);
    private static Vector3 MeshCenter(MeshFilter mesh) => mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center);
    private static Vector3 Mean(Vector3[] points) => points.Aggregate(Vector3.zero, (sum, p) => sum + p) / points.Length;
    private static Transform FindRoute(BuildLocation location) => location.transform.parent.Find(RouteName);
    private static LiveLoadVehicle FindTruck(Scene scene) => Unique(scene, (LiveLoadVehicle truck) => truck.name == TruckName, TruckName);

    private static Transform Create(Scene scene, Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        var obj = new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        Undo.RegisterCreatedObjectUndo(obj, "Create " + name);
        Undo.SetTransformParent(obj.transform, parent, "Place " + name);
        obj.transform.SetPositionAndRotation(position, rotation);
        return obj.transform;
    }

    private static T Unique<T>(Scene scene, Func<T, bool> predicate, string label) where T : Component
    {
        T[] found = All<T>(scene).Where(predicate).ToArray();
        Require(found.Length == 1, $"Expected one {label} in {scene.name}, found {found.Length}.");
        return found[0];
    }

    private static T[] All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
