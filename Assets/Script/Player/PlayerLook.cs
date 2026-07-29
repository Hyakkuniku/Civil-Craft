using UnityEngine;

public class PlayerLook : MonoBehaviour
{
    [Header("Camera Setup")]
    public Camera cam;
    [Tooltip("Create an Empty GameObject inside the player near chest/head height and drag it here.")]
    public Transform followTarget; 

    [Header("Distance & Limits")]
    public float defaultDistance = 4f;
    public float minDistance = 1f;
    public float maxDistance = 8f;
    
    [Header("Sensitivity")]
    public float xSensitivity = 30f;
    public float ySensitivity = 30f;

    [Header("Pitch Limits (Up/Down)")]
    public float minPitch = -20f;
    public float maxPitch = 70f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Which layers should the camera collide with? (e.g., Environment, Ground)")]
    public LayerMask collisionMask;

    [HideInInspector] public bool canLook = true;

    private float yaw = 0f;
    private float pitch = 15f; // Start looking slightly down
    private float currentDistance;

    private void Start()
    {
        currentDistance = defaultDistance;
        
        // Fallback if target is missing
        if (followTarget == null) followTarget = transform; 

        // Detach the camera from the player so it can orbit freely
        if (cam != null && cam.transform.parent == transform)
        {
            cam.transform.SetParent(null);
        }
    }

    public void ProcessLook(Vector2 input)
    {
        if (!canLook || cam == null || followTarget == null) return;

        float mouseX = input.x;
        float mouseY = input.y;

        // Calculate Orbit Angles
        yaw += (mouseX * Time.deltaTime) * xSensitivity;
        pitch -= (mouseY * Time.deltaTime) * ySensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        // DO NOT move the camera if Build Mode is active and took control!
        if (!canLook || cam == null || followTarget == null) return;

        // 1. Calculate desired rotation
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);

        // 2. Calculate ideal position
        Vector3 direction = new Vector3(0, 0, -defaultDistance);
        Vector3 desiredPosition = followTarget.position + rotation * direction;

        // 3. Simple SphereCast for Wall Avoidance
        currentDistance = defaultDistance;
        Vector3 rayDir = (desiredPosition - followTarget.position).normalized;
        
        if (Physics.SphereCast(followTarget.position, 0.25f, rayDir, out RaycastHit hit, defaultDistance, collisionMask))
        {
            // Push the camera in if a wall is in the way
            currentDistance = Mathf.Clamp(hit.distance, minDistance, defaultDistance);
        }

        // 4. Apply Final Position & Rotation
        Vector3 finalPosition = followTarget.position + rotation * new Vector3(0, 0, -currentDistance);
        cam.transform.position = finalPosition;
        cam.transform.rotation = rotation;
    }
}