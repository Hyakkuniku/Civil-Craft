using UnityEngine;
using TMPro;

public class AlmanacPlayerStats : MonoBehaviour
{
    [Header("UI Text Fields")]
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI goldText;
    public TextMeshProUGUI expText;
    public TextMeshProUGUI bridgesBuiltText;

    [Header("New Lifetime Stats (Optional)")]
    public TextMeshProUGUI contractsCompletedText; 
    public TextMeshProUGUI totalGoldEarnedText;

    private void OnEnable()
    {
        // Refresh the UI every time they flip to this page
        RefreshStats();
    }

    private void RefreshStats()
    {
        // Safety check: Ensure the manager exists
        if (PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null) 
            return;

        // Grab the live data
        PlayerData data = PlayerDataManager.Instance.CurrentData;

        // Update the UI texts
        if (playerNameText != null) playerNameText.text = "Engineer: " + data.playerName;
        if (titleText != null) titleText.text = "Rank: " + data.GetTitle();
        if (goldText != null) goldText.text = "Gold: " + data.gold.ToString("N0"); // "N0" adds commas (e.g. 1,000)
        if (expText != null) expText.text = "EXP: " + data.exp.ToString("N0");
        
        // --- THE FIX: Using the new 'lifetimeBridgesBuilt' variable! ---
        if (bridgesBuiltText != null) bridgesBuiltText.text = "Bridges Built: " + data.lifetimeBridgesBuilt;

        // Populate the new optional lifetime stats
        if (contractsCompletedText != null) contractsCompletedText.text = "Contracts Done: " + data.lifetimeContractsCompleted;
        if (totalGoldEarnedText != null) totalGoldEarnedText.text = "Lifetime Earnings: " + data.lifetimeGoldEarned.ToString("N0");
    }
}