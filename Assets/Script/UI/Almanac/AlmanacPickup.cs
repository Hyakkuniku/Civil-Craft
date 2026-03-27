using UnityEngine;

public class AlmanacPickup : Interactable
{
    private void Start()
    {
        // If the player loads the game and already owns it, hide the 3D model immediately
        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.hasAlmanac)
        {
            gameObject.SetActive(false);
        }
    }

    // Triggers when the player walks up and presses the interact button
    protected override void Intract() 
    {
        if (PlayerDataManager.Instance != null)
        {
            // Tell the save manager we found it
            PlayerDataManager.Instance.UnlockAlmanac();
            
            // Log it and vanish from the 3D world!
            Debug.Log("<color=orange>You found the Almanac!</color>");
            gameObject.SetActive(false);
        }
    }
}