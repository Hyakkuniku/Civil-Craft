using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

// Targeted, manual migration: no runtime UI creation or changes to other vehicles.
// This used to run after every script reload, scene open, and Play Mode exit.
// The Truck1 scene setup has already been migrated, so repeatedly scanning all
// loaded objects only stalls the Editor and can dirty the scene unexpectedly.
public static class Truck1SceneSetupRepair
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";

    [MenuItem("Tools/Civil Craft/Repair Truck1 Setup")]
    private static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (LiveLoadVehicle truck in Resources.FindObjectsOfTypeAll<LiveLoadVehicle>())
        {
            if (truck.gameObject.scene.path != ScenePath || truck.name != "truck1 1") continue;
            var data = new SerializedObject(truck);
            bool changed = false;
            var rotation = data.FindProperty("useWaypointRotation");
            if (rotation.boolValue) { rotation.boolValue = false; changed = true; }
            changed |= AssignIfEmpty(data, "vehicleInfoPanel", FindSceneObject<GameObject>(8880294211448637854UL));
            changed |= AssignIfEmpty(data, "vehicleNameText", FindSceneObject<TMP_Text>(3652125584594633526UL));
            changed |= AssignIfEmpty(data, "vehicleWeightText", FindSceneObject<TMP_Text>(6128661215989856902UL));
            changed |= AssignIfEmpty(data, "vehicleSpeedText", FindSceneObject<TMP_Text>(7088754691972931975UL));
            if (!changed) continue;
            Undo.RecordObject(truck, "Repair Truck1 facing and inspection");
            data.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(truck);
            EditorSceneManager.MarkSceneDirty(truck.gameObject.scene);
            Debug.Log("[Truck1] Preserved authored facing and connected available inspection references. Save CanyonCrossing to keep these Inspector changes.", truck);
        }
    }

    private static bool AssignIfEmpty(SerializedObject data, string field, Object value)
    {
        var property = data.FindProperty(field);
        if (property.objectReferenceValue != null || value == null) return false;
        property.objectReferenceValue = value;
        return true;
    }

    private static T FindSceneObject<T>(ulong localId) where T : Object
    {
        foreach (T item in Resources.FindObjectsOfTypeAll<T>())
        {
            GameObject go = item as GameObject;
            if (go == null && item is Component component) go = component.gameObject;
            if (go == null || go.scene.path != ScenePath) continue;
            if (GlobalObjectId.GetGlobalObjectIdSlow(item).targetObjectId == localId) return item;
        }
        return null;
    }
}
