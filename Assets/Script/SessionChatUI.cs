using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Uses only scene-authored objects. History is discarded when the runner changes.</summary>
[DisallowMultipleComponent]
public sealed class SessionChatUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button openButton;
    [SerializeField] private TMP_InputField messageInput;
    [SerializeField] private TMP_Text messages;
    [SerializeField] private ScrollRect messageScroll;
    [SerializeField] private Button sendButton;
    [SerializeField] private TMP_Text status;
    [SerializeField] private TMP_Text unreadBadge;
    private readonly SessionChatHistory history = new SessionChatHistory();
    private NetworkRunner boundRunner;
    private PlayerMotor motor;
    private InputManager input;
    private bool captured, motorEnabled, inputEnabled, lookEnabled;
    private int unread;
    private float nextRefresh;
    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOpenState() { IsOpen = false; }

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
        // Only the formatter's sender highlight is markup; names/messages are escaped.
        if (messages != null) messages.richText = true;
        if (messageInput != null) { messageInput.characterLimit = SessionChatHistory.MessageLimit; messageInput.richText = false; }
        RefreshHistory(true);
    }
    private void OnEnable() { FusionMultiplayerAvatar.ChatMessageReceived += Receive; }
    private void OnDisable() { FusionMultiplayerAvatar.ChatMessageReceived -= Receive; Close(); }
    private void OnDestroy() { FusionMultiplayerAvatar.ChatMessageReceived -= Receive; Close(); }

    private static NetworkRunner ActiveRunner()
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        return connection != null && connection.IsHostWorldSession && !connection.IsNetworkSceneLoading ? connection.Runner : null;
    }
    private static FusionMultiplayerAvatar LocalAvatar(NetworkRunner runner)
    {
        return runner != null && runner.IsRunning && runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject obj) &&
            obj != null && obj.IsValid ? obj.GetComponent<FusionMultiplayerAvatar>() : null;
    }
    private void BindSession()
    {
        NetworkRunner current = ActiveRunner();
        if (boundRunner == current) return;
        Close(); boundRunner = current; history.Clear(); unread = 0;
        if (messageInput != null) messageInput.SetTextWithoutNotify(string.Empty);
        RefreshHistory(true);
    }
    private bool CanOpen => gameObject.scene.name == FusionConnectionManager.HostWorldSceneName && ActiveRunner() != null &&
        (GameManager.Instance == null || !GameManager.Instance.IsTransitioning && !GameManager.Instance.IsCargoTestActive);

    public void Open()
    {
        BindSession();
        if (captured || panel == null || !CanOpen) return;
        WorldMultiplayerPanelUI.CloseForChallenge(gameObject.scene);
        foreach (PlayerMotor candidate in FindObjectsOfType<PlayerMotor>(true))
            if (candidate.gameObject.scene == gameObject.scene) { motor = candidate; break; }
        if (motor != null)
        {
            input = motor.GetComponent<InputManager>();
            PlayerLook look = motor.GetComponent<PlayerLook>();
            motorEnabled = motor.enabled; inputEnabled = input != null && input.IsPlayerInputEnabled; lookEnabled = look != null && look.canLook;
            motor.enabled = false;
            if (input != null) { input.SetPlayerInputEnable(false); input.SetLookEnabled(false); }
        }
        captured = true; IsOpen = true; unread = 0;
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.OpenPanel(panel); else panel.SetActive(true);
        RefreshHistory(true); RefreshControls();
        if (messageInput != null) { messageInput.Select(); messageInput.ActivateInputField(); }
    }
    public void Close()
    {
        if (!captured) { if (panel != null) panel.SetActive(false); return; }
        if (messageInput != null) messageInput.DeactivateInputField();
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(panel); else if (panel != null) panel.SetActive(false);
        captured = false; IsOpen = false;
        if (motor != null) motor.enabled = motorEnabled;
        if (input != null) { input.SetPlayerInputEnable(inputEnabled); input.SetLookEnabled(lookEnabled); }
        motor = null; input = null;
    }
    public static void CloseForTransition(Scene scene)
    {
        foreach (SessionChatUI ui in FindObjectsOfType<SessionChatUI>(true))
            if (ui.gameObject.scene == scene && ui.captured) ui.Close();
    }
    public void Send()
    {
        BindSession();
        if (!captured || messageInput == null) return;
        FusionMultiplayerAvatar avatar = LocalAvatar(boundRunner);
        if (avatar == null || !avatar.TrySendChat(messageInput.text)) { RefreshControls(); return; }
        messageInput.SetTextWithoutNotify(string.Empty);
        messageInput.ActivateInputField(); RefreshControls();
    }
    public void SubmitInput(string unused) { Send(); }

    private void Update()
    {
        BindSession();
        if (captured && (!CanOpen || panel == null || !panel.activeInHierarchy)) Close();
#if ENABLE_INPUT_SYSTEM
        if (captured && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (captured && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Close();
#endif
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.1f;
        RefreshControls();
    }
    private void RefreshControls()
    {
        if (openButton != null) openButton.interactable = CanOpen;
        FusionMultiplayerAvatar avatar = LocalAvatar(boundRunner);
        bool canSend = avatar != null && avatar.CanSendChat;
        if (sendButton != null) sendButton.interactable = canSend && messageInput != null && SessionChatHistory.Normalize(messageInput.text).Length > 0;
        if (status != null) status.text = boundRunner == null ? "Turn on multiplayer to chat." :
            FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.ConnectedPlayerCount < 2 ? "Waiting for a guest to join..." :
            avatar == null ? "Connecting chat..." : !canSend ? "Please wait a moment before sending again." : "Enter to send  •  Session only";
        if (unreadBadge != null) { unreadBadge.gameObject.SetActive(unread > 0); unreadBadge.text = unread > 9 ? "9+" : unread.ToString(); }
    }
    private void Receive(NetworkRunner runner, int sequence, string name, string body)
    {
        BindSession();
        if (runner == null || runner != boundRunner || !history.Add(sequence, name, body)) return;
        if (!captured) unread++;
        RefreshHistory(captured && (messageScroll == null || messageScroll.verticalNormalizedPosition <= 0.03f));
        RefreshControls();
    }
    private void RefreshHistory(bool scrollToBottom)
    {
        if (messages == null) return;
        messages.text = history.Count == 0 ? "No messages yet. Say hello!" : history.Text;
        if (messageScroll == null || messageScroll.viewport == null) return;
        Canvas.ForceUpdateCanvases();
        messages.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Max(messageScroll.viewport.rect.height, messages.preferredHeight + 24f));
        if (scrollToBottom) { messageScroll.StopMovement(); messageScroll.verticalNormalizedPosition = 0f; }
    }
}
