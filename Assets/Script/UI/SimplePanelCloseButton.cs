using UnityEngine;

/// <summary>
/// Safe fallback for legacy panels whose original scene-specific close target
/// no longer exists. Prefer a panel manager's Close method whenever available.
/// </summary>
public sealed class SimplePanelCloseButton : MonoBehaviour
{
    public GameObject panelToClose;

    public void Close()
    {
        if (panelToClose == null) return;

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.ClosePanel(panelToClose);
        else
            panelToClose.SetActive(false);
    }
}
