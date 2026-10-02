using System.Collections.Generic;
using Fusion;
using UnityEngine;

public sealed partial class FusionMultiplayerAvatar
{
    [Networked] public bool IsSessionHost { get; private set; }
    [Networked] public MultiplayerChallengeState ChallengeState { get; private set; }
    [SerializeField, Min(5f)] private float challengeInvitationSeconds = 30f;
    [SerializeField, Min(5f)] private float challengeTravelSeconds = 20f;
    [SerializeField, Range(1f, 10f)] private float challengeCountdownSeconds = 3f;
    [SerializeField, Range(5f, 30f)] private float challengeBuildSetupSeconds = 15f;
    private int pendingArrivalRevision;
    private bool pendingArrival;
    private BuildLocation authorityChallengeSite;

    public bool IsChallengeBusy => ChallengeState.Phase != MultiplayerChallengePhase.None;
    public bool IsAvatarPoseReady => PoseReady;

    public static FusionMultiplayerAvatar FindHost(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning) return null;
        foreach (PlayerRef player in runner.ActivePlayers)
            if (runner.TryGetPlayerObject(player, out NetworkObject obj) && obj != null && obj.IsValid)
            {
                var avatar = obj.GetComponent<FusionMultiplayerAvatar>();
                if (avatar != null && avatar.IsSessionHost) return avatar;
            }
        return null;
    }

    public bool CanInvite(BuildLocation site)
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        GameManager game = GameManager.Instance;
        return HasInputAuthority && HasStateAuthority && IsSessionHost && !IsChallengeBusy &&
            connection != null && connection.Runner == Runner && connection.IsHostWorldSession &&
            !connection.IsNetworkSceneLoading && connection.ConnectedPlayerCount == 2 &&
            game != null && game.CurrentState == GameManager.GameState.Normal && !game.IsTransitioning &&
            sceneMotor != null && !CargoItem.IsCarriedBy(sceneMotor.transform) &&
            (TutorialManager.Instance == null || !TutorialManager.Instance.IsTutorialActive) &&
            MultiplayerChallengeRules.IsCompletedHostSite(site) &&
            site.activeContract.ContractID.Length <= 128 &&
            MultiplayerChallengeRules.SiteKey(site).Length <= 256 &&
            MultiplayerChallengeRules.ResolveSite(MultiplayerChallengeRules.SiteKey(site)) == site;
    }

    public bool TryInvite(BuildLocation site)
    {
        if (!CanInvite(site)) return false;
        PlayerRef guest = PlayerRef.None;
        foreach (PlayerRef player in Runner.ActivePlayers)
            if (player != Object.InputAuthority && Runner.TryGetPlayerObject(player, out NetworkObject obj) &&
                obj != null && obj.IsValid && obj.GetComponent<FusionMultiplayerAvatar>() is FusionMultiplayerAvatar avatar &&
                avatar.IsAvatarPoseReady) guest = player;
        if (guest == PlayerRef.None) return false;
        authorityChallengeSite = site;
        foreach (PlayerRef player in Runner.ActivePlayers)
            if (Runner.TryGetPlayerObject(player, out NetworkObject obj) && obj != null)
            {
                var avatar = obj.GetComponent<FusionMultiplayerAvatar>();
                if (avatar != null) avatar.pendingArrival = false;
            }
        ChallengeState = new MultiplayerChallengeState
        {
            Revision = unchecked(ChallengeState.Revision + 1), Phase = MultiplayerChallengePhase.Invited,
            Guest = guest, SiteKey = MultiplayerChallengeRules.SiteKey(site),
            ContractKey = site.activeContract.ContractID,
            Deadline = TickTimer.CreateFromSeconds(Runner, challengeInvitationSeconds)
        };
        return true;
    }

    public void RespondToChallenge(int revision, bool accept)
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority) return;
        RPC_RespondToChallenge(revision, accept);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_RespondToChallenge(int revision, bool accept)
    {
        FusionMultiplayerAvatar host = FindHost(Runner);
        if (host == null || !host.HasStateAuthority ||
            !MultiplayerChallengeRules.CanRespond(host.ChallengeState, Object.InputAuthority, revision)) return;
        if (!accept) { host.EndChallenge(MultiplayerChallengeResult.Declined); return; }
        if (host.ChallengeState.Deadline.ExpiredOrNotRunning(Runner))
        { host.EndChallenge(MultiplayerChallengeResult.TimedOut); return; }
        BuildLocation site = MultiplayerChallengeRules.ResolveSite(host.ChallengeState.SiteKey.ToString());
        if (!MultiplayerChallengeRules.IsCompletedHostSite(site) || GameManager.Instance == null ||
            GameManager.Instance.CurrentState != GameManager.GameState.Normal || GameManager.Instance.IsTransitioning)
        { host.EndChallenge(MultiplayerChallengeResult.Cancelled); return; }
        MultiplayerChallengeState state = host.ChallengeState;
        state.Phase = MultiplayerChallengePhase.Teleporting;
        state.Deadline = TickTimer.CreateFromSeconds(Runner, host.challengeTravelSeconds);
        host.ChallengeState = state;
    }

    public void ReportChallengeArrival(int revision, bool success)
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority) return;
        RPC_ChallengeArrival(revision, success);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_ChallengeArrival(int revision, bool success)
    {
        FusionMultiplayerAvatar host = FindHost(Runner);
        if (host == null || !host.HasStateAuthority || !MultiplayerChallengeRules.CanArrive(
            host.ChallengeState, Object.InputAuthority, host.Object.InputAuthority, revision)) return;
        if (!success) { host.EndChallenge(MultiplayerChallengeResult.TravelFailed); return; }
        // The server checks the arrival against its latest owner pose as well as
        // the client's acknowledgement. It does not accept an arbitrary destination.
        BuildLocation site = MultiplayerChallengeRules.ResolveSite(host.ChallengeState.SiteKey.ToString());
        if (site == null) { host.EndChallenge(MultiplayerChallengeResult.TravelFailed); return; }
        // A reliable acknowledgement can overtake the unreliable movement pose.
        // Wait for that pose instead of falsely rejecting a successful teleport.
        pendingArrivalRevision = revision;
        pendingArrival = true;
    }

    public void SetChallengeReady(int revision, bool ready)
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority) return;
        RPC_SetChallengeReady(revision, ready);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_SetChallengeReady(int revision, bool ready)
    {
        FusionMultiplayerAvatar host = FindHost(Runner);
        if (host == null || !host.HasStateAuthority) return;
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (connection == null || connection.Runner != Runner || !connection.IsHostWorldSession || connection.IsNetworkSceneLoading ||
            !host.AreChallengeParticipantsAtSite()) return;
        MultiplayerChallengeState state = host.ChallengeState;
        // Readiness cannot undo a countdown which has already finished on the
        // server clock but is still being displayed in a delayed client snapshot.
        if (state.Phase == MultiplayerChallengePhase.Countdown && state.Deadline.ExpiredOrNotRunning(Runner)) return;
        if (!MultiplayerChallengeRules.TrySetReady(ref state, Object.InputAuthority,
            host.Object.InputAuthority, revision, ready)) return;
        if (state.Phase == MultiplayerChallengePhase.Countdown)
            state.Deadline = TickTimer.CreateFromSeconds(Runner, Mathf.Clamp(host.challengeCountdownSeconds, 1f, 10f));
        host.ChallengeState = state;
    }

    public void ReportChallengeBuildPrepared(int revision, bool success)
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority) return;
        RPC_ChallengeBuildPrepared(revision, success);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_ChallengeBuildPrepared(int revision, bool success)
    {
        FusionMultiplayerAvatar host = FindHost(Runner);
        if (host == null || !host.HasStateAuthority || !MultiplayerChallengeRules.CanPrepareBuild(
            host.ChallengeState, Object.InputAuthority, host.Object.InputAuthority, revision)) return;
        if (!success || !host.AreChallengeParticipantsAtSite() || host.ChallengeState.Deadline.ExpiredOrNotRunning(Runner))
        { host.EndChallenge(MultiplayerChallengeResult.BuildSetupFailed); return; }
        MultiplayerChallengeState state = host.ChallengeState;
        if (MultiplayerChallengeRules.TryPrepareBuild(ref state, Object.InputAuthority, host.Object.InputAuthority, revision))
            host.ChallengeState = state;
    }

    public void CancelChallenge(int revision)
    {
        if (Object == null || !Object.IsValid || !HasInputAuthority) return;
        RPC_CancelChallenge(revision);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable)]
    private void RPC_CancelChallenge(int revision)
    {
        FusionMultiplayerAvatar host = FindHost(Runner);
        if (host == null || !host.HasStateAuthority || host.ChallengeState.Revision != revision || !host.IsChallengeBusy) return;
        if (Object.InputAuthority == host.Object.InputAuthority || Object.InputAuthority == host.ChallengeState.Guest)
            host.EndChallenge(host.ChallengeState.Phase == MultiplayerChallengePhase.TestResults ? MultiplayerChallengeResult.None : MultiplayerChallengeResult.Cancelled);
    }

    private void UpdateChallengeAuthority()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !IsSessionHost || !IsChallengeBusy) return;
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        bool guestPresent = false;
        foreach (PlayerRef player in Runner.ActivePlayers) if (player == ChallengeState.Guest) guestPresent = true;
        if (!guestPresent) EndChallenge(MultiplayerChallengeResult.PlayerLeft);
        else if (connection == null || !connection.IsHostWorldSession || connection.IsNetworkSceneLoading)
            EndChallenge(MultiplayerChallengeResult.Cancelled);
        else if ((ChallengeState.Phase == MultiplayerChallengePhase.Invited ||
                  ChallengeState.Phase == MultiplayerChallengePhase.Teleporting) &&
                 ChallengeState.Deadline.ExpiredOrNotRunning(Runner))
            EndChallenge(MultiplayerChallengeResult.TimedOut);
        else if (ChallengeState.Phase == MultiplayerChallengePhase.Teleporting)
        {
            BuildLocation site = authorityChallengeSite;
            if (site == null) { EndChallenge(MultiplayerChallengeResult.TravelFailed); return; }
            MultiplayerChallengeState state = ChallengeState;
            foreach (PlayerRef player in Runner.ActivePlayers)
                if (Runner.TryGetPlayerObject(player, out NetworkObject obj) && obj != null)
                {
                    var avatar = obj.GetComponent<FusionMultiplayerAvatar>();
                    if (avatar == null || !avatar.pendingArrival || avatar.pendingArrivalRevision != state.Revision ||
                        !avatar.PoseReady || !MultiplayerChallengeLobbyUI.IsAtSite(site, avatar.Position)) continue;
                    if (player == Object.InputAuthority) state.HostArrived = true;
                    else if (player == state.Guest) state.GuestArrived = true;
                }
            if (state.HostArrived && state.GuestArrived)
            { state.Phase = MultiplayerChallengePhase.Lobby; state.Deadline = default; }
            if ((bool)state.HostArrived != (bool)ChallengeState.HostArrived || (bool)state.GuestArrived != (bool)ChallengeState.GuestArrived ||
                state.Phase != ChallengeState.Phase) ChallengeState = state;
        }
        else if (ChallengeState.Phase == MultiplayerChallengePhase.Lobby ||
                 ChallengeState.Phase == MultiplayerChallengePhase.Countdown ||
                 ChallengeState.Phase == MultiplayerChallengePhase.ReadyToBuild ||
                 ChallengeState.Phase == MultiplayerChallengePhase.Building ||
                 ChallengeState.Phase == MultiplayerChallengePhase.SubmissionsReady || MultiplayerChallengeRules.IsTestPhase(ChallengeState.Phase))
        {
            if (!AreChallengeParticipantsAtSite())
            { EndChallenge(MultiplayerChallengeResult.Cancelled); return; }
            if (ChallengeState.Phase == MultiplayerChallengePhase.ReadyToBuild &&
                ChallengeState.Deadline.ExpiredOrNotRunning(Runner))
            { EndChallenge(MultiplayerChallengeResult.BuildSetupFailed); return; }
            if (ChallengeState.Phase != MultiplayerChallengePhase.Countdown) return;
            MultiplayerChallengeState state = ChallengeState;
            if (!state.HostReady || !state.GuestReady || !state.Deadline.IsRunning)
            { EndChallenge(MultiplayerChallengeResult.Cancelled); return; }
            if (MultiplayerChallengeRules.TryFinishCountdown(ref state, state.Deadline.Expired(Runner)))
            {
                state.Deadline = TickTimer.CreateFromSeconds(Runner, Mathf.Clamp(challengeBuildSetupSeconds, 5f, 30f));
                ChallengeState = state;
            }
        }
    }

    private bool AreChallengeParticipantsAtSite()
    {
        if (authorityChallengeSite == null || !ChallengeState.HostArrived || !ChallengeState.GuestArrived) return false;
        return IsChallengeParticipantAtSite(Object.InputAuthority) && IsChallengeParticipantAtSite(ChallengeState.Guest);
    }

    // Called only by the host's reliable-data receiver after validating the complete graph.
    public bool TryAcceptValidatedChallengeBridge(PlayerRef player, int revision, float cost, int bars)
    {
        if (Object == null || !Object.IsValid || !IsSessionHost || !HasStateAuthority || Runner == null || !Runner.IsServer ||
            !AreChallengeParticipantsAtSite()) return false;
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (connection == null || connection.Runner != Runner || !connection.IsHostWorldSession || connection.IsNetworkSceneLoading)
            return false;
        MultiplayerChallengeState state = ChallengeState;
        if (!MultiplayerChallengeRules.TryAcceptSubmission(ref state, player, Object.InputAuthority, revision, cost, bars)) return false;
        ChallengeState = state;
        return true;
    }

    private bool IsChallengeParticipantAtSite(PlayerRef player)
    {
        if (!Runner.TryGetPlayerObject(player, out NetworkObject obj) || obj == null || !obj.IsValid) return false;
        var avatar = obj.GetComponent<FusionMultiplayerAvatar>();
        return avatar != null && avatar.PoseReady && MultiplayerChallengeLobbyUI.IsAtSite(authorityChallengeSite, avatar.Position);
    }

    private void EndChallenge(MultiplayerChallengeResult result)
    {
        MultiplayerChallengeState state = ChallengeState;
        state.Phase = MultiplayerChallengePhase.None;
        state.Result = result;
        state.HostArrived = state.GuestArrived = false;
        state.HostReady = state.GuestReady = false;
        state.HostBuilding = state.GuestBuilding = false;
        state.HostSubmitted = state.GuestSubmitted = false;
        state.HostSubmittedCost = state.GuestSubmittedCost = 0f;
        state.HostSubmittedBars = state.GuestSubmittedBars = 0;
        state.HostSubmissionOrder = state.GuestSubmissionOrder = state.TestIndex = 0;
        state.GuestTestViewReady = false;
        state.TestElapsed = state.TestStress = 0f;
        state.HostTestOutcome = state.GuestTestOutcome = ChallengeTestOutcome.None;
        state.HostCrossingSeconds = state.GuestCrossingSeconds = state.HostPeakStress = state.GuestPeakStress = 0f;
        state.Deadline = default;
        ChallengeState = state;
    }

    public string LobbyAppearanceKey => HasInputAuthority && PlayerDataManager.Instance != null
        ? JsonUtility.ToJson(PlayerDataManager.Instance.GetCosmeticLoadoutCopy()) : Appearance;

    /// <summary>A visual-only wardrobe preview. Never clones the gameplay Player or its controller.</summary>
    public GameObject CreateLobbyVisual(Transform parent)
    {
        Transform source = sceneMotor != null ? sceneMotor.transform.Find("NewCharacterModel") : null;
        if (source == null || sceneCosmetics == null) return null;
        GameObject clone = Instantiate(source.gameObject, parent, false);
        clone.SetActive(false);
        clone.name = "Lobby Character (Visual Only)";
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = source.lossyScale;
        var map = new Dictionary<Transform, Transform>();
        MapClonedTransforms(source, clone.transform, map);
        foreach (Transform node in clone.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
        foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        foreach (Camera camera in clone.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (AudioListener listener in clone.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        CosmeticLoadoutData loadout;
        try { loadout = JsonUtility.FromJson<CosmeticLoadoutData>(LobbyAppearanceKey); }
        catch { loadout = null; }
        if (loadout == null) loadout = new CosmeticLoadoutData();
        CosmeticBindingUtility.Apply(CopyBindings(sceneCosmetics.cosmeticBindings, map), loadout);
        foreach (CosmeticItem hat in CopyHats(sceneCosmetics.hats, map))
            if (hat.cosmeticModel != null) hat.cosmeticModel.SetActive(loadout.IsAccessoryEquipped(hat.cosmeticID));
        Animator animator = clone.GetComponentInChildren<Animator>(true);
        clone.SetActive(true);
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;
            animator.SetFloat(SpeedParameter, 0f);
            animator.SetBool(SprintParameter, false);
            animator.SetBool(GroundedParameter, true);
            PlayWaveAnimation(animator, false);
            int carry = animator.GetLayerIndex("Carry Pose");
            if (carry >= 0) animator.SetLayerWeight(carry, 0f);
            animator.Play("Base Layer.idle", 0, 0f);
            animator.Update(0f);
        }
        return clone;
    }
}
