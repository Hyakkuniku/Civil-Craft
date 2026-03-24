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

    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.onFoot;
        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();
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
        motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());
    }

    private void LateUpdate()
    {
        // --- NEW: Require holding Right-Click to look around so the mouse is free ---
        if (requireRightClickToLook)
        {
            if (Input.GetMouseButton(1)) // 1 = Right Mouse Button
            {
                // Lock and hide the cursor while actively looking around
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                
                look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
            }
            else
            {
                // Free the cursor so you can click UI and interactables
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                
                // Stop the camera from drifting
                look.ProcessLook(Vector2.zero);
            }
        }
        else
        {
            // Old behavior: Constantly follow the mouse
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