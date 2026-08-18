using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class TutorialTrigger : MonoBehaviour
{
    private bool hasTriggered = false;

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // SAFETY FIX: Check if the tutorial is actually active before advancing
        if (other.CompareTag("Player") && !hasTriggered)
        {
            if (TutorialManager.Instance != null && TutorialManager.Instance.IsTutorialActive)
            {
                hasTriggered = true;
                TutorialManager.Instance.ShowNextStep();
                gameObject.SetActive(false);
            }
        }
    }
}