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
        if (transform.parent != null && transform.parent.name == "Receipt Items")
        {
            SetupPaperRow(mat, billableLength);
            return;
        }
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
        if (priceText != null) priceText.text = $"₱{costPerUnit:N0}/m";
        if (quantityText != null) quantityText.text = $"x {billableLength:F1}m";
        if (totalText != null) totalText.text = $"= ₱{rowTotal:N0}";
    }

    private void SetupPaperRow(BridgeMaterialSO mat, float length)
    {
        if (mat == null) return;
        TMP_FontAsset font = materialNameText != null ? materialNameText.font : TMP_Settings.defaultFontAsset;
        if (LevelCompleteManager.Instance != null && LevelCompleteManager.Instance.ReceiptFont != null)
            font = LevelCompleteManager.Instance.ReceiptFont;
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        foreach (var graphic in GetComponents<Graphic>()) graphic.enabled = false;
        foreach (var group in GetComponents<LayoutGroup>()) group.enabled = false;
        var fitter = GetComponent<ContentSizeFitter>(); if (fitter != null) fitter.enabled = false;
        transform.localScale = Vector3.one;
        var element = GetComponent<LayoutElement>();
        if (element == null) element = gameObject.AddComponent<LayoutElement>();
        element.ignoreLayout = false; element.minHeight = 96; element.preferredHeight = 96; element.flexibleHeight = 0;
        string title = mat.name.Replace("Material", "").Replace("SO", "").Trim();
        CompletionReceiptLayout.Label(transform,"Item",title,0,.49f,.62f,1,28,font);
        CompletionReceiptLayout.Label(transform,"Amount",$"₱{length * mat.costPerMeter:N0}",.63f,.49f,1,1,28,font,TextAlignmentOptions.MidlineRight);
        CompletionReceiptLayout.Label(transform,"Quantity and rate",$"{length:F1} m × ₱{mat.costPerMeter:N0}/m",0,.12f,1,.48f,23,font);
        CompletionReceiptLayout.Divider(transform,"Dashed divider",0,.04f,1);
    }
}
