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

    // Start is called before the first frame update
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    // Update is called once per frame
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
    }

    // ────────────────────────────────────────────────────────────
    // NEW: Apply physical weight to the bridge when walking on it
    // ────────────────────────────────────────────────────────────
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Get the Rigidbody of whatever we are standing on (like a bridge piece)
        Rigidbody body = hit.collider.attachedRigidbody;

        // If it doesn't have a physics body, or if it's locked, do nothing
        if (body == null || body.isKinematic)
            return;

        // We only want to push DOWN (gravity), not shove the bridge sideways
        // hit.moveDirection.y is negative when gravity is pulling us down
        if (hit.moveDirection.y < -0.3f)
        {
            // Push down on the exact spot the player's feet are touching
            Vector3 downwardForce = new Vector3(0, -1, 0);
            body.AddForceAtPosition(downwardForce * playerWeight, hit.point);
        }
    }
}