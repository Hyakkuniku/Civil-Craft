using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; 

public class MaterialButtonTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
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

    [Header("Disabled Visuals")]
    [Tooltip("What color should the button turn if the contract forbids this material?")]
    public Color disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.8f);
    private Color originalColor = Color.white;

    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private bool isCurrentlySelected = false;
    
    private bool isMaterialAllowed = true;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            originalPosition = rectTransform.anchoredPosition;
        }

        if (buttonImage != null)
        {
            originalColor = buttonImage.color;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(EvaluateMaterialRestriction);
        }
    }

    private void Start()
    {
        if (buttonImage != null && defaultSprite != null && !isCurrentlySelected)
        {
            buttonImage.sprite = defaultSprite;
        }

        if (BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null)
        {
            BuildUIController.Instance.barCreator.OnActiveMaterialChanged += HandleMaterialChanged;
            HandleMaterialChanged(BuildUIController.Instance.barCreator.activeMaterial);
        }
    }

    private void OnEnable()
    {
        EvaluateMaterialRestriction();
    }

    private void OnDestroy()
    {
        if (BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null)
        {
            BuildUIController.Instance.barCreator.OnActiveMaterialChanged -= HandleMaterialChanged;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(EvaluateMaterialRestriction);
        }
    }

    public void EvaluateMaterialRestriction()
    {
        isMaterialAllowed = true; 

        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            ContractSO contract = GameManager.Instance.CurrentContract;
            
            if (contract.allowedMaterials != null && contract.allowedMaterials.Count > 0)
            {
                bool isNaturallyAllowed = contract.allowedMaterials.Contains(buttonMaterial);
                
                // --- THE FIX: Ask the PlayerDataManager if we bought this! ---
                bool isUnlockedByPurchase = false;
                if (PlayerDataManager.Instance != null)
                {
                    isUnlockedByPurchase = PlayerDataManager.Instance.IsMaterialUnlockedForContract(contract.name, buttonMaterial.name);
                }

                if (!isNaturallyAllowed && !isUnlockedByPurchase)
                {
                    isMaterialAllowed = false;
                }
            }
        }

        if (buttonImage != null)
        {
            buttonImage.color = isMaterialAllowed ? originalColor : disabledColor;
        }

        if (!isMaterialAllowed && isCurrentlySelected && BuildUIController.Instance != null)
        {
            BuildUIController.Instance.barCreator.SetActiveMaterial(null);
        }
    }

    private void HandleMaterialChanged(BridgeMaterialSO newMaterial)
    {
        bool isDeleting = BuildUIController.Instance != null && BuildUIController.Instance.barCreator.isDeleteMode;
        bool shouldBeSelected = !isDeleting && (newMaterial == buttonMaterial) && isMaterialAllowed;

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

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isMaterialAllowed)
        {
            if (BuildUIController.Instance != null)
            {
                BuildUIController.Instance.PromptUnlockMaterial(this);
            }
            return;
        }

        if (BuildUIController.Instance != null)
        {
            BuildUIController.Instance.OnMaterialSelected(buttonMaterial);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null && buttonMaterial != null && isMaterialAllowed)
            MaterialTooltipManager.Instance.ShowTooltip(buttonMaterial);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null)
            MaterialTooltipManager.Instance.HideTooltip();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null && buttonMaterial != null && isMaterialAllowed)
            MaterialTooltipManager.Instance.ShowTooltip(buttonMaterial);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null)
            MaterialTooltipManager.Instance.HideTooltip();
    }
}