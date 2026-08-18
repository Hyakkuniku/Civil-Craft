using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using EnhancedTouchData = UnityEngine.InputSystem.EnhancedTouch.Touch;

public enum TutorialInputAction
{
    Walk,
    Look
}

public enum WalkCompletionMode
{
    Distance,
    HeldDuration,
    Either
}

/// <summary>
/// Detects meaningful cross-platform Walk or Look input for one active tutorial step.
/// Start and stop listening through TutorialStep UnityEvents.
/// </summary>
[DefaultExecutionOrder(110)]
public class TutorialInputDetector : MonoBehaviour
{
    [Header("Tutorial Action")]
    public TutorialInputAction action = TutorialInputAction.Walk;

    [Header("Input Source")]
    [Tooltip("Player InputManager. Automatically found when empty.")]
    public InputManager inputManager;
    [Range(0.01f, 1f)] public float inputDeadzone = 0.15f;

    [Header("Walk Completion")]
    public WalkCompletionMode walkCompletionMode = WalkCompletionMode.Either;
    [Min(0.01f)] public float requiredWalkDistance = 2f;
    [Min(0.01f)] public float requiredHeldDuration = 1f;
    [Tooltip("Usually the player root. Automatically uses the InputManager transform.")]
    public Transform movementTarget;
    public bool ignoreVerticalMovement = true;

    [Header("Look Completion")]
    [Min(0.01f)] public float requiredLookDegrees = 45f;
    [Tooltip("Usually PlayerLook.cam. Camera.main is used as a fallback.")]
    public Transform lookTarget;

    [Header("Optional Event")]
    public UnityEvent onCompleted = new UnityEvent();

    public bool IsListening { get; private set; }
    public float WalkedDistance { get; private set; }
    public float HeldInputDuration { get; private set; }
    public float LookedDegrees { get; private set; }

    private int listeningStepIndex = -1;
    private Vector3 previousMovementPosition;
    private Quaternion previousLookRotation;

    /// <summary>Uses the Action selected in this component's Inspector.</summary>
    public void BeginListening()
    {
        TutorialManager tutorial = TutorialManager.Instance;
        if (tutorial == null || !tutorial.IsTutorialActive)
        {
            Debug.LogWarning($"{nameof(TutorialInputDetector)} must be started from an active TutorialStep.", this);
            StopListening();
            return;
        }

        ResolveReferences();
        if ((action == TutorialInputAction.Walk && movementTarget == null) ||
            (action == TutorialInputAction.Look && lookTarget == null))
        {
            Debug.LogWarning($"{nameof(TutorialInputDetector)} on '{name}' is missing its tracked transform.", this);
            StopListening();
            return;
        }

        listeningStepIndex = tutorial.CurrentStepIndex;
        WalkedDistance = 0f;
        HeldInputDuration = 0f;
        LookedDegrees = 0f;

        if (movementTarget != null) previousMovementPosition = movementTarget.position;
        if (lookTarget != null) previousLookRotation = lookTarget.rotation;
        IsListening = true;
    }

    public void BeginWalkListening()
    {
        action = TutorialInputAction.Walk;
        BeginListening();
    }

    public void BeginLookListening()
    {
        action = TutorialInputAction.Look;
        BeginListening();
    }

    public void StopListening()
    {
        IsListening = false;
        listeningStepIndex = -1;
        HeldInputDuration = 0f;
    }

    private void LateUpdate()
    {
        if (!IsListening) return;

        TutorialManager tutorial = TutorialManager.Instance;
        if (tutorial == null || !tutorial.IsTutorialActive ||
            tutorial.CurrentStepIndex != listeningStepIndex)
        {
            StopListening();
            return;
        }

        if (action == TutorialInputAction.Walk) EvaluateWalk();
        else EvaluateLook();
    }

    private void EvaluateWalk()
    {
        if (movementTarget == null)
        {
            ResolveReferences();
            if (movementTarget == null) return;
            previousMovementPosition = movementTarget.position;
        }

        bool hasInput = ReadMovementInput().magnitude >= inputDeadzone;
        Vector3 movementDelta = movementTarget.position - previousMovementPosition;
        if (ignoreVerticalMovement) movementDelta.y = 0f;
        if (hasInput) WalkedDistance += movementDelta.magnitude;

        HeldInputDuration = hasInput ? HeldInputDuration + Time.unscaledDeltaTime : 0f;
        previousMovementPosition = movementTarget.position;

        bool distanceReached = WalkedDistance >= requiredWalkDistance;
        bool durationReached = HeldInputDuration >= requiredHeldDuration;
        bool completed = walkCompletionMode == WalkCompletionMode.Distance
            ? distanceReached
            : walkCompletionMode == WalkCompletionMode.HeldDuration
                ? durationReached
                : distanceReached || durationReached;

        if (completed) CompleteCurrentStep();
    }

    private void EvaluateLook()
    {
        if (lookTarget == null)
        {
            ResolveReferences();
            if (lookTarget == null) return;
            previousLookRotation = lookTarget.rotation;
        }

        float inputMagnitude = ReadLookInputMagnitude();
        float rotationDelta = Quaternion.Angle(previousLookRotation, lookTarget.rotation);

        // Requiring actual camera rotation prevents joystick movement touches and UI
        // drags from falsely completing this step.
        if (inputMagnitude >= inputDeadzone && rotationDelta > 0.001f)
            LookedDegrees += rotationDelta;

        previousLookRotation = lookTarget.rotation;
        if (LookedDegrees >= requiredLookDegrees) CompleteCurrentStep();
    }

    private void CompleteCurrentStep()
    {
        TutorialManager tutorial = TutorialManager.Instance;
        int completedStepIndex = listeningStepIndex;

        IsListening = false;
        listeningStepIndex = -1;
        onCompleted?.Invoke();

        if (tutorial != null && tutorial.IsTutorialActive &&
            tutorial.CurrentStepIndex == completedStepIndex)
        {
            tutorial.ShowNextStep();
        }
    }

    private Vector2 ReadMovementInput()
    {
        if (inputManager != null) return inputManager.ReadMovementInput();

        Vector2 value = Vector2.zero;
        if (Keyboard.current != null)
        {
            value.x = (Keyboard.current.dKey.isPressed ? 1f : 0f) -
                      (Keyboard.current.aKey.isPressed ? 1f : 0f);
            value.y = (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                      (Keyboard.current.sKey.isPressed ? 1f : 0f);
        }

        if (Gamepad.current != null && Gamepad.current.leftStick.ReadValue().sqrMagnitude > value.sqrMagnitude)
            value = Gamepad.current.leftStick.ReadValue();
        return value;
    }

    private float ReadLookInputMagnitude()
    {
        float magnitude = inputManager != null ? inputManager.ReadLookInput().magnitude : 0f;

        // Keep desktop tutorial completion consistent with InputManager: moving the
        // cursor alone is not a look action unless RMB is being held.
        if (Mouse.current != null && Mouse.current.rightButton.isPressed)
            magnitude = Mathf.Max(magnitude, Mouse.current.delta.ReadValue().magnitude);
        if (Gamepad.current != null)
            magnitude = Mathf.Max(magnitude, Gamepad.current.rightStick.ReadValue().magnitude);

        if (EnhancedTouchSupport.enabled)
        {
            foreach (EnhancedTouchData touch in EnhancedTouchData.activeTouches)
            {
                if (touch.screenPosition.x > Screen.width * 0.5f)
                    magnitude = Mathf.Max(magnitude, touch.delta.magnitude);
            }
        }

        return magnitude;
    }

    private void ResolveReferences()
    {
        if (inputManager == null) inputManager = FindObjectOfType<InputManager>();
        if (movementTarget == null && inputManager != null) movementTarget = inputManager.transform;

        if (lookTarget == null && inputManager != null)
        {
            PlayerLook playerLook = inputManager.GetComponent<PlayerLook>();
            if (playerLook != null && playerLook.cam != null)
                lookTarget = playerLook.cam.transform;
        }

        if (lookTarget == null && Camera.main != null)
            lookTarget = Camera.main.transform;
    }
}
