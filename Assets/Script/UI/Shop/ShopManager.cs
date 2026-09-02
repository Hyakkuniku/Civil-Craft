using System;
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
    public static ShopManager Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private string currencyPrefix = "₱";
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
    [SerializeField] private ShopCategory defaultCategory = ShopCategory.Builder;
    [SerializeField] private bool sortItemsByPrice;

    [Header("Grid")]
    [SerializeField] private Transform itemGridContent;
    [SerializeField] private ShopItemUI itemCardPrefab;

    [Header("Category Tabs")]
    [SerializeField] private List<ShopCategoryTab> categoryTabs = new List<ShopCategoryTab>();

    [Header("Purchase Events")]
    public ShopItemEvent onItemPurchased = new ShopItemEvent();
    public ShopMessageEvent onPurchaseRejected = new ShopMessageEvent();
    public UnityEvent onAddCurrencyClicked = new UnityEvent();

    private readonly List<ShopItemUI> cardPool = new List<ShopItemUI>();
    private readonly Dictionary<Button, UnityAction> tabListeners = new Dictionary<Button, UnityAction>();
    private ShopCategory currentCategory;
    private PlayerDataManager boundPlayerData;
    private ShopItemData pendingPurchase;
    private bool initialized;

    public GameObject Panel => shopPanel;
    public IReadOnlyList<ShopItemData> AllItems => allItems;
    public ShopCategory CurrentCategory => currentCategory;

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
            if (item != null) discovered.Add(item);
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

    private void OnDestroy()
    {
        UnbindPlayerData();

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
    }

    public void CloseShop()
    {
        if (shopPanel == null) return;

        CancelPendingPurchase();
        HidePurchaseFeedback();

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.ClosePanel(shopPanel);
        else
            shopPanel.SetActive(false);
    }

    public void ShowCategory(ShopCategory category)
    {
        InitializeIfNeeded();
        currentCategory = category;

        List<ShopItemData> filtered = new List<ShopItemData>();
        HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (ShopItemData item in allItems)
        {
            if (item == null || item.category != category || string.IsNullOrWhiteSpace(item.ItemId))
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

    public void ShowBuilder() => ShowCategory(ShopCategory.Builder);
    public void ShowMaterials() => ShowCategory(ShopCategory.Materials);
    public void ShowTools() => ShowCategory(ShopCategory.Tools);
    public void ShowDecorations() => ShowCategory(ShopCategory.Decorations);
    public void ShowVehicles() => ShowCategory(ShopCategory.Vehicles);
    public void ShowBundles() => ShowCategory(ShopCategory.Bundles);

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
    }

    public void CancelPendingPurchase()
    {
        pendingPurchase = null;
        if (purchaseConfirmationPanel != null)
            purchaseConfirmationPanel.SetActive(false);
    }

    public void HidePurchaseFeedback()
    {
        if (purchaseFeedbackPanel != null)
            purchaseFeedbackPanel.SetActive(false);
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

        foreach (ShopCategoryTab tab in categoryTabs)
        {
            if (tab == null || tab.button == null || tabListeners.ContainsKey(tab.button))
                continue;

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

        Debug.Log($"[ShopManager] Purchased '{item.itemName}' for {FormatPrice(item.price)}.", item);
        onItemPurchased.Invoke(item);
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
            confirmationIconImage.sprite = item.icon;
            confirmationIconImage.color = Color.white;
            confirmationIconImage.preserveAspect = true;
            confirmationIconImage.enabled = item.icon != null;
        }
    }

    private void BindPlayerData()
    {
        PlayerDataManager current = PlayerDataManager.Instance;
        if (boundPlayerData == current) return;

        UnbindPlayerData();
        boundPlayerData = current;
        if (boundPlayerData != null)
            boundPlayerData.OnCurrencyChanged += HandleCurrencyChanged;
    }

    private void UnbindPlayerData()
    {
        if (boundPlayerData != null)
            boundPlayerData.OnCurrencyChanged -= HandleCurrencyChanged;
        boundPlayerData = null;
    }

    private void HandleCurrencyChanged()
    {
        UpdateCurrencyDisplay();
        RefreshVisibleCards();
    }
}
