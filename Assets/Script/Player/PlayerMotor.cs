using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMotor : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;
    private bool isGrounded;
    
    [Header("Movement")]
    public float speed = 5f;
    public float gravity = -9.8f;
    
    [Header("Jumping")]
    public float jumpHeight = 1.5f;
    
    // --- NEW: Controls how fast you move in the air! ---
    [Tooltip("1 = full speed. 0.5 = half speed. 0 = no moving in the air.")]
    [Range(0f, 1f)]
    public float airSpeedMultiplier = 0.4f; 

    [Header("Physics Interaction")]
    [Tooltip("How heavy the player is. Increase this to break the bridge!")]
    public float playerWeight = 500f; 

    [Header("Animation")]
    [Tooltip("Drag the 3D model that has the Animator component here!")]
    public Animator playerAnimator; 

    void Start()
    {
        controller = GetComponent<CharacterController>();

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
        this.enabled = false;
    }

    private void HandleExitBuildMode()
    {
        this.enabled = true;
    }

    void Update()
    {
        isGrounded = controller.isGrounded;
        
        if (isGrounded && Input.GetKeyDown(KeyCode.Space))
        {
            Jump();
        }
    }

    public void ProcessMove(Vector2 input)
    {
        Vector3 moveDirection = Vector3.zero;
        moveDirection.x = input.x;
        moveDirection.z = input.y;
        
        // --- THE FIX: Calculate the current speed based on whether we are on the ground or in the air ---
        float currentSpeed = isGrounded ? speed : (speed * airSpeedMultiplier);
        
        // Apply horizontal movement with the new speed limit
        controller.Move(transform.TransformDirection(moveDirection) * currentSpeed * Time.deltaTime);
        
        // Apply gravity and vertical movement
        playerVelocity.y += gravity * Time.deltaTime;
        
        if (isGrounded && playerVelocity.y < 0)
            playerVelocity.y = -2f;
            
        controller.Move(playerVelocity * Time.deltaTime);

        if (playerAnimator != null)
        {
            float moveAmount = Mathf.Clamp01(Mathf.Abs(input.x) + Mathf.Abs(input.y));
            playerAnimator.SetFloat("Speed", moveAmount);
        }
    }

    public void Jump()
    {
        if (isGrounded)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        if (body == null || body.isKinematic)
            return;

        if (hit.moveDirection.y < -0.3f)
        {
            Vector3 downwardForce = new Vector3(0, -1, 0);
            body.AddForceAtPosition(downwardForce * playerWeight, hit.point);
        }
    }
}