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

    // Transition variables removed since it now snaps instantly!
    
    private Transform mainCamParent;
    private Vector3 mainCamLocalPos;
    private Quaternion mainCamLocalRot;

    [Header("UI to hide during build mode")]
    [SerializeField] private List<GameObject> uiElementsToHide = new List<GameObject>();

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

        if (mainCamera == null)
            mainCamera = Camera.main;
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

        // 1. Instantly reset physics
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

        // 2. Disable Player
        if (playerMotor != null) playerMotor.enabled = false;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(false);
            inputManager.SetPlayerInputEnable(false);
        }

        // 3. Unparent Camera safely and SNAP instantly to Build Camera
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

        // 4. Update UI instantly
        foreach (GameObject uiElement in uiElementsToHide)
            if (uiElement != null) uiElement.SetActive(false);

        var playerUI = FindObjectOfType<PlayerUI>();
        if (playerUI != null) playerUI.UpdateText(string.Empty);

        SetAllBuildablesToWireframe(true);

        // 5. Fire Event INSTANTLY
        OnEnterBuildMode?.Invoke();
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;

        // 1. Fire Event INSTANTLY to shut down the BarCreator and UI
        OnExitBuildMode?.Invoke();

        // 2. Activate physics so the player can walk on the bridge
        if (physicsManager != null && !physicsManager.isSimulating)
        {
            physicsManager.ActivatePhysics();
        }

        // 3. INSTANT SNAP back to Player Camera
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

        // Restore Player Controls
        if (playerMotor != null) playerMotor.enabled = true;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(true);
            inputManager.SetPlayerInputEnable(true);
        }

        // Safely Deactivate Build Location
        if (ActiveBuildLocation != null)
        {
            Transform pTransform = playerMotor != null ? playerMotor.transform : null;
            if (pTransform != null) ActiveBuildLocation.DeactivateBuildMode(pTransform);
            ActiveBuildLocation = null;
        }

        // Restore Normal UI
        foreach (GameObject uiElement in uiElementsToHide)
            if (uiElement != null) uiElement.SetActive(true);

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