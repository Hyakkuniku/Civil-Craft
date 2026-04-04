using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class DoorKnockTrigger : MonoBehaviour
{
    [Header("Door Settings")]
    [Tooltip("A unique name for this door so the save file remembers it (e.g., 'MainHouseDoor')")]
    public string uniqueDoorID = "MainHouseDoor";

    [Header("Dialogue Settings")]
    public Dialogue dialogue;

    [Header("Knock Settings")]
    [Tooltip("How many seconds to wait after clicking before the dialogue appears.")]
    public float delayAfterKnock = 1.0f;

    [Header("Door Enter Event")]
    [Tooltip("What happens when the player actually enters the door? (Put your DoorTransition.EnterDoor here!)")]
    public UnityEvent onEnterDoor;

    private DialogueManager dialogueManager;
    private EventOnlyInteractable interactable;
    private bool isKnocking = false;
    private bool hasKnocked = false; 

    private void Awake()
    {
        dialogueManager = FindObjectOfType<DialogueManager>();
        interactable = GetComponent<EventOnlyInteractable>();
    }

    private void Start()
    {
        // 1. When the scene loads, check our save data!
        if (PlayerDataManager.Instance != null && !string.IsNullOrEmpty(uniqueDoorID))
        {
            if (PlayerDataManager.Instance.IsDoorUnlocked(uniqueDoorID))
            {
                // If we already unlocked it, immediately set it to "Enter Door"
                hasKnocked = true;
                if (interactable != null) interactable.promptMessage = "Enter Door";
            }
            else
            {
                // Otherwise, ensure it says Knock
                if (interactable != null) interactable.promptMessage = "Knock on Door";
            }
        }
    }

    public void OnDoorClicked()
    {
        if (hasKnocked)
        {
            onEnterDoor?.Invoke();
            return;
        }

        if (!isKnocking)
        {
            StartCoroutine(KnockRoutine());
        }
    }

    private IEnumerator KnockRoutine()
    {
        isKnocking = true;
        Debug.Log("*Knock Knock Knock*");

        yield return new WaitForSeconds(delayAfterKnock);

        if (dialogueManager != null)
        {
            dialogueManager.StartDialogue(dialogue, () => 
            {
                isKnocking = false;
                hasKnocked = true; 
                
                // --- THE FIX: Save the door as unlocked forever! ---
                if (PlayerDataManager.Instance != null && !string.IsNullOrEmpty(uniqueDoorID))
                {
                    PlayerDataManager.Instance.UnlockDoor(uniqueDoorID);
                }

                if (interactable != null)
                {
                    interactable.promptMessage = "Enter Door";
                }
            });
        }
        else
        {
            hasKnocked = true;
            if (interactable != null) interactable.promptMessage = "Enter Door";
            isKnocking = false;
        }
    }
}