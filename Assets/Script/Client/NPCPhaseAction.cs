using UnityEngine;

[AddComponentMenu("Civil Craft/NPC Phase Action")]
public sealed class NPCPhaseAction : MonoBehaviour
{
    public NPCProgressionManager npc;
    [SerializeField] private string phaseId;

    // Assign this no-argument method to a button, dialogue or tutorial UnityEvent.
    public void MoveToSelectedPhase()
    {
        if (npc == null)
        {
            Debug.LogWarning("[NPCPhaseAction] Assign the target NPC first.", this);
            return;
        }
        npc.MoveToPhaseById(phaseId);
    }
}
