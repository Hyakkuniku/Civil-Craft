using UnityEngine;
using System.Collections; 
using System.Collections.Generic;

public class TutorialSequence : MonoBehaviour
{
    [Header("Progression Settings")]
    public string lessonName;
    public string requiredPreviousLesson;
    
    [Tooltip("Check this if this tutorial should automatically start when the scene loads (like the movement tutorial)")]
    public bool playOnStart = false;

    // --- THE FIX: We removed the global Wasp Waypoints from here! They are now inside the TutorialStep! ---

    [Header("Tutorial Steps")]
    public TutorialStep[] tutorialSteps;

    private IEnumerator Start()
    {
        if (playOnStart)
        {
            yield return new WaitForSeconds(0.1f); 
            TryStartTutorial();
        }
    }

    public void TryStartTutorial()
    {
        if (PlayerDataManager.Instance != null)
        {
            var data = PlayerDataManager.Instance.CurrentData;
            
            if (!string.IsNullOrEmpty(lessonName) && data.completedLessons.Contains(lessonName)) return;
            
            if (!string.IsNullOrEmpty(requiredPreviousLesson) && !data.completedLessons.Contains(requiredPreviousLesson)) return;
        }

        if (TutorialManager.Instance != null)
        {
            TutorialManager.Instance.PlayTutorial(this);
        }
    }
}