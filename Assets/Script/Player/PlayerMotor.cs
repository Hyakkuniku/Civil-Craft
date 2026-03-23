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

    [Header("Physics Interaction")]
    [Tooltip("How heavy the player is. Increase this to break the bridge!")]
    public float playerWeight = 500f; 

    [Header("Animation")]
    [Tooltip("Drag the 3D model that has the Animator component here!")]
    public Animator playerAnimator; // <-- NEW: Reference to your animator

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
    }

    public void ProcessMove(Vector2 input)
    {
        Vector3 moveDirection = Vector3.zero;
        moveDirection.x = input.x;
        moveDirection.z = input.y;
        
        controller.Move(transform.TransformDirection(moveDirection) * speed * Time.deltaTime);
        
        playerVelocity.y += gravity * Time.deltaTime;
        
        if (isGrounded && playerVelocity.y < 0)
            playerVelocity.y = -2f;
            
        controller.Move(playerVelocity * Time.deltaTime);

        // --- NEW: ANIMATION LOGIC ---
        if (playerAnimator != null)
        {
            // Calculate how much the player is moving based on input (0 means still, >0 means moving)
            float moveAmount = Mathf.Clamp01(Mathf.Abs(input.x) + Mathf.Abs(input.y));
            
            // Send this number to the Animator's "Speed" parameter!
            playerAnimator.SetFloat("Speed", moveAmount);
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