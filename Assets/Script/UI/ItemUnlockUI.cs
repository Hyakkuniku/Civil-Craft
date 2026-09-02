using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ItemUnlockUI : MonoBehaviour
{
    private sealed class RewardRequest
    {
        public string itemName;
        public Sprite itemIcon;
        public string hatID;
        public System.Action onCollect;
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
    private bool collectButtonBound;
    private bool rewardVisible;
    private readonly Queue<RewardRequest> pendingRewards = new Queue<RewardRequest>();

    private void Awake()
    {
        Instance = this;
        if (popupPanel != null) popupPanel.SetActive(false);
        BindCollectButton();
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
            onCollect = onCollect
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

        // Set the visuals
        if (itemNameText != null) itemNameText.text = "Unlocked:\n" + request.itemName;
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

        // Equip the hat!
        if (!string.IsNullOrEmpty(pendingHatID) && PlayerCosmetics.Instance != null)
        {
            PlayerCosmetics.Instance.UnlockAndEquipHat(pendingHatID);
        }

        // Trigger whatever was supposed to happen next (like fireworks or advancing the tutorial)
        System.Action callback = onCollectCallback;
        onCollectCallback = null;
        pendingHatID = string.Empty;
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
        collectButtonBound = true;
    }

}
