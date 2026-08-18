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

    // --- NEW: Reference to the Interactable component ---
    private Interactable myInteractable;

    private void Awake()
    {
        // Grab the Interactable component on this door
        myInteractable = GetComponent<Interactable>();
    }

    private void Start()
    {
        // Check the save file when the scene loads
        if (isLocked && !string.IsNullOrEmpty(permanentlyUnlockAfterLesson) && PlayerDataManager.Instance != null)
        {
            if (PlayerDataManager.Instance.CurrentData.completedLessons.Contains(permanentlyUnlockAfterLesson))
            {
                isLocked = false; 
            }
        }

        // --- THE FIX: Hide the button if the door is locked! ---
        if (myInteractable != null)
        {
            myInteractable.enabled = !isLocked; 
        }
    }

    public void EnterDoor()
    {
        // Failsafe: Block the transition if the door is still somehow locked
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
        
        // --- THE FIX: Turn the interaction button back on! ---
        if (myInteractable != null)
        {
            myInteractable.enabled = true;
        }
        
        Debug.Log("Door Unlocked! The button will now appear.");
    }
}