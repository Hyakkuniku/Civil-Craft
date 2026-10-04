using System;
using Fusion;
using UnityEngine;

public enum ChallengeReaction : byte { None, Winning, Crying }

/// <summary>Cosmetic results only; cancellation/errors never count as a defeat.</summary>
public static class ChallengeReactionPolicy
{
    public static ChallengeReaction ForParticipant(MultiplayerChallengeState state, MultiplayerChallengeResult result,
        PlayerRef participant, PlayerRef host)
    {
        if (state.Revision <= 0 || state.Phase != MultiplayerChallengePhase.TestResults || result != MultiplayerChallengeResult.None ||
            participant != host && participant != state.Guest) return ChallengeReaction.None;
        if (state.Winner == ChallengeWinner.NoWinner) return ChallengeReaction.Crying;
        if (state.Winner != ChallengeWinner.Host && state.Winner != ChallengeWinner.Guest) return ChallengeReaction.None;
        bool won = state.Winner == ChallengeWinner.Host ? participant == host : participant == state.Guest;
        return won ? ChallengeReaction.Winning : ChallengeReaction.Crying;
    }
}

/// <summary>One existing authored clip, with a bounded exit back to locomotion.</summary>
public sealed class ChallengeReactionPlayback
{
    public const float MaximumSeconds = 12f;
    private const float FadeSeconds = 0.15f;
    private static readonly int IdleState = Animator.StringToHash("Base Layer.idle");
    private Animator animator;
    private int stateHash;
    private float startedAt, duration;
    private bool originalRootMotion;
    public bool IsPlaying => animator != null;

    public static int StateHash(Animator target, ChallengeReaction reaction)
    {
        if (target == null || reaction == ChallengeReaction.None) return 0;
        string name = reaction == ChallengeReaction.Winning ? "Winning" : "crying";
        int hash = Animator.StringToHash("Base Layer." + name);
        if (target.HasState(0, hash)) return hash;
        hash = Animator.StringToHash("Base Layer." + (reaction == ChallengeReaction.Winning ? "winning" : "Crying"));
        return target.HasState(0, hash) ? hash : 0;
    }

    public static float Duration(Animator target, ChallengeReaction reaction)
    {
        if (target != null && target.runtimeAnimatorController != null)
            foreach (AnimationClip clip in target.runtimeAnimatorController.animationClips)
                if (clip != null && string.Equals(clip.name, reaction == ChallengeReaction.Winning ? "Winning" : "crying", StringComparison.OrdinalIgnoreCase))
                    return Mathf.Clamp(clip.length, 0.25f, MaximumSeconds);
        return 4f;
    }

    public bool Start(Animator target, ChallengeReaction reaction, float now, float seconds, float elapsed = 0f)
    {
        int hash = StateHash(target, reaction);
        if (hash == 0 || !target.isActiveAndEnabled || elapsed >= seconds) return false;
        Stop();
        animator = target; stateHash = hash; duration = Mathf.Clamp(seconds, 0.25f, MaximumSeconds);
        startedAt = now - Mathf.Max(0f, elapsed); originalRootMotion = target.applyRootMotion;
        // Imported celebration/crying root curves must not move the saved-world
        // CharacterController or fight its network pose replication.
        target.applyRootMotion = false;
        target.SetBool("Wave", false); target.ResetTrigger("Jump");
        target.CrossFadeInFixedTime(hash, FadeSeconds, 0, Mathf.Max(0f, elapsed));
        return true;
    }

    public bool Update(float now, bool interrupted)
    {
        if (!IsPlaying) return false;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (interrupted || !animator.isActiveAndEnabled || now - startedAt >= duration + FadeSeconds ||
            state.fullPathHash == stateHash && state.normalizedTime >= 1f)
        { Stop(); return false; }
        return true;
    }

    public void Stop()
    {
        if (animator != null)
        {
            bool stillInReaction = animator.GetCurrentAnimatorStateInfo(0).fullPathHash == stateHash ||
                animator.GetNextAnimatorStateInfo(0).fullPathHash == stateHash;
            animator.applyRootMotion = originalRootMotion;
            if (animator.isActiveAndEnabled && stillInReaction && animator.HasState(0, IdleState))
                animator.CrossFadeInFixedTime(IdleState, FadeSeconds, 0);
        }
        animator = null; stateHash = 0;
    }
}

public sealed partial class FusionMultiplayerAvatar
{
    [Networked] private int ResultReactionRevision { get; set; }
    [Networked] private ChallengeReaction ResultReaction { get; set; }
    [Networked] private int StartedReactionRevision { get; set; }
    [Networked] private bool ResultReactionPlaying { get; set; }
    [Networked] private float ResultReactionSeconds { get; set; }
    [Networked] private TickTimer ResultReactionDeadline { get; set; }
    private int returnedWorldRevision, localPlayedReactionRevision, localPlayingReactionRevision, remotePlayedReactionRevision;
    private readonly ChallengeReactionPlayback localReaction = new ChallengeReactionPlayback();
    private readonly ChallengeReactionPlayback remoteReaction = new ChallengeReactionPlayback();

    private void QueueCompletedChallengeReactions(MultiplayerChallengeState completed, MultiplayerChallengeResult result)
    {
        if (!HasStateAuthority || !IsSessionHost || completed.Phase != MultiplayerChallengePhase.TestResults || result != MultiplayerChallengeResult.None) return;
        foreach (PlayerRef player in new[] { Object.InputAuthority, completed.Guest })
            if (Runner.TryGetPlayerObject(player, out NetworkObject obj) && obj != null && obj.IsValid)
            {
                var avatar = obj.GetComponent<FusionMultiplayerAvatar>();
                if (avatar == null || !avatar.HasStateAuthority) continue;
                avatar.ResultReactionRevision = completed.Revision;
                avatar.ResultReaction = ChallengeReactionPolicy.ForParticipant(completed, result, player, Object.InputAuthority);
                avatar.ResultReactionPlaying = false; avatar.ResultReactionDeadline = default;
            }
    }

    // The UI calls this for both visible avatars only AFTER restoring world pose,
    // camera, panels and input. A network result alone never starts an animation.
    public void MarkChallengeReturnedToWorld(int revision) { returnedWorldRevision = revision; }

    private bool ReactionWorldReady
    {
        get
        {
            FusionConnectionManager connection = FusionConnectionManager.Instance;
            GameManager game = GameManager.Instance;
            return connection != null && connection.Runner == Runner && connection.IsHostWorldSession && !connection.IsNetworkSceneLoading &&
                game != null && game.CurrentState == GameManager.GameState.Normal && !game.IsTransitioning &&
                (UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel);
        }
    }

    private void UpdateLocalChallengeReaction()
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority) return;
        bool poseReady = ReactionWorldReady && sceneMotor != null && sceneMotor.isActiveAndEnabled &&
            sceneAnimator != null && sceneAnimator.isActiveAndEnabled;
        InputManager input = sceneMotor != null ? sceneMotor.GetComponent<InputManager>() : null;
        poseReady &= input == null || input.IsPlayerInputEnabled;
        bool moving = sceneAnimator != null && (sceneAnimator.GetFloat(SpeedParameter) >= 0.1f || !sceneAnimator.GetBool(GroundedParameter));
        bool carrying = sceneMotor != null && CargoItem.IsCarriedBy(sceneMotor.transform);
        FusionMultiplayerAvatar host = FindHost(Runner);
        bool challengeClosed = host != null && !host.IsChallengeBusy;
        if (localReaction.IsPlaying)
        {
            if (!localReaction.Update(Time.unscaledTime, !poseReady || moving || carrying || !challengeClosed)) StopLocalChallengeReaction();
            return;
        }
        if (!poseReady || moving || carrying || !challengeClosed || ResultReaction == ChallengeReaction.None ||
            ResultReactionRevision <= 0 || ResultReactionRevision != returnedWorldRevision || ResultReactionRevision == localPlayedReactionRevision) return;
        localPlayedReactionRevision = ResultReactionRevision;
        float seconds = ChallengeReactionPlayback.Duration(sceneAnimator, ResultReaction);
        if (localWaveActive) SetLocalWave(false);
        if (!localReaction.Start(sceneAnimator, ResultReaction, Time.unscaledTime, seconds))
        { Debug.LogWarning("[Challenge reaction] The player's authored Winning/crying state is unavailable.", this); return; }
        localPlayingReactionRevision = ResultReactionRevision;
        if (HasStateAuthority) AcceptChallengeReaction(localPlayingReactionRevision, true, seconds);
        else RPC_ChallengeReaction(localPlayingReactionRevision, true, seconds);
    }

    private void StopLocalChallengeReaction()
    {
        localReaction.Stop();
        int revision = localPlayingReactionRevision; localPlayingReactionRevision = 0;
        if (revision <= 0 || Runner == null || !Runner.IsRunning || Object == null || !Object.IsValid || !HasInputAuthority) return;
        if (HasStateAuthority) AcceptChallengeReaction(revision, false, 0f);
        else RPC_ChallengeReaction(revision, false, 0f);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_ChallengeReaction(int revision, bool playing, float seconds) => AcceptChallengeReaction(revision, playing, seconds);

    private void AcceptChallengeReaction(int revision, bool playing, float seconds)
    {
        if (!HasStateAuthority || revision != ResultReactionRevision || ResultReaction == ChallengeReaction.None) return;
        if (!playing) { ResultReactionPlaying = false; ResultReactionDeadline = default; return; }
        FusionMultiplayerAvatar host = FindHost(Runner);
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (StartedReactionRevision == revision || host == null || host.IsChallengeBusy || host.ChallengeState.Revision != revision ||
            connection == null || connection.Runner != Runner || !connection.IsHostWorldSession || connection.IsNetworkSceneLoading ||
            float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0.25f || seconds > ChallengeReactionPlayback.MaximumSeconds) return;
        StartedReactionRevision = revision; ResultReactionPlaying = true; ResultReactionSeconds = seconds;
        ResultReactionDeadline = TickTimer.CreateFromSeconds(Runner, seconds + 0.15f);
        IsWaving = false;
    }

    private void UpdateChallengeReactionAuthority()
    {
        if (!HasStateAuthority || !ResultReactionPlaying) return;
        FusionMultiplayerAvatar host = FindHost(Runner);
        if (ResultReactionDeadline.ExpiredOrNotRunning(Runner) || host == null || host.IsChallengeBusy)
        { ResultReactionPlaying = false; ResultReactionDeadline = default; }
    }

    private bool ApplyRemoteChallengeReaction()
    {
        bool ready = ResultReactionPlaying && ResultReactionRevision == returnedWorldRevision && ReactionWorldReady &&
            MoveSpeed < 0.1f && Grounded && ResultReactionDeadline.IsRunning && !ResultReactionDeadline.Expired(Runner);
        if (!ready) { remoteReaction.Stop(); return false; }
        if (remotePlayedReactionRevision != ResultReactionRevision && remoteAnimator != null)
        {
            float elapsed = Mathf.Max(0f, ResultReactionSeconds + 0.15f - (ResultReactionDeadline.RemainingTime(Runner) ?? 0f));
            if (remoteReaction.Start(remoteAnimator, ResultReaction, Time.unscaledTime, ResultReactionSeconds, elapsed))
                remotePlayedReactionRevision = ResultReactionRevision;
        }
        return remoteReaction.Update(Time.unscaledTime, false);
    }
}
