using UnityEditor;
using UnityEngine;

public static class StoryCargoDropLocationCreator
{
    [MenuItem("GameObject/Civil Craft/Story Cargo Drop Location", false, 20)]
    private static void CreateStoryCargoDropLocation()
    {
        CargoItem selectedCargo = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<CargoItem>()
            : null;

        GameObject zoneObject = new GameObject("Story Cargo Drop Location");
        Undo.RegisterCreatedObjectUndo(zoneObject, "Create story cargo drop location");
        if (Selection.activeTransform != null)
        {
            zoneObject.transform.SetParent(Selection.activeTransform.parent, true);
            zoneObject.transform.position = Selection.activeTransform.position;
        }

        BoxCollider zone = Undo.AddComponent<BoxCollider>(zoneObject);
        zone.isTrigger = true;
        zone.size = new Vector3(3f, 3f, 3f);

        CargoDropLocation drop = Undo.AddComponent<CargoDropLocation>(zoneObject);
        drop.allowStoryDeliveryWithoutContract = true;
        drop.acceptedStoryCargo = selectedCargo;

        GameObject socketObject = new GameObject("Cargo Drop Socket");
        Undo.RegisterCreatedObjectUndo(socketObject, "Create story cargo socket");
        socketObject.transform.SetParent(zoneObject.transform, false);
        drop.dropSocket = socketObject.transform;

        int interactableLayer = LayerMask.NameToLayer("Interactable");
        if (interactableLayer >= 0) zoneObject.layer = interactableLayer;

        if (selectedCargo != null)
        {
            Undo.RecordObject(selectedCargo, "Mark selected cargo as story cargo");
            selectedCargo.storyCargo = true;
            EditorUtility.SetDirty(selectedCargo);
        }

        Selection.activeGameObject = zoneObject;
        EditorGUIUtility.PingObject(zoneObject);
    }
}
