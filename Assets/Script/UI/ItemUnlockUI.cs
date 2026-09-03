using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text;

public class ItemUnlockUI : MonoBehaviour
{
    private sealed class RewardRequest
    {
        public string itemName;
        public Sprite itemIcon;
        public string hatID;
        public System.Action onCollect;
        public string detailsText;
        public string buttonLabel;
        public bool useMaterialLayout;
        public BridgeMaterialSO materialToDiscover;
    }

    public static ItemUnlockUI Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("The main background panel of the popup")]
    public GameObject popupPanel;
    [Tooltip("The text that says the name of the item")]
    public TextMeshProUGUI itemNameText;
    [Tooltip("The image that shows a picture of the item")]
    public Image itemIconImage;
    [Tooltip("The Collect button at the bottom")]
    public Button collectButton;

    // --- NEW: UI Hiding Logic ---
    [Header("HUD Management")]
    [Tooltip("Drag UI elements here (like the Minimap or HUD Canvas) that should hide while this is open.")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();
    private List<GameObject> temporarilyHiddenUI = new List<GameObject>();

    private string pendingHatID;
    private System.Action onCollectCallback;
    private BridgeMaterialSO pendingMaterialToDiscover;
    private bool collectButtonBound;
    private bool rewardVisible;
    private readonly Queue<RewardRequest> pendingRewards = new Queue<RewardRequest>();
    private TextMeshProUGUI collectButtonText;

    private RectTransform itemTextRect;
    private RectTransform iconContainerRect;
    private Vector2 defaultTextAnchorMin;
    private Vector2 defaultTextAnchorMax;
    private Vector2 defaultTextPosition;
    private Vector2 defaultTextSize;
    private Vector2 defaultTextPivot;
    private Vector2 defaultIconAnchorMin;
    private Vector2 defaultIconAnchorMax;
    private Vector2 defaultIconPosition;
    private Vector2 defaultIconSize;
    private Vector2 defaultIconPivot;
    private TextAlignmentOptions defaultTextAlignment;
    private FontStyles defaultFontStyle;
    private bool defaultAutoSizing;
    private float defaultFontSize;
    private float defaultFontSizeMin;
    private float defaultFontSizeMax;
    private bool layoutCached;

    private void Awake()
    {
        Instance = this;
        if (popupPanel != null) popupPanel.SetActive(false);
        BindCollectButton();
        CacheDefaultLayout();
    }

    private void OnDestroy()
    {
        if (collectButtonBound && collectButton != null)
            collectButton.onClick.RemoveListener(OnCollectClicked);
        if (Instance == this) Instance = null;

    }

    public void ShowReward(string itemName, Sprite itemIcon, string hatID, System.Action onCollect)
    {
        pendingRewards.Enqueue(new RewardRequest
        {
            itemName = itemName,
            itemIcon = itemIcon,
            hatID = hatID,
            onCollect = onCollect,
            buttonLabel = "COLLECT"
        });

        if (!rewardVisible)
            ShowNextReward();
    }

    /// <summary>
    /// Shows player-facing details for a bridge material. Acknowledging it records
    /// the material in the Almanac; it does not spend currency or unlock build use.
    /// </summary>
    public void ShowMaterialIntroduction(
        BridgeMaterialSO material,
        System.Action onDismiss = null,
        string buttonLabel = "GOT IT")
    {
        if (material == null)
        {
            Debug.LogWarning("[ItemUnlockUI] Cannot introduce a missing BridgeMaterialSO.", this);
            onDismiss?.Invoke();
            return;
        }

        pendingRewards.Enqueue(new RewardRequest
        {
            itemName = material.GetDisplayName(),
            itemIcon = material.materialIcon,
            hatID = string.Empty,
            onCollect = onDismiss,
            detailsText = BuildMaterialDetails(material),
            buttonLabel = string.IsNullOrWhiteSpace(buttonLabel) ? "GOT IT" : buttonLabel.Trim().ToUpperInvariant(),
            useMaterialLayout = true,
            materialToDiscover = material
        });

        if (!rewardVisible)
            ShowNextReward();
    }

    private void ShowNextReward()
    {
        if (pendingRewards.Count == 0) return;

        RewardRequest request = pendingRewards.Dequeue();
        pendingHatID = request.hatID;
        onCollectCallback = request.onCollect;
        pendingMaterialToDiscover = request.materialToDiscover;
        rewardVisible = true;

        // --- NEW: Hide background UI ---
        temporarilyHiddenUI.Clear();
        foreach (GameObject ui in uiElementsToHide)
        {
            // Only remember and hide it if it was actually turned on!
            if (ui != null && ui.activeSelf)
            {
                temporarilyHiddenUI.Add(ui);
                ui.SetActive(false);
            }
        }

        // Set the visuals. Material introductions use the same hand-made panel,
        // but expand the text area and use a non-collecting acknowledgement button.
        if (request.useMaterialLayout)
            ApplyMaterialLayout();
        else
            RestoreDefaultLayout();

        if (itemNameText != null)
        {
            itemNameText.text = request.useMaterialLayout
                ? request.detailsText
                : "Unlocked:\n" + request.itemName;
        }

        if (collectButtonText != null)
            collectButtonText.text = string.IsNullOrWhiteSpace(request.buttonLabel)
                ? "COLLECT"
                : request.buttonLabel;
        if (itemIconImage != null)
        {
            itemIconImage.sprite = request.itemIcon;
            itemIconImage.enabled = request.itemIcon != null;
            itemIconImage.preserveAspect = true;
        }

        // Show the panel
        if (popupPanel != null) popupPanel.SetActive(true);
    }

    private void OnCollectClicked()
    {
        // Hide the panel
        if (popupPanel != null) popupPanel.SetActive(false);

        // --- NEW: Restore background UI ---
        foreach (GameObject ui in temporarilyHiddenUI)
        {
            if (ui != null) ui.SetActive(true);
        }
        temporarilyHiddenUI.Clear();

        // Cosmetic rewards are saved only after Collect. PlayerCosmetics updates
        // the visible model when it exists; the data-manager fallback still makes
        // the reward permanent when the player object is temporarily unavailable.
        if (!string.IsNullOrEmpty(pendingHatID))
        {
            if (PlayerCosmetics.Instance != null)
                PlayerCosmetics.Instance.UnlockAndEquipHat(pendingHatID);
            else if (PlayerDataManager.Instance != null)
                PlayerDataManager.Instance.UnlockCosmeticReward(pendingHatID, true);
        }

        // A material enters the Almanac only after the player acknowledges its
        // introduction. This also works for every entry in a stacked queue.
        if (pendingMaterialToDiscover != null)
            MaterialDiscoverySaveManager.Discover(pendingMaterialToDiscover);

        // Trigger whatever was supposed to happen next (like fireworks or advancing the tutorial)
        System.Action callback = onCollectCallback;
        onCollectCallback = null;
        pendingHatID = string.Empty;
        pendingMaterialToDiscover = null;
        rewardVisible = false;

        // The reward callback runs only after the panel is closed, so feature
        // notifications triggered by it naturally appear after Collect.
        callback?.Invoke();

        if (pendingRewards.Count > 0)
            ShowNextReward();
    }

    private void BindCollectButton()
    {
        if (collectButtonBound || collectButton == null) return;
        collectButton.onClick.AddListener(OnCollectClicked);
        collectButtonText = collectButton.GetComponentInChildren<TextMeshProUGUI>(true);
        collectButtonBound = true;
    }

    private void CacheDefaultLayout()
    {
        if (layoutCached || itemNameText == null || itemIconImage == null) return;

        itemTextRect = itemNameText.rectTransform;
        iconContainerRect = itemIconImage.transform.parent as RectTransform;
        if (itemTextRect == null || iconContainerRect == null) return;

        defaultTextAnchorMin = itemTextRect.anchorMin;
        defaultTextAnchorMax = itemTextRect.anchorMax;
        defaultTextPosition = itemTextRect.anchoredPosition;
        defaultTextSize = itemTextRect.sizeDelta;
        defaultTextPivot = itemTextRect.pivot;
        defaultIconAnchorMin = iconContainerRect.anchorMin;
        defaultIconAnchorMax = iconContainerRect.anchorMax;
        defaultIconPosition = iconContainerRect.anchoredPosition;
        defaultIconSize = iconContainerRect.sizeDelta;
        defaultIconPivot = iconContainerRect.pivot;
        defaultTextAlignment = itemNameText.alignment;
        defaultFontStyle = itemNameText.fontStyle;
        defaultAutoSizing = itemNameText.enableAutoSizing;
        defaultFontSize = itemNameText.fontSize;
        defaultFontSizeMin = itemNameText.fontSizeMin;
        defaultFontSizeMax = itemNameText.fontSizeMax;
        layoutCached = true;
    }

    private void ApplyMaterialLayout()
    {
        CacheDefaultLayout();
        if (!layoutCached) return;

        iconContainerRect.anchorMin = new Vector2(0.06f, 0.34f);
        iconContainerRect.anchorMax = new Vector2(0.46f, 0.76f);
        iconContainerRect.anchoredPosition = Vector2.zero;
        iconContainerRect.sizeDelta = Vector2.zero;
        iconContainerRect.pivot = new Vector2(0.5f, 0.5f);

        itemTextRect.anchorMin = new Vector2(0.50f, 0.25f);
        itemTextRect.anchorMax = new Vector2(0.94f, 0.86f);
        itemTextRect.anchoredPosition = Vector2.zero;
        itemTextRect.sizeDelta = Vector2.zero;
        itemTextRect.pivot = new Vector2(0.5f, 0.5f);
        itemNameText.alignment = TextAlignmentOptions.TopLeft;
        itemNameText.fontStyle = FontStyles.Normal;
        itemNameText.enableAutoSizing = true;
        itemNameText.fontSizeMin = 16f;
        itemNameText.fontSizeMax = 34f;
    }

    private void RestoreDefaultLayout()
    {
        CacheDefaultLayout();
        if (!layoutCached) return;

        itemTextRect.anchorMin = defaultTextAnchorMin;
        itemTextRect.anchorMax = defaultTextAnchorMax;
        itemTextRect.anchoredPosition = defaultTextPosition;
        itemTextRect.sizeDelta = defaultTextSize;
        itemTextRect.pivot = defaultTextPivot;
        iconContainerRect.anchorMin = defaultIconAnchorMin;
        iconContainerRect.anchorMax = defaultIconAnchorMax;
        iconContainerRect.anchoredPosition = defaultIconPosition;
        iconContainerRect.sizeDelta = defaultIconSize;
        iconContainerRect.pivot = defaultIconPivot;
        itemNameText.alignment = defaultTextAlignment;
        itemNameText.fontStyle = defaultFontStyle;
        itemNameText.enableAutoSizing = defaultAutoSizing;
        itemNameText.fontSize = defaultFontSize;
        itemNameText.fontSizeMin = defaultFontSizeMin;
        itemNameText.fontSizeMax = defaultFontSizeMax;
    }

    private static string BuildMaterialDetails(BridgeMaterialSO material)
    {
        StringBuilder details = new StringBuilder();
        details.Append("<size=115%><b>").Append(material.GetDisplayName()).Append("</b></size>");

        if (!string.IsNullOrWhiteSpace(material.introductionDescription))
            details.Append("\n\n").Append(material.introductionDescription.Trim());

        details.Append("\n\n<size=82%>")
            .Append("<b>Cost:</b> ").Append(material.costPerMeter.ToString("N0")).Append(" / meter")
            .Append("\n<b>Maximum length:</b> ").Append(material.maxLength.ToString("0.##")).Append(" m")
            .Append("\n<b>Tension limit:</b> ").Append(material.maxTension.ToString("N0")).Append(" N")
            .Append("\n<b>Compression limit:</b> ").Append(material.maxCompression.ToString("N0")).Append(" N")
            .Append("</size>");

        return details.ToString();
    }

}
