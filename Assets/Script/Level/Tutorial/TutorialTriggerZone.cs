using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class TutorialTriggerZone : MonoBehaviour
{
    [Tooltip("Drag the TutorialSequence object you want to play when the player enters this box")]
    public TutorialSequence sequenceToPlay;

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && sequenceToPlay != null)
        {
            sequenceToPlay.TryStartTutorial();
            
            // Turn off the trigger so it doesn't spam
            gameObject.SetActive(false); 
        }
    }
}