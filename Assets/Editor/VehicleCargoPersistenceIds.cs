using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Persist identities in the scene, not object names, hierarchy paths, or runtime instance IDs.
[InitializeOnLoad]
public static class VehicleCargoPersistenceIds
{
    private static bool scheduled;
    static VehicleCargoPersistenceIds()
    {
        EditorApplication.hierarchyChanged += Schedule;
        EditorSceneManager.sceneOpened += (scene, mode) => Schedule();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) Schedule();
        };
        Schedule();
    }

    private static void Schedule()
    {
        if (scheduled) return;
        scheduled = true;
        EditorApplication.delayCall += AssignIds;
    }

    [MenuItem("Tools/Civil Craft/Assign Vehicle Cargo Save IDs")]
    private static void AssignIds()
    {
        scheduled = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int changed = Assign<CargoItem>("persistentCargoId") + Assign<VehicleCargoSlot>("persistentSlotId");
        if (changed > 0)
            Debug.Log($"[Vehicle Cargo] Assigned {changed} persistent cargo/slot IDs. Save the scene before testing permanent loading.");
    }

    private static int Assign<T>(string field) where T : Component
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var items = Resources.FindObjectsOfTypeAll<T>();
        // Existing saved objects are visited before newly duplicated scene objects.
        Array.Sort(items, (a, b) => GlobalObjectId.GetGlobalObjectIdSlow(a).targetObjectId.CompareTo(
            GlobalObjectId.GetGlobalObjectIdSlow(b).targetObjectId));
        int changed = 0;
        foreach (T item in items)
        {
            if (EditorUtility.IsPersistent(item) || !item.gameObject.scene.IsValid() ||
                !item.gameObject.scene.isLoaded || string.IsNullOrEmpty(item.gameObject.scene.path) ||
                PrefabStageUtility.GetPrefabStage(item.gameObject) != null) continue;
            var data = new SerializedObject(item);
            var id = data.FindProperty(field);
            if (!string.IsNullOrWhiteSpace(id.stringValue) && used.Add(id.stringValue)) continue;
            Undo.RecordObject(item, "Assign vehicle cargo save ID");
            id.stringValue = Guid.NewGuid().ToString("N");
            used.Add(id.stringValue);
            data.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            EditorSceneManager.MarkSceneDirty(item.gameObject.scene);
            changed++;
        }
        return changed;
    }
}
