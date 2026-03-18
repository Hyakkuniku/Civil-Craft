using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BoxCollider))]
public class FinishLineTrigger : MonoBehaviour
{
    [Header("Completion Settings")]
    [Tooltip("Tags of objects that can trigger the win state (e.g., 'Player', 'Vehicle')")]
    public string[] acceptedTags = { "Player", "Vehicle" };

    [Header("Events")]
    public UnityEvent OnLevelCompleted;

    private bool levelCompleted = false;

    private void OnTriggerEnter(Collider other)
    {
        // Prevent triggering multiple times (like if multiple car wheels hit it)
        if (levelCompleted) return;

        foreach (string acceptedTag in acceptedTags)
        {
            if (other.CompareTag(acceptedTag))
            {
                levelCompleted = true;
                
                // Fire off any custom events (like fireworks or sounds)
                OnLevelCompleted?.Invoke();

                // Tell the UI Manager to pop up and pause the game!
                if (LevelCompleteManager.Instance != null)
                {
                    LevelCompleteManager.Instance.ShowLevelCompleteScreen();
                }
                break;
            }
        }
    }
}