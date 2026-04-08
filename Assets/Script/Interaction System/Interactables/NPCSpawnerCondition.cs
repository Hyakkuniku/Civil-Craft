using UnityEngine;

public class NPCSpawnerCondition : MonoBehaviour
{
    [Header("Spawn Condition")]
    [Tooltip("The exact name of the lesson/tutorial that must be completed to trigger this.")]
    public string requiredLessonName = "HouseTutorial";

    [Tooltip("If TRUE, the NPC spawns OUTSIDE after the tutorial. If FALSE, the NPC stays hidden INSIDE after the tutorial.")]
    public bool appearAfterLesson = true;

    private void Start()
    {
        // Safety check
        if (PlayerDataManager.Instance == null) return;

        // Check if the player's save file has this specific lesson marked as complete
        bool isLessonComplete = PlayerDataManager.Instance.CurrentData.completedLessons.Contains(requiredLessonName);

        if (appearAfterLesson)
        {
            // For the OUTSIDE NPC: Only turn ON if the tutorial is finished.
            gameObject.SetActive(isLessonComplete);
        }
        else
        {
            // For the INSIDE NPC: Turn OFF if the tutorial is finished, so he isn't in two places at once!
            gameObject.SetActive(!isLessonComplete);
        }
    }
}