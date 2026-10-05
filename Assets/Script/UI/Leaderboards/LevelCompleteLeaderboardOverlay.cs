using UnityEngine;

/// <summary>Keeps the completion report and its input/HUD state beneath a nested board.</summary>
[DisallowMultipleComponent]
public sealed class LevelCompleteLeaderboardOverlay : MonoBehaviour
{
    private UIPanelCoordinator coordinator;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        coordinator = UIPanelCoordinator.Instance;
        if (coordinator != null) coordinator.OpenPanel(gameObject, false);
    }

    private void OnDisable()
    {
        if (coordinator != null && coordinator.ContainsPanel(gameObject))
            coordinator.ClosePanel(gameObject, false);
        coordinator = null;
    }
}
