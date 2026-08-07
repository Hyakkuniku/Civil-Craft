using UnityEngine;
using UnityEngine.Events;
using System.Collections;
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
    [Tooltip("Add things here that you want hidden ONLY when the Redo Panel is open (Optional)")]
    public List<GameObject> extraElementsToHideOnRedo = new List<GameObject>(); 
    
    // --- NEW: CINEMATIC FADER ---
    [Header("Cinematic Transition Fader")]
    [Tooltip("Drag a CanvasGroup attached to a full-screen black panel here.")]
    public CanvasGroup transitionFader;
    [Tooltip("How fast the screen fades to black during the camera swap.")]
    public float fadeDuration = 0.25f;

    private BuildLocation pendingRedoLocation;
    private bool isTransitioning = false; 

    private void Awake()
    {
        Instance = this; 

        if (mainCamera == null) mainCamera = Camera.main;
        
        foreach (GameObject uiElement in buildModeUIElements) 
        {
            if (uiElement != null) uiElement.SetActive(false);
        }

        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(false);

        // --- THE FIX: Wake up the fader even if you turned it off in the Inspector! ---
        if (transitionFader != null)
        {
            transitionFader.gameObject.SetActive(true); 
            transitionFader.alpha = 0f;
            transitionFader.blocksRaycasts = false;
        }
    }

    private void Update()
    {
        if (CurrentState == GameState.Building && !isTransitioning && Input.GetKeyDown(KeyCode.Escape)) 
        {
            ExitBuildMode();
        }
    }

    public void ShowRedoConfirmPanel(BuildLocation loc)
    {
        if (isTransitioning) return;

        pendingRedoLocation = loc;
        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(true);
        
        foreach (GameObject uiElement in uiElementsToHide) 
            if (uiElement != null) uiElement.SetActive(false);
            
        foreach (GameObject uiElement in extraElementsToHideOnRedo) 
            if (uiElement != null) uiElement.SetActive(false);

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
        
        foreach (GameObject uiElement in extraElementsToHideOnRedo) 
            if (uiElement != null) uiElement.SetActive(true);

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
        
        foreach (GameObject uiElement in uiElementsToHide) 
            if (uiElement != null) uiElement.SetActive(true);
            
        foreach (GameObject uiElement in extraElementsToHideOnRedo) 
            if (uiElement != null) uiElement.SetActive(true);

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        { 
            inputObj.SetPlayerInputEnable(true); 
            inputObj.SetLookEnabled(true); 
        }
    }

    public void EnterBuildMode(BuildLocation location, Transform player)
    {
        if (CurrentState == GameState.Building || isTransitioning) return;
        
        StartCoroutine(EnterBuildModeRoutine(location, player));
    }

    private IEnumerator EnterBuildModeRoutine(BuildLocation location, Transform player)
    {
        isTransitioning = true;
        CurrentState = GameState.Building;
        currentPlayerTransform = player;
        ActiveBuildLocation = location;

        if (LevelCompleteManager.Instance != null)
            LevelCompleteManager.Instance.ResetCompletionState();

        if (location != null && location.activeContract != null)
            CurrentContract = location.activeContract; 

        if (BuildUIController.Instance != null && CurrentContract != null)
            BuildUIController.Instance.maxBudget = CurrentContract.budget;

        // 1. Freeze the player and hide Overworld UI instantly
        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        { 
            inputObj.SetPlayerInputEnable(false); 
            inputObj.SetLookEnabled(false); 
        }
        
        PlayerMotor motor = FindObjectOfType<PlayerMotor>();
        if (motor != null) motor.enabled = false;

        foreach (GameObject uiElement in uiElementsToHide) if (uiElement != null) uiElement.SetActive(false);

        // 2. Unparent and animate the Main Camera down to the blueprint
        if (mainCamera != null)
        {
            mainCamParent = mainCamera.transform.parent;
            mainCamLocalPos = mainCamera.transform.localPosition;
            mainCamLocalRot = mainCamera.transform.localRotation;
            mainCamera.transform.SetParent(null); 

            if (location.blueprintDiveTarget != null)
            {
                Vector3 startPos = mainCamera.transform.position;
                Quaternion startRot = mainCamera.transform.rotation;
                float duration = location.diveDuration;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                    
                    mainCamera.transform.position = Vector3.Lerp(startPos, location.blueprintDiveTarget.position, t);
                    mainCamera.transform.rotation = Quaternion.Slerp(startRot, location.blueprintDiveTarget.rotation, t);
                    yield return null;
                }
            }

            // --- FADE OUT TO BLACK ---
            if (transitionFader != null)
            {
                transitionFader.blocksRaycasts = true;
                float elapsedFade = 0f;
                while (elapsedFade < fadeDuration)
                {
                    elapsedFade += Time.deltaTime;
                    transitionFader.alpha = Mathf.Lerp(0f, 1f, elapsedFade / fadeDuration);
                    yield return null;
                }
                transitionFader.alpha = 1f;
            }

            // 3. Swap to the 2D Location Camera behind the black screen
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

        // 4. Show Build Mode UI while the screen is black
        foreach (GameObject uiElement in buildModeUIElements) if (uiElement != null) uiElement.SetActive(true);
        OnEnterBuildMode?.Invoke();

        // --- FADE IN TO CLEAR ---
        if (transitionFader != null)
        {
            float elapsedFade = 0f;
            while (elapsedFade < fadeDuration)
            {
                elapsedFade += Time.deltaTime;
                transitionFader.alpha = Mathf.Lerp(1f, 0f, elapsedFade / fadeDuration);
                yield return null;
            }
            transitionFader.alpha = 0f;
            transitionFader.blocksRaycasts = false;
        }

        isTransitioning = false;
    }

    public void ExitBuildMode()
    {
        if (CurrentState == GameState.Normal || isTransitioning) return;
        
        StartCoroutine(ExitBuildModeRoutine());
    }

    private IEnumerator ExitBuildModeRoutine()
    {
        isTransitioning = true;
        CurrentState = GameState.Normal;

        // --- FADE OUT TO BLACK ---
        if (transitionFader != null)
        {
            transitionFader.blocksRaycasts = true;
            float elapsedFade = 0f;
            while (elapsedFade < fadeDuration)
            {
                elapsedFade += Time.deltaTime;
                transitionFader.alpha = Mathf.Lerp(0f, 1f, elapsedFade / fadeDuration);
                yield return null;
            }
            transitionFader.alpha = 1f;
        }

        // 1. Hide Build Mode UI instantly
        foreach (GameObject uiElement in buildModeUIElements) if (uiElement != null) uiElement.SetActive(false);

        // 2. Prepare the camera swap behind the black screen
        if (mainCamera != null && ActiveBuildLocation != null)
        {
            if (ActiveBuildLocation.locationCamera != null)
            {
                ActiveBuildLocation.locationCamera.enabled = false;
                mainCamera.enabled = true;
            }

            if (ActiveBuildLocation.blueprintDiveTarget != null && mainCamParent != null)
            {
                mainCamera.transform.position = ActiveBuildLocation.blueprintDiveTarget.position;
                mainCamera.transform.rotation = ActiveBuildLocation.blueprintDiveTarget.rotation;
            }
            else
            {
                mainCamera.transform.SetParent(mainCamParent);
                mainCamera.transform.localPosition = mainCamLocalPos;
                mainCamera.transform.localRotation = mainCamLocalRot;
            }
        }

        // --- FADE IN TO CLEAR ---
        if (transitionFader != null)
        {
            float elapsedFade = 0f;
            while (elapsedFade < fadeDuration)
            {
                elapsedFade += Time.deltaTime;
                transitionFader.alpha = Mathf.Lerp(1f, 0f, elapsedFade / fadeDuration);
                yield return null;
            }
            transitionFader.alpha = 0f;
            transitionFader.blocksRaycasts = false;
        }

        // 3. Animate the camera pulling back out of the blueprint
        if (mainCamera != null && ActiveBuildLocation != null && ActiveBuildLocation.blueprintDiveTarget != null && mainCamParent != null)
        {
            Vector3 startPos = mainCamera.transform.position;
            Quaternion startRot = mainCamera.transform.rotation;
            float duration = ActiveBuildLocation.diveDuration;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                
                Vector3 targetWorldPos = mainCamParent.TransformPoint(mainCamLocalPos);
                Quaternion targetWorldRot = mainCamParent.rotation * mainCamLocalRot;

                mainCamera.transform.position = Vector3.Lerp(startPos, targetWorldPos, t);
                mainCamera.transform.rotation = Quaternion.Slerp(startRot, targetWorldRot, t);
                
                yield return null;
            }

            mainCamera.transform.SetParent(mainCamParent);
            mainCamera.transform.localPosition = mainCamLocalPos;
            mainCamera.transform.localRotation = mainCamLocalRot;
        }

        // 4. Restore Overworld UI and Unfreeze Player
        foreach (GameObject uiElement in uiElementsToHide) if (uiElement != null) uiElement.SetActive(true);

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(true);
            inputObj.SetLookEnabled(true);
        }
        
        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = true;

        OnExitBuildMode?.Invoke();

        if (ActiveBuildLocation != null && currentPlayerTransform != null)
        {
            ActiveBuildLocation.DeactivateBuildMode(currentPlayerTransform);
        }

        currentPlayerTransform = null;
        ActiveBuildLocation = null; 
        CurrentContract = null;
        isTransitioning = false;
    }

    public bool IsInBuildMode() => CurrentState == GameState.Building;
}