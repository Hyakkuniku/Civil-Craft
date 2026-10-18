using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Borrows only the local gameplay camera while an NPC-owned quiz is open.
/// Lives beside QuizUIManager, never beneath the panel that Pause can hide.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(300)]
public sealed class QuizNPCPresentation : MonoBehaviour
{
    [SerializeField, Min(0f)] private float cameraBlendDuration = 0.45f;
    [SerializeField, Range(30f, 65f)] private float closeupFieldOfView = 42f;
    [SerializeField, Min(0f)] private float questionTalkingDuration = 3f;
    [SerializeField] private string talkingBoolParameter = "isTalking";
    [SerializeField] private string walkingBoolParameter = "isWalking";
    [SerializeField] private string runningBoolParameter = "isRunning";

    private static QuizNPCPresentation activePresentation;
    private NPCProgressionManager speaker;
    private NPCProgressionPhase speakerPhase;
    private UnityEngine.Object sessionOwner;
    private Camera borrowedCamera;
    private PlayerLook localLook;
    private bool lookEnabledBefore;
    private Transform cameraParent;
    private Vector3 cameraLocalPosition, cameraWorldPosition;
    private Quaternion cameraLocalRotation, cameraWorldRotation;
    private float fieldOfViewBefore, nearClipBefore;
    private float fieldOfViewApplied, nearClipApplied;
    private Animator speakerAnimator;
    private readonly List<BoolSnapshot> animationBefore = new List<BoolSnapshot>(3);
    private readonly List<Renderer> renderers = new List<Renderer>(8);
    private readonly List<CinematicDirector> sceneCinematics = new List<CinematicDirector>(4);
    private readonly RaycastHit[] collisionHits = new RaycastHit[24];
    private readonly QuizLocalAvatarVisibility avatarVisibility = new QuizLocalAvatarVisibility();
    private Rect viewport;
    private Bounds speakerBounds;
    private Vector3 shotPosition, startPosition;
    private Quaternion shotRotation, startRotation;
    private float startedAt, speakingUntil;
    private float lastAspect;
    private bool speakingAllowed;
    private bool active;

    private struct BoolSnapshot
    {
        public string name;
        public bool value;
    }

    public bool IsActive => active;
    public string SpeakerName { get; private set; } = string.Empty;

    /// <summary>Only the actual requesting NPC is framed; no nearest-NPC lookup.</summary>
    public bool Begin(UnityEngine.Object owner, Rect npcScreenViewport)
    {
        End(true);
        if (!isActiveAndEnabled || owner == null || activePresentation != null) return false;
        GameObject ownerObject = owner as GameObject;
        if (owner is Component component) ownerObject = component.gameObject;
        NPCProgressionManager candidate = ownerObject != null
            ? ownerObject.GetComponentInParent<NPCProgressionManager>() : null;
        if (candidate == null || !candidate.isActiveAndEnabled || candidate.IsTravelling ||
            !candidate.gameObject.scene.IsValid() || !candidate.gameObject.scene.isLoaded ||
            !NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(candidate.transform)) return false;

        GameManager manager = GameManager.Instance;
        Camera camera = manager != null && manager.gameObject.scene == candidate.gameObject.scene
            ? manager.MainCamera : null;
        if (camera == null || !camera.isActiveAndEnabled || camera.orthographic || camera.targetTexture != null ||
            camera.gameObject.scene != candidate.gameObject.scene || !AllowsGameplayCamera(manager)) return false;

        // Search only once at acquisition. Remote avatars have no enabled local
        // InputManager; a map/build/portrait camera must never be substituted.
        PlayerLook look = null;
        foreach (PlayerLook possible in FindObjectsOfType<PlayerLook>())
        {
            InputManager input = possible.GetComponent<InputManager>();
            if (possible.cam == camera && possible.gameObject.scene == candidate.gameObject.scene &&
                input != null && input.isActiveAndEnabled)
            { look = possible; break; }
        }
        if (look == null || !TryReadBounds(candidate, out Bounds bounds)) return false;
        sceneCinematics.Clear();
        foreach (CinematicDirector director in FindObjectsOfType<CinematicDirector>())
            if (director.gameObject.scene == candidate.gameObject.scene)
            {
                if (director.IsPlaying) { sceneCinematics.Clear(); return false; }
                sceneCinematics.Add(director);
            }

        // Recording also writes in LateUpdate even when PlayerLook is disabled.
        // Release its scoped shot before capturing the ordinary return pose.
        RecordingCameraShortcut.ReturnToPlayerForGameplayTransition();
        // Clear the orbit's temporary wall/near-clip adjustment before taking
        // our snapshot. Otherwise re-enabling PlayerLook could mistake the
        // temporary close-camera value for its authored normal clipping plane.
        look.ReleaseOrbitForRecording();
        speaker = candidate;
        speakerPhase = candidate.CurrentPhase;
        sessionOwner = owner;
        borrowedCamera = camera;
        localLook = look;
        speakerBounds = bounds;
        viewport = npcScreenViewport;
        if (!TryCalculateShot()) { ClearReferences(); return false; }
        if (!avatarVisibility.Begin(camera, QuizLocalAvatarVisibility.ResolveLocalVisual(look)))
        { ClearReferences(); return false; }

        cameraParent = camera.transform.parent;
        cameraLocalPosition = camera.transform.localPosition;
        cameraLocalRotation = camera.transform.localRotation;
        cameraWorldPosition = startPosition = camera.transform.position;
        cameraWorldRotation = startRotation = camera.transform.rotation;
        fieldOfViewBefore = camera.fieldOfView;
        nearClipBefore = camera.nearClipPlane;
        lookEnabledBefore = look.enabled;
        // The orbit's temporary clip has already been released. Use a
        // close-up-safe clipping plane only while we own this shot.
        CanyonCameraOcclusionPolicy.SetSuspended(camera, this, true);
        look.enabled = false;
        nearClipApplied = Mathf.Min(nearClipBefore, 0.08f);
        camera.nearClipPlane = nearClipApplied;
        startedAt = Time.unscaledTime;
        speakingAllowed = true;
        speakingUntil = startedAt + questionTalkingDuration;
        active = true;
        activePresentation = this;
        SpeakerName = ResolveSpeakerName(candidate);
        CaptureAnimation(candidate);
        SceneManager.sceneUnloaded += SceneUnloaded;
        ApplyAnimation();
        ApplyCamera();
        return true;
    }

    /// <summary>Normalized screen space reserved for the speaker by the quiz UI.</summary>
    public void SetViewport(Rect npcScreenViewport)
    {
        if (!active || viewport == npcScreenViewport) return;
        viewport = npcScreenViewport;
        if (!TryCalculateShot()) End(true);
    }

    /// <summary>New questions receive a short existing talking/gesture animation.</summary>
    public void PresentQuestion()
    {
        if (!active) return;
        speakingUntil = Time.unscaledTime + Mathf.Max(0f, questionTalkingDuration);
        ApplyAnimation();
    }

    /// <summary>Reading/results/Pause stop gestures without relinquishing the shot.</summary>
    public void SetSpeaking(bool value)
    {
        speakingAllowed = value;
        if (active) ApplyAnimation();
    }

    public static bool IsPresenting(Transform npc)
    {
        return activePresentation != null && activePresentation.active && npc != null &&
            activePresentation.speaker != null && activePresentation.speaker.transform == npc;
    }

    private void LateUpdate()
    {
        avatarVisibility.PrepareForNewFrame();
        if (!active) return;
        bool valid = sessionOwner != null && speaker != null && speaker.isActiveAndEnabled &&
            !(sessionOwner is Behaviour ownerBehaviour && !ownerBehaviour.isActiveAndEnabled) &&
            !speaker.IsTravelling && speaker.CurrentPhase == speakerPhase &&
            speaker.gameObject.scene.IsValid() && speaker.gameObject.scene.isLoaded &&
            NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(speaker.transform) &&
            borrowedCamera != null && borrowedCamera.isActiveAndEnabled && localLook != null &&
            localLook.cam == borrowedCamera && borrowedCamera.transform.parent == cameraParent &&
            !borrowedCamera.orthographic && borrowedCamera.targetTexture == null &&
            AllowsGameplayCamera(GameManager.Instance) && GameManager.Instance.MainCamera == borrowedCamera;
        if (valid)
            foreach (CinematicDirector director in sceneCinematics)
                if (director != null && director.IsPlaying) { valid = false; break; }
        if (!valid)
        {
            // Do not overwrite an externally started transition/cinematic's pose.
            EndInternal(CanRestoreShot());
            return;
        }
        if (!Mathf.Approximately(lastAspect, borrowedCamera.aspect) && !TryCalculateShot())
        { End(true); return; }
        ApplyAnimation();
        ApplyCamera();
    }

    private static bool AllowsGameplayCamera(GameManager manager)
    {
        return manager != null && !manager.IsTransitioning &&
            manager.CurrentState == GameManager.GameState.Normal && !LoadingScreenManager.IsLoading;
    }

    private bool CanRestoreShot()
    {
        if (borrowedCamera == null || borrowedCamera.transform.parent != cameraParent ||
            !AllowsGameplayCamera(GameManager.Instance)) return false;
        foreach (CinematicDirector director in sceneCinematics)
            if (director != null && director.IsPlaying) return false;
        return true;
    }

    private void ApplyCamera()
    {
        if (borrowedCamera == null) return;
        float t = cameraBlendDuration <= 0f ? 1f :
            Mathf.Clamp01((Time.unscaledTime - startedAt) / cameraBlendDuration);
        t = t * t * (3f - 2f * t);
        borrowedCamera.transform.SetPositionAndRotation(Vector3.Lerp(startPosition, shotPosition, t),
            Quaternion.Slerp(startRotation, shotRotation, t));
        fieldOfViewApplied = Mathf.Lerp(fieldOfViewBefore, closeupFieldOfView, t);
        borrowedCamera.fieldOfView = fieldOfViewApplied;
    }

    private bool TryCalculateShot()
    {
        if (speaker == null || borrowedCamera == null) return false;
        Bounds focusBounds = SpeakerFocusBounds(speakerBounds);
        if (!TryFramingPose(focusBounds, speaker.transform.forward, borrowedCamera.aspect,
                closeupFieldOfView, borrowedCamera.rect, viewport, out shotPosition, out shotRotation)) return false;
        lastAspect = borrowedCamera.aspect;

        // Use the gameplay orbit's own scenery mask. Ignore the speaking NPC and
        // local player, and never modify shared renderers/layers to obtain a shot.
        Vector3 focus = focusBounds.center;
        Vector3 delta = shotPosition - focus;
        float distance = delta.magnitude;
        if (localLook == null || localLook.collisionMask.value == 0 || distance <= 0.01f) return true;
        int count = Physics.SphereCastNonAlloc(focus, 0.14f, delta / distance, collisionHits, distance,
            localLook.collisionMask, QueryTriggerInteraction.Ignore);
        // A full non-alloc buffer cannot prove that all nearer obstructions
        // were observed. Use the normal quiz UI rather than guess a safe shot.
        if (count >= collisionHits.Length) return false;
        float clear = distance;
        for (int i = 0; i < count; i++)
        {
            Collider collider = collisionHits[i].collider;
            if (collider == null || collider.transform == speaker.transform ||
                collider.transform.IsChildOf(speaker.transform) || collider.transform == localLook.transform ||
                collider.transform.IsChildOf(localLook.transform)) continue;
            clear = Mathf.Min(clear, Mathf.Max(0f, collisionHits[i].distance - 0.18f));
        }
        // Pulling substantially closer enlarges the speaker beyond the reserved
        // screen region. Keep composition and fall back instead of clipping the
        // character behind the right-hand card. Only tiny probe adjustments fit
        // inside the framing margin. Never move the NPC or hide an obstruction.
        if (clear < distance * 0.98f) return false;
        shotPosition = focus + delta.normalized * clear;
        return true;
    }

    /// <summary>
    /// A modest closer composition: retain the top 84% of the visible NPC,
    /// including the exact top of the head and the existing width/depth.
    /// </summary>
    internal static Bounds SpeakerFocusBounds(Bounds fullBounds)
    {
        const float retainedHeight = 0.84f;
        Vector3 size = fullBounds.size;
        Vector3 center = fullBounds.center;
        center.y += size.y * ((1f - retainedHeight) * 0.5f);
        size.y *= retainedHeight;
        return new Bounds(center, size);
    }

    /// <summary>Pure composition math, also exercised in detached Editor checks.</summary>
    internal static bool TryFramingPose(Bounds bounds, Vector3 speakerForward, float aspect, float fieldOfView,
        Rect cameraScreenRect, Rect npcScreenRect, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero; rotation = Quaternion.identity;
        if (bounds.size.y <= 0.01f || aspect <= 0f || cameraScreenRect.width <= 0f || cameraScreenRect.height <= 0f ||
            npcScreenRect.width <= 0f || npcScreenRect.height <= 0f || fieldOfView <= 0f || fieldOfView >= 179f) return false;
        float minX = Mathf.Max(cameraScreenRect.xMin, npcScreenRect.xMin);
        float maxX = Mathf.Min(cameraScreenRect.xMax, npcScreenRect.xMax);
        float minY = Mathf.Max(cameraScreenRect.yMin, npcScreenRect.yMin);
        float maxY = Mathf.Min(cameraScreenRect.yMax, npcScreenRect.yMax);
        if (maxX - minX < 0.06f || maxY - minY < 0.1f) return false;
        Vector2 centre = new Vector2(((minX + maxX) * 0.5f - cameraScreenRect.x) / cameraScreenRect.width,
            ((minY + maxY) * 0.5f - cameraScreenRect.y) / cameraScreenRect.height);
        float height = (maxY - minY) / cameraScreenRect.height;
        float width = (maxX - minX) / cameraScreenRect.width;
        Vector3 front = Vector3.ProjectOnPlane(speakerForward, Vector3.up);
        if (front.sqrMagnitude <= 0.0001f) front = Vector3.forward;
        front.Normalize();
        // A slight three-quarter view keeps the player's usual standing position
        // out of the central speaker sightline without rotating the authored NPC.
        front = Quaternion.AngleAxis(18f, Vector3.up) * front;
        rotation = Quaternion.LookRotation(-front, Vector3.up);
        Vector3 right = rotation * Vector3.right;
        float tangent = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 focus = bounds.center + Vector3.up * (bounds.size.y * 0.03f);
        // Fit the eight actual perspective corners, not just projected width /
        // height at the centre depth. Off-axis composition means a nearer corner
        // shifts away from the NPC slot's centre as well as becoming larger.
        // Solve each frustum-plane inequality for distance with a 9% inset.
        float left = (minX - cameraScreenRect.x) / cameraScreenRect.width + width * 0.09f;
        float rightEdge = (maxX - cameraScreenRect.x) / cameraScreenRect.width - width * 0.09f;
        float bottom = (minY - cameraScreenRect.y) / cameraScreenRect.height + height * 0.09f;
        float top = (maxY - cameraScreenRect.y) / cameraScreenRect.height - height * 0.09f;
        float horizontalTan = tangent * aspect;
        float distance = 0.8f;
        Vector3 forward = rotation * Vector3.forward;
        Vector3 extents = bounds.extents;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 offset = bounds.center + new Vector3(x * extents.x, y * extents.y, z * extents.z) - focus;
            float horizontal = Vector3.Dot(offset, right);
            float vertical = Vector3.Dot(offset, Vector3.up);
            float depth = Vector3.Dot(offset, forward);
            distance = Mathf.Max(distance, 0.3f - depth);
            distance = Mathf.Max(distance, ((2f * left - 1f) * horizontalTan * depth - horizontal) /
                (2f * (centre.x - left) * horizontalTan));
            distance = Mathf.Max(distance, (horizontal - (2f * rightEdge - 1f) * horizontalTan * depth) /
                (2f * (rightEdge - centre.x) * horizontalTan));
            distance = Mathf.Max(distance, ((2f * bottom - 1f) * tangent * depth - vertical) /
                (2f * (centre.y - bottom) * tangent));
            distance = Mathf.Max(distance, (vertical - (2f * top - 1f) * tangent * depth) /
                (2f * (top - centre.y) * tangent));
        }
        position = focus + front * distance - right * ((centre.x - 0.5f) * 2f * distance * tangent * aspect) -
            Vector3.up * ((centre.y - 0.5f) * 2f * distance * tangent);
        return IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private bool TryReadBounds(NPCProgressionManager candidate, out Bounds bounds)
    {
        bounds = default;
        renderers.Clear();
        candidate.GetComponentsInChildren(false, renderers);
        bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                !renderer.gameObject.activeInHierarchy || renderer.GetComponent<TMPro.TMP_Text>() != null) continue;
            if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
            if (renderer.bounds.size.sqrMagnitude < 0.0001f) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found && bounds.size.y > 0.01f;
    }

    private static string ResolveSpeakerName(NPCProgressionManager candidate)
    {
        NPCProgressionPhase phase = candidate.CurrentPhase;
        if (phase != null && phase.contract != null && !string.IsNullOrWhiteSpace(phase.contract.clientName))
            return phase.contract.clientName;
        if (phase != null && phase.phaseDialogue != null && !string.IsNullOrWhiteSpace(phase.phaseDialogue.name))
            return phase.phaseDialogue.name;
        return candidate.name.Replace("(Clone)", string.Empty).Trim();
    }

    private void CaptureAnimation(NPCProgressionManager candidate)
    {
        speakerAnimator = candidate.GetComponentInChildren<Animator>();
        animationBefore.Clear();
        if (speakerAnimator == null || speakerAnimator.runtimeAnimatorController == null) return;
        CaptureBool(talkingBoolParameter);
        CaptureBool(walkingBoolParameter);
        CaptureBool(runningBoolParameter);
    }

    private void CaptureBool(string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter) || speakerAnimator == null) return;
        foreach (AnimatorControllerParameter value in speakerAnimator.parameters)
            if (value.type == AnimatorControllerParameterType.Bool && string.Equals(value.name, parameter, StringComparison.Ordinal))
            {
                animationBefore.Add(new BoolSnapshot { name = parameter, value = speakerAnimator.GetBool(parameter) });
                return;
            }
    }

    private void ApplyAnimation()
    {
        if (speakerAnimator == null || !speakerAnimator.isActiveAndEnabled || speakerAnimator.runtimeAnimatorController == null) return;
        bool talking = speakingAllowed && Time.unscaledTime < speakingUntil &&
            (PauseManager.Instance == null || !PauseManager.Instance.isPaused);
        foreach (BoolSnapshot parameter in animationBefore)
            speakerAnimator.SetBool(parameter.name, parameter.name == talkingBoolParameter && talking);
    }

    /// <summary>Call before restoring gameplay input or invoking NPC continuation.</summary>
    public void End(bool immediate = true) => EndInternal(CanRestoreShot());

    private void EndInternal(bool restorePose)
    {
        avatarVisibility.End();
        SceneManager.sceneUnloaded -= SceneUnloaded;
        if (!active) { ClearReferences(); return; }
        active = false;
        if (activePresentation == this) activePresentation = null;
        try
        {
            if (borrowedCamera != null)
            {
                // Only restore our shot when no later controller took ownership.
                // A foreign shot may also have changed projection since our
                // final write: preserve those values, but remove our remaining
                // projection override if it has not been replaced.
                if (restorePose)
                {
                    if (cameraParent != null)
                    {
                        borrowedCamera.transform.localPosition = cameraLocalPosition;
                        borrowedCamera.transform.localRotation = cameraLocalRotation;
                    }
                    else borrowedCamera.transform.SetPositionAndRotation(cameraWorldPosition, cameraWorldRotation);
                }
                if (restorePose || Mathf.Approximately(borrowedCamera.fieldOfView, fieldOfViewApplied))
                    borrowedCamera.fieldOfView = fieldOfViewBefore;
                if (restorePose || Mathf.Approximately(borrowedCamera.nearClipPlane, nearClipApplied))
                    borrowedCamera.nearClipPlane = nearClipBefore;
            }
            if (speakerAnimator != null && speakerAnimator.runtimeAnimatorController != null)
                foreach (BoolSnapshot parameter in animationBefore)
                    speakerAnimator.SetBool(parameter.name, parameter.value);
            if (localLook != null) localLook.enabled = lookEnabledBefore;
        }
        finally
        {
            CanyonCameraOcclusionPolicy.SetSuspended(borrowedCamera, this, false);
            ClearReferences();
        }
    }

    private void ClearReferences()
    {
        speaker = null; speakerPhase = null; sessionOwner = null;
        borrowedCamera = null; localLook = null; speakerAnimator = null; cameraParent = null;
        SpeakerName = string.Empty;
        animationBefore.Clear(); renderers.Clear(); sceneCinematics.Clear();
    }

    private void SceneUnloaded(Scene scene)
    {
        if (active && (speaker == null || speaker.gameObject.scene == scene ||
            borrowedCamera == null || borrowedCamera.gameObject.scene == scene)) EndInternal(false);
    }
    private void OnDisable() => End(true);
    private void OnDestroy() => End(true);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        if (activePresentation != null)
        {
            // Other subsystem reset methods may already have cleared singleton
            // managers. Do not let their callback order strand a surviving
            // scene-reload-disabled camera at the previous session's NPC shot.
            QuizNPCPresentation previous = activePresentation;
            bool ownsPose = previous.borrowedCamera != null &&
                previous.borrowedCamera.transform.parent == previous.cameraParent;
            previous.EndInternal(ownsPose);
        }
        activePresentation = null;
    }
}
