using UnityEngine;

[CreateAssetMenu(fileName = "NewContract", menuName = "Bridge/Contract")]
public class ContractSO : ScriptableObject
{
    [Header("Contract Details")]
    public string clientName = "Mayor";
    
    [TextArea(2, 4)]
    public string jobDescription = "We need a bridge across this ravine!";

    [Header("Job Constraints")]
    public float budget = 2000f;
    public float liveLoadWeight = 50f; 
    
    // --- NEW: Contract Rewards! ---
    [Header("Rewards")]
    [Tooltip("How much gold the player earns for beating this level.")]
    public int goldReward = 500;
    [Tooltip("How much EXP the player earns for beating this level.")]
    public int expReward = 100;

    [Header("Dialogue Integration")]
    public Dialogue offerDialogue;
    public Dialogue reminderDialogue; 
    public Dialogue finishedContractDialogue; 
}