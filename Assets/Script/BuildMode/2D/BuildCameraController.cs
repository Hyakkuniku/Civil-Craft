using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Collections; // --- NEW: Required for Coroutines ---

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

    // --- NEW: SIMULATION VIEW TRANSITION ---
    [Header("Simulation View Transition")]
    [Tooltip("How much higher the camera goes when you press Play")]
    public float simHeightOffset = 8f;
    [Tooltip("How many degrees it tilts down when you press Play")]
    public float simPitchOffset = 15f;
    [Tooltip("How fast the camera moves to the simulation view")]
    public float simTransitionSpeed = 3f;

    private Vector3 preSimPos;
    private float preSimPitch;
    private Coroutine simTransitionRoutine;
    private bool isInSimTransition = false;

    private Camera activeCamera;
    private float lastTwoFingerTime = 0f;

    private bool isInitialized = false;
    private float lockedZPosition; 

    private HashSet<int> uiTouches = new HashSet<int>();
    private bool mouseStartedOnUI = false;
    private PointerEventData cachedEventData;
    private List<RaycastResult> cachedRaycastResults = new List<RaycastResult>();

    private void OnEnable() { EnhancedTouchSupport.Enable(); }

    private void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameManager.GameState.Building) 
        {
            isInitialized = false; 
            uiTouches.Clear(); 
            mouseStartedOnUI = false;
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

    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;
        
        if (cachedEventData == null) cachedEventData = new PointerEventData(EventSystem.current);
        
        cachedEventData.position = screenPosition;
        cachedRaycastResults.Clear();
        
        EventSystem.current.RaycastAll(cachedEventData, cachedRaycastResults);
        
        foreach (RaycastResult result in cachedRaycastResults)
        {
            if (result.gameObject.GetComponentInParent<ScrollRect>() != null ||
                result.gameObject.GetComponentInParent<Selectable>() != null)
            {
                return true;
            }
        }
        
        return false; 
    }

    private void HandleCameraInput()
    {
        // --- NEW: Block camera inputs while we are flying to the Simulation View! ---
        if (isInSimTransition) return;

        if (Touch.activeTouches.Count > 0)
        {
            foreach (var touch in Touch.activeTouches)
            {
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    if (IsPointerOverUI(touch.screenPosition))
                    {
                        uiTouches.Add(touch.finger.index);
                    }
                }
                else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    uiTouches.Remove(touch.finger.index);
                }
            }

            List<Touch> validTouches = new List<Touch>();
            foreach (var touch in Touch.activeTouches)
            {
                if (!uiTouches.Contains(touch.finger.index))
                {
                    validTouches.Add(touch);
                }
            }

            if (validTouches.Count == 2)
            {
                lastTwoFingerTime = Time.time;
                Touch t0 = validTouches[0];
                Touch t1 = validTouches[1];

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
                    RotateCamera(avgDeltaY * touchPitchSpeed);
                }
            }
            else if (validTouches.Count == 1)
            {
                if (Time.time - lastTwoFingerTime < 0.15f) return; 
                
                if (barCreator != null && (barCreator.IsCreating || barCreator.IsErasing || barCreator.IsSelecting || barCreator.IsMoving || barCreator.IsPasting)) return;

                Touch touch = validTouches[0];
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
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                mouseStartedOnUI = IsPointerOverUI(Input.mousePosition);
            }
            
            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2))
            {
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
                {
                    mouseStartedOnUI = false;
                }
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f && !IsPointerOverUI(Input.mousePosition))
            {
                float zoomDelta = scroll * -pcZoomSpeed;
                if (activeCamera.orthographic) activeCamera.orthographicSize = Mathf.Clamp(activeCamera.orthographicSize + zoomDelta, minZoom, maxZoom);
                else activeCamera.fieldOfView = Mathf.Clamp(activeCamera.fieldOfView + zoomDelta, minZoom, maxZoom);
            }

            if (!mouseStartedOnUI)
            {
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
                    RotateCamera(Input.GetAxis("Mouse Y") * pcPitchSpeed);
                }
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

    // --- NEW: CINEMATIC METHODS ---
    public void GoToSimulationView()
    {
        if (activeCamera == null) return;
        if (simTransitionRoutine != null) StopCoroutine(simTransitionRoutine);
        
        preSimPos = activeCamera.transform.localPosition;
        
        preSimPitch = activeCamera.transform.localEulerAngles.x;
        if (preSimPitch > 180f) preSimPitch -= 360f;

        Vector3 targetPos = preSimPos + new Vector3(0, simHeightOffset, 0);
        targetPos.x = Mathf.Clamp(targetPos.x, minHorizontal, maxHorizontal);
        targetPos.y = Mathf.Clamp(targetPos.y, minHeight, maxHeight);

        float targetPitch = Mathf.Clamp(preSimPitch + simPitchOffset, minPitch, maxPitch);

        simTransitionRoutine = StartCoroutine(TransitionRoutine(targetPos, targetPitch));
    }

    public void ReturnToBuildView()
    {
        if (activeCamera == null) return;
        if (simTransitionRoutine != null) StopCoroutine(simTransitionRoutine);

        simTransitionRoutine = StartCoroutine(TransitionRoutine(preSimPos, preSimPitch));
    }

    private IEnumerator TransitionRoutine(Vector3 targetPos, float targetPitch)
    {
        isInSimTransition = true;
        Vector3 startPos = activeCamera.transform.localPosition;
        
        float startPitch = activeCamera.transform.localEulerAngles.x;
        if (startPitch > 180f) startPitch -= 360f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * simTransitionSpeed;
            float smoothT = Mathf.SmoothStep(0, 1, t);

            Vector3 newPos = Vector3.Lerp(startPos, targetPos, smoothT);
            newPos.z = lockedZPosition; // Guarantee Z depth stays exact
            activeCamera.transform.localPosition = newPos;

            float newPitch = Mathf.Lerp(startPitch, targetPitch, smoothT);
            activeCamera.transform.localRotation = Quaternion.Euler(newPitch, activeCamera.transform.localEulerAngles.y, activeCamera.transform.localEulerAngles.z);

            yield return null;
        }

        activeCamera.transform.localPosition = new Vector3(targetPos.x, targetPos.y, lockedZPosition);
        activeCamera.transform.localRotation = Quaternion.Euler(targetPitch, activeCamera.transform.localEulerAngles.y, activeCamera.transform.localEulerAngles.z);

        isInSimTransition = false;
    }
}