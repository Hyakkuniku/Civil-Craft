using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ObjectiveTabButton : MonoBehaviour
{
    public TextMeshProUGUI questTitleText;
    public GameObject readyToTurnInIcon; // A little '!' or checkmark icon

    private ObjectiveTrackerUI.TrackedTask myTask;

    public void Setup(ObjectiveTrackerUI.TrackedTask task)
    {
        myTask = task;
        
        if (questTitleText != null)
        {
            // Add a prefix so players know what type of quest it is
            string prefix = task.isTutorial ? "[Guide] " : "[Contract] ";
            questTitleText.text = prefix + task.title;
            
            // Turn the text gold if it's ready to complete!
            questTitleText.color = task.isReadyToTurnIn ? Color.yellow : Color.white;
        }

        if (readyToTurnInIcon != null) 
        {
            readyToTurnInIcon.SetActive(task.isReadyToTurnIn);
        }
    }

    // Hook this to the Button's OnClick event in the inspector!
    public void OnClicked()
    {
        if (ObjectiveTrackerUI.Instance != null && myTask != null)
        {
            ObjectiveTrackerUI.Instance.SelectTask(myTask);
        }
    }
}