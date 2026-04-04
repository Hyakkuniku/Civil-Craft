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

    [Header("Wasp Waypoints")]
    // --- THE FIX: Updated to use the new GuiderWaypoint class! ---
    public List<GuiderWaypoint> tutorialWaypoints;

    [Header("Tutorial Steps")]
    public TutorialStep[] tutorialSteps;

    // Wait a fraction of a second before starting so the Managers can finish loading!
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