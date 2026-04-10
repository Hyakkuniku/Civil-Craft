using UnityEngine;

public class AlmanacPickup : Interactable
{
    public bool advancesTutorial = false;

    private void Start()
    {
        // If the player already owns the Almanac, hide the 3D book in the world
        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.hasAlmanac)
            gameObject.SetActive(false);
    }

    protected override void Intract() 
    {
        if (PlayerDataManager.Instance != null)
        {
            // 1. Unlock it in the save file
            PlayerDataManager.Instance.UnlockAlmanac();
            
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
}