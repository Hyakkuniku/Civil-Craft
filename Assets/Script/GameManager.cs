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

    public UnityEvent OnEnterBuildMode;
    public UnityEvent OnExitBuildMode;

    public BuildLocation ActiveBuildLocation { get; private set; }

    [SerializeField] private Camera mainCamera;
    private PlayerMotor playerMotor;
    private PlayerLook playerLook;
    private InputManager inputManager;
    private BridgePhysicsManager physicsManager;

    private float cameraTransitionSpeed = 1.5f;
    private Coroutine cameraTransitionCoroutine;
    
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

        if (physicsManager != null && physicsManager.isSimulating)
        {
            physicsManager.StopPhysicsAndReset();
        }

        ActiveBuildLocation = location;
        CurrentState = GameState.Building;

        // NEW: Pull the budget from the Contract given by the NPC
        if (BuildUIController.Instance != null && location.activeContract != null)
        {
            BuildUIController.Instance.maxBudget = location.activeContract.budget;
        }

        if (cachedBuildables.Count == 0) CacheBuildableObjects();

        if (playerMotor != null)    playerMotor.enabled = false;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(false);
            inputManager.SetPlayerInputEnable(false);
        }

        if (mainCamera != null)
        {
            mainCamParent = mainCamera.transform.parent;
            mainCamLocalPos = mainCamera.transform.localPosition;
            mainCamLocalRot = mainCamera.transform.localRotation;
        }

        foreach (GameObject uiElement in uiElementsToHide)
            if (uiElement != null) uiElement.SetActive(false);

        var playerUI = FindObjectOfType<PlayerUI>();
        if (playerUI != null) playerUI.UpdateText(string.Empty);

        SetAllBuildablesToWireframe(true);

        if (cameraTransitionCoroutine != null) StopCoroutine(cameraTransitionCoroutine);
        cameraTransitionCoroutine = StartCoroutine(TransitionToBuildCamera(location));
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;

        if (physicsManager != null && !physicsManager.isSimulating)
        {
            physicsManager.ActivatePhysics();
        }

        if (cameraTransitionCoroutine != null) StopCoroutine(cameraTransitionCoroutine);
        cameraTransitionCoroutine = StartCoroutine(TransitionToPlayerCamera());
    }

    private IEnumerator TransitionToBuildCamera(BuildLocation location)
    {
        if (mainCamera != null)
        {
            mainCamera.transform.SetParent(null);
            Vector3 startPos = mainCamera.transform.position;
            Quaternion startRot = mainCamera.transform.rotation;
            Vector3 targetPos = location.locationCamera != null ? location.locationCamera.transform.position : location.GetDesiredCameraPosition();
            Quaternion targetRot = location.locationCamera != null ? location.locationCamera.transform.rotation : location.GetDesiredCameraRotation();

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * cameraTransitionSpeed;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                mainCamera.transform.position = Vector3.Lerp(startPos, targetPos, smoothT);
                mainCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, smoothT);
                yield return null;
            }

            mainCamera.transform.position = targetPos;
            mainCamera.transform.rotation = targetRot;

            if (location.locationCamera != null)
            {
                mainCamera.enabled = false;
                location.locationCamera.enabled = true;
            }
        }
        OnEnterBuildMode?.Invoke();
    }

    private IEnumerator TransitionToPlayerCamera()
    {
        if (mainCamera != null && ActiveBuildLocation != null)
        {
            if (ActiveBuildLocation.locationCamera != null)
            {
                mainCamera.transform.position = ActiveBuildLocation.locationCamera.transform.position;
                mainCamera.transform.rotation = ActiveBuildLocation.locationCamera.transform.rotation;
                ActiveBuildLocation.locationCamera.enabled = false;
                mainCamera.enabled = true;
            }

            Vector3 startPos = mainCamera.transform.position;
            Quaternion startRot = mainCamera.transform.rotation;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * cameraTransitionSpeed;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 targetPos = mainCamParent != null ? mainCamParent.TransformPoint(mainCamLocalPos) : startPos;
                Quaternion targetRot = mainCamParent != null ? mainCamParent.rotation * mainCamLocalRot : startRot;

                mainCamera.transform.position = Vector3.Lerp(startPos, targetPos, smoothT);
                mainCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, smoothT);
                yield return null;
            }

            if (mainCamParent != null)
            {
                mainCamera.transform.SetParent(mainCamParent);
                mainCamera.transform.localPosition = mainCamLocalPos;
                mainCamera.transform.localRotation = mainCamLocalRot;
            }
        }

        if (playerMotor != null) playerMotor.enabled = true;
        if (inputManager != null)
        {
            inputManager.SetLookEnabled(true);
            inputManager.SetPlayerInputEnable(true);
        }

        ActiveBuildLocation?.DeactivateBuildMode(FindObjectOfType<PlayerMotor>()?.transform);
        ActiveBuildLocation = null;

        foreach (GameObject uiElement in uiElementsToHide)
            if (uiElement != null) uiElement.SetActive(true);

        SetAllBuildablesToWireframe(false);
        OnExitBuildMode?.Invoke();
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