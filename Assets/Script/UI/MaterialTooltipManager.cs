using UnityEngine;
using UnityEngine.UI;
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
    private CanvasGroup tooltipCanvasGroup;
    private BridgeMaterialSO displayedMaterial;

    private void Awake()
    {
        Instance = this;
        if (tooltipPanel == null) return;
        tooltipCanvasGroup = tooltipPanel.GetComponent<CanvasGroup>();
        if (tooltipCanvasGroup == null) tooltipCanvasGroup = tooltipPanel.AddComponent<CanvasGroup>();
        tooltipCanvasGroup.interactable = false;
        tooltipCanvasGroup.blocksRaycasts = false;
        tooltipCanvasGroup.alpha = 0f;
        StyleMaterialCard();
        // Keep the authored hierarchy warm so opening it does not activate a
        // whole TMP/layout tree on the same frame as a touch.
        tooltipPanel.SetActive(true);
    }

    private void StyleMaterialCard()
    {
        // The authored layout group was measuring 200-pixel-wide, auto-sized
        // text on every material change. A fixed card avoids both tiny text and
        // a layout rebuild when the player holds a material icon.
        VerticalLayoutGroup layout = tooltipPanel.GetComponent<VerticalLayoutGroup>();
        if (layout != null) layout.enabled = false;
        ContentSizeFitter fitter = tooltipPanel.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;

        RectTransform panelRect = tooltipPanel.transform as RectTransform;
        if (panelRect != null)
        {
            panelRect.sizeDelta = new Vector2(440f, 306f);
            panelRect.anchoredPosition = new Vector2(18f, panelRect.anchoredPosition.y);
        }

        Image background = tooltipPanel.GetComponent<Image>();
        if (background != null)
        {
            background.color = new Color(0.045f, 0.09f, 0.19f, 0.94f);
            background.raycastTarget = false;
        }

        StyleMaterialText(materialNameText, 119f, 40f, 27f, true);
        StyleMaterialText(costText, 77f, 32f, 21f, false);
        StyleMaterialText(weightText, 31f, 52f, 20f, false);
        StyleMaterialText(strengthText, -19f, 46f, 20f, false);
        StyleMaterialText(lengthText, -65f, 32f, 21f, false);
        StyleMaterialText(typeText, -109f, 38f, 20f, false);
    }

    private static void StyleMaterialText(TextMeshProUGUI label, float y, float height, float size, bool heading)
    {
        if (label == null) return;
        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(396f, height);
        label.fontSize = size;
        label.enableAutoSizing = false;
        label.enableWordWrapping = !heading;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.fontStyle = heading ? FontStyles.Bold : FontStyles.Normal;
        label.color = heading
            ? new Color(1f, 0.77f, 0.42f, 1f)
            : new Color(0.98f, 0.95f, 0.88f, 1f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
    }

    public void ShowTooltip(BridgeMaterialSO material, bool immediate = false)
    {
        if (material == null || tooltipPanel == null) return;

        // Stop any existing timer so they don't overlap if the player swipes quickly across buttons
        if (showCoroutine != null)
        {
            StopCoroutine(showCoroutine);
            showCoroutine = null;
        }

        if (immediate) DisplayTooltip(material);
        else showCoroutine = StartCoroutine(ShowTooltipCoroutine(material));
    }

    private IEnumerator ShowTooltipCoroutine(BridgeMaterialSO material)
    {
        // Keep desktop hover intentional; mobile touches should respond quickly.
        yield return new WaitForSeconds(Application.isMobilePlatform
            ? Mathf.Min(hoverDelay, 0.12f) : hoverDelay);

        showCoroutine = null;
        DisplayTooltip(material);
    }

    private void DisplayTooltip(BridgeMaterialSO material)
    {
        if (tooltipCanvasGroup == null) return;
        if (displayedMaterial == material && tooltipCanvasGroup.alpha > 0f) return;
        displayedMaterial = material;

        // Populate the UI with the exact data from your SO
        if (materialNameText != null) materialNameText.text = material.GetDisplayName();
        if (costText != null) costText.text = $"<color=#F2BF72>COST</color>  <b>₱{material.costPerMeter:N0} / m</b>";
        if (weightText != null)
        {
            weightText.text = material.isDualBeam
                ? $"<color=#F2BF72>MASS</color>  <b>{material.GetPlacedMassPerMeter():0.###} kg/m placed</b>\n{material.massPerMeter:0.###} kg/m per beam"
                : $"<color=#F2BF72>MASS</color>  <b>{material.massPerMeter:0.###} kg/m</b>";
        }
        if (lengthText != null) lengthText.text = $"<color=#F2BF72>MAX LENGTH</color>  <b>{material.maxLength} m</b>";

        // Tension-only materials do not have a meaningful compression capacity.
        if (strengthText != null)
        {
            if (material.isRope)
                strengthText.text = $"<color=#F2BF72>TENSION LIMIT</color>  <b>{material.maxTension:N0} N</b>";
            else
                strengthText.text = $"<color=#F2BF72>AXIAL LIMIT</color>  <b>{Mathf.Min(material.maxTension, material.maxCompression):N0} N</b>";
        }

        // Let the player know what type of material this is
        if (typeText != null)
        {
            if (material.isRoad) typeText.text = "<color=#F2BF72>TYPE</color>  <b>Road · Drivable</b>";
            else if (material.isRope) typeText.text = "<color=#F2BF72>TYPE</color>  <b>Cable · Tension only</b>";
            else typeText.text = "<color=#F2BF72>TYPE</color>  <b>Structural beam</b>";
        }

        // Show the panel in its fixed location
        tooltipCanvasGroup.alpha = 1f;
    }

    public void HideTooltip()
    {
        // Cancel the timer if the player looks away before it pops up!
        if (showCoroutine != null)
        {
            StopCoroutine(showCoroutine);
            showCoroutine = null;
        }

        if (tooltipCanvasGroup != null) tooltipCanvasGroup.alpha = 0f;
    }
}
