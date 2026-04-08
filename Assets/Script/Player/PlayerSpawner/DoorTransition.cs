using UnityEngine;

public class DoorTransition : MonoBehaviour
{
    [Header("Destination Settings")]
    [Tooltip("The exact name of the Scene you want to load")]
    public string sceneToLoad;
    
    [Tooltip("The exact name of the Spawn Point object in the NEXT scene")]
    public string spawnPointNameInNextScene;

    [Header("Tutorial Lock")]
    [Tooltip("If checked, the player cannot use this door until UnlockDoor() is called.")]
    public bool isLocked = false; 

    [Tooltip("Optional: The exact name of the lesson that permanently unlocks this door once completed.")]
    public string permanentlyUnlockAfterLesson = "HouseTutorial";

    private void Start()
    {
        // --- THE FIX: Check the save file when the scene loads! ---
        // If the door is supposed to be locked, but we already beat the tutorial, unlock it forever!
        if (isLocked && !string.IsNullOrEmpty(permanentlyUnlockAfterLesson) && PlayerDataManager.Instance != null)
        {
            if (PlayerDataManager.Instance.CurrentData.completedLessons.Contains(permanentlyUnlockAfterLesson))
            {
                isLocked = false; 
            }
        }
    }

    // We will link this to your InteractionEvent in the Inspector!
    public void EnterDoor()
    {
        // Block the transition if the door is locked!
        if (isLocked)
        {
            Debug.Log("The door is locked. I should finish what I'm doing first!");
            
            if (BuildUIController.Instance != null) 
            {
                BuildUIController.Instance.LogAction("I should talk to the NPC first.");
            }
            return;
        }

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

    // A public method we can call from our Tutorial Events!
    public void UnlockDoor()
    {
        isLocked = false;
        Debug.Log("Door Unlocked!");
    }
}