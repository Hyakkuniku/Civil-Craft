using UnityEngine;

public class BuildLocation : MonoBehaviour
{
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
    
    // ADDED: A reference to the Interactable component on this object
    private Interactable myInteractable;

    private void Awake()
    {
        if (locationCamera != null) locationCamera.enabled = false;
        if (gridCanvas != null) gridCanvas.enabled = false;
        
        // Grab the interactable component so we can change its text directly!
        myInteractable = GetComponent<Interactable>();
    }

    // ADDED: This constantly checks if you have a contract and updates the UI prompt instantly
    private void Update()
    {
        if (myInteractable != null)
        {
            if (activeContract == null)
            {
                myInteractable.promptMessage = "Requires Contract! Talk to the client.";
            }
            else
            {
                myInteractable.promptMessage = "Enter Build Mode";
            }
        }
    }

    public void ActivateBuildMode(Transform player)
    {
        if (GameManager.Instance == null) return;

        // Block building if no contract has been accepted (it will just ignore your click)
        if (activeContract == null) return; 

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