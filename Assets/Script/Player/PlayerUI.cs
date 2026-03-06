using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerUI : MonoBehaviour
{
    [Header("Button Settings")]
    [Tooltip("The UI Panel that has a Vertical Layout Group component")]
    [SerializeField] private Transform buttonContainer; 
    [Tooltip("A Prefab of a UI Button that has TextMeshPro inside it")]
    [SerializeField] private GameObject interactButtonPrefab; 

    // Keeps track of which object belongs to which button
    private Dictionary<Interactable, GameObject> activeButtons = new Dictionary<Interactable, GameObject>();

    public void UpdateButtons(List<Interactable> nearbyInteractables)
    {
        // 1. Create or UPDATE buttons for interactables we are near
        foreach (Interactable interactable in nearbyInteractables)
        {
            if (!activeButtons.ContainsKey(interactable))
            {
                // If the button doesn't exist yet, make a new one
                CreateButton(interactable);
            }
            else
            {
                // NEW: If the button already exists, check if its text needs to be updated!
                GameObject existingButton = activeButtons[interactable];
                TextMeshProUGUI buttonText = existingButton.GetComponentInChildren<TextMeshProUGUI>();
                
                // Only update it if the text has actually changed (saves performance)
                if (buttonText != null && buttonText.text != interactable.promptMessage)
                {
                    buttonText.text = interactable.promptMessage;
                }
            }
        }

        // 2. Find and destroy buttons for interactables we walked away from
        List<Interactable> toRemove = new List<Interactable>();
        foreach (var kvp in activeButtons)
        {
            if (!nearbyInteractables.Contains(kvp.Key))
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (Interactable interactable in toRemove)
        {
            Destroy(activeButtons[interactable]);
            activeButtons.Remove(interactable);
        }
    }

    private void CreateButton(Interactable interactable)
    {
        // Spawn the button inside the layout group container
        GameObject newButton = Instantiate(interactButtonPrefab, buttonContainer);
        
        // Update the button's text to match the promptMessage
        TextMeshProUGUI buttonText = newButton.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null)
        {
            buttonText.text = interactable.promptMessage;
        }

        // Hook up the button's click event to trigger the object's interaction
        Button btn = newButton.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(() => interactable.BaseInteract());
        }

        // Add it to our dictionary so we can keep track of it
        activeButtons.Add(interactable, newButton);
    }
}