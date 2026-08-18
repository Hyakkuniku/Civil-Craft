using UnityEngine;
using UnityEngine.SceneManagement; // Required to check the current scene name!

public class PlayerSpawnManager : MonoBehaviour
{
    // A static string survives scene loads! It remembers our target door.
    public static string targetSpawnPointName = "";

    private void Start()
    {
        CharacterController cc = GetComponent<CharacterController>();

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
        else if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData != null)
        {
            // Only teleport to the saved position if the saved scene perfectly matches the current scene
            if (PlayerDataManager.Instance.CurrentData.lastSavedScene == SceneManager.GetActiveScene().name)
            {
                if (PlayerDataManager.Instance.CurrentData.lastSavedPosition != null)
                {
                    if (cc != null) cc.enabled = false;

                    transform.position = PlayerDataManager.Instance.CurrentData.lastSavedPosition.ToVector3();

                    if (cc != null) cc.enabled = true;
                }
            }
        }
    }

    // --- NEW: Automatically save position when the scene ends (like hitting "Restart" or walking through a door) ---
    private void OnDestroy()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.SavePlayerPosition(SceneManager.GetActiveScene().name, transform.position);
        }
    }

    // --- NEW: Automatically save position if the player ALT+F4s or closes the app on their phone ---
    private void OnApplicationQuit()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.SavePlayerPosition(SceneManager.GetActiveScene().name, transform.position);
        }
    }
}