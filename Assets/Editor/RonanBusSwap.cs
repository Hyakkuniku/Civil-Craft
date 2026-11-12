#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class RonanBusSwap
{
    public const string ModelPath = "Assets/Elements/Vehicles/ronan's main quest bus.fbx";
    private const string Canyon = "Assets/Scenes/CanyonCrossing.unity";
    private const string Output = "Logs/RonanBus";
    private const string Request = "Temp/ronan-bus-swap.request";
    private const string VisualName = "Ronan Bus Visual";
    private const string Action = "Replace Ronan truck with bus";

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(Request);
        try { ApplyLoaded(); }
        catch (Exception error) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/Swap.txt", "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Story/Ronan/Replace Truck With Bus")]
    public static void ApplyLoaded()
    {
        if (ParrelSync.ClonesManager.IsClone()) throw new InvalidOperationException("Apply the scene change from the original Editor.");
        Scene scene = SceneManager.GetSceneByPath(Canyon);
        Require(scene.IsValid() && scene.isLoaded && !EditorApplication.isPlayingOrWillChangePlaymode, "Open CanyonCrossing in Edit Mode.");
        bool hadEdits = scene.isDirty;
        string result = Swap(scene);
        if (!hadEdits) Require(EditorSceneManager.SaveScene(scene), "Could not save the clean scene after replacement.");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Swap.txt", result + (hadEdits ? "\nNEEDS_SAVE: CanyonCrossing had existing unsaved edits; all were preserved." : "\nSAVED: CanyonCrossing."));
    }

    public static void TrialBatch()
    {
        Scene scene = EditorSceneManager.OpenScene(Canyon, OpenSceneMode.Single);
        Directory.CreateDirectory(Output);
        string result = Swap(scene);
        var vehicle = Vehicle(scene);
        RenderModel(vehicle.transform.Find(VisualName).gameObject, scene, Output + "/Bus-Fitted.png");
        // Object.Instantiate creates the same disconnected hierarchy used in a
        // player, instead of exercising Editor-only imported-prefab restrictions.
        GameObject runtimeCopy = UnityEngine.Object.Instantiate(vehicle.gameObject);
        runtimeCopy.transform.localScale = vehicle.transform.lossyScale;
        vehicle = runtimeCopy.GetComponent<LiveLoadVehicle>();
        Vector3[] centers = vehicle.wheelObjects.Select(wheel => wheel.GetComponent<Renderer>().bounds.center).ToArray();
        typeof(LiveLoadVehicle).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(vehicle, null);
        vehicle.RefreshCargoMass();
        for (int index = 0; index < vehicle.wheelObjects.Length; index++)
        {
            GameObject wheel = vehicle.wheelObjects[index];
            SphereCollider axle = wheel.transform.parent.GetComponent<SphereCollider>();
            Require(axle != null && axle.radius > .3f && axle.radius < .6f &&
                Mathf.Abs(axle.radius * axle.transform.lossyScale.x - wheel.GetComponent<Renderer>().bounds.extents.y) < .003f &&
                Vector3.Distance(wheel.GetComponent<Renderer>().bounds.center, centers[index]) < .003f,
                "Runtime wheel creation shifted or incorrectly scaled " + wheel.name + "; parent " + wheel.transform.parent.name +
                "; radius " + (axle != null ? axle.radius.ToString("F3") : "none") +
                "; shift " + Vector3.Distance(wheel.GetComponent<Renderer>().bounds.center, centers[index]).ToString("F5"));
        }
        Require(Mathf.Abs(vehicle.GetComponent<Rigidbody>().mass - vehicle.assignedContract.liveLoadWeight) < .01f,
            "Runtime bus mass differs from Ronan's assigned contract.");
        result += "\nPASS: normal vehicle initialization creates four physical axles without shifting the model; runtime mass matches the contract.";
        UnityEngine.Object.DestroyImmediate(runtimeCopy);
        File.WriteAllText(Output + "/Trial.txt", result + "\nTemporary loaded copy only; scene not saved.");
        Debug.Log("[Ronan Bus] Trial PASS\n" + result);
    }

    public static void ValidateSavedBatch()
    {
        Scene scene = EditorSceneManager.OpenScene(Canyon, OpenSceneMode.Single);
        Directory.CreateDirectory(Output);
        string result = Validate(scene, Vehicle(scene), out _);
        File.WriteAllText(Output + "/Saved-Validation.txt", result);
        Debug.Log("[Ronan Bus] Saved validation PASS\n" + result);
    }

    private static LiveLoadVehicle Vehicle(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<LiveLoadVehicle>(true))
        .Single(item => item.assignedContract != null && item.assignedContract.ContractID == "RonanMainContract");

    private static string Swap(Scene scene)
    {
        LiveLoadVehicle vehicle = Vehicle(scene);
        if (vehicle.transform.Find(VisualName) != null) return Validate(scene, vehicle, out _);
        Require(vehicle.startPoint != null && vehicle.endPoint != null && vehicle.wheelObjects != null &&
            vehicle.wheelObjects.Length == 4 && vehicle.wheelObjects.All(wheel => wheel != null), "Ronan's existing route or four wheels are missing.");
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Require(asset != null, "The imported Ronan bus is missing.");
        Transform start = vehicle.startPoint, end = vehicle.endPoint;
        Vector3 startPosition = start.position, endPosition = end.position;
        Vector3 vehiclePosition = vehicle.transform.position;
        Quaternion vehicleRotation = vehicle.transform.rotation;
        float mass = vehicle.assignedContract.liveLoadWeight, speed = vehicle.maxSpeed, torque = vehicle.engineTorque;
        float direction = Mathf.Sign(end.position.x - start.position.x);
        Require(Mathf.Abs(end.position.x - start.position.x) > 20f, "Ronan's route direction is invalid.");
        Renderer[] oldWheels = vehicle.wheelObjects.Select(wheel => wheel.GetComponentInChildren<Renderer>(true)).ToArray();
        Require(oldWheels.All(wheel => wheel != null), "Cannot measure Ronan's old tire contact points.");
        float front = oldWheels.Max(wheel => wheel.bounds.center.x * direction) * direction;
        float ground = oldWheels.Min(wheel => wheel.bounds.min.y);
        float lane = oldWheels.Average(wheel => wheel.bounds.center.z);
        Renderer[] oldRenderers = vehicle.GetComponentsInChildren<Renderer>(true);
        Collider[] oldSolids = vehicle.GetComponentsInChildren<Collider>(true).Where(collider => !collider.isTrigger).ToArray();
        Vector3 scale = vehicle.transform.lossyScale;
        Require(scale.x > .001f && Mathf.Abs(scale.x - scale.y) < .001f && Mathf.Abs(scale.x - scale.z) < .001f,
            "Ronan's vehicle root has unexpected nonuniform scaling.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(Action);
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            Undo.RegisterCreatedObjectUndo(model, Action);
            Undo.SetTransformParent(model.transform, vehicle.transform, Action);
            Undo.RecordObject(model.transform, Action);
            model.name = VisualName;
            PrefabUtility.RecordPrefabInstancePropertyModifications(model);
            model.transform.localScale = Vector3.one / scale.x;
            model.transform.rotation = Quaternion.LookRotation(Vector3.left * direction, Vector3.up);
            model.transform.localPosition = Vector3.zero;
            GameObject[] wheels = model.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.name.StartsWith("Wheel_", StringComparison.Ordinal)).OrderBy(renderer => renderer.name)
                .Select(renderer => renderer.gameObject).ToArray();
            Require(wheels.Length == 4, "The Ronan bus must retain its four separate wheel meshes.");
            Renderer[] tireRenderers = wheels.Select(wheel => wheel.GetComponent<Renderer>()).ToArray();
            float newFront = tireRenderers.Max(wheel => wheel.bounds.center.x * direction) * direction;
            Vector3 offset = new Vector3(front - newFront, ground - tireRenderers.Min(wheel => wheel.bounds.min.y),
                lane - tireRenderers.Average(wheel => wheel.bounds.center.z));
            model.transform.position += offset;
            SeatOnBank(vehicle, model.transform, tireRenderers, front, direction);
            foreach (Transform item in model.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(item.gameObject, Action); item.gameObject.layer = vehicle.gameObject.layer;
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.gameObject);
            }
            var chassis = Undo.AddComponent<BoxCollider>(model);
            bool initialized = false; Bounds bounds = default;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true).Where(renderer => !wheels.Contains(renderer.gameObject)))
                for (int corner = 0; corner < 8; corner++)
                {
                    Bounds world = renderer.bounds;
                    var point = model.transform.InverseTransformPoint(new Vector3((corner & 1) == 0 ? world.min.x : world.max.x,
                        (corner & 2) == 0 ? world.min.y : world.max.y, (corner & 4) == 0 ? world.min.z : world.max.z));
                    if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; } else bounds.Encapsulate(point);
                }
            chassis.center = bounds.center; chassis.size = bounds.size;
            foreach (Renderer renderer in oldRenderers) Undo.DestroyObjectImmediate(renderer);
            foreach (Collider collider in oldSolids) Undo.DestroyObjectImmediate(collider);
            // Generated wheel collider radii are measured in world metres. Keep
            // the Rigidbody at unit world scale while retaining every child pose.
            float inherited = vehicle.transform.lossyScale.x;
            if (Mathf.Abs(inherited - 1f) > .0001f)
            {
                Undo.RecordObject(vehicle.transform, Action);
                foreach (Transform child in vehicle.transform)
                {
                    Undo.RecordObject(child, Action);
                    child.localPosition *= inherited;
                    child.localScale *= inherited;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                }
                vehicle.transform.localScale /= inherited;
                PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle.transform);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
            Undo.RecordObject(vehicle, Action);
            vehicle.vehicleName = "Bus"; vehicle.wheelObjects = wheels;
            vehicle.wheelRadius = tireRenderers.Average(renderer => renderer.bounds.extents.y);
            vehicle.spinAxis = Vector3.right;
            var data = new SerializedObject(vehicle);
            data.FindProperty("useWheelMeshRadius").boolValue = true;
            data.FindProperty("alignWheelAxisToMesh").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle);
            Physics.SyncTransforms();
            Require(vehicle.startPoint == start && vehicle.endPoint == end && start.position == startPosition && end.position == endPosition &&
                vehicle.transform.position == vehiclePosition && vehicle.transform.rotation == vehicleRotation &&
                vehicle.assignedContract.liveLoadWeight == mass && vehicle.maxSpeed == speed && vehicle.engineTorque == torque,
                "A protected route, pose, mass or driving setting changed.");
            string result = Validate(scene, vehicle, out _);
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); EditorSceneManager.MarkSceneDirty(scene);
            return result + "\nPreserved the existing controller, prefab identity, route markers, finish trigger, contract mass, speed and torque.";
        }
        catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
    }

    private static string Validate(Scene scene, LiveLoadVehicle vehicle, out Bounds fullBounds)
    {
        Transform model = vehicle.transform.Find(VisualName);
        Require(model != null && model.GetComponent<BoxCollider>() != null, "Ronan's bus body or collider is missing.");
        Require(vehicle.wheelObjects.Length == 4 && vehicle.wheelObjects.Distinct().Count() == 4 &&
            vehicle.wheelObjects.All(wheel => wheel != null && wheel.transform.IsChildOf(model)), "Bus wheel binding is invalid.");
        Require(vehicle.endPoint.GetComponent<FinishLineTrigger>()?.assignedContract == vehicle.assignedContract,
            "Ronan's finish trigger lost its contract.");
        Require(vehicle.GetComponent<Rigidbody>() != null && model.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
            model.GetComponentsInChildren<LiveLoadVehicle>(true).Length == 0, "The visual bus contains a duplicate controller or Rigidbody.");
        Require((vehicle.transform.lossyScale - Vector3.one).sqrMagnitude < .00001f,
            "The bus physics root must use world-metre dimensions.");
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        fullBounds = renderers[0].bounds; foreach (Renderer renderer in renderers) fullBounds.Encapsulate(renderer.bounds);
        Require(fullBounds.size.x > 7 && fullBounds.size.x < 11 && fullBounds.size.z > 2 && fullBounds.size.z < 4,
            "Bus size is not proportional to the existing road.");
        float direction = Mathf.Sign(vehicle.endPoint.position.x - vehicle.startPoint.position.x);
        Require(Vector3.Dot(-model.forward, Vector3.right * direction) > .99f, "Bus front faces away from the crossing.");
        var report = new StringBuilder("PASS: Ronan's bus; four separate driven wheels; wheel radius " + vehicle.wheelRadius.ToString("F3") +
            " m; size " + fullBounds.size.ToString("F3") + "; contract load " + vehicle.assignedContract.liveLoadWeight + " kg.");
        foreach (GameObject wheel in vehicle.wheelObjects)
        {
            Bounds bounds = wheel.GetComponent<Renderer>().bounds;
            RaycastHit[] hits = Physics.RaycastAll(bounds.center + Vector3.up * 2, Vector3.down, 5, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => !hit.collider.transform.IsChildOf(vehicle.transform) && hit.normal.y > .5f).OrderBy(hit => hit.distance).ToArray();
            Require(hits.Length > 0 && Mathf.Abs(hits[0].point.y - bounds.min.y) < .02f,
                "Bus tire is not seated on the starting bank: " + wheel.name + (hits.Length > 0 ? "; gap " + (bounds.min.y - hits[0].point.y) : "; no footing"));
            report.AppendLine("\nTire " + wheel.name + ": ground gap " + (bounds.min.y - hits[0].point.y).ToString("F3") + " m.");
        }
        return report.ToString();
    }

    private static void SeatOnBank(LiveLoadVehicle vehicle, Transform model, Renderer[] tires, float front, float direction)
    {
        float Ground(Renderer tire)
        {
            RaycastHit[] hits = Physics.RaycastAll(tire.bounds.center + Vector3.up * 2, Vector3.down, 5, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => !hit.collider.transform.IsChildOf(vehicle.transform) && hit.normal.y > .5f).OrderBy(hit => hit.distance).ToArray();
            Require(hits.Length > 0, "The bus has no bank under " + tire.name);
            return hits[0].point.y;
        }
        Physics.SyncTransforms();
        Renderer[] frontTires = tires.Where(tire => tire.name.StartsWith("Wheel_F", StringComparison.Ordinal)).ToArray();
        Renderer[] rearTires = tires.Where(tire => tire.name.StartsWith("Wheel_R", StringComparison.Ordinal)).ToArray();
        Require(frontTires.Length == 2 && rearTires.Length == 2, "Cannot identify the two bus axles.");
        float spacing = frontTires.Average(tire => tire.bounds.center.x) - rearTires.Average(tire => tire.bounds.center.x);
        float angle = Mathf.Atan((frontTires.Average(Ground) - rearTires.Average(Ground)) / spacing) * Mathf.Rad2Deg;
        Require(Mathf.Abs(angle) < 10f, "The authored bank slope is too steep for this replacement fit.");
        model.rotation = Quaternion.AngleAxis(angle, Vector3.forward) * model.rotation;
        float newFront = tires.Max(tire => tire.bounds.center.x * direction) * direction;
        model.position += Vector3.right * (front - newFront);
        Physics.SyncTransforms();
        float raise = tires.Max(tire => Ground(tire) - tire.bounds.min.y);
        model.position += Vector3.up * (raise + .002f);
        Physics.SyncTransforms();
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void InspectBatch()
    {
        Scene scene = EditorSceneManager.OpenScene(Canyon, OpenSceneMode.Single);
        Inspect(scene);
    }

    [MenuItem("Tools/Civil Craft/Story/Ronan/Inspect Bus Replacement")]
    public static void InspectLoaded() => Inspect(SceneManager.GetSceneByPath(Canyon));

    private static void Inspect(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open CanyonCrossing in Edit Mode first.");
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        var vehicle = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LiveLoadVehicle>(true))
            .Single(item => item.assignedContract != null && item.assignedContract.ContractID == "RonanMainContract");
        report.AppendLine("Vehicle " + vehicle.name + " at " + vehicle.transform.position.ToString("F3") + "; scale " + vehicle.transform.lossyScale.ToString("F3"));
        report.AppendLine("Start " + vehicle.startPoint.position.ToString("F3") + "; end " + vehicle.endPoint.position.ToString("F3") +
            "; contract mass " + vehicle.assignedContract.liveLoadWeight + "; body mass " + vehicle.GetComponent<Rigidbody>().mass);
        foreach (Transform t in vehicle.GetComponentsInChildren<Transform>(true))
        {
            Renderer r = t.GetComponent<Renderer>(); Collider c = t.GetComponent<Collider>();
            report.AppendLine("OLD " + t.name + "; local " + t.localPosition.ToString("F3") + "; euler " + t.localEulerAngles.ToString("F1") +
                (r != null ? "; bounds " + r.bounds.ToString("F3") : "") + (c != null ? "; collider " + c.GetType().Name + " " + c.bounds.ToString("F3") : ""));
        }
        report.AppendLine("Wheel radius " + vehicle.wheelRadius + "; spin axis " + vehicle.spinAxis + "; wheels " +
            string.Join(",", vehicle.wheelObjects.Select(item => item != null ? item.name : "MISSING")));
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (asset == null) throw new InvalidOperationException("Ronan's bus model has not imported.");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                Renderer r = t.GetComponent<Renderer>(); MeshFilter mesh = t.GetComponent<MeshFilter>();
                report.AppendLine("BUS " + t.name + "; local " + t.localPosition.ToString("F3") + "; euler " + t.localEulerAngles.ToString("F1") +
                    "; scale " + t.localScale.ToString("F3") + (r != null ? "; bounds " + r.bounds.ToString("F3") : "") +
                    (mesh != null && mesh.sharedMesh != null ? "; mesh " + mesh.sharedMesh.bounds.ToString("F3") : ""));
            }
            RenderModel(model, preview, Output + "/Bus-Imported.png");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        File.WriteAllText(Output + "/Inspection.txt", report.ToString());
        Debug.Log("[Ronan Bus] Read-only inspection complete.");
    }

    private static void RenderModel(GameObject model, Scene scene, string path)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        var cameraGo = new GameObject("Bus preview camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        SceneManager.MoveGameObjectToScene(cameraGo, scene);
        Camera camera = cameraGo.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
        camera.orthographic = true; camera.orthographicSize = bounds.size.magnitude * .42f;
        camera.transform.position = bounds.center + model.transform.TransformDirection(new Vector3(-1, .55f, -1)).normalized * bounds.size.magnitude * 2;
        camera.transform.LookAt(bounds.center); camera.farClipPlane = bounds.size.magnitude * 10;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(96, 119, 138, 255);
        var lightGo = new GameObject("Bus preview light", typeof(Light)); SceneManager.MoveGameObjectToScene(lightGo, scene);
        Light light = lightGo.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
        lightGo.transform.rotation = Quaternion.Euler(40, -30, 0);
        RenderTexture previous = RenderTexture.active;
        var target = new RenderTexture(1024, 640, 24); target.Create();
        var image = new Texture2D(1024, 640, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1024, 640), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraGo); UnityEngine.Object.DestroyImmediate(lightGo);
        }
    }
}
#endif
