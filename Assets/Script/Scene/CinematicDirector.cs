using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;

[System.Serializable]
public class CinematicShot
{
    [Header("Shot Info")]
    public string shotName = "New Shot";
    [Tooltip("How long this specific shot lasts in seconds.")]
    public float duration = 4f;

    [Header("Camera Movement")]
    [Tooltip("Leave empty to start from the camera's current position.")]
    public Transform cameraStartPoint;
    [Tooltip("Where the camera will smoothly move and rotate towards.")]
    public Transform cameraEndPoint;

    [Header("Player Movement (Optional)")]
    [Tooltip("Leave empty to start from the player's current position.")]
    public Transform playerStartPoint;
    [Tooltip("Where the player will walk towards.")]
    public Transform playerWalkTarget;
    public bool playWalkAnimation = true;

    // --- NEW: Bridge Info UI Toggle ---
    [Header("Bridge Info Overlay")]
    [Tooltip("Check this to display the Bridge Info Canvas during this specific shot.")]
    public bool showBridgeInfoCanvas = false;

    [Header("Timing")]
    public AnimationCurve movementCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Delay before moving to the next shot.")]
    public float postShotDelay = 0.5f;
}

public class CinematicDirector : MonoBehaviour
{
    [Header("Cinematic Identity")]
    [Tooltip("A unique name for this cutscene (e.g., 'MainIntro').")]
    public string cinematicID = "IntroCutscene";
    [Tooltip("If true, it saves to PlayerPrefs and will never play again on this save file.")]
    public bool playOnlyOnce = true;
    [Tooltip("If true, it plays automatically when the scene loads.")]
    public bool playOnStart = true;

    [Header("Actors")]
    public Camera cinematicCamera;
    public Transform playerActor;
    public Animator playerAnimator;

    [Tooltip("The exact name of the parameter in your Animator that makes the player walk.")]
    public string animatorWalkParameter = "Speed";
    [Tooltip("The value to set the parameter to (e.g., 1 for walking, 0 for idle).")]
    public float walkValue = 1f;

    [Tooltip("The exact name of the Grounded parameter so the player doesn't get stuck jumping.")]
    public string animatorGroundedParameter = "IsGrounded";

    [Header("UI To Hide")]
    [Tooltip("Drag your HUD Canvas or Panels here so they vanish during the movie.")]
    public List<GameObject> hudElementsToHide = new List<GameObject>();

    [Header("Cinematic UI Exclusivity")]
    [Tooltip("The cinematic UI root that is allowed to remain visible. Leave empty to use Bridge Info Canvas. All other active UI is hidden and restored exactly when the cinematic ends.")]
    public GameObject cinematicUIRoot;

    // --- NEW: Bridge Info UI System ---
    [Header("Bridge Info UI System")]
    [Tooltip("The contract containing the data you want to display on screen.")]
    public ContractSO contractToDisplay;
    [Tooltip("The parent UI panel/canvas holding the bridge info.")]
    public GameObject bridgeInfoCanvas;

    public TMPro.TextMeshProUGUI clientNameText;
    public TMPro.TextMeshProUGUI spanText;
    public TMPro.TextMeshProUGUI budgetText;
    public TMPro.TextMeshProUGUI loadText;

    [Header("The Sequence")]
    public List<CinematicShot> shots = new List<CinematicShot>();

    [Header("Events")]
    public UnityEvent OnCinematicStarted;
    public UnityEvent OnCinematicFinished;

    private Transform originalCamParent;
    private Vector3 originalCamLocalPos;
    private Quaternion originalCamLocalRot;
    private bool isPlaying = false;

    private bool isFloatParam = false;
    private bool isParamCached = false;
    private bool shotHasWalked = false;
    private GameObject coordinatedCinematicPanel;
    private GameObject generatedCoordinatorAnchor;
    private bool cinematicPanelFrameOpen;
    private readonly Dictionary<GameObject, bool> fallbackHudStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<Canvas, bool> fallbackCanvasStates = new Dictionary<Canvas, bool>();

    private GameObject dynamicallySpawnedRockTrail;

    private void Start()
    {
        if (bridgeInfoCanvas != null) bridgeInfoCanvas.SetActive(false);

        if (playOnStart)
        {
            PlayCinematic();
        }
    }

    public void PlayCinematic()
    {
        if (isPlaying) return;

        if (playOnlyOnce && PlayerPrefs.GetInt($"Cinematic_{cinematicID}", 0) == 1)
        {
            Debug.Log($"[Cinematic] '{cinematicID}' has already been played. Skipping.");
            return;
        }

        StartCoroutine(CinematicRoutine());
    }

    private IEnumerator CinematicRoutine()
    {
        isPlaying = true;
        OnCinematicStarted?.Invoke();

        BeginCinematicUIIsolation();

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(false);
            inputObj.SetLookEnabled(false);
        }

        PlayerMotor motor = FindObjectOfType<PlayerMotor>();
        if (motor != null) motor.enabled = false;

        if (cinematicCamera == null) cinematicCamera = Camera.main;

        if (cinematicCamera != null)
        {
            originalCamParent = cinematicCamera.transform.parent;
            originalCamLocalPos = cinematicCamera.transform.localPosition;
            originalCamLocalRot = cinematicCamera.transform.localRotation;
            cinematicCamera.transform.SetParent(null);
        }

        foreach (CinematicShot shot in shots)
        {
            yield return StartCoroutine(PlayShot(shot));
        }

        if (cinematicCamera != null)
        {
            cinematicCamera.transform.SetParent(originalCamParent);
            cinematicCamera.transform.localPosition = originalCamLocalPos;
            cinematicCamera.transform.localRotation = originalCamLocalRot;
        }

        if (motor != null) motor.enabled = true;

        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(true);
            inputObj.SetLookEnabled(true);
        }

        // --- NEW: Guarantee the bridge info canvas turns off when the cinematic ends ---
        if (bridgeInfoCanvas != null) bridgeInfoCanvas.SetActive(false);

        EndCinematicUIIsolation();

        if (dynamicallySpawnedRockTrail != null)
        {
            foreach (Transform child in dynamicallySpawnedRockTrail.transform)
            {
                Destroy(child.gameObject);
            }

            TrailRenderer tr = dynamicallySpawnedRockTrail.GetComponentInChildren<TrailRenderer>();
            if (tr != null) tr.Clear();

            ParticleSystem ps = dynamicallySpawnedRockTrail.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Clear();

            dynamicallySpawnedRockTrail.SetActive(true);
        }

        if (playerAnimator != null && shotHasWalked)
        {
            SetWalkAnimation(false);
        }

        if (playOnlyOnce)
        {
            PlayerPrefs.SetInt($"Cinematic_{cinematicID}", 1);
            PlayerPrefs.Save();
        }

        isPlaying = false;
        OnCinematicFinished?.Invoke();
    }

    private void OnDisable()
    {
        // Coroutines stop immediately when this component is disabled. Never leave
        // the HUD/canvases in their cinematic state if that happens mid-shot.
        EndCinematicUIIsolation();
        isPlaying = false;
    }

    private void BeginCinematicUIIsolation()
    {
        EndCinematicUIIsolation();

        // Save the ORIGINAL states before UIPanelCoordinator changes anything.
        fallbackHudStates.Clear();
        fallbackCanvasStates.Clear();

        foreach (GameObject ui in hudElementsToHide)
        {
            if (ui == null || fallbackHudStates.ContainsKey(ui)) continue;

            // Remember whether the root object itself was active.
            fallbackHudStates.Add(ui, ui.activeSelf);

            // Also remember every Canvas.enabled state under this UI root.
            // This protects against coordinators that disable Canvas components
            // instead of only deactivating GameObjects.
            Canvas[] canvases = ui.GetComponentsInChildren<Canvas>(true);
            foreach (Canvas canvas in canvases)
            {
                if (canvas != null && !fallbackCanvasStates.ContainsKey(canvas))
                {
                    fallbackCanvasStates.Add(canvas, canvas.enabled);
                }
            }
        }

        coordinatedCinematicPanel = cinematicUIRoot != null
            ? cinematicUIRoot
            : bridgeInfoCanvas;

        // Let the coordinator see/capture the ORIGINAL UI state first.
        if (UIPanelCoordinator.Instance != null)
        {
            GameObject coordinatorAnchor = coordinatedCinematicPanel;

            if (coordinatorAnchor == null)
            {
                generatedCoordinatorAnchor =
                    new GameObject("CinematicUIIsolationAnchor", typeof(RectTransform));

                generatedCoordinatorAnchor.transform.SetParent(transform.parent, false);
                coordinatorAnchor = generatedCoordinatorAnchor;
            }

            UIPanelCoordinator.Instance.OpenPanel(coordinatorAnchor, false);

            coordinatedCinematicPanel = coordinatorAnchor;
            cinematicPanelFrameOpen = true;
        }

        // Explicitly hide everything assigned in UI To Hide AFTER the coordinator
        // has captured the original scene state.
        foreach (KeyValuePair<GameObject, bool> state in fallbackHudStates)
        {
            if (state.Key != null)
            {
                state.Key.SetActive(false);
            }
        }
    }

    private void EndCinematicUIIsolation()
    {
        // Close the coordinator first so our saved state is the FINAL state applied.
        if (cinematicPanelFrameOpen && coordinatedCinematicPanel != null &&
            UIPanelCoordinator.Instance != null)
        {
            UIPanelCoordinator.Instance.ClosePanel(coordinatedCinematicPanel);
        }

        cinematicPanelFrameOpen = false;
        coordinatedCinematicPanel = null;

        if (generatedCoordinatorAnchor != null)
        {
            Destroy(generatedCoordinatorAnchor);
            generatedCoordinatorAnchor = null;
        }

        // Restore GameObjects first.
        foreach (KeyValuePair<GameObject, bool> state in fallbackHudStates)
        {
            if (state.Key != null)
            {
                state.Key.SetActive(state.Value);
            }
        }

        // Then restore Canvas.enabled so a reactivated Tutorial Canvas actually renders.
        foreach (KeyValuePair<Canvas, bool> state in fallbackCanvasStates)
        {
            if (state.Key != null)
            {
                state.Key.enabled = state.Value;
            }
        }

        fallbackHudStates.Clear();
        fallbackCanvasStates.Clear();
    }

    private IEnumerator PlayShot(CinematicShot shot)
    {
        float elapsed = 0f;

        Vector3 camStartPos = shot.cameraStartPoint != null ? shot.cameraStartPoint.position : cinematicCamera.transform.position;
        Quaternion camStartRot = shot.cameraStartPoint != null ? shot.cameraStartPoint.rotation : cinematicCamera.transform.rotation;

        Vector3 camEndPos = shot.cameraEndPoint != null ? shot.cameraEndPoint.position : camStartPos;
        Quaternion camEndRot = shot.cameraEndPoint != null ? shot.cameraEndPoint.rotation : camStartRot;

        Vector3 playerStartPos = shot.playerStartPoint != null ? shot.playerStartPoint.position : playerActor.position;
        Vector3 playerEndPos = shot.playerWalkTarget != null ? shot.playerWalkTarget.position : playerStartPos;

        float fixedPlayerHeight = playerStartPos.y;

        if (shot.playerStartPoint != null) playerActor.position = playerStartPos;

        bool needsWalking = shot.playWalkAnimation && playerAnimator != null && shot.playerWalkTarget != null;
        if (needsWalking) shotHasWalked = true;

        // --- NEW: Populate and Show/Hide the Bridge Info UI for this specific shot ---
        if (shot.showBridgeInfoCanvas && bridgeInfoCanvas != null && contractToDisplay != null)
        {
            bridgeInfoCanvas.SetActive(true);
            if (clientNameText != null) clientNameText.text = "Client: " + contractToDisplay.clientName;
            if (spanText != null) spanText.text = "Span: " + contractToDisplay.bridgeSpan + "m";
            if (budgetText != null) budgetText.text = $"Budget: ₱{contractToDisplay.budget:N0}";
            if (loadText != null) loadText.text = "Live Load: " + contractToDisplay.liveLoadWeight + "kg";
        }
        else if (bridgeInfoCanvas != null)
        {
            bridgeInfoCanvas.SetActive(false);
        }

        while (elapsed < shot.duration)
        {
            elapsed += Time.deltaTime;
            float t = shot.movementCurve.Evaluate(elapsed / shot.duration);

            if (dynamicallySpawnedRockTrail == null)
            {
                dynamicallySpawnedRockTrail = GameObject.Find("RockTrail_Container");
                if (dynamicallySpawnedRockTrail != null)
                {
                    dynamicallySpawnedRockTrail.SetActive(false);
                }
            }

            if (cinematicCamera != null)
            {
                cinematicCamera.transform.position = Vector3.Lerp(camStartPos, camEndPos, t);
                cinematicCamera.transform.rotation = Quaternion.Slerp(camStartRot, camEndRot, t);
            }

            if (playerActor != null && shot.playerWalkTarget != null)
            {
                Vector3 newPos = Vector3.Lerp(playerStartPos, playerEndPos, t);
                newPos.y = fixedPlayerHeight;
                playerActor.position = newPos;

                Vector3 moveDir = (playerEndPos - playerStartPos).normalized;
                moveDir.y = 0;
                if (moveDir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir);
                    playerActor.rotation = Quaternion.Slerp(playerActor.rotation, targetRot, Time.deltaTime * 10f);
                }
            }

            if (needsWalking)
            {
                SetWalkAnimation(true);
            }
            else if (playerAnimator != null)
            {
                SetWalkAnimation(false);
            }

            yield return null;
        }

        if (cinematicCamera != null)
        {
            cinematicCamera.transform.position = camEndPos;
            cinematicCamera.transform.rotation = camEndRot;
        }

        if (playerActor != null && shot.playerWalkTarget != null)
        {
            Vector3 finalPos = playerEndPos;
            finalPos.y = fixedPlayerHeight;
            playerActor.position = finalPos;
        }

        if (shot.postShotDelay > 0f)
        {
            yield return new WaitForSeconds(shot.postShotDelay);
        }
    }

    private void SetWalkAnimation(bool isWalking)
    {
        if (playerAnimator == null) return;

        if (!string.IsNullOrEmpty(animatorGroundedParameter))
        {
            playerAnimator.SetBool(animatorGroundedParameter, true);
        }

        if (!isParamCached)
        {
            foreach (AnimatorControllerParameter param in playerAnimator.parameters)
            {
                if (param.name == animatorWalkParameter)
                {
                    isFloatParam = param.type == AnimatorControllerParameterType.Float;
                    break;
                }
            }
            isParamCached = true;
        }

        if (isFloatParam)
        {
            playerAnimator.SetFloat(animatorWalkParameter, isWalking ? walkValue : 0f);
        }
        else
        {
            playerAnimator.SetBool(animatorWalkParameter, isWalking);
        }
    }
}
