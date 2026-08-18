using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class ObjectiveGiverTrigger : MonoBehaviour
{
    [Header("Objective Details")]
    public string objectiveTitle = "Lost Hammer";
    
    [TextArea(2, 4)]
    public string objectiveDescription = "Find the blacksmith's lost hammer near the river.";

    [Header("Settings")]
    public bool giveOnTriggerEnter = true;

    [Header("Navigation")]
    [Tooltip("Drag the physical object in the scene you want the rock trail to lead to.")]
    public GameObject navigationTarget;

    private void Start()
    {
        GetComponent<BoxCollider>().isTrigger = true;

        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData != null)
        {
            var activeTasks = PlayerDataManager.Instance.CurrentData.activeQuests;
            if (activeTasks.Exists(t => t.title == objectiveTitle))
            {
                gameObject.SetActive(false);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (giveOnTriggerEnter && other.CompareTag("Player"))
        {
            GiveTheObjective();
            gameObject.SetActive(false); 
        }
    }

    public void GiveTheObjective()
    {
        if (ObjectiveTrackerUI.Instance != null)
        {
            string targetName = navigationTarget != null ? navigationTarget.name : "";
            ObjectiveTrackerUI.Instance.AddGenericTask(objectiveTitle, objectiveDescription, targetName);
            Debug.Log($"<color=cyan>New Objective Added: {objectiveTitle}</color>");
        }
        else
        {
            Debug.LogWarning("ObjectiveTrackerUI is missing from the scene!");
        }
    }
}