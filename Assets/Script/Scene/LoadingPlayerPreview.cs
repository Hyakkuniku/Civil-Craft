using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class LoadingPlayerPreview : MonoBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int SprintingHash = Animator.StringToHash("IsSprinting");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int SprintStateHash = Animator.StringToHash("Sprint");
    private static readonly int WalkStateHash = Animator.StringToHash("walk");

    private Animator previewAnimator;
    private int runningStateHash;
    private float lastNormalizedTime;
    private float stalledSeconds;
    private float recoveryStallSeconds;
    private int stalledFrames;
    private bool runningConfigured;
    private bool manualRecovery;
    private Renderer[] previewRenderers;
    private Bounds previewLocalBounds;
    private bool hasPreviewBounds;
    private Camera previewCamera;

    public void BindPreviewCamera(Camera camera)
    {
        previewCamera = camera;
    }

    private void OnEnable()
    {
        Camera.onPreCull += PrepareForCamera;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void OnDisable()
    {
        Camera.onPreCull -= PrepareForCamera;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        PrepareForCamera(camera);
    }

    internal void PrepareForCamera(Camera camera)
    {
        if (camera == null || camera != previewCamera || !isActiveAndEnabled) return;
        // Skinning and other camera callbacks run after LateUpdate in a player
        // build. Repair this preview only, immediately before its camera culls.
        RefreshVisibility();
    }

    public void BeginRunning(RuntimeAnimatorController fallbackController)
    {
        runningConfigured = false;
        // Capture only the outfit selected by the cosmetic bindings. Restoring
        // visibility must never activate every unselected shirt, hat or shoe.
        var selected = new List<Renderer>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            if (renderer != null && renderer.gameObject.activeInHierarchy)
                selected.Add(renderer);
        previewRenderers = selected.ToArray();
        hasPreviewBounds = false;
        RefreshVisibility();

        previewAnimator = GetComponentInChildren<Animator>(true);
        if (previewAnimator == null)
        {
            Debug.LogWarning("[LoadingScreen] The loading player has no Animator.", this);
            CapturePreviewBounds(false);
            return;
        }

        if (previewAnimator.runtimeAnimatorController == null)
            previewAnimator.runtimeAnimatorController = fallbackController;

        previewAnimator.applyRootMotion = false;
        previewAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // A cloned in-scene visual can carry an Animator speed of zero from a
        // paused/cinematic state; the preview must not inherit that state.
        previewAnimator.speed = 1f;
        previewAnimator.enabled = true;

        SetFloatIfPresent(previewAnimator, SpeedHash, 1f);
        SetBoolIfPresent(previewAnimator, GroundedHash, true);
        SetBoolIfPresent(previewAnimator, SprintingHash, true);
        ResetTriggerIfPresent(previewAnimator, JumpHash);

        // Start in the run cycle immediately so there is no idle pose during
        // the loading-screen fade-in.
        runningStateHash = previewAnimator.HasState(0, SprintStateHash)
            ? SprintStateHash
            : previewAnimator.HasState(0, WalkStateHash) ? WalkStateHash : 0;
        if (runningStateHash == 0)
        {
            Debug.LogWarning("[LoadingScreen] The loading player has no running state.", this);
            CapturePreviewBounds(false);
            return;
        }

        previewAnimator.Play(runningStateHash, 0, 0f);
        previewAnimator.Update(0f);
        CapturePreviewBounds(true);
        lastNormalizedTime = previewAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        stalledSeconds = 0f;
        recoveryStallSeconds = 0f;
        stalledFrames = 0;
        manualRecovery = false;
        runningConfigured = true;
    }

    private void LateUpdate()
    {
        UpdateRunningAnimation();
        // Scene activation and animation can replace the skinning bounds. Keep
        // the temporary preview's culling envelope valid after the pose updates.
        RefreshVisibility();
    }

    private void UpdateRunningAnimation()
    {
        if (!runningConfigured || previewAnimator == null || !gameObject.activeInHierarchy)
            return;

        // The preview is not the destination player. Keep its Animator immune
        // to paused game time and to state carried over from a cloned visual.
        if (!previewAnimator.enabled) previewAnimator.enabled = true;
        if (previewAnimator.updateMode != AnimatorUpdateMode.UnscaledTime)
            previewAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        if (previewAnimator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (previewAnimator.speed <= 0f) previewAnimator.speed = 1f;

        AnimatorStateInfo state = previewAnimator.GetCurrentAnimatorStateInfo(0);
        if ((state.shortNameHash != runningStateHash && !previewAnimator.IsInTransition(0)) ||
            (!state.loop && state.normalizedTime >= 1f))
        {
            previewAnimator.Play(runningStateHash, 0, 0f);
            previewAnimator.Update(0f);
            lastNormalizedTime = previewAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            stalledSeconds = 0f;
            recoveryStallSeconds = 0f;
            stalledFrames = 0;
            manualRecovery = false;
            return;
        }

        float frameTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (frameTime <= 0f) return;

        if (Mathf.Abs(state.normalizedTime - lastNormalizedTime) > 0.0001f)
        {
            stalledSeconds = 0f;
            recoveryStallSeconds = 0f;
            stalledFrames = 0;
            manualRecovery = false;
        }
        else
        {
            if (!manualRecovery)
            {
                stalledSeconds += frameTime;
                stalledFrames++;
                manualRecovery = stalledFrames >= 3 && stalledSeconds >= 0.15f;
            }

            if (manualRecovery)
            {
                previewAnimator.Update(frameTime);
                float recoveredTime = previewAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                recoveryStallSeconds = Mathf.Abs(recoveredTime - state.normalizedTime) > 0.0001f
                    ? 0f
                    : recoveryStallSeconds + frameTime;
                if (recoveryStallSeconds >= 0.75f)
                {
                    previewAnimator.Play(runningStateHash, 0, 0f);
                    previewAnimator.Update(0f);
                    recoveryStallSeconds = 0f;
                }
            }
        }

        lastNormalizedTime = previewAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
    }

    internal bool TryGetPreviewBounds(out Bounds bounds)
    {
        bounds = hasPreviewBounds
            ? TransformBounds(transform.localToWorldMatrix, previewLocalBounds)
            : default;
        return hasPreviewBounds;
    }

    internal void RefreshVisibility()
    {
        if (previewRenderers == null) return;
        bool hasBounds = TryGetPreviewBounds(out Bounds cullingBounds);
        if (hasBounds)
        {
            // This common envelope deliberately includes the whole running
            // outfit, instead of trusting each garment's imported bind-pose box.
            cullingBounds.Expand(Mathf.Max(0.1f, cullingBounds.size.magnitude * 0.15f));
        }
        foreach (Renderer renderer in previewRenderers)
        {
            if (renderer == null) continue;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.allowOcclusionWhenDynamic = false;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                // Automatic offscreen bounds are rebuilt by native skinning and
                // can discard the common outfit envelope on mobile. The Animator
                // always runs; our visible, stable bounds keep skinning active.
                skinned.updateWhenOffscreen = false;
                skinned.forceMatrixRecalculationPerRender = true;
                // Android's performance preset limits Auto to two influences.
                // Only this small preview uses four; gameplay quality is untouched.
                skinned.quality = SkinQuality.Bone4;
                // A world-space override avoids the different root-bone/mesh
                // coordinate systems of modular cosmetics. Update it every frame
                // so it also follows any movement of this preview's stage.
                if (hasBounds)
                {
                    Transform boundsRoot = skinned.rootBone != null ? skinned.rootBone : skinned.transform;
                    skinned.localBounds = TransformBounds(boundsRoot.worldToLocalMatrix, cullingBounds);
                    skinned.bounds = cullingBounds;
                }
            }
        }
    }

    private void CapturePreviewBounds(bool sampleRunningCycle)
    {
        // CPU skinning is only used during setup, never on every loading frame.
        // Measure actual deformed geometry before assigning oversized culling
        // boxes, otherwise camera framing would shrink the character needlessly.
        var scratch = new Mesh { name = "Loading preview bounds (temporary)" };
        try
        {
            int samples = sampleRunningCycle ? 8 : 1;
            for (int pose = 0; pose < samples; pose++)
            {
                if (sampleRunningCycle)
                {
                    previewAnimator.Play(runningStateHash, 0, (float)pose / samples);
                    previewAnimator.Update(0f);
                }
                foreach (Renderer renderer in previewRenderers)
                {
                    if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
                    Bounds local;
                    if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                    {
                        // Include the renderer's scale in the bake conversion so
                        // the resulting vertices can be transformed normally.
                        // The imported garments use scales from 1 to ~1000;
                        // baking without scale and applying that transform again
                        // would multiply their measured size a second time.
                        skinned.BakeMesh(scratch, true);
                        scratch.RecalculateBounds();
                        if (scratch.vertexCount == 0) continue;
                        local = TransformBounds(transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                            scratch.bounds);
                    }
                    else
                    {
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        local = filter != null && filter.sharedMesh != null
                            ? TransformBounds(transform.worldToLocalMatrix * renderer.localToWorldMatrix,
                                filter.sharedMesh.bounds)
                            : TransformBounds(transform.worldToLocalMatrix, renderer.bounds);
                    }
                    if (!IsFinite(local.center) || !IsFinite(local.extents)) continue;
                    if (!hasPreviewBounds)
                    {
                        previewLocalBounds = local;
                        hasPreviewBounds = true;
                    }
                    else previewLocalBounds.Encapsulate(local);
                }
            }
            // Allow for poses between the samples while keeping camera framing
            // based on the actual character rather than imported bounds.
            if (hasPreviewBounds) previewLocalBounds.Expand(previewLocalBounds.size * 0.08f);
        }
        finally
        {
            if (sampleRunningCycle)
            {
                previewAnimator.Play(runningStateHash, 0, 0f);
                previewAnimator.Update(0f);
            }
            if (Application.isPlaying) Destroy(scratch);
            else DestroyImmediate(scratch);
        }
        RefreshVisibility();
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
    {
        Vector3 x = matrix.MultiplyVector(new Vector3(local.extents.x, 0f, 0f));
        Vector3 y = matrix.MultiplyVector(new Vector3(0f, local.extents.y, 0f));
        Vector3 z = matrix.MultiplyVector(new Vector3(0f, 0f, local.extents.z));
        return new Bounds(matrix.MultiplyPoint3x4(local.center), new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f);
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static void SetFloatIfPresent(Animator animator, int parameterHash, float value)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == parameterHash && parameter.type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(parameterHash, value);
                return;
            }
        }
    }

    private static void SetBoolIfPresent(Animator animator, int parameterHash, bool value)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == parameterHash && parameter.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(parameterHash, value);
                return;
            }
        }
    }

    private static void ResetTriggerIfPresent(Animator animator, int parameterHash)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == parameterHash &&
                parameter.type == AnimatorControllerParameterType.Trigger)
            {
                animator.ResetTrigger(parameterHash);
                return;
            }
        }
    }
}
