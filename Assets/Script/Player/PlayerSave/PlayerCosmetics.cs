using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class CosmeticItem
{
    [Tooltip("The unique ID saved in JSON (e.g., 'EngineerHardHat')")]
    public string cosmeticID; 
    [Tooltip("The actual 3D model on the character to turn on/off")]
    public GameObject cosmeticModel; 
}

public class PlayerCosmetics : MonoBehaviour
{
    public static PlayerCosmetics Instance { get; private set; }

    [Header("Cosmetic Library")]
    public List<CosmeticItem> hats = new List<CosmeticItem>();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Check JSON and equip the right hat as soon as the scene loads
        RefreshCosmetics();
    }

    public void RefreshCosmetics()
    {
        if (PlayerDataManager.Instance == null) return;

        string savedHat = PlayerDataManager.Instance.CurrentData.equippedHatID;

        // Loop through all hats. If the ID matches, turn it ON. Otherwise, OFF.
        foreach (var hat in hats)
        {
            if (hat.cosmeticModel != null)
            {
                hat.cosmeticModel.SetActive(hat.cosmeticID == savedHat);
            }
        }
    }

    public void UnlockAndEquipHat(string hatID)
    {
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.UnlockCosmeticReward(hatID, true);

        // Instantly update the visual on the character
        RefreshCosmetics();
    }
}
