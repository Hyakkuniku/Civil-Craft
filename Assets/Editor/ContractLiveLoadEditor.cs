#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(ContractSO))]
public sealed class ContractLiveLoadEditor : Editor
{
    private ReorderableList entries;

    private void OnEnable()
    {
        SerializedProperty list = serializedObject.FindProperty("liveLoadTests");
        if (list == null) return;
        entries = new ReorderableList(serializedObject, list, true, true, true, true);
        entries.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Required vehicles (2–4, ordered)");
        entries.elementHeight = EditorGUIUtility.singleLineHeight * 8f + 14f;
        entries.drawElementCallback = (rect, index, active, focused) =>
        {
            SerializedProperty entry = list.GetArrayElementAtIndex(index);
            rect.y += 3f; rect.height = EditorGUIUtility.singleLineHeight;
            using (new EditorGUI.DisabledScope(true)) EditorGUI.PropertyField(rect, entry.FindPropertyRelative("entryId"), new GUIContent("Permanent entry ID"));
            foreach (string field in new[] { "displayName", "emptyWeightKg", "departureDelay", "allowCargo", "minimumLoadedCargo" })
            {
                rect.y += EditorGUIUtility.singleLineHeight + 2f;
                EditorGUI.PropertyField(rect, entry.FindPropertyRelative(field));
            }
            rect.y += EditorGUIUtility.singleLineHeight + 2f;
            EditorGUI.LabelField(rect, "Bind this ID to a vehicle on its Build Location.", EditorStyles.miniLabel);
        };
        entries.onAddCallback = value =>
        {
            int index = list.arraySize++; SerializedProperty entry = list.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("entryId").stringValue = Guid.NewGuid().ToString("N");
            entry.FindPropertyRelative("displayName").stringValue = "Vehicle " + (index + 1);
            entry.FindPropertyRelative("emptyWeightKg").floatValue = 0f;
            entry.FindPropertyRelative("departureDelay").floatValue = 0f;
            entry.FindPropertyRelative("allowCargo").boolValue = false;
            entry.FindPropertyRelative("minimumLoadedCargo").intValue = 0;
            value.index = index;
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty mode = serializedObject.FindProperty("vehicleTestMode");
        EditorGUILayout.PropertyField(mode, new GUIContent("Vehicle test mode"));
        bool multiple = mode.enumValueIndex != 0;
        if (multiple)
        {
            EditorGUILayout.HelpBox("Each vehicle has its own empty weight; loaded cargo is added. Sequential tests retain bridge damage. Together delays start after settling and pause with the simulation. Existing single-load values are kept when switching modes.", MessageType.Info);
            entries?.DoLayoutList();
        }
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("liveLoadWeight"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("allowVehicleCargo"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("minimumLoadedCargo"));
        }
        DrawPropertiesExcluding(serializedObject, "m_Script", "vehicleTestMode", "liveLoadTests", "liveLoadWeight", "allowVehicleCargo", "minimumLoadedCargo");
        serializedObject.ApplyModifiedProperties();
        foreach (string issue in ValidateDefinition((ContractSO)target)) EditorGUILayout.HelpBox(issue, MessageType.Error);
    }

    public static IEnumerable<string> ValidateDefinition(ContractSO contract)
    {
        if (contract == null || !contract.HasMultipleLiveLoads) yield break;
        if (contract.liveLoadMode != ContractSO.LiveLoadMode.Vehicle || contract.winCondition != ContractSO.WinCondition.FinishLine)
            yield return "Multiple live loads require Vehicle mode and Finish Line completion.";
        if (contract.liveLoadTests == null || contract.liveLoadTests.Count < 2 || contract.liveLoadTests.Count > 4)
            yield return "Configure between two and four required vehicles.";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (contract.liveLoadTests == null) yield break;
        for (int index = 0; index < contract.liveLoadTests.Count; index++)
        {
            ContractLiveLoadEntry entry = contract.liveLoadTests[index];
            if (entry == null) { yield return "Vehicle " + (index + 1) + " is missing."; continue; }
            if (string.IsNullOrWhiteSpace(entry.entryId) || !ids.Add(entry.entryId)) yield return "Vehicle " + (index + 1) + " needs a unique permanent ID. Use + to create a new entry; do not duplicate another entry's ID.";
            if (!Finite(entry.emptyWeightKg) || entry.emptyWeightKg <= 0f || entry.emptyWeightKg > LiveLoadTestRoster.MaxWeightKg)
                yield return "Set a positive empty vehicle weight up to " + LiveLoadTestRoster.MaxWeightKg.ToString("N0") + " kg for " + ContractLiveLoadPresentation.Label(entry.displayName, index) + ".";
            if (!Finite(entry.departureDelay) || entry.departureDelay < 0f || entry.departureDelay > LiveLoadTestRoster.MaxDepartureDelay)
                yield return "Departure delays must be between 0 and " + LiveLoadTestRoster.MaxDepartureDelay.ToString("N0") + " simulation seconds.";
            if (entry.minimumLoadedCargo < 0 || (!entry.allowCargo && entry.minimumLoadedCargo > 0)) yield return "Minimum cargo must be nonnegative and requires Allow Cargo.";
        }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

[CustomEditor(typeof(BuildLocation))]
public sealed class MultiLoadBuildLocationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        BuildLocation location = (BuildLocation)target;
        if (location.activeContract == null || !location.activeContract.HasMultipleLiveLoads) return;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Choose each required vehicle entry once in the bindings, then drag its existing scene vehicle with Start and End points. No vehicle is created or assigned to other contracts automatically.", MessageType.Info);
        if (GUILayout.Button("Validate This Live-Load Setup (Read Only)"))
        {
            bool valid = LiveLoadTestRoster.TryCreate(location, location.activeContract, out _, out string reason, false);
            if (valid) Debug.Log("[Multiple Live Loads] Setup is valid: " + location.name, location);
            else Debug.LogWarning("[Multiple Live Loads] " + reason, location);
        }
    }
}

[CustomPropertyDrawer(typeof(ContractLiveLoadBinding))]
public sealed class ContractLiveLoadBindingDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight * 2f + 8f;
    public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(rect, label, property);
        var location = property.serializedObject.targetObject as BuildLocation;
        var ids = new List<string> { string.Empty }; var names = new List<string> { "Choose required vehicle" };
        if (location != null && location.activeContract != null && location.activeContract.liveLoadTests != null)
            for (int index = 0; index < location.activeContract.liveLoadTests.Count; index++)
            {
                ContractLiveLoadEntry entry = location.activeContract.liveLoadTests[index]; if (entry == null) continue;
                ids.Add(entry.entryId); names.Add((index + 1) + ". " + ContractLiveLoadPresentation.Label(entry.displayName, index));
            }
        SerializedProperty id = property.FindPropertyRelative("entryId");
        int selected = ids.IndexOf(id.stringValue);
        if (selected < 0) { selected = ids.Count; ids.Add(id.stringValue); names.Add("Missing contract entry (fix this binding)"); }
        rect.y += 2f; rect.height = EditorGUIUtility.singleLineHeight;
        int next = EditorGUI.Popup(rect, "Contract entry", selected, names.ToArray());
        if (next != selected) id.stringValue = ids[next];
        rect.y += EditorGUIUtility.singleLineHeight + 3f;
        EditorGUI.PropertyField(rect, property.FindPropertyRelative("vehicle"), new GUIContent("Scene vehicle"));
        EditorGUI.EndProperty();
    }
}
#endif
