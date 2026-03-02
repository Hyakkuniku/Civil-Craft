using UnityEngine;

public class DeliveryZone : MonoBehaviour
{
    private bool hasDelivered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (hasDelivered) return;

        // Check if the object entering the zone is the Cargo
        CargoItem cargo = other.GetComponentInParent<CargoItem>();
        
        if (cargo != null)
        {
            hasDelivered = true;
            Debug.Log("<color=cyan>Cargo Delivered to Zone!</color>");
            
            // Tell the UI to show the Complete Button
            if (ObjectiveTrackerUI.Instance != null)
            {
                ObjectiveTrackerUI.Instance.ShowCompleteButton();
            }

            // REMOVED: cargo.Drop(); 
            // Now the player will just keep holding it until they press the interact button again!
        }
    }
}