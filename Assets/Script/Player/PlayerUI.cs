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

    private Dictionary<Interactable, GameObject> activeButtons = new Dictionary<Interactable, GameObject>();
    
    // --- OPTIMIZATION: Pre-allocate this list so we don't generate garbage 10x a second! ---
    private List<Interactable> toRemove = new List<Interactable>();

    public void UpdateButtons(List<Interactable> nearbyInteractables)
    {
        foreach (Interactable interactable in nearbyInteractables)
        {
            // --- THE FIX: If the component is turned off (locked door), ignore it completely! ---
            if (!interactable.isActiveAndEnabled) continue;

            if (!activeButtons.ContainsKey(interactable))
            {
                CreateButton(interactable);
            }
            else
            {
                GameObject existingButton = activeButtons[interactable];
                TextMeshProUGUI buttonText = existingButton.GetComponentInChildren<TextMeshProUGUI>();
                
                if (buttonText != null && buttonText.text != interactable.promptMessage)
                {
                    buttonText.text = interactable.promptMessage;
                }
            }
        }

        // --- OPTIMIZATION: Clear and reuse the existing memory bucket! ---
        toRemove.Clear();
        foreach (var kvp in activeButtons)
        {
            // --- THE FIX: Destroy the button if the player walks away OR if the script gets turned off! ---
            if (!nearbyInteractables.Contains(kvp.Key) || !kvp.Key.isActiveAndEnabled)
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
        GameObject newButton = Instantiate(interactButtonPrefab, buttonContainer);
        
        TextMeshProUGUI buttonText = newButton.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null)
        {
            buttonText.text = interactable.promptMessage;
        }

        Button btn = newButton.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(() => interactable.BaseInteract());
        }

        activeButtons.Add(interactable, newButton);
    }
}