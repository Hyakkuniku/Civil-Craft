using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState
    {
        Normal,
        Building
    }

    public GameState CurrentState { get; private set; } = GameState.Normal;

    // Events
    public UnityEvent OnEnterBuildMode;
    public UnityEvent OnExitBuildMode;
    public UnityEvent OnEnterBuildGridMode;     // NEW
    public UnityEvent OnExitBuildGridMode;      // NEW

    // Current active build location
    public BuildLocation ActiveBuildLocation { get; private set; }

    // Grid system
    [Header("Build Grid")]
    [SerializeField] private BuildGrid buildGridPrefab;     // ← Drag your BuildGrid prefab here in Inspector
    private BuildGrid activeBuildGrid;

    // References
    [SerializeField] private Camera mainCamera;
    private PlayerMotor playerMotor;
    private PlayerLook playerLook;
    private InputManager inputManager;

    // Camera transition (when no dedicated location camera is used)
    private Vector3 cameraTargetPos;
    private Quaternion cameraTargetRot;
    private float cameraTransitionSpeed = 5f;
    private bool isTransitioning;

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

        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    private void Update()
    {
        if (CurrentState == GameState.Building && isTransitioning)
        {
            mainCamera.transform.position = Vector3.Lerp(
                mainCamera.transform.position, 
                cameraTargetPos, 
                Time.deltaTime * cameraTransitionSpeed);

            mainCamera.transform.rotation = Quaternion.Slerp(
                mainCamera.transform.rotation, 
                cameraTargetRot, 
                Time.deltaTime * cameraTransitionSpeed);

            if (Vector3.Distance(mainCamera.transform.position, cameraTargetPos) < 0.1f &&
                Quaternion.Angle(mainCamera.transform.rotation, cameraTargetRot) < 1f)
            {
                isTransitioning = false;
            }
        }

        // Quick exit from build mode (mostly for testing)
        if (CurrentState == GameState.Building && Input.GetKeyDown(KeyCode.Escape))
        {
            ExitBuildMode();
        }
    }

    public void EnterBuildMode(BuildLocation location, Transform player)
    {
        if (CurrentState == GameState.Building) return;

        ActiveBuildLocation = location;
        CurrentState = GameState.Building;

        // Disable player controls
        if (playerMotor != null) playerMotor.enabled = false;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(false);
            inputManager.SetPlayerInputEnable(false);
        }

        // ────────────────────────────── CAMERA LOGIC ──────────────────────────────
        if (location.locationCamera != null)
        {
            // Use dedicated camera for this build location
            if (mainCamera != null) mainCamera.enabled = false;
            location.locationCamera.enabled = true;
        }
        else
        {
            // Smoothly move main camera to overview position
            cameraTargetPos = location.GetDesiredCameraPosition();
            cameraTargetRot = location.GetDesiredCameraRotation();
            isTransitioning = true;
        }

        // ────────────────────────────── SPAWN GRID ──────────────────────────────
        SpawnBuildGrid(location);

        OnEnterBuildMode?.Invoke();
        OnEnterBuildGridMode?.Invoke();

        Debug.Log($"Entered build mode at: {location.name}");
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;

        // Re-enable player controls
        if (playerMotor != null) playerMotor.enabled = true;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(true);
            inputManager.SetPlayerInputEnable(true);
        }

        // ────────────────────────────── CAMERA BACK ──────────────────────────────
        if (ActiveBuildLocation != null && ActiveBuildLocation.locationCamera != null)
        {
            ActiveBuildLocation.locationCamera.enabled = false;
        }
        if (mainCamera != null) mainCamera.enabled = true;

        // ────────────────────────────── CLEAN UP GRID ──────────────────────────────
        if (activeBuildGrid != null)
        {
            Destroy(activeBuildGrid.gameObject);
            activeBuildGrid = null;
            OnExitBuildGridMode?.Invoke();
        }

        // Let location clean up (unparent player, etc.)
        ActiveBuildLocation?.DeactivateBuildMode(FindObjectOfType<PlayerMotor>()?.transform);

        ActiveBuildLocation = null;

        OnExitBuildMode?.Invoke();

        Debug.Log("Exited build mode");
    }

    private void SpawnBuildGrid(BuildLocation location)
    {
        if (buildGridPrefab == null)
        {
            Debug.LogWarning("No BuildGrid prefab assigned in GameManager!");
            return;
        }

        // Instantiate grid as child of the BuildLocation
        activeBuildGrid = Instantiate(buildGridPrefab, location.transform);
        activeBuildGrid.transform.localPosition = Vector3.zero;
        activeBuildGrid.transform.localRotation = Quaternion.identity;

        // Let the grid initialize itself (pass reference to location if needed)
        activeBuildGrid.Initialize(location);
    }

    // Optional: helper method you can call from other scripts
    public bool IsInBuildMode() => CurrentState == GameState.Building;
}