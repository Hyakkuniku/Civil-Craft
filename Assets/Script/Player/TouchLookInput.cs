using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.EventSystems; 
using System.Collections.Generic;
using UnityEngine.UI; // --- NEW: Required to check for Buttons ---

public class TouchLookInput : MonoBehaviour
{
    private PlayerLook look;
    private int rightFingerId = -1;
    private float halfScreenWidth;

    void Awake()
    {
        look = GetComponent<PlayerLook>();
        halfScreenWidth = Screen.width / 2f;
    }

    void OnEnable() { EnhancedTouchSupport.Enable(); }
    void OnDisable() { EnhancedTouchSupport.Disable(); }

    void Update()
    {
        foreach (var touch in Touch.activeTouches)
        {
            int fingerId = touch.finger.index;
            Vector2 pos = touch.screenPosition;

            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                // Use our smart UI check
                if (IsTouchOverClickableUI(pos))
                    continue;

                if (pos.x > halfScreenWidth && rightFingerId == -1)
                {
                    rightFingerId = fingerId;
                }
            }

            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
            {
                if (fingerId == rightFingerId)
                {
                    look.ProcessLook(touch.delta);
                }
            }

            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                if (fingerId == rightFingerId)
                    rightFingerId = -1;
            }
        }
    }

    // --- THE SMART FIX: Only block the camera for ACTUAL buttons! ---
    private bool IsTouchOverClickableUI(Vector2 touchPosition)
    {
        if (EventSystem.current == null) return false;

        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = touchPosition;
        List<RaycastResult> results = new List<RaycastResult>();
        
        EventSystem.current.RaycastAll(eventData, results);
        
        foreach (RaycastResult result in results)
        {
            // Check if the UI element we touched (or its parent) has a Selectable component (like a Button, Slider, or Toggle)
            if (result.gameObject.GetComponentInParent<Selectable>() != null)
            {
                return true; // It's a real button! Block the camera.
            }
        }
        
        return false; // We just hit a transparent background or plain text. Let the camera move!
    }
}