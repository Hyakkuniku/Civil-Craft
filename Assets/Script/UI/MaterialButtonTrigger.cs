using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro; 

public class MaterialButtonTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    [Tooltip("Drag the BridgeMaterialSO for this specific button here!")]
    public BridgeMaterialSO buttonMaterial;

    [Header("Tutorial UI Hiding")]
    [Tooltip("Drag the PARENT layout object here (e.g., the 'Road' or 'Beam' GameObject). This hides the empty space!")]
    public GameObject parentWrapper;

    [Header("Selection Visuals (Movement)")]
    public float selectedUpOffset = 15f;

    [Header("Selection Visuals (Images)")]
    public Image buttonImage;
    public Sprite defaultSprite;
    public Sprite selectedOutlineSprite;

    [Header("Disabled Visuals")]
    public Color disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.8f);
    private Color originalColor = Color.white;

    [Header("Quantity Badge")]
    public GameObject badgeContainer;
    public TextMeshProUGUI quantityText;

    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private bool isCurrentlySelected = false;
    
    private bool isMaterialAllowed = true;
    private bool hasReachedQuantityLimit = false;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null) originalPosition = rectTransform.anchoredPosition;
        if (buttonImage != null) originalColor = buttonImage.color;

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
        hasReachedQuantityLimit = false;
        bool isTutorial = false; 

        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            ContractSO contract = GameManager.Instance.CurrentContract;
            isTutorial = contract.isTutorialContract;
            
            if (contract.allowedMaterials != null && contract.allowedMaterials.Count > 0)
            {
                MaterialAllowance allowanceData = null;
                foreach (var allowance in contract.allowedMaterials)
                {
                    if (allowance.material == buttonMaterial)
                    {
                        allowanceData = allowance;
                        break;
                    }
                }

                bool isNaturallyAllowed = allowanceData != null;
                bool isUnlockedByPurchase = false;
                if (PlayerDataManager.Instance != null)
                {
                    isUnlockedByPurchase = PlayerDataManager.Instance.IsMaterialUnlockedForContract(contract.name, buttonMaterial.name);
                }

                if (!isNaturallyAllowed && !isUnlockedByPurchase)
                {
                    isMaterialAllowed = false;
                }
                
                if (isMaterialAllowed && allowanceData != null && allowanceData.maxPieces > 0)
                {
                    int currentCount = 0;
                    if (BuildUIController.Instance != null)
                    {
                        currentCount = BuildUIController.Instance.GetMaterialUsageCount(buttonMaterial);
                        if (currentCount >= allowanceData.maxPieces)
                        {
                            hasReachedQuantityLimit = true;
                        }
                    }

                    if (badgeContainer != null) badgeContainer.SetActive(true);
                    if (quantityText != null)
                    {
                        int remainingPieces = allowanceData.maxPieces - currentCount;
                        quantityText.text = remainingPieces.ToString(); 
                    }
                }
                else
                {
                    if (badgeContainer != null) badgeContainer.SetActive(false);
                }
            }
            else
            {
                if (badgeContainer != null) badgeContainer.SetActive(false);
            }
        }

        // Hide forbidden materials in a tutorial!
        if (isTutorial && !isMaterialAllowed)
        {
            if (parentWrapper != null) parentWrapper.SetActive(false);
            else gameObject.SetActive(false); 
            return; 
        }
        else
        {
            if (parentWrapper != null) parentWrapper.SetActive(true);
            else gameObject.SetActive(true);

            if (buttonImage != null)
            {
                buttonImage.color = (!isMaterialAllowed || hasReachedQuantityLimit) ? disabledColor : originalColor;
            }
        }

        if ((!isMaterialAllowed || hasReachedQuantityLimit) && isCurrentlySelected && BuildUIController.Instance != null)
        {
            BuildUIController.Instance.barCreator.SetActiveMaterial(null);
        }
    }

    private void HandleMaterialChanged(BridgeMaterialSO newMaterial)
    {
        bool isDeleting = BuildUIController.Instance != null && BuildUIController.Instance.barCreator.isDeleteMode;
        bool shouldBeSelected = !isDeleting && (newMaterial == buttonMaterial) && isMaterialAllowed && !hasReachedQuantityLimit;

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
        // --- THE FIX: Block clicks if the tutorial is locked and this isn't the requested material! ---
        if (BuildUIController.Instance != null && BuildUIController.Instance.isTutorialUI_Locked)
        {
            if (BuildUIController.Instance.whitelistedMaterial != buttonMaterial)
            {
                Debug.Log($"<color=orange>Tutorial Blocked Click on: {buttonMaterial.name}</color>");
                return; // Ignored!
            }
        }

        if (!isMaterialAllowed)
        {
            if (BuildUIController.Instance != null)
            {
                BuildUIController.Instance.PromptUnlockMaterial(this);
            }
            return;
        }

        if (hasReachedQuantityLimit)
        {
            if (BuildUIController.Instance != null)
            {
                BuildUIController.Instance.LogAction($"Limit Reached for {buttonMaterial.name}!");
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