using UnityEngine;
using TMPro;
using System.Collections;

public class MaterialTooltipManager : MonoBehaviour
{
    public static MaterialTooltipManager Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("How long to hover/hold before the tooltip appears (in seconds)")]
    public float hoverDelay = 0.5f;

    [Header("UI References")]
    [Tooltip("The actual popup panel background. Place this wherever you want it to stay on screen!")]
    public GameObject tooltipPanel; 
    
    [Header("Text Fields")]
    public TextMeshProUGUI materialNameText;
    public TextMeshProUGUI costText;
    public TextMeshProUGUI weightText;
    public TextMeshProUGUI strengthText;
    public TextMeshProUGUI lengthText;
    public TextMeshProUGUI typeText;

    private Coroutine showCoroutine;

    private void Awake()
    {
        Instance = this;
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
    }

    public void ShowTooltip(BridgeMaterialSO material)
    {
        if (material == null || tooltipPanel == null) return;

        // Stop any existing timer so they don't overlap if the player swipes quickly across buttons
        if (showCoroutine != null) StopCoroutine(showCoroutine);

        // Start the delay timer
        showCoroutine = StartCoroutine(ShowTooltipCoroutine(material));
    }

    private IEnumerator ShowTooltipCoroutine(BridgeMaterialSO material)
    {
        // Wait for the exact amount of time you set in the Inspector
        yield return new WaitForSeconds(hoverDelay);

        // Populate the UI with the exact data from your SO
        if (materialNameText != null) materialNameText.text = material.GetDisplayName();
        if (costText != null) costText.text = $"Cost: ₱{material.costPerMeter:N0}/m";
        if (weightText != null)
        {
            weightText.text = material.isDualBeam
                ? $"Mass: {material.massPerMeter:0.###} kg/m each ({material.GetPlacedMassPerMeter():0.###} kg/m placed)"
                : $"Mass: {material.massPerMeter:0.###} kg/m";
        }
        if (lengthText != null) lengthText.text = $"Max Length: {material.maxLength}m";

        // Tension-only materials do not have a meaningful compression capacity.
        if (strengthText != null)
        {
            if (material.isRope)
                strengthText.text = $"Tension Limit: {material.maxTension:N0} N";
            else
                strengthText.text = $"Weakest Axial Limit: {Mathf.Min(material.maxTension, material.maxCompression):N0} N";
        }

        // Let the player know what type of material this is
        if (typeText != null)
        {
            if (material.isRoad) typeText.text = "Type: Road (Drivable)";
            else if (material.isRope) typeText.text = "Type: Cable (Tension Only)";
            else typeText.text = "Type: Structural Beam";
        }

        // Show the panel in its fixed location
        tooltipPanel.SetActive(true);
    }

    public void HideTooltip()
    {
        // Cancel the timer if the player looks away before it pops up!
        if (showCoroutine != null)
        {
            StopCoroutine(showCoroutine);
            showCoroutine = null;
        }

        if (tooltipPanel != null) tooltipPanel.SetActive(false);
    }
}
