using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class BuildCameraController : MonoBehaviour
{
    [Header("System References")]
    public BarCreator barCreator;

    [Header("Zoom Settings (2 Fingers)")]
    public float touchZoomSpeed = 0.05f;
    public float minZoom = 15f;
    public float maxZoom = 60f;

    [Header("Pan Settings (1 Finger)")]
    public float touchPanSpeed = 0.02f;

    [Header("Pitch Settings (2 Fingers)")]
    [Tooltip("How fast the camera rotates up and down.")]
    public float touchPitchSpeed = 0.1f;
    [Tooltip("How much you have to swipe before the camera starts rotating (prevents accidental rotation while zooming).")]
    public float pitchDeadzone = 2.0f;

    private Camera activeCamera;

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameManager.GameState.Building) return;

        if (GameManager.Instance.ActiveBuildLocation != null && GameManager.Instance.ActiveBuildLocation.locationCamera != null)
        {
            activeCamera = GameManager.Instance.ActiveBuildLocation.locationCamera;
        }
        else
        {
            activeCamera = Camera.main;
        }

        // Wait for the GameManager camera animation to finish
        if (activeCamera == null || !activeCamera.enabled) return;
        
        if (barCreator == null) barCreator = FindObjectOfType<BarCreator>();

        HandleTouchInput();
    }

    private void HandleTouchInput()
    {
        if (Touch.activeTouches.Count == 0) return;

        // --- 1-FINGER PANNING ---
        if (Touch.activeTouches.Count == 1)
        {
            if (barCreator != null && barCreator.IsCreating) return;

            Touch touch = Touch.activeTouches[0];
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                Vector3 panDelta = new Vector3(-touch.delta.x * touchPanSpeed, -touch.delta.y * touchPanSpeed, 0);
                activeCamera.transform.Translate(panDelta, Space.Self);
            }
        }
        // --- 2-FINGER ZOOM AND PITCH ---
        else if (Touch.activeTouches.Count == 2)
        {
            Touch t0 = Touch.activeTouches[0];
            Touch t1 = Touch.activeTouches[1];

            // 1. ZOOM LOGIC
            float prevMagnitude = ((t0.screenPosition - t0.delta) - (t1.screenPosition - t1.delta)).magnitude;
            float currentMagnitude = (t0.screenPosition - t1.screenPosition).magnitude;
            float zoomDelta = (currentMagnitude - prevMagnitude) * -touchZoomSpeed;

            if (Mathf.Abs(zoomDelta) > 0.001f)
            {
                if (activeCamera.orthographic)
                    activeCamera.orthographicSize = Mathf.Clamp(activeCamera.orthographicSize + zoomDelta, minZoom, maxZoom);
                else
                    activeCamera.fieldOfView = Mathf.Clamp(activeCamera.fieldOfView + zoomDelta, minZoom, maxZoom);
            }

            // 2. PITCH LOGIC (Swipe Up/Down)
            float avgDeltaY = (t0.delta.y + t1.delta.y) / 2f;

            // Only rotate if the swipe is larger than our deadzone threshold
            if (Mathf.Abs(avgDeltaY) > pitchDeadzone)
            {
                // Get current pitch safely
                float currentPitch = activeCamera.transform.eulerAngles.x;
                if (currentPitch > 180f) currentPitch -= 360f; 

                // Apply new pitch
                currentPitch -= avgDeltaY * touchPitchSpeed;
                currentPitch = Mathf.Clamp(currentPitch, -90f, 90f);

                activeCamera.transform.rotation = Quaternion.Euler(currentPitch, activeCamera.transform.eulerAngles.y, activeCamera.transform.eulerAngles.z);
            }
        }
    }

    // --- BUTTON TRIGGER FUNCTION ---
    public void CycleCameraRotation()
    {
        if (activeCamera == null || !activeCamera.enabled) return;

        float currentX = activeCamera.transform.eulerAngles.x;
        if (currentX > 180f) currentX -= 360f; 

        float newPitch = 0f;

        // Cycle: 0 -> 90 -> -90
        if (currentX > -45f && currentX < 45f) 
        {
            newPitch = 90f;   // Look down
        }
        else if (currentX >= 45f) 
        {
            newPitch = -90f;  // Look up
        }
        else 
        {
            newPitch = 0f;    // Look ahead
        }

        activeCamera.transform.rotation = Quaternion.Euler(newPitch, activeCamera.transform.eulerAngles.y, activeCamera.transform.eulerAngles.z);
    }
}