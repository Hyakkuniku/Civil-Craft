using Fusion;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored multiplayer button; its Inspector On Click calls PlayWave.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class MultiplayerEmoteButton : MonoBehaviour
{
    [SerializeField] private Button button;
    private float nextRefreshTime;

    private void Awake() { if (button == null) button = GetComponent<Button>(); }
    private void OnEnable() { nextRefreshTime = 0f; }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime) return;
        nextRefreshTime = Time.unscaledTime + 0.1f;
        RefreshInteractable();
    }

    public void PlayWave()
    {
        GetLocalAvatar()?.TryWave();
        RefreshInteractable();
    }

    private void RefreshInteractable()
    {
        if (button == null) return;
        FusionMultiplayerAvatar avatar = GetLocalAvatar();
        button.interactable = avatar != null && avatar.CanWave;
    }

    private static FusionMultiplayerAvatar GetLocalAvatar()
    {
        FusionConnectionManager connection = FusionConnectionManager.Instance;
        NetworkRunner runner = connection != null ? connection.Runner : null;
        if (runner == null || !runner.IsRunning || !connection.IsAvatarScene || connection.IsNetworkSceneLoading ||
            !runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject player) || player == null || !player.IsValid)
            return null;
        return player.GetComponent<FusionMultiplayerAvatar>();
    }
}
