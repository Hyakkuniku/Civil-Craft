using UnityEngine;

public class PlayerLadderClimb : MonoBehaviour
{
    [Header("Climbing Settings")]
    public float climbSpeed = 3f;
    
    [Header("Script References")]
    [Tooltip("Drag your PlayerMotor script here so we disable it while climbing!")]
    public MonoBehaviour playerMotorScript; 
    
    [Tooltip("Drag your InputManager here to read the mobile joystick!")]
    public InputManager inputManager; // <-- NEW: For your mobile joystick!

    private CharacterController controller;
    private Rigidbody rb;
    private bool isClimbing = false;
    private Transform currentLadder; // <-- NEW: Remembers the ladder so we know its slant!

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ladder"))
        {
            currentLadder = other.transform; // Save the specific slanted ladder we touched
            StartClimbing();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Ladder"))
        {
            currentLadder = null;
            StopClimbing();
        }
    }

    private void StartClimbing()
    {
        isClimbing = true;
        
        if (playerMotorScript != null) playerMotorScript.enabled = false;
        
        if (rb != null) 
        {
            rb.useGravity = false;
            rb.velocity = Vector3.zero; 
        }
    }

    private void StopClimbing()
    {
        isClimbing = false;
        
        if (playerMotorScript != null) playerMotorScript.enabled = true;
        
        if (rb != null) rb.useGravity = true;
    }

    private void Update()
    {
        if (!isClimbing || currentLadder == null) return;

        // 1. READ YOUR MOBILE JOYSTICK
        float verticalInput = GetVerticalInput(); 

        // 2. THE JIGGLE FIX!
        // Instead of moving World Up, we move exactly along the ladder's local Up direction!
        Vector3 climbDirection = currentLadder.up * (verticalInput * climbSpeed);

        // 3. APPLY MOVEMENT
        if (controller != null)
        {
            controller.Move(climbDirection * Time.deltaTime);
        }
        else if (rb != null)
        {
            rb.MovePosition(transform.position + climbDirection * Time.deltaTime);
        }
        else
        {
            transform.Translate(climbDirection * Time.deltaTime, Space.World);
        }
    }

    // --- MOBILE INPUT INTEGRATION ---
    // --- MOBILE INPUT INTEGRATION ---
    private float GetVerticalInput()
    {
        if (inputManager != null)
        {
            // We read the exact same Vector2 your PlayerMotor uses, but we only grab the 'y' (Up/Down) axis!
            return inputManager.onFoot.Movement.ReadValue<Vector2>().y; 
        }
        
        // Backup for testing in the Unity PC Editor without the InputManager assigned
        return Input.GetAxis("Vertical"); 
    }
}