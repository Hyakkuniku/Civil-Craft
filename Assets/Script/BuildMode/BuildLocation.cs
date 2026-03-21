using UnityEngine;

// 1. Inherit from YOUR Interactable script!
public class BuildLocation : Interactable 
{
    [Header("UI / Grid")]
    [Tooltip("The camera-space canvas holding the grid material for this specific location")]
    public Canvas gridCanvas; 

    [Header("Camera")]
    public Camera locationCamera;           
    public Vector3 cameraPositionOffset = new Vector3(0, 8, -12);
    public Vector3 cameraLookAtOffset   = new Vector3(0, 2, 0);   

    [Header("Behavior")]
    public bool lockPlayerToZone = false;           
    
    [Header("Active Contract")]
    [Tooltip("The contract currently assigned to this ravine. Can be assigned by an NPC.")]
    public ContractSO activeContract; 

    // --- NEW: Tutorial Integration ---
    [Header("Tutorial Settings")]
    [Tooltip("Check this if successfully entering Build Mode here should finish the tutorial!")]
    public bool advancesTutorial = false; 
    // ---------------------------------

    private Transform originalPlayerParent;

    private void Awake()
    {
        if (locationCamera != null) locationCamera.enabled = false;
        if (gridCanvas != null) gridCanvas.enabled = false;
    }

    private void Update()
    {
        // 2. We update the promptMessage variable from your Interactable base class
        if (activeContract == null)
        {
            promptMessage = "Requires Contract! Talk to the client.";
        }
        else
        {
            promptMessage = "Enter Build Mode";
        }
    }

    // 3. This overrides the virtual Intract() method in YOUR Interactable.cs!
    // It triggers automatically when your PlayerUI button is clicked.
    protected override void Intract()
    {
        TryEnterBuildMode();
    }

    public void TryEnterBuildMode()
    {
        if (activeContract == null)
        {
            Debug.LogWarning("<color=red>Access Denied!</color> You cannot build here without a valid contract.");
            return;
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null)
        {
            ActivateBuildMode(player.transform);
        }
    }

    public void ActivateBuildMode(Transform player)
    {
        if (GameManager.Instance == null || activeContract == null) return; 

        if (gridCanvas != null) gridCanvas.enabled = true;

        GameManager.Instance.EnterBuildMode(this, player);

        if (lockPlayerToZone && player != null)
        {
            originalPlayerParent = player.parent;
            player.SetParent(transform);
        }

        // --- NEW: Trigger the end of the tutorial! ---
        if (advancesTutorial && TutorialManager.Instance != null)
        {
            TutorialManager.Instance.ShowNextStep();
        }
        // ---------------------------------------------
    }

    public void DeactivateBuildMode(Transform player)
    {
        if (gridCanvas != null) gridCanvas.enabled = false;

        if (lockPlayerToZone && originalPlayerParent != null && player != null)
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