using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Client_NPC : Interactable
{   

    // Reference to the DialogueTrigger component (we'll get it automatically)
    private DialogueTrigger dialogueTrigger;

    void Awake()
    {
        // Get the DialogueTrigger from the same GameObject
        dialogueTrigger = GetComponent<DialogueTrigger>();
        
        // Optional safety check
        if (dialogueTrigger == null)
        {
            Debug.LogWarning($"No DialogueTrigger found on {gameObject.name}!", this);
        }
    }

    protected override void Intract()
    {
        // This is the important line
        if (dialogueTrigger != null)
        {
            dialogueTrigger.TriggerDialogue();
        }
        else
        {
            Debug.LogWarning("Cannot start dialogue — DialogueTrigger is missing!", this);
        }
    }
}