using UnityEngine;

public class BuildLocation : MonoBehaviour
{
    [Header("Visuals & Prompt")]
    public string promptMessage = "Build Bridge / Structure";
    
    [Header("UI / Grid")]
    [Tooltip("The camera-space canvas holding the grid material for this specific location")]
    public Canvas gridCanvas; 

    [Header("Camera")]
    public Camera locationCamera;           
    public Vector3 cameraPositionOffset = new Vector3(0, 8, -12);
    public Vector3 cameraLookAtOffset   = new Vector3(0, 2, 0);   

    [Header("Behavior")]
    public bool disablePlayerMovement = true;
    public bool lockPlayerToZone = false;           
    public float maxBuildDistanceFromCenter = 25f;  
    
    [Header("Active Contract")]
    [Tooltip("The contract currently assigned to this ravine. Can be assigned by an NPC.")]
    public ContractSO activeContract; 

    private Transform originalPlayerParent;

    private void Awake()
    {
        if (locationCamera != null) locationCamera.enabled = false;
        if (gridCanvas != null) gridCanvas.enabled = false;
    }

    public void ActivateBuildMode(Transform player)
    {
        if (GameManager.Instance == null) return;

        // Block building if no contract has been accepted!
        if (activeContract == null)
        {
            Debug.LogWarning("You must accept a contract from an NPC before building here!");
            return; 
        }

        if (gridCanvas != null) gridCanvas.enabled = true;

        GameManager.Instance.EnterBuildMode(this, player);

        if (lockPlayerToZone)
        {
            originalPlayerParent = player.parent;
            player.SetParent(transform);
        }
    }

    public void DeactivateBuildMode(Transform player)
    {
        if (gridCanvas != null) gridCanvas.enabled = false;

        if (lockPlayerToZone && originalPlayerParent != null)
        {
            player.SetParent(originalPlayerParent);
            originalPlayerParent = null;
        }
    }

    public Vector3 GetDesiredCameraPosition()
    {
        return transform.position + cameraPositionOffset;
    }

    public Quaternion GetDesiredCameraRotation()
    {
        Vector3 lookAt = transform.position + cameraLookAtOffset;
        return Quaternion.LookRotation(lookAt - GetDesiredCameraPosition());
    }
}