using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class TutorialTrigger : MonoBehaviour
{
    private bool hasTriggered = false;

    private void Awake()
    {
        // Automatically ensures the collider doesn't block player movement
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // When the player walks into this invisible box
        if (other.CompareTag("Player") && !hasTriggered)
        {
            hasTriggered = true;

            // Call YOUR existing manager to advance the step!
            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.ShowNextStep();
            }

            // Turn off this trigger so it never fires again
            gameObject.SetActive(false);
        }
    }
}