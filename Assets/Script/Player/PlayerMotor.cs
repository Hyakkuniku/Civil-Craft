using UnityEngine;

public class PlayerMotor : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;
    private bool isGrounded;
    
    [Header("References")]
    [Tooltip("Drag the Main Camera here so movement is relative to where you are looking.")]
    public Transform cameraTransform; 
    [Tooltip("Drag the 3D model that has the Animator component here!")]
    public Animator playerAnimator; 

    [Header("Movement")]
    public float speed = 5f;
    public float gravity = -9.8f;
    [Tooltip("How fast the character spins around to face the direction they are walking.")]
    public float rotationSpeed = 12f;
    
    [Header("Jumping")]
    public float jumpHeight = 1.5f;
    [Range(0f, 1f)] public float airSpeedMultiplier = 0.4f; 

    [Header("Physics Interaction")]
    public float playerWeight = 500f; 

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HandleExitBuildMode);
        }
    }

    private void HandleEnterBuildMode() { this.enabled = false; }
    private void HandleExitBuildMode() { this.enabled = true; }

    private void Update()
    {
        isGrounded = controller.isGrounded;

        // --- NEW: Tell the Animator if we are currently on the ground or in the air ---
        if (playerAnimator != null)
        {
            playerAnimator.SetBool("IsGrounded", isGrounded);
        }
        
        if (isGrounded && Input.GetKeyDown(KeyCode.Space))
        {
            Jump();
        }
    }

    public void ProcessMove(Vector2 input)
    {
        Vector3 moveDirection = Vector3.zero;

        if (cameraTransform != null && (input.x != 0 || input.y != 0))
        {
            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;
            
            camForward.y = 0;
            camRight.y = 0;
            
            camForward.Normalize();
            camRight.Normalize();

            moveDirection = (camForward * input.y + camRight * input.x).normalized;
        }

        if (moveDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
        
        float currentSpeed = isGrounded ? speed : (speed * airSpeedMultiplier);
        controller.Move(moveDirection * currentSpeed * Time.deltaTime);
        
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

            // --- NEW: Fire the Jump trigger in the Animator! ---
            if (playerAnimator != null)
            {
                playerAnimator.SetTrigger("Jump");
            }
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        if (body == null || body.isKinematic) return;

        if (hit.moveDirection.y < -0.3f)
        {
            Vector3 downwardForce = new Vector3(0, -1, 0);
            body.AddForceAtPosition(downwardForce * playerWeight, hit.point);
        }
    }
}