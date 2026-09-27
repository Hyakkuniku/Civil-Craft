using UnityEngine;

[DisallowMultipleComponent]
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

    public void BeginRunning(RuntimeAnimatorController fallbackController)
    {
        previewAnimator = GetComponentInChildren<Animator>(true);
        if (previewAnimator == null)
        {
            Debug.LogWarning("[LoadingScreen] The loading player has no Animator.", this);
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
            return;
        }

        previewAnimator.Play(runningStateHash, 0, 0f);
        previewAnimator.Update(0f);
        lastNormalizedTime = previewAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        stalledSeconds = 0f;
        recoveryStallSeconds = 0f;
        stalledFrames = 0;
        manualRecovery = false;
        runningConfigured = true;
    }

    private void LateUpdate()
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
