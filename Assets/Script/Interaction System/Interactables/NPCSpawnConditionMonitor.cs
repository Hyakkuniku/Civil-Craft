using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Kept outside gated NPC hierarchies so an inactive NPC can become visible again.
public sealed class NPCSpawnConditionMonitor : MonoBehaviour
{
    private static readonly HashSet<NPCSpawnerCondition> conditions = new HashSet<NPCSpawnerCondition>();
    private readonly List<NPCSpawnerCondition> snapshot = new List<NPCSpawnerCondition>();
    private float nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        conditions.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        var root = new GameObject("NPC Spawn Condition Monitor");
        DontDestroyOnLoad(root);
        root.AddComponent<NPCSpawnConditionMonitor>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (NPCSpawnerCondition condition in root.GetComponentsInChildren<NPCSpawnerCondition>(true))
                Register(condition);
    }

    public static void Register(NPCSpawnerCondition condition)
    {
        if (condition != null && condition.gameObject.scene.IsValid()) conditions.Add(condition);
    }
    public static void Unregister(NPCSpawnerCondition condition) { conditions.Remove(condition); }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .25f;
        // Visibility events may add or destroy NPCs during evaluation.
        conditions.RemoveWhere(condition => condition == null);
        snapshot.Clear();
        snapshot.AddRange(conditions);
        foreach (NPCSpawnerCondition condition in snapshot)
            if (condition != null && condition.gameObject.scene.isLoaded) condition.TickConditions();
    }
}
