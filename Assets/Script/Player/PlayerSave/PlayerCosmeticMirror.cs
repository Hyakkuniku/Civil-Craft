using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mirrors the player's saved cosmetics onto a separate display model, such as
/// the Almanac photobooth character. This is intentionally not another
/// PlayerCosmetics component so it cannot replace the gameplay singleton.
/// </summary>
[DisallowMultipleComponent]
public class PlayerCosmeticMirror : MonoBehaviour
{
    [Header("Display Cosmetic Library")]
    [Tooltip("Use the same cosmetic IDs as PlayerCosmetics, but assign the models belonging to this display character.")]
    public List<CosmeticItem> hats = new List<CosmeticItem>();

    private void OnEnable()
    {
        PlayerCosmetics.EquippedHatChanged -= ApplyEquippedHat;
        PlayerCosmetics.EquippedHatChanged += ApplyEquippedHat;
        RefreshCosmetics();
    }

    private void Start()
    {
        // OnEnable can run before PlayerDataManager has loaded its save data.
        RefreshCosmetics();
    }

    private void OnDisable()
    {
        PlayerCosmetics.EquippedHatChanged -= ApplyEquippedHat;
    }

    public void RefreshCosmetics()
    {
        string equippedHatID = string.Empty;
        if (PlayerDataManager.Instance != null &&
            PlayerDataManager.Instance.CurrentData != null)
        {
            equippedHatID = PlayerDataManager.Instance.CurrentData.equippedHatID ?? string.Empty;
        }

        ApplyEquippedHat(equippedHatID);
    }

    private void ApplyEquippedHat(string equippedHatID)
    {
        foreach (CosmeticItem hat in hats)
        {
            if (hat == null || hat.cosmeticModel == null)
                continue;

            bool shouldShow = !string.IsNullOrWhiteSpace(equippedHatID) &&
                              string.Equals(
                                  hat.cosmeticID?.Trim(),
                                  equippedHatID.Trim(),
                                  StringComparison.Ordinal);
            hat.cosmeticModel.SetActive(shouldShow);
        }
    }
}
