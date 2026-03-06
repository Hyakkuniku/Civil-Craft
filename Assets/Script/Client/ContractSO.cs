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
    
    public float cargoWeight = 50f; 
    
    [Header("Dialogue Integration")]
    [Tooltip("What they say when giving you the job")]
    public Dialogue offerDialogue;
    
    // NEW: What they say if you talk to them again before finishing the job
    [Tooltip("What they say if you talk to them while the job is active")]
    public Dialogue reminderDialogue; 
}