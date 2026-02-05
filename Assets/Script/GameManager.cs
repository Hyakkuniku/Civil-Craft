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
    public UnityEvent OnEnterBuildGridMode;
    public UnityEvent OnExitBuildGridMode;

    // Current active build location
    public BuildLocation ActiveBuildLocation { get; private set; }

    // Grid system
    [Header("Build Grid")]
    [SerializeField] private BuildGrid buildGridPrefab;
    private BuildGrid activeBuildGrid;

    // References
    [SerializeField] private Camera mainCamera;
    private PlayerMotor playerMotor;
    private PlayerLook playerLook;
    private InputManager inputManager;

    // Camera transition
    private Vector3 cameraTargetPos;
    private Quaternion cameraTargetRot;
    private float cameraTransitionSpeed = 5f;
    private bool isTransitioning;

    [Header("UI to hide during build mode")]
    [SerializeField] private GameObject joystickUI;       // mobile joystick canvas/panel
    [SerializeField] private GameObject dialogueUI;       // dialogue panel/canvas
    [SerializeField] private GameObject promptUI;         // ← NEW: the prompt text GameObject (or its parent)

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

        // Quick exit (for testing)
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

        // Camera logic
        if (location.locationCamera != null)
        {
            if (mainCamera != null) mainCamera.enabled = false;
            location.locationCamera.enabled = true;
        }
        else
        {
            cameraTargetPos = location.GetDesiredCameraPosition();
            cameraTargetRot = location.GetDesiredCameraRotation();
            isTransitioning = true;
        }

        // Spawn grid
        SpawnBuildGrid(location);

        // Hide UI elements during build mode
        if (joystickUI != null) joystickUI.SetActive(false);
        if (dialogueUI   != null) dialogueUI.SetActive(false);
        if (promptUI     != null) promptUI.SetActive(false);           // ← NEW

        // Optional: force clear the prompt text immediately
        var playerUI = FindObjectOfType<PlayerUI>();
        if (playerUI != null) playerUI.UpdateText(string.Empty);

        OnEnterBuildMode?.Invoke();
        OnEnterBuildGridMode?.Invoke();

        Debug.Log($"Entered build mode at: {location.name}");
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;

        // Re-enable controls
        if (playerMotor != null) playerMotor.enabled = true;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(true);
            inputManager.SetPlayerInputEnable(true);
        }

        // Camera back
        if (ActiveBuildLocation != null && ActiveBuildLocation.locationCamera != null)
        {
            ActiveBuildLocation.locationCamera.enabled = false;
        }
        if (mainCamera != null) mainCamera.enabled = true;

        // Clean up grid
        if (activeBuildGrid != null)
        {
            Destroy(activeBuildGrid.gameObject);
            activeBuildGrid = null;
            OnExitBuildGridMode?.Invoke();
        }

        ActiveBuildLocation?.DeactivateBuildMode(FindObjectOfType<PlayerMotor>()?.transform);
        ActiveBuildLocation = null;

        // Show UI elements again
        if (joystickUI != null) joystickUI.SetActive(true);
        if (dialogueUI   != null) dialogueUI.SetActive(true);
        if (promptUI     != null) promptUI.SetActive(true);            // ← NEW

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

        activeBuildGrid = Instantiate(buildGridPrefab, location.transform);
        activeBuildGrid.transform.localPosition = Vector3.zero;
        activeBuildGrid.transform.localRotation = Quaternion.identity;

        activeBuildGrid.Initialize(location);
    }

    public bool IsInBuildMode() => CurrentState == GameState.Building;
}