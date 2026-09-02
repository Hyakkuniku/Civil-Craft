using UnityEngine;

public class AlmanacPickup : Interactable
{
    [Header("Tutorial Settings")]
    public bool advancesTutorial = false;

    [Header("UI Reward Integration")]
    [Tooltip("The name of the item shown on the UI popup")]
    public string rewardDisplayName = "Engineering Almanac";
    [Tooltip("The 2D picture of the book for the UI popup")]
    public Sprite rewardSprite; 

    private bool collectionPending;

    private void Start()
    {
        // If the player already owns the Almanac, hide the 3D book in the world
        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.hasAlmanac)
            gameObject.SetActive(false);
    }

    protected override void Intract() 
    {
        if (collectionPending) return;

        if (PlayerDataManager.Instance != null)
        {
            collectionPending = true;
            if (ItemUnlockUI.Instance != null)
            {
                // We pass an empty string "" for the hatID because this is a book, not a hat!
                // The code inside the { } runs ONLY after they click the "Collect" button.
                ItemUnlockUI.Instance.ShowReward(rewardDisplayName, rewardSprite, "", () => 
                {
                    CompletePickup();
                });
            }
            else
            {
                // Fallback just in case you test a scene without the UI Canvas
                CompletePickup();
            }
        }
    }

    private void CompletePickup()
    {
        collectionPending = false;

        // 1. Unlock it in the save file
        bool wasAlreadyUnlocked = PlayerDataManager.Instance.CurrentData.hasAlmanac;
        PlayerDataManager.Instance.UnlockAlmanac();
        if (!wasAlreadyUnlocked)
            AchievementPopupNotification.NotifyFeatureUnlock(rewardDisplayName, rewardSprite);
        
        // 2. Advance the tutorial if this pickup is part of one
        if (advancesTutorial && TutorialManager.Instance != null)
        {
            TutorialManager.Instance.ShowNextStep();
        }
        
        Debug.Log("<color=orange>You found the Almanac!</color>");

        // 3. Hide the physical book from the scene
        gameObject.SetActive(false);
    }
}
