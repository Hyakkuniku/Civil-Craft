using UnityEngine;

public class MoveTutorialTrigger : MonoBehaviour
{
    private bool hasTriggered = false;
    private Vector3 lastPosition;

    private void Start()
    {
        // Remember starting position
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            lastPosition = player.transform.position;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (hasTriggered) return;
        if (!other.CompareTag("Player")) return;

        // Check if player actually moved a little bit
        Vector3 currentPos = other.transform.position;
        float distanceMoved = Vector3.Distance(currentPos, lastPosition);

        if (distanceMoved > 0.15f) // moved more than ~15 cm
        {
            hasTriggered = true;
            Debug.Log("Movement detected → advancing tutorial");

            var tutorial = TutorialManager.Instance;
            if (tutorial != null)
            {
                tutorial.ShowNextStep();
            }
            else
            {
                Debug.LogWarning("TutorialManager.Instance is null!");
            }
        }

        lastPosition = currentPos; // update for next frame
    }
}