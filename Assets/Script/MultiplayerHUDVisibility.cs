using System.Collections.Generic;
using UnityEngine;

/// <summary>Visibility only: all UI and CanvasGroups are authored in the scene.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10000)]
public sealed class MultiplayerHUDVisibility : MonoBehaviour
{
    [SerializeField] private GameObject emoteButton;
    [SerializeField] private GameObject chatButton;
    [Tooltip("Build Canvas branches except the bridge-test introduction, plus multiplayer utility controls.")]
    [SerializeField] private CanvasGroup[] testHiddenGroups;
    private struct GroupState
    {
        public CanvasGroup group;
        public float alpha;
        public bool interactable, blocksRaycasts;
    }
    private readonly List<GroupState> previous = new List<GroupState>();
    private bool? wasOnline;

    private void Awake() { ApplyVisibility(false, false); }
    private void LateUpdate()
    {
        var connection = FusionConnectionManager.Instance;
        bool online = connection != null && connection.IsAvatarScene && !connection.IsSessionStopping;
        var host = online && connection.IsHostWorldSession && !connection.IsNetworkSceneLoading
            ? FusionMultiplayerAvatar.FindHost(connection.Runner) : null;
        bool testing = host != null && GameManager.Instance != null && GameManager.Instance.IsSessionChallengeBuild &&
            MultiplayerChallengeRules.IsTestPhase(host.ChallengeState.Phase);
        ApplyVisibility(online, testing);
    }

    internal void ApplyVisibility(bool online, bool testing)
    {
        // The controller lives on the persistent scene service, not either button,
        // so disabling the buttons cannot disable the code that brings them back.
        // Only restore on session changes; don't undo modal/coordinator visibility.
        if (!online || wasOnline != online)
        {
            if (emoteButton != null) emoteButton.SetActive(online);
            if (chatButton != null) chatButton.SetActive(online);
        }
        wasOnline = online;
        if (!testing) { RestoreGroups(); return; }
        if (previous.Count == 0 && testHiddenGroups != null)
            foreach (CanvasGroup group in testHiddenGroups)
                if (group != null && !previous.Exists(state => state.group == group))
                    previous.Add(new GroupState { group = group, alpha = group.alpha,
                        interactable = group.interactable, blocksRaycasts = group.blocksRaycasts });
        // Do not deactivate controls: other controllers may refresh their activeSelf
        // during tests, and cleanup still needs to run. Mask rendering and input only.
        foreach (GroupState state in previous)
            if (state.group != null)
            {
                state.group.alpha = 0f;
                state.group.interactable = false;
                state.group.blocksRaycasts = false;
            }
    }

    private void RestoreGroups()
    {
        foreach (GroupState state in previous)
            if (state.group != null)
            {
                state.group.alpha = state.alpha;
                state.group.interactable = state.interactable;
                state.group.blocksRaycasts = state.blocksRaycasts;
            }
        previous.Clear();
    }
    private void OnDisable() { RestoreGroups(); wasOnline = null; }
}
