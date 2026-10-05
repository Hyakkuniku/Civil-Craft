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
    [Tooltip("Optional authored name highlight; older panels retain their original confirmation text.")]
    public TMP_Text confirmationNameText;

    // --- THE FIX: List of Canvases to turn off ---
    [Header("UI to Hide")]
    [Tooltip("Drag the HUD or other Canvases here to hide them while typing.")]
    public List<GameObject> canvasesToHide = new List<GameObject>();
    private List<GameObject> temporarilyHiddenCanvases = new List<GameObject>();

    [Header("Events")]
    [Tooltip("What happens AFTER they confirm their name? (e.g., Trigger the next dialogue!)")]
    public UnityEvent onNameConfirmed;

    private string pendingName = "";
    private bool nameAwaitingConfirmation;
    private TMP_Text nameFeedback;
    private string normalHint;
    private Color normalHintColor;
    private bool normalHintRichText;

    private void Start()
    {
        if (nameInputPanel != null) nameInputPanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
    }

    // Call this from the DialogueTrigger's new event!
    public void ShowNamePrompt()
    {
        pendingName = string.Empty;
        nameAwaitingConfirmation = false;
        SetNameFeedback(null);
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
        if (nameInputField == null) return;
        string proposedName = nameInputField.text.Trim();
        
        // Fallback just in case they leave it completely blank
        if (string.IsNullOrEmpty(proposedName))
        {
            proposedName = "Engineer";
        }

        if (!PlayerNamePolicy.TryValidate(proposedName, out string error))
        {
            RejectName(error);
            return;
        }
        pendingName = proposedName;
        nameAwaitingConfirmation = true;
        SetNameFeedback(null);

        // Hide the input, show the confirmation
        if (nameInputPanel != null) nameInputPanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(true);

        // Update the text to show what they typed
        if (confirmationText != null)
        {
            confirmationText.text = confirmationNameText != null
                ? "This is how Bhan and other engineers will know you."
                : $"Are you sure your name is {pendingName}?";
        }
        // Display the name literally: typed rich-text tags are not UI markup.
        if (confirmationNameText != null)
        { confirmationNameText.richText = false; confirmationNameText.text = pendingName; }
    }

    // Link this to your Confirmation Panel's "Yes" button!
    public void ConfirmNameYes()
    {
        if (!PlayerNamePolicy.TryValidate(pendingName, out string error))
        {
            RejectName(error);
            return;
        }
        if (!nameAwaitingConfirmation)
        {
            RejectName("Please submit your name before confirming it.");
            return;
        }
        nameAwaitingConfirmation = false;
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
        nameAwaitingConfirmation = false;
        // Go back to the input panel
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        if (nameInputPanel != null) nameInputPanel.SetActive(true);
    }

    private void RejectName(string error)
    {
        pendingName = string.Empty;
        nameAwaitingConfirmation = false;
        if (confirmationPanel != null) confirmationPanel.SetActive(false);
        if (nameInputPanel != null) nameInputPanel.SetActive(true);
        SetNameFeedback(error);
    }

    private void SetNameFeedback(string error)
    {
        // Reuse the scene-authored hint; never create a runtime error panel.
        if (nameFeedback == null && nameInputPanel != null)
            foreach (TMP_Text label in nameInputPanel.GetComponentsInChildren<TMP_Text>(true))
                if (label.name == "Registration Name Hint")
                {
                    nameFeedback = label;
                    normalHint = label.text;
                    normalHintColor = label.color;
                    normalHintRichText = label.richText;
                    break;
                }
        if (nameFeedback == null) return;
        nameFeedback.text = error ?? normalHint;
        nameFeedback.color = error == null ? normalHintColor : new Color32(164, 54, 37, 255);
        nameFeedback.richText = error == null && normalHintRichText;
    }
}
