using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro; 

[DefaultExecutionOrder(100)]
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
    private Vector2 badgeParentScale;
    private bool badgeLayoutReady;
    private bool badgeUsesSharedTemplate;
    private static GameObject quantityBadgeTemplate;

    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private bool isCurrentlySelected = false;
    
    private bool isMaterialAllowed = true;
    private bool hasReachedQuantityLimit = false;
    private bool isMaterialHidden;
    public bool IsAvailableForSelection => buttonMaterial != null && isMaterialAllowed &&
        !isMaterialHidden && !hasReachedQuantityLimit;
    private Transform toolbarOrderCell;
    private Transform toolbarOrderParent;
    private int toolbarAuthoredIndex;

    internal int GetToolbarAuthoredIndex(Transform cell)
    {
        if (toolbarOrderCell != cell || toolbarOrderParent != cell.parent)
        {
            toolbarOrderCell = cell;
            toolbarOrderParent = cell.parent;
            toolbarAuthoredIndex = cell.GetSiblingIndex();
        }
        return toolbarAuthoredIndex;
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null) originalPosition = rectTransform.anchoredPosition;
        if (buttonImage != null) originalColor = buttonImage.color;
        if (buttonMaterial != null && buttonMaterial.isPier)
            gameObject.AddComponent<MaterialPierDrag>();
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

    private void LateUpdate()
    {
        if (badgeContainer != null && badgeContainer.activeInHierarchy) RefreshQuantityBadgeLayout();
    }

    /// <summary>Keep every remaining-piece badge compact in Canvas units, even on skewed legacy docks.</summary>
    public void RefreshQuantityBadgeLayout()
    {
        if (buttonMaterial == null) return;
        if (quantityBadgeTemplate == null) quantityBadgeTemplate = Resources.Load<GameObject>("UI/MaterialQuantityBadge");
        if (!badgeUsesSharedTemplate && quantityBadgeTemplate != null)
        {
            // Anchor to the common input cell, independent of each artwork's fitted size.
            Transform parent = transform;
            if (badgeContainer != null) badgeContainer.SetActive(false);
            badgeContainer = Instantiate(quantityBadgeTemplate, parent, false);
            badgeContainer.name = "Quantity Badge";
            quantityText = badgeContainer.GetComponentInChildren<TextMeshProUGUI>(true);
            badgeUsesSharedTemplate = true;
            badgeLayoutReady = false;
        }
        if (badgeContainer == null)
        {
            badgeContainer = new GameObject("Quantity Badge", typeof(RectTransform), typeof(Image));
            badgeContainer.transform.SetParent(buttonImage != null ? buttonImage.transform : transform, false);
            badgeContainer.GetComponent<Image>().color = Color.white;
            badgeLayoutReady = false;
        }
        if (quantityText == null)
        {
            var label = new GameObject("Remaining Pieces", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(badgeContainer.transform, false);
            quantityText = label.GetComponent<TextMeshProUGUI>();
            quantityText.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF") ?? TMP_Settings.defaultFontAsset;
            quantityText.color = new Color32(50, 45, 41, 255);
            badgeLayoutReady = false;
        }
        if (badgeContainer == null || quantityText == null ||
            !(badgeContainer.transform is RectTransform badge) || badge.parent == null) return;
        Transform canvas = null;
        for (Transform cursor = badge.parent; cursor != null; cursor = cursor.parent)
            if (cursor.GetComponent<Canvas>() != null) canvas = cursor;
        if (canvas == null) return;
        Matrix4x4 basis = Matrix4x4.identity;
        for (Transform cursor = badge.parent; cursor != null && cursor != canvas; cursor = cursor.parent)
            basis = Matrix4x4.TRS(Vector3.zero, cursor.localRotation, cursor.localScale) * basis;
        var scale = new Vector2(basis.MultiplyVector(Vector3.right).magnitude, basis.MultiplyVector(Vector3.up).magnitude);
        if (scale.x < .00001f || scale.y < .00001f) return;
        if (badgeLayoutReady && (scale - badgeParentScale).sqrMagnitude < .000001f) return;
        badgeParentScale = scale;
        badgeLayoutReady = true;
        badge.anchorMin = badge.anchorMax = badge.pivot = Vector2.one;
        badge.localRotation = Quaternion.identity;
        badge.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f);
        badge.sizeDelta = new Vector2(34f, 24f);
        badge.anchoredPosition3D = new Vector3(-3f / scale.x, -3f / scale.y, 0f);
        RectTransform text = quantityText.rectTransform;
        text.localScale = Vector3.one;
        text.localRotation = Quaternion.identity;
        text.anchorMin = Vector2.zero;
        text.anchorMax = Vector2.one;
        text.pivot = Vector2.one * .5f;
        text.offsetMin = new Vector2(2f, 1f);
        text.offsetMax = new Vector2(-2f, -1f);
        quantityText.margin = Vector4.zero;
        quantityText.fontSize = quantityText.fontSizeMax = 14f;
        quantityText.fontSizeMin = 9f;
        quantityText.enableAutoSizing = true;
        quantityText.enableWordWrapping = false;
        quantityText.alignment = TextAlignmentOptions.Center;
        quantityText.richText = false;
        quantityText.raycastTarget = false;
        Graphic style = quantityBadgeTemplate != null ? quantityBadgeTemplate.GetComponent<Graphic>() : null;
        TMP_Text typeStyle = quantityBadgeTemplate != null ? quantityBadgeTemplate.GetComponentInChildren<TMP_Text>(true) : null;
        quantityText.font = typeStyle != null ? typeStyle.font :
            Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF") ?? TMP_Settings.defaultFontAsset;
        quantityText.color = typeStyle != null ? typeStyle.color : new Color32(50, 45, 41, 255);
        if (badge.TryGetComponent<Graphic>(out Graphic image))
        {
            image.color = style != null ? style.color : Color.white;
            image.raycastTarget = false;
        }
    }

    private void OnDestroy()
    {
        if (BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null)
        {
            BuildUIController.Instance.barCreator.OnActiveMaterialChanged -= HandleMaterialChanged;
        }
    }

    public void EvaluateMaterialRestriction()
    {
        ContractSO currentContract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;
        bool hasContractOptIn = buttonMaterial != null && buttonMaterial.CanUseInContract(currentContract);
        isMaterialAllowed = hasContractOptIn;
        hasReachedQuantityLimit = false;
        isMaterialHidden = !hasContractOptIn;
        bool isTutorial = false; 
        bool isExplicitlyHidden = false;

        if (GameManager.Instance != null && GameManager.Instance.CurrentContract != null)
        {
            ContractSO contract = GameManager.Instance.CurrentContract;
            isTutorial = contract.IsTutorialForCurrentPlayer();
            isExplicitlyHidden = contract.IsMaterialHidden(buttonMaterial);
            
            if (contract.allowedMaterials != null && contract.allowedMaterials.Count > 0)
            {
                MaterialAllowance allowanceData = null;
                foreach (var allowance in contract.allowedMaterials)
                {
                    if (allowance != null && allowance.material == buttonMaterial)
                    {
                        allowanceData = allowance;
                        break;
                    }
                }

                bool isNaturallyAllowed = allowanceData != null;
                bool isUnlockedByPurchase = false;
                if (PlayerDataManager.Instance != null && buttonMaterial != null &&
                    !(GameManager.Instance != null && GameManager.Instance.IsSessionChallengeBuild))
                {
                    isUnlockedByPurchase = PlayerDataManager.Instance.IsMaterialUnlockedForContract(contract.ContractID, buttonMaterial.name);
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

                    RefreshQuantityBadgeLayout();
                    if (badgeContainer != null) badgeContainer.SetActive(true);
                    if (quantityText != null)
                    {
                        int remainingPieces = Mathf.Max(0, allowanceData.maxPieces - currentCount);
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
        else if (badgeContainer != null) badgeContainer.SetActive(false);

        isMaterialHidden |= isExplicitlyHidden || (isTutorial && !isMaterialAllowed);
        if (isMaterialHidden)
        {
            if (BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null &&
                BuildUIController.Instance.barCreator.activeMaterial == buttonMaterial)
            {
                BuildUIController.Instance.barCreator.SetActiveMaterial(null);
            }

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

        if ((!isMaterialAllowed || hasReachedQuantityLimit) && BuildUIController.Instance != null &&
            BuildUIController.Instance.barCreator != null &&
            BuildUIController.Instance.barCreator.activeMaterial == buttonMaterial)
        {
            BuildUIController.Instance.barCreator.SetActiveMaterial(null);
        }
    }

    private void HandleMaterialChanged(BridgeMaterialSO newMaterial)
    {
        bool isDeleting = BuildUIController.Instance != null && BuildUIController.Instance.barCreator != null &&
                          BuildUIController.Instance.barCreator.isDeleteMode;
        bool shouldBeSelected = !isDeleting && !isMaterialHidden && buttonMaterial != null &&
                                (newMaterial == buttonMaterial) && isMaterialAllowed && !hasReachedQuantityLimit;

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
        // Contract/purchase/usage state may have changed since OnEnable.
        EvaluateMaterialRestriction();
        if (isMaterialHidden) return;

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
            MaterialTooltipManager.Instance.ShowTooltip(buttonMaterial, true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (MaterialTooltipManager.Instance != null)
            MaterialTooltipManager.Instance.HideTooltip();
    }
}
