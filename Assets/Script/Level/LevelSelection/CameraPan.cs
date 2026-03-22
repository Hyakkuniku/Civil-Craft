using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Pan Settings")]
    public float panSpeed = 20f;
    
    [Header("Zoom Settings")]
    public float zoomSpeedPC = 5f;
    public float zoomSpeedMobile = 0.05f;
    public float minZoom = 5f;  // The closest you can zoom in
    public float maxZoom = 25f; // The furthest you can zoom out

    private Camera cam;
    private Vector3 dragOrigin;

    void Start()
    {
        cam = Camera.main;
    }

    void Update()
    {
        HandleZoom();
        HandlePan();
    }

    void HandlePan()
    {
        // Prevent panning if the player is using two fingers to zoom
        if (Input.touchCount >= 2) return;

        if (Input.GetMouseButtonDown(0))
        {
            dragOrigin = Input.mousePosition;
            return;
        }

        if (!Input.GetMouseButton(0)) return;

        Vector3 pos = cam.ScreenToViewportPoint(Input.mousePosition - dragOrigin);
        Vector3 move = new Vector3(pos.x * panSpeed, 0, pos.y * panSpeed);
        
        transform.Translate(-move, Space.World);
        dragOrigin = Input.mousePosition;
    }

    void HandleZoom()
    {
        float zoomDelta = 0f;

        // 1. PC Zoom (Mouse Scroll Wheel)
        if (Input.mouseScrollDelta.y != 0)
        {
            // scrollDelta.y is usually 1 (up) or -1 (down)
            zoomDelta = Input.mouseScrollDelta.y * zoomSpeedPC;
        }
        // 2. Mobile Zoom (Pinch)
        else if (Input.touchCount == 2)
        {
            Touch touchZero = Input.GetTouch(0);
            Touch touchOne = Input.GetTouch(1);

            // Find the position of each touch in the previous frame
            Vector2 touchZeroPrevPos = touchZero.position - touchZero.deltaPosition;
            Vector2 touchOnePrevPos = touchOne.position - touchOne.deltaPosition;

            // Find the distance between the touches in each frame
            float prevTouchDeltaMag = (touchZeroPrevPos - touchOnePrevPos).magnitude;
            float touchDeltaMag = (touchZero.position - touchOne.position).magnitude;

            // The difference in distance is our zoom amount
            zoomDelta = (touchDeltaMag - prevTouchDeltaMag) * zoomSpeedMobile;
        }

        // Apply the zoom if there was any input
        if (zoomDelta != 0)
        {
            ApplyZoom(zoomDelta);
        }
    }

    void ApplyZoom(float delta)
    {
        // Automatically handles both Orthographic and Perspective cameras
        if (cam.orthographic)
        {
            cam.orthographicSize -= delta;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
        }
        else
        {
            cam.fieldOfView -= delta;
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView, minZoom, maxZoom);
        }
    }
}