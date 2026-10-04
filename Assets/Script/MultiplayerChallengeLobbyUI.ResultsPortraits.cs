using Fusion;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class MultiplayerChallengeLobbyUI
{
    [Header("Authored result character portraits")]
    [SerializeField] private RawImage resultsHostPortrait;
    [SerializeField] private RawImage resultsGuestPortrait;
    private readonly ChallengeResultPortraitReaction hostResultReaction = new ChallengeResultPortraitReaction();
    private readonly ChallengeResultPortraitReaction guestResultReaction = new ChallengeResultPortraitReaction();

    private void RefreshResultPortraits(MultiplayerChallengeState state)
    {
        if (host == null || resultsPanel == null || !resultsPanel.activeInHierarchy ||
            resultsHostPortrait == null || resultsGuestPortrait == null) return;
        // Two authored RawImages each sample one half of the same transparent
        // lobby stage. No gameplay player, camera or network outcome is modified.
        RefreshPortraits(GetAvatar(state.Guest), resultsHostPortrait, true);
        PlayerRef hostPlayer = host.Object.InputAuthority;
        hostResultReaction.Tick(hostPortrait, ChallengeReactionPolicy.ForParticipant(state, MultiplayerChallengeResult.None,
            hostPlayer, hostPlayer), state.Revision, Time.unscaledTime);
        guestResultReaction.Tick(guestPortrait, ChallengeReactionPolicy.ForParticipant(state, MultiplayerChallengeResult.None,
            state.Guest, hostPlayer), state.Revision, Time.unscaledTime);
    }

    private void ResetResultPortraitReactions()
    {
        hostResultReaction.Reset();
        guestResultReaction.Reset();
    }
}

/// <summary>Repeats the authored result clip on a visual-only portrait, not a world avatar.</summary>
public sealed class ChallengeResultPortraitReaction
{
    private readonly ChallengeReactionPlayback playback = new ChallengeReactionPlayback();
    private GameObject portrait;
    private Animator animator;
    private ChallengeReaction reaction;
    private int revision = int.MinValue;
    private float nextStart;
    public bool IsPlaying => playback.IsPlaying;

    public void Tick(GameObject target, ChallengeReaction outcome, int resultRevision, float now)
    {
        if (target != portrait || reaction != outcome || revision != resultRevision)
        {
            Reset();
            portrait = target; reaction = outcome; revision = resultRevision;
            animator = portrait != null ? portrait.GetComponentInChildren<Animator>(true) : null;
            nextStart = now;
        }
        if (animator == null || !animator.isActiveAndEnabled || outcome == ChallengeReaction.None)
        { playback.Stop(); return; }
        if (playback.IsPlaying)
        {
            if (!playback.Update(now, false)) nextStart = now + 0.85f;
            return;
        }
        if (now < nextStart) return;
        // Use unscaled timing: results modals may pause the gameplay clock.
        if (!playback.Start(animator, outcome, now, ChallengeReactionPlayback.Duration(animator, outcome)))
            nextStart = float.PositiveInfinity;
    }

    public void Reset()
    {
        playback.Stop();
        portrait = null; animator = null; reaction = ChallengeReaction.None;
        revision = int.MinValue; nextStart = 0;
    }
}
