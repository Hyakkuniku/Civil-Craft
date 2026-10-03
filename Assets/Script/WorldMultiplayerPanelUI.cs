using System;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Controls only scene-authored world session UI; never creates UI at runtime.</summary>
[DisallowMultipleComponent]
public sealed class WorldMultiplayerPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button turnOnButton;
    [SerializeField] private TMP_Text turnOnLabel;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject codeCard;
    [SerializeField] private TMP_Text codeText;
    [SerializeField] private GameObject hostRow;
    [SerializeField] private TMP_Text hostName;
    [SerializeField] private GameObject guestRow;
    [SerializeField] private TMP_Text guestName;
    [SerializeField] private GameObject waitingLabel;
    [SerializeField] private Button kickButton;
    [SerializeField] private GameObject kickConfirmation;
    [SerializeField] private TMP_Text kickGuestName;
    [SerializeField] private Button confirmKickButton;
    [SerializeField] private Button turnOffButton;
    [SerializeField] private TMP_Text turnOffLabel;
    [SerializeField] private GameObject turnOffConfirmation;
    [SerializeField] private Button confirmTurnOffButton;
    private bool starting, stopping, inputCaptured;
    private float nextRefresh;
    private PlayerMotor motor;
    private InputManager input;
    private bool motorWasEnabled, inputWasEnabled, lookWasEnabled;
    private string notice;
    private NetworkRunner kickRunner;
    private NetworkRunner turnOffRunner;
    private NetworkObject kickAvatar;
    private PlayerRef kickPlayer = PlayerRef.None;
    private PlayerRef lastJoinedGuest = PlayerRef.None;

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
        if (kickConfirmation != null) kickConfirmation.SetActive(false);
        if (turnOffConfirmation != null) turnOffConfirmation.SetActive(false);
        // IGNs must never be interpreted as TMP markup.
        if (hostName != null) hostName.richText = false;
        if (guestName != null) guestName.richText = false;
        if (kickGuestName != null) kickGuestName.richText = false;
        RefreshOpenButton();
    }

    // Keep session management available to an already-connected visitor/host.
    private bool HasMultiplayerAccess => MultiplayerProgressionGate.IsUnlocked ||
        (FusionConnectionManager.Instance?.IsHosting ?? false) ||
        (FusionConnectionManager.Instance?.IsClientConnected ?? false);

    private bool CanOpen => HasMultiplayerAccess &&
        gameObject.scene.name == FusionConnectionManager.HostWorldSceneName &&
        (GameManager.Instance == null || GameManager.Instance.CurrentState == GameManager.GameState.Normal &&
            !GameManager.Instance.IsTransitioning) && !MultiplayerChallengeLobbyUI.IsChallengeActive &&
        !(FusionConnectionManager.Instance?.IsNetworkSceneLoading ?? false);

    public void Open()
    {
        if (panel == null || !CanOpen || inputCaptured) return;
        SessionChatUI.CloseForTransition(gameObject.scene);
        foreach (PlayerMotor candidate in FindObjectsOfType<PlayerMotor>(true))
            if (candidate.gameObject.scene == gameObject.scene) { motor = candidate; break; }
        if (motor != null)
        {
            input = motor.GetComponent<InputManager>();
            var look = motor.GetComponent<PlayerLook>();
            motorWasEnabled = motor.enabled;
            inputWasEnabled = input != null && input.IsPlayerInputEnabled;
            lookWasEnabled = look != null && look.canLook;
            motor.enabled = false;
            if (input != null) { input.SetPlayerInputEnable(false); input.SetLookEnabled(false); }
        }
        inputCaptured = true;
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.OpenPanel(panel);
        else panel.SetActive(true);
        RefreshView();
    }

    public void Close()
    {
        CancelKick();
        CancelTurnOff();
        if (panel != null)
        {
            if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(panel);
            else panel.SetActive(false);
        }
        if (!inputCaptured) return;
        inputCaptured = false;
        if (motor != null) motor.enabled = motorWasEnabled;
        if (input != null) { input.SetPlayerInputEnable(inputWasEnabled); input.SetLookEnabled(lookWasEnabled); }
        motor = null; input = null;
        // Closing this panel does not close the online room.
    }

    public static void CloseForChallenge(Scene scene)
    {
        foreach (WorldMultiplayerPanelUI ui in FindObjectsOfType<WorldMultiplayerPanelUI>(true))
            if (ui.gameObject.scene == scene && ui.inputCaptured) ui.Close();
    }

    private void Update()
    {
        RefreshOpenButton();
        if (!inputCaptured) return;
        if (MultiplayerChallengeLobbyUI.IsChallengeActive ||
            GameManager.Instance != null && (GameManager.Instance.CurrentState != GameManager.GameState.Normal || GameManager.Instance.IsTransitioning) ||
            panel == null || !panel.activeInHierarchy) { Close(); return; }
        if (EscapePressed())
        {
            if (stopping) return;
            if (turnOffConfirmation != null && turnOffConfirmation.activeSelf) CancelTurnOff();
            else if (kickConfirmation != null && kickConfirmation.activeSelf) CancelKick(); else Close();
            return;
        }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        RefreshView();
    }

    private void RefreshOpenButton()
    {
        if (openButton == null) return;
        bool visible = HasMultiplayerAccess;
        if (openButton.gameObject.activeSelf != visible) openButton.gameObject.SetActive(visible);
        openButton.interactable = CanOpen;
    }

    private static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return UnityEngine.Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    public async void TurnOnMultiplayer()
    {
        if (starting || stopping || !inputCaptured || !CanOpen || !MultiplayerProgressionGate.IsUnlocked) return;
        FusionConnectionManager connection = FusionConnectionManager.GetOrCreate();
        if (connection.IsHosting || connection.IsClientConnected || connection.IsSessionStopping) { RefreshView(); return; }
        starting = true; notice = null; RefreshView();
        try { await connection.StartHostFromCurrentWorldAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Debug.LogWarning($"[World multiplayer] Could not start session: {exception.Message}", this);
            if (this != null) notice = "Could not create a room. Check your connection and try again.";
        }
        finally { if (this != null) { starting = false; RefreshView(); } }
    }

    public void RequestKick()
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (stopping || !inputCaptured || kickConfirmation == null || connection == null || !connection.IsHostWorldSession ||
            !connection.TryGetGuest(out PlayerRef guest, out NetworkObject avatar) || avatar == null || !avatar.IsValid) return;
        kickRunner = connection.Runner; kickPlayer = guest; kickAvatar = avatar;
        if (kickGuestName != null) kickGuestName.text = GuestName(avatar);
        kickConfirmation.SetActive(true);
    }

    public void RequestTurnOff()
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (starting || stopping || !inputCaptured || turnOffConfirmation == null || connection == null ||
            !connection.IsHosting || !connection.IsHostWorldSession || connection.IsNetworkSceneLoading) return;
        CancelKick();
        turnOffRunner = connection.Runner;
        turnOffConfirmation.SetActive(true);
    }

    public async void ConfirmTurnOff()
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        if (stopping || !inputCaptured || turnOffConfirmation == null || !turnOffConfirmation.activeSelf ||
            connection == null || connection.Runner != turnOffRunner || !connection.IsHosting ||
            !connection.IsHostWorldSession || connection.IsNetworkSceneLoading) return;
        stopping = true;
        CancelTurnOff();
        notice = null;
        try
        {
            // Clear code/roster immediately, but don't permit a new runner until shutdown finishes.
            var shutdown = connection.StopHostingWorldAsync();
            RefreshView();
            await shutdown;
            if (this != null) notice = "<b>Multiplayer is off.</b> You can keep playing in your world.";
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[World multiplayer] Could not close session: {exception.Message}", this);
            if (this != null) notice = "Could not close the room. Please try again.";
        }
        finally { if (this != null) { stopping = false; RefreshView(); } }
    }

    public void CancelTurnOff()
    {
        if (turnOffConfirmation != null) turnOffConfirmation.SetActive(false);
        turnOffRunner = null;
    }

    public void ConfirmKick()
    {
        if (!inputCaptured || kickConfirmation == null || !kickConfirmation.activeSelf) return;
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        bool removed = connection != null && connection.Runner == kickRunner && connection.TryKickGuest(kickPlayer, kickAvatar);
        CancelKick();
        notice = removed ? "Guest removed. Your room and join code remain active." : "That guest is no longer connected.";
        RefreshView();
    }

    public void CancelKick()
    {
        if (kickConfirmation != null) kickConfirmation.SetActive(false);
        kickRunner = null; kickAvatar = null; kickPlayer = PlayerRef.None;
    }

    private void RefreshView()
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        bool hosting = connection != null && connection.IsHosting;
        bool visiting = connection != null && connection.IsClientConnected;
        PlayerRef guest = PlayerRef.None; NetworkObject avatar = null;
        if (hosting) connection.TryGetGuest(out guest, out avatar);
        bool loading = connection != null && connection.IsNetworkSceneLoading;
        bool shuttingDown = stopping || (connection != null && connection.IsSessionStopping);
        ApplySessionView(hosting, visiting, loading, shuttingDown, guest, avatar,
            connection != null ? connection.HostJoinCode : null, GuestName(avatar));
        if (turnOffConfirmation != null && turnOffConfirmation.activeSelf &&
            (!hosting || connection.Runner != turnOffRunner || loading)) CancelTurnOff();
        if (kickConfirmation != null && kickConfirmation.activeSelf)
        {
            bool same = hosting && connection.Runner == kickRunner && guest == kickPlayer && avatar == kickAvatar && avatar != null;
            if (!same) CancelKick();
            if (confirmKickButton != null) confirmKickButton.interactable = same;
        }
    }

    // Render the roster independently of input and avatar initialization. A connected guest
    // appears immediately, then their replicated IGN replaces the connecting placeholder.
    private void ApplySessionView(bool hosting, bool visiting, bool loading, bool shuttingDown,
        PlayerRef guest, NetworkObject avatar, string joinCode, string guestDisplayName)
    {
        bool online = hosting || visiting;
        bool joined = hosting && guest != PlayerRef.None;
        if (joined && guest != lastJoinedGuest) notice = null;
        lastJoinedGuest = joined ? guest : PlayerRef.None;
        if (turnOnButton != null) { turnOnButton.gameObject.SetActive(!online && !shuttingDown && MultiplayerProgressionGate.IsUnlocked); turnOnButton.interactable = !starting && !shuttingDown && MultiplayerProgressionGate.IsUnlocked; }
        if (turnOffButton != null) { turnOffButton.gameObject.SetActive(hosting || shuttingDown); turnOffButton.interactable = hosting && !loading && !shuttingDown; }
        if (turnOffLabel != null) turnOffLabel.text = shuttingDown ? "CLOSING ROOM..." : "TURN OFF MULTIPLAYER";
        if (turnOnLabel != null) turnOnLabel.text = starting ? "CREATING ROOM..." : "TURN ON MULTIPLAYER";
        if (codeCard != null) codeCard.SetActive(!visiting);
        if (codeText != null) codeText.text = hosting ? joinCode ?? "..." : starting ? "CREATING..." : "NOT ACTIVE";
        if (hostRow != null) hostRow.SetActive(!visiting);
        if (hostName != null) hostName.text = PlayerDataManager.Instance?.CurrentData?.playerName ?? "Engineer";
        if (guestRow != null) guestRow.SetActive(joined);
        if (guestName != null) guestName.text = guestDisplayName;
        if (waitingLabel != null) waitingLabel.SetActive(hosting && !joined);
        if (kickButton != null) kickButton.interactable = joined && avatar != null && avatar.IsValid && !loading && !shuttingDown;
        if (confirmTurnOffButton != null) confirmTurnOffButton.interactable = hosting && !loading && !shuttingDown;
        if (statusText != null) statusText.text = !string.IsNullOrEmpty(notice) ? notice :
            shuttingDown ? "Closing your <b>online room</b>..." :
            starting ? "Creating your <b>private online room</b>..." :
            visiting ? "You are visiting the host's world. Only the host can manage this session." :
            hosting ? "<color=#376F42><b>MULTIPLAYER ON</b></color>  •  " + (joined ? "2 / 2 PLAYERS" : "1 / 2 PLAYERS") :
            !MultiplayerProgressionGate.IsUnlocked ? MultiplayerProgressionGate.LockedMessage :
            "Invite a friend into <b>your world</b>. Turn on multiplayer to get a join code.";
    }

    private static string GuestName(NetworkObject avatar)
    {
        string value = avatar != null && avatar.IsValid ? avatar.GetComponent<FusionMultiplayerAvatar>()?.MapPlayerName : null;
        return string.IsNullOrWhiteSpace(value) ? "Guest (connecting...)" : value;
    }

    private void OnDisable() { Close(); }
    private void OnDestroy() { Close(); }
}
