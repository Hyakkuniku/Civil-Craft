using UnityEngine;

public class DoorTransition : MonoBehaviour
{
    [Header("Destination Settings")]
    [Tooltip("The exact name of the Scene you want to load")]
    public string sceneToLoad;
    
    [Tooltip("The exact name of the Spawn Point object in the NEXT scene")]
    public string spawnPointNameInNextScene;

    // We will link this to your InteractionEvent in the Inspector!
    public void EnterDoor()
    {
        // 1. Save the destination
        PlayerSpawnManager.targetSpawnPointName = spawnPointNameInNextScene;

        // 2. Find YOUR SceneController and tell it to load the scene
        SceneController sceneController = FindObjectOfType<SceneController>();
        if (sceneController != null)
        {
            sceneController.LoadScene(sceneToLoad);
        }
        else
        {
            Debug.LogError("No SceneController found in this scene! Please add one.");
        }
    }
}