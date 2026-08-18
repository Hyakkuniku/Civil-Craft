using UnityEngine;

public class TutorialTaskSender : MonoBehaviour
{
    [Header("Task Details")]
    public string taskTitle = "Get Almanac";
    
    [TextArea(2, 4)]
    public string taskDescription = "Search the house for the dusty book.";

    [Header("Navigation")]
    [Tooltip("Drag the physical object in the scene you want the rock trail to lead to.")]
    public GameObject navigationTarget;

    public void SendTask()
    {
        if (ObjectiveTrackerUI.Instance != null)
        {
            string targetName = navigationTarget != null ? navigationTarget.name : "";
            ObjectiveTrackerUI.Instance.AddGenericTask(taskTitle, taskDescription, targetName);
            Debug.Log($"<color=cyan>Tutorial Task Added: {taskTitle}</color>");
        }
    }
}