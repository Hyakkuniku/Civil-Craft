using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic; // --- NEW: Required for using Lists ---

public class ItemUnlockUI : MonoBehaviour
{
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

    private void Awake()
    {
        Instance = this;
        if (popupPanel != null) popupPanel.SetActive(false);

        // Automatically hook up the Collect button!
        if (collectButton != null)
        {
            collectButton.onClick.AddListener(OnCollectClicked);
        }
    }

    public void ShowReward(string itemName, Sprite itemIcon, string hatID, System.Action onCollect)
    {
        pendingHatID = hatID;
        onCollectCallback = onCollect;

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
        if (itemNameText != null) itemNameText.text = "Unlocked:\n" + itemName;
        if (itemIconImage != null && itemIcon != null) itemIconImage.sprite = itemIcon;

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
        onCollectCallback?.Invoke();
    }
}