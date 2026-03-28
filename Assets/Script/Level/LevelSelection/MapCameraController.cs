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
    public float panCloseUIThreshold = 2f; 

    [Header("Zoom Settings")]
    public float zoomSpeedPC = 5f;
    public float zoomSpeedMobile = 0.01f;
    public float minZoom = 5f;
    public float maxZoom = 25f;

    [Header("Map Boundaries")]
    [Tooltip("Assign your water plane here to automatically calculate bounds!")]
    public Renderer waterPlane; 
    public Vector2 minBounds = new Vector2(-30, -30);
    public Vector2 maxBounds = new Vector2(30, 30);

    [Header("Interaction Settings")]
    [Tooltip("Set your Level Nodes to a specific layer (e.g., 'MapNodes') and select it here!")]
    public LayerMask nodeLayer; 

    private Vector2 dragOriginPC;
    private bool isDraggingPC = false;
    private float lastTapTime = 0f;

    // --- OPTIMIZATION: Pre-allocate to prevent UI Raycast memory leaks! ---
    private PointerEventData cachedEventData;
    private List<RaycastResult> cachedRaycastResults = new List<RaycastResult>();

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void Start()
    {
        // Automatically calculate map limits if a water plane is assigned
        if (waterPlane != null)
        {
            minBounds = new Vector2(waterPlane.bounds.min.x, waterPlane.bounds.min.z);
            maxBounds = new Vector2(waterPlane.bounds.max.x, waterPlane.bounds.max.z);
        }

        if (EventSystem.current != null)
            cachedEventData = new PointerEventData(EventSystem.current);
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

        if (waterPlane != null)
        {
            float halfHeight = 0f;
            float halfWidth = 0f;

            // 1. Calculate how much the camera can currently see
            if (cam.orthographic)
            {
                halfHeight = cam.orthographicSize;
                halfWidth = halfHeight * cam.aspect;
            }
            else
            {
                // For Perspective cameras, calculate based on distance to the water
                float distance = Mathf.Abs(cam.transform.position.y - waterPlane.bounds.center.y);
                halfHeight = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                halfWidth = halfHeight * cam.aspect;
            }

            // 2. Shrink the bounds by the size of the camera's view
            float limitMinX = waterPlane.bounds.min.x + halfWidth;
            float limitMaxX = waterPlane.bounds.max.x - halfWidth;
            float limitMinZ = waterPlane.bounds.min.z + halfHeight;
            float limitMaxZ = waterPlane.bounds.max.z - halfHeight;

            // Failsafe: If zoomed out so far that the camera sees MORE than the whole water plane,
            // this locks the camera to the center so it doesn't glitch out.
            if (limitMinX > limitMaxX) limitMinX = limitMaxX = waterPlane.bounds.center.x;
            if (limitMinZ > limitMaxZ) limitMinZ = limitMaxZ = waterPlane.bounds.center.z;

            // 3. Apply the dynamic limits!
            pos.x = Mathf.Clamp(pos.x, limitMinX, limitMaxX);
            pos.z = Mathf.Clamp(pos.z, limitMinZ, limitMaxZ);
        }
        else
        {
            // Fallback to manual bounds if no water plane is assigned
            pos.x = Mathf.Clamp(pos.x, minBounds.x, maxBounds.x);
            pos.z = Mathf.Clamp(pos.z, minBounds.y, maxBounds.y);
        }

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
        
        if (cachedEventData == null) cachedEventData = new PointerEventData(EventSystem.current);
        
        cachedEventData.position = screenPosition;
        cachedRaycastResults.Clear();
        
        EventSystem.current.RaycastAll(cachedEventData, cachedRaycastResults);
        return cachedRaycastResults.Count > 0;
    }
}