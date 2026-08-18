using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.EventSystems; 
using System.Collections.Generic;
using UnityEngine.UI;

public class TouchLookInput : MonoBehaviour
{
    private PlayerLook look;
    private int rightFingerId = -1;
    private float halfScreenWidth;

    // --- OPTIMIZATION: Cache these to prevent memory allocation on every screen tap! ---
    private PointerEventData cachedEventData;
    private List<RaycastResult> cachedRaycastResults = new List<RaycastResult>();

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

    private bool IsTouchOverClickableUI(Vector2 touchPosition)
    {
        if (EventSystem.current == null) return false;

        // Initialize it once if it's null, otherwise reuse it!
        if (cachedEventData == null)
        {
            cachedEventData = new PointerEventData(EventSystem.current);
        }
        
        cachedEventData.position = touchPosition;
        cachedRaycastResults.Clear(); // Empty the old results
        
        EventSystem.current.RaycastAll(cachedEventData, cachedRaycastResults);
        
        foreach (RaycastResult result in cachedRaycastResults)
        {
            if (result.gameObject.GetComponentInParent<Selectable>() != null)
            {
                return true; 
            }
        }
        
        return false; 
    }
}