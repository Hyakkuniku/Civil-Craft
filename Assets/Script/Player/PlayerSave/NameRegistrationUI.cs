using UnityEngine;
using TMPro;
using UnityEngine.Events;
using System.Collections.Generic; // --- REQUIRED FOR LISTS ---

public class NameRegistrationUI : MonoBehaviour
{
    [Header("Input UI References")]
    [Tooltip("The parent panel containing the input field and submit button.")]
    public GameObject nameInputPanel;
    [Tooltip("The TextMeshPro Input Field where the player types.")]
    public TMP_InputField nameInputField;

    [Header("Confirmation UI References")]
    [Tooltip("The parent panel containing the Yes/No confirmation.")]
    public GameObject confirmationPanel;
    [Tooltip("The text that asks 'Are you sure your name is X?'")]
    public TextMeshProUGUI confirmationText;

    // --- THE FIX: List of Canvases to turn off ---
    [Header("UI to Hide")]
    [Tooltip("Drag the HUD or other Canvases here to hide them while typing.")]
    public List<GameObject> canvasesToHide = new List<GameObject>();
    private List<GameObject> temporarilyHiddenCanvases = new List<GameObject>();

    [Header("Events")]
    [Tooltip("What happens AFTER they confirm their name? (e.g., Trigger the next dialogue!)")]
    public UnityEvent onNameConfirmed;

    private string pendingName = "";

    private void Start()
    {
        if (nameInputPanel != null) nameInputPanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
    }

    // Call this from the DialogueTrigger's new event!
    public void ShowNamePrompt()
    {
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        if (nameInputPanel != null) nameInputPanel.SetActive(true);
        if (nameInputField != null) nameInputField.text = "";

        // --- THE FIX: Hide other UI elements ---
        temporarilyHiddenCanvases.Clear();
        foreach (GameObject canvasObj in canvasesToHide)
        {
            if (canvasObj != null && canvasObj.activeSelf)
            {
                temporarilyHiddenCanvases.Add(canvasObj);
                canvasObj.SetActive(false);
            }
        }

        // Freeze the player while they type
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        { 
            inputObj.SetPlayerInputEnable(false); 
            inputObj.SetLookEnabled(false); 
        }
    }

    // Link this to your Input Panel's "Submit" button!
    public void SubmitName()
    {
        pendingName = nameInputField.text.Trim();
        
        // Fallback just in case they leave it completely blank
        if (string.IsNullOrEmpty(pendingName)) 
        {
            pendingName = "Engineer"; 
        }

        // Hide the input, show the confirmation
        if (nameInputPanel != null) nameInputPanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(true);

        // Update the text to show what they typed
        if (confirmationText != null)
        {
            confirmationText.text = $"Are you sure your name is {pendingName}?";
        }
    }

    // Link this to your Confirmation Panel's "Yes" button!
    public void ConfirmNameYes()
    {
        // Save it permanently!
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.CurrentData.playerName = pendingName;
            PlayerDataManager.Instance.SaveGame();
        }

        if (confirmationPanel != null) confirmationPanel.SetActive(false);

        // --- THE FIX: Restore the hidden UI elements ---
        foreach (GameObject canvasObj in temporarilyHiddenCanvases)
        {
            if (canvasObj != null) canvasObj.SetActive(true);
        }
        temporarilyHiddenCanvases.Clear();

        // Unfreeze the player
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        { 
            inputObj.SetPlayerInputEnable(true); 
            inputObj.SetLookEnabled(true); 
        }

        // Fire the next event (like a follow up dialogue or tutorial step)
        onNameConfirmed?.Invoke();
    }

    // Link this to your Confirmation Panel's "No" button!
    public void ConfirmNameNo()
    {
        // Go back to the input panel
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        if (nameInputPanel != null) nameInputPanel.SetActive(true);
    }
}