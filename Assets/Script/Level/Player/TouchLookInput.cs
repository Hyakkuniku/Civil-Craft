using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

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

    void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }

    void Update()
    {
        foreach (var touch in Touch.activeTouches)
        {
            int fingerId = touch.finger.index;
            Vector2 pos = touch.screenPosition;

            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
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
}
