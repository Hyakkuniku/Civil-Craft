using UnityEngine;

[CreateAssetMenu(fileName = "NewContract", menuName = "Bridge/Contract")]
public class ContractSO : ScriptableObject
{
    [Header("Contract Details")]
    public string clientName = "Mayor";
    
    [TextArea(2, 4)]
    public string jobDescription = "We need a bridge across this ravine!";

    [Header("Job Constraints")]
    [Tooltip("The maximum amount of money the player can spend on this bridge.")]
    public float budget = 2000f;
    
    [Header("Dialogue Integration")]
    [Tooltip("The dialogue the NPC will say when handing over this contract.")]
    public Dialogue offerDialogue;
}