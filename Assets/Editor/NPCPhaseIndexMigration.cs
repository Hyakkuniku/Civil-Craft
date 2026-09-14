using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Converts serialized listeners in-place, retaining listener order and call state.
[InitializeOnLoad]
public static class NPCPhaseIndexMigration
{
    static NPCPhaseIndexMigration()
    {
        EditorApplication.delayCall += Migrate;
        EditorSceneManager.sceneOpened += (scene, mode) => Migrate();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Migrate;
        };
    }

    [MenuItem("Tools/Civil Craft/Migrate NPC Phase Index Events")]
    private static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
        if (!scene.IsValid() || !scene.isLoaded) return;
        int migrated = 0, skipped = 0;
        foreach (MonoBehaviour owner in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (owner == null || !owner.gameObject.scene.IsValid() ||
                owner.gameObject.scene.path != "Assets/Scenes/CanyonCrossing.unity") continue;
            var data = new SerializedObject(owner);
            var iterator = data.GetIterator();
            var paths = new List<string>();
            while (iterator.Next(true))
                if (iterator.name == "m_MethodName" && iterator.propertyType == SerializedPropertyType.String &&
                    iterator.stringValue == "MoveToPhase")
                    paths.Add(iterator.propertyPath.Substring(0, iterator.propertyPath.Length - ".m_MethodName".Length));
            foreach (string path in paths)
            {
                var call = data.FindProperty(path);
                var npc = call.FindPropertyRelative("m_Target").objectReferenceValue as NPCProgressionManager;
                int index = call.FindPropertyRelative("m_Arguments.m_IntArgument").intValue;
                string id = Resolve(npc, index);
                if (id == null || call.FindPropertyRelative("m_Mode").intValue != 3)
                {
                    skipped++;
                    Debug.LogWarning($"[Phase Migration] Kept ambiguous listener {owner.name}/{path}, index {index}. Target must have a unique nonempty phase ID and a constant integer argument.", owner);
                    continue;
                }
                NPCPhaseAction action = Undo.AddComponent<NPCPhaseAction>(owner.gameObject);
                var actionData = new SerializedObject(action);
                actionData.FindProperty("npc").objectReferenceValue = npc;
                actionData.FindProperty("phaseId").stringValue = id;
                actionData.ApplyModifiedProperties();
                Undo.RecordObject(owner, "Migrate phase index event");
                call.FindPropertyRelative("m_Target").objectReferenceValue = action;
                call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = typeof(NPCPhaseAction).AssemblyQualifiedName;
                call.FindPropertyRelative("m_MethodName").stringValue = "MoveToSelectedPhase";
                call.FindPropertyRelative("m_Mode").intValue = 1; // Void, not a dynamic argument.
                call.FindPropertyRelative("m_Arguments.m_IntArgument").intValue = 0;
                data.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
                PrefabUtility.RecordPrefabInstancePropertyModifications(action);
                EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
                Debug.Log($"[Phase Migration] {owner.name}/{path}: {npc.name} index {index} -> '{id}'.", owner);
                migrated++;
            }
        }
        // Zero-length event arrays can retain dormant prefab overrides. Convert
        // those too without enlarging the array or re-enabling a removed callback.
        foreach (var root in scene.GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)) continue;
            var mods = PrefabUtility.GetPropertyModifications(child.gameObject);
            if (mods == null) continue;
            var updated = new List<PropertyModification>(mods);
            bool changed = false;
            foreach (var method in mods)
            {
                if (method.value != "MoveToPhase" || !method.propertyPath.EndsWith(".m_MethodName")) continue;
                string path = method.propertyPath.Substring(0, method.propertyPath.Length - ".m_MethodName".Length);
                var target = updated.Find(p => p.target == method.target && p.propertyPath == path + ".m_Target");
                var arg = updated.Find(p => p.target == method.target && p.propertyPath == path + ".m_Arguments.m_IntArgument");
                var mode = updated.Find(p => p.target == method.target && p.propertyPath == path + ".m_Mode");
                if (arg == null || !int.TryParse(arg.value, out int index) || mode == null || mode.value != "3") continue;
                string id = Resolve(target?.objectReference as NPCProgressionManager, index);
                if (id == null) { skipped++; continue; }
                method.value = "MoveToPhaseById";
                mode.value = "5"; // Constant string.
                arg.value = "0";
                var str = updated.Find(p => p.target == method.target && p.propertyPath == path + ".m_Arguments.m_StringArgument");
                if (str == null) { str = new PropertyModification { target = method.target, propertyPath = path + ".m_Arguments.m_StringArgument" }; updated.Add(str); }
                str.value = id;
                changed = true;
                migrated++;
                Debug.Log($"[Phase Migration] Prefab override {child.name}/{path}: index {index} -> '{id}' (event array size unchanged).", child);
            }
            if (!changed) continue;
            Undo.RegisterFullObjectHierarchyUndo(child.gameObject, "Migrate dormant phase overrides");
            PrefabUtility.SetPropertyModifications(child.gameObject, updated.ToArray());
            EditorSceneManager.MarkSceneDirty(child.gameObject.scene);
        }
        if (migrated > 0 || skipped > 0)
            Debug.Log($"[Phase Migration] Migrated {migrated} listeners; skipped {skipped}. Save CanyonCrossing to persist these Inspector changes.");
    }

    private static string Resolve(NPCProgressionManager npc, int index)
    {
        if (npc == null) return null;
        var phases = new SerializedObject(npc).FindProperty("phases");
        if (index < 0 || index >= phases.arraySize) return null;
        string id = phases.GetArrayElementAtIndex(index).FindPropertyRelative("phaseId").stringValue;
        if (string.IsNullOrWhiteSpace(id)) return null;
        int count = 0;
        for (int i = 0; i < phases.arraySize; i++)
            if (phases.GetArrayElementAtIndex(i).FindPropertyRelative("phaseId").stringValue == id) count++;
        return count == 1 ? id : null;
    }
}
