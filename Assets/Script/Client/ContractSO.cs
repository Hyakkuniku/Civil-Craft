using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class MaterialAllowance
{
    public BridgeMaterialSO material;
    
    [Tooltip("The maximum number of pieces allowed. Set to 0 for INFINITE pieces.")]
    public int maxPieces = 0; 
}

public enum BuildModeTool
{
    Select,
    Move,
    Delete,
    Grid,
    CancelDrawing,
    ExitBuildMode,
    ResetCamera,
    Statistics,
    Cut,
    Copy,
    Paste,
    Undo,
    Redo,
    DeleteSelected,
    Simulate
}

[CreateAssetMenu(fileName = "NewContract", menuName = "Bridge/Contract")]
public class ContractSO : ScriptableObject
{
    public enum WinCondition { FinishLine, Timer }

    [Header("Tutorial Settings")]
    [Tooltip("If checked, any materials NOT on the Allowed list will be completely hidden instead of grayed out.")]
    public bool isTutorialContract = false;

    /// <summary>
    /// True only while this contract is configured as a tutorial and the
    /// current player has not completed it yet. This deliberately does not
    /// modify the ScriptableObject asset when progression changes.
    /// </summary>
    public bool IsTutorialForCurrentPlayer()
    {
        return isTutorialContract && !WasTutorialCompletedByCurrentPlayer();
    }

    public bool WasTutorialCompletedByCurrentPlayer()
    {
        return isTutorialContract &&
               PlayerDataManager.Instance != null &&
               PlayerDataManager.Instance.HasContractCompletionRecord(name);
    }

    [Header("NPC & Reward Settings")]
    [Tooltip("If TRUE, rewards are given automatically upon clicking Save & Bake (no NPC required).")]
    public bool autoCollectReward = false;

    [Header("Contract Details")]
    public string clientName = "Mayor";
    
    [TextArea(2, 4)]
    public string jobDescription = "We need a bridge across this ravine!";

    [Header("Job Constraints")]
    public float budget = 2000f;
    public float liveLoadWeight = 50f; 
    // --- NEW: Bridge Span added here! ---
    [Tooltip("The required span/length of the bridge in meters.")]
    public float bridgeSpan = 30f; 

    [Header("Material Restrictions")]
    [Tooltip("List the specific materials allowed for this job and their quantity limits.")]
    public List<MaterialAllowance> allowedMaterials = new List<MaterialAllowance>();

    [Header("Build UI Visibility")]
    [Tooltip("These tools are completely hidden while this contract is active.")]
    public List<BuildModeTool> hiddenTools = new List<BuildModeTool>();
    [Tooltip("These material buttons are completely hidden while this contract is active, regardless of allowance/unlock state.")]
    public List<BridgeMaterialSO> hiddenMaterials = new List<BridgeMaterialSO>();

    public bool IsToolHidden(BuildModeTool tool)
    {
        return hiddenTools != null && hiddenTools.Contains(tool);
    }

    public bool IsMaterialHidden(BridgeMaterialSO material)
    {
        return material != null && hiddenMaterials != null && hiddenMaterials.Contains(material);
    }

    [Header("Challenges / Constraints (Checklist)")]
    [Tooltip("If checked, the bridge will instantly fail if it hits a certain stress level.")]
    public bool enforceMaxStress = false;
    [Tooltip("The maximum allowed stress percentage before failure (e.g., 85)")]
    [Range(1f, 100f)] public float maxAllowedStress = 100f;

    [Tooltip("If checked, the player has a limited amount of time to build the bridge.")]
    public bool isTimeAttack = false;
    [Tooltip("How many seconds the player has to build the bridge before failing.")]
    public float timeAttackDuration = 60f;

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
