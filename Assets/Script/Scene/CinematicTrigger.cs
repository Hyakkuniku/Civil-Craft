using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class CinematicTrigger : MonoBehaviour
{
    [Tooltip("Drag the CinematicDirector you want to play into this slot.")]
    public CinematicDirector cinematicToPlay;

    private void Awake()
    {
        // Ensure the collider is set to Trigger so the player walks through it
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && cinematicToPlay != null)
        {
            // Uncheck "Play On Start" on your CinematicDirector if you use this!
            cinematicToPlay.PlayCinematic();
            
            // Turn off the trigger zone so the cutscene doesn't loop if they walk back
            gameObject.SetActive(false); 
        }
    }
}