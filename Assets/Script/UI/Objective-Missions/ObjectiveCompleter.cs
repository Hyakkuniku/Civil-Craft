using UnityEngine;

public class ObjectiveCompleter : MonoBehaviour
{
    [Header("Task To Complete")]
    [Tooltip("This MUST exactly match the Title you gave the task!")]
    public string taskTitle = "Get Almanac";

    [Header("Settings")]
    [Tooltip("If true, it completes the task the second the player walks into this object's collider.")]
    public bool completeOnTriggerEnter = false;

    private void OnTriggerEnter(Collider other)
    {
        if (completeOnTriggerEnter && other.CompareTag("Player"))
        {
            MarkTaskDone();
            
            // Turn off so it doesn't fire twice
            gameObject.SetActive(false); 
        }
    }

    // You can call this from a UnityEvent, a Button, or when an item is picked up!
    public void MarkTaskDone()
    {
        if (ObjectiveTrackerUI.Instance != null)
        {
            ObjectiveTrackerUI.Instance.CompleteGenericTask(taskTitle);
        }
        else
        {
            Debug.LogWarning("ObjectiveTrackerUI is missing from the scene!");
        }
    }
}