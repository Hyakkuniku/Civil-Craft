#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs the production wheel rig in an isolated physics scene; never enters Play Mode.
[InitializeOnLoad]
internal static class ClarkOffsetVehicleValidation
{
    private const string Request = "Temp/clark-offset-vehicles.request";
    private const string Report = "Logs/clark-offset-vehicles.txt";
    private const string ContractPath = "Assets/BridgeBuilder/Data/Contract/IndustrialZone/Clark Offset Road Contract.asset";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static ClarkOffsetVehicleValidation() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
            Run(command);
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Invoke(LiveLoadVehicle v, string method) => typeof(LiveLoadVehicle).GetMethod(method, Private).Invoke(v, null);
    private static void Set(LiveLoadVehicle v, string field, object value) => typeof(LiveLoadVehicle).GetField(field, Private).SetValue(v, value);
    private static GameObject New(Scene scene, string name)
    {
        var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go;
    }
    private static void Run(string command)
    {
        var source = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        Require(source.IsValid() && source.isLoaded, "Open CanyonCrossing in Edit Mode.");
        var contract = AssetDatabase.LoadAssetAtPath<ContractSO>(ContractPath);
        var vehicles = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LiveLoadVehicle>(true))
            .Where(v => v.assignedContract == contract).ToArray();
        Require(vehicles.Length == 5, "Expected the five Offset Road vehicles.");
        var report = new StringBuilder();
        Require(command == "probe" || command == "apply" || command == "validate", "Unknown vehicle command.");
        if (command == "apply") Apply(source, vehicles, report);
        foreach (var v in vehicles)
        {
            Drive(v, 0f, report, command != "probe");
            if (!v.name.StartsWith("suvv", StringComparison.Ordinal)) Drive(v, 6f, report, command != "probe");
            File.WriteAllText(Report, report.ToString());
        }
        File.WriteAllText(Report, report.ToString());
    }
    private static void Apply(Scene scene, LiveLoadVehicle[] vehicles, StringBuilder report)
    {
        string backup = "LocalBackups/ClarkOffsetVehicles-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path, backup + "/CanyonCrossing.unity");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Enlarge Offset SUVs and tune truck drive");
        foreach (var vehicle in vehicles)
        {
            if (vehicle.name.StartsWith("suvv", StringComparison.Ordinal))
            {
                // Absolute target makes the command safe to repeat. Keep the Rigidbody
                // root at unit scale: wheel collider radii are specified in metres.
                var body = vehicle.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "SUV.002");
                float factor = 5.3625f / body.bounds.size.x;
                Require(factor > .95f && factor < 1.3f, "Unexpected SUV dimensions.");
                float oldBottom = vehicle.wheelObjects.Min(w => w.GetComponentInChildren<Renderer>().bounds.min.y);
                Undo.RecordObject(vehicle, "Size SUV wheels");
                foreach (Transform child in vehicle.transform)
                {
                    Undo.RecordObject(child, "Size SUV model");
                    child.localPosition *= factor; child.localScale *= factor;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                }
                foreach (var box in vehicle.GetComponents<BoxCollider>())
                {
                    Undo.RecordObject(box, "Size SUV chassis"); box.center *= factor; box.size *= factor;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(box);
                }
                vehicle.wheelRadius *= factor;
                float newBottom = vehicle.wheelObjects.Min(w => w.GetComponentInChildren<Renderer>().bounds.min.y);
                float lift = oldBottom - newBottom;
                foreach (var target in new[] { vehicle.transform, vehicle.startPoint, vehicle.endPoint })
                {
                    Undo.RecordObject(target, "Keep SUV tires on road"); target.position += Vector3.up * lift;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }
                report.AppendLine("SIZED " + vehicle.name + " body length=" + body.bounds.size.x + " radius=" + vehicle.wheelRadius);
            }
            else
            {
                Undo.RecordObject(vehicle, "Tune loaded truck drive");
                vehicle.engineTorque = 6000f;
                report.AppendLine("TUNED " + vehicle.name + " torque=" + vehicle.engineTorque);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle);
        }
        Physics.SyncTransforms();
        var parked = vehicles.Select(v => {
            Require(v.TryGetMultiLoadStartBounds(out Bounds bounds), "Missing vehicle bounds.");
            bounds.center += v.endPoint.position - v.startPoint.position; return bounds;
        }).ToArray();
        for (int a = 0; a < parked.Length; a++)
            for (int b = a + 1; b < parked.Length; b++) Require(!parked[a].Intersects(parked[b]), "Parking envelopes overlap after resizing.");
        Undo.FlushUndoRecordObjects(); EditorSceneManager.MarkSceneDirty(scene);
        Require(EditorSceneManager.SaveScene(scene), "Could not save vehicle changes.");
        Undo.CollapseUndoOperations(group);
        report.AppendLine("SAVED vehicles; backup=" + backup);
    }
    private static void Drive(LiveLoadVehicle authored, float incline, StringBuilder report, bool verify)
    {
        Scene test = EditorSceneManager.NewPreviewScene();
        LiveLoadVehicle vehicle = null;
        try
        {
            Require(test.GetPhysicsScene() != Physics.defaultPhysicsScene, "Preview must have isolated physics.");
            var clone = UnityEngine.Object.Instantiate(authored.gameObject);
            SceneManager.MoveGameObjectToScene(clone, test); clone.SetActive(true);
            // Instantiate copies the nested object's local pose in Edit Mode.
            // Explicitly place it at the test origin before sampling the tires.
            clone.transform.SetPositionAndRotation(Vector3.zero, authored.transform.rotation);
            clone.transform.localScale = authored.transform.lossyScale;
            vehicle = clone.GetComponent<LiveLoadVehicle>(); vehicle.enabled = false;
            vehicle.physicsManager = null; vehicle.vehicleInfoPanel = null;
            Set(vehicle, "configureNPCObstacle", false);
            float bottom = vehicle.wheelObjects.Min(w => w.GetComponentInChildren<Renderer>().bounds.center.y - vehicle.wheelRadius);
            clone.transform.position += Vector3.up * (.04f - bottom);
            float direction = Mathf.Sign(authored.endPoint.position.x - authored.startPoint.position.x);
            float slope = Mathf.Tan(incline * Mathf.Deg2Rad) * direction;
            // Shift onto the incline, then lift the lowest wheel enough to settle.
            float lift = vehicle.wheelObjects.Max(w => {
                var center = w.GetComponentInChildren<Renderer>().bounds.center;
                return slope * center.x - (center.y - vehicle.wheelRadius);
            });
            clone.transform.position += Vector3.up * (lift + .03f);
            vehicle.startPoint = New(test, "Start").transform; vehicle.startPoint.position = clone.transform.position;
            vehicle.endPoint = New(test, "Finish").transform; vehicle.endPoint.position = clone.transform.position + Vector3.right * direction * 100f;
            var ground = New(test, "Road"); ground.transform.rotation = Quaternion.Euler(0f, 0f, incline * direction);
            var groundCollider = ground.AddComponent<BoxCollider>(); groundCollider.center = Vector3.down * .5f; groundCollider.size = new Vector3(300f, 1f, 20f);
            Invoke(vehicle, "Awake"); Invoke(vehicle, "BuildWheelPhysics");
            var body = clone.GetComponent<Rigidbody>(); body.isKinematic = false;
            foreach (var wheel in clone.GetComponentsInChildren<HingeJoint>()) wheel.GetComponent<Rigidbody>().isKinematic = false;
            typeof(LiveLoadVehicle).GetMethod("SetDrivingRotationConstraint", Private).Invoke(vehicle, new object[] { true });
            Set(vehicle, "isDriving", true);
            Physics.SyncTransforms();
            var physics = test.GetPhysicsScene();
            for (int step = 0; step < 400; step++) { Invoke(vehicle, "FixedUpdate"); physics.Simulate(.02f); }
            float distance = body.position.x * direction;
            report.AppendLine("DRIVE " + authored.name + " slope=" + incline + " torque=" + vehicle.engineTorque +
                " distance=" + distance.ToString("F3") + " speed=" + body.velocity.ToString("F3") + " position=" + body.position.ToString("F3"));
            foreach (var hinge in clone.GetComponentsInChildren<HingeJoint>())
                report.AppendLine("  axle=" + hinge.name + " axis=" + hinge.transform.TransformDirection(hinge.axis).ToString("F3") +
                    " radius=" + hinge.GetComponent<SphereCollider>().radius + " spin=" + hinge.GetComponent<Rigidbody>().angularVelocity.ToString("F3"));
            if (verify)
            {
                Require(distance > 15f && body.position.y > -2f, authored.name + " did not drive over the test road.");
                Require(body.velocity.x * direction > 3f, authored.name + " stalled on the test road.");
                report.AppendLine("PASS: forward travel and sustained speed on " + incline + " degree road.");
            }
        }
        finally
        {
            if (vehicle != null) { vehicle.physicsManager = null; vehicle.StopAndReset(); }
            EditorSceneManager.ClosePreviewScene(test);
        }
    }
}
#endif
