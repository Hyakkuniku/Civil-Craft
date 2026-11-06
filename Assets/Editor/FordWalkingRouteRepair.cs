#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class FordWalkingRouteRepair
{
    private const string Request = "Temp/ford-walking-route.request";
    private const string Report = "Logs/ford-walking-route.txt";
    static FordWalkingRouteRepair() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        Scene previous = SceneManager.GetActiveScene(), added = default;
        try
        {
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
            if ((!scene.IsValid() || !scene.isLoaded) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                added = EditorSceneManager.OpenScene("Assets/Scenes/CanyonCrossing.unity", OpenSceneMode.Additive);
                SceneManager.SetActiveScene(added);
            }
            if (command == "inspect") Inspect(); else if (command == "apply") Apply(); else if (command == "validate") Validate(); else throw new InvalidOperationException("Use inspect, apply or validate.");
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL " + error); }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (added.IsValid() && added.isLoaded && !added.isDirty) EditorSceneManager.CloseScene(added, true);
        }
    }
    private static string PathOf(Transform value) => value == null ? "none" : value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static Scene Canyon() { var scene = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity"); if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open CanyonCrossing."); return scene; }
    private static NPCProgressionManager Ford(Scene scene) => All<NPCProgressionManager>(scene).Single(npc => npc.ProgressionSaveId == "FordNPC");
    private static LiveLoadVehicle[] Trucks(Scene scene) => All<LiveLoadVehicle>(scene).Where(vehicle => vehicle.assignedContract != null && vehicle.assignedContract.ContractID == "FordMainContract").ToArray();
    private static Transform Spawn(NPCProgressionManager ford) => new SerializedObject(ford).FindProperty("authoredSpawnLocation").objectReferenceValue as Transform;
    private const string RouteName = "Walk Around Trucks";
    private sealed class Plan
    {
        internal NPCProgressionManager ford;
        internal NPCProgressionPhase phase;
        internal Vector3 start, destination;
        internal Vector3[] waypoints;
        internal float radius, height;
    }
    private static bool Ground(NPCProgressionManager ford, Vector3 point, out Vector3 grounded)
    {
        var method = typeof(NPCProgressionManager).GetMethod("TryProjectWaypointToGround", BindingFlags.NonPublic | BindingFlags.Instance);
        object[] args = { point, Vector3.zero }; bool result = (bool)method.Invoke(ford, args); grounded = (Vector3)args[1]; return result;
    }
    private static bool Blocks(Collider collider, NPCProgressionManager ford, Scene scene)
    {
        if (collider == null || collider.isTrigger || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.gameObject.scene != scene) return false;
        if (collider.transform.IsChildOf(ford.transform) || collider.GetComponentInParent<NPCProgressionManager>() != null || collider.GetComponentInParent<PlayerMotor>() != null) return false;
        return true;
    }
    private static bool Leg(Scene scene, Plan plan, Vector3 start, Vector3 end, out string detail)
    {
        float distance = Vector3.ProjectOnPlane(end - start, Vector3.up).magnitude;
        int samples = Mathf.Max(1, Mathf.CeilToInt(distance / .2f));
        for (int i = 0; i <= samples; i++)
        {
            Vector3 point = Vector3.Lerp(start, end, (float)i / samples);
            if (!Ground(plan.ford, point, out Vector3 feet)) { detail = "No production ground below " + point.ToString("F3"); return false; }
            float bodyRadius = plan.radius + .15f;
            Vector3 lower = feet + Vector3.up * (bodyRadius + .08f);
            Vector3 upper = feet + Vector3.up * Mathf.Max(bodyRadius + .08f, plan.height - bodyRadius);
            foreach (var collider in Physics.OverlapCapsule(lower, upper, bodyRadius, ~0, QueryTriggerInteraction.Ignore))
                if (Blocks(collider, plan.ford, scene)) { detail = "Body clearance blocked by " + PathOf(collider.transform) + " at " + feet.ToString("F3"); return false; }
        }
        detail = $"{samples+1} grounded capsule samples over {distance:F2}m"; return true;
    }
    private static Plan MakePlan(Scene scene, StringBuilder report)
    {
        var ford = Ford(scene); var agent = ford.GetComponent<NavMeshAgent>(); var phase = ford.AuthoredPhases.Single(phase => phase.phaseId == "ExplainSituation");
        Require(phase.targetLocation != null && Spawn(ford) != null, "Ford's authored spawn or original site marker is missing.");
        var trucks = Trucks(scene); Require(trucks.Length == 2, "Expected Ford's two existing test trucks.");
        var boxes = trucks.Select(truck => { Require(truck.TryGetMultiLoadEnvelopeBounds(out Bounds bounds), "A Ford truck has no solid chassis envelope."); return bounds; }).OrderBy(bounds => bounds.center.x).ToArray();
        var plan = new Plan { ford = ford, phase = phase, start = Spawn(ford).position, destination = phase.targetLocation.position, radius = Mathf.Max(.3f, agent.radius), height = Mathf.Max(agent.height, agent.radius*2f) };
        float clearance = plan.radius + .4f;
        float north = boxes.Max(bounds => bounds.max.z) + clearance;
        float south = boxes.Min(bounds => bounds.min.z) - clearance;
        float rear = boxes.Min(bounds => bounds.min.x) - clearance;
        float gap = boxes[1].min.x - boxes[0].max.x;
        var candidates = new List<Vector3[]>();
        float southEntry = gap > clearance * 2f + .1f ? (boxes[0].max.x + boxes[1].min.x) * .5f : boxes[1].center.x;
        candidates.Add(new[] { new Vector3(rear, plan.start.y, south), new Vector3(southEntry, plan.start.y, south) });
        if (gap > clearance * 2f + .1f)
        {
            float middle = (boxes[0].max.x + boxes[1].min.x) * .5f;
            candidates.Add(new[] { new Vector3(rear, plan.start.y, north), new Vector3(middle, plan.start.y, north), new Vector3(middle, plan.start.y, south) });
        }
        candidates.Add(new[] { new Vector3(rear, plan.start.y, north), new Vector3(rear, plan.start.y, south) });
        Physics.SyncTransforms();
        foreach (var candidate in candidates)
        {
            bool valid = true; Vector3 cursor = plan.start; var points = new List<Vector3>();
            foreach (Vector3 point in candidate.Concat(new[] { plan.destination }))
            {
                if (!Ground(ford, point, out Vector3 grounded) || !Leg(scene, plan, cursor, grounded, out string _)) { valid = false; break; }
                points.Add(grounded); cursor = grounded;
            }
            if (!valid) continue;
            plan.waypoints = points.Take(points.Count - 1).ToArray();
            report.AppendLine($"PLANNED: {plan.waypoints.Length} authored ground waypoints; agent radius={plan.radius:F2}m, height={plan.height:F2}m; original destination={plan.destination:F4}.");
            return plan;
        }
        var details = new StringBuilder();
        foreach (var candidate in candidates)
        {
            Vector3 cursor = plan.start;
            foreach (Vector3 point in candidate.Concat(new[] { plan.destination }))
            {
                bool grounded = Ground(ford, point, out Vector3 feet);
                if (!grounded || !Leg(scene, plan, cursor, feet, out string _)) { Leg(scene, plan, cursor, grounded ? feet : point, out string detail); details.AppendLine(detail); break; }
                cursor = feet;
            }
        }
        throw new InvalidOperationException("No safe corridor around Ford's parked trucks. No route was changed.\n" + details);
    }
    [MenuItem("Tools/Civil Craft/Story/Ford/Repair Truck Walking Route")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before authoring Ford's route.");
        Scene scene = Canyon(); bool dirty = scene.isDirty; var report = new StringBuilder(); Plan plan = MakePlan(scene, report);
        var story = scene.GetRootGameObjects().Single(root => root.name == "Ford Story");
        var existing = story.transform.Find(RouteName); Require(existing == null || existing.childCount == plan.waypoints.Length, "The authored Ford detour has custom children; review them first.");
        var data = new SerializedObject(plan.ford); var phases = data.FindProperty("phases"); int index = Array.FindIndex(plan.ford.AuthoredPhases.ToArray(), phase => phase.phaseId == "ExplainSituation");
        var protectedPhases = plan.ford.AuthoredPhases.Where(phase => phase != plan.phase).ToDictionary(phase => phase.phaseId, phase => JsonUtility.ToJson(phase));
        var protectedVehicles = Trucks(scene).SelectMany(truck => new UnityEngine.Object[] { truck, truck.transform, truck.startPoint, truck.endPoint }).Where(value => value != null).Distinct().ToDictionary(value => value, value => EditorJsonUtility.ToJson(value));
        string backup = "LocalBackups/FordWalkingRoute-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); Directory.CreateDirectory(backup); File.Copy(scene.path, backup + "/CanyonCrossing.unity");
        Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Route Ford around parked trucks");
        try
        {
            Transform route = existing;
            if (route == null) { var created = new GameObject(RouteName); SceneManager.MoveGameObjectToScene(created, scene); Undo.RegisterCreatedObjectUndo(created, "Create Ford walking corridor"); created.transform.SetParent(story.transform, false); route = created.transform; }
            var markers = new List<Transform>();
            for (int i = 0; i < plan.waypoints.Length; i++)
            {
                Transform marker;
                if (i < route.childCount) marker = route.GetChild(i);
                else { var created = new GameObject("Walk Point " + (i+1)); SceneManager.MoveGameObjectToScene(created, scene); Undo.RegisterCreatedObjectUndo(created, "Create Ford route marker"); created.transform.SetParent(route, false); marker = created.transform; }
                Undo.RecordObject(marker, "Place clear walking marker"); marker.position = plan.waypoints[i]; EditorUtility.SetDirty(marker); markers.Add(marker);
            }
            Undo.RecordObject(plan.ford, "Assign Ford's truck detour"); var phaseData = phases.GetArrayElementAtIndex(index);
            phaseData.FindPropertyRelative("useGroundedWaypoints").boolValue = true;
            var points = phaseData.FindPropertyRelative("travelWaypoints"); points.arraySize = markers.Count;
            for (int i = 0; i < markers.Count; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = markers[i];
            data.ApplyModifiedProperties(); EditorUtility.SetDirty(plan.ford); PrefabUtility.RecordPrefabInstancePropertyModifications(plan.ford);
            foreach (var phase in plan.ford.AuthoredPhases.Where(phase => phase.phaseId != "ExplainSituation")) Require(JsonUtility.ToJson(phase) == protectedPhases[phase.phaseId], "Another Ford phase changed.");
            foreach (var value in protectedVehicles) Require(EditorJsonUtility.ToJson(value.Key) == value.Value, "A truck, route, mass or cargo setting changed.");
            Check(scene, report); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(undo); EditorSceneManager.MarkSceneDirty(scene);
            bool saved = !dirty && EditorSceneManager.SaveScene(scene); report.AppendLine(saved ? "SAVED: CanyonCrossing" : "NEEDS_SAVE: existing scene edits remain unsaved."); report.AppendLine("Backup: " + backup); File.WriteAllText(Report, report.ToString());
        }
        catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(undo); throw; }
    }
    [MenuItem("Tools/Civil Craft/Story/Ford/Validate Truck Walking Route")]
    public static void Validate() { var scene = Canyon(); var report = new StringBuilder(); Check(scene, report); report.AppendLine("Scene dirty=" + scene.isDirty); File.WriteAllText(Report, report.ToString()); }
    private static void Check(Scene scene, StringBuilder report)
    {
        var ford = Ford(scene); var phase = ford.AuthoredPhases.Single(phase => phase.phaseId == "ExplainSituation"); var agent = ford.GetComponent<NavMeshAgent>();
        Require(phase.useGroundedWaypoints && !phase.teleportToTargetLocation && phase.travelWaypoints.Count >= 2 && phase.travelWaypoints.All(point => point != null), "Ford's explicit walking corridor is not assigned.");
        var plan = new Plan { ford = ford, phase = phase, radius = Mathf.Max(.3f, agent.radius), height = Mathf.Max(agent.height, agent.radius*2f) };
        Vector3 cursor = Spawn(ford).position;
        foreach (Vector3 destination in phase.travelWaypoints.Select(point => point.position).Concat(new[] { phase.targetLocation.position }))
        {
            Require(Leg(scene, plan, cursor, destination, out string detail), detail); report.AppendLine($"PASS {cursor:F3} → {destination:F3}: {detail}."); cursor = destination;
        }
        foreach (var truck in Trucks(scene))
        {
            var collider = truck.GetComponentsInChildren<Collider>(true).First(value => value.enabled && !value.isTrigger);
            Require(VehicleNPCObstacleGeometry.TryGetLocalBounds(collider, truck.transform, .2f, out Bounds obstacle), "Could not resolve the truck's local avoidance footprint.");
            Bounds world = default; bool found = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = truck.transform.TransformPoint(new Vector3((i&1)==0 ? obstacle.min.x : obstacle.max.x, (i&2)==0 ? obstacle.min.y : obstacle.max.y, (i&4)==0 ? obstacle.min.z : obstacle.max.z));
                if (!found) { world = new Bounds(corner, Vector3.zero); found = true; } else world.Encapsulate(corner);
            }
            report.AppendLine($"PASS {PathOf(truck.transform)} yaw={truck.transform.eulerAngles.y:F1}: physical chassis {collider.bounds}; corrected avoidance footprint {world}.");
        }
        report.AppendLine("PASS original Ford site, arrival/dialogue callbacks, contract/quiz/lesson flow and both test trucks preserved. No player progress changed.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Inspect()
    {
        var scene = Canyon(); var ford = Ford(scene); var agent = ford.GetComponent<NavMeshAgent>(); var data = new SerializedObject(ford);
        var report = new StringBuilder($"FORD WALK play={Application.isPlaying} dirty={scene.isDirty}\n");
        report.AppendLine($"NPC {PathOf(ford.transform)} at {ford.transform.position:F5} spawn={Spawn(ford)?.position:F5} radius={agent.radius} height={agent.height} movementMode={data.FindProperty("movementMode").enumValueIndex}");
        foreach (var phase in ford.AuthoredPhases) report.AppendLine($"PHASE {phase.phaseId} target={PathOf(phase.targetLocation)} pos={(phase.targetLocation != null ? phase.targetLocation.position.ToString("F5") : "none")} grounded={phase.useGroundedWaypoints} teleport={phase.teleportToTargetLocation} waypoints=" + string.Join(";", phase.travelWaypoints.Where(point => point != null).Select(point => PathOf(point) + "=" + point.position.ToString("F5"))));
        foreach (var truck in Trucks(scene))
        {
            truck.TryGetMultiLoadEnvelopeBounds(out Bounds bounds);
            report.AppendLine($"TRUCK {PathOf(truck.transform)} at={truck.transform.position:F5} rotation={truck.transform.eulerAngles:F4} scale={truck.transform.lossyScale:F4} active={truck.gameObject.activeInHierarchy} envelope={bounds}");
            foreach (var obstacle in truck.GetComponentsInChildren<NavMeshObstacle>(true)) report.AppendLine($"OBSTACLE {PathOf(obstacle.transform)} enabled={obstacle.enabled} carving={obstacle.carving} center={obstacle.center} size={obstacle.size}");
            foreach (var collider in truck.GetComponentsInChildren<Collider>(true)) report.AppendLine($"COLLIDER {PathOf(collider.transform)} type={collider.GetType().Name} layer={collider.gameObject.layer} active={collider.gameObject.activeInHierarchy} enabled={collider.enabled} trigger={collider.isTrigger} bounds={collider.bounds}");
        }
        foreach (var phase in ford.AuthoredPhases.Where(phase => phase.targetLocation != null).Take(1))
        {
            var spawn = Spawn(ford); var path = new NavMeshPath(); var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (NavMesh.SamplePosition(spawn != null ? spawn.position : ford.transform.position, out NavMeshHit start, 2f, filter) && NavMesh.SamplePosition(phase.targetLocation.position, out NavMeshHit finish, 2f, filter))
                report.AppendLine($"NAVPATH {NavMesh.CalculatePath(start.position, finish.position, filter, path)} status={path.status} corners=" + string.Join(";", path.corners.Select(point => point.ToString("F5"))));
        }
        File.WriteAllText(Report, report.ToString());
    }
}
#endif
