using UnityEngine;

[DisallowMultipleComponent]
public sealed class LoadingPlayerPreview : MonoBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int SprintingHash = Animator.StringToHash("IsSprinting");

    private Animator previewAnimator;

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
        previewAnimator.enabled = true;

        SetFloatIfPresent(previewAnimator, SpeedHash, 1f);
        SetBoolIfPresent(previewAnimator, GroundedHash, true);
        SetBoolIfPresent(previewAnimator, SprintingHash, true);

        // Start in the run cycle immediately so there is no idle pose during
        // the loading-screen fade-in.
        int sprintState = Animator.StringToHash("Sprint");
        if (previewAnimator.HasState(0, sprintState))
            previewAnimator.Play(sprintState, 0, 0f);
        else
            previewAnimator.Play("walk", 0, 0f);
        previewAnimator.Update(0f);
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
}
