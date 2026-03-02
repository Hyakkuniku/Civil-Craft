using UnityEngine;
using TMPro;
using UnityEngine.UI; // Needed for the Button

public class ObjectiveTrackerUI : MonoBehaviour
{
    public static ObjectiveTrackerUI Instance { get; private set; }

    [Header("UI References")]
    public GameObject trackerPanel; 
    
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI budgetText;
    
    // ADDED: Text for the cargo weight and the Complete button
    public TextMeshProUGUI weightText; 
    public GameObject completeButton; 

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        
        if (trackerPanel != null) trackerPanel.SetActive(false);
    }

    public void SetObjective(ContractSO contract)
    {
        if (contract == null) return;

        if (trackerPanel != null) trackerPanel.SetActive(true);
        if (completeButton != null) completeButton.SetActive(false); // Hide button initially

        if (titleText != null) titleText.text = "Client: " + contract.clientName;
        if (descriptionText != null) descriptionText.text = contract.jobDescription;
        if (budgetText != null) budgetText.text = "Budget: $" + contract.budget;
        
        // ADDED: Show the weight
        if (weightText != null) weightText.text = "Cargo Weight: " + contract.cargoWeight + "kg";
    }

    // ADDED: Called by the Delivery Zone when the cargo arrives
    public void ShowCompleteButton()
    {
        if (completeButton != null) completeButton.SetActive(true);
    }

    // ADDED: Link this to the UI Button's OnClick event in the inspector!
    public void OnCompleteButtonClicked()
    {
        Debug.Log("<color=green>Contract Completed!</color> Ready for the next one.");
        ClearObjective();
        
        // Optional: Add logic here to load the next level, grant money, or unlock the next NPC!
    }

    public void ClearObjective()
    {
        if (trackerPanel != null) trackerPanel.SetActive(false);
    }
}