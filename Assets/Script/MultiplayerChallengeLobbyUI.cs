using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Controls scene-authored panels and visual-only character previews.</summary>
[DisallowMultipleComponent]
public sealed class MultiplayerChallengeLobbyUI : MonoBehaviour
{
    [Header("Authored UI")]
    [SerializeField] private GameObject challengePanel;
    [SerializeField] private TMP_Text challengeTitle;
    [SerializeField] private TMP_Text challengeMessage;
    [SerializeField] private Button acceptButton;
    [SerializeField] private Button declineButton;
    [SerializeField] private Button invitationCloseButton;
    [SerializeField] private Button challengeOfferButton;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TMP_Text lobbyTitle;
    [SerializeField] private TMP_Text lobbyStatus;
    [SerializeField] private TMP_Text hostName;
    [SerializeField] private TMP_Text guestName;
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonLabel;
    [Header("Authored portrait stage")]
    [SerializeField] private Camera portraitCamera;
    [SerializeField] private RawImage portraitImage;
    [SerializeField] private Transform hostPortraitAnchor;
    [SerializeField] private Transform guestPortraitAnchor;

    private readonly ChallengeSiteIsolation isolation = new ChallengeSiteIsolation();
    private FusionMultiplayerAvatar host, local;
    private NetworkRunner boundRunner;
    private GameObject openPanel;
    private PlayerMotor motor;
    private InputManager input;
    private bool inputCaptured, inputWasEnabled, lookWasEnabled, motorWasEnabled, hasReturnPose;
    private Vector3 returnPosition;
    private Quaternion returnRotation;
    private int arrivalRevision = int.MinValue, dismissedRevision = int.MinValue;
    private float nextPortraitRefresh, nextOfferRefresh;
    private GameObject hostPortrait, guestPortrait;
    private string hostAppearance, guestAppearance;
    private RenderTexture portraitTexture;
    private FusionHostWorldBridgeSync bridgeSync;
    private string resolvedSiteKey;
    private BuildLocation resolvedSite;
    private bool readyRequestPending, requestedReady;
    private int readyRequestRevision;
    private float readyRequestTime;

    public static bool IsChallengeActive
    {
        get
        {
            FusionConnectionManager connection = FusionConnectionManager.Instance;
            if (connection == null || !connection.IsHostWorldSession) return false;
            FusionMultiplayerAvatar hostAvatar = FusionMultiplayerAvatar.FindHost(connection.Runner);
            return hostAvatar != null && hostAvatar.IsChallengeBusy;
        }
    }

    private void Awake()
    {
        if (challengePanel != null) challengePanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (challengeOfferButton != null) challengeOfferButton.gameObject.SetActive(false);
        if (readyButton != null) readyButton.interactable = false;
        if (portraitCamera != null) portraitCamera.enabled = false;
    }

    public static bool CanOfferChallenge(BuildLocation site)
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (connection == null || !connection.IsHosting || !connection.IsHostWorldSession ||
            connection.IsNetworkSceneLoading) return false;
        FusionMultiplayerAvatar avatar = FusionMultiplayerAvatar.FindHost(connection.Runner);
        return avatar != null && avatar.CanInvite(site);
    }

    public void SendChallenge()
    {
        GameManager game = GameManager.Instance;
        BuildLocation site = game != null ? game.PendingRedoLocation : null;
        if (!CanOfferChallenge(site)) return;
        var avatar = FusionMultiplayerAvatar.FindHost(FusionConnectionManager.Instance.Runner);
        if (avatar != null && avatar.TryInvite(site)) game.CancelRedo();
    }

    public void AcceptChallenge() { local?.RespondToChallenge(host != null ? host.ChallengeState.Revision : -1, true); }
    public void DeclineChallenge() { local?.RespondToChallenge(host != null ? host.ChallengeState.Revision : -1, false); }
    public void ToggleReady()
    {
        if (host == null || local == null || boundRunner == null || readyRequestPending) return;
        MultiplayerChallengeState state = host.ChallengeState;
        bool ready = boundRunner.LocalPlayer == host.Object.InputAuthority ? (bool)state.HostReady : (bool)state.GuestReady;
        if (!MultiplayerChallengeRules.CanSetReady(state, boundRunner.LocalPlayer,
            host.Object.InputAuthority, state.Revision, !ready)) return;
        requestedReady = !ready;
        readyRequestRevision = state.Revision;
        readyRequestTime = Time.unscaledTime;
        readyRequestPending = true;
        if (readyButton != null) readyButton.interactable = false;
        local.SetChallengeReady(state.Revision, requestedReady);
    }

    public void CancelOrClose()
    {
        if (host != null && host.IsChallengeBusy) local?.CancelChallenge(host.ChallengeState.Revision);
        else
        {
            if (host != null) dismissedRevision = host.ChallengeState.Revision;
            CloseView();
        }
    }

    private void Update()
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (connection == null || !connection.IsHostWorldSession || connection.IsNetworkSceneLoading)
        {
            CloseView();
            host = local = null;
            boundRunner = null;
            arrivalRevision = dismissedRevision = int.MinValue;
            return;
        }
        if (boundRunner != connection.Runner)
        {
            CloseView();
            arrivalRevision = dismissedRevision = int.MinValue;
            boundRunner = connection.Runner;
        }
        host = FusionMultiplayerAvatar.FindHost(boundRunner);
        local = GetAvatar(boundRunner.LocalPlayer);
        if (Time.unscaledTime >= nextOfferRefresh)
        {
            nextOfferRefresh = Time.unscaledTime + 0.25f;
            GameManager.Instance?.RefreshMultiplayerChallengeOffer();
        }
        if (host == null || local == null) { CloseView(); return; }
        MultiplayerChallengeState state = host.ChallengeState;
        if (state.Phase == MultiplayerChallengePhase.None)
        {
            readyRequestPending = false;
            RestoreWorld();
            if (state.Result != MultiplayerChallengeResult.None && state.Revision != dismissedRevision)
            {
                ShowPanel(challengePanel);
                SetInvitationControls(false, false, true);
                challengeTitle.text = "CHALLENGE ENDED";
                challengeMessage.text = ResultMessage(state.Result);
            }
            else CloseView();
            return;
        }
        string siteKey = state.SiteKey.ToString();
        if (resolvedSiteKey != siteKey || resolvedSite == null)
        {
            resolvedSiteKey = siteKey;
            resolvedSite = MultiplayerChallengeRules.ResolveSite(siteKey);
        }
        BuildLocation site = resolvedSite;
        if (state.Phase == MultiplayerChallengePhase.Invited)
        {
            ShowPanel(challengePanel);
            bool guest = boundRunner.LocalPlayer == state.Guest;
            SetInvitationControls(guest, guest, !guest);
            challengeTitle.text = guest ? "BRIDGE CHALLENGE" : "INVITATION SENT";
            string siteName = site != null ? site.name : "build location";
            float seconds = state.Deadline.RemainingTime(boundRunner) ?? 0f;
            challengeMessage.text = guest
                ? $"{host.MapPlayerName} challenges you at {siteName}.\nAccept to travel to the lobby.\n{Mathf.CeilToInt(seconds)} seconds remaining."
                : $"Waiting for {GetAvatar(state.Guest)?.MapPlayerName ?? "guest"} to accept at {siteName}.\n{Mathf.CeilToInt(seconds)} seconds remaining.";
            return;
        }
        if (state.Phase == MultiplayerChallengePhase.Teleporting)
        {
            ShowPanel(challengePanel);
            SetInvitationControls(false, false, true);
            challengeTitle.text = "TRAVELLING TO CHALLENGE";
            challengeMessage.text = "Waiting for both players to arrive safely...";
            if (arrivalRevision != state.Revision)
            {
                arrivalRevision = state.Revision;
                bool success = TryTeleport(site, boundRunner.LocalPlayer == state.Guest);
                if (success)
                {
                    isolation.ShowOnly(site, FindObjectsOfType<BuildLocation>(true));
                    bridgeSync = boundRunner.GetComponent<FusionHostWorldBridgeSync>();
                    bridgeSync?.SetChallengeSiteVisibility(MultiplayerChallengeRules.SiteKey(site));
                }
                local.ReportChallengeArrival(state.Revision, success);
            }
            return;
        }
        if (state.Phase == MultiplayerChallengePhase.ReadyToBuild)
        {
            readyRequestPending = false;
            // Keep world isolation and input capture. Competitive bridge editing
            // is deliberately not entered until the separate session builds exist.
            ShowPanel(challengePanel);
            SetInvitationControls(false, false, true);
            challengeTitle.text = "READY TO BUILD";
            challengeMessage.text = $"Both players are ready at {(site != null ? site.name : "the bridge")}.\n" +
                "Countdown complete. Competitive building is not enabled yet.\nClose to return to the world.";
            if (portraitCamera != null) portraitCamera.enabled = false;
            return;
        }
        // Arrival acknowledgements and host pose validation gate the lobby.
        ShowPanel(lobbyPanel);
        lobbyTitle.text = $"CHALLENGE LOBBY — {(site != null ? site.name : "Bridge")} ";
        bool countingDown = state.Phase == MultiplayerChallengePhase.Countdown;
        float remaining = state.Deadline.RemainingTime(boundRunner) ?? 0f;
        lobbyStatus.text = countingDown
            ? (remaining > 0f ? $"Starting in {Mathf.CeilToInt(remaining)}..." : "Waiting for the host's start confirmation...")
            : "Select Ready when you are prepared. Both players must be ready to start.";
        hostName.text = host.MapPlayerName + (host == local ? " (YOU)" : " (HOST)") +
            ((bool)state.HostReady ? "\nREADY" : "\nNOT READY");
        FusionMultiplayerAvatar guestAvatar = GetAvatar(state.Guest);
        guestName.text = (guestAvatar != null ? guestAvatar.MapPlayerName : "Guest") + (guestAvatar == local ? " (YOU)" : " (GUEST)") +
            ((bool)state.GuestReady ? "\nREADY" : "\nNOT READY");
        bool localReady = host == local ? (bool)state.HostReady : (bool)state.GuestReady;
        if (readyRequestPending && (readyRequestRevision != state.Revision || localReady == requestedReady ||
            Time.unscaledTime - readyRequestTime >= 3f)) readyRequestPending = false;
        if (readyButton != null) readyButton.interactable = !readyRequestPending &&
            MultiplayerChallengeRules.CanSetReady(state, boundRunner.LocalPlayer, host.Object.InputAuthority, state.Revision, !localReady);
        if (readyButtonLabel != null) readyButtonLabel.text = readyRequestPending ? "UPDATING..." :
            localReady ? (countingDown ? "CANCEL READY" : "UNREADY") : "READY";
        RefreshPortraits(guestAvatar);
    }

    private FusionMultiplayerAvatar GetAvatar(PlayerRef player)
    {
        return boundRunner != null && boundRunner.IsRunning && boundRunner.TryGetPlayerObject(player, out NetworkObject obj) &&
            obj != null && obj.IsValid ? obj.GetComponent<FusionMultiplayerAvatar>() : null;
    }

    private void SetInvitationControls(bool accept, bool decline, bool close)
    {
        if (acceptButton != null) acceptButton.gameObject.SetActive(accept);
        if (declineButton != null) declineButton.gameObject.SetActive(decline);
        if (invitationCloseButton != null) invitationCloseButton.gameObject.SetActive(close);
    }

    private void ShowPanel(GameObject panel)
    {
        if (panel == null || openPanel == panel) return;
        if (openPanel != null)
        {
            if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(openPanel);
            else openPanel.SetActive(false);
        }
        CaptureInput();
        openPanel = panel;
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.OpenPanel(panel);
        else panel.SetActive(true);
    }

    private void CaptureInput()
    {
        if (inputCaptured) return;
        foreach (PlayerMotor candidate in FindObjectsOfType<PlayerMotor>(true))
            if (candidate.gameObject.scene == gameObject.scene) { motor = candidate; break; }
        if (motor == null) return;
        input = motor.GetComponent<InputManager>();
        PlayerLook look = motor.GetComponent<PlayerLook>();
        inputWasEnabled = input != null && input.IsPlayerInputEnabled;
        lookWasEnabled = look != null && look.canLook;
        motorWasEnabled = motor.enabled;
        inputCaptured = true;
        motor.enabled = false;
        if (input != null) { input.SetPlayerInputEnable(false); input.SetLookEnabled(false); }
    }

    private bool TryTeleport(BuildLocation site, bool guest)
    {
        if (site == null || motor == null || motor.gameObject.scene != site.gameObject.scene) return false;
        Transform target = TravelTarget(site);
        if (!TryResolveLanding(site, motor.GetComponent<CharacterController>(), guest, out Vector3 destination))
        {
            Debug.LogWarning($"[Challenge travel] No supported, unobstructed landing near {site.name}'s " +
                $"{target.name} at {target.position}. Check the Fast Travel Target's ground and headroom.", site);
            return false;
        }
        if (!hasReturnPose)
        {
            returnPosition = motor.transform.position;
            returnRotation = motor.transform.rotation;
            hasReturnPose = true;
        }
        MovePlayer(destination, target.rotation);
        return IsAtSite(site, motor.transform.position);
    }

    /// <summary>Use the authored travel anchor, not the NPC NavMesh. Query the site's physics scene so
    /// this also works in isolated editor fixtures. No player or bridge state is changed here.</summary>
    public static bool TryResolveLanding(BuildLocation site, CharacterController controller, bool guest,
        out Vector3 destination)
    {
        destination = default;
        if (site == null || controller == null) return false;
        Transform target = TravelTarget(site);
        Vector3 scale = controller.transform.lossyScale;
        float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float half = Mathf.Max(0f, controller.height * Mathf.Abs(scale.y) * 0.5f - radius);
        Vector3 up = target.rotation * Vector3.up;
        Vector3 centerOffset = target.rotation * Vector3.Scale(controller.center, scale);
        float bottomOffset = centerOffset.y - Mathf.Abs(up.y) * half - radius;
        float spacing = Mathf.Max(2f, radius * 2f + 0.25f);
        Vector3[] offsets = guest
            ? new[] { target.right * spacing, -target.right * spacing,
                -target.forward * spacing, target.forward * spacing }
            : new[] { Vector3.zero, target.forward, -target.forward };
        var groundHits = new RaycastHit[32];
        var obstacles = new Collider[64];
        PhysicsScene physics = site.gameObject.scene.GetPhysicsScene();
        Physics.SyncTransforms();
        // Include Default: some authored terrain uses it rather than Ground.
        const int groundMask = (1 << 0) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 12);
        const int obstacleMask = Physics.DefaultRaycastLayers & ~((1 << 4) | (1 << 5) | (1 << 7) | (1 << 8));
        foreach (Vector3 offset in offsets)
        {
            Vector3 candidate = target.position + offset;
            int hits = physics.Raycast(candidate + centerOffset + Vector3.up * 2f, Vector3.down,
                groundHits, 6f, groundMask, QueryTriggerInteraction.Ignore);
            if (hits == groundHits.Length) continue; // Saturated queries must not claim safety.
            int nearest = -1;
            for (int i = 0; i < hits; i++)
            {
                if (IsPlayerCollider(groundHits[i].collider, controller)) continue;
                if (nearest < 0 || groundHits[i].distance < groundHits[nearest].distance) nearest = i;
            }
            if (nearest < 0 || Vector3.Angle(groundHits[nearest].normal, Vector3.up) > controller.slopeLimit) continue;
            // The controller's root is not necessarily at its feet (ours has a
            // low centre). Account for both centre and world scale before clearance.
            candidate.y = groundHits[nearest].point.y - bottomOffset + 0.05f;
            if (!IsAtSite(site, candidate)) continue;
            Vector3 center = candidate + centerOffset;
            int count = physics.OverlapCapsule(center + up * half, center - up * half, radius,
                obstacles, obstacleMask, QueryTriggerInteraction.Ignore);
            bool blocked = count == obstacles.Length;
            for (int i = 0; i < count && !blocked; i++)
                blocked = !IsPlayerCollider(obstacles[i], controller);
            if (blocked) continue;
            destination = candidate;
            return true;
        }
        return false;
    }

    private static bool IsPlayerCollider(Collider collider, CharacterController localController) =>
        collider == localController || collider.transform.IsChildOf(localController.transform) ||
        collider.GetComponentInParent<PlayerMotor>() != null ||
        collider.GetComponentInParent<FusionMultiplayerAvatar>() != null;

    private static Transform TravelTarget(BuildLocation site) => site.fastTravelTarget != null ? site.fastTravelTarget :
        site.navigationTarget != null ? site.navigationTarget.transform : site.transform;

    public static bool IsAtSite(BuildLocation site, Vector3 position) => site != null &&
        (position - TravelTarget(site).position).sqrMagnitude <= 64f;

    private void MovePlayer(Vector3 position, Quaternion rotation)
    {
        if (motor == null) return;
        CharacterController controller = motor.GetComponent<CharacterController>();
        bool enabled = controller != null && controller.enabled;
        if (enabled) controller.enabled = false;
        motor.transform.SetPositionAndRotation(position, rotation);
        motor.ResetTestMotion();
        Physics.SyncTransforms();
        if (enabled) controller.enabled = true;
        motor.GetComponent<PlayerLook>()?.SnapToFollowTarget();
    }

    private void RefreshPortraits(FusionMultiplayerAvatar guest)
    {
        if (portraitCamera == null || portraitImage == null || hostPortraitAnchor == null || guestPortraitAnchor == null) return;
        if (portraitTexture == null)
        {
            // Match the authored display rectangle rather than stretching the
            // players on different screen aspect ratios.
            Canvas.ForceUpdateCanvases();
            Rect display = portraitImage.rectTransform.rect;
            float aspect = display.height > 0f ? display.width / display.height : 2f;
            int height = Mathf.Clamp(Mathf.RoundToInt(768f / Mathf.Max(0.5f, aspect)), 128, 1024);
            portraitTexture = new RenderTexture(768, height, 16)
            { name = "Challenge Lobby Portraits (Session Only)", hideFlags = HideFlags.DontSave };
            portraitTexture.Create();
            portraitCamera.targetTexture = portraitTexture;
            portraitImage.texture = portraitTexture;
        }
        portraitCamera.enabled = true;
        if (Time.unscaledTime < nextPortraitRefresh) return;
        nextPortraitRefresh = Time.unscaledTime + 0.5f;
        RefreshPortrait(host, hostPortraitAnchor, ref hostPortrait, ref hostAppearance);
        RefreshPortrait(guest, guestPortraitAnchor, ref guestPortrait, ref guestAppearance);
    }

    private static void RefreshPortrait(FusionMultiplayerAvatar avatar, Transform anchor, ref GameObject portrait, ref string appearance)
    {
        if (avatar == null) return;
        string key = avatar.LobbyAppearanceKey;
        if (portrait != null && appearance == key) return;
        if (portrait != null) Destroy(portrait);
        portrait = avatar.CreateLobbyVisual(anchor);
        appearance = portrait != null ? key : null;
        if (portrait == null) return;
        foreach (Renderer renderer in portrait.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.activeInHierarchy) renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
                skinned.forceMatrixRecalculationPerRender = true;
            }
        }
    }

    private void RestoreWorld()
    {
        isolation.Dispose();
        bridgeSync?.SetChallengeSiteVisibility(null);
        bridgeSync = null;
        if (hasReturnPose && motor != null && motor.gameObject.scene == gameObject.scene)
            MovePlayer(returnPosition, returnRotation);
        hasReturnPose = false;
        if (portraitCamera != null) portraitCamera.enabled = false;
        if (hostPortrait != null) Destroy(hostPortrait);
        if (guestPortrait != null) Destroy(guestPortrait);
        hostPortrait = guestPortrait = null;
        hostAppearance = guestAppearance = null;
    }

    private void CloseView()
    {
        readyRequestPending = false;
        if (readyButton != null) readyButton.interactable = false;
        RestoreWorld();
        if (openPanel != null)
        {
            if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(openPanel);
            else openPanel.SetActive(false);
        }
        openPanel = null;
        if (inputCaptured && input != null)
        {
            input.SetPlayerInputEnable(inputWasEnabled);
            input.SetLookEnabled(lookWasEnabled);
        }
        inputCaptured = false;
        if (motor != null) motor.enabled = motorWasEnabled;
        motor = null;
        input = null;
    }

    private static string ResultMessage(MultiplayerChallengeResult result)
    {
        switch (result)
        {
            case MultiplayerChallengeResult.Declined: return "The guest declined the challenge.";
            case MultiplayerChallengeResult.TimedOut: return "The challenge timed out before both players arrived.";
            case MultiplayerChallengeResult.TravelFailed: return "A safe landing point could not be confirmed. No bridge was changed.";
            case MultiplayerChallengeResult.PlayerLeft: return "The other player left the session.";
            default: return "The challenge was cancelled. Your saved bridges are unchanged.";
        }
    }

    private void OnDisable() { CloseView(); }
    private void OnDestroy()
    {
        CloseView();
        if (portraitCamera != null) portraitCamera.targetTexture = null;
        if (portraitImage != null) portraitImage.texture = null;
        if (portraitTexture != null) { portraitTexture.Release(); Destroy(portraitTexture); }
    }
}
