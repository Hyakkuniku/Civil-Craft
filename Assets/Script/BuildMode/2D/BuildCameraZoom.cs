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

    [Header("Movement Limits (Boundary)")]
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

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameManager.GameState.Building) 
        {
            isInitialized = false; 
            return;
        }

        if (GameManager.Instance.ActiveBuildLocation != null && GameManager.Instance.ActiveBuildLocation.locationCamera != null)
        {
            activeCamera = GameManager.Instance.ActiveBuildLocation.locationCamera;
        }
        else
        {
            activeCamera = Camera.main;
        }

        if (activeCamera == null || !activeCamera.enabled) return;
        
        if (barCreator == null) barCreator = FindObjectOfType<BarCreator>();

        if (!isInitialized)
        {
            lockedZPosition = activeCamera.transform.position.z;
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

                float prevMag = ((t0.screenPosition - t0.delta) - (t1.screenPosition - t1.delta)).magnitude;
                float currentMag = (t0.screenPosition - t1.screenPosition).magnitude;
                float zoomDelta = (currentMag - prevMag) * -touchZoomSpeed;

                if (Mathf.Abs(zoomDelta) > 0.001f)
                {
                    if (activeCamera.orthographic)
                        activeCamera.orthographicSize = Mathf.Clamp(activeCamera.orthographicSize + zoomDelta, minZoom, maxZoom);
                    else
                        activeCamera.fieldOfView = Mathf.Clamp(activeCamera.fieldOfView + zoomDelta, minZoom, maxZoom);
                }

                float avgDeltaY = (t0.delta.y + t1.delta.y) / 2f;
                if (Mathf.Abs(avgDeltaY) > pitchDeadzone)
                {
                    RotateCamera(avgDeltaY * touchPitchSpeed);
                }
            }
            else if (Touch.activeTouches.Count == 1)
            {
                if (Time.time - lastTwoFingerTime < 0.15f) return; 
                
                // --- THE FIX: Ignore camera panning if you are building OR erasing! ---
                if (barCreator != null && (barCreator.IsCreating || barCreator.IsErasing)) return;

                Touch touch = Touch.activeTouches[0];
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    Vector3 panDelta = new Vector3(-touch.delta.x * touchPanSpeed, -touch.delta.y * touchPanSpeed, 0);
                    activeCamera.transform.position += panDelta;
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
                if (activeCamera.orthographic)
                    activeCamera.orthographicSize = Mathf.Clamp(activeCamera.orthographicSize + zoomDelta, minZoom, maxZoom);
                else
                    activeCamera.fieldOfView = Mathf.Clamp(activeCamera.fieldOfView + zoomDelta, minZoom, maxZoom);
            }

            Vector3 panInput = Vector3.zero;
            if (Input.GetMouseButton(2)) 
            {
                panInput = new Vector3(-Input.GetAxis("Mouse X") * pcPanSpeed, -Input.GetAxis("Mouse Y") * pcPanSpeed, 0);
            }
            else
            {
                panInput = new Vector3(Input.GetAxis("Horizontal") * pcPanSpeed * Time.deltaTime * 50f, 
                                       Input.GetAxis("Vertical") * pcPanSpeed * Time.deltaTime * 50f, 0);
            }

            if (panInput != Vector3.zero)
            {
                activeCamera.transform.position += panInput;
                ApplyConstraints();
            }

            if (Input.GetMouseButton(1))
            {
                RotateCamera(Input.GetAxis("Mouse Y") * pcPitchSpeed);
            }

            if (Input.GetKeyDown(rotateCameraKey)) CycleCameraRotation();
        }
    }

    private void RotateCamera(float amount)
    {
        float currentPitch = activeCamera.transform.eulerAngles.x;
        if (currentPitch > 180f) currentPitch -= 360f; 

        currentPitch -= amount;
        currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

        activeCamera.transform.rotation = Quaternion.Euler(currentPitch, activeCamera.transform.eulerAngles.y, activeCamera.transform.eulerAngles.z);
        
        ApplyConstraints();
    }

    private void ApplyConstraints()
    {
        Vector3 pos = activeCamera.transform.position;

        pos.x = Mathf.Clamp(pos.x, minHorizontal, maxHorizontal);
        pos.y = Mathf.Clamp(pos.y, minHeight, maxHeight);
        
        pos.z = lockedZPosition;

        activeCamera.transform.position = pos;
    }

    public void CycleCameraRotation()
    {
        if (activeCamera == null || !activeCamera.enabled) return;

        float currentX = activeCamera.transform.eulerAngles.x;
        if (currentX > 180f) currentX -= 360f; 

        float newPitch = 0f;
        float topThreshold = maxPitch * 0.5f;
        float bottomThreshold = minPitch * 0.5f;

        if (currentX > bottomThreshold && currentX < topThreshold) newPitch = maxPitch;   
        else if (currentX >= topThreshold) newPitch = minPitch;  
        else newPitch = 0f;    

        activeCamera.transform.rotation = Quaternion.Euler(newPitch, activeCamera.transform.eulerAngles.y, activeCamera.transform.eulerAngles.z);
        ApplyConstraints();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = new Vector3((minHorizontal + maxHorizontal) / 2f, (minHeight + maxHeight) / 2f, lockedZPosition);
        Vector3 size = new Vector3(maxHorizontal - minHorizontal, maxHeight - minHeight, 1f);
        Gizmos.DrawWireCube(center, size);
    }
}