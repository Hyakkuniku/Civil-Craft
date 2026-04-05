using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class MaterialAllowance
{
    public BridgeMaterialSO material;
    
    [Tooltip("The maximum number of pieces allowed. Set to 0 for INFINITE pieces.")]
    public int maxPieces = 0; 
}

[CreateAssetMenu(fileName = "NewContract", menuName = "Bridge/Contract")]
public class ContractSO : ScriptableObject
{
    public enum WinCondition { FinishLine, Timer }

    // --- NEW: Tutorial Flag ---
    [Header("Tutorial Settings")]
    [Tooltip("If checked, any materials NOT on the Allowed list will be completely hidden instead of grayed out.")]
    public bool isTutorialContract = false;

    [Header("Contract Details")]
    public string clientName = "Mayor";
    
    [TextArea(2, 4)]
    public string jobDescription = "We need a bridge across this ravine!";

    [Header("Job Constraints")]
    public float budget = 2000f;
    public float liveLoadWeight = 50f; 

    [Header("Material Restrictions")]
    [Tooltip("List the specific materials allowed for this job and their quantity limits.")]
    public List<MaterialAllowance> allowedMaterials = new List<MaterialAllowance>();

    [Header("Challenges / Constraints (Checklist)")]
    [Tooltip("If checked, the bridge will instantly fail if it hits a certain stress level.")]
    public bool enforceMaxStress = false;
    [Tooltip("The maximum allowed stress percentage before failure (e.g., 85)")]
    [Range(1f, 100f)] public float maxAllowedStress = 100f;

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