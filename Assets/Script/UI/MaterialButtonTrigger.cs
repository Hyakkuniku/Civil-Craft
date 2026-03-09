using UnityEngine;
using UnityEngine.EventSystems;

public class MaterialButtonTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Drag the BridgeMaterialSO for this specific button here!")]
    public BridgeMaterialSO buttonMaterial;

    [Header("Selection Visuals")]
    [Tooltip("How many pixels the button moves UP when selected")]
    public float selectedUpOffset = 15f;

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
    }

    private void Update()
    {
        // Safely check if this button's material is the one currently active in the BarCreator
        if (BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null)
        {
            // --- THE FIX: Must NOT be in Delete Mode, AND must be the active material! ---
            bool shouldBeSelected = !BuildUIController.Instance.barCreator.isDeleteMode && 
                                    (BuildUIController.Instance.barCreator.activeMaterial == buttonMaterial); //

            // Only update the position if the selection state just changed
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
        }
        else
        {
            // Reset the button back to its original resting position
            if (rectTransform != null) rectTransform.anchoredPosition = originalPosition;
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