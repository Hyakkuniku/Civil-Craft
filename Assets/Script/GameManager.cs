using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState
    {
        Normal,
        Building
    }

    public GameState CurrentState { get; private set; } = GameState.Normal;

    public UnityEvent OnEnterBuildMode;
    public UnityEvent OnExitBuildMode;

    public BuildLocation ActiveBuildLocation { get; private set; }

    [SerializeField] private Camera mainCamera;
    private PlayerMotor playerMotor;
    private PlayerLook playerLook;
    private InputManager inputManager;
    private BridgePhysicsManager physicsManager;
    private PlayerInteract playerInteract;

    private Transform mainCamParent;
    private Vector3 mainCamLocalPos;
    private Quaternion mainCamLocalRot;

    [Header("UI Management")]
    [Tooltip("UI to hide during build mode (like crosshairs, player health, etc)")]
    [SerializeField] private List<GameObject> uiElementsToHide = new List<GameObject>();
    
    // NEW: List of UI elements to show ONLY when building
    [Tooltip("UI to show only during build mode (like budget, build menus, etc)")]
    [SerializeField] private List<GameObject> buildModeUIElements = new List<GameObject>();

    private List<BuildableVisual> cachedBuildables = new List<BuildableVisual>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        playerMotor = FindObjectOfType<PlayerMotor>();
        playerLook  = FindObjectOfType<PlayerLook>();
        inputManager = FindObjectOfType<InputManager>();
        physicsManager = FindObjectOfType<BridgePhysicsManager>();
        playerInteract = FindObjectOfType<PlayerInteract>();

        if (mainCamera == null)
            mainCamera = Camera.main;
            
        // NEW: Ensure build UI is turned off when the game first starts!
        foreach (GameObject uiElement in buildModeUIElements)
        {
            if (uiElement != null) uiElement.SetActive(false);
        }
    }

    private void Update()
    {
        if (CurrentState == GameState.Building && Input.GetKeyDown(KeyCode.Escape))
        {
            ExitBuildMode();
        }
    }

    public void EnterBuildMode(BuildLocation location, Transform player)
    {
        if (CurrentState == GameState.Building) return;

        if (physicsManager != null && physicsManager.isSimulating)
        {
            physicsManager.StopPhysicsAndReset();
        }

        ActiveBuildLocation = location;
        CurrentState = GameState.Building;

        if (BuildUIController.Instance != null && location.activeContract != null)
        {
            BuildUIController.Instance.maxBudget = location.activeContract.budget;
        }

        if (cachedBuildables.Count == 0) CacheBuildableObjects();

        // Disable Player Controls and Interactions
        if (playerMotor != null) playerMotor.enabled = false;
        if (playerInteract != null) playerInteract.enabled = false; 
        
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(false);
            inputManager.SetPlayerInputEnable(false);
        }

        // Unparent Camera safely and SNAP instantly to Build Camera
        if (mainCamera != null)
        {
            mainCamParent = mainCamera.transform.parent;
            mainCamLocalPos = mainCamera.transform.localPosition;
            mainCamLocalRot = mainCamera.transform.localRotation;
            mainCamera.transform.SetParent(null); 

            Vector3 targetPos = location.locationCamera != null ? location.locationCamera.transform.position : location.GetDesiredCameraPosition();
            Quaternion targetRot = location.locationCamera != null ? location.locationCamera.transform.rotation : location.GetDesiredCameraRotation();

            mainCamera.transform.position = targetPos;
            mainCamera.transform.rotation = targetRot;

            if (location.locationCamera != null)
            {
                mainCamera.enabled = false;
                location.locationCamera.enabled = true;
            }
        }

        // --- UI MANAGEMENT ---
        
        // Hide normal player UI
        foreach (GameObject uiElement in uiElementsToHide)
            if (uiElement != null) uiElement.SetActive(false);

        // NEW: Show build mode UI
        foreach (GameObject uiElement in buildModeUIElements)
            if (uiElement != null) uiElement.SetActive(true);

        // Clear out interaction buttons
        var playerUI = FindObjectOfType<PlayerUI>();
        if (playerUI != null) playerUI.UpdateButtons(new List<Interactable>());

        SetAllBuildablesToWireframe(true);

        OnEnterBuildMode?.Invoke();
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;

        OnExitBuildMode?.Invoke();

        if (physicsManager != null && !physicsManager.isSimulating)
        {
            physicsManager.ActivatePhysics();
        }

        if (mainCamera != null && ActiveBuildLocation != null)
        {
            if (ActiveBuildLocation.locationCamera != null)
            {
                ActiveBuildLocation.locationCamera.enabled = false;
                mainCamera.enabled = true;
            }

            if (mainCamParent != null)
            {
                mainCamera.transform.SetParent(mainCamParent);
                mainCamera.transform.localPosition = mainCamLocalPos;
                mainCamera.transform.localRotation = mainCamLocalRot;
            }
        }

        // Restore Player Controls and Interactions
        if (playerMotor != null) playerMotor.enabled = true;
        if (playerInteract != null) playerInteract.enabled = true;
        
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(true);
            inputManager.SetPlayerInputEnable(true);
        }

        if (ActiveBuildLocation != null)
        {
            Transform pTransform = playerMotor != null ? playerMotor.transform : null;
            if (pTransform != null) ActiveBuildLocation.DeactivateBuildMode(pTransform);
            ActiveBuildLocation = null;
        }

        // --- UI MANAGEMENT ---
        
        // Restore normal player UI
        foreach (GameObject uiElement in uiElementsToHide)
            if (uiElement != null) uiElement.SetActive(true);

        // NEW: Hide build mode UI
        foreach (GameObject uiElement in buildModeUIElements)
            if (uiElement != null) uiElement.SetActive(false);

        SetAllBuildablesToWireframe(false);
    }

    private void CacheBuildableObjects()
    {
        cachedBuildables.Clear();
        var all = FindObjectsOfType<BuildableVisual>(true);
        cachedBuildables.AddRange(all);
    }

    private void SetAllBuildablesToWireframe(bool inBuildMode)
    {
        foreach (var visual in cachedBuildables)
        {
            if (visual == null) continue;
            if (inBuildMode) visual.SetBuildMode();
            else visual.SetNormalMode();
        }
    }

    public bool IsInBuildMode() => CurrentState == GameState.Building;
}