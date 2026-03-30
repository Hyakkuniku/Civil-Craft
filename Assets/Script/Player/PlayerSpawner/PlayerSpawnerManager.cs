using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    // A static string survives scene loads! It remembers our target door.
    public static string targetSpawnPointName = "";

    private void Start()
    {
        // If we don't have a specific target, just spawn normally
        if (string.IsNullOrEmpty(targetSpawnPointName)) return;

        // Try to find the specific spawn point in the new scene
        GameObject spawnPoint = GameObject.Find(targetSpawnPointName);

        if (spawnPoint != null)
        {
            // --- IMPORTANT TELEPORTATION FIX ---
            // If your player uses a CharacterController or Rigidbody, you often have to 
            // turn it off for one frame to teleport them, otherwise physics pushes them back!
            
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            // Move the player to the spawn point
            transform.position = spawnPoint.transform.position;
            transform.rotation = spawnPoint.transform.rotation;

            if (cc != null) cc.enabled = true;
            
            // Clear the variable so we don't accidentally spawn here next time
            targetSpawnPointName = ""; 
        }
        else
        {
            Debug.LogWarning("Could not find a spawn point named: " + targetSpawnPointName);
        }
    }
}