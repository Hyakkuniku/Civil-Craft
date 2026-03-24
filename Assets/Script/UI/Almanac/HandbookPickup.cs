using UnityEngine;

public class HandbookPickup : Interactable
{
    private void Start()
    {
        // Set the text that pops up when you look at it
        promptMessage = "Pick up Builder's Handbook";
    }

    protected override void Intract() 
    {
        // 1. Tell the manager we now own the book
        if (BridgeHandbookManager.Instance != null)
        {
            BridgeHandbookManager.Instance.AcquireBook();
            
            // 2. Remove the 3D model from the world so we can't pick it up twice
            gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("Could not find the BridgeHandbookManager in the scene!");
        }
    }
}