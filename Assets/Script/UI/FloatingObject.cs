using UnityEngine;

public class FloatingObject : MonoBehaviour
{
    [Header("Floating Bobbing Settings")]
    public float amplitude = 0.2f; 
    public float frequency = 1f;   
    
    [Header("Movement Speeds")]
    [Tooltip("How fast the piece flies in to lock into place.")]
    public float attachSpeed = 8f; 
    [Tooltip("How fast the piece drifts back to its floating spot.")]
    public float floatBackSpeed = 3f;

    [HideInInspector] 
    public bool isFloating = true; 

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 basePosition; 

    void Start()
    {
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        basePosition = transform.position;
    }

    void Update()
    {
        // Automatically choose the correct speed based on what the piece is doing
        float currentSpeed = isFloating ? floatBackSpeed : attachSpeed;

        // 1. Smoothly glide the invisible base position toward the target
        basePosition = Vector3.Lerp(basePosition, targetPosition, Time.deltaTime * currentSpeed);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * currentSpeed);

        // 2. If floating is active, apply the bobbing math on top of the base position
        if (isFloating)
        {
            float bobOffset = Mathf.Sin(Time.time * Mathf.PI * frequency) * amplitude;
            transform.position = new Vector3(basePosition.x, basePosition.y + bobOffset, basePosition.z);
        }
        else
        {
            // If not floating, just lock tightly to the base position
            transform.position = basePosition;
        }
    }

    // The Manager calls this to hand off the coordinates
    public void SetTarget(Transform targetTransform, bool shouldFloat, bool snapInstantly = false) 
    {
        if (targetTransform == null) return;
        
        targetPosition = targetTransform.position;
        targetRotation = targetTransform.rotation;
        isFloating = shouldFloat;

        if (snapInstantly)
        {
            basePosition = targetPosition;
            transform.position = targetPosition;
            transform.rotation = targetRotation;
        }
    }
}