using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class TutorialTriggerZone : MonoBehaviour
{
    [Tooltip("Drag the TutorialSequence object you want to play when the player enters this box")]
    public TutorialSequence sequenceToPlay;

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryTrigger(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // If another tutorial was active on entry, retry while the player remains
        // in the zone so this trigger is not silently lost.
        TryTrigger(other);
    }

    private void TryTrigger(Collider other)
    {
        if (!other.CompareTag("Player") || sequenceToPlay == null || !sequenceToPlay.CanStartTutorial()) return;

        sequenceToPlay.TryStartTutorial();

        // Consume the trigger only after the manager actually accepted the sequence.
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsTutorialActive)
            gameObject.SetActive(false);
    }
}
