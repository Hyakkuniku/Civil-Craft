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

    private Transform mainCamParent;
    private Vector3 mainCamLocalPos;
    private Quaternion mainCamLocalRot;
    
    // NEW: We cache the player's transform here so we don't need the PlayerMotor script
    private Transform currentPlayerTransform;

    [Header("UI Management")]
    [Tooltip("UI to hide during build mode (like crosshairs, player health, etc)")]
    [SerializeField] private List<GameObject> uiElementsToHide = new List<GameObject>();
    
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

        if (mainCamera == null)
            mainCamera = Camera.main;
            
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

        ActiveBuildLocation = location;
        CurrentState = GameState.Building;
        currentPlayerTransform = player;

        if (BuildUIController.Instance != null && location.activeContract != null)
        {
            BuildUIController.Instance.maxBudget = location.activeContract.budget;
        }

        if (cachedBuildables.Count == 0) CacheBuildableObjects();

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

        foreach (GameObject uiElement in uiElementsToHide)
            if (uiElement != null) uiElement.SetActive(false);

        foreach (GameObject uiElement in buildModeUIElements)
            if (uiElement != null) uiElement.SetActive(true);

        SetAllBuildablesToWireframe(true);

        // This single line now tells the Player, Input, and Physics to do their thing!
        OnEnterBuildMode?.Invoke();
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;

        // This single line tells the Player, Input, and Physics to restore themselves!
        OnExitBuildMode?.Invoke();

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

        if (ActiveBuildLocation != null)
        {
            if (currentPlayerTransform != null) ActiveBuildLocation.DeactivateBuildMode(currentPlayerTransform);
            ActiveBuildLocation = null;
            currentPlayerTransform = null;
        }

        foreach (GameObject uiElement in uiElementsToHide)
            if (uiElement != null) uiElement.SetActive(true);

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