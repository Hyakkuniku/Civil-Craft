using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using TMPro;

[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Normal, Building }
    public GameState CurrentState { get; private set; } = GameState.Normal;
    public bool IsTransitioning => isTransitioning;

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
    private readonly Dictionary<GameObject, bool> uiStateBeforeBuildMode = new Dictionary<GameObject, bool>();
    private readonly Dictionary<GameObject, bool> uiStateBeforeRedo = new Dictionary<GameObject, bool>();
    private readonly Dictionary<GameObject, bool> buildLocationStateBeforeBuildMode = new Dictionary<GameObject, bool>();

    private void Awake()
    {
        Instance = this; 

        if (mainCamera == null) mainCamera = Camera.main;
        
        foreach (GameObject uiElement in buildModeUIElements) 
        {
            if (uiElement != null) uiElement.SetActive(false);
        }

        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(false);

        RefreshRedoConfirmationCopy();

        HideTransitionFader();
    }

    private void OnDisable()
    {
        isTransitioning = false;
        RestoreCapturedStates(buildLocationStateBeforeBuildMode);
        HideTransitionFader();
    }

    private void Update()
    {
        bool cancelPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                             (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        if (CurrentState == GameState.Building && !isTransitioning && cancelPressed)
        {
            ExitBuildMode();
        }
    }

    public void ShowRedoConfirmPanel(BuildLocation loc)
    {
        if (isTransitioning || loc == null || loc.IsRedesignBlockedByNPCTravel) return;

        pendingRedoLocation = loc;
        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(true);
        
        CaptureAndHide(uiElementsToHide, uiStateBeforeRedo);
            
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
        if (pendingRedoLocation != null && pendingRedoLocation.IsRedesignBlockedByNPCTravel)
        {
            Debug.LogWarning("Bridge redesign was cancelled because an NPC phase transition is in progress.");
            CancelRedo();
            return;
        }

        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(false);

        // Restore first so EnterBuildMode can capture the true overworld state.
        // This occurs in the same frame and does not produce a visible flash.
        RestoreCapturedStates(uiStateBeforeRedo);
        
        foreach (GameObject uiElement in extraElementsToHideOnRedo) 
            if (uiElement != null) uiElement.SetActive(true);

        if (pendingRedoLocation != null)
        {
            BuildLocation redesignLocation = pendingRedoLocation;
            PlayerMotor player = FindObjectOfType<PlayerMotor>();
            bool redesignStarted = redesignLocation.BeginBridgeRedesign();
            bool enteredBuildMode = redesignStarted && player != null &&
                                    redesignLocation.ActivateBuildMode(player.transform);

            // Entering build mode can still be rejected by a tutorial or another
            // transition. Never leave the committed bridge hidden in that case.
            if (redesignStarted && !enteredBuildMode)
                redesignLocation.CancelBridgeRedesign();
        }
        pendingRedoLocation = null;
    }

    public void CancelRedo()
    {
        if (redoConfirmPanel != null) redoConfirmPanel.SetActive(false);
        pendingRedoLocation = null;
        
        RestoreCapturedStates(uiStateBeforeRedo);
            
        foreach (GameObject uiElement in extraElementsToHideOnRedo) 
            if (uiElement != null) uiElement.SetActive(true);

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null) 
        { 
            inputObj.SetPlayerInputEnable(true); 
            inputObj.SetLookEnabled(true); 
        }
    }

    public bool EnterBuildMode(BuildLocation location, Transform player)
    {
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsTutorialActive)
        {
            Debug.LogWarning("Build Mode entry blocked while a tutorial is active.");
            return false;
        }

        if (CurrentState == GameState.Building || isTransitioning) return false;
        
        StartCoroutine(EnterBuildModeRoutine(location, player));
        return true;
    }

    private IEnumerator EnterBuildModeRoutine(BuildLocation location, Transform player)
    {
        isTransitioning = true;
        try
        {
        CurrentState = GameState.Building;
        currentPlayerTransform = player;
        ActiveBuildLocation = location;
        HideInactiveBuildLocations(location);

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

        CaptureAndHide(uiElementsToHide, uiStateBeforeBuildMode);

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
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                    
                    mainCamera.transform.position = Vector3.Lerp(startPos, location.blueprintDiveTarget.position, t);
                    mainCamera.transform.rotation = Quaternion.Slerp(startRot, location.blueprintDiveTarget.rotation, t);
                    yield return null;
                }
            }

            // --- FADE OUT TO BLACK ---
            if (transitionFader != null)
            {
                transitionFader.gameObject.SetActive(true);
                transitionFader.blocksRaycasts = true;
                float elapsedFade = 0f;
                while (elapsedFade < fadeDuration)
                {
                    elapsedFade += Time.unscaledDeltaTime;
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
        InvokeEventSafely(OnEnterBuildMode);

        // BuildUI may only have awakened when its parent was enabled above, so do not
        // rely exclusively on its OnEnterBuildMode subscription.
        if (BuildUIController.Instance != null)
            BuildUIController.Instance.RefreshContractBuildUI();

        // --- FADE IN TO CLEAR ---
        if (transitionFader != null)
        {
            float elapsedFade = 0f;
            while (elapsedFade < fadeDuration)
            {
                elapsedFade += Time.unscaledDeltaTime;
                transitionFader.alpha = Mathf.Lerp(1f, 0f, elapsedFade / fadeDuration);
                yield return null;
            }
            HideTransitionFader();
        }

        }
        finally
        {
            HideTransitionFader();
            isTransitioning = false;
        }
    }

    public void ExitBuildMode()
    {
        BridgePhysicsManager physicsManager = FindObjectOfType<BridgePhysicsManager>();
        if (physicsManager != null && physicsManager.IsSimulationActive)
        {
            Debug.LogWarning("Build Mode exit is locked while bridge simulation is running.");
            return;
        }

        if (BuildTutorialDirector.Instance != null && BuildTutorialDirector.Instance.isTutorialRunning)
        {
            Debug.LogWarning("Build Mode exit blocked while the build tutorial is active.");
            return;
        }

        if (CurrentState == GameState.Normal || isTransitioning) return;
        
        StartCoroutine(ExitBuildModeRoutine());
    }

    private IEnumerator ExitBuildModeRoutine()
    {
        isTransitioning = true;
        try
        {
        CurrentState = GameState.Normal;

        // --- FADE OUT TO BLACK ---
        if (transitionFader != null)
        {
            transitionFader.gameObject.SetActive(true);
            transitionFader.blocksRaycasts = true;
            float elapsedFade = 0f;
            while (elapsedFade < fadeDuration)
            {
                elapsedFade += Time.unscaledDeltaTime;
                transitionFader.alpha = Mathf.Lerp(0f, 1f, elapsedFade / fadeDuration);
                yield return null;
            }
            transitionFader.alpha = 1f;
        }

        // Restore the open world while the screen is black, before returning
        // the camera to the player.
        if (ActiveBuildLocation != null && ActiveBuildLocation.IsRedesigningBridge)
            ActiveBuildLocation.CancelBridgeRedesign();
        RestoreCapturedStates(buildLocationStateBeforeBuildMode);

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
                elapsedFade += Time.unscaledDeltaTime;
                transitionFader.alpha = Mathf.Lerp(1f, 0f, elapsedFade / fadeDuration);
                yield return null;
            }
            HideTransitionFader();
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
                elapsed += Time.unscaledDeltaTime;
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
        RestoreCapturedStates(uiStateBeforeBuildMode);
        MinimapUnlockController.RefreshAll();

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(true);
            inputObj.SetLookEnabled(true);
        }
        
        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = true;

        InvokeEventSafely(OnExitBuildMode);

        if (ActiveBuildLocation != null && currentPlayerTransform != null)
        {
            ActiveBuildLocation.DeactivateBuildMode(currentPlayerTransform);
        }

        currentPlayerTransform = null;
        ActiveBuildLocation = null; 
        CurrentContract = null;
        }
        finally
        {
            HideTransitionFader();
            isTransitioning = false;
        }
    }

    public bool IsInBuildMode() => CurrentState == GameState.Building;

    private void RefreshRedoConfirmationCopy()
    {
        if (redoConfirmPanel == null) return;

        foreach (TMP_Text label in redoConfirmPanel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label != null && label.text.Contains("remove your current structure"))
                label.text = "Your current bridge will stay until the redesign is completed";
        }
    }

    private void HideTransitionFader()
    {
        if (transitionFader == null) return;
        transitionFader.alpha = 0f;
        transitionFader.interactable = false;
        transitionFader.blocksRaycasts = false;
        transitionFader.gameObject.SetActive(false);
    }

    private static void InvokeEventSafely(UnityEvent targetEvent)
    {
        if (targetEvent == null) return;
        try
        {
            targetEvent.Invoke();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void CaptureAndHide(
        List<GameObject> targets,
        Dictionary<GameObject, bool> capturedStates)
    {
        capturedStates.Clear();
        if (targets == null) return;

        foreach (GameObject target in targets)
        {
            if (target == null || capturedStates.ContainsKey(target)) continue;
            capturedStates.Add(target, target.activeSelf);
            target.SetActive(false);
        }
    }

    private static void RestoreCapturedStates(Dictionary<GameObject, bool> capturedStates)
    {
        foreach (KeyValuePair<GameObject, bool> state in capturedStates)
        {
            if (state.Key != null)
                state.Key.SetActive(state.Value);
        }

        capturedStates.Clear();
    }

    private void HideInactiveBuildLocations(BuildLocation activeLocation)
    {
        RestoreCapturedStates(buildLocationStateBeforeBuildMode);
        if (activeLocation == null) return;

        List<GameObject> activeTargets = new List<GameObject>();
        activeLocation.AppendBuildModeIsolationTargets(activeTargets);

        BuildLocation[] locations = FindObjectsOfType<BuildLocation>(true);
        foreach (BuildLocation location in locations)
        {
            if (location == null || location == activeLocation ||
                !location.hideWhenAnotherBuildLocationIsActive ||
                !location.gameObject.scene.IsValid() ||
                !location.gameObject.scene.isLoaded ||
                location.gameObject.scene != activeLocation.gameObject.scene)
                continue;

            List<GameObject> targets = new List<GameObject>();
            location.AppendBuildModeIsolationTargets(targets);
            foreach (GameObject target in targets)
            {
                if (target == null || buildLocationStateBeforeBuildMode.ContainsKey(target) ||
                    OverlapsActiveSite(target, activeTargets))
                    continue;

                buildLocationStateBeforeBuildMode.Add(target, target.activeSelf);
                target.SetActive(false);
            }
        }
    }

    private static bool OverlapsActiveSite(GameObject candidate, List<GameObject> activeTargets)
    {
        if (candidate == null || activeTargets == null) return false;

        Transform candidateTransform = candidate.transform;
        foreach (GameObject activeTarget in activeTargets)
        {
            if (activeTarget == null) continue;
            Transform activeTransform = activeTarget.transform;
            if (candidate == activeTarget || candidateTransform.IsChildOf(activeTransform) ||
                activeTransform.IsChildOf(candidateTransform))
                return true;
        }

        return false;
    }
}
