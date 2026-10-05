using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-50)]
public class PlayerMotor : MonoBehaviour
{
    public event System.Action Jumped;

    private CharacterController controller;
    private PlayerMovementAudio movementAudio;
    private Vector3 playerVelocity;
    private Vector3 previousFixedPosition;
    private Vector3 currentFixedPosition;
    private float lastMotionFixedTime;
    private bool hasFixedMotion;
    private Transform visualRoot;
    private Vector3 visualRootBaseLocalPosition;

    public void ResetTestMotion()
    {
        movementAudio?.StopImmediately();
        playerVelocity = Vector3.zero;
        ResetRenderMotion();
    }

    public Vector3 RenderPositionOffset
    {
        get
        {
            FusionConnectionManager connection = FusionConnectionManager.Instance;
            if (!isActiveAndEnabled || !hasFixedMotion ||
                (SceneManager.GetActiveScene().name != "Multiplayer" &&
                 SceneManager.GetActiveScene().name != FusionConnectionManager.HostWorldSceneName) ||
                connection == null || !connection.IsClientConnected ||
                (transform.position - currentFixedPosition).sqrMagnitude > 1f)
                return Vector3.zero;

            float alpha = Mathf.Clamp01((Time.time - lastMotionFixedTime) /
                Mathf.Max(0.0001f, Time.fixedDeltaTime));
            Vector3 renderPosition = Vector3.Lerp(previousFixedPosition,
                currentFixedPosition, alpha);
            return renderPosition - transform.position;
        }
    }

    public Vector3 GetVisualBaseLocalPosition(Transform candidate) =>
        candidate != null && candidate == visualRoot
            ? visualRootBaseLocalPosition : candidate != null ? candidate.localPosition : Vector3.zero;

    private void ResetRenderMotion()
    {
        previousFixedPosition = transform.position;
        currentFixedPosition = transform.position;
        lastMotionFixedTime = Time.fixedTime;
        hasFixedMotion = false;
        if (visualRoot != null) visualRoot.localPosition = visualRootBaseLocalPosition;
    }
    private bool isGrounded;
    
    [Header("References")]
    [Tooltip("Drag the Main Camera here so movement is relative to where you are looking.")]
    public Transform cameraTransform; 
    [Tooltip("Drag the 3D model that has the Animator component here!")]
    public Animator playerAnimator; 

    [Header("Movement")]
    public float speed = 5f;
    [Header("Joystick Sprint")]
    [Range(0.1f, 1f)] public float sprintStartThreshold = 0.85f;
    [Range(0f, 1f)] public float sprintStopThreshold = 0.7f;
    [Min(1f)] public float sprintSpeedMultiplier = 1.5f;
    [Tooltip("Maximum walking speed while carrying cargo, even at full joystick input.")]
    [Min(0f)] public float carryingWalkSpeed = 4f;
    private CargoItem carriedItem;
    private Transform carryingHips;
    private Vector3 carryingHipOrigin;
    public void SetCarriedItem(CargoItem item)
    {
        carriedItem = item;
        if (item == null) return;
        if (playerAnimator != null)
        {
            if (playerAnimator.isHuman)
                carryingHips = playerAnimator.GetBoneTransform(HumanBodyBones.Hips);
            else
                foreach (Transform bone in playerAnimator.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "mixamorig:Hips" || bone.name == "Hips")
                    { carryingHips = bone; break; }
            if (carryingHips != null) carryingHipOrigin = carryingHips.localPosition;
        }
        isSprinting = false;
        if (playerAnimator != null) playerAnimator.SetBool(SprintParameter, false);
    }

    private void LateUpdate()
    {
        if (visualRoot != null)
            visualRoot.localPosition = visualRootBaseLocalPosition +
                transform.InverseTransformVector(IsCarryingCargo()
                    ? Vector3.zero : RenderPositionOffset);

        if (carriedItem == null || carryingHips == null) return;
        // Generic clips animate hip translation even with Apply Root Motion off.
        // The CharacterController owns travel; keep the model over that body.
        // Run before CargoItem/PlayerLook LateUpdate so hands and camera agree.
        Vector3 pose = carryingHips.localPosition;
        pose.x = carryingHipOrigin.x;
        pose.z = carryingHipOrigin.z;
        carryingHips.localPosition = pose;
    }

    private bool IsCarryingCargo()
    {
        if (carriedItem != null || CargoItem.IsCarriedBy(transform)) return true;
        // Retain the restriction if scripts reload while an item is held.
        int layer = playerAnimator != null ? playerAnimator.GetLayerIndex("Carry Pose") : -1;
        return layer >= 0 && playerAnimator.GetLayerWeight(layer) > 0.5f;
    }
    private bool isSprinting;
    [SerializeField] private bool runEnabled = true;
    private static readonly int SprintParameter = Animator.StringToHash("IsSprinting");
    public void SetRunEnabled(bool enabled)
    {
        runEnabled = enabled;
        if (enabled) return;
        isSprinting = false;
        if (playerAnimator != null) playerAnimator.SetBool(SprintParameter, false);
    }
    public float gravity = -9.8f;
    [Tooltip("How fast the character spins around to face the direction they are walking.")]
    public float rotationSpeed = 12f;
    
    [Header("Jumping")]
    public float jumpHeight = 1.5f;
    [Range(0f, 1f)] public float airSpeedMultiplier = 0.4f;
    [Tooltip("SFX ID in AudioManager, played once when a jump is accepted.")]
    [SerializeField] private string jumpSfxId = "Jump";

    [Header("Physics Interaction")]
    public float playerWeight = 500f; 

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        movementAudio = PlayerMovementAudio.Attach(gameObject, false);
        visualRoot = transform.Find("NewCharacterModel");
        if (visualRoot != null) visualRootBaseLocalPosition = visualRoot.localPosition;
        ResetRenderMotion();
        
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
        
        bool jumpPressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                           (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        if (isGrounded && jumpPressed)
        {
            Jump();
        }
    }

    public void ProcessMove(Vector2 input)
    {
        if (!isActiveAndEnabled || controller == null || !controller.enabled) return;
        // Keep gravity/grounding active while only the recording camera moves.
        if (RecordingCameraShortcut.BlocksMovement(this)) input = Vector2.zero;
        Vector3 beforeMove = transform.position;
        previousFixedPosition = beforeMove;
        input = Vector2.ClampMagnitude(input, 1f);
        float moveAmount = input.magnitude;
        float startThreshold = Mathf.Clamp(sprintStartThreshold, 0.1f, 1f);
        float stopThreshold = Mathf.Clamp(sprintStopThreshold, 0f, startThreshold);
        bool carrying = IsCarryingCargo();
        // Match Carry Hold -> Carry Walk's threshold: do not slide while the
        // Animator is still in its standing pose during a light joystick touch.
        if (carrying && moveAmount <= 0.1f)
        {
            input = Vector2.zero;
            moveAmount = 0f;
        }
        bool runLocked = TutorialManager.Instance != null && TutorialManager.Instance.IsRunLocked;
        isSprinting = runEnabled && !runLocked && !carrying && moveAmount > 0.1f && (isSprinting
            ? moveAmount > stopThreshold : moveAmount >= startThreshold);
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
        
        float currentSpeed = carrying
            ? Mathf.Min(speed, carryingWalkSpeed) * moveAmount
            : speed * moveAmount * (isSprinting ? sprintSpeedMultiplier : 1f);
        if (!isGrounded) currentSpeed *= airSpeedMultiplier;
        controller.Move(moveDirection * currentSpeed * Time.deltaTime);
        
        playerVelocity.y += gravity * Time.deltaTime;
        
        if (isGrounded && playerVelocity.y < 0)
            playerVelocity.y = -2f;
            
        controller.Move(playerVelocity * Time.deltaTime);

        if (playerAnimator != null)
        {
            playerAnimator.SetFloat("Speed", moveAmount);
            playerAnimator.SetBool(SprintParameter, isSprinting);
        }

        currentFixedPosition = transform.position;
        lastMotionFixedTime = Time.fixedTime;
        hasFixedMotion = true;
        Vector3 actualMovement = currentFixedPosition - beforeMove;
        actualMovement.y = 0f;
        movementAudio?.SetLocalMovement(actualMovement.magnitude / Mathf.Max(0.0001f, Time.deltaTime),
            isSprinting, controller.isGrounded && playerVelocity.y <= 0f, moveAmount > 0.1f);
    }

    private void OnDisable()
    {
        movementAudio?.StopImmediately();
        ResetRenderMotion();
        isSprinting = false;
        if (playerAnimator != null)
        {
            playerAnimator.SetFloat("Speed", 0f);
            playerAnimator.SetBool(SprintParameter, false);
        }
    }

    public void Jump()
    {
        if (!isActiveAndEnabled || controller == null || !controller.enabled ||
            RecordingCameraShortcut.BlocksMovement(this) ||
            Time.timeScale <= 0f || AudioListener.pause) return;
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsJumpLocked) return;
        // The controller can still report grounded during takeoff. Consume only
        // the first press, rather than replaying the sound and restarting the jump.
        if (isGrounded && playerVelocity.y <= 0f)
        {
            movementAudio?.StopImmediately();
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            // --- NEW: Fire the Jump trigger in the Animator! ---
            if (playerAnimator != null)
            {
                playerAnimator.SetTrigger("Jump");
            }

            if (AudioManager.Instance != null && !string.IsNullOrWhiteSpace(jumpSfxId))
                AudioManager.Instance.PlaySFX(jumpSfxId);

            Jumped?.Invoke();
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
