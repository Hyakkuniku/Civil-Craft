using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[Serializable]
public class ShopItemEvent : UnityEvent<ShopItemData> { }

[Serializable]
public class ShopMessageEvent : UnityEvent<string> { }

[Serializable]
public class ShopCategoryTab
{
    public ShopCategory category;
    public Button button;
    [Tooltip("Optional highlight, underline, or selected background for this tab.")]
    public GameObject selectedVisual;
}

[DisallowMultipleComponent]
public class ShopManager : MonoBehaviour
{
    private const string ShopIntroLessonId = "Sequence_Shop";
    // A new lesson ID is intentional: older saves may have completed the
    // optional vest prompt without buying it.
    private const string SafetyVestFollowupLessonId = "Sequence_Shop_SafetyVest_Purchase";
    private const string SafetyVestItemId = "shop_cosmetic_Accessory_SafetyVest";
    private const string SafetyVestCosmeticId = "Accessory_SafetyVest";
    private const string SafetyVestEquipLessonId = "Sequence_Shop_SafetyVest_Equip";

    public static ShopManager Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private string currencyPrefix = "";
    [SerializeField] private TMP_Text secondaryCurrencyText;
    [SerializeField] private string secondaryCurrencyPlaceholder = "0";

    [Header("Purchase Confirmation")]
    [SerializeField] private GameObject purchaseConfirmationPanel;
    [SerializeField] private TMP_Text confirmationTitleText;
    [SerializeField] private TMP_Text confirmationDescriptionText;
    [SerializeField] private TMP_Text confirmationPriceText;
    [SerializeField] private Image confirmationIconImage;

    [Header("Purchase Feedback")]
    [Tooltip("Shown for insufficient funds, already-owned items, and other rejected purchases.")]
    [SerializeField] private GameObject purchaseFeedbackPanel;
    [SerializeField] private TMP_Text purchaseFeedbackTitleText;
    [SerializeField] private TMP_Text purchaseFeedbackMessageText;

    [Header("Catalog")]
    [Tooltip("Add ShopItemData assets here. No code changes are needed when the catalog grows.")]
    [SerializeField] private List<ShopItemData> allItems = new List<ShopItemData>();
    [SerializeField, Tooltip("Automatically includes every ShopItemData asset in the project while editing.")]
    private bool autoPopulateCatalogFromProject = true;
    [SerializeField] private ShopCategory defaultCategory = ShopCategory.Accessory;
    [SerializeField] private bool sortItemsByPrice;

    [Header("Grid")]
    [SerializeField] private Transform itemGridContent;
    [SerializeField] private ShopItemUI itemCardPrefab;

    [Header("Category Tabs")]
    [SerializeField] private List<ShopCategoryTab> categoryTabs = new List<ShopCategoryTab>();

    [Header("Tutorial Integration")]
    [SerializeField, Tooltip("Optional tutorial shown after the shop UI has finished opening. Give the sequence a unique Lesson Name to show it only once per save.")]
    private TutorialSequence onOpenTutorial;
    [SerializeField, Tooltip("Prevents the player from closing the shop before the assigned shop tutorial reaches its final step.")]
    private bool blockClosingUntilFinalTutorialStep = true;

    [Header("Purchase Events")]
    public ShopItemEvent onItemPurchased = new ShopItemEvent();
    public ShopMessageEvent onPurchaseRejected = new ShopMessageEvent();
    public UnityEvent onAddCurrencyClicked = new UnityEvent();

    private readonly List<ShopItemUI> cardPool = new List<ShopItemUI>();
    private readonly Dictionary<Button, UnityAction> tabListeners = new Dictionary<Button, UnityAction>();
    private ShopCategory currentCategory;
    private PlayerDataManager boundPlayerData;
    private ShopItemData pendingPurchase;
    private Coroutine openTutorialCoroutine;
    private Coroutine ownedVestAdvanceCoroutine;
    private TutorialStep[] originalOpenTutorialSteps;
    private TutorialStep safetyVestPurchaseStep;
    private TutorialStep shopCloseStep;
    private TutorialStep openAlmanacStep;
    private TutorialStep openEditPlayerStep;
    private TutorialStep selectSafetyVestStep;
    private TutorialStep saveSafetyVestStep;
    private CharacterCustomizationController customizationController;
    private bool initialized;

    public GameObject Panel => shopPanel;
    public IReadOnlyList<ShopItemData> AllItems => allItems;
    public ShopCategory CurrentCategory => currentCategory;
    public bool IsGuidingVestEquip
    {
        get
        {
            TutorialManager tutorial = TutorialManager.Instance;
            return tutorial != null && tutorial.IsPlayingSequence(onOpenTutorial) &&
                   (tutorial.CurrentStep == openAlmanacStep ||
                    tutorial.CurrentStep == openEditPlayerStep ||
                    tutorial.CurrentStep == selectSafetyVestStep ||
                    tutorial.CurrentStep == saveSafetyVestStep);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!autoPopulateCatalogFromProject || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        List<ShopItemData> discovered = new List<ShopItemData>();
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:ShopItemData"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            ShopItemData item = UnityEditor.AssetDatabase.LoadAssetAtPath<ShopItemData>(path);
            if (item != null && item.cosmeticDefinition != null) discovered.Add(item);
        }

        discovered.Sort((left, right) => string.Compare(
            left.itemName,
            right.itemName,
            StringComparison.OrdinalIgnoreCase));

        bool changed = allItems == null || allItems.Count != discovered.Count;
        if (!changed)
        {
            for (int i = 0; i < discovered.Count; i++)
            {
                if (allItems[i] == discovered[i]) continue;
                changed = true;
                break;
            }
        }

        if (!changed) return;
        allItems = discovered;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ShopManager] More than one ShopManager is active. Using the first instance.", this);
            return;
        }

        Instance = this;
        InitializeIfNeeded();

        if (shopPanel != null)
            shopPanel.SetActive(false);
        if (purchaseConfirmationPanel != null)
            purchaseConfirmationPanel.SetActive(false);
        if (purchaseFeedbackPanel != null)
            purchaseFeedbackPanel.SetActive(false);
    }

    private void Start()
    {
        BindPlayerData();
        UpdateCurrencyDisplay();
    }

    private void Update()
    {
        MonitorVestEquipTutorial();
    }

    private void OnDestroy()
    {
        UnbindPlayerData();
        if (safetyVestPurchaseStep != null)
            safetyVestPurchaseStep.OnStepStart.RemoveListener(PrepareSafetyVestPurchaseStep);
        if (openAlmanacStep != null)
            openAlmanacStep.OnStepStart.RemoveListener(PrepareOpenAlmanacStep);
        if (openEditPlayerStep != null)
            openEditPlayerStep.OnStepStart.RemoveListener(PrepareEditPlayerStep);
        if (selectSafetyVestStep != null)
            selectSafetyVestStep.OnStepStart.RemoveListener(PrepareSelectSafetyVestStep);
        if (saveSafetyVestStep != null)
            saveSafetyVestStep.OnStepStart.RemoveListener(PrepareSaveSafetyVestStep);

        foreach (KeyValuePair<Button, UnityAction> listener in tabListeners)
        {
            if (listener.Key != null)
                listener.Key.onClick.RemoveListener(listener.Value);
        }
        tabListeners.Clear();

        if (Instance == this)
            Instance = null;
    }

    public void OpenShop()
    {
        InitializeIfNeeded();
        BindPlayerData();

        if (shopPanel == null)
        {
            Debug.LogError("[ShopManager] Shop Panel is not assigned.", this);
            return;
        }

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.OpenPanel(shopPanel);
        else
            shopPanel.SetActive(true);

        shopPanel.transform.SetAsLastSibling();
        CancelPendingPurchase();
        HidePurchaseFeedback();
        UpdateCurrencyDisplay();
        ShowCategory(currentCategory);

        if (openTutorialCoroutine != null)
            StopCoroutine(openTutorialCoroutine);
        openTutorialCoroutine = StartCoroutine(StartOpenTutorialWhenReady());
    }

    public void CloseShop()
    {
        if (shopPanel == null) return;

        if (IsOpenTutorialBlockingClose())
        {
            Debug.Log("[ShopManager] Finish the shop introduction before closing the shop.", this);
            return;
        }

        // If the player cannot make the required purchase, let them leave without
        // recording tutorial completion. It will prompt them again on the next visit.
        TutorialManager tutorial = TutorialManager.Instance;
        if (tutorial != null && tutorial.IsPlayingSequence(onOpenTutorial) &&
            tutorial.CurrentStep == safetyVestPurchaseStep && !CanBuySafetyVestNow())
            tutorial.CancelTutorialWithoutCompletion(onOpenTutorial);

        if (openTutorialCoroutine != null)
        {
            StopCoroutine(openTutorialCoroutine);
            openTutorialCoroutine = null;
        }

        CancelPendingPurchase();
        HidePurchaseFeedback();

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.ClosePanel(shopPanel);
        else
            shopPanel.SetActive(false);
    }

    private IEnumerator StartOpenTutorialWhenReady()
    {
        // Let the panel, generated item cards, and layout groups settle before a
        // tutorial pointer reads their final screen positions.
        yield return new WaitForEndOfFrame();
        openTutorialCoroutine = null;

        if (onOpenTutorial == null || shopPanel == null || !shopPanel.activeInHierarchy)
            yield break;

        Canvas.ForceUpdateCanvases();

        ConfigureSafetyVestTutorial();

        if (TutorialManager.Instance != null)
            TutorialManager.Instance.PlayPriorityTutorial(onOpenTutorial);
        else
            onOpenTutorial.TryStartTutorial();
    }

    private bool IsOpenTutorialBlockingClose()
    {
        if (!blockClosingUntilFinalTutorialStep || onOpenTutorial == null ||
            onOpenTutorial.tutorialSteps == null || onOpenTutorial.tutorialSteps.Length == 0 ||
            TutorialManager.Instance == null ||
            !TutorialManager.Instance.IsPlayingSequence(onOpenTutorial))
        {
            return false;
        }

        if (TutorialManager.Instance.CurrentStep == safetyVestPurchaseStep &&
            !CanBuySafetyVestNow())
            return false;

        int closeStepIndex = Array.IndexOf(onOpenTutorial.tutorialSteps, shopCloseStep);
        if (closeStepIndex < 0) closeStepIndex = onOpenTutorial.tutorialSteps.Length - 1;
        return TutorialManager.Instance.CurrentStepIndex < closeStepIndex;
    }

    private bool CanBuySafetyVestNow()
    {
        ShopItemData vest = allItems.Find(item => item != null && item.ItemId == SafetyVestItemId);
        if (vest == null || boundPlayerData == null || boundPlayerData.CurrentData == null ||
            boundPlayerData.CurrentData.gold < vest.price)
            return false;

        foreach (ShopItemUI card in cardPool)
        {
            if (card != null && card.Item != null && card.Item.ItemId == SafetyVestItemId &&
                card.PurchaseButtonTarget != null)
                return true;
        }

        return false;
    }

    private void ConfigureSafetyVestTutorial()
    {
        if (onOpenTutorial == null ||
            (onOpenTutorial.lessonName != ShopIntroLessonId &&
             onOpenTutorial.lessonName != SafetyVestFollowupLessonId &&
             onOpenTutorial.lessonName != SafetyVestEquipLessonId))
            return;

        if (originalOpenTutorialSteps == null)
            originalOpenTutorialSteps = onOpenTutorial.tutorialSteps;
        if (originalOpenTutorialSteps == null || originalOpenTutorialSteps.Length == 0)
            return;
        shopCloseStep = originalOpenTutorialSteps[originalOpenTutorialSteps.Length - 1];

        ShopItemData vest = allItems.Find(item => item != null && item.ItemId == SafetyVestItemId);
        if (vest == null)
        {
            Debug.LogWarning("[ShopManager] Safety Vest is missing from the shop catalog; its tutorial step was not added.", this);
            onOpenTutorial.lessonName = ShopIntroLessonId;
            onOpenTutorial.tutorialSteps = originalOpenTutorialSteps;
            return;
        }

        if (safetyVestPurchaseStep == null)
        {
            safetyVestPurchaseStep = new TutorialStep
            {
                message = "Buy the <b><#E09500>SAFETY VEST</color></b> in Accessories. Confirm the purchase to continue. Short on coins? Close the Shop and return later.",
                screenPosition = TutorialPosition.LowCenter,
                showNextButton = false,
                canSkip = false,
                usePointer = true,
                pointerOffset = new Vector2(0f, 80f),
                pointerRotation = 180f
            };
            safetyVestPurchaseStep.OnStepStart.AddListener(PrepareSafetyVestPurchaseStep);
        }
        EnsureVestEquipSteps();

        bool ownsVest = boundPlayerData != null && boundPlayerData.OwnsShopItem(SafetyVestItemId);
        bool wearsVest = IsSafetyVestSavedEquipped();
        bool finishedIntro = boundPlayerData != null &&
            boundPlayerData.CurrentData.completedLessons.Contains(ShopIntroLessonId);

        if (finishedIntro)
        {
            // Older saves may have completed the previous purchase-only lesson.
            // The new lesson finishes only after the vest is saved as equipped.
            onOpenTutorial.lessonName = wearsVest ? ShopIntroLessonId : SafetyVestEquipLessonId;
            if (wearsVest)
            {
                onOpenTutorial.tutorialSteps = originalOpenTutorialSteps;
                return;
            }

            List<TutorialStep> followupSteps = new List<TutorialStep>();
            if (!ownsVest) followupSteps.Add(safetyVestPurchaseStep);
            followupSteps.Add(shopCloseStep);
            AddVestEquipSteps(followupSteps);
            foreach (TutorialStep step in followupSteps)
            {
                if (step != null) step.canSkip = false;
            }
            onOpenTutorial.tutorialSteps = followupSteps.ToArray();
            return;
        }

        onOpenTutorial.lessonName = ShopIntroLessonId;
        List<TutorialStep> extendedSteps = new List<TutorialStep>(originalOpenTutorialSteps.Length + 5);
        for (int i = 0; i < originalOpenTutorialSteps.Length - 1; i++)
            extendedSteps.Add(originalOpenTutorialSteps[i]);
        if (!ownsVest && !wearsVest) extendedSteps.Add(safetyVestPurchaseStep);
        extendedSteps.Add(shopCloseStep);
        if (!wearsVest) AddVestEquipSteps(extendedSteps);
        foreach (TutorialStep step in extendedSteps)
        {
            if (step != null) step.canSkip = false;
        }
        onOpenTutorial.tutorialSteps = extendedSteps.ToArray();
    }

    private void AddVestEquipSteps(List<TutorialStep> steps)
    {
        steps.Add(openAlmanacStep);
        steps.Add(openEditPlayerStep);
        steps.Add(selectSafetyVestStep);
        steps.Add(saveSafetyVestStep);
    }

    private void EnsureVestEquipSteps()
    {
        if (openAlmanacStep != null) return;

        openAlmanacStep = CreateVestEquipStep(
            "Now open the <b><#E09500>ALMANAC</color></b> to equip your new Safety Vest.");
        openAlmanacStep.OnStepStart.AddListener(PrepareOpenAlmanacStep);

        openEditPlayerStep = CreateVestEquipStep(
            "Tap <b><#E09500>EDIT PLAYER</color></b> on your profile to open the wardrobe.");
        openEditPlayerStep.OnStepStart.AddListener(PrepareEditPlayerStep);

        selectSafetyVestStep = CreateVestEquipStep(
            "In Accessories, select the <b><#E09500>SAFETY VEST</color></b> to wear it.");
        selectSafetyVestStep.OnStepStart.AddListener(PrepareSelectSafetyVestStep);

        saveSafetyVestStep = CreateVestEquipStep(
            "Tap <b><#E09500>SAVE LOOK</color></b> to keep the Safety Vest equipped.");
        saveSafetyVestStep.OnStepStart.AddListener(PrepareSaveSafetyVestStep);
    }

    private static TutorialStep CreateVestEquipStep(string message)
    {
        return new TutorialStep
        {
            message = message,
            screenPosition = TutorialPosition.Left,
            showNextButton = false,
            canSkip = false,
            usePointer = true,
            pointerOffset = new Vector2(0f, 80f),
            pointerRotation = 180f
        };
    }

    private void PrepareOpenAlmanacStep()
    {
        GameObject button = AlmanacManager.Instance != null
            ? AlmanacManager.Instance.hudOpenButton : null;
        PointStepAt(openAlmanacStep, button != null
            ? button.GetComponent<RectTransform>() : null);
    }

    private void PrepareEditPlayerStep()
    {
        Button button = AlmanacManager.Instance != null
            ? AlmanacManager.Instance.EditPlayerButton : null;
        PointStepAt(openEditPlayerStep, button != null
            ? button.GetComponent<RectTransform>() : null);
    }

    private void PrepareSelectSafetyVestStep()
    {
        CharacterCustomizationController controller = GetCustomizationController();
        if (controller != null && controller.IsCustomizationReady)
            controller.ShowCategory(CosmeticCategory.Accessories);
        PointStepAt(selectSafetyVestStep, controller != null
            ? controller.GetCosmeticOptionTarget(SafetyVestCosmeticId) : null);
    }

    private void PrepareSaveSafetyVestStep()
    {
        CharacterCustomizationController controller = GetCustomizationController();
        PointStepAt(saveSafetyVestStep, controller != null
            ? controller.GetSaveLookTarget() : null);
    }

    private static void PointStepAt(TutorialStep step, RectTransform target)
    {
        if (step == null) return;
        step.pointerTarget = target;
        step.usePointer = target != null;
    }

    private CharacterCustomizationController GetCustomizationController()
    {
        if (customizationController != null) return customizationController;
        AlmanacManager almanac = AlmanacManager.Instance;
        if (almanac != null && almanac.Panel != null)
            customizationController = almanac.Panel.GetComponentInChildren<CharacterCustomizationController>(true);
        if (customizationController == null)
            customizationController = FindObjectOfType<CharacterCustomizationController>(true);
        return customizationController;
    }

    private static bool IsSafetyVestSavedEquipped()
    {
        PlayerDataManager dataManager = PlayerDataManager.Instance;
        return dataManager != null && dataManager.CurrentData != null &&
               dataManager.CurrentData.cosmeticLoadout != null &&
               dataManager.CurrentData.cosmeticLoadout.IsAccessoryEquipped(SafetyVestCosmeticId);
    }

    private void MonitorVestEquipTutorial()
    {
        TutorialManager tutorial = TutorialManager.Instance;
        if (tutorial == null || !tutorial.IsPlayingSequence(onOpenTutorial)) return;

        TutorialStep step = tutorial.CurrentStep;
        if (step != openAlmanacStep && step != openEditPlayerStep &&
            step != selectSafetyVestStep && step != saveSafetyVestStep)
            return;

        AlmanacManager almanac = AlmanacManager.Instance;
        CharacterCustomizationController controller = GetCustomizationController();
        if (step == openAlmanacStep)
        {
            if (almanac != null && almanac.IsOpenAndReady)
                tutorial.ShowNextStep();
            return;
        }

        if (step == saveSafetyVestStep && IsSafetyVestSavedEquipped())
        {
            // Saving persists immediately, but let the wardrobe's return animation
            // finish before completing the tutorial and releasing queued guides.
            if (controller == null || !controller.IsCustomizationOpen)
                tutorial.ShowNextStep();
            return;
        }

        if (almanac == null || !almanac.IsOpenAndReady)
        {
            ReturnToVestEquipStep(tutorial, openAlmanacStep);
            return;
        }

        if (step == openEditPlayerStep)
        {
            if (controller != null && controller.IsCustomizationReady)
                tutorial.ShowNextStep();
            return;
        }

        if (controller == null) return;
        if (!controller.IsCustomizationOpen)
        {
            ReturnToVestEquipStep(tutorial, openEditPlayerStep);
            return;
        }
        if (!controller.IsCustomizationReady) return;

        if (step == selectSafetyVestStep)
        {
            if (controller.CurrentCategory != CosmeticCategory.Accessories)
                PrepareSelectSafetyVestStep();
            if (controller.IsAccessorySelectedInPreview(SafetyVestCosmeticId))
                tutorial.ShowNextStep();
        }
        else if (!controller.IsAccessorySelectedInPreview(SafetyVestCosmeticId))
        {
            ReturnToVestEquipStep(tutorial, selectSafetyVestStep);
        }
    }

    private void ReturnToVestEquipStep(TutorialManager tutorial, TutorialStep target)
    {
        if (tutorial == null || onOpenTutorial == null || onOpenTutorial.tutorialSteps == null)
            return;
        int index = Array.IndexOf(onOpenTutorial.tutorialSteps, target);
        if (index >= 0) tutorial.ReturnToStep(index);
    }

    private void PrepareSafetyVestPurchaseStep()
    {
        ShowAccessories();

        safetyVestPurchaseStep.usePointer = false;
        safetyVestPurchaseStep.pointerTarget = null;
        foreach (ShopItemUI card in cardPool)
        {
            if (card == null || card.Item == null || card.Item.ItemId != SafetyVestItemId ||
                !card.gameObject.activeInHierarchy)
                continue;

            RectTransform target = card.PurchaseButtonTarget;
            if (target == null) target = card.GetComponent<RectTransform>();
            safetyVestPurchaseStep.pointerTarget = target;
            safetyVestPurchaseStep.usePointer = target != null;
            ScrollCardIntoView(card.GetComponent<RectTransform>());
            break;
        }

        if (boundPlayerData != null && boundPlayerData.OwnsShopItem(SafetyVestItemId) &&
            ownedVestAdvanceCoroutine == null)
            ownedVestAdvanceCoroutine = StartCoroutine(AdvanceOwnedVestStep());
    }

    private IEnumerator AdvanceOwnedVestStep()
    {
        yield return null;
        ownedVestAdvanceCoroutine = null;
        TutorialManager tutorial = TutorialManager.Instance;
        if (tutorial != null && tutorial.IsPlayingSequence(onOpenTutorial) &&
            tutorial.CurrentStep == safetyVestPurchaseStep)
            tutorial.ShowNextStep();
    }

    private void ScrollCardIntoView(RectTransform card)
    {
        if (card == null || itemGridContent == null) return;
        ScrollRect scroll = itemGridContent.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null) return;

        RectTransform viewport = scroll.viewport != null
            ? scroll.viewport : scroll.GetComponent<RectTransform>();
        if (viewport == null) return;

        Canvas.ForceUpdateCanvases();
        Bounds cardBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, card);
        Rect visible = viewport.rect;
        float offset = cardBounds.max.y > visible.yMax
            ? cardBounds.max.y - visible.yMax
            : cardBounds.min.y < visible.yMin
                ? cardBounds.min.y - visible.yMin
                : 0f;
        if (Mathf.Approximately(offset, 0f)) return;

        scroll.StopMovement();
        scroll.content.anchoredPosition -= new Vector2(0f, offset);
        Canvas.ForceUpdateCanvases();
    }

    public void ShowCategory(ShopCategory category)
    {
        InitializeIfNeeded();
        currentCategory = category;

        List<ShopItemData> filtered = new List<ShopItemData>();
        HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (ShopItemData item in allItems)
        {
            if (item == null || item.cosmeticDefinition == null ||
                item.category != category || string.IsNullOrWhiteSpace(item.ItemId))
                continue;

            if (!seenIds.Add(item.ItemId))
            {
                Debug.LogWarning($"[ShopManager] Duplicate Shop Item ID '{item.ItemId}' was skipped.", item);
                continue;
            }

            filtered.Add(item);
        }

        filtered.Sort((left, right) =>
        {
            if (sortItemsByPrice)
            {
                int priceComparison = left.price.CompareTo(right.price);
                if (priceComparison != 0) return priceComparison;
            }

            return string.Compare(left.itemName, right.itemName, StringComparison.OrdinalIgnoreCase);
        });

        EnsureCardCapacity(filtered.Count);
        for (int i = 0; i < cardPool.Count; i++)
        {
            bool visible = i < filtered.Count;
            ShopItemUI card = cardPool[i];
            if (card == null) continue;

            card.gameObject.SetActive(visible);
            if (!visible) continue;

            card.transform.SetSiblingIndex(i);
            card.Bind(filtered[i], this);
        }

        if (itemGridContent is RectTransform contentRect)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }

        RefreshTabVisuals();
    }

    public void ShowCategory(int categoryIndex)
    {
        if (Enum.IsDefined(typeof(ShopCategory), categoryIndex))
            ShowCategory((ShopCategory)categoryIndex);
    }

    public void ShowAccessories() => ShowCategory(ShopCategory.Accessory);
    public void ShowHairstyles() => ShowCategory(ShopCategory.Hairstyle);
    public void ShowShirts() => ShowCategory(ShopCategory.Shirt);
    public void ShowPants() => ShowCategory(ShopCategory.Pants);
    public void ShowShoes() => ShowCategory(ShopCategory.Shoes);

    public bool TryPurchase(ShopItemData item)
    {
        BindPlayerData();

        if (!CanPurchase(item, out string rejection))
            return RejectPurchase(rejection);

        if (purchaseConfirmationPanel == null)
            return CompletePurchase(item);

        pendingPurchase = item;
        PopulateConfirmation(item);
        purchaseConfirmationPanel.SetActive(true);
        purchaseConfirmationPanel.transform.SetAsLastSibling();
        UpdateTutorialPresentationForModal();
        return true;
    }

    public void ConfirmPendingPurchase()
    {
        if (pendingPurchase == null)
        {
            CancelPendingPurchase();
            return;
        }

        ShopItemData item = pendingPurchase;
        if (!CompletePurchase(item)) return;

        pendingPurchase = null;
        if (purchaseConfirmationPanel != null)
            purchaseConfirmationPanel.SetActive(false);
        UpdateTutorialPresentationForModal();
    }

    public void CancelPendingPurchase()
    {
        pendingPurchase = null;
        if (purchaseConfirmationPanel != null)
            purchaseConfirmationPanel.SetActive(false);
        UpdateTutorialPresentationForModal();
    }

    public void HidePurchaseFeedback()
    {
        if (purchaseFeedbackPanel != null)
            purchaseFeedbackPanel.SetActive(false);
        UpdateTutorialPresentationForModal();
    }

    private void UpdateTutorialPresentationForModal()
    {
        TutorialManager tutorial = TutorialManager.Instance;
        if (tutorial == null || onOpenTutorial == null) return;

        bool modalOpen = (purchaseConfirmationPanel != null &&
                          purchaseConfirmationPanel.activeInHierarchy) ||
                         (purchaseFeedbackPanel != null &&
                          purchaseFeedbackPanel.activeInHierarchy);
        tutorial.SetPresentationSuppressed(onOpenTutorial, modalOpen);
    }

    public void HandleAddCurrencyClicked()
    {
        Debug.Log("[ShopManager] Secondary currency purchase/reward flow is not configured yet.", this);
        onAddCurrencyClicked.Invoke();
    }

    public bool IsOwned(ShopItemData item)
    {
        BindPlayerData();
        return item != null && boundPlayerData != null && boundPlayerData.OwnsShopItem(item.ItemId);
    }

    public string FormatPrice(int price)
    {
        return $"{currencyPrefix}{Mathf.Max(0, price):N0}";
    }

    public void UpdateCurrencyDisplay()
    {
        BindPlayerData();

        int amount = boundPlayerData != null && boundPlayerData.CurrentData != null
            ? boundPlayerData.CurrentData.gold
            : 0;
        if (currencyText != null)
            currencyText.text = $"{currencyPrefix}{amount:N0}";
        if (secondaryCurrencyText != null)
            secondaryCurrencyText.text = secondaryCurrencyPlaceholder;
    }

    private void InitializeIfNeeded()
    {
        if (initialized) return;
        initialized = true;
        currentCategory = defaultCategory;

        string[] categoryLabels = { "ACCESSORY", "HAIRSTYLE", "SHIRT", "PANTS", "SHOES" };

        for (int i = 0; i < categoryTabs.Count; i++)
        {
            ShopCategoryTab tab = categoryTabs[i];
            if (tab == null || tab.button == null || tabListeners.ContainsKey(tab.button))
                continue;

            bool validCategory = i < categoryLabels.Length &&
                                 Enum.IsDefined(typeof(ShopCategory), tab.category);
            tab.button.gameObject.SetActive(validCategory);
            if (!validCategory) continue;

            TMP_Text label = tab.button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = categoryLabels[(int)tab.category];

            ShopCategory capturedCategory = tab.category;
            UnityAction action = () => ShowCategory(capturedCategory);
            tab.button.onClick.AddListener(action);
            tabListeners.Add(tab.button, action);
        }
    }

    private void EnsureCardCapacity(int requiredCount)
    {
        if (requiredCount <= cardPool.Count) return;
        if (itemGridContent == null || itemCardPrefab == null)
        {
            Debug.LogError("[ShopManager] Assign both Item Grid Content and Item Card Prefab.", this);
            return;
        }

        while (cardPool.Count < requiredCount)
        {
            ShopItemUI card = Instantiate(itemCardPrefab, itemGridContent);
            card.name = $"ShopItemCard_{cardPool.Count + 1}";
            cardPool.Add(card);
        }
    }

    private void RefreshVisibleCards()
    {
        foreach (ShopItemUI card in cardPool)
        {
            if (card != null && card.gameObject.activeSelf)
                card.RefreshPurchaseState();
        }
    }

    private void RefreshTabVisuals()
    {
        foreach (ShopCategoryTab tab in categoryTabs)
        {
            if (tab == null || tab.selectedVisual == null) continue;
            tab.selectedVisual.SetActive(tab.category == currentCategory);
        }
    }

    private bool RejectPurchase(string message)
    {
        Debug.LogWarning($"[ShopManager] {message}", this);
        ShowPurchaseFeedback(message);
        onPurchaseRejected.Invoke(message);
        return false;
    }

    private void ShowPurchaseFeedback(string message)
    {
        CancelPendingPurchase();

        if (purchaseFeedbackPanel == null)
            return;

        if (purchaseFeedbackTitleText != null)
            purchaseFeedbackTitleText.text = "PURCHASE UNAVAILABLE";
        if (purchaseFeedbackMessageText != null)
            purchaseFeedbackMessageText.text = message;

        purchaseFeedbackPanel.SetActive(true);
        purchaseFeedbackPanel.transform.SetAsLastSibling();
        UpdateTutorialPresentationForModal();
    }

    private bool CanPurchase(ShopItemData item, out string rejection)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
        {
            rejection = "This shop item is not configured correctly.";
            return false;
        }

        if (boundPlayerData == null || boundPlayerData.CurrentData == null)
        {
            rejection = "Player save data is unavailable.";
            return false;
        }

        if (!item.canPurchaseMultipleTimes && boundPlayerData.OwnsShopItem(item.ItemId))
        {
            rejection = $"{item.itemName} is already owned.";
            return false;
        }

        if (boundPlayerData.CurrentData.gold < item.price)
        {
            rejection = $"You do not have enough money for {item.itemName}.\n" +
                        $"You have {FormatPrice(boundPlayerData.CurrentData.gold)}, " +
                        $"but it costs {FormatPrice(item.price)}.";
            return false;
        }

        rejection = string.Empty;
        return true;
    }

    private bool CompletePurchase(ShopItemData item)
    {
        BindPlayerData();
        if (!CanPurchase(item, out string rejection))
            return RejectPurchase(rejection);

        if (!boundPlayerData.TryPurchaseShopItem(
                item.ItemId,
                item.price,
                item.canPurchaseMultipleTimes))
        {
            return RejectPurchase($"Could not purchase {item.itemName}.");
        }

        if (item.cosmeticDefinition != null &&
            !string.IsNullOrWhiteSpace(item.cosmeticDefinition.PermanentID))
        {
            boundPlayerData.UnlockCosmeticReward(item.cosmeticDefinition.PermanentID, false);
        }

        Debug.Log($"[ShopManager] Purchased '{item.itemName}' for {FormatPrice(item.price)}.", item);
        onItemPurchased.Invoke(item);
        if (item.ItemId == SafetyVestItemId && ownedVestAdvanceCoroutine == null &&
            TutorialManager.Instance != null &&
            TutorialManager.Instance.IsPlayingSequence(onOpenTutorial) &&
            TutorialManager.Instance.CurrentStep == safetyVestPurchaseStep)
            ownedVestAdvanceCoroutine = StartCoroutine(AdvanceOwnedVestStep());
        RefreshVisibleCards();
        UpdateCurrencyDisplay();
        return true;
    }

    private void PopulateConfirmation(ShopItemData item)
    {
        if (confirmationTitleText != null)
            confirmationTitleText.text = $"BUY {item.itemName.ToUpperInvariant()}?";
        if (confirmationDescriptionText != null)
            confirmationDescriptionText.text = item.description;
        if (confirmationPriceText != null)
            confirmationPriceText.text = FormatPrice(item.price);
        if (confirmationIconImage != null)
        {
            Sprite displayIcon = item.cosmeticDefinition != null && item.cosmeticDefinition.icon != null
                ? item.cosmeticDefinition.icon
                : item.icon;
            confirmationIconImage.sprite = displayIcon;
            confirmationIconImage.color = Color.white;
            confirmationIconImage.preserveAspect = true;
            confirmationIconImage.enabled = displayIcon != null;
        }
    }

    private void BindPlayerData()
    {
        PlayerDataManager current = PlayerDataManager.Instance;
        if (boundPlayerData == current) return;

        UnbindPlayerData();
        boundPlayerData = current;
        if (boundPlayerData != null)
        {
            boundPlayerData.OnCurrencyChanged += HandleCurrencyChanged;
            boundPlayerData.OnItemOwnershipChanged += RefreshVisibleCards;
        }
    }

    private void UnbindPlayerData()
    {
        if (boundPlayerData != null)
        {
            boundPlayerData.OnCurrencyChanged -= HandleCurrencyChanged;
            boundPlayerData.OnItemOwnershipChanged -= RefreshVisibleCards;
        }
        boundPlayerData = null;
    }

    private void HandleCurrencyChanged()
    {
        UpdateCurrencyDisplay();
        RefreshVisibleCards();
    }
}
