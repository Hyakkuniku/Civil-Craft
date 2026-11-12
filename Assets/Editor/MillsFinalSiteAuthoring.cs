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

/// <summary>Exact scene-local setup for Mills's final regional crossing.</summary>
[InitializeOnLoad]
internal static class MillsFinalSiteAuthoring
{
    private const string Request = "Temp/mills-final-site.request";
    private const string Report = "Logs/mills-final-site.txt";
    private const string SiteRoot = "Mills MAIN CONTRACT (2)";
    private const string VehicleRoot = "Mills Final Temporary Vehicle";
    private const string PlacedVehicleRoot = "Mills 3nd Contract Bus (1)";
    private const string SourceVehicleRoot = "Mills 2nd Contract Bus";
    private const string Action = "Author Mills final temporary vehicle";
    private const string ContractPath = "Assets/BridgeBuilder/Data/Contract/IndustrialZone/Mills Reconnecting Arcadia Contract.asset";
    internal const string ContractId = "MillsReconnectingArcadia";
    private static bool running;
    static MillsFinalSiteAuthoring() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        running = true;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        var report = new StringBuilder();
        try
        {
            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
            Require(scene.IsValid() && scene.isLoaded, "Open CanyonCrossing in Edit Mode. No scene was opened or saved.");
            if (command == "inspect") Inspect(scene, report);
            else if (command == "validate") Validate(scene, FindLocation(scene), LoadContract(), report);
            else if (command == "apply")
            {
                Require(File.Exists(Report) && File.ReadAllText(Report).Contains("INSPECTION COMPLETE"), "Run the read-only final-site inspection before authoring the temporary vehicle.");
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(Action);
                try { Apply(scene, FindLocation(scene), LoadContract(), report); Undo.FlushUndoRecordObjects(); EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group); report.AppendLine("NEEDS SAVE: final-site setup applied; the scene and existing edits remain unsaved."); }
                catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
            }
            else throw new InvalidOperationException("Unknown final-site command: " + command);
        }
        catch (Exception error) { report.Insert(0, "FAIL\n"); report.AppendLine(error.ToString()); }
        finally { Directory.CreateDirectory("Logs"); File.WriteAllText(Report, report.ToString()); running = false; }
    }
    internal static BuildLocation FindLocation(Scene scene) => All<BuildLocation>(scene).Single(site => HasAncestor(site.transform, SiteRoot));
    internal static LiveLoadVehicle FindVehicle(Scene scene) => All<LiveLoadVehicle>(scene).Single(vehicle => HasAncestor(vehicle.transform, VehicleRoot));
    private static ContractSO LoadContract() => AssetDatabase.LoadAssetAtPath<ContractSO>(ContractPath) ?? throw new InvalidOperationException("Missing " + ContractPath);
    internal static (Point start, Point end) GetRoadAnchors(BuildLocation location)
    {
        Point start = location.startingAnchors.Single(point => point != null && point.name == "LeftAnchor");
        Point end = location.endingAnchors.Single(point => point != null && point.name == "RightAnchor");
        Require(start.originalIsAnchor && end.originalIsAnchor && end.transform.position.x > start.transform.position.x &&
            Mathf.Abs(start.transform.position.y - end.transform.position.y) < .05f && Mathf.Abs(start.transform.position.z - end.transform.position.z) < .05f,
            "The final crossing requires its exact level X-axis road anchors.");
        return (start, end);
    }
    internal static void Inspect(Scene scene, StringBuilder report)
    {
        BuildLocation location = FindLocation(scene); var (start, end) = GetRoadAnchors(location);
        report.AppendLine($"FINAL SITE {Path(location.transform)} position={location.transform.position:F3}; anchors {start.transform.position:F3} -> {end.transform.position:F3}; span={end.transform.position.x-start.transform.position.x:0.###}m; contract={location.activeContract?.name}; dirty={scene.isDirty}");
        foreach (var vehicle in All<LiveLoadVehicle>(scene))
        {
            report.AppendLine($"LIVE LOAD {Path(vehicle.transform)} active={vehicle.gameObject.activeInHierarchy}, position={vehicle.transform.position:F3}, rotation={vehicle.transform.eulerAngles:F2}, mass={vehicle.vehicleMass}, contract={vehicle.assignedContract?.name}, wheels={vehicle.wheelObjects?.Length}, radius={vehicle.wheelRadius:0.###}, axis={vehicle.spinAxis:F3}");
            report.AppendLine($"  Start={Path(vehicle.startPoint)} at {vehicle.startPoint?.position.ToString("F3")}; End={Path(vehicle.endPoint)} at {vehicle.endPoint?.position.ToString("F3")}");
            if (vehicle.transform.position.x < 3100f) continue;
            vehicle.TryGetMultiLoadEnvelopeBounds(out Bounds bounds); report.AppendLine("  Envelope " + bounds);
            foreach (var wheel in vehicle.wheelObjects ?? Array.Empty<GameObject>())
                report.AppendLine(wheel == null ? "  Missing wheel" : $"  Wheel {Path(wheel.transform)} bounds={wheel.GetComponentInChildren<Renderer>(true)?.bounds}, worldAxis={wheel.transform.TransformDirection(vehicle.spinAxis):F3}");
            foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
                report.AppendLine($"  Mesh {Path(filter.transform)} source={filter.sharedMesh?.name} bounds={filter.GetComponent<Renderer>()?.bounds}");
        }
        foreach (var value in All<Transform>(scene).Where(value => value.position.x > 3100f &&
            (value.name.IndexOf("vehicle", StringComparison.OrdinalIgnoreCase) >= 0 || value.name.IndexOf("truck", StringComparison.OrdinalIgnoreCase) >= 0 ||
             value.name.IndexOf("bus", StringComparison.OrdinalIgnoreCase) >= 0 || value.name.IndexOf("suv", StringComparison.OrdinalIgnoreCase) >= 0 || value.name.IndexOf("car", StringComparison.OrdinalIgnoreCase) >= 0)))
            report.AppendLine($"PROSPECTIVE {Path(value)} position={value.position:F3}, rotation={value.eulerAngles:F2}, scale={value.lossyScale:F3}");
        report.AppendLine("INSPECTION COMPLETE: no objects changed or saved.");
    }
    internal static void Apply(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        Require(scene.IsValid() && scene.isLoaded && location == FindLocation(scene), "Use Mills's exact third site.");
        Require(contract != null && contract.ContractID == ContractId && contract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle &&
            contract.vehicleTestMode == ContractSO.VehicleTestMode.Single && contract.winCondition == ContractSO.WinCondition.FinishLine &&
            contract.liveLoadWeight > 0f && !contract.allowVehicleCargo && contract.twoWayRoad && contract.twoWayRoadWidth >= 16f,
            "The final temporary SUV requires its positive Single Finish Line contract and authored wide deck.");
        Require(location.activeContract == null || location.activeContract == contract, "The final workbench belongs to another contract.");
        Require(location.liveLoadVehicles == null || location.liveLoadVehicles.Count == 0, "Review existing final-site multi-load bindings before replacing them.");
        var (start, end) = GetRoadAnchors(location);
        Require(Mathf.Abs(contract.bridgeSpan - (end.transform.position.x-start.transform.position.x)) < .05f, "Match the final contract to its actual 150 m span.");
        var existing = All<LiveLoadVehicle>(scene).Where(vehicle => HasAncestor(vehicle.transform, VehicleRoot) || HasAncestor(vehicle.transform, PlacedVehicleRoot)).ToArray();
        Require(existing.Length <= 1, "Duplicate Mills final temporary vehicle roots.");
        LiveLoadVehicle vehicle;
        if (existing.Length == 0)
        {
            Require(!All<LiveLoadVehicle>(scene).Any(value => value.assignedContract == contract), "A final-contract live load already exists. Inspect and bind it explicitly instead of creating a duplicate.");
            Require(!All<LiveLoadVehicle>(scene).Any(value => value.transform.position.x > 3100f), "An authored live load exists near the final site. Review the inspection and explicitly select it before adding a temporary clone.");
            GameObject source = scene.GetRootGameObjects().Single(root => root.name == SourceVehicleRoot);
            LiveLoadVehicle original = source.GetComponentsInChildren<LiveLoadVehicle>(true).Single();
            Require(original.assignedContract != null && original.assignedContract.ContractID == "MillsWorkersCrossing" &&
                original.startPoint != null && original.endPoint != null && original.startPoint.IsChildOf(source.transform) && original.endPoint.IsChildOf(source.transform) &&
                !original.startPoint.IsChildOf(original.transform) && !original.endPoint.IsChildOf(original.transform),
                "Only clone the exact inspected workers' temporary SUV root with its own independent fixed route.");
            var sourceSite = MillsWorkersCrossingSiteAuthoring.FindLocation(scene);
            var (sourceAnchor, _) = MillsWorkersCrossingSiteAuthoring.GetRoadAnchors(sourceSite);
            GameObject copy = UnityEngine.Object.Instantiate(source); copy.name = VehicleRoot;
            SceneManager.MoveGameObjectToScene(copy, scene); Undo.RegisterCreatedObjectUndo(copy, Action);
            copy.transform.position += start.transform.position - sourceAnchor.transform.position;
            vehicle = copy.GetComponentsInChildren<LiveLoadVehicle>(true).Single();
            Require(vehicle != original && vehicle.startPoint != original.startPoint && vehicle.endPoint != original.endPoint &&
                vehicle.startPoint.IsChildOf(copy.transform) && vehicle.endPoint.IsChildOf(copy.transform), "The temporary clone retained source route references.");
            var events = new SerializedObject(vehicle);
            foreach (string field in new[] { "onInspectionWindowClosed", "onRequiredCargoLoaded" })
            {
                var calls = events.FindProperty(field)?.FindPropertyRelative("m_PersistentCalls.m_Calls");
                if (calls != null) calls.arraySize = 0;
            }
            events.ApplyModifiedProperties();
            report.AppendLine("Created an independent copy of the inspected temporary SUV; the workers' vehicle and route were not changed.");
        }
        else
        {
            vehicle = existing[0];
            Transform rig = vehicle.transform;
            while (rig.parent != null && rig.name != VehicleRoot && rig.name != PlacedVehicleRoot) rig = rig.parent;
            Require(rig.name == VehicleRoot || rig.name == PlacedVehicleRoot, "Use the inspected third-contract SUV root.");
            Undo.RecordObject(rig.gameObject, Action); rig.gameObject.name = VehicleRoot; Mark(rig.gameObject);
            report.AppendLine("Bound the explicitly placed third-contract temporary SUV; no extra vehicle was created.");
        }
        Require(vehicle.startPoint != null && vehicle.endPoint != null && vehicle.startPoint != vehicle.endPoint &&
            HasAncestor(vehicle.startPoint, VehicleRoot) && HasAncestor(vehicle.endPoint, VehicleRoot) &&
            !vehicle.startPoint.IsChildOf(vehicle.transform) && !vehicle.endPoint.IsChildOf(vehicle.transform), "The final vehicle needs its own fixed route markers.");
        Require(vehicle.assignedContract == null || vehicle.assignedContract == contract || vehicle.assignedContract.ContractID == "MillsWorkersCrossing", "The final temporary vehicle is assigned to an unrelated contract.");
        Require(vehicle.wheelObjects != null && vehicle.wheelObjects.Length == 4 && vehicle.wheelObjects.Distinct().Count() == 4 &&
            vehicle.wheelObjects.All(wheel => wheel != null && wheel.transform.IsChildOf(vehicle.transform)), "The inspected temporary SUV requires four distinct wheels.");
        Require(vehicle.wheelRadius > .1f && vehicle.wheelRadius < 1.5f && vehicle.GetComponent<Rigidbody>() != null &&
            vehicle.wheelObjects.All(wheel => Vector3.Dot(wheel.transform.TransformDirection(vehicle.spinAxis).normalized, Vector3.forward) > .9f),
            "Keep the working SUV wheel size, Rigidbody, and positive-X motor orientation.");
        Require(vehicle.TryGetMultiLoadEnvelopeBounds(out Bounds envelope), "The final SUV needs a physical chassis.");
        float nose = envelope.max.x - vehicle.transform.position.x, tail = envelope.min.x - vehicle.transform.position.x;
        var wheelOffsets = vehicle.wheelObjects.Select(wheel => WheelCenter(wheel) - vehicle.transform.position).ToArray();
        float bottomOffset = wheelOffsets.Min(point => point.y) - vehicle.wheelRadius;
        float laneZ = vehicle.transform.position.z, deckY = start.transform.position.y + .025f;
        float startX = start.transform.position.x - nose - .75f;
        Require(FindLevelWheelGround(scene, wheelOffsets, startX, laneZ, deckY, out float startGround), "The temporary SUV must start with all four wheels on the final site's solid approach.");
        float stopX = 0f, stopGround = 0f; bool parking = false;
        for (int sample=0; sample<24 && !parking; sample++)
        {
            float candidate = end.transform.position.x - tail + 1.5f + sample*.5f;
            if (!FindLevelWheelGround(scene, wheelOffsets, candidate, laneZ, deckY, out float ground)) continue;
            stopX=candidate; stopGround=ground; parking=true;
        }
        Require(parking, "No solid level destination supports every final SUV wheel after its rear clears the 150 m crossing.");
        Transform startMarker=vehicle.startPoint, endMarker=vehicle.endPoint;
        Undo.RecordObjects(new UnityEngine.Object[]{vehicle, vehicle.transform, vehicle.gameObject, vehicle.GetComponent<Rigidbody>(), startMarker, endMarker, location, location.gameObject}, Action);
        vehicle.gameObject.name="Mills Final Temporary SUV";
        vehicle.transform.position=new Vector3(startX, startGround-bottomOffset+.025f, laneZ);
        // Route marker axes are world aligned, independently of the imported
        // SUV's 180-degree model heading. The finish offset points past the bank.
        startMarker.SetPositionAndRotation(vehicle.transform.position, Quaternion.identity);
        endMarker.SetPositionAndRotation(new Vector3(stopX,stopGround-bottomOffset+.025f,laneZ), Quaternion.identity);
        FinishLineTrigger finish=endMarker.GetComponent<FinishLineTrigger>();
        if(finish==null)finish=Undo.AddComponent<FinishLineTrigger>(endMarker.gameObject);else Undo.RecordObject(finish,Action);
        BoxCollider zone=endMarker.GetComponent<BoxCollider>();
        if(zone==null)zone=Undo.AddComponent<BoxCollider>(endMarker.gameObject);else Undo.RecordObject(zone,Action);
        finish.assignedContract=contract; zone.isTrigger=true; zone.enabled=true;
        zone.center=new Vector3(nose+.525f,stopGround+2f-endMarker.position.y,0f); zone.size=new Vector3(1.25f,5f,Mathf.Min(contract.twoWayRoadWidth,5f));
        vehicle.assignedContract=contract; vehicle.vehicleMass=contract.liveLoadWeight; vehicle.vehicleName="Regional Traffic (Temporary SUV)";
        vehicle.advancesTutorial=false; vehicle.GetComponent<Rigidbody>().mass=contract.liveLoadWeight;
        if(vehicle.physicsManager==null)vehicle.physicsManager=All<BridgePhysicsManager>(scene).Single(manager=>manager.isActiveAndEnabled);
        Require(vehicle.physicsManager.gameObject.scene==scene,"Use CanyonCrossing's Bridge Physics Manager.");
        location.gameObject.name="workbenchMills3"; location.advancesTutorial=false;
        if(startMarker.parent==endMarker.parent && startMarker.parent!=null)
        {
            Undo.RecordObject(startMarker.parent.gameObject,Action);startMarker.parent.gameObject.name="Mills Final Crossing Route";Mark(startMarker.parent.gameObject);
        }
        foreach(var value in new UnityEngine.Object[]{vehicle,vehicle.transform,vehicle.gameObject,vehicle.GetComponent<Rigidbody>(),startMarker,endMarker,location,location.gameObject,finish,zone})Mark(value);
        Physics.SyncTransforms();Validate(scene,location,contract,report);
    }
    internal static void Validate(Scene scene, BuildLocation location, ContractSO contract, StringBuilder report)
    {
        if (contract != null && contract.HasMultipleLiveLoads)
        {
            MillsFinalTrafficSetup.Check(scene, location, contract, report);
            return;
        }
        var vehicle=FindVehicle(scene);var(start,end)=GetRoadAnchors(location);
        Require(location==FindLocation(scene)&&location.name=="workbenchMills3"&&!location.advancesTutorial&&
            contract!=null&&contract.ContractID==ContractId&&vehicle.assignedContract==contract&&vehicle.vehicleMass==contract.liveLoadWeight&&vehicle.physicsManager!=null,
            "The final site, contract, and temporary live-load bindings are incomplete.");
        Require(All<LiveLoadVehicle>(scene).Count(value=>value.assignedContract==contract)==1,"The single final test must have exactly one required vehicle.");
        Require(vehicle.startPoint!=null&&vehicle.endPoint!=null&&HasAncestor(vehicle.startPoint,VehicleRoot)&&HasAncestor(vehicle.endPoint,VehicleRoot),"The final route must belong to the independent temporary rig.");
        Require(vehicle.TryGetMultiLoadStartBounds(out Bounds bounds),"The final temporary SUV needs a physical envelope.");
        vehicle.GetMultiLoadRoutePoses(out Vector3 from,out Vector3 to);
        Require(bounds.max.x<start.transform.position.x&&to.x+bounds.min.x-from.x>end.transform.position.x+.5f&&Mathf.Abs(from.z-to.z)<.01f,
            "The final SUV must start fully off the bridge and park fully beyond it without lane drift.");
        Require(Mathf.Abs(bounds.center.z-start.transform.position.z)+bounds.extents.z<contract.twoWayRoadWidth*.5f-.1f,"The final SUV must remain inside the widened deck.");
        var finish=vehicle.endPoint.GetComponent<FinishLineTrigger>();var zone=vehicle.endPoint.GetComponent<BoxCollider>();
        Require(finish!=null&&finish.assignedContract==contract&&zone!=null&&zone.isTrigger&&zone.enabled,"The final finish trigger belongs to another contract or is disabled.");
        float firstContactRoot=zone.bounds.min.x-(bounds.max.x-from.x);
        Require(firstContactRoot+bounds.min.x-from.x>end.transform.position.x+.5f,"The final trigger would finish before the complete vehicle clears the bridge.");
        foreach(var wheel in vehicle.wheelObjects)
            foreach(var shift in new[]{Vector3.zero,to-from})
            {
                Vector3 point=WheelCenter(wheel)+shift;
                Require(TryGround(scene,new Vector3(point.x,point.y-vehicle.wheelRadius,point.z),out float y)&&Mathf.Abs(y-(point.y-vehicle.wheelRadius))<.12f,
                    $"Final SUV wheel {wheel.name} needs road beneath it at {point:F3}.");
            }
        Require(MillsWorkersCrossingSiteAuthoring.FindVehicle(scene).assignedContract.ContractID=="MillsWorkersCrossing"&&
            MillsHeavyEquipmentSiteAuthoring.FindVehicle(scene).assignedContract.ContractID=="MillsMainContract","Earlier Mills vehicle bindings changed.");
        report.AppendLine($"PASS: final crossing {end.transform.position.x-start.transform.position.x:0.###}m, single {contract.liveLoadWeight:0.##} kg temporary SUV, {vehicle.wheelObjects.Length} wheels, lane offset {vehicle.transform.position.z-start.transform.position.z:0.###}m; route {from:F3} -> {to:F3}; grounded approaches and full-envelope finish.");
        report.AppendLine("TEMPORARY VISUAL: one SUV represents regional traffic. This setup does not claim to implement mixed regional vehicle models or a multiple-vehicle roster.");
    }
    private static bool FindLevelWheelGround(Scene scene,Vector3[] offsets,float x,float z,float referenceY,out float ground)
    {
        var heights=new List<float>();
        foreach(var offset in offsets){if(!TryGround(scene,new Vector3(x+offset.x,referenceY,z+offset.z),out float y)){ground=0;return false;}heights.Add(y);}
        ground=heights.Count>0?heights.Average():0;return heights.Count==offsets.Length&&heights.Count>0&&heights.Max()-heights.Min()<=.09f;
    }
    private static bool TryGround(Scene scene,Vector3 point,out float height)
    {
        var hits=Physics.RaycastAll(point+Vector3.up*3f,Vector3.down,7f,~0,QueryTriggerInteraction.Ignore)
            .Where(hit=>hit.collider.gameObject.scene==scene&&hit.collider.GetComponentInParent<LiveLoadVehicle>()==null&&hit.normal.y>.7f&&Mathf.Abs(hit.point.y-point.y)<.6f)
            .OrderBy(hit=>Mathf.Abs(hit.point.y-point.y)).ToArray();height=hits.Length>0?hits[0].point.y:0;return hits.Length>0;
    }
    private static Vector3 WheelCenter(GameObject wheel)
    {
        var renderer=wheel.GetComponentInChildren<Renderer>(true);Require(renderer!=null,"The temporary SUV wheel has no visible mesh.");return renderer.bounds.center;
    }
    private static void Mark(UnityEngine.Object value){EditorUtility.SetDirty(value);if(PrefabUtility.IsPartOfPrefabInstance(value))PrefabUtility.RecordPrefabInstancePropertyModifications(value);}
    private static string Path(Transform value) => value == null ? "unassigned" : (value.parent == null ? value.name : Path(value.parent) + "/" + value.name);
    private static bool HasAncestor(Transform value, string name) { for (; value != null; value = value.parent) if (value.name == name) return true; return false; }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
#endif
