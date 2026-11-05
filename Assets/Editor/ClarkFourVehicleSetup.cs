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

[InitializeOnLoad]
internal static class ClarkFourVehicleSetup
{
    private const string Request = "Temp/clark-four-vehicles.request";
    private const string Report = "Logs/clark-four-vehicles.txt";
    internal const string ContractPath = "Assets/BridgeBuilder/Data/Contract/IndustrialZone/Clark Contract.asset";
    static ClarkFourVehicleSetup() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if (command == "inspect") Inspect(); else if (command == "validate") Validate(); else if (command == "apply") Apply(); else throw new InvalidOperationException("Unknown Clark command."); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }
    private static Scene Scene()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode.");
        return scene;
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static LiveLoadVehicle[] Trucks(Scene scene) => All<LiveLoadVehicle>(scene).Where(v => v.name.StartsWith("cargo truck clark 1", StringComparison.Ordinal)).OrderBy(v => v.transform.position.z).ThenBy(v => v.transform.position.x).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Civil Craft/Story/Clark/Inspect Four Freight Vehicles")]
    public static void Inspect()
    {
        var scene = Scene();
        var location = All<BuildLocation>(scene).Single(v => v.name == "workbenchClark1");
        var output = new StringBuilder($"Clark inspection, scene dirty={scene.isDirty}\n");
        var (start, end) = ClarkFreightSiteAuthoring.GetRoadAnchors(location);
        output.AppendLine($"Anchors {start.transform.position:F3} -> {end.transform.position:F3}");
        foreach (var v in Trucks(scene))
        {
            v.TryGetMultiLoadStartBounds(out Bounds bounds);
            output.AppendLine($"ID={v.GetInstanceID()} {v.name} position={v.transform.position:F3} rotation={v.transform.eulerAngles:F2} weight={v.vehicleMass} contract={v.assignedContract?.name} bounds={bounds}");
            output.AppendLine($"  start={v.startPoint?.position:F3} finish={v.endPoint?.position:F3} spin={v.spinAxis:F2} radius={v.wheelRadius} wheels={v.wheelObjects?.Length}");
        }
        File.WriteAllText(Report, output.ToString());
    }
    [MenuItem("Tools/Civil Craft/Story/Clark/Apply Four Freight Vehicles")]
    public static void Apply()
    {
        var scene = Scene();
        var location = All<BuildLocation>(scene).Single(v => v.name == "workbenchClark1");
        var contract = AssetDatabase.LoadAssetAtPath<ContractSO>(ContractPath);
        var trucks = Trucks(scene);
        Require(trucks.Length == 4 && contract != null && contract.twoWayRoad && contract.winCondition == ContractSO.WinCondition.FinishLine,
            "Expected Clark's four trucks, two-way deck, and Finish Line contract.");
        if (contract.HasMultipleLiveLoads) { Validate(); return; }
        Require(contract.liveLoadTests.Count == 0 && location.liveLoadVehicles.Count == 0 && !EditorUtility.IsDirty(contract),
            "Save contract edits and review existing multi-load entries before applying.");
        var (start, end) = ClarkFreightSiteAuthoring.GetRoadAnchors(location);
        float middle = (start.transform.position.x + end.transform.position.x) * .5f;
        var west = trucks.Where(v => v.transform.position.x < middle).OrderByDescending(v => v.transform.position.x).ToArray();
        var east = trucks.Where(v => v.transform.position.x > middle).OrderBy(v => v.transform.position.x).ToArray();
        Require(west.Length == 2 && east.Length == 2 && trucks.All(v => v.assignedContract == contract && v.startPoint != null && v.endPoint != null &&
            v.physicsManager != null && v.endPoint.GetComponent<FinishLineTrigger>()?.assignedContract == contract), "Expected two trucks on each bank, each with its own Clark route and finish trigger.");
        Require(trucks.Select(v => v.startPoint).Distinct().Count() == 4 && trucks.Select(v => v.endPoint).Distinct().Count() == 4,
            "Every truck needs independent route markers.");
        Require(trucks.All(v => Mathf.Abs(v.transform.position.z - start.transform.position.z) < contract.twoWayRoadWidth * .5f - 2f),
            "Keep the trucks inside the two-way deck edges.");
        bool wasDirty = scene.isDirty;
        string backup = "LocalBackups/ClarkFourVehicles-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup);
        File.Copy(scene.path, backup + "/CanyonCrossing.unity");
        File.Copy(ContractPath, backup + "/Clark Contract.asset");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clark four-truck two-way test");
        try
        {
            Undo.RecordObjects(new UnityEngine.Object[] {contract, location}, "Configure Clark four-truck roster");
            var ordered = new[] { west[0], east[0], west[1], east[1] };
            var entries = new List<ContractLiveLoadEntry>();
            var bindings = new List<ContractLiveLoadBinding>();
            for (int lane = 0; lane < 2; lane++)
            {
                var pair = lane == 0 ? west : east;
                float direction = lane == 0 ? 1f : -1f;
                float nearStop = direction > 0 ? pair.Min(v => v.endPoint.position.x) : pair.Max(v => v.endPoint.position.x);
                Require(pair[0].TryGetMultiLoadStartBounds(out Bounds lead), "Lead truck needs physical bounds.");
                Require(pair[1].TryGetMultiLoadStartBounds(out Bounds rear), "Following truck needs physical bounds.");
                float parkingGap = lead.extents.x + rear.extents.x + 1.5f;
                for (int index = 0; index < pair.Length; index++)
                {
                    var v = pair[index];
                    Undo.RecordObjects(new UnityEngine.Object[] {v, v.startPoint, v.endPoint}, "Straighten Clark lane and parking route");
                    v.startPoint.SetPositionAndRotation(v.transform.position, Quaternion.identity);
                    v.endPoint.SetPositionAndRotation(new Vector3(nearStop + (index == 0 ? direction * parkingGap : 0f),
                        v.transform.position.y, v.transform.position.z), Quaternion.identity);
                    var finish = v.endPoint.GetComponent<FinishLineTrigger>();
                    var zone = finish.GetComponent<BoxCollider>();
                    Require(zone != null && zone.isTrigger, "Missing Clark finish trigger box.");
                    Undo.RecordObject(zone, "Keep Clark finish zone in its traffic lane");
                    zone.center = new Vector3(0f, 1.8f, 0f);
                    zone.size = new Vector3(1.25f, 5f, 5f);
                    v.vehicleMass = contract.liveLoadWeight;
                    foreach (var target in new UnityEngine.Object[] {v, v.startPoint, v.endPoint, zone}) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                    CheckLanding(scene, v);
                }
            }
            for (int index = 0; index < ordered.Length; index++)
            {
                var entry = new ContractLiveLoadEntry { displayName = "Cargo Truck " + (index + 1), emptyWeightKg = contract.liveLoadWeight, departureDelay = 0f };
                entries.Add(entry);
                bindings.Add(new ContractLiveLoadBinding { entryId = entry.entryId, vehicle = ordered[index] });
            }
            contract.vehicleTestMode = ContractSO.VehicleTestMode.Together;
            contract.liveLoadTests = entries;
            contract.jobDescription = "Connect Ironhaven's freight yard to the warehouse district with a 110-meter, two-way bridge. All four cargo trucks must cross safely, with two travelling in each direction. Consider how the roadway, hangers, cables, towers, and anchorages work together.";
            for (int index = 0; index < contract.offerDialogue.sentences.Length; index++)
                if (contract.offerDialogue.sentences[index].Contains("A <b><#E09500>cargo truck"))
                    contract.offerDialogue.sentences[index] = "We'll test it with <b><#E09500>four cargo trucks, two travelling in each direction</color></b>. The bridge must support them safely throughout the crossing.";
            location.liveLoadVehicles = bindings;
            PrefabUtility.RecordPrefabInstancePropertyModifications(location);
            var report = new StringBuilder();
            Check(scene, location, contract, report);
            Undo.FlushUndoRecordObjects();
            EditorUtility.SetDirty(contract);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty)
            {
                Require(EditorSceneManager.SaveScene(scene), "Could not save CanyonCrossing.");
                AssetDatabase.SaveAssetIfDirty(contract);
            }
            Undo.CollapseUndoOperations(group);
            report.AppendLine(wasDirty ? "NEEDS_SAVE: existing unsaved scene edits preserved; scene and contract left dirty." : "SAVED: CanyonCrossing and Clark Contract.");
            report.AppendLine("Backup: " + backup);
            File.WriteAllText(Report, report.ToString());
        }
        catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
    }
    private static void CheckLanding(Scene scene, LiveLoadVehicle vehicle)
    {
        foreach (var wheel in vehicle.wheelObjects)
        {
            var renderer = wheel.GetComponentInChildren<Renderer>();
            Require(renderer != null, "Missing truck wheel renderer.");
            Vector3 sample = renderer.bounds.center + vehicle.endPoint.position - vehicle.transform.position;
            bool grounded = Physics.RaycastAll(sample + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.collider.gameObject.scene == scene && hit.collider.GetComponentInParent<LiveLoadVehicle>() == null &&
                    hit.normal.y > .7f && Mathf.Abs(hit.point.y - (sample.y - vehicle.wheelRadius)) < .6f);
            Require(grounded, $"No level parking road beneath {vehicle.name} wheel at {sample:F2}.");
        }
    }
    internal static void Check(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        var previous = location.activeContract;
        try
        {
            location.activeContract = contract;
            Require(LiveLoadTestRoster.TryCreate(location, contract, out var roster, out string reason, false), reason);
            Require(roster.Participants.Count == 4 && roster.Mode == ContractSO.VehicleTestMode.Together && roster.Participants.All(p => p.DepartureDelay == 0), "Expected four simultaneous Together vehicles.");
            var parked = new List<Bounds>();
            foreach (var p in roster.Participants)
            {
                p.Vehicle.GetMultiLoadRoutePoses(out Vector3 from, out Vector3 to);
                Require(Mathf.Abs(from.z - to.z) < .01f, "A truck route drifts out of its lane.");
                Require(p.Vehicle.TryGetMultiLoadStartBounds(out Bounds bounds), "Missing physical truck bounds.");
                bounds.center += to - from;
                Require(!parked.Any(other => bounds.Intersects(other)), "Truck parking envelopes overlap.");
                parked.Add(bounds);
                CheckLanding(scene, p.Vehicle);
                report.AppendLine($"PASS {p.DisplayName}: {p.WeightKg} kg, {from:F2} -> {to:F2}, delay {p.DepartureDelay}s, ID={p.EntryId}");
            }
            Require(roster.Participants.Count(p => p.Vehicle.endPoint.position.x > p.Vehicle.startPoint.position.x) == 2, "Expected two vehicles in each direction.");
            report.AppendLine("PASS: four required vehicles, independent lane routes, nonoverlapping starts/parking, fully cleared finish envelopes, and grounded parking wheels.");
        }
        finally { location.activeContract = previous; }
    }
    public static void Validate()
    {
        var scene = Scene();
        var location = All<BuildLocation>(scene).Single(v => v.name == "workbenchClark1");
        var contract = AssetDatabase.LoadAssetAtPath<ContractSO>(ContractPath);
        var report = new StringBuilder();
        Check(scene, location, contract, report);
        report.AppendLine($"Scene dirty={scene.isDirty}");
        File.WriteAllText(Report, report.ToString());
    }
}
#endif
