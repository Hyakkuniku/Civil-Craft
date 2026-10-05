using UnityEngine;

public class DoorTransition : MonoBehaviour
{
    [Header("Destination Settings")]
    [Tooltip("The exact name of the Scene you want to load")]
    public string sceneToLoad;
    
    [Tooltip("The exact name of the Spawn Point object in the NEXT scene")]
    public string spawnPointNameInNextScene;

    [Header("Arrival Progress (optional)")]
    [Tooltip("Lesson saved only after the player successfully reaches this door's destination spawn point.")]
    public string arrivalLessonId;
    [Tooltip("Interaction prompt used after that arrival has been saved. Empty preserves the authored prompt.")]
    public string completedPromptMessage;
    [Tooltip("Optional later lesson that proves players with older saves already reached the destination.")]
    public string legacyCompletedLessonId;

    [Header("Tutorial Lock")]
    [Tooltip("If checked, the player cannot use this door until UnlockDoor() is called.")]
    public bool isLocked = false; 

    [Tooltip("Optional: The exact name of the lesson that permanently unlocks this door once completed.")]
    public string permanentlyUnlockAfterLesson = "HouseTutorial";

    // --- NEW: Reference to the Interactable component ---
    private Interactable myInteractable;
    private string originalPromptMessage;
    private static string pendingArrivalScene;
    private static string pendingArrivalSpawnPoint;
    private static string pendingArrivalLessonId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        ClearPendingArrival();
    }

    private void Awake()
    {
        // Grab the Interactable component on this door
        myInteractable = GetComponent<Interactable>();
        if (myInteractable != null) originalPromptMessage = myInteractable.promptMessage;
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

        RefreshPrompt();
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

        if (LoadingScreenManager.IsLoading) return;

        // A new route replaces any unconsumed arrival; failed loads cannot
        // leave an old quest marker waiting to complete in a later scene.
        ClearPendingArrival();
        PlayerSpawnManager.targetSpawnPointName = "";
        SceneController sceneController = FindObjectOfType<SceneController>();
        if (sceneController != null)
        {
            PlayerSpawnManager.targetSpawnPointName = spawnPointNameInNextScene;
            sceneController.LoadScene(sceneToLoad);
            if (LoadingScreenManager.IsLoading)
                QueuePendingArrival(sceneToLoad, spawnPointNameInNextScene, arrivalLessonId);
            else
                PlayerSpawnManager.targetSpawnPointName = "";
        }
        else
        {
            Debug.LogError("No SceneController found in this scene! Please add one.");
        }
    }

    public void RefreshPrompt()
    {
        if (myInteractable == null) return;
        PlayerData data = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.CurrentData : null;
        myInteractable.promptMessage = ResolvePromptMessage(
            data, originalPromptMessage, arrivalLessonId,
            completedPromptMessage, legacyCompletedLessonId);
    }

    public static string ResolvePromptMessage(
        PlayerData data, string originalPromptMessage, string arrivalLessonId,
        string completedPromptMessage, string legacyCompletedLessonId)
    {
        if (string.IsNullOrWhiteSpace(arrivalLessonId) ||
            string.IsNullOrWhiteSpace(completedPromptMessage) || data?.completedLessons == null)
            return originalPromptMessage;

        bool arrived = data.completedLessons.Contains(arrivalLessonId.Trim()) ||
            (!string.IsNullOrWhiteSpace(legacyCompletedLessonId) &&
             data.completedLessons.Contains(legacyCompletedLessonId.Trim()));
        return arrived ? completedPromptMessage : originalPromptMessage;
    }

    public static void QueuePendingArrival(
        string destinationScene, string destinationSpawnPoint, string lessonId)
    {
        ClearPendingArrival();
        if (string.IsNullOrWhiteSpace(destinationScene) ||
            string.IsNullOrWhiteSpace(destinationSpawnPoint) || string.IsNullOrWhiteSpace(lessonId))
            return;

        pendingArrivalScene = destinationScene;
        pendingArrivalSpawnPoint = destinationSpawnPoint;
        pendingArrivalLessonId = lessonId.Trim();
    }

    public static bool TryConsumePendingArrival(
        string arrivedScene, string arrivedSpawnPoint, out string lessonId)
    {
        bool matches = !string.IsNullOrEmpty(pendingArrivalLessonId) &&
            string.Equals(pendingArrivalScene, arrivedScene, System.StringComparison.Ordinal) &&
            string.Equals(pendingArrivalSpawnPoint, arrivedSpawnPoint, System.StringComparison.Ordinal);
        lessonId = matches ? pendingArrivalLessonId : null;
        ClearPendingArrival();
        return matches;
    }

    public static void ClearPendingArrival()
    {
        pendingArrivalScene = null;
        pendingArrivalSpawnPoint = null;
        pendingArrivalLessonId = null;
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
