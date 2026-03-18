using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // --- NEW: Required to change the Image sprite ---

public class MaterialButtonTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Drag the BridgeMaterialSO for this specific button here!")]
    public BridgeMaterialSO buttonMaterial;

    [Header("Selection Visuals (Movement)")]
    [Tooltip("How many pixels the button moves UP when selected. Set to 0 if you only want the outline change.")]
    public float selectedUpOffset = 15f;

    // --- NEW: Image Swapping Variables ---
    [Header("Selection Visuals (Images)")]
    [Tooltip("The Image component on this button that shows the icon")]
    public Image buttonImage;
    [Tooltip("The normal image without the outline")]
    public Sprite defaultSprite;
    [Tooltip("The selected image WITH the outline")]
    public Sprite selectedOutlineSprite;

    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private bool isCurrentlySelected = false;

    private void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            // Save the starting position so we know exactly where to return to
            originalPosition = rectTransform.anchoredPosition;
        }

        // Initialize to the default sprite just in case
        if (buttonImage != null && defaultSprite != null && !isCurrentlySelected)
        {
            buttonImage.sprite = defaultSprite;
        }
    }

    private void Update()
    {
        // Safely check if this button's material is the one currently active in the BarCreator
        if (BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null)
        {
            // Must NOT be in Delete Mode, AND must be the active material!
            bool shouldBeSelected = !BuildUIController.Instance.barCreator.isDeleteMode && 
                                    (BuildUIController.Instance.barCreator.activeMaterial == buttonMaterial);

            // Only update the position and image if the selection state just changed
            if (shouldBeSelected != isCurrentlySelected)
            {
                isCurrentlySelected = shouldBeSelected;
                UpdateSelectionVisuals();
            }
        }
    }

    private void UpdateSelectionVisuals()
    {
        if (isCurrentlySelected)
        {
            // Move the button up by the offset amount
            if (rectTransform != null) rectTransform.anchoredPosition = originalPosition + new Vector2(0, selectedUpOffset);
            
            // --- THE FIX: Swap to the outlined sprite! ---
            if (buttonImage != null && selectedOutlineSprite != null)
            {
                buttonImage.sprite = selectedOutlineSprite;
            }
        }
        else
        {
            // Reset the button back to its original resting position
            if (rectTransform != null) rectTransform.anchoredPosition = originalPosition;

            // --- THE FIX: Swap back to the normal sprite! ---
            if (buttonImage != null && defaultSprite != null)
            {
                buttonImage.sprite = defaultSprite;
            }
        }
    }

    // --- TOOLTIP LOGIC ---
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null && buttonMaterial != null)
            MaterialTooltipManager.Instance.ShowTooltip(buttonMaterial);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null)
            MaterialTooltipManager.Instance.HideTooltip();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null && buttonMaterial != null)
            MaterialTooltipManager.Instance.ShowTooltip(buttonMaterial);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null)
            MaterialTooltipManager.Instance.HideTooltip();
    }
}