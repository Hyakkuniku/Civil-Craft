using UnityEngine;
using TMPro;

public class ObjectiveTrackerUI : MonoBehaviour
{
    public static ObjectiveTrackerUI Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("The parent object holding the tracker UI. This gets turned on when a contract is active.")]
    public GameObject trackerPanel; 
    
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI budgetText;

    private void Awake()
    {
        if (Instance == null) 
        {
            Instance = this;
        }
        else 
        {
            Destroy(gameObject);
            return;
        }
        
        if (trackerPanel != null) 
        {
            trackerPanel.SetActive(false);
        }
    }

    public void SetObjective(ContractSO contract)
    {
        if (contract == null) return;

        if (trackerPanel != null) trackerPanel.SetActive(true);

        if (titleText != null) titleText.text = "Client: " + contract.clientName;
        if (descriptionText != null) descriptionText.text = contract.jobDescription;
        if (budgetText != null) budgetText.text = "Budget: $" + contract.budget;
    }

    public void ClearObjective()
    {
        if (trackerPanel != null) trackerPanel.SetActive(false);
    }
}