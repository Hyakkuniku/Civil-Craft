using UnityEngine;
using UnityEngine.Events;
using System.Collections;
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

    // Events
    public UnityEvent OnEnterBuildMode;
    public UnityEvent OnExitBuildMode;

    // Current active build location
    public BuildLocation ActiveBuildLocation { get; private set; }

    // References
    [SerializeField] private Camera mainCamera;
    private PlayerMotor playerMotor;
    private PlayerLook playerLook;
    private InputManager inputManager;

    // UPGRADED: Camera transition variables for seamless flying
    private float cameraTransitionSpeed = 1.5f;
    private Coroutine cameraTransitionCoroutine;
    
    // Variables to remember where the player camera belongs
    private Transform mainCamParent;
    private Vector3 mainCamLocalPos;
    private Quaternion mainCamLocalRot;

    [Header("UI to hide during build mode")]
    [Tooltip("Add any UI GameObjects here that should disappear when building.")]
    [SerializeField] private List<GameObject> uiElementsToHide = new List<GameObject>();

    // Wireframe toggle support
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

        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    private void Update()
    {
        // Quick exit (for testing / PC debugging)
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

        if (cachedBuildables.Count == 0)
        {
            CacheBuildableObjects();
        }

        // Disable player controls
        if (playerMotor != null)    playerMotor.enabled = false;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(false);
            inputManager.SetPlayerInputEnable(false);
        }

        // Capture exactly where the main camera currently sits relative to the player
        if (mainCamera != null)
        {
            mainCamParent = mainCamera.transform.parent;
            mainCamLocalPos = mainCamera.transform.localPosition;
            mainCamLocalRot = mainCamera.transform.localRotation;
        }

        foreach (GameObject uiElement in uiElementsToHide)
        {
            if (uiElement != null) uiElement.SetActive(false);
        }

        var playerUI = FindObjectOfType<PlayerUI>();
        if (playerUI != null) playerUI.UpdateText(string.Empty);

        SetAllBuildablesToWireframe(true);

        // Start seamless transition coroutine
        if (cameraTransitionCoroutine != null) StopCoroutine(cameraTransitionCoroutine);
        cameraTransitionCoroutine = StartCoroutine(TransitionToBuildCamera(location));
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;

        // Start seamless transition coroutine back to the player
        if (cameraTransitionCoroutine != null) StopCoroutine(cameraTransitionCoroutine);
        cameraTransitionCoroutine = StartCoroutine(TransitionToPlayerCamera());
    }

    // ────────────────────────────────────────────────
    //  Seamless Camera Transition Coroutines
    // ────────────────────────────────────────────────

    private IEnumerator TransitionToBuildCamera(BuildLocation location)
    {
        if (mainCamera != null)
        {
            // 1. Detach the camera so it can fly independently
            mainCamera.transform.SetParent(null);

            Vector3 startPos = mainCamera.transform.position;
            Quaternion startRot = mainCamera.transform.rotation;

            // Target is either the physical location camera, or the manual offset
            Vector3 targetPos = location.locationCamera != null ? location.locationCamera.transform.position : location.GetDesiredCameraPosition();
            Quaternion targetRot = location.locationCamera != null ? location.locationCamera.transform.rotation : location.GetDesiredCameraRotation();

            // 2. Fly smoothly to the destination
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * cameraTransitionSpeed;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                mainCamera.transform.position = Vector3.Lerp(startPos, targetPos, smoothT);
                mainCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, smoothT);
                yield return null;
            }

            // Snap accurately to the final target
            mainCamera.transform.position = targetPos;
            mainCamera.transform.rotation = targetRot;

            // 3. Swap the active cameras seamlessly now that they perfectly align
            if (location.locationCamera != null)
            {
                mainCamera.enabled = false;
                location.locationCamera.enabled = true;
            }
        }

        OnEnterBuildMode?.Invoke();
        Debug.Log($"Entered build mode at: {location.name}");
    }

    private IEnumerator TransitionToPlayerCamera()
    {
        if (mainCamera != null && ActiveBuildLocation != null)
        {
            // 1. Seamlessly swap back to the main camera before flying
            if (ActiveBuildLocation.locationCamera != null)
            {
                mainCamera.transform.position = ActiveBuildLocation.locationCamera.transform.position;
                mainCamera.transform.rotation = ActiveBuildLocation.locationCamera.transform.rotation;

                ActiveBuildLocation.locationCamera.enabled = false;
                mainCamera.enabled = true;
            }

            Vector3 startPos = mainCamera.transform.position;
            Quaternion startRot = mainCamera.transform.rotation;

            // 2. Fly smoothly back to the player's head/body
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * cameraTransitionSpeed;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                // Dynamically track the target in case the player object is moving slightly (e.g., gravity)
                Vector3 targetPos = mainCamParent != null ? mainCamParent.TransformPoint(mainCamLocalPos) : startPos;
                Quaternion targetRot = mainCamParent != null ? mainCamParent.rotation * mainCamLocalRot : startRot;

                mainCamera.transform.position = Vector3.Lerp(startPos, targetPos, smoothT);
                mainCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, smoothT);
                yield return null;
            }

            // 3. Re-parent and snap into exact standard FPS position
            if (mainCamParent != null)
            {
                mainCamera.transform.SetParent(mainCamParent);
                mainCamera.transform.localPosition = mainCamLocalPos;
                mainCamera.transform.localRotation = mainCamLocalRot;
            }
        }

        // 4. Return controls and UI to the player
        if (playerMotor != null) playerMotor.enabled = true;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(true);
            inputManager.SetPlayerInputEnable(true);
        }

        ActiveBuildLocation?.DeactivateBuildMode(FindObjectOfType<PlayerMotor>()?.transform);
        ActiveBuildLocation = null;

        foreach (GameObject uiElement in uiElementsToHide)
        {
            if (uiElement != null) uiElement.SetActive(true);
        }

        SetAllBuildablesToWireframe(false);
        OnExitBuildMode?.Invoke();

        Debug.Log("Exited build mode");
    }

    // ────────────────────────────────────────────────
    //  Wireframe / Buildable Visuals Logic
    // ────────────────────────────────────────────────

    private void CacheBuildableObjects()
    {
        cachedBuildables.Clear();
        var all = FindObjectsOfType<BuildableVisual>(true);
        cachedBuildables.AddRange(all);
        Debug.Log($"Cached {cachedBuildables.Count} buildable visual objects");
    }

    private void SetAllBuildablesToWireframe(bool inBuildMode)
    {
        foreach (var visual in cachedBuildables)
        {
            if (visual == null) continue;

            if (inBuildMode)
                visual.SetBuildMode();
            else
                visual.SetNormalMode();
        }
    }

    public bool IsInBuildMode() => CurrentState == GameState.Building;
}