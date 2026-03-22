using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class BuildCameraController : MonoBehaviour
{
    [Header("System References")]
    public BarCreator barCreator;

    [Header("Zoom Settings")]
    public float touchZoomSpeed = 0.05f;
    public float pcZoomSpeed = 15f; 
    public float minZoom = 15f;
    public float maxZoom = 60f;

    [Header("Pan Settings")]
    public float touchPanSpeed = 0.02f;
    public float pcPanSpeed = 0.5f; 

    [Header("Movement Limits (Local Space Boundary)")]
    public float maxHeight = 50f;
    public float minHeight = -10f;
    public float maxHorizontal = 50f;
    public float minHorizontal = -50f;

    [Header("Pitch Settings (Rotation)")]
    public float touchPitchSpeed = 0.1f;
    public float pcPitchSpeed = 3.0f; 
    public float pitchDeadzone = 2.0f; 
    
    public float minPitch = -90f; 
    public float maxPitch = 90f;  

    [Header("PC Controls")]
    public KeyCode rotateCameraKey = KeyCode.R; 

    private Camera activeCamera;
    private float lastTwoFingerTime = 0f;

    private bool isInitialized = false;
    private float lockedZPosition; 

    private void OnEnable() { EnhancedTouchSupport.Enable(); }

    private void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameManager.GameState.Building) 
        {
            isInitialized = false; 
            return;
        }

        if (GameManager.Instance.ActiveBuildLocation != null && GameManager.Instance.ActiveBuildLocation.locationCamera != null)
            activeCamera = GameManager.Instance.ActiveBuildLocation.locationCamera;
        else
            activeCamera = Camera.main;

        if (activeCamera == null || !activeCamera.enabled) return;
        if (barCreator == null) barCreator = FindObjectOfType<BarCreator>();

        if (!isInitialized)
        {
            lockedZPosition = activeCamera.transform.localPosition.z;
            isInitialized = true;
        }

        HandleCameraInput();
    }

    private void HandleCameraInput()
    {
        if (Touch.activeTouches.Count > 0)
        {
            if (Touch.activeTouches.Count == 2)
            {
                lastTwoFingerTime = Time.time;
                Touch t0 = Touch.activeTouches[0];
                Touch t1 = Touch.activeTouches[1];

                if (t0.phase == UnityEngine.InputSystem.TouchPhase.Began || t1.phase == UnityEngine.InputSystem.TouchPhase.Began) return;
                
                if (barCreator != null && barCreator.IsPasting) return;

                float prevMag = ((t0.screenPosition - t0.delta) - (t1.screenPosition - t1.delta)).magnitude;
                float currentMag = (t0.screenPosition - t1.screenPosition).magnitude;
                float zoomDelta = (currentMag - prevMag) * -touchZoomSpeed;

                if (Mathf.Abs(zoomDelta) > 0.001f)
                {
                    if (activeCamera.orthographic) activeCamera.orthographicSize = Mathf.Clamp(activeCamera.orthographicSize + zoomDelta, minZoom, maxZoom);
                    else activeCamera.fieldOfView = Mathf.Clamp(activeCamera.fieldOfView + zoomDelta, minZoom, maxZoom);
                }

                float avgDeltaY = (t0.delta.y + t1.delta.y) / 2f;
                if (Mathf.Abs(avgDeltaY) > pitchDeadzone) 
                {
                    // Allowed to rotate freely again!
                    RotateCamera(avgDeltaY * touchPitchSpeed);
                }
            }
            else if (Touch.activeTouches.Count == 1)
            {
                if (Time.time - lastTwoFingerTime < 0.15f) return; 
                
                if (barCreator != null && (barCreator.IsCreating || barCreator.IsErasing || barCreator.IsSelecting || barCreator.IsMoving || barCreator.IsPasting)) return;

                Touch touch = Touch.activeTouches[0];
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    Vector3 panDelta = new Vector3(-touch.delta.x * touchPanSpeed, -touch.delta.y * touchPanSpeed, 0);
                    activeCamera.transform.localPosition += panDelta;
                    ApplyConstraints();
                }
            }
        }
        else 
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                float zoomDelta = scroll * -pcZoomSpeed;
                if (activeCamera.orthographic) activeCamera.orthographicSize = Mathf.Clamp(activeCamera.orthographicSize + zoomDelta, minZoom, maxZoom);
                else activeCamera.fieldOfView = Mathf.Clamp(activeCamera.fieldOfView + zoomDelta, minZoom, maxZoom);
            }

            Vector3 panInput = Vector3.zero;
            if (Input.GetMouseButton(2)) panInput = new Vector3(-Input.GetAxis("Mouse X") * pcPanSpeed, -Input.GetAxis("Mouse Y") * pcPanSpeed, 0);
            else panInput = new Vector3(Input.GetAxis("Horizontal") * pcPanSpeed * Time.deltaTime * 50f, Input.GetAxis("Vertical") * pcPanSpeed * Time.deltaTime * 50f, 0);

            if (panInput != Vector3.zero)
            {
                activeCamera.transform.localPosition += panInput;
                ApplyConstraints();
            }

            if (Input.GetMouseButton(1)) 
            {
                // Allowed to rotate freely again!
                RotateCamera(Input.GetAxis("Mouse Y") * pcPitchSpeed);
            }
            
            if (Input.GetKeyDown(rotateCameraKey)) CycleCameraRotation();
        }
    }

    private void RotateCamera(float amount)
    {
        float currentPitch = activeCamera.transform.localEulerAngles.x;
        if (currentPitch > 180f) currentPitch -= 360f; 

        currentPitch -= amount;
        currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

        activeCamera.transform.localRotation = Quaternion.Euler(currentPitch, activeCamera.transform.localEulerAngles.y, activeCamera.transform.localEulerAngles.z);
        ApplyConstraints();
    }

    private void ApplyConstraints()
    {
        Vector3 localPos = activeCamera.transform.localPosition;
        localPos.x = Mathf.Clamp(localPos.x, minHorizontal, maxHorizontal);
        localPos.y = Mathf.Clamp(localPos.y, minHeight, maxHeight);
        localPos.z = lockedZPosition;
        activeCamera.transform.localPosition = localPos;
    }

    public void CycleCameraRotation()
    {
        if (activeCamera == null || !activeCamera.enabled) return;

        float currentX = activeCamera.transform.localEulerAngles.x;
        if (currentX > 180f) currentX -= 360f; 

        float newPitch = 0f;
        float topThreshold = maxPitch * 0.5f;
        float bottomThreshold = minPitch * 0.5f;

        if (currentX > bottomThreshold && currentX < topThreshold) newPitch = maxPitch;   
        else if (currentX >= topThreshold) newPitch = minPitch;  
        else newPitch = 0f;    

        activeCamera.transform.localRotation = Quaternion.Euler(newPitch, activeCamera.transform.localEulerAngles.y, activeCamera.transform.localEulerAngles.z);
        ApplyConstraints();
    }

    public void ResetCameraRotation()
    {
        if (activeCamera != null)
        {
            activeCamera.transform.localRotation = Quaternion.Euler(0, 0, 0);
            ApplyConstraints();
        }
    }
}