using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class FordTogetherSetup
{
    private const string Request = "Temp/ford-together.request";
    private const string Report = "Logs/ford-together.txt";
    private const string ActionName = "Set up Ford's two-truck test";

    [InitializeOnLoadMethod]
    private static void WatchRequest() => EditorApplication.update += CheckRequest;

    private static void CheckRequest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        Apply();
    }

    [MenuItem("Tools/Civil Craft/Story/Set Up Ford Together Test")]
    private static void Apply()
    {
        try { Configure(); }
        catch (Exception error) { File.WriteAllText(Report, error.ToString()); Debug.LogException(error); }
    }

    private static void Configure()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        if (!scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open CanyonCrossing in Edit Mode.");
        bool wasDirty = scene.isDirty;
        GameObject[] roots = scene.GetRootGameObjects();
        ContractSO contract = AssetDatabase.LoadAssetAtPath<ContractSO>(
            "Assets/BridgeBuilder/Data/Contract/TownRiver/Ford Contract.asset");
        BuildLocation location = roots.SelectMany(root => root.GetComponentsInChildren<BuildLocation>(true))
            .Single(site => site.name == "workbenchFord" &&
                site.GetComponentsInParent<Transform>(true).Any(parent => parent.name == "FordMainContract"));
        if (contract == null || (location.activeContract != null && location.activeContract != contract))
            throw new InvalidOperationException("Ford's workbench has an unexpected contract assignment.");
        BridgePhysicsManager physics = roots.SelectMany(root => root.GetComponentsInChildren<BuildUIController>(true))
            .Single(controller => controller.physicsManager != null).physicsManager;
        LiveLoadVehicle lead = Vehicle(roots, "FordCar");
        LiveLoadVehicle rear = Vehicle(roots, "FordCar (1)");
        if (new[] { lead, rear }.Any(vehicle => vehicle.assignedContract != contract ||
            vehicle.startPoint == null || vehicle.endPoint == null ||
            vehicle.endPoint.GetComponent<FinishLineTrigger>()?.assignedContract != contract ||
            vehicle.wheelObjects == null || vehicle.wheelObjects.Length != 6))
            throw new InvalidOperationException("Expected two six-wheel Ford trucks with their own Ford finish triggers.");
        if (lead.endPoint == rear.endPoint || lead.transform.position.x <= rear.transform.position.x ||
            !LiveLoadTestRoster.TryGetSiteSpan(location, out float bridgeStart, out float bridgeEnd))
            throw new InvalidOperationException("Expected separate Ford routes with the duplicate behind the lead truck.");
        if (contract.HasMultipleLiveLoads)
        {
            Verify(location, contract, lead, rear);
            File.WriteAllText(Report, "PASS: Ford's Together test is already configured.");
            return;
        }
        if (contract.liveLoadMode != ContractSO.LiveLoadMode.Vehicle || contract.winCondition != ContractSO.WinCondition.FinishLine ||
            contract.allowVehicleCargo || contract.liveLoadWeight <= 0 ||
            contract.liveLoadTests.Count != 0 || location.liveLoadVehicles.Count != 0)
            throw new InvalidOperationException("Ford's existing contract settings need review before applying.");

        var leadEntry = new ContractLiveLoadEntry { displayName = "Ford Truck 1", emptyWeightKg = contract.liveLoadWeight };
        var rearEntry = new ContractLiveLoadEntry { displayName = "Ford Truck 2", emptyWeightKg = contract.liveLoadWeight };
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(ActionName);
        float leadStop, rearStop;
        try
        {
            EnsureChassis(lead);
            EnsureChassis(rear);
            Bounds leadBounds = Footprint(lead);
            Bounds rearBounds = Footprint(rear);
            Vector3 oldLeadStop = StopPose(lead);
            Vector3 oldRearStop = StopPose(rear);
            float rearTail = rear.transform.position.x - rearBounds.min.x;
            float rearNose = rearBounds.max.x - rear.transform.position.x;
            float leadTail = lead.transform.position.x - leadBounds.min.x;
            rearStop = Mathf.Max(Mathf.Min(oldLeadStop.x, oldRearStop.x), bridgeEnd + rearTail + .75f);
            leadStop = Mathf.Max(oldLeadStop.x, rearStop + rearNose + leadTail + 1f);
            float startShift = Mathf.Min(0f, bridgeStart - leadBounds.max.x - .5f);
            leadBounds.center += Vector3.right * startShift;
            if (leadBounds.Intersects(rearBounds) || rearBounds.max.x > bridgeStart + .01f)
                throw new InvalidOperationException("Ford's two trucks need separate starting space before the crossing.");
            Physics.SyncTransforms();
            CheckLanding(scene, rear, oldRearStop, rearStop);
            CheckLanding(scene, lead, oldLeadStop, leadStop);
            Undo.RecordObjects(new UnityEngine.Object[]
            {
                contract, location, lead, rear, lead.transform, lead.startPoint, lead.endPoint, rear.endPoint
            }, ActionName);
            lead.transform.position += Vector3.right * startShift;
            lead.startPoint.position += Vector3.right * startShift;
            lead.endPoint.position += Vector3.right * (leadStop - oldLeadStop.x);
            rear.endPoint.position += Vector3.right * (rearStop - oldRearStop.x);
            lead.physicsManager = rear.physicsManager = physics;
            contract.vehicleTestMode = ContractSO.VehicleTestMode.Together;
            contract.liveLoadTests = new List<ContractLiveLoadEntry> { leadEntry, rearEntry };
            location.liveLoadVehicles = new List<ContractLiveLoadBinding>
            {
                new ContractLiveLoadBinding { entryId = leadEntry.entryId, vehicle = lead },
                new ContractLiveLoadBinding { entryId = rearEntry.entryId, vehicle = rear }
            };
            foreach (UnityEngine.Object target in new UnityEngine.Object[]
                { location, lead, rear, lead.transform, lead.startPoint, lead.endPoint, rear.endPoint })
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            Verify(location, contract, lead, rear);
            Undo.FlushUndoRecordObjects();
            EditorUtility.SetDirty(contract);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty)
            {
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save CanyonCrossing.");
                AssetDatabase.SaveAssetIfDirty(contract);
            }
            Undo.CollapseUndoOperations(group);
        }
        catch
        {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(group);
            throw;
        }
        string result = $"PASS: Together, two trucks departing at 0 seconds, {contract.liveLoadWeight:g} kg each. " +
            $"Start separation {lead.transform.position.x - rear.transform.position.x:F2} m. " +
            $"Bridge ends at X {bridgeEnd:F2}. Parking X {rearStop:F2} and {leadStop:F2}. " +
            (wasDirty ? "Scene and contract left dirty for the user's save." : "CanyonCrossing and Ford Contract saved.");
        File.WriteAllText(Report, result);
        Debug.Log(result, location);
    }

    private static LiveLoadVehicle Vehicle(GameObject[] roots, string name) =>
        roots.Single(root => root.name == name).GetComponentsInChildren<LiveLoadVehicle>(true).Single();

    private static void EnsureChassis(LiveLoadVehicle vehicle)
    {
        Transform visual = vehicle.transform.Find("Ford Truck");
        if (visual == null) throw new InvalidOperationException("The Ford Truck visual root is missing.");
        if (!visual.gameObject.activeSelf)
        {
            Undo.RecordObject(visual.gameObject, ActionName);
            visual.gameObject.SetActive(true);
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual.gameObject);
        }
        BoxCollider chassis = visual.GetComponent<BoxCollider>();
        if (chassis == null)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(visual);
            BoxCollider authored = source != null ? source.GetComponent<BoxCollider>() : null;
            if (authored == null) throw new InvalidOperationException("The Ford prefab chassis collider is missing.");
            chassis = Undo.AddComponent<BoxCollider>(visual.gameObject);
            chassis.center = authored.center;
            chassis.size = authored.size;
            chassis.sharedMaterial = authored.sharedMaterial;
        }
        if (chassis.enabled && !chassis.isTrigger) return;
        Undo.RecordObject(chassis, ActionName);
        chassis.enabled = true;
        chassis.isTrigger = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(chassis);
    }

    private static Bounds Footprint(LiveLoadVehicle vehicle)
    {
        if (!vehicle.TryGetMultiLoadStartBounds(out Bounds bounds))
        {
            string details = string.Join(", ", vehicle.GetComponentsInChildren<Collider>(true).Select(
                collider => $"{collider.name}/{collider.GetType().Name} enabled={collider.enabled} trigger={collider.isTrigger} self={collider.gameObject.activeSelf}"));
            BoxCollider direct = vehicle.transform.Find("Ford Truck")?.GetComponent<BoxCollider>();
            throw new InvalidOperationException($"{vehicle.transform.root.name} needs its chassis collider. Descendant colliders: {details}. Direct chassis: {direct}, activeSelf={direct?.gameObject.activeSelf}, enabled={direct?.enabled}, trigger={direct?.isTrigger}.");
        }
        foreach (GameObject wheel in vehicle.wheelObjects)
            bounds.Encapsulate(wheel.GetComponentInChildren<Renderer>().bounds);
        return bounds;
    }

    private static Vector3 StopPose(LiveLoadVehicle vehicle)
    {
        Vector3 startOffset = vehicle.transform.position - vehicle.startPoint.position;
        return vehicle.endPoint.position + vehicle.endPoint.rotation *
            (Quaternion.Inverse(vehicle.startPoint.rotation) * startOffset);
    }

    private static void CheckLanding(Scene scene, LiveLoadVehicle vehicle, Vector3 oldStop, float newStopX)
    {
        Vector3 stop = new Vector3(newStopX, oldStop.y, vehicle.transform.position.z);
        var hits = new RaycastHit[32];
        foreach (GameObject wheel in vehicle.wheelObjects)
        {
            Bounds wheelBounds = wheel.GetComponentInChildren<Renderer>().bounds;
            Vector3 center = stop + wheelBounds.center - vehicle.transform.position;
            Vector3 origin = center + Vector3.up * 3f;
            int count = scene.GetPhysicsScene().Raycast(origin, Vector3.down, hits, 9f, ~0, QueryTriggerInteraction.Ignore);
            bool supported = false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.GetComponentInParent<LiveLoadVehicle>() == null &&
                    Mathf.Abs(hits[i].point.y - (center.y - vehicle.wheelRadius)) <= 2f)
                    supported = true;
            }
            if (!supported) throw new InvalidOperationException($"No solid landing beneath {vehicle.transform.root.name} near X {center.x:F2}.");
        }
    }

    private static void Verify(BuildLocation location, ContractSO contract, LiveLoadVehicle lead, LiveLoadVehicle rear)
    {
        ContractSO previous = location.activeContract;
        LiveLoadTestRoster roster;
        try
        {
            // The NPC assigns the contract at acceptance. Validate that state
            // without pre-accepting Ford's contract in the saved scene.
            location.activeContract = contract;
            if (!LiveLoadTestRoster.TryCreate(location, contract, out roster, out string reason, requireCargo: false))
                throw new InvalidOperationException(reason);
        }
        finally { location.activeContract = previous; }
        if (roster.Mode != ContractSO.VehicleTestMode.Together || roster.Participants.Count != 2 ||
            roster.Participants[0].Vehicle != lead || roster.Participants[1].Vehicle != rear ||
            roster.Participants.Any(entry => entry.DepartureDelay != 0f))
            throw new InvalidOperationException("Ford's simultaneous roster failed verification.");
    }
}
