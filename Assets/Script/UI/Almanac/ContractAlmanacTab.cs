using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections.Generic;

public class ContractAlmanacTab : MonoBehaviour
{
    [Header("Master Contract List")]
    public List<ContractSO> allGameContracts;

    [Header("Left Page (Photo)")]
    public RawImage snapshotImage;
    public TextMeshProUGUI snapshotCaptionText;

    [Header("Right Page (Details)")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI clientText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI rewardsText;

    [Header("Pagination")]
    public TextMeshProUGUI pageCounterText;

    private List<ContractSO> completedContractsList = new List<ContractSO>();
    private int currentIndex = 0;

    private void OnEnable()
    {
        // Listen to the Almanac Manager!
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.OnCategoryChanged += CheckIfActiveTab;
        }
        
        // Run a manual check just in case we were opened directly
        CheckIfActiveTab(0); 
    }

    private void OnDisable()
    {
        // Stop listening and release the buttons if we get turned off
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.OnCategoryChanged -= CheckIfActiveTab;
            AlmanacManager.Instance.DisableVirtualPagination(HandleVirtualPageTurn);
        }
    }

    private void CheckIfActiveTab(int categoryIndex)
    {
        // THE FIX: If our Title Text is physically visible on the screen, that means we are the active tab!
        if (titleText != null && titleText.gameObject.activeInHierarchy)
        {
            if (AlmanacManager.Instance != null)
            {
                AlmanacManager.Instance.EnableVirtualPagination(HandleVirtualPageTurn);
            }
            RefreshContractList();
        }
    }

    private void HandleVirtualPageTurn(bool goingForward)
    {
        if (goingForward) ShowNextContract();
        else ShowPreviousContract();
    }

    public void RefreshContractList()
    {
        if (PlayerDataManager.Instance == null) return;

        completedContractsList.Clear();
        List<string> completedNames = PlayerDataManager.Instance.CurrentData.completedContracts;

        foreach (ContractSO contract in allGameContracts)
        {
            if (completedNames.Contains(contract.name))
            {
                completedContractsList.Add(contract);
            }
        }

        if (completedContractsList.Count == 0)
        {
            ClearDetails();
        }
        else
        {
            currentIndex = 0;
            DisplayContract(currentIndex);
        }
    }

    public void ShowNextContract()
    {
        if (currentIndex < completedContractsList.Count - 1)
        {
            currentIndex++;
            DisplayContract(currentIndex);
        }
    }

    public void ShowPreviousContract()
    {
        if (currentIndex > 0)
        {
            currentIndex--;
            DisplayContract(currentIndex);
        }
    }

    private void DisplayContract(int index)
    {
        ContractSO contract = completedContractsList[index];

        if (titleText != null) titleText.text = "Job: " + contract.name;
        if (clientText != null) clientText.text = "Client: " + contract.clientName;
        if (descriptionText != null) descriptionText.text = contract.jobDescription;
        if (rewardsText != null) rewardsText.text = "Paid: " + contract.goldReward + "G | " + contract.expReward + "XP";

        if (snapshotImage != null)
        {
            string photoPath = Application.persistentDataPath + "/" + contract.name + "_photo.png";
            
            if (File.Exists(photoPath))
            {
                byte[] bytes = File.ReadAllBytes(photoPath);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                
                snapshotImage.texture = tex;
                snapshotImage.color = Color.white;
                if (snapshotCaptionText != null) snapshotCaptionText.text = contract.clientName + "'s Bridge";
            }
            else
            {
                snapshotImage.texture = null;
                snapshotImage.color = Color.black; 
                if (snapshotCaptionText != null) snapshotCaptionText.text = "Photo Missing";
            }
        }

        if (pageCounterText != null) pageCounterText.text = (index + 1) + " / " + completedContractsList.Count;

        // Tell the Almanac Manager if the virtual buttons should be greyed out!
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.virtualHasPrev = (index > 0);
            AlmanacManager.Instance.virtualHasNext = (index < completedContractsList.Count - 1);
            AlmanacManager.Instance.ForceUpdatePaginationUI();
        }
    }

    private void ClearDetails()
    {
        if (titleText != null) titleText.text = "No Contracts Completed";
        if (clientText != null) clientText.text = "";
        if (descriptionText != null) descriptionText.text = "Complete jobs to unlock your photo history.";
        if (rewardsText != null) rewardsText.text = "";
        
        if (snapshotImage != null) { snapshotImage.texture = null; snapshotImage.color = new Color(0,0,0,0); }
        if (snapshotCaptionText != null) snapshotCaptionText.text = "";
        if (pageCounterText != null) pageCounterText.text = "0 / 0";

        // Grey out the arrows since there are 0 contracts!
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.virtualHasPrev = false;
            AlmanacManager.Instance.virtualHasNext = false;
            AlmanacManager.Instance.ForceUpdatePaginationUI();
        }
    }
}