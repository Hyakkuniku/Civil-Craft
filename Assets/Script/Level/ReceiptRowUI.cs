using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ReceiptRowUI : MonoBehaviour
{
    public Image iconImage;
    public TextMeshProUGUI materialNameText; // <-- NEW: Text field for the name
    public TextMeshProUGUI priceText;
    public TextMeshProUGUI quantityText;
    public TextMeshProUGUI totalText;

    public void Setup(BridgeMaterialSO mat, float billableLength)
    {
        // 1. Set the Icon
        if (iconImage != null && mat.materialIcon != null) 
        {
            iconImage.sprite = mat.materialIcon;
        }
        
        // 2. Set the Name (and clean up the string so it looks nice!)
        if (materialNameText != null) 
        {
            string cleanName = mat.name.Replace("Material", "").Replace("SO", "").Trim();
            materialNameText.text = cleanName;
        }

        // 3. Do the Math
        float costPerUnit = mat.costPerMeter;
        float rowTotal = billableLength * costPerUnit;

        // 4. Fill out the rest of the text
        if (priceText != null) priceText.text = $"${costPerUnit}/m";
        if (quantityText != null) quantityText.text = $"x {billableLength:F1}m";
        if (totalText != null) totalText.text = $"= ${rowTotal:F0}"; 
    }
}