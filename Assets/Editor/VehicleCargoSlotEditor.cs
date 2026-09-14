using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VehicleCargoSlotEditor
{
    [MenuItem("GameObject/Civil Craft/Vehicle Cargo Slot", false, 20)]
    private static void CreateSlot()
    {
        Transform selected = Selection.activeTransform;
        LiveLoadVehicle truck = selected != null ? selected.GetComponentInParent<LiveLoadVehicle>() : null;
        if (truck == null || !truck.gameObject.scene.IsValid())
        {
            EditorUtility.DisplayDialog("Vehicle Cargo Slot", "Select a scene vehicle or its truck-bed child first.", "OK");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var go = new GameObject("Cargo Loading Slot");
        Undo.RegisterCreatedObjectUndo(go, "Create cargo loading slot");
        go.transform.SetParent(selected, false);
        var slot = Undo.AddComponent<VehicleCargoSlot>(go);
        slot.vehicle = truck;
        slot.cargoSocket = go.transform;
        go.layer = LayerMask.NameToLayer("Interactable");
        var trigger = go.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = Vector3.one;
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[Vehicle Cargo] Move this slot to the cargo pivot position in the truck bed. Resize its trigger for access from the back. Enable Allow Vehicle Cargo on the vehicle's contract.", slot);
    }
}
