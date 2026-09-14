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

    public enum GameState { Normal, Building, CargoTesting }
    public bool IsCargoTestActive => cargoTestPhysics != null;
    private BridgePhysicsManager cargoTestPhysics;
    private CargoItem.TestSnapshot cargoSnapshot;
    private Vector3 cargoTestPlayerPosition;
    private Quaternion cargoTestPlayerRotation;
    private bool cargoTestDeterministic, cargoTestVisualizer;
    private readonly Dictionary<Behaviour, bool> cargoBuildUI = new Dictionary<Behaviour, bool>();
    private bool cargoViewEntered;
    private bool cargoGridVisible;
    private UnityEngine.UI.Button cargoCancelButton;

    private bool RejectCargoTest(string message)
    {
        Debug.LogWarning("[CargoTest] " + message, this);
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction(message);
        return false;
    }

    public bool TryBeginCargoTest(BridgePhysicsManager physics)
    {
        if (CurrentState != GameState.Building || isTransitioning || IsCargoTestActive ||
            physics == null || currentPlayerTransform == null || ActiveBuildLocation == null) return false;
        CargoItem cargo = ActiveBuildLocation.testCargo;
        if (cargo == null)
            return RejectCargoTest("No cargo assigned to " + ActiveBuildLocation.name + ". Assign Linked Cargo on the NPC contract phase or Test Cargo on this workbench.");
        if (!cargo.gameObject.activeInHierarchy)
            return RejectCargoTest("Test cargo '" + cargo.name + "' is inactive. Enable it and its parents before testing.");
        if (cargo.IsPermanentlyLoaded)
            return RejectCargoTest("This cargo is permanently loaded in a vehicle. Assign a different item for a player-carried crossing test.");
        if (cargo.DeliveryRestorePending)
            return RejectCargoTest("Cargo delivery restoration is pending. Check the Console for a missing saved drop location.");
        if (CurrentContract == null || CurrentContract.liveLoadMode != ContractSO.LiveLoadMode.PlayerCarriedCargo ||
            CurrentContract.winCondition != ContractSO.WinCondition.FinishLine)
            return RejectCargoTest("Cargo contracts must use PlayerCarriedCargo and the FinishLine win condition.");
        bool hasFinish = false;
        foreach (CargoDropLocation finish in FindObjectsOfType<CargoDropLocation>())
        {
            BoxCollider zone = finish.GetComponent<BoxCollider>();
            if (finish.isActiveAndEnabled && finish.assignedContract == CurrentContract &&
                zone != null && zone.enabled && zone.isTrigger) hasFinish = true;
        }
        if (!hasFinish || LevelCompleteManager.Instance == null)
        {
            return RejectCargoTest("Assign an active Cargo Drop Location with Is Trigger enabled to this exact contract, and a completion manager.");
        }
        cargoCancelButton = ActiveBuildLocation.cargoTestCancelButton != null
            ? ActiveBuildLocation.cargoTestCancelButton.GetComponent<UnityEngine.UI.Button>() : null;
        foreach (GameObject ui in buildModeUIElements)
        {
            if (ui != null && cargoCancelButton != null && cargoCancelButton.transform.IsChildOf(ui.transform))
            {
                return RejectCargoTest("Place the cancel button on the overworld Canvas, not inside the hidden BuildCanvas.");
            }
        }
        if (CargoItem.IsCarriedBy(currentPlayerTransform) && !cargo.IsHeldBy(currentPlayerTransform))
        {
            Debug.LogWarning("[CargoTest] Put down the other cargo before starting this contract's test.", this);
            return false;
        }
        // Capture the previous drop location AND its pickup lock before unlocking reuse.
        cargoSnapshot = cargo.CaptureTestState();
        cargoGridVisible = ActiveBuildLocation.IsGridVisualActive;
        if (cargoCancelButton != null) cargoCancelButton.onClick.AddListener(CancelCargoTest);
        cargo.SetWeight(CurrentContract.liveLoadWeight);
        cargoTestPlayerPosition = currentPlayerTransform.position;
        cargoTestPlayerRotation = currentPlayerTransform.rotation;
        cargoTestPhysics = physics;
        cargoTestDeterministic = physics.useDeterministicStressAnalysis;
        cargoTestVisualizer = physics.enableVisualizer;
        if (!cargo.BeginPlayerCargoTest(CurrentContract))
        {
            CancelCargoTest();
            return RejectCargoTest("Could not unlock the assigned cargo for this crossing test.");
        }
        physics.useDeterministicStressAnalysis = false;
        physics.enableVisualizer = true;
        physics.ActivatePhysics();
        StartCoroutine(EnterCargoTestView());
        return true;
    }

    private IEnumerator EnterCargoTestView()
    {
        while (cargoTestPhysics != null && !cargoTestPhysics.isSimulating && cargoTestPhysics.IsSimulationActive)
            yield return null;
        if (cargoTestPhysics == null) yield break;
        if (!cargoTestPhysics.isSimulating) { CancelCargoTest(); yield break; }
        CurrentState = GameState.CargoTesting;
        cargoViewEntered = true;
        // Keep simulation scripts alive even when they live beneath BuildCanvas.
        cargoBuildUI.Clear();
        foreach (GameObject ui in buildModeUIElements)
        {
            if (ui == null) continue;
            foreach (Canvas canvas in ui.GetComponentsInChildren<Canvas>(true))
            { if (!cargoBuildUI.ContainsKey(canvas)) cargoBuildUI.Add(canvas, canvas.enabled); canvas.enabled = false; }
            foreach (UnityEngine.UI.GraphicRaycaster raycaster in ui.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true))
            { if (!cargoBuildUI.ContainsKey(raycaster)) cargoBuildUI.Add(raycaster, raycaster.enabled); raycaster.enabled = false; }
        }
        RestoreDecorativeCanyons();
        SetOtherBuildLocationsVisibleForCargoTest(true);
        ActiveBuildLocation.SetGridVisualActive(false);
        if (ActiveBuildLocation.locationCamera != null) ActiveBuildLocation.locationCamera.enabled = false;
        if (mainCamera != null)
        {
            mainCamera.transform.SetParent(mainCamParent, false);
            mainCamera.transform.localPosition = mainCamLocalPos;
            mainCamera.transform.localRotation = mainCamLocalRot;
            mainCamera.enabled = true;
        }
        // Do not invoke OnExitBuildMode: that would discard the simulation/draft.
        foreach (var entry in uiStateBeforeBuildMode) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        SetCargoPlayerControl(true);
        if (ActiveBuildLocation.cargoTestCancelButton != null) ActiveBuildLocation.cargoTestCancelButton.SetActive(true);
        while (cargoTestPhysics != null && CurrentState == GameState.CargoTesting)
        {
            if (currentPlayerTransform == null || ActiveBuildLocation.testCargo == null ||
                currentPlayerTransform.position.y < cargoTestPlayerPosition.y - 8f ||
                ActiveBuildLocation.testCargo.transform.position.y < cargoSnapshot.position.y - 8f ||
                cargoTestPhysics.HadBrokenPartsThisRun || !cargoTestPhysics.IsSimulationActive)
            { CancelCargoTest(); yield break; }
            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame))
            { CancelCargoTest(); yield break; }
            yield return null;
        }
    }

    private void SetCargoPlayerControl(bool enabled)
    {
        InputManager input = FindObjectOfType<InputManager>();
        if (input != null) { input.SetPlayerInputEnable(enabled); input.SetLookEnabled(enabled); }
        if (currentPlayerTransform == null) return;
        PlayerMotor motor = currentPlayerTransform.GetComponent<PlayerMotor>();
        if (motor != null) motor.enabled = enabled;
        PlayerInteract interaction = currentPlayerTransform.GetComponent<PlayerInteract>();
        if (interaction != null) interaction.enabled = enabled;
    }

    private void RestoreCargoBuildView()
    {
        if (!cargoViewEntered) return;
        cargoViewEntered = false;
        CurrentState = GameState.Building;
        SetCargoPlayerControl(false);
        foreach (var entry in uiStateBeforeBuildMode) if (entry.Key != null) entry.Key.SetActive(false);
        foreach (var entry in cargoBuildUI) if (entry.Key != null) entry.Key.enabled = entry.Value;
        cargoBuildUI.Clear();
        SetOtherBuildLocationsVisibleForCargoTest(false);
        HideDecorativeCanyons();
        if (mainCamera != null) mainCamera.enabled = false;
        if (ActiveBuildLocation.locationCamera != null) ActiveBuildLocation.locationCamera.enabled = true;
        ActiveBuildLocation.SetGridVisualActive(cargoGridVisible);
        if (ActiveBuildLocation.cargoTestCancelButton != null) ActiveBuildLocation.cargoTestCancelButton.SetActive(false);
    }

    public void CancelCargoTest()
    {
        if (!IsCargoTestActive) return;
        BridgePhysicsManager physics = cargoTestPhysics;
        cargoTestPhysics = null;
        if (cargoCancelButton != null) cargoCancelButton.onClick.RemoveListener(CancelCargoTest);
        RestoreCargoBuildView();
        physics.StopPhysicsAndReset();
        physics.useDeterministicStressAnalysis = cargoTestDeterministic;
        physics.enableVisualizer = cargoTestVisualizer;
        if (currentPlayerTransform != null)
        {
            CharacterController cc = currentPlayerTransform.GetComponent<CharacterController>();
            bool wasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            currentPlayerTransform.SetPositionAndRotation(cargoTestPlayerPosition, cargoTestPlayerRotation);
            PlayerMotor motor = currentPlayerTransform.GetComponent<PlayerMotor>();
            if (motor != null) motor.ResetTestMotion();
            if (cc != null) cc.enabled = wasEnabled;
        }
        if (ActiveBuildLocation.testCargo != null) ActiveBuildLocation.testCargo.RestoreTestState(cargoSnapshot);
        BarCreator creator = FindObjectOfType<BarCreator>(true);
        if (creator != null) creator.isSimulating = false;
        Physics.SyncTransforms();
    }

    public bool TryCompleteCargoTest(ContractSO contract, Collider entrant, CargoDropLocation dropLocation = null)
    {
        if (CurrentState != GameState.CargoTesting || !IsCargoTestActive || contract == null ||
            contract != CurrentContract || !cargoTestPhysics.isSimulating || cargoTestPhysics.HadBrokenPartsThisRun ||
            ActiveBuildLocation == null || ActiveBuildLocation.testCargo == null || entrant == null ||
            LevelCompleteManager.Instance == null ||
            (LevelFailedManager.Instance != null && LevelFailedManager.Instance.isFailed) ||
            entrant.GetComponentInParent<PlayerMotor>() == null ||
            entrant.GetComponentInParent<PlayerMotor>().transform != currentPlayerTransform ||
            !ActiveBuildLocation.testCargo.IsHeldBy(currentPlayerTransform) ||
            Vector3.Distance(currentPlayerTransform.position, cargoTestPlayerPosition) < 2f) return false;
        float stressLimit = contract.enforceMaxStress ? contract.maxAllowedStress / 100f : 1f;
        if (!BridgePhysicsManager.DebugInvincibleBridge && cargoTestPhysics.peakStressThisRun >= stressLimit)
            return false;
        if (dropLocation == null || dropLocation.assignedContract != contract ||
            !ActiveBuildLocation.testCargo.PlaceAtDropLocation(dropLocation)) return false;
        BridgePhysicsManager physics = cargoTestPhysics;
        cargoTestPhysics = null;
        if (cargoCancelButton != null) cargoCancelButton.onClick.RemoveListener(CancelCargoTest);
        RestoreCargoBuildView();
        physics.useDeterministicStressAnalysis = cargoTestDeterministic;
        physics.enableVisualizer = cargoTestVisualizer;
        physics.lockStressTracking = true;
        LevelCompleteManager.Instance.CompleteLevel(contract);
        return true;
    }
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

    [Header("Build Mode - Decorative Canyon Visibility")]
    [Tooltip("Hide the renderers on these decorative map objects and their children while building. Colliders and scripts remain active. Assign scenery only, not build-location roots, anchors, or bridges.")]
    [SerializeField] private List<GameObject> decorativeCanyonsToHide = new List<GameObject>();
    private readonly Dictionary<Renderer, bool> canyonRenderingStateBeforeBuildMode = new Dictionary<Renderer, bool>();

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
        RestoreDecorativeCanyons();
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

        if (CurrentState != GameState.Normal || isTransitioning) return false;
        
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
        HideDecorativeCanyons();
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
        if (IsCargoTestActive) { CancelCargoTest(); return; }
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
        if (ActiveBuildLocation != null)
            ActiveBuildLocation.HideUnfinishedBridgeDraft();
        RestoreCapturedStates(buildLocationStateBeforeBuildMode);

        // 1. Hide Build Mode UI instantly
        RestoreDecorativeCanyons();
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

    private void HideDecorativeCanyons()
    {
        RestoreDecorativeCanyons();
        if (decorativeCanyonsToHide == null) return;

        foreach (GameObject canyon in decorativeCanyonsToHide)
        {
            if (canyon == null) continue;
            foreach (Renderer visual in canyon.GetComponentsInChildren<Renderer>(true))
            {
                // Overlapping parent/child entries must not overwrite the original state.
                if (visual == null || canyonRenderingStateBeforeBuildMode.ContainsKey(visual)) continue;
                canyonRenderingStateBeforeBuildMode.Add(visual, visual.forceRenderingOff);
                visual.forceRenderingOff = true;
            }
        }
    }

    private void RestoreDecorativeCanyons()
    {
        foreach (KeyValuePair<Renderer, bool> state in canyonRenderingStateBeforeBuildMode)
        {
            if (state.Key != null) state.Key.forceRenderingOff = state.Value;
        }
        canyonRenderingStateBeforeBuildMode.Clear();
    }

    /// <summary>
    /// Build Mode isolates the active site by disabling the other Build Locations.
    /// A player-carried test takes place in the real world, so temporarily restore
    /// those captured environment states without clearing them. If the test is
    /// cancelled, the same objects can be hidden again for continued editing.
    /// </summary>
    private void SetOtherBuildLocationsVisibleForCargoTest(bool visible)
    {
        foreach (KeyValuePair<GameObject, bool> state in buildLocationStateBeforeBuildMode)
        {
            if (state.Key != null)
                state.Key.SetActive(visible ? state.Value : false);
        }

        if (!visible) return;

        // Restore the surrounding terrain and completed bridges, but preserve the
        // rule that unfinished work at unrelated sites is invisible in 3D space.
        foreach (BuildLocation location in FindObjectsOfType<BuildLocation>(true))
        {
            if (location != null && location != ActiveBuildLocation &&
                location.gameObject.scene.IsValid() && ActiveBuildLocation != null &&
                location.gameObject.scene == ActiveBuildLocation.gameObject.scene)
            {
                location.HideUnfinishedBridgeDraft();
            }
        }
    }

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
