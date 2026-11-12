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
internal static class ClarkOffsetTrafficSetup
{
    private const string Request = "Temp/clark-offset-traffic.request";
    private const string Report = "Logs/clark-offset-traffic.txt";
    private const string ContractPath = "Assets/BridgeBuilder/Data/Contract/IndustrialZone/Clark Offset Road Contract.asset";
    static ClarkOffsetTrafficSetup() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { Run(command); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }
    private static void Run(string command)
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode.");
        var contract = AssetDatabase.LoadAssetAtPath<ContractSO>(ContractPath);
        var location = ClarkOffsetSiteAuthoring.FindLocation(scene);
        var vehicles = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LiveLoadVehicle>(true))
            .Where(vehicle => vehicle.assignedContract == contract).OrderBy(vehicle => vehicle.transform.position.z)
            .ThenByDescending(vehicle => vehicle.transform.position.x).ToArray();
        var report = new StringBuilder("Scene dirty=" + scene.isDirty + "\n");
        foreach (var vehicle in vehicles)
            report.AppendLine(vehicle.name + " position=" + vehicle.transform.position.ToString("F3") + " mass=" + vehicle.vehicleMass +
                " start=" + (vehicle.startPoint == null ? "none" : vehicle.startPoint.position.ToString("F3")) +
                " end=" + (vehicle.endPoint == null ? "none" : vehicle.endPoint.position.ToString("F3")));
        if (command == "inspect")
        {
            foreach (var vehicle in vehicles)
            {
                report.AppendLine(vehicle.name + " scale=" + vehicle.transform.lossyScale.ToString("F4") +
                    " torque=" + vehicle.engineTorque + " speed=" + vehicle.maxSpeed + " radius=" + vehicle.wheelRadius +
                    " spin=" + vehicle.spinAxis + " rotation=" + vehicle.transform.eulerAngles);
                foreach (var wheel in vehicle.wheelObjects)
                {
                    if (wheel == null) { report.AppendLine("  MISSING WHEEL"); continue; }
                    var renderer = wheel.GetComponentInChildren<Renderer>();
                    report.AppendLine("  wheel=" + wheel.name + " position=" + wheel.transform.position.ToString("F3") +
                        " bounds=" + (renderer == null ? "none" : renderer.bounds.ToString("F3")));
                }
                foreach (var box in vehicle.GetComponentsInChildren<BoxCollider>(true).Where(c => !c.isTrigger))
                    report.AppendLine("  solid=" + box.name + " bounds=" + box.bounds.ToString("F3"));
                foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
                    report.AppendLine("  mesh=" + renderer.name + " bounds=" + renderer.bounds.ToString("F3"));
            }
            File.WriteAllText(Report, report.ToString()); return;
        }
        Require(command == "apply" || command == "validate", "Unknown traffic command.");
        Require(vehicles.Length >= 2 && vehicles.Length <= LiveLoadTestRoster.MaxVehicles, "Expected 2-5 assigned Offset Road vehicles.");
        if (command == "apply")
        {
            if (contract.HasMultipleLiveLoads)
            {
                Check(location, contract, report);
                File.WriteAllText(Report, report.ToString());
                return;
            }
            Require(!EditorUtility.IsDirty(contract), "Save the contract's Inspector edits first.");
            bool wasDirty = scene.isDirty;
            string backup = "LocalBackups/ClarkOffsetTraffic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(scene.path, backup + "/CanyonCrossing.unity");
            File.Copy(ContractPath, backup + "/ClarkOffset.asset");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure staggered Clark Offset traffic");
            try
            {
                Undo.RecordObjects(new UnityEngine.Object[] { contract, location }, "Configure Offset traffic");
                var anchors = ClarkOffsetSiteAuthoring.GetRoadAnchors(location);
                float middle = (anchors.start.transform.position.x + anchors.end.transform.position.x) * .5f;
                vehicles = vehicles.OrderBy(v => v.transform.position.x < middle ? 0 : 1).ThenBy(v => v.transform.position.x < middle ? -v.transform.position.x : v.transform.position.x).ToArray();
                foreach (bool fromLeft in new[] { true, false })
                {
                    var lane = vehicles.Where(v => (v.transform.position.x < middle) == fromLeft)
                        .OrderBy(v => fromLeft ? -v.transform.position.x : v.transform.position.x).ToArray();
                    float parkingOffset = 0f;
                    for (int index = lane.Length - 1; index >= 0; index--)
                    {
                        var vehicle = lane[index];
                        Require(vehicle.startPoint != null && vehicle.endPoint != null, "Missing vehicle route.");
                        Require(vehicle.TryGetMultiLoadStartBounds(out Bounds bounds), "Missing vehicle bounds.");
                        Undo.RecordObject(vehicle, "Assign independent Offset route");
                        var startMarker = UnityEngine.Object.Instantiate(vehicle.startPoint.gameObject, vehicle.startPoint.parent);
                        var endMarker = UnityEngine.Object.Instantiate(vehicle.endPoint.gameObject, vehicle.endPoint.parent);
                        Undo.RegisterCreatedObjectUndo(startMarker, "Create Offset start");
                        Undo.RegisterCreatedObjectUndo(endMarker, "Create Offset finish");
                        startMarker.name = vehicle.name + " Traffic Start";
                        endMarker.name = vehicle.name + " Traffic Finish";
                        startMarker.transform.SetPositionAndRotation(vehicle.transform.position, Quaternion.identity);
                        float finishX = fromLeft ? anchors.end.transform.position.x + 12f + parkingOffset : anchors.start.transform.position.x - 12f - parkingOffset;
                        endMarker.transform.SetPositionAndRotation(new Vector3(finishX, vehicle.transform.position.y, vehicle.transform.position.z), Quaternion.identity);
                        vehicle.startPoint = startMarker.transform; vehicle.endPoint = endMarker.transform;
                        var trigger = endMarker.GetComponent<FinishLineTrigger>();
                        Require(trigger != null, "Missing finish trigger.");
                        trigger.assignedContract = contract;
                        var box = endMarker.GetComponent<BoxCollider>();
                        if (box != null) { box.center = new Vector3(0f, 1.8f, 0f); box.size = new Vector3(1.25f, 5f, 5f); }
                        PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle);
                        parkingOffset += bounds.size.x + 2f;
                    }
                }
                var entries = vehicles.Select((vehicle, index) => new ContractLiveLoadEntry {
                    displayName = vehicle.name, emptyWeightKg = vehicle.vehicleMass > 0 ? vehicle.vehicleMass : contract.liveLoadWeight,
                    departureDelay = index * 3f }).ToList();
                contract.vehicleTestMode = ContractSO.VehicleTestMode.Together;
                contract.liveLoadTests = entries;
                location.liveLoadVehicles = vehicles.Select((vehicle, index) => new ContractLiveLoadBinding {
                    entryId = entries[index].entryId, vehicle = vehicle }).ToList();
                Check(location, contract, report);
                PrefabUtility.RecordPrefabInstancePropertyModifications(location);
                Undo.FlushUndoRecordObjects();
                EditorUtility.SetDirty(contract); EditorSceneManager.MarkSceneDirty(scene);
                if (!wasDirty) Require(EditorSceneManager.SaveScene(scene), "Could not save traffic bindings.");
                AssetDatabase.SaveAssetIfDirty(contract);
                Undo.CollapseUndoOperations(group);
                report.AppendLine(wasDirty ? "NEEDS_SAVE: existing scene edits preserved; save scene to retain bindings." : "SAVED: scene and contract.");
                report.AppendLine("Backup: " + backup);
            }
            catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
        }
        else Check(location, contract, report);
        File.WriteAllText(Report, report.ToString());
    }
    private static void Check(BuildLocation location, ContractSO contract, StringBuilder report)
    {
        var previous = location.activeContract;
        try
        {
            location.activeContract = contract;
            Require(LiveLoadTestRoster.TryCreate(location, contract, out var roster, out var reason, false), reason);
            Require(roster.Mode == ContractSO.VehicleTestMode.Together, "Expected Together mode.");
            foreach (var entry in roster.Participants) report.AppendLine("PASS: " + entry.DisplayName + " delay=" + entry.DepartureDelay + "s weight=" + entry.WeightKg);
        }
        finally { location.activeContract = previous; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
