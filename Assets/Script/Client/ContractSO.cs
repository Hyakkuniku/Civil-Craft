using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewContract", menuName = "Bridge/Contract")]
public class ContractSO : ScriptableObject
{
    public enum WinCondition { FinishLine, Timer }

    [Header("Contract Details")]
    public string clientName = "Mayor";
    
    [TextArea(2, 4)]
    public string jobDescription = "We need a bridge across this ravine!";

    [Header("Job Constraints")]
    public float budget = 2000f;
    public float liveLoadWeight = 50f; 

    // --- NEW: Material Restrictions ---
    [Header("Material Restrictions")]
    [Tooltip("List the specific materials allowed for this job. If this list is EMPTY, ALL materials are allowed!")]
    public List<BridgeMaterialSO> allowedMaterials = new List<BridgeMaterialSO>();

    [Header("Winning Condition")]
    public WinCondition winCondition = WinCondition.FinishLine;
    [Tooltip("If Win Condition is Timer, how many seconds must the bridge survive simulation?")]
    public float requiredIntactTime = 5f;
    
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