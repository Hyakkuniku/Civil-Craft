using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections.Generic;

public class ContractAlmanacTab : MonoBehaviour
{
    [Header("Master Contract List")]
    [Tooltip("Drag EVERY ContractSO in your game into this list!")]
    public List<ContractSO> allGameContracts;

    [Header("Left Page (The Stack)")]
    public Transform buttonContainer; 
    [Tooltip("A UI Button Prefab with a TextMeshProUGUI child.")]
    public GameObject contractButtonPrefab;

    [Header("Right Page 1 (Details)")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI clientText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI rewardsText;

    [Header("Right Page 2 (Snapshot)")]
    [Tooltip("Use a RawImage for this, NOT a regular Image component!")]
    public RawImage snapshotImage;
    public TextMeshProUGUI snapshotCaptionText;

    private void OnEnable()
    {
        RefreshContractList();
    }

    public void RefreshContractList()
    {
        // Clear the old list
        foreach (Transform child in buttonContainer) 
        {
            Destroy(child.gameObject);
        }

        if (PlayerDataManager.Instance == null) return;

        List<string> completed = PlayerDataManager.Instance.CurrentData.completedContracts;

        // Clear details if nothing is completed yet
        if (completed.Count == 0)
        {
            ClearDetails();
            return;
        }

        bool selectedFirst = false;

        // Create buttons for every contract the player has finished
        foreach (ContractSO contract in allGameContracts)
        {
            if (completed.Contains(contract.name))
            {
                GameObject btnObj = Instantiate(contractButtonPrefab, buttonContainer);
                TextMeshProUGUI btnText = btnObj.GetComponentInChildren<TextMeshProUGUI>();
                
                if (btnText != null) btnText.text = contract.clientName + " - " + contract.name;

                Button btn = btnObj.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => DisplayContract(contract));
                }

                // Automatically display the first one in the list
                if (!selectedFirst)
                {
                    DisplayContract(contract);
                    selectedFirst = true;
                }
            }
        }
    }

    public void DisplayContract(ContractSO contract)
    {
        // 1. Fill out Page 1 (Details)
        if (titleText != null) titleText.text = "Job: " + contract.name;
        if (clientText != null) clientText.text = "Client: " + contract.clientName;
        if (descriptionText != null) descriptionText.text = contract.jobDescription;
        if (rewardsText != null) rewardsText.text = "Paid: " + contract.goldReward + "G | " + contract.expReward + "XP";

        // 2. Fill out Page 2 (Snapshot)
        if (snapshotImage != null)
        {
            string photoPath = Application.persistentDataPath + "/" + contract.name + "_photo.png";
            
            if (File.Exists(photoPath))
            {
                // Read the PNG from the hard drive and apply it to the RawImage
                byte[] bytes = File.ReadAllBytes(photoPath);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                
                snapshotImage.texture = tex;
                snapshotImage.color = Color.white;
                
                if (snapshotCaptionText != null) snapshotCaptionText.text = contract.clientName + "'s Bridge";
            }
            else
            {
                // Fallback if the photo got deleted or lost
                snapshotImage.texture = null;
                snapshotImage.color = Color.black; 
                if (snapshotCaptionText != null) snapshotCaptionText.text = "Photo Missing";
            }
        }
    }

    private void ClearDetails()
    {
        if (titleText != null) titleText.text = "No Contracts Completed";
        if (clientText != null) clientText.text = "";
        if (descriptionText != null) descriptionText.text = "Complete jobs to unlock history.";
        if (rewardsText != null) rewardsText.text = "";
        if (snapshotImage != null) snapshotImage.texture = null;
        if (snapshotCaptionText != null) snapshotCaptionText.text = "";
    }
}