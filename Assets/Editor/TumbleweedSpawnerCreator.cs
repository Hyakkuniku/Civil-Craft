using UnityEditor;
using UnityEngine;

public static class TumbleweedSpawnerCreator
{
    private const string PrefabPath =
        "Assets/Elements/Canyon Crossing - MAP 1/Map 1/PROPS/tumbleweed.prefab";

    [MenuItem("GameObject/Civil Craft/Tumbleweed Spawner", false, 35)]
    private static void CreateSpawner(MenuCommand command)
    {
        GameObject gameObject = new GameObject("Tumbleweed Spawner");
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Tumbleweed Spawner");
        GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
        TumbleweedSpawner spawner = Undo.AddComponent<TumbleweedSpawner>(gameObject);

        SerializedObject serializedSpawner = new SerializedObject(spawner);
        serializedSpawner.FindProperty("tumbleweedPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        serializedSpawner.ApplyModifiedProperties();

        Selection.activeGameObject = gameObject;
    }

    [MenuItem("GameObject/Civil Craft/Tumbleweed Spawn Area", false, 36)]
    private static void CreateSpawnArea(MenuCommand command)
    {
        GameObject context = command.context as GameObject;
        TumbleweedSpawner parentSpawner = context != null
            ? context.GetComponentInParent<TumbleweedSpawner>()
            : null;

        GameObject gameObject = new GameObject("Tumbleweed Spawn Area");
        Undo.RegisterCreatedObjectUndo(gameObject, "Create Tumbleweed Spawn Area");
        if (parentSpawner != null)
            GameObjectUtility.SetParentAndAlign(gameObject, parentSpawner.gameObject);
        else if (context != null)
            gameObject.transform.position = context.transform.position;

        TumbleweedSpawnArea area = Undo.AddComponent<TumbleweedSpawnArea>(gameObject);
        if (parentSpawner != null)
        {
            SerializedObject serializedSpawner = new SerializedObject(parentSpawner);
            SerializedProperty areas = serializedSpawner.FindProperty("spawnAreas");
            int index = areas.arraySize;
            areas.InsertArrayElementAtIndex(index);
            areas.GetArrayElementAtIndex(index).objectReferenceValue = area;
            serializedSpawner.ApplyModifiedProperties();
            EditorUtility.SetDirty(parentSpawner);
        }

        Selection.activeGameObject = gameObject;
    }
}
