using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; 

public class MaterialButtonTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Drag the BridgeMaterialSO for this specific button here!")]
    public BridgeMaterialSO buttonMaterial;

    [Header("Selection Visuals (Movement)")]
    [Tooltip("How many pixels the button moves UP when selected. Set to 0 if you only want the outline change.")]
    public float selectedUpOffset = 15f;

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
            originalPosition = rectTransform.anchoredPosition;
        }

        if (buttonImage != null && defaultSprite != null && !isCurrentlySelected)
        {
            buttonImage.sprite = defaultSprite;
        }

        // --- NEW: Subscribe to the BarCreator event instead of polling in Update! ---
        if (BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null)
        {
            BuildUIController.Instance.barCreator.OnActiveMaterialChanged += HandleMaterialChanged;
            
            // Check initial state
            HandleMaterialChanged(BuildUIController.Instance.barCreator.activeMaterial);
        }
    }

    private void OnDestroy()
    {
        // Always unsubscribe to prevent memory leaks!
        if (BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null)
        {
            BuildUIController.Instance.barCreator.OnActiveMaterialChanged -= HandleMaterialChanged;
        }
    }

    // --- NEW: Event Callback ---
    private void HandleMaterialChanged(BridgeMaterialSO newMaterial)
    {
        bool isDeleting = BuildUIController.Instance.barCreator.isDeleteMode;
        bool shouldBeSelected = !isDeleting && (newMaterial == buttonMaterial);

        if (shouldBeSelected != isCurrentlySelected)
        {
            isCurrentlySelected = shouldBeSelected;
            UpdateSelectionVisuals();
        }
    }

    private void UpdateSelectionVisuals()
    {
        if (isCurrentlySelected)
        {
            if (rectTransform != null) rectTransform.anchoredPosition = originalPosition + new Vector2(0, selectedUpOffset);
            if (buttonImage != null && selectedOutlineSprite != null) buttonImage.sprite = selectedOutlineSprite;
        }
        else
        {
            if (rectTransform != null) rectTransform.anchoredPosition = originalPosition;
            if (buttonImage != null && defaultSprite != null) buttonImage.sprite = defaultSprite;
        }
    }

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