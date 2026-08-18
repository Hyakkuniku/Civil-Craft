using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ObjectiveTabButton : MonoBehaviour
{
    public TextMeshProUGUI questTitleText;
    public GameObject readyToTurnInIcon; 

    // --- THE FIX: We use the global TrackedTask now! ---
    private TrackedTask myTask;

    public void Setup(TrackedTask task)
    {
        myTask = task;
        
        if (questTitleText != null)
        {
            string prefix = task.isTutorial ? "[Guide] " : "[Contract] ";
            
            if (task.isCompleted) prefix = "[Done] ";

            questTitleText.text = prefix + task.title;
            
            if (task.isCompleted) 
                questTitleText.color = Color.gray;
            else if (task.isReadyToTurnIn) 
                questTitleText.color = Color.yellow;
            else 
                questTitleText.color = Color.white;
        }

        if (readyToTurnInIcon != null) 
        {
            readyToTurnInIcon.SetActive(task.isReadyToTurnIn && !task.isCompleted);
        }
    }

    public void OnClicked()
    {
        if (ObjectiveTrackerUI.Instance != null && myTask != null)
        {
            ObjectiveTrackerUI.Instance.SelectTask(myTask);
        }
    }
}