using UnityEngine;

public class FindHideHeadLocally : MonoBehaviour
{
    void Start()
    {
        // Use FindObjectsByType with FindObjectsInactive.Include
        var scripts = Object.FindObjectsByType<PauseManager>(
            FindObjectsInactive.Include, 
            FindObjectsSortMode.None
        );

        foreach (var script in scripts)
        {
            Debug.Log($"Found HideHeadLocally on: {script.gameObject.name}", script.gameObject);
        }
    }
}