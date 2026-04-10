using UnityEngine;

public class AlmanacPickup : Interactable
{
    public bool advancesTutorial = false;

    private void Start()
    {
        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData.hasAlmanac)
            gameObject.SetActive(false);
    }

    protected override void Intract() 
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.UnlockAlmanac();
            
            if (advancesTutorial && TutorialManager.Instance != null)
            {
                TutorialManager.Instance.ShowNextStep();
            }
            
            Debug.Log("<color=orange>You found the Almanac!</color>");

            // --- THE FIX: Auto-open the book using your AlmanacManager! ---
            if (AlmanacManager.Instance != null)
            {
                AlmanacManager.Instance.OpenAlmanac();
            }

            gameObject.SetActive(false);
        }
    }
}