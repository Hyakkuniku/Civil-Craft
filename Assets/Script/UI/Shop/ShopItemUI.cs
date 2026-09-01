using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ShopItemUI : MonoBehaviour
{
    [Header("Card References")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Button buyButton;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private GameObject ownedBadge;

    private ShopManager owner;
    private ShopItemData item;

    public ShopItemData Item => item;

    private void Awake()
    {
        if (buyButton != null)
            buyButton.onClick.AddListener(HandleBuyClicked);
    }

    private void OnDestroy()
    {
        if (buyButton != null)
            buyButton.onClick.RemoveListener(HandleBuyClicked);
    }

    public void Bind(ShopItemData data, ShopManager shopManager)
    {
        item = data;
        owner = shopManager;

        if (titleText != null)
            titleText.text = item != null ? item.itemName : "Missing Item";
        if (descriptionText != null)
            descriptionText.text = item != null ? item.description : string.Empty;

        if (iconImage != null)
        {
            iconImage.sprite = item != null ? item.icon : null;
            // The setup prefab uses an empty Image before an item is bound.
            // Always restore an opaque tint when a real catalog icon is assigned.
            iconImage.color = Color.white;
            iconImage.enabled = iconImage.sprite != null;
            iconImage.preserveAspect = true;
        }

        RefreshPurchaseState();
    }

    public void RefreshPurchaseState()
    {
        bool valid = item != null && owner != null;
        bool owned = valid && owner.IsOwned(item) && !item.canPurchaseMultipleTimes;

        if (ownedBadge != null)
            ownedBadge.SetActive(owned);

        if (priceText != null)
        {
            priceText.text = owned
                ? "OWNED"
                : owner != null && item != null
                    ? owner.FormatPrice(item.price)
                    : "--";
        }

        if (buyButton != null)
            buyButton.interactable = valid && !owned;
    }

    private void HandleBuyClicked()
    {
        if (owner != null && item != null)
            owner.TryPurchase(item);
    }
}
