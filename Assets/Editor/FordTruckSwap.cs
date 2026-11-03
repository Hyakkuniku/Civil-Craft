using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class FordTruckSwap
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string ContractPath = "Assets/BridgeBuilder/Data/Contract/TownRiver/Ford Contract.asset";
    private const string ModelName = "ford's main quest cargo_truck";
    private const string Request = "Temp/replace-ford-truck.request";
    private const string Report = "Logs/ford-truck-swap.txt";
    private const string ActionName = "Replace Ford's test vehicle";

    [InitializeOnLoadMethod]
    private static void RegisterRequest() => EditorApplication.update += CheckRequest;

    private static void CheckRequest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request);
        Apply();
    }

    [MenuItem("Tools/Civil Craft/Story/Replace Ford Test Vehicle")]
    private static void Apply()
    {
        try { Replace(); }
        catch (Exception error) { File.WriteAllText(Report, error.ToString()); Debug.LogException(error); }
    }

    private static void Replace()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        ContractSO contract = AssetDatabase.LoadAssetAtPath<ContractSO>(ContractPath);
        if (!scene.isLoaded || contract == null || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open CanyonCrossing in Edit Mode first.");

        GameObject[] roots = scene.GetRootGameObjects();
        LiveLoadVehicle vehicle = roots.SelectMany(root => root.GetComponentsInChildren<LiveLoadVehicle>(true))
            .Single(candidate => candidate.assignedContract == contract);
        Transform start = vehicle.startPoint;
        Transform end = vehicle.endPoint;
        if (start == null || end == null || end.GetComponent<FinishLineTrigger>()?.assignedContract != contract)
            throw new InvalidOperationException("Ford's original route and finish trigger could not be confirmed.");

        Transform installed = vehicle.transform.Find("Ford Truck");
        if (installed != null)
        {
            Validate(vehicle, installed, contract, start, end);
            return;
        }

        GameObject donor = roots.Single(root => root.name == "Ford Truck");
        Transform model = donor.transform.Find(ModelName);
        if (model == null) throw new InvalidOperationException("The new Ford Truck model is missing.");
        LiveLoadVehicle[] placeholders = donor.GetComponentsInChildren<LiveLoadVehicle>(true);
        if (placeholders.Length != 2 || placeholders.Any(item => item.assignedContract != null ||
            item.startPoint != null || item.endPoint != null || item.physicsManager != null) ||
            donor.GetComponentsInChildren<Collider>(true).Length != 0)
            throw new InvalidOperationException("The added truck has custom gameplay wiring. Review it before replacing Ford's vehicle.");

        MeshRenderer chassis = model.Find("Cargo_Truck.001")?.GetComponent<MeshRenderer>();
        GameObject[] wheels = Enumerable.Range(2, 6)
            .Select(index => model.Find($"Cargo_Truck.{index:000}")?.gameObject).ToArray();
        if (chassis == null || wheels.Any(wheel => wheel == null || wheel.GetComponent<MeshRenderer>() == null))
            throw new InvalidOperationException("Expected the Ford truck chassis and six separate wheel meshes.");
        if (vehicle.wheelObjects == null || vehicle.wheelObjects.Length == 0)
            throw new InvalidOperationException("Ford's current wheel height could not be determined.");

        MeshRenderer[] oldRenderers = vehicle.GetComponentsInChildren<MeshRenderer>(true);
        Collider[] oldSolids = vehicle.GetComponentsInChildren<Collider>(true).Where(item => !item.isTrigger).ToArray();
        float oldGround = vehicle.wheelObjects.Select(wheel => wheel.GetComponentInChildren<Renderer>(true).bounds.min.y).Min();
        float direction = Mathf.Sign(end.position.x - start.position.x);
        if (Mathf.Abs(end.position.x - start.position.x) < 1f)
            throw new InvalidOperationException("Ford's route must have distinct start and end positions.");

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(ActionName);
        try
        {
            // Keep the existing vehicle component and prefab hierarchy so scene
            // references, route markers, cargo slots and story events retain their IDs.
            foreach (LiveLoadVehicle placeholder in placeholders) Undo.DestroyObjectImmediate(placeholder);
            foreach (Rigidbody body in donor.GetComponentsInChildren<Rigidbody>(true)) Undo.DestroyObjectImmediate(body);
            foreach (MeshRenderer renderer in oldRenderers) Undo.DestroyObjectImmediate(renderer);
            foreach (Collider collider in oldSolids) Undo.DestroyObjectImmediate(collider);

            Undo.SetTransformParent(donor.transform, vehicle.transform, ActionName);
            Undo.RecordObject(donor.transform, ActionName);
            if (Vector3.Dot(model.forward, Vector3.right * direction) < 0f)
                donor.transform.Rotate(Vector3.up, 180f, Space.World);
            float newGround = wheels.Min(wheel => wheel.GetComponent<Renderer>().bounds.min.y);
            Vector3 position = donor.transform.position;
            position.x = vehicle.transform.position.x;
            position.z = vehicle.transform.position.z;
            position.y += oldGround - newGround;
            donor.transform.position = position;
            foreach (Transform part in donor.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(part.gameObject, ActionName);
                part.gameObject.layer = vehicle.gameObject.layer;
            }

            BoxCollider box = Undo.AddComponent<BoxCollider>(donor);
            MeshFilter bodyMesh = chassis.GetComponent<MeshFilter>();
            Bounds bodyBounds = bodyMesh.sharedMesh.bounds;
            Bounds boxBounds = new Bounds(donor.transform.InverseTransformPoint(bodyMesh.transform.TransformPoint(bodyBounds.center)), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3((corner & 1) == 0 ? bodyBounds.min.x : bodyBounds.max.x,
                    (corner & 2) == 0 ? bodyBounds.min.y : bodyBounds.max.y,
                    (corner & 4) == 0 ? bodyBounds.min.z : bodyBounds.max.z);
                boxBounds.Encapsulate(donor.transform.InverseTransformPoint(bodyMesh.transform.TransformPoint(point)));
            }
            box.center = boxBounds.center;
            box.size = boxBounds.size;

            Undo.RecordObject(vehicle, ActionName);
            vehicle.vehicleName = "Ford Truck";
            vehicle.wheelObjects = wheels;
            vehicle.wheelRadius = wheels.Average(wheel => wheel.GetComponent<Renderer>().bounds.extents.y);
            PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle);
            Validate(vehicle, donor.transform, contract, start, end);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
        }
        catch
        {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    private static void Validate(LiveLoadVehicle vehicle, Transform donor, ContractSO contract, Transform start, Transform end)
    {
        if (vehicle.assignedContract != contract || vehicle.startPoint != start || vehicle.endPoint != end ||
            vehicle.GetComponent<Rigidbody>() == null || donor.GetComponentInChildren<Rigidbody>(true) != null ||
            donor.GetComponentInChildren<LiveLoadVehicle>(true) != null ||
            donor.GetComponent<BoxCollider>() == null ||
            vehicle.wheelObjects == null || vehicle.wheelObjects.Length != 6 ||
            vehicle.wheelObjects.Distinct().Count() != 6 ||
            vehicle.wheelObjects.Any(wheel => wheel == null || !wheel.transform.IsChildOf(donor)) ||
            vehicle.wheelRadius <= 0f)
            throw new InvalidOperationException("Ford's replacement truck failed its scene wiring checks.");
        string result = $"PASS: Ford Truck uses the existing Ford contract vehicle, start and finish. Six wheels, radius {vehicle.wheelRadius:F2} m, and chassis collider verified.";
        File.WriteAllText(Report, result);
        Debug.Log(result, vehicle);
    }
}
