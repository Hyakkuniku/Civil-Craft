using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem;
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
    private Camera defaultStateCamera;
    private Vector3 defaultLocalPosition;
    private Quaternion defaultLocalRotation;
    private float defaultOrthographicSize;
    private float defaultFieldOfView;
    private bool hasDefaultCameraState;
    private readonly Dictionary<Camera, CameraDefaultState> cameraDefaultStates =
        new Dictionary<Camera, CameraDefaultState>();
    private float lastTwoFingerTime = 0f;

    private struct CameraDefaultState
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public float orthographicSize;
        public float fieldOfView;
    }

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

        if (!isInitialized || defaultStateCamera != activeCamera)
        {
            CaptureDefaultCameraState(activeCamera);
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
            ScrollRect scrollRect = result.gameObject.GetComponentInParent<ScrollRect>();
            Selectable selectable = result.gameObject.GetComponentInParent<Selectable>();
            if (scrollRect == null && selectable == null) continue;

            GameObject interactiveObject = selectable != null
                ? selectable.gameObject
                : scrollRect.gameObject;

            // btn_continue covers the entire tutorial canvas so the player can
            // tap anywhere to advance. It must remain clickable without causing
            // both fingers to be discarded as UI touches during camera lessons.
            if (TutorialManager.Instance != null &&
                TutorialManager.Instance.AllowsCameraInputThrough(interactiveObject))
            {
                return false;
            }

            return true;
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
                
                if (ClipboardManager.Instance != null && ClipboardManager.Instance.isDraggingSelection) return;

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
                
                if (barCreator != null && (barCreator.IsCreating || barCreator.IsErasing || barCreator.IsSelecting || barCreator.IsMoving)) return;
                if (ClipboardManager.Instance != null && ClipboardManager.Instance.isDraggingSelection) return;

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
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse == null) return;

            Vector2 mousePosition = mouse.position.ReadValue();
            bool anyMousePressed = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            bool anyMouseReleased = mouse.leftButton.wasReleasedThisFrame || mouse.rightButton.wasReleasedThisFrame || mouse.middleButton.wasReleasedThisFrame;

            if (anyMousePressed)
            {
                mouseStartedOnUI = IsPointerOverUI(mousePosition);
            }
            
            if (anyMouseReleased)
            {
                if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed)
                {
                    mouseStartedOnUI = false;
                }
            }

            float scroll = mouse.scroll.ReadValue().y / 120f;
            if (Mathf.Abs(scroll) > 0.001f && !IsPointerOverUI(mousePosition))
            {
                float zoomDelta = scroll * -pcZoomSpeed;
                if (activeCamera.orthographic) activeCamera.orthographicSize = Mathf.Clamp(activeCamera.orthographicSize + zoomDelta, minZoom, maxZoom);
                else activeCamera.fieldOfView = Mathf.Clamp(activeCamera.fieldOfView + zoomDelta, minZoom, maxZoom);
            }

            if (!mouseStartedOnUI)
            {
                Vector3 panInput = Vector3.zero;
                Vector2 mouseDelta = mouse.delta.ReadValue() * 0.1f;
                if (mouse.middleButton.isPressed) panInput = new Vector3(-mouseDelta.x * pcPanSpeed, -mouseDelta.y * pcPanSpeed, 0);
                else if (keyboard != null)
                {
                    float horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                                       (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
                    float vertical = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
                                     (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
                    panInput = new Vector3(horizontal * pcPanSpeed * Time.deltaTime * 50f, vertical * pcPanSpeed * Time.deltaTime * 50f, 0);
                }

                if (panInput != Vector3.zero)
                {
                    activeCamera.transform.localPosition += panInput;
                    ApplyConstraints();
                }

                if (mouse.rightButton.isPressed)
                {
                    RotateCamera(mouseDelta.y * pcPitchSpeed);
                }
            }
            
            if (WasKeyPressedThisFrame(rotateCameraKey)) CycleCameraRotation();
        }
    }

    private static bool WasKeyPressedThisFrame(KeyCode keyCode)
    {
        if (Keyboard.current == null) return false;

        string keyName = keyCode == KeyCode.Return ? nameof(Key.Enter) : keyCode.ToString();
        return System.Enum.TryParse(keyName, out Key key) && key != Key.None && Keyboard.current[key].wasPressedThisFrame;
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
        ResolveActiveCamera();
        if (activeCamera == null)
            return;

        if (!hasDefaultCameraState || defaultStateCamera != activeCamera)
            CaptureDefaultCameraState(activeCamera);

        if (simTransitionRoutine != null)
        {
            StopCoroutine(simTransitionRoutine);
            simTransitionRoutine = null;
        }

        isInSimTransition = false;
        activeCamera.transform.localPosition = defaultLocalPosition;
        activeCamera.transform.localRotation = defaultLocalRotation;

        if (activeCamera.orthographic)
            activeCamera.orthographicSize = defaultOrthographicSize;
        else
            activeCamera.fieldOfView = defaultFieldOfView;

        lockedZPosition = defaultLocalPosition.z;
    }

    private void ResolveActiveCamera()
    {
        if (GameManager.Instance != null &&
            GameManager.Instance.ActiveBuildLocation != null &&
            GameManager.Instance.ActiveBuildLocation.locationCamera != null)
        {
            activeCamera = GameManager.Instance.ActiveBuildLocation.locationCamera;
        }
        else if (activeCamera == null)
        {
            activeCamera = Camera.main;
        }
    }

    private void CaptureDefaultCameraState(Camera cameraToCapture)
    {
        if (cameraToCapture == null)
            return;

        if (!cameraDefaultStates.TryGetValue(cameraToCapture, out CameraDefaultState state))
        {
            state = new CameraDefaultState
            {
                localPosition = cameraToCapture.transform.localPosition,
                localRotation = cameraToCapture.transform.localRotation,
                orthographicSize = cameraToCapture.orthographicSize,
                fieldOfView = cameraToCapture.fieldOfView
            };
            cameraDefaultStates.Add(cameraToCapture, state);
        }

        defaultStateCamera = cameraToCapture;
        defaultLocalPosition = state.localPosition;
        defaultLocalRotation = state.localRotation;
        defaultOrthographicSize = state.orthographicSize;
        defaultFieldOfView = state.fieldOfView;
        lockedZPosition = defaultLocalPosition.z;
        hasDefaultCameraState = true;
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
