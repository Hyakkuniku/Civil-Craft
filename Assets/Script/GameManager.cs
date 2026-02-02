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

    public UnityEvent OnEnterBuildMode;
    public UnityEvent OnExitBuildMode;

    // Current active build location
    public BuildLocation ActiveBuildLocation { get; private set; }

    // References
    [SerializeField] private Camera mainCamera;
    private PlayerMotor playerMotor;
    private PlayerLook playerLook;
    private InputManager inputManager;

    // For smooth camera transition (optional but recommended)
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
    }

    private void Update()
    {
        if (CurrentState == GameState.Building && isTransitioning)
        {
            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, cameraTargetPos, Time.deltaTime * cameraTransitionSpeed);
            mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, cameraTargetRot, Time.deltaTime * cameraTransitionSpeed);

            if (Vector3.Distance(mainCamera.transform.position, cameraTargetPos) < 0.1f)
                isTransitioning = false;
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

        // Disable controls
        if (playerMotor != null)        playerMotor.enabled = false;
        inputManager.SetLookEnabled(false);
        inputManager.SetPlayerInputEnable(false);

        // Camera logic
        if (location.locationCamera != null)
        {
            // Use dedicated camera for this location
            mainCamera.enabled = false;
            location.locationCamera.enabled = true;
        }
        else
        {
            // Smoothly move main camera to nice overview position
            cameraTargetPos = location.GetDesiredCameraPosition();
            cameraTargetRot = location.GetDesiredCameraRotation();
            isTransitioning = true;
        }

        OnEnterBuildMode?.Invoke();

        Debug.Log($"Entered build mode at: {location.name}");
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;

        // Re-enable controls
        if (playerMotor != null) playerMotor.enabled = true;
        inputManager.SetLookEnabled(true);
        inputManager.SetPlayerInputEnable(true);

        // Camera back
        if (ActiveBuildLocation != null && ActiveBuildLocation.locationCamera != null)
        {
            ActiveBuildLocation.locationCamera.enabled = false;
        }
        mainCamera.enabled = true;

        // Let location clean up (e.g. unparent player)
        ActiveBuildLocation?.DeactivateBuildMode(FindObjectOfType<PlayerMotor>().transform);

        ActiveBuildLocation = null;

        OnExitBuildMode?.Invoke();

        Debug.Log("Exited build mode");
    }
}