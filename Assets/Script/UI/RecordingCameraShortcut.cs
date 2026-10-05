using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Local desktop recording camera; never disables player or network components.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-200)]
public sealed class RecordingCameraShortcut : MonoBehaviour
{
    public enum CameraMode { FollowPlayer, FreeCamera, LockedShot }
    private static RecordingCameraShortcut instance;
    [SerializeField, Min(.1f)] private float moveSpeed = 8f;
    [SerializeField, Min(1f)] private float fastMultiplier = 4f;
    [SerializeField, Min(.01f)] private float mouseSensitivity = .12f;
    [SerializeField, Min(0f)] private float movementSmoothTime = .12f;
    private PlayerLook localLook;
    private Camera recordingCamera;
    private Transform originalParent;
    private Vector3 shotPosition, velocity;
    private Quaternion shotRotation;
    private float yaw, pitch, nearClip;
    private CursorLockMode originalCursorLock;
    private bool originalCursorVisible;
    public CameraMode Mode { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (instance != null) return;
        new GameObject("Recording Camera Shortcut").AddComponent<RecordingCameraShortcut>();
#endif
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
    private void OnEnable() => SceneManager.activeSceneChanged += ActiveSceneChanged;
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= ActiveSceneChanged;
        ReturnToPlayer(false);
    }
    private void OnDestroy() { if (instance == this) instance = null; }
    private void ActiveSceneChanged(Scene previous, Scene next) => ReturnToPlayer(false);
    private void OnApplicationFocus(bool focused)
    {
        if (!focused && Mode == CameraMode.FreeCamera) { velocity = Vector3.zero; ReleaseCursor(); }
    }

    internal static bool ShouldUseShortcut(bool control, bool pressed, bool typing) => control && pressed && !typing;
    public static bool BlocksMovement(Component actor) => instance != null && instance.isActiveAndEnabled &&
        instance.Mode == CameraMode.FreeCamera && instance.OwnsPlayer(actor);
    public static bool OverridesLook(PlayerLook look) => instance != null && instance.isActiveAndEnabled &&
        instance.Mode != CameraMode.FollowPlayer && instance.localLook == look &&
        look != null && instance.recordingCamera == look.cam;
    public static bool OwnsFreeCameraCursor(Component actor) => BlocksMovement(actor);
    private bool OwnsPlayer(Component actor) => actor != null && localLook != null &&
        (actor.transform == localLook.transform || actor.transform.IsChildOf(localLook.transform));

    /// <summary>Release before build/cinematic controllers capture the normal camera pose.</summary>
    public static void ReturnToPlayerForGameplayTransition()
    {
        if (instance != null) instance.ReturnToPlayer(true);
    }

    private bool Suspended => !Application.isFocused || Time.timeScale <= 0f ||
        (PauseManager.Instance != null && PauseManager.Instance.isPaused) ||
        RecordingHUDShortcut.IsTextInputFocused() ||
        (UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel);
    private static bool GameplayAllowsRecording =>
        !LoadingScreenManager.IsLoading &&
        (GameManager.Instance == null || (!GameManager.Instance.IsTransitioning &&
            GameManager.Instance.CurrentState == GameManager.GameState.Normal)) &&
        (FusionConnectionManager.Instance == null || !FusionConnectionManager.Instance.IsNetworkSceneLoading) &&
        (TutorialManager.Instance == null || !TutorialManager.Instance.IsTutorialActive);

    private void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Mode != CameraMode.FollowPlayer && (!GameplayAllowsRecording || localLook == null ||
            !localLook.isActiveAndEnabled || recordingCamera == null || !recordingCamera.isActiveAndEnabled ||
            localLook.cam != recordingCamera)) ReturnToPlayer(false);
        if (Suspended) { velocity = Vector3.zero; return; }
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        if (ShouldUseShortcut(control, keyboard.fKey.wasPressedThisFrame, false))
        {
            if (Mode == CameraMode.FreeCamera) ReturnToPlayer(true);
            else if (Mode == CameraMode.LockedShot) ResumeFreeCamera();
            else TryBeginFromGameplay();
        }
        else if (ShouldUseShortcut(control, keyboard.lKey.wasPressedThisFrame, false))
        {
            if (Mode == CameraMode.FreeCamera) LockCamera();
            else if (Mode == CameraMode.LockedShot) ReturnToPlayer(true);
        }
        if (Mode != CameraMode.FreeCamera) return;
        Vector3 input = new Vector3((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
            (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0),
            (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
        Mouse mouse = Mouse.current;
        bool mouseLook = mouse != null && mouse.rightButton.isPressed;
        Vector2 look = mouseLook ? mouse.delta.ReadValue() : Vector2.zero;
        if (mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > .01f)
            moveSpeed = Mathf.Clamp(moveSpeed * Mathf.Pow(1.15f, Mathf.Sign(mouse.scroll.ReadValue().y)), .5f, 100f);
        MoveCamera(input, look, keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed,
            Mathf.Min(Time.unscaledDeltaTime, .05f));
#endif
    }

    private void LateUpdate()
    {
        if (Mode == CameraMode.FollowPlayer) return;
        ApplyShotPose();
        if (Mode != CameraMode.FreeCamera) return;
        bool capture = !Suspended && Mouse.current != null && Mouse.current.rightButton.isPressed;
        Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !capture;
    }

    private void TryBeginFromGameplay()
    {
        if (!GameplayAllowsRecording) return;
        Camera active = GameManager.Instance != null ? GameManager.Instance.MainCamera : Camera.main;
        if (active == null || !active.isActiveAndEnabled) return;
        // Each client has its ordinary local scene player. Remote Fusion avatars
        // contain visual clones, not this input/camera controller.
        foreach (PlayerLook look in FindObjectsOfType<PlayerLook>())
        {
            InputManager input = look.GetComponent<InputManager>();
            if (look.cam != active || input == null || !input.isActiveAndEnabled ||
                !input.IsPlayerInputEnabled || !look.canLook) continue;
            BeginFreeCamera(look);
            break;
        }
    }

    internal bool BeginFreeCamera(PlayerLook look)
    {
        if (Mode != CameraMode.FollowPlayer || look == null || look.cam == null) return false;
        localLook = look;
        recordingCamera = look.cam;
        look.ReleaseOrbitForRecording();
        originalParent = recordingCamera.transform.parent;
        nearClip = recordingCamera.nearClipPlane;
        originalCursorLock = Cursor.lockState;
        originalCursorVisible = Cursor.visible;
        shotPosition = recordingCamera.transform.position;
        shotRotation = recordingCamera.transform.rotation;
        recordingCamera.transform.SetParent(null, true);
        ReadShotAngles();
        velocity = Vector3.zero;
        Mode = CameraMode.FreeCamera;
        return true;
    }

    private void ReadShotAngles()
    {
        yaw = shotRotation.eulerAngles.y;
        pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, shotRotation.eulerAngles.x), -89f, 89f);
    }
    internal void MoveCamera(Vector3 input, Vector2 lookDelta, bool fast, float deltaTime)
    {
        if (Mode != CameraMode.FreeCamera || recordingCamera == null || deltaTime <= 0f) return;
        yaw += lookDelta.x * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - lookDelta.y * mouseSensitivity, -89f, 89f);
        shotRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 direction = shotRotation * new Vector3(input.x, 0f, input.z) + Vector3.up * input.y;
        Vector3 desired = Vector3.ClampMagnitude(direction, 1f) * moveSpeed * (fast ? fastMultiplier : 1f);
        float blend = movementSmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-deltaTime / movementSmoothTime);
        velocity = Vector3.Lerp(velocity, desired, blend);
        shotPosition += velocity * deltaTime;
        ApplyShotPose();
    }
    internal void ApplyShotPose()
    {
        if (Mode != CameraMode.FollowPlayer && recordingCamera != null)
            recordingCamera.transform.SetPositionAndRotation(shotPosition, shotRotation);
    }
    public void LockCamera()
    {
        if (Mode != CameraMode.FreeCamera) return;
        velocity = Vector3.zero;
        Mode = CameraMode.LockedShot;
        RestoreCursor();
        ApplyShotPose();
    }
    public void ResumeFreeCamera()
    {
        if (Mode != CameraMode.LockedShot) return;
        ReadShotAngles(); velocity = Vector3.zero;
        Mode = CameraMode.FreeCamera;
    }
    public void ReturnToPlayer(bool snapToPlayer = true)
    {
        if (Mode == CameraMode.FollowPlayer) return;
        Mode = CameraMode.FollowPlayer;
        velocity = Vector3.zero;
        if (recordingCamera != null)
        {
            recordingCamera.transform.SetParent(originalParent, true);
            recordingCamera.nearClipPlane = nearClip;
        }
        RestoreCursor();
        if (snapToPlayer && localLook != null && localLook.isActiveAndEnabled)
            localLook.SnapToFollowTarget();
        localLook = null; recordingCamera = null; originalParent = null;
    }
    private void RestoreCursor() { Cursor.lockState = originalCursorLock; Cursor.visible = originalCursorVisible; }
    private static void ReleaseCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
}
