using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates full-screen/modal UI without requiring the panels to share a prefab.
/// It records exact activeSelf states so closing a nested panel restores the previous UI.
/// </summary>
public class UIPanelCoordinator : MonoBehaviour
{
    private sealed class ObjectState
    {
        public GameObject target;
        public bool wasActive;
    }

    private sealed class PanelFrame
    {
        public GameObject panel;
        public readonly List<ObjectState> previousStates = new List<ObjectState>();
    }

    public static UIPanelCoordinator Instance { get; private set; }

    [Header("Main HUD Objects")]
    [Tooltip("HUD roots hidden whenever a managed full-screen panel opens.")]
    public List<GameObject> hudObjects = new List<GameObject>();

    [Header("Mutually Exclusive Panels")]
    [Tooltip("Pause, Almanac, Settings, Objective Tracker, and other large panel roots.")]
    public List<GameObject> managedPanels = new List<GameObject>();

    private readonly Stack<PanelFrame> panelStack = new Stack<PanelFrame>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen(GameObject panel)
    {
        return panel != null && panelStack.Count > 0 && panelStack.Peek().panel == panel;
    }

    public void OpenPanel(GameObject panel)
    {
        OpenPanel(panel, true);
    }

    public void OpenPanel(GameObject panel, bool activatePanel)
    {
        if (panel == null) return;

        if (IsOpen(panel))
        {
            if (activatePanel) panel.SetActive(true);
            return;
        }

        PanelFrame frame = new PanelFrame { panel = panel };
        HashSet<GameObject> recorded = new HashSet<GameObject>();

        RecordStates(hudObjects, frame, recorded);
        RecordStates(managedPanels, frame, recorded);

        foreach (GameObject hud in hudObjects)
        {
            if (hud != null && hud != panel) hud.SetActive(false);
        }

        foreach (GameObject managedPanel in managedPanels)
        {
            if (managedPanel != null && managedPanel != panel) managedPanel.SetActive(false);
        }

        if (activatePanel) panel.SetActive(true);
        panelStack.Push(frame);
    }

    public void ClosePanel(GameObject panel)
    {
        if (panel == null) return;

        if (!ContainsPanel(panel))
        {
            panel.SetActive(false);
            return;
        }

        // Normally the requested panel is on top. Unwinding also safely handles a
        // parent panel being closed while Settings or another nested panel is open.
        while (panelStack.Count > 0)
        {
            PanelFrame frame = panelStack.Pop();
            if (frame.panel != null) frame.panel.SetActive(false);
            RestoreStates(frame.previousStates);

            if (frame.panel == panel) return;
        }

        panel.SetActive(false);
    }

    private bool ContainsPanel(GameObject panel)
    {
        foreach (PanelFrame frame in panelStack)
        {
            if (frame.panel == panel) return true;
        }

        return false;
    }

    public void CloseAllPanels()
    {
        while (panelStack.Count > 0)
        {
            PanelFrame frame = panelStack.Pop();
            if (frame.panel != null) frame.panel.SetActive(false);
            RestoreStates(frame.previousStates);
        }
    }

    private static void RecordStates(
        List<GameObject> objects,
        PanelFrame frame,
        HashSet<GameObject> recorded)
    {
        if (objects == null) return;

        foreach (GameObject target in objects)
        {
            if (target == null || !recorded.Add(target)) continue;
            frame.previousStates.Add(new ObjectState
            {
                target = target,
                wasActive = target.activeSelf
            });
        }
    }

    private static void RestoreStates(List<ObjectState> states)
    {
        foreach (ObjectState state in states)
        {
            if (state.target != null) state.target.SetActive(state.wasActive);
        }
    }
}
