using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    private PlayerInput playerInput;
    public PlayerInput.OnFootActions onFoot;

    private PlayerMotor motor;
    private PlayerLook look;

    [Header("PC Controls")]
    [Tooltip("Lock and hide the cursor while desktop mouse-look is active.")]
    public bool lockCursorForMouseLook = true;

    [Header("Mobile Settings")]
    [Tooltip("Check this to test mobile touch controls in the Unity Editor. Automatically enables on phone builds!")]
    public bool useMobileTouchControls = false;

    public bool IsUsingMobileControls => Application.isMobilePlatform || useMobileTouchControls;
    public bool IsPlayerInputEnabled => onFoot.enabled;
    public bool IsLookInputEnabled => look != null && look.canLook;

    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.onFoot;
        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();

        // Automatically detect if we are building for a mobile device
        #if UNITY_ANDROID || UNITY_IOS
            useMobileTouchControls = true;
        #endif
    }

    void Start()
    {
        // Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HandleExitBuildMode);
        }
    }

    private void HandleEnterBuildMode()
    {
        SetLookEnabled(false);
        SetPlayerInputEnable(false);
    }

    private void HandleExitBuildMode()
    {
        SetLookEnabled(true);
        SetPlayerInputEnable(true);
    }

    void FixedUpdate()
    {
        // Movement is still handled here (your on-screen joystick will feed into this perfectly)
        motor.ProcessMove(ReadMovementInput());
    }

    private void LateUpdate()
    {
        // TouchLookInput owns mobile swipes. Never process the same touch delta here.
        if (IsUsingMobileControls)
        {
            ApplyCursorState();
            return;
        }

        // Desktop mouse delta is already bound to onFoot/Look. There is no click,
        // toggle, or right-button gate: looking is continuous whenever look is enabled.
        if (look != null && look.canLook && onFoot.enabled)
            look.ProcessLook(ReadLookInput());

        ApplyCursorState();
    }

    public Vector2 ReadMovementInput()
    {
        return onFoot.Movement.ReadValue<Vector2>();
    }

    public Vector2 ReadLookInput()
    {
        Vector2 lookInput = onFoot.Look.ReadValue<Vector2>();

        // The generated Look action currently covers mouse/touch. Reading the right
        // stick here also supports physical gamepads and an OnScreenStick targeting it.
        if (Gamepad.current != null)
        {
            Vector2 rightStick = Gamepad.current.rightStick.ReadValue();
            if (rightStick.sqrMagnitude > lookInput.sqrMagnitude)
                lookInput = rightStick;
        }

        return lookInput;
    }

    private void OnEnable() 
    {
        onFoot.Enable();
        ApplyCursorState();
    }

    private void OnDisable()
    {
        onFoot.Disable();
        ReleaseCursor();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) ApplyCursorState();
        else ReleaseCursor();
    }

    public void SetPlayerInputEnable(bool enabled)
    {
        if (enabled)
            onFoot.Enable();
        else
            onFoot.Disable();

        ApplyCursorState();
    }

    public void SetLookEnabled(bool enabled)
    {
        if (look != null)
            look.canLook = enabled;

        ApplyCursorState();
    }

    private void ApplyCursorState()
    {
        bool shouldLock = lockCursorForMouseLook &&
                          !IsUsingMobileControls &&
                          Mouse.current != null &&
                          isActiveAndEnabled &&
                          onFoot.enabled &&
                          look != null &&
                          look.canLook;

        if (shouldLock)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            ReleaseCursor();
        }
    }

    private static void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
