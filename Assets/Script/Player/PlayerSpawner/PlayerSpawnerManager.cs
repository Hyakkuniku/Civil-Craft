using UnityEngine;
using UnityEngine.AI;

public class PlayerSpawnManager : MonoBehaviour
{
    // A static string survives scene loads! It remembers our target door.
    public static string targetSpawnPointName = "";

    [Header("Saved Spawn Safety")]
    [Tooltip("Reject a saved position that is not close to the scene's walkable navigation surface. This prevents old saves from reopening inside canyon collision.")]
    [SerializeField] private bool validateSavedSpawnAgainstNavMesh = true;
    [SerializeField, Min(0.25f)] private float savedSpawnNavMeshRadius = 2f;
    [SerializeField, Min(0.25f)] private float savedSpawnVerticalTolerance = 1.5f;

    private LevelResetManager levelResetManager;

    private void Start()
    {
        CharacterController cc = GetComponent<CharacterController>();
        levelResetManager = FindObjectOfType<LevelResetManager>(true);

        // SCENARIO 1: We just walked through a specific door
        if (!string.IsNullOrEmpty(targetSpawnPointName))
        {
            GameObject spawnPoint = GameObject.Find(targetSpawnPointName);

            if (spawnPoint != null)
            {
                if (cc != null) cc.enabled = false;

                transform.position = spawnPoint.transform.position;
                transform.rotation = spawnPoint.transform.rotation;

                if (cc != null) cc.enabled = true;
                
                targetSpawnPointName = ""; 
            }
            else
            {
                Debug.LogWarning("Could not find a spawn point named: " + targetSpawnPointName);
            }
        }
        // SCENARIO 2: We are loading the game, let's check if we have a saved position here!
        else if (gameObject.scene.name != "Multiplayer" &&
                 PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData != null)
        {
            // Only teleport to the saved position if the saved scene perfectly matches the current scene
            if (PlayerDataManager.Instance.CurrentData.lastSavedScene == gameObject.scene.name)
            {
                if (PlayerDataManager.Instance.CurrentData.lastSavedPosition != null)
                {
                    Vector3 savedPosition =
                        PlayerDataManager.Instance.CurrentData.lastSavedPosition.ToVector3();
                    if (!IsSavedSpawnSafe(savedPosition))
                    {
                        Debug.LogWarning(
                            $"Saved player position {savedPosition} is not on accessible ground. " +
                            "Using the authored scene spawn instead.",
                            this);
                        return;
                    }

                    if (cc != null) cc.enabled = false;

                    transform.position = savedPosition;

                    if (cc != null) cc.enabled = true;
                }
            }
        }
    }

    // --- NEW: Automatically save position when the scene ends (like hitting "Restart" or walking through a door) ---
    private void OnDestroy()
    {
        SaveSafestKnownPosition();
    }

    // --- NEW: Automatically save position if the player ALT+F4s or closes the app on their phone ---
    private void OnApplicationQuit()
    {
        SaveSafestKnownPosition();
    }

    private bool IsSavedSpawnSafe(Vector3 savedPosition)
    {
        if (!validateSavedSpawnAgainstNavMesh)
            return true;

        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            return true;

        if (!NavMesh.SamplePosition(
                savedPosition,
                out NavMeshHit hit,
                Mathf.Max(0.25f, savedSpawnNavMeshRadius),
                NavMesh.AllAreas))
        {
            return false;
        }

        return Mathf.Abs(hit.position.y - savedPosition.y) <=
               Mathf.Max(0.25f, savedSpawnVerticalTolerance);
    }

    private void SaveSafestKnownPosition()
    {
        // This shared Player also exists in Multiplayer. Its position must not
        // replace the separate Story resume location when that scene closes.
        string sceneName = gameObject.scene.name;
        if (sceneName == "Multiplayer") return;
        if (PlayerDataManager.Instance == null) return;

        Vector3 positionToSave = transform.position;
        if (levelResetManager == null)
            levelResetManager = FindObjectOfType<LevelResetManager>(true);
        if (levelResetManager != null &&
            levelResetManager.TryGetLastSafePlayerPosition(out Vector3 safePosition))
        {
            positionToSave = safePosition;
        }

        PlayerDataManager.Instance.SavePlayerPosition(
            sceneName,
            positionToSave);
    }
}
