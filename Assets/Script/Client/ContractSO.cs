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
    
    // ADDED: The required weight of the object to carry
    public float cargoWeight = 50f; 
    
    [Header("Dialogue Integration")]
    public Dialogue offerDialogue;
}