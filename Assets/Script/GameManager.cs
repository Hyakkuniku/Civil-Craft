using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Normal, Building }
    public GameState CurrentState { get; private set; } = GameState.Normal;

    public UnityEvent OnEnterBuildMode;
    public UnityEvent OnExitBuildMode;

    public BuildLocation ActiveBuildLocation { get; private set; }
    
    // --- NEW: Global memory of the active contract! ---
    public ContractSO CurrentContract { get; private set; } 

    [SerializeField] private Camera mainCamera;
    private Transform mainCamParent;
    private Vector3 mainCamLocalPos;
    private Quaternion mainCamLocalRot;
    private Transform currentPlayerTransform;

    [Header("UI Management")]
    [SerializeField] private List<GameObject> uiElementsToHide = new List<GameObject>();
    [SerializeField] private List<GameObject> buildModeUIElements = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }

        if (mainCamera == null) mainCamera = Camera.main;
        foreach (GameObject uiElement in buildModeUIElements) if (uiElement != null) uiElement.SetActive(false);
    }

    // --- NEW: Safe Framerate Lock ---
    private void Start()
    {
        // Tells the Unity Engine to natively target 60 FPS without freezing the main thread!
        Application.targetFrameRate = 100000;
    }

    private void Update()
    {
        if (CurrentState == GameState.Building && Input.GetKeyDown(KeyCode.Escape)) ExitBuildMode();
    }

    public void EnterBuildMode(BuildLocation location, Transform player)
    {
        if (CurrentState == GameState.Building) return;

        ActiveBuildLocation = location;
        
        if (location != null && location.activeContract != null)
        {
            CurrentContract = location.activeContract; 
        }

        CurrentState = GameState.Building;
        currentPlayerTransform = player;

        if (BuildUIController.Instance != null && CurrentContract != null)
            BuildUIController.Instance.maxBudget = CurrentContract.budget;

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

        foreach (GameObject uiElement in uiElementsToHide) if (uiElement != null) uiElement.SetActive(false);
        foreach (GameObject uiElement in buildModeUIElements) if (uiElement != null) uiElement.SetActive(true);

        OnEnterBuildMode?.Invoke();
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal) return;

        CurrentState = GameState.Normal;
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
            currentPlayerTransform = null;
        }

        foreach (GameObject uiElement in uiElementsToHide) if (uiElement != null) uiElement.SetActive(true);
        foreach (GameObject uiElement in buildModeUIElements) if (uiElement != null) uiElement.SetActive(false);
    }

    public bool IsInBuildMode() => CurrentState == GameState.Building;
}