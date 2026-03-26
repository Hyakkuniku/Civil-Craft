using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class MapCameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    public Camera cam;

    [Header("Pan Settings")]
    public float pcPanSpeed = 15f;
    public float touchPanSpeed = 0.01f; 
    
    // --- NEW: How far the mouse/finger needs to move before we consider it a "pan" and close the UI ---
    public float panCloseUIThreshold = 2f; 

    [Header("Zoom Settings")]
    public float zoomSpeedPC = 5f;
    public float zoomSpeedMobile = 0.01f;
    public float minZoom = 5f;
    public float maxZoom = 25f;

    [Header("Map Boundaries")]
    public Vector2 minBounds = new Vector2(-30, -30);
    public Vector2 maxBounds = new Vector2(30, 30);

    [Header("Interaction Settings")]
    [Tooltip("Set your Level Nodes to a specific layer (e.g., 'MapNodes') and select it here!")]
    public LayerMask nodeLayer; 

    private Vector2 dragOriginPC;
    private bool isDraggingPC = false;
    private float lastTapTime = 0f;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void OnEnable() { EnhancedTouchSupport.Enable(); }
    void OnDisable() { EnhancedTouchSupport.Disable(); }

    void Update()
    {
        if (Touch.activeTouches.Count > 0)
        {
            HandleMobileInput();
        }
        else
        {
            HandlePCInput();
        }
    }

    void HandleMobileInput()
    {
        if (Touch.activeTouches.Count == 1)
        {
            Touch touch = Touch.activeTouches[0];

            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                lastTapTime = Time.time;
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                // --- NEW: If we pan intentionally, close the Level Info Panel! ---
                if (touch.delta.magnitude > panCloseUIThreshold)
                {
                    if (MapUIManager.Instance != null) MapUIManager.Instance.CloseLevelInfo();
                }

                float currentZoom = cam.orthographic ? cam.orthographicSize : cam.fieldOfView;
                float dynamicPanSpeed = touchPanSpeed * (currentZoom / minZoom);

                Vector3 move = new Vector3(-touch.delta.x * dynamicPanSpeed, 0, -touch.delta.y * dynamicPanSpeed);
                cam.transform.Translate(move, Space.World);
                ApplyBounds();
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended)
            {
                if (Time.time - lastTapTime < 0.25f && touch.history.Count < 5)
                {
                    TrySelectNode(touch.screenPosition);
                }
            }
        }
        else if (Touch.activeTouches.Count == 2)
        {
            Touch t0 = Touch.activeTouches[0];
            Touch t1 = Touch.activeTouches[1];

            // --- NEW: Also close the UI if they pinch to zoom! ---
            if (MapUIManager.Instance != null) MapUIManager.Instance.CloseLevelInfo();

            Vector2 t0Prev = t0.screenPosition - t0.delta;
            Vector2 t1Prev = t1.screenPosition - t1.delta;

            float prevMag = (t0Prev - t1Prev).magnitude;
            float currentMag = (t0.screenPosition - t1.screenPosition).magnitude;

            float zoomDelta = (currentMag - prevMag) * -zoomSpeedMobile;
            ApplyZoom(zoomDelta);
        }
    }

    void HandlePCInput()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            ApplyZoom(scroll * -zoomSpeedPC);
            // --- NEW: Close UI on scroll zoom ---
            if (MapUIManager.Instance != null) MapUIManager.Instance.CloseLevelInfo();
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (!IsPointerOverUI(Input.mousePosition))
            {
                dragOriginPC = Input.mousePosition;
                isDraggingPC = true;
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            if (isDraggingPC && Vector2.Distance(dragOriginPC, Input.mousePosition) < 10f)
            {
                TrySelectNode(Input.mousePosition);
            }
            isDraggingPC = false;
        }

        if (isDraggingPC && Input.GetMouseButton(0))
        {
            Vector2 delta = (Vector2)Input.mousePosition - dragOriginPC;
            dragOriginPC = Input.mousePosition;

            // --- NEW: If we drag intentionally, close the Level Info Panel! ---
            if (delta.magnitude > panCloseUIThreshold)
            {
                if (MapUIManager.Instance != null) MapUIManager.Instance.CloseLevelInfo();
            }

            float currentZoom = cam.orthographic ? cam.orthographicSize : cam.fieldOfView;
            float dynamicPanSpeed = (pcPanSpeed * 0.001f) * (currentZoom / minZoom);

            Vector3 move = new Vector3(-delta.x * dynamicPanSpeed, 0, -delta.y * dynamicPanSpeed);
            cam.transform.Translate(move, Space.World);
            ApplyBounds();
        }
    }

    void ApplyZoom(float delta)
    {
        if (cam.orthographic)
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize + delta, minZoom, maxZoom);
        else
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView + delta, minZoom, maxZoom);
    }

    void ApplyBounds()
    {
        Vector3 pos = cam.transform.position;
        pos.x = Mathf.Clamp(pos.x, minBounds.x, maxBounds.x);
        pos.z = Mathf.Clamp(pos.z, minBounds.y, maxBounds.y);
        cam.transform.position = pos;
    }

    void TrySelectNode(Vector2 screenPos)
    {
        if (IsPointerOverUI(screenPos)) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, nodeLayer))
        {
            LevelNode node = hit.collider.GetComponent<LevelNode>();
            if (node != null)
            {
                node.OnNodeTapped();
            }
        }
    }

    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = screenPosition;
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }
}