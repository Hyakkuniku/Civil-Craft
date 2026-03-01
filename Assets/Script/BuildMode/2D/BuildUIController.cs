using UnityEngine;

public class BuildUIController : MonoBehaviour
{
    [Header("System References")]
    public BarCreator barCreator;
    public BridgePhysicsManager physicsManager;

    [Header("Global Keyboard Shortcuts")]
    [Tooltip("Allow simulating and restarting via keyboard from any game state.")]
    public bool useKeyboardShortcuts = true;
    public KeyCode simulateKey = KeyCode.Return;   // Press Enter to Simulate
    public KeyCode restartKey = KeyCode.Backspace; // Press Backspace to Restart

    private void Start()
    {
        if (barCreator == null) barCreator = FindObjectOfType<BarCreator>();
        if (physicsManager == null) physicsManager = FindObjectOfType<BridgePhysicsManager>();
    }

    private void Update()
    {
        // Listen for keyboard presses regardless of UI visibility
        if (useKeyboardShortcuts)
        {
            if (Input.GetKeyDown(simulateKey))
            {
                OnSimulateButtonClicked();
            }

            if (Input.GetKeyDown(restartKey))
            {
                OnRestartButtonClicked();
            }
        }
    }

    // ────────────────────────────────────────────────
    // BUTTON TRIGGERS (Can be called by UI or Keyboard)
    // ────────────────────────────────────────────────

    public void OnSimulateButtonClicked()
    {
        if (physicsManager != null && !physicsManager.isSimulating)
        {
            // Force the player to drop whatever line they are drawing
            if (barCreator != null) 
            {
                barCreator.CancelCreation();
                barCreator.isSimulating = true;
            }
            
            physicsManager.ActivatePhysics();
        }
    }

    public void OnRestartButtonClicked()
    {
        if (physicsManager != null && physicsManager.isSimulating)
        {
            physicsManager.StopPhysicsAndReset();
            
            // Allow the player to build again
            if (barCreator != null) barCreator.isSimulating = false;
        }
    }

    public void OnToggleGridButtonClicked()
    {
        if (barCreator != null) barCreator.ToggleGrid();
    }

    public void OnCancelDrawingButtonClicked()
    {
        if (barCreator != null) barCreator.CancelCreation();
    }

    public void OnExitBuildModeButtonClicked()
    {
        if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode();
    }

    public void OnMaterialSelected(BridgeMaterialSO newMaterial)
    {
        if (barCreator != null)
        {
            barCreator.isDeleteMode = false;
            barCreator.SetActiveMaterial(newMaterial);
        }
    }

    public void OnToggleDeleteModeButtonClicked()
    {
        if (barCreator != null) barCreator.ToggleDeleteMode();
    }

    public void OnUndoButtonClicked()
    {
        if (barCreator != null) barCreator.Undo();
    }

    public void OnRedoButtonClicked()
    {
        if (barCreator != null) barCreator.Redo();
    }
}