// TutorialLevel.cs
using UnityEngine;

public class TutorialLevel : MonoBehaviour
{
    [Header("Auto Start")]
    public bool autoStartOnAwake = true;

    void Start()
    {
        
            TutorialManager.Instance.StartTutorial();
    }
}