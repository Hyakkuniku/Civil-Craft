using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class VehicleInspectionPanelRepair
{
    static VehicleInspectionPanelRepair()
    {
        EditorApplication.delayCall += Repair;
        EditorSceneManager.sceneOpened += (scene, mode) => Repair();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Repair;
        };
    }

    [MenuItem("Tools/Civil Craft/Repair Shared Vehicle Panel Close Buttons")]
    private static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var panels = new HashSet<GameObject>();
        foreach (LiveLoadVehicle vehicle in Resources.FindObjectsOfTypeAll<LiveLoadVehicle>())
        {
            if (!vehicle.gameObject.scene.IsValid() || !vehicle.gameObject.scene.isLoaded ||
                EditorUtility.IsPersistent(vehicle) || vehicle.vehicleInfoPanel == null)
                continue;

            GameObject panel = vehicle.vehicleInfoPanel;
            panels.Add(panel);
            VehicleInspectionPanel handler = panel.GetComponent<VehicleInspectionPanel>();
            if (handler == null) handler = Undo.AddComponent<VehicleInspectionPanel>(panel);
            if (handler.LayoutVersion >= VehicleInspectionPanel.CurrentLayoutVersion) continue;

            Undo.RecordObjects(panel.GetComponentsInChildren<RectTransform>(true), "Revamp vehicle inspection layout");
            Undo.RecordObjects(panel.GetComponentsInChildren<TMP_Text>(true), "Revamp vehicle inspection typography");
            Undo.RecordObject(handler, "Revamp vehicle inspection layout");
            handler.ApplyLayout(vehicle.vehicleNameText, vehicle.vehicleWeightText, vehicle.vehicleSpeedText);
            foreach (RectTransform rect in panel.GetComponentsInChildren<RectTransform>(true)) EditorUtility.SetDirty(rect);
            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true)) EditorUtility.SetDirty(text);
            EditorUtility.SetDirty(handler);
            PrefabUtility.RecordPrefabInstancePropertyModifications(handler);
            EditorSceneManager.MarkSceneDirty(panel.scene);
        }
        int repaired = 0;
        foreach (GameObject panel in panels)
        {
            if (!panel.scene.IsValid() || PrefabStageUtility.GetPrefabStage(panel) != null) continue;
            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            {
                var data = new SerializedObject(button);
                var calls = data.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                bool changed = false;
                for (int i = 0; i < calls.arraySize; i++)
                {
                    var call = calls.GetArrayElementAtIndex(i);
                    if (call.FindPropertyRelative("m_MethodName").stringValue != "CloseInfoPanel") continue;
                    string type = call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue;
                    if (!type.StartsWith("LiveLoadVehicle,")) continue;
                    var handler = panel.GetComponent<VehicleInspectionPanel>();
                    if (handler == null) handler = Undo.AddComponent<VehicleInspectionPanel>(panel);
                    Undo.RecordObject(button, "Repair shared vehicle Close button");
                    call.FindPropertyRelative("m_Target").objectReferenceValue = handler;
                    call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = typeof(VehicleInspectionPanel).AssemblyQualifiedName;
                    call.FindPropertyRelative("m_MethodName").stringValue = "Close";
                    call.FindPropertyRelative("m_Mode").intValue = 1;
                    // Preserve call state and all unrelated button callbacks.
                    changed = true;
                    repaired++;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(handler);
                }
                if (!changed) continue;
                data.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
                EditorSceneManager.MarkSceneDirty(panel.scene);
            }
        }
        if (repaired > 0) Debug.Log($"[Vehicle Panel Repair] Connected {repaired} Close listener(s) to the shared panel instead of a vehicle. Save the scene to keep the repair.");
    }
}
