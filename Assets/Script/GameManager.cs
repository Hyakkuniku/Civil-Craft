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
    public ContractSO CurrentContract { get; private set; } 

    [SerializeField] private Camera mainCamera;
    private Transform mainCamParent;
    private Vector3 mainCamLocalPos;
    private Quaternion mainCamLocalRot;
    private Transform currentPlayerTransform;

    [Header("UI Management")]
    [SerializeField] private List<GameObject> uiElementsToHide = new List<GameObject>();
    [SerializeField] private List<GameObject> buildModeUIElements = new List<GameObject>();

    [Header("Open World UI")]
    public GameObject redoConfirmPanel;
    private BuildLocation pendingRedoLocation;

    private void Awake()
    {
        Instance = this; 

        if (mainCamera == null) mainCamera = Camera.main;
        
        foreach (GameObject uiElement in buildModeUIElements) 
        {
            if (uiElement != null) uiElement.SetActive(false);
        }

        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(false);
    }

    private void Start()
    {
        Application.targetFrameRate = 100000;
    }

    private void Update()
    {
        if (CurrentState == GameState.Building && Input.GetKeyDown(KeyCode.Escape)) ExitBuildMode();
    }

    public void ShowRedoConfirmPanel(BuildLocation loc)
    {
        pendingRedoLocation = loc;
        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(true);
        
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        { 
            inputObj.SetPlayerInputEnable(false); 
            inputObj.SetLookEnabled(false); 
        }
    }

    public void ConfirmRedo()
    {
        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(false);
        if (pendingRedoLocation != null)
        {
            pendingRedoLocation.DeleteBakedBridge(); 
            
            PlayerMotor player = FindObjectOfType<PlayerMotor>();
            if (player != null) pendingRedoLocation.ActivateBuildMode(player.transform);
        }
        pendingRedoLocation = null;
    }

    public void CancelRedo()
    {
        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(false);
        pendingRedoLocation = null;
        
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        { 
            inputObj.SetPlayerInputEnable(true); 
            inputObj.SetLookEnabled(true); 
        }
    }

    public void EnterBuildMode(BuildLocation location, Transform player)
    {
        if (CurrentState == GameState.Building) return;

        if (LevelCompleteManager.Instance != null)
        {
            LevelCompleteManager.Instance.ResetCompletionState();
        }

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

        // --- FAIL-SAFE 1: Guarantee the UI comes back instantly! ---
        foreach (GameObject uiElement in uiElementsToHide) if (uiElement != null) uiElement.SetActive(true);
        foreach (GameObject uiElement in buildModeUIElements) if (uiElement != null) uiElement.SetActive(false);

        // --- FAIL-SAFE 2: Unlock player controls instantly! ---
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(true);
            inputObj.SetLookEnabled(true);
        }
        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = true;

        OnExitBuildMode?.Invoke();

        if (mainCamera != null && ActiveBuildLocation != null)
        {
            if (ActiveBuildLocation.locationCamera != null)
            {
                ActiveBuildLocation.locationCamera.enabled = false;
                mainCamera.enabled = true;
            }

            // THE FIX: Unconditionally snap the camera back, even if parent is null!
            mainCamera.transform.SetParent(mainCamParent);
            mainCamera.transform.localPosition = mainCamLocalPos;
            mainCamera.transform.localRotation = mainCamLocalRot;
        }

        if (ActiveBuildLocation != null)
        {
            if (currentPlayerTransform != null) ActiveBuildLocation.DeactivateBuildMode(currentPlayerTransform);
        }

        // --- THE FIX: Wipe the GameManager's memory clean for the next ravine! ---
        currentPlayerTransform = null;
        ActiveBuildLocation = null; 
        CurrentContract = null;
    }

    public bool IsInBuildMode() => CurrentState == GameState.Building;
}