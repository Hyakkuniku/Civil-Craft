#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using Object = UnityEngine.Object;

/// <summary>Opt-in observations only. Never sends input, starts Play Mode, changes UI or reads account data.</summary>
[InitializeOnLoad]
public static class MobileJoystickRuntimeDiagnostics
{
    private const string Request = "Temp/mobile-joystick-runtime-diagnostics.request";
    private const string Report = "Temp/mobile-joystick-runtime-diagnostics.txt";
    private static double nextSnapshot;
    private static int attempts;
    private static readonly List<RaycastResult> hits = new List<RaycastResult>(32);
    private static readonly Queue<string> observations = new Queue<string>();
    private static PointerEventData pointer;
    private static EventSystem pointerOwner;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    static MobileJoystickRuntimeDiagnostics()
    {
        EditorApplication.update += Observe;
        InputSystem.onAfterUpdate += ObservePlayerInput;
    }

    private static void ObservePlayerInput()
    {
        // EditorApplication.update can see Editor-window mouse coordinates,
        // not the Game-view state queried by the player Update. Capture presses
        // during the actual runtime input update instead of guessing from it.
        if (!EditorApplication.isPlaying ||
            (InputState.currentUpdateType != InputUpdateType.Dynamic && InputState.currentUpdateType != InputUpdateType.Fixed) ||
            Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        ObserveSnapshot(true);
    }

    private static T Field<T>(MobileMovementJoystick stick, string name)
    {
        object value = typeof(MobileMovementJoystick).GetField(name, PrivateInstance)?.GetValue(stick);
        return value is T typed ? typed : default;
    }

    private static void Observe()
    {
        ObserveSnapshot(false);
    }

    private static void ObserveSnapshot(bool runtimePress)
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            BuildPipeline.isBuildingPlayer) return;
        Mouse mouse = Mouse.current;
        bool pressed = EditorApplication.isPlaying && mouse != null && mouse.leftButton.isPressed;
        bool start = runtimePress;
        if (!start && EditorApplication.timeSinceStartup < nextSnapshot) return;
        nextSnapshot = EditorApplication.timeSinceStartup + 1d;
        if (start) attempts++;
        try
        {
            var text = new StringBuilder();
            text.AppendLine("Read-only joystick diagnostics; no input, scene, preferences, save or account changes.");
            text.AppendLine($"Playing={EditorApplication.isPlaying}; inputUpdate={InputState.currentUpdateType}; Paused={EditorApplication.isPaused}; hostEditor={Application.isEditor}; mobileReported={Application.isMobilePlatform}; focused={Application.isFocused}; mousePresent={mouse != null}; mouseHeld={pressed}; attempts={attempts}; fixed={MobileJoystickSettings.IsFixed}; screen={Screen.width}x{Screen.height}");
            Vector2 position = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            int touchCount = EnhancedTouchSupport.enabled ? Touch.activeTouches.Count : 0;
            text.AppendLine($"Pointer={position}; leftHalf={position.x < Screen.width * .5f}; safeArea={Screen.safeArea}; touches={touchCount}");
            text.AppendLine($"Loading={LoadingScreenManager.IsLoading}; pause={PauseManager.Instance != null && PauseManager.Instance.isPaused}; coordinatedPanel={UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel}; state={(GameManager.Instance != null ? GameManager.Instance.CurrentState.ToString() : "none")}; transition={GameManager.Instance != null && GameManager.Instance.IsTransitioning}");
            foreach (InputManager input in Object.FindObjectsOfType<InputManager>(true))
                text.AppendLine($"Input component={input.gameObject.scene.name}/{input.name}; active={input.isActiveAndEnabled}; actionsEnabled={input.IsPlayerInputEnabled}; recordingBlocked={RecordingCameraShortcut.BlocksMovement(input)}");
            foreach (MobileMovementJoystick stick in Object.FindObjectsOfType<MobileMovementJoystick>(true))
            {
                RectTransform baseRect = stick.BaseRect;
                Canvas cachedCanvas = Field<Canvas>(stick, "canvas");
                InputManager input = Field<InputManager>(stick, "playerInput");
                text.AppendLine($"Joystick={Path(stick.transform)}; active={stick.isActiveAndEnabled}; initialized={Field<bool>(stick, "initialized")}; cachedFocus={Field<bool>(stick, "hasFocus")}; appPaused={Field<bool>(stick, "appPaused")}; dragging={stick.IsDragging}; boundInput={input != null}; boundEnabled={input != null && input.IsPlayerInputEnabled}; canvas={cachedCanvas != null}; canvasEnabled={cachedCanvas != null && cachedCanvas.isActiveAndEnabled}; devicePresent={stick.control != null}; deviceEnabled={stick.control != null && stick.control.device.enabled}");
                Camera camera = cachedCanvas != null && cachedCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? cachedCanvas.worldCamera : null;
                text.AppendLine($"  base={baseRect != null}; baseHit={baseRect != null && RectTransformUtility.RectangleContainsScreenPoint(baseRect, position, camera)}; lastSent={Field<Vector2>(stick, "lastSentValue")}");
                if (Field<bool>(stick, "initialized"))
                {
                    MethodInfo geometry = typeof(MobileMovementJoystick).GetMethod("TryGetFloatingOriginBounds", PrivateInstance);
                    if (geometry != null)
                    {
                        object[] arguments = { default(Rect) };
                        bool validBounds = (bool)geometry.Invoke(stick, arguments);
                        text.AppendLine($"  floatingBoundsValid={validBounds}; localBounds={arguments[0]}");
                    }
                }
                CanvasGroup[] groups = Field<CanvasGroup[]>(stick, "canvasGroups");
                if (groups != null) foreach (CanvasGroup group in groups)
                    if (group != null) text.AppendLine($"  Group={Path(group.transform)}; enabled={group.enabled}; alpha={group.alpha}; interactable={group.interactable}; ignoreParent={group.ignoreParentGroups}");
            }
            EventSystem current = EventSystem.current;
            text.AppendLine($"EventSystem={current != null}");
            if (current != null)
            {
                if (pointer == null || pointerOwner != current) { pointerOwner = current; pointer = new PointerEventData(current); }
                pointer.Reset(); pointer.position = position; hits.Clear(); current.RaycastAll(pointer, hits);
                foreach (RaycastResult hit in hits)
                    if (hit.gameObject != null && hit.module is GraphicRaycaster)
                    {
                        Canvas hitCanvas = hit.gameObject.GetComponentInParent<Canvas>(true);
                        Camera hitCamera = hitCanvas != null ? hitCanvas.worldCamera : null;
                        text.AppendLine($"  UI hit={Path(hit.gameObject.transform)}; order={hit.sortingOrder}; depth={hit.depth}; module={hit.module.GetType().Name}; canvas={(hitCanvas != null ? hitCanvas.name : "none")}; mode={(hitCanvas != null ? hitCanvas.renderMode.ToString() : "none")}; camera={(hitCamera != null ? hitCamera.name : "none")}; cameraEnabled={hitCamera != null && hitCamera.isActiveAndEnabled}");
                    }
            }
            foreach (Gamepad pad in Gamepad.all)
                text.AppendLine($"Gamepad id={pad.deviceId}; enabled={pad.enabled}; leftStick={pad.leftStick.ReadValue()}; onScreen={pad.usages.Contains(new UnityEngine.InputSystem.Utilities.InternedString("OnScreen"))}");
            if (start)
            {
                observations.Enqueue($"CLICK {attempts} at editorTime={EditorApplication.timeSinceStartup:0.00}\n" + text);
                while (observations.Count > 8) observations.Dequeue();
            }
            foreach (string observation in observations) text.AppendLine(observation);
            Directory.CreateDirectory("Temp"); File.WriteAllText(Report, text.ToString());
        }
        catch (Exception error) { File.WriteAllText(Report, "Read-only diagnostics error: " + error.Message); }
    }

    private static string Path(Transform node)
    {
        string name = node.name;
        while (node.parent != null) { node = node.parent; name = node.name + "/" + name; }
        return name;
    }
}
#endif
