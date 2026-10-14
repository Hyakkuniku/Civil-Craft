using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Changes only one Camera's native occlusion switch, never shared renderers or layers.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Camera)), DefaultExecutionOrder(500)]
public sealed class CanyonCameraOcclusionPolicy : MonoBehaviour
{
    public enum CameraRole { Excluded = 0, Gameplay = 1 }

    [SerializeField] private CanyonOcclusionProfile profile;
    [SerializeField] private CameraRole role = CameraRole.Excluded;
    private Camera ownedCamera;
    private readonly HashSet<Object> suspensionOwners = new HashSet<Object>();
    private readonly List<Object> obsoleteOwners = new List<Object>(2);
    private static readonly List<CanyonCameraOcclusionPolicy> policies = new List<CanyonCameraOcclusionPolicy>(24);

    public CameraRole Role => role;
    public CanyonOcclusionProfile Profile => profile;

    public void Configure(CanyonOcclusionProfile sceneProfile, CameraRole cameraRole)
    {
        profile = sceneProfile;
        role = cameraRole;
        RefreshPolicy();
    }

    public void SetSuspended(Object owner, bool suspended)
    {
        if (owner == null) return;
        if (suspended) suspensionOwners.Add(owner);
        else suspensionOwners.Remove(owner);
        RefreshPolicy();
    }

    public static void SetSuspended(Camera camera, Object owner, bool suspended)
    {
        if (camera != null && camera.TryGetComponent(out CanyonCameraOcclusionPolicy policy))
            policy.SetSuspended(owner, suspended);
    }

    private void CaptureCamera()
    {
        if (ownedCamera == null) ownedCamera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        CaptureCamera();
        if (!policies.Contains(this)) policies.Add(this);
        BindCallbacks();
        RefreshPolicy();
    }

    private void LateUpdate()
    {
        // Only the gameplay camera needs a moving coverage/state check. All
        // other roles stay excluded, even when rendered manually for a capture.
        if (role == CameraRole.Gameplay) RefreshPolicy();
    }

    public void RefreshPolicy()
    {
        CaptureCamera();
        if (ownedCamera == null) return;
        obsoleteOwners.Clear();
        foreach (Object owner in suspensionOwners)
            if (owner == null) obsoleteOwners.Add(owner);
        foreach (Object owner in obsoleteOwners) suspensionOwners.Remove(owner);

        GameManager manager = GameManager.Instance;
        bool ready = isActiveAndEnabled && role == CameraRole.Gameplay &&
            ownedCamera.isActiveAndEnabled && !ownedCamera.orthographic && ownedCamera.targetTexture == null &&
            profile != null && profile.isActiveAndEnabled && profile.OcclusionRequested &&
            profile.bakeVerified && profile.bakedData != null && profile.gameplayCamera == ownedCamera &&
            gameObject.scene.IsValid() && gameObject.scene.isLoaded &&
            gameObject.scene.name == CanyonOcclusionProfile.TargetSceneName &&
            profile.gameObject.scene == gameObject.scene &&
            manager != null && manager.isActiveAndEnabled && manager.gameObject.scene == gameObject.scene &&
            manager.MainCamera == ownedCamera && manager.CurrentState == GameManager.GameState.Normal &&
            !manager.IsTransitioning && !LoadingScreenManager.IsLoading &&
            suspensionOwners.Count == 0 && profile.ContainsCameraPosition(ownedCamera.transform.position);
        ownedCamera.useOcclusionCulling = ready;
    }

    private void OnDisable()
    {
        policies.Remove(this);
        // Removing/disabling the component must fail open, not leave the last
        // enabled state active while the camera is moved or scenery is isolated.
        if (ownedCamera != null) ownedCamera.useOcclusionCulling = false;
        if (policies.Count == 0) UnbindCallbacks();
    }

    private void OnDestroy()
    {
        policies.Remove(this);
        // The camera may survive component removal. Do not reactivate an old,
        // unguarded true setting after the coverage/state checks disappear.
        if (ownedCamera != null) ownedCamera.useOcclusionCulling = false;
        if (policies.Count == 0) UnbindCallbacks();
    }

    private static void BindCallbacks()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering;
        RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
        Camera.onPreCull -= BeforeBuiltInCamera;
        Camera.onPreCull += BeforeBuiltInCamera;
    }

    private static void UnbindCallbacks()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering;
        Camera.onPreCull -= BeforeBuiltInCamera;
    }

    private static void BeforeCameraRendering(ScriptableRenderContext context, Camera camera) => ApplyFor(camera);
    private static void BeforeBuiltInCamera(Camera camera) => ApplyFor(camera);

    private static void ApplyFor(Camera camera)
    {
        foreach (CanyonCameraOcclusionPolicy policy in policies)
        {
            if (policy == null || !policy.isActiveAndEnabled || policy.ownedCamera != camera) continue;
            policy.RefreshPolicy();
            return;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        UnbindCallbacks();
        for (int index = policies.Count - 1; index >= 0; index--)
        {
            CanyonCameraOcclusionPolicy policy = policies[index];
            if (policy == null || !policy.isActiveAndEnabled) { policies.RemoveAt(index); continue; }
            policy.suspensionOwners.Clear();
            policy.obsoleteOwners.Clear();
            policy.CaptureCamera();
            if (policy.ownedCamera != null) policy.ownedCamera.useOcclusionCulling = false;
        }
        // Preserve surviving components when scene/domain reload is disabled;
        // no per-frame discovery or duplicated render callbacks is necessary.
        if (policies.Count > 0) BindCallbacks();
    }
}
