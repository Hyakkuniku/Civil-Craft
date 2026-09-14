using UnityEngine;

/// <summary>An optional, serializable phase action. Stores an ID, never a list index.</summary>
[System.Serializable]
public sealed class NPCPhaseCall
{
    public NPCProgressionManager npc;
    public string phaseId;

    public void Invoke()
    {
        if (string.IsNullOrEmpty(phaseId)) return;
        if (npc == null)
        {
            Debug.LogWarning("[NPCPhaseCall] The selected phase has no target NPC. Action skipped.");
            return;
        }
        npc.MoveToPhaseById(phaseId);
    }
}
