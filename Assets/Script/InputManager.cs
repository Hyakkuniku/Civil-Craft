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
    [Tooltip("If true, the player must hold Right-Click to look around, freeing the mouse cursor.")]
    public bool requireRightClickToLook = true;

    [Header("Mobile Settings")]
    [Tooltip("Check this to test mobile touch controls in the Unity Editor. Automatically enables on phone builds!")]
    public bool useMobileTouchControls = false;

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
        motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());
    }

    private void LateUpdate()
    {
        // --- THE FIX: If we are on mobile, STOP the PC camera logic! ---
        // Let your TouchLookInput.cs script handle the camera instead.
        if (useMobileTouchControls)
        {
            return;
        }

        // --- PC Camera Logic ---
        if (requireRightClickToLook)
        {
            if (Input.GetMouseButton(1)) // 1 = Right Mouse Button
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                
                look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                
                look.ProcessLook(Vector2.zero);
            }
        }
        else
        {
            look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
        }
    }

    private void OnEnable() 
    {
        onFoot.Enable();
    }

    private void OnDisable()
    {
        onFoot.Disable();
    }

    public void SetPlayerInputEnable(bool enabled)
    {
        if (enabled)
            onFoot.Enable();
        else
            onFoot.Disable();
    }

    public void SetLookEnabled(bool enabled)
    {
        if (look != null)
            look.canLook = enabled;
    }
}