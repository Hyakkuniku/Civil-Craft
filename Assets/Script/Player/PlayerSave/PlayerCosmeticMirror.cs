using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerCosmeticMirror : MonoBehaviour
{
    [Header("Legacy Hat Library")]
    public List<CosmeticItem> hats = new List<CosmeticItem>();

    [Header("Categorized Model Bindings")]
    public List<CosmeticModelBinding> cosmeticBindings = new List<CosmeticModelBinding>();

    private bool previewSessionActive;
    private CosmeticLoadoutData previewLoadout;

    public bool IsPreviewing => previewSessionActive;

    private void OnEnable()
    {
        PlayerCosmetics.LoadoutChanged -= HandleSavedLoadoutChanged;
        PlayerCosmetics.LoadoutChanged += HandleSavedLoadoutChanged;
        PlayerCosmetics.EquippedHatChanged -= HandleSavedHatChanged;
        PlayerCosmetics.EquippedHatChanged += HandleSavedHatChanged;
        RefreshCosmetics();
    }

    private void Start() => RefreshCosmetics();

    private void OnDisable()
    {
        PlayerCosmetics.LoadoutChanged -= HandleSavedLoadoutChanged;
        PlayerCosmetics.EquippedHatChanged -= HandleSavedHatChanged;
    }

    public void RefreshCosmetics()
    {
        if (previewSessionActive && previewLoadout != null)
        {
            ApplyLoadoutInternal(previewLoadout);
            return;
        }

        CosmeticLoadoutData loadout = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetCosmeticLoadoutCopy()
            : new CosmeticLoadoutData();
        ApplyLoadoutInternal(loadout);
    }

    public void ApplyLoadout(CosmeticLoadoutData loadout)
    {
        if (loadout == null) return;
        if (previewSessionActive)
            previewLoadout = loadout.Clone();
        ApplyLoadoutInternal(loadout);
    }

    /// <summary>
    /// Starts an unsaved wardrobe session. Saved-loadout events cannot replace
    /// the photobooth appearance until this session is explicitly ended.
    /// </summary>
    public void BeginPreview(CosmeticLoadoutData loadout)
    {
        previewSessionActive = true;
        previewLoadout = loadout != null ? loadout.Clone() : new CosmeticLoadoutData();
        ApplyLoadoutInternal(previewLoadout);
    }

    public void UpdatePreview(CosmeticLoadoutData loadout)
    {
        if (loadout == null) return;
        if (!previewSessionActive) previewSessionActive = true;
        previewLoadout = loadout.Clone();
        ApplyLoadoutInternal(previewLoadout);
    }

    public void EndPreview(bool restoreSavedLoadout)
    {
        previewSessionActive = false;
        previewLoadout = null;
        if (restoreSavedLoadout) RefreshCosmetics();
    }

    private void ApplyLoadoutInternal(CosmeticLoadoutData loadout)
    {
        if (loadout == null) return;
        CosmeticBindingUtility.Apply(cosmeticBindings, loadout);
        ApplyLegacyHat(loadout.accessoriesID);
    }

    private void ApplyLegacyHat(string equippedHatID)
    {
        foreach (CosmeticItem hat in hats)
        {
            if (hat == null || hat.cosmeticModel == null) continue;
            bool visible = !string.IsNullOrWhiteSpace(equippedHatID) &&
                           string.Equals(hat.cosmeticID?.Trim(), equippedHatID.Trim(),
                               StringComparison.Ordinal);
            hat.cosmeticModel.SetActive(visible);
        }
    }

    private void HandleSavedLoadoutChanged(CosmeticLoadoutData loadout)
    {
        if (!previewSessionActive) ApplyLoadoutInternal(loadout);
    }

    private void HandleSavedHatChanged(string equippedHatID)
    {
        if (!previewSessionActive) ApplyLegacyHat(equippedHatID);
    }
}
