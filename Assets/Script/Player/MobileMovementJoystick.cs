using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Drives the existing virtual left stick with one owned finger. Floating mode
/// does not need a full-screen raycast blocker or a second joystick graphic.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Civil Craft/Mobile Movement Joystick")]
public sealed class MobileMovementJoystick : OnScreenControl
{
    // Retain the package stick's serialized names/range when migrating scenes.
    [SerializeField, Min(1f)] private float m_MovementRange = 100f;
    [SerializeField] private string m_ControlPath = "<Gamepad>/leftStick";
    [SerializeField] private RectTransform joystickBase;
    [SerializeField] private InputManager playerInput;
    private readonly MobileJoystickMotion motion = new MobileJoystickMotion();
    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>(16);
    private readonly Vector3[] visualCorners = new Vector3[4];
    private PointerEventData raycastPointer;
    private EventSystem raycastEventSystem;
    private RectTransform handle, coordinateSpace;
    private Canvas canvas;
    private CanvasGroup[] canvasGroups;
    private Vector3 homeBasePosition, homeHandlePosition;
    private Vector2 homeBaseAnchoredPosition, homeHandleAnchoredPosition;
    private Vector2 lastSentValue;
    private bool initialized, touchSupportEnabled, fixedMode, hasFocus = true, appPaused;
    private int screenWidth, screenHeight;
    private Rect lastSafeArea;
    private float nextPlayerSearch;
    private const int MousePointerId = -1;

    public float movementRange => m_MovementRange;
    public RectTransform BaseRect => joystickBase;
    public bool IsDragging => motion.HasPointer;
    protected override string controlPathInternal { get => m_ControlPath; set => m_ControlPath = value; }

    // Editor migration only; values are assigned before the control is used.
    public void Configure(RectTransform baseRect, float range, string path)
    {
        joystickBase = baseRect;
        m_MovementRange = Mathf.Max(1f, range);
        m_ControlPath = path;
    }

    protected override void OnEnable()
    {
        InitializeVisuals();
        base.OnEnable();
        EnhancedTouchSupport.Enable();
        touchSupportEnabled = true;
        MobileJoystickSettings.Changed += ApplyFixedSetting;
        fixedMode = MobileJoystickSettings.IsFixed;
        screenWidth = Screen.width;
        screenHeight = Screen.height;
        lastSafeArea = Screen.safeArea;
        hasFocus = Application.isFocused;
        ResetInteraction();
    }

    protected override void OnDisable()
    {
        MobileJoystickSettings.Changed -= ApplyFixedSetting;
        ResetInteraction();
        if (touchSupportEnabled) EnhancedTouchSupport.Disable();
        touchSupportEnabled = false;
        base.OnDisable();
    }

    private void InitializeVisuals()
    {
        if (initialized) return;
        handle = transform as RectTransform;
        if (joystickBase == null && handle != null) joystickBase = handle.parent as RectTransform;
        coordinateSpace = joystickBase != null ? joystickBase.parent as RectTransform : null;
        if (handle == null || joystickBase == null || coordinateSpace == null) return;
        homeBasePosition = joystickBase.localPosition;
        homeHandlePosition = handle.localPosition;
        homeBaseAnchoredPosition = joystickBase.anchoredPosition;
        homeHandleAnchoredPosition = handle.anchoredPosition;
        canvas = GetComponentInParent<Canvas>();
        canvasGroups = GetComponentsInParent<CanvasGroup>(true);
        initialized = true;
    }

    private void Update()
    {
        // Game-view focus callbacks can be missed across Editor transitions or
        // disabled reloads. Poll the actual Editor focus before reading input.
        if (Application.isEditor && hasFocus != Application.isFocused)
        {
            ResetInteraction();
            hasFocus = Application.isFocused;
        }
        // Polling also works when Enter Play Mode disables domain/scene reload
        // and a surviving control loses a static preference-event subscription.
        if (fixedMode != MobileJoystickSettings.IsFixed) ApplyFixedSetting(MobileJoystickSettings.IsFixed);
        if (screenWidth != Screen.width || screenHeight != Screen.height || lastSafeArea != Screen.safeArea)
        {
            screenWidth = Screen.width;
            screenHeight = Screen.height;
            lastSafeArea = Screen.safeArea;
            ResetInteraction();
        }
        if (!CanUseMovement()) { ResetInteraction(); return; }

        var touches = Touch.activeTouches;
        // Mouse emulation and actual touch cannot share ownership. Otherwise a
        // new touch could suppress processing of the mouse's eventual release.
        if (motion.HasPointer && motion.PointerId == MousePointerId && touches.Count > 0)
        { ResetInteraction(); return; }
        if (motion.HasPointer && motion.PointerId != MousePointerId)
        {
            foreach (Touch touch in touches)
            {
                if (touch.touchId != motion.PointerId) continue;
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                    touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled) ResetInteraction();
                else MovePointer(touch.touchId, touch.screenPosition);
                return;
            }
            ResetInteraction(); // A lost/cancelled device cannot leave a held stick.
            return;
        }

        if (!motion.HasPointer)
            foreach (Touch touch in touches)
            {
                // Never acquire a finger already held before a menu closed.
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began &&
                    TryBeginPointer(touch.touchId, touch.screenPosition)) break;
            }

        // Preserve mouse testing of the mobile HUD in Editor/desktop. Physical
        // keyboard/gamepad bindings are unchanged; no synthetic pointer device.
        if ((Application.isEditor || !Application.isMobilePlatform) && touches.Count == 0 && Mouse.current != null)
        {
            Vector2 position = Mouse.current.position.ReadValue();
            if (!motion.HasPointer && Mouse.current.leftButton.wasPressedThisFrame)
                TryBeginPointer(MousePointerId, position);
            if (motion.HasPointer && motion.PointerId == MousePointerId)
            {
                if (!Mouse.current.leftButton.isPressed) ResetInteraction();
                else MovePointer(MousePointerId, position);
            }
        }
    }

    private bool CanUseMovement()
    {
        if (!initialized || !hasFocus || appPaused || canvas == null || !canvas.isActiveAndEnabled ||
            !canvas.rootCanvas.isActiveAndEnabled || !joystickBase.gameObject.activeInHierarchy ||
            LoadingScreenManager.IsLoading || PauseManager.Instance != null && PauseManager.Instance.isPaused ||
            UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel ||
            GameManager.Instance != null && (GameManager.Instance.IsTransitioning ||
                GameManager.Instance.CurrentState == GameManager.GameState.Building)) return false;
        if (canvasGroups != null)
            foreach (CanvasGroup group in canvasGroups)
            {
                if (group == null || !group.enabled) continue;
                if (!group.interactable || group.alpha <= 0.001f) return false;
                if (group.ignoreParentGroups) break;
            }
        if ((playerInput == null || !playerInput.isActiveAndEnabled) && Time.unscaledTime >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.unscaledTime + 1f;
            // Network scenes may contain remote, disabled input components.
            foreach (InputManager candidate in FindObjectsOfType<InputManager>())
                if (candidate.isActiveAndEnabled) { playerInput = candidate; break; }
        }
        return playerInput != null && playerInput.isActiveAndEnabled && playerInput.IsPlayerInputEnabled &&
            !RecordingCameraShortcut.BlocksMovement(playerInput);
    }

    private Camera EventCamera => canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
        ? canvas.worldCamera : null;

    private bool TryBeginPointer(int pointerId, Vector2 screenPosition)
    {
        if (motion.HasPointer || !CanUseMovement() || screenPosition.x < 0f ||
            screenPosition.x >= Screen.width * 0.5f || screenPosition.y < 0f || screenPosition.y > Screen.height ||
            fixedMode && !RectTransformUtility.RectangleContainsScreenPoint(joystickBase, screenPosition, EventCamera) ||
            IsStartBlockedByUI(screenPosition)) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(coordinateSpace, screenPosition, EventCamera, out Vector2 local))
            return false;
        if (fixedMode) motion.ClearFloatingBounds();
        else if (!TryGetFloatingOriginBounds(out Rect bounds) || !motion.SetFloatingBounds(bounds)) return false;
        if (!motion.TryBegin(pointerId, local, homeBasePosition, fixedMode)) return false;
        ApplyVisuals();
        PublishValue();
        return true;
    }

    private bool TryGetFloatingOriginBounds(out Rect bounds)
    {
        bounds = default;
        Rect safe = Screen.safeArea;
        if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
        Vector2 screenMin = new Vector2(Mathf.Max(0f, safe.xMin), Mathf.Max(0f, safe.yMin));
        Vector2 screenMax = new Vector2(Mathf.Min(Screen.width * .5f, safe.xMax), Mathf.Min(Screen.height, safe.yMax));
        if (screenMax.x <= screenMin.x || screenMax.y <= screenMin.y ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(coordinateSpace, screenMin, EventCamera, out Vector2 localMin) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(coordinateSpace, screenMax, EventCamera, out Vector2 localMax)) return false;

        Vector2 origin = joystickBase.localPosition;
        Vector2 baseMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 baseMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        AccumulateVisualBounds(joystickBase, origin, ref baseMin, ref baseMax);
        Vector2 handleMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 handleMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        AccumulateVisualBounds(handle, origin, ref handleMin, ref handleMax);
        // Reserve the handle's full travel as well as the ring: neither graphic
        // may cross the screen midpoint or a safe-area edge during a long drag.
        Vector3 travelX = coordinateSpace.InverseTransformVector(joystickBase.TransformVector(Vector3.right * m_MovementRange));
        Vector3 travelY = coordinateSpace.InverseTransformVector(joystickBase.TransformVector(Vector3.up * m_MovementRange));
        Vector2 travel = new Vector2(Mathf.Sqrt(travelX.x * travelX.x + travelY.x * travelY.x),
            Mathf.Sqrt(travelX.y * travelX.y + travelY.y * travelY.y));
        Vector2 visualMin = Vector2.Min(baseMin, handleMin - travel);
        Vector2 visualMax = Vector2.Max(baseMax, handleMax + travel);
        Vector2 min = Vector2.Min(localMin, localMax) - visualMin;
        Vector2 max = Vector2.Max(localMin, localMax) - visualMax;
        // Extremely narrow views may not fit the full artwork. Prefer clipping
        // at the left screen edge over letting any part enter the camera side.
        if (min.x > max.x) min.x = max.x;
        if (min.y > max.y) min.y = max.y = (min.y + max.y) * .5f;
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private void AccumulateVisualBounds(RectTransform rect, Vector2 origin, ref Vector2 min, ref Vector2 max)
    {
        rect.GetWorldCorners(visualCorners);
        foreach (Vector3 corner in visualCorners)
        {
            Vector2 offset = (Vector2)coordinateSpace.InverseTransformPoint(corner) - origin;
            min = Vector2.Min(min, offset);
            max = Vector2.Max(max, offset);
        }
    }

    private bool IsStartBlockedByUI(Vector2 position)
    {
        EventSystem current = EventSystem.current;
        if (current == null) return true;
        if (raycastPointer == null || raycastEventSystem != current)
        {
            raycastEventSystem = current;
            raycastPointer = new PointerEventData(current);
        }
        raycastPointer.Reset();
        raycastPointer.position = position;
        raycastResults.Clear();
        current.RaycastAll(raycastPointer, raycastResults);
        return AreStartRaycastsBlocked(raycastResults);
    }

    private bool AreStartRaycastsBlocked(List<RaycastResult> results)
    {
        foreach (RaycastResult hit in results)
        {
            // Ignore non-UI (physics) raycasts and allow this control's graphics.
            // Other buttons, minimaps, scroll views and modal blockers take priority.
            if (!(hit.module is GraphicRaycaster) || hit.gameObject == null) continue;
            // Raycasts are sorted front-to-back. A joystick hit wins over UI
            // behind it; a button/blocker above the joystick still wins.
            if (hit.gameObject.transform.IsChildOf(joystickBase)) return false;
            // Build backgrounds still raycast when their render camera is off.
            // Ignore only their decoration for walking; do not disable targets
            // used by BarCreator's construction input when build mode resumes.
            if (IsDecorativeBuildGrid(hit.gameObject)) continue;
            return true;
        }
        return false;
    }

    private static bool IsDecorativeBuildGrid(GameObject target)
    {
        Canvas owner = target.GetComponentInParent<Canvas>(true);
        if (owner == null || owner.GetComponent<BuildGridRendering>() == null) return false;
        for (Transform node = target.transform; node != null; node = node.parent)
        {
            // Preserve buttons, custom drag surfaces and disabled handlers
            // within this canvas. An outer BarCreator is deliberately excluded:
            // CanUseMovement already rejects building mode before this query.
            foreach (MonoBehaviour component in node.GetComponents<MonoBehaviour>())
                if (component is IEventSystemHandler) return false;
            if (node == owner.transform) break;
        }
        return true;
    }

    private void MovePointer(int pointerId, Vector2 screenPosition)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(coordinateSpace, screenPosition, EventCamera, out Vector2 local))
        { ResetInteraction(); return; }
        if (motion.Move(pointerId, local, m_MovementRange)) { ApplyVisuals(); PublishValue(); }
    }

    private void ApplyVisuals()
    {
        Vector2 origin = motion.Origin;
        joystickBase.localPosition = new Vector3(origin.x, origin.y, homeBasePosition.z);
        Vector2 offset = motion.HandlePosition - origin;
        handle.localPosition = homeHandlePosition + new Vector3(offset.x, offset.y, 0f);
    }

    private void PublishValue()
    {
        if (lastSentValue == motion.Value) return;
        lastSentValue = motion.Value;
        SendValueToControl(lastSentValue);
    }

    private void ResetInteraction()
    {
        motion.Cancel();
        if (lastSentValue != Vector2.zero) SendValueToControl(Vector2.zero);
        lastSentValue = Vector2.zero;
        if (!initialized) return;
        // Anchored homes survive safe-area/layout changes and screen rotation;
        // a captured localPosition alone can drift when the parent rect resizes.
        joystickBase.anchoredPosition = homeBaseAnchoredPosition;
        handle.anchoredPosition = homeHandleAnchoredPosition;
        homeBasePosition = joystickBase.localPosition;
        homeHandlePosition = handle.localPosition;
    }

    private void ApplyFixedSetting(bool isFixed) { ResetInteraction(); fixedMode = isFixed; }
    private void OnApplicationFocus(bool focused) { hasFocus = focused; if (!focused) ResetInteraction(); }
    private void OnApplicationPause(bool paused) { appPaused = paused; if (paused) ResetInteraction(); }
}
