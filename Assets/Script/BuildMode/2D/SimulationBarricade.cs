using UnityEngine;

public class SimulationBarricade : MonoBehaviour
{
    [Tooltip("Drag the 3D models and Colliders of your barricades into this list.")]
    public GameObject[] barricadeVisuals;

    private BridgePhysicsManager physicsManager;

    private void Start()
    {
        // Find the Physics Manager in the scene
        physicsManager = FindObjectOfType<BridgePhysicsManager>();
        
        if (physicsManager != null)
        {
            // Subscribe to the newly added Action Events!
            physicsManager.OnSimulationStarted += HideBarricades;
            physicsManager.OnSimulationStopped += ShowBarricades;

            // Set the initial state correctly
            if (physicsManager.isSimulating) HideBarricades();
            else ShowBarricades();
        }
    }

    private void OnDestroy()
    {
        // ALWAYS unsubscribe when destroyed to prevent memory leaks!
        if (physicsManager != null)
        {
            physicsManager.OnSimulationStarted -= HideBarricades;
            physicsManager.OnSimulationStopped -= ShowBarricades;
        }
    }

    private void ShowBarricades()
    {
        foreach (var obj in barricadeVisuals)
        {
            if (obj != null) obj.SetActive(true);
        }
    }

    private void HideBarricades()
    {
        foreach (var obj in barricadeVisuals)
        {
            if (obj != null) obj.SetActive(false);
        }
    }
}