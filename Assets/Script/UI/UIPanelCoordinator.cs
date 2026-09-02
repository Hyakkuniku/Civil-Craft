using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        public readonly List<CanvasState> previousCanvasStates = new List<CanvasState>();
    }

    private sealed class CanvasState
    {
        public Canvas target;
        public bool wasEnabled;
    }

    public static UIPanelCoordinator Instance { get; private set; }

    [Header("Main HUD Objects")]
    [Tooltip("HUD roots hidden whenever a managed full-screen panel opens.")]
    public List<GameObject> hudObjects = new List<GameObject>();

    [Header("Mutually Exclusive Panels")]
    [Tooltip("Pause, Almanac, Settings, Objective Tracker, and other large panel roots.")]
    public List<GameObject> managedPanels = new List<GameObject>();

    private readonly Stack<PanelFrame> panelStack = new Stack<PanelFrame>();

    /// <summary>
    /// True while any full-screen/modal panel is being coordinated. Lightweight
    /// overlays such as tutorial pointers use this to suspend their rendering
    /// without discarding the guidance target they need to restore afterwards.
    /// </summary>
    public bool HasOpenPanel => panelStack.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureCoordinatorExists()
    {
        if (FindObjectOfType<UIPanelCoordinator>(true) != null)
            return;

        GameObject coordinatorObject = new GameObject("UIPanelCoordinator");
        coordinatorObject.AddComponent<UIPanelCoordinator>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
        RefreshManagedPanels();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Instance = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Restore any persistent Canvas/HUD state captured in the previous scene
        // before discarding that scene's modal stack.
        CloseAllPanels();
        hudObjects.RemoveAll(item => item == null);
        managedPanels.RemoveAll(item => item == null);
        RefreshManagedPanels();
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

        RefreshManagedPanels();

        if (IsOpen(panel))
        {
            if (activatePanel) panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            return;
        }

        PanelFrame frame = new PanelFrame { panel = panel };
        HashSet<GameObject> recorded = new HashSet<GameObject>();

        RecordStates(hudObjects, frame, recorded);
        RecordStates(managedPanels, frame, recorded);

        foreach (GameObject hud in hudObjects)
        {
            if (CanHideObject(hud, panel)) hud.SetActive(false);
        }

        foreach (GameObject managedPanel in managedPanels)
        {
            if (CanHideObject(managedPanel, panel)) managedPanel.SetActive(false);
        }

        // A previously opened modal may have disabled an intermediate wrapper such
        // as MainCanvas/UtilityPanel. SetActive(true) on the requested child is not
        // enough in that case, so temporarily enable its GameObject parent chain.
        // The exact previous activeSelf values are restored when this panel closes.
        EnableTargetParentChain(panel, frame, recorded);

        // A parent modal may also have disabled the Canvas component that owns this
        // nested panel. Restore that component independently from GameObject state.
        EnableTargetCanvases(panel, frame);
        HideSameCanvasSiblings(panel, frame, recorded);
        HideOtherCanvases(panel, frame);

        if (activatePanel) panel.SetActive(true);
        panel.transform.SetAsLastSibling();
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
            RestoreCanvasStates(frame.previousCanvasStates);
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
            RestoreCanvasStates(frame.previousCanvasStates);
            RestoreStates(frame.previousStates);
        }
    }

    private void RefreshManagedPanels()
    {
        managedPanels.RemoveAll(item => item == null);
        hudObjects.RemoveAll(item => item == null);

        foreach (SettingsManager manager in FindObjectsOfType<SettingsManager>(true))
            AddUnique(managedPanels, manager.settingsPanel);

        foreach (PauseManager manager in FindObjectsOfType<PauseManager>(true))
        {
            AddUnique(managedPanels, manager.pausePanel);
            AddUnique(managedPanels, manager.settingsPanel);
        }

        foreach (AlmanacManager manager in FindObjectsOfType<AlmanacManager>(true))
            AddUnique(managedPanels, manager.Panel);

        foreach (LessonUIManager manager in FindObjectsOfType<LessonUIManager>(true))
            AddUnique(managedPanels, manager.Panel);

        foreach (AchievementUIManager manager in FindObjectsOfType<AchievementUIManager>(true))
            AddUnique(managedPanels, manager.achievementPanel);

        foreach (ShopManager manager in FindObjectsOfType<ShopManager>(true))
            AddUnique(managedPanels, manager.Panel);

        foreach (ObjectiveTrackerUI tracker in FindObjectsOfType<ObjectiveTrackerUI>(true))
            AddUnique(managedPanels, tracker.trackerPanel);

        // Interaction prompts live on their own Canvas in CanyonCrossing, so
        // disabling only the regular HUD did not change the container's active
        // state. Treat it as HUD so modal panels (especially Lessons) suppress
        // it and restore its exact previous state when they close.
        foreach (Transform candidate in FindObjectsOfType<Transform>(true))
        {
            if (candidate != null &&
                candidate.gameObject.scene.IsValid() &&
                candidate.name == "InteractionButtonContainer")
            {
                AddUnique(hudObjects, candidate.gameObject);
            }
        }
    }

    private static void AddUnique(List<GameObject> list, GameObject target)
    {
        if (target != null && !list.Contains(target))
            list.Add(target);
    }

    private static bool CanHideObject(GameObject target, GameObject panel)
    {
        if (target == null || panel == null || target == panel)
            return false;

        // Never disable an ancestor of the requested panel; doing so would also
        // make the requested panel disappear.
        return !panel.transform.IsChildOf(target.transform);
    }

    private static void HideSameCanvasSiblings(
        GameObject panel,
        PanelFrame frame,
        HashSet<GameObject> recorded)
    {
        Canvas owner = panel.GetComponentInParent<Canvas>(true);
        if (owner == null || panel.transform == owner.transform)
            return;

        Transform activeBranch = panel.transform;
        while (activeBranch.parent != null)
        {
            Transform parent = activeBranch.parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling == activeBranch || !sibling.gameObject.activeSelf)
                    continue;

                GameObject siblingObject = sibling.gameObject;
                if (!(siblingObject.transform is RectTransform) || !recorded.Add(siblingObject))
                    continue;

                frame.previousStates.Add(new ObjectState
                {
                    target = siblingObject,
                    wasActive = true
                });
                siblingObject.SetActive(false);
            }

            if (parent == owner.transform)
                break;
            activeBranch = parent;
        }
    }

    private static void HideOtherCanvases(GameObject panel, PanelFrame frame)
    {
        foreach (Canvas canvas in FindObjectsOfType<Canvas>(true))
        {
            if (canvas == null || !canvas.enabled || !canvas.gameObject.activeInHierarchy)
                continue;

            // Achievement toasts are a global overlay and must remain visible
            // even when Level Complete, Almanac, Lessons, or Settings is open.
            if (canvas.GetComponent<AchievementPopupNotification>() != null)
                continue;

            Transform canvasTransform = canvas.transform;
            bool containsPanel = panel.transform == canvasTransform ||
                                 panel.transform.IsChildOf(canvasTransform);
            bool belongsToPanel = canvasTransform.IsChildOf(panel.transform);
            if (containsPanel || belongsToPanel)
                continue;

            frame.previousCanvasStates.Add(new CanvasState
            {
                target = canvas,
                wasEnabled = true
            });
            canvas.enabled = false;
        }
    }

    private static void EnableTargetCanvases(GameObject panel, PanelFrame frame)
    {
        Canvas[] targetCanvases = panel.GetComponentsInParent<Canvas>(true);
        foreach (Canvas canvas in targetCanvases)
        {
            if (canvas == null || canvas.enabled)
                continue;

            frame.previousCanvasStates.Add(new CanvasState
            {
                target = canvas,
                wasEnabled = false
            });
            canvas.enabled = true;
        }
    }

    private static void EnableTargetParentChain(
        GameObject panel,
        PanelFrame frame,
        HashSet<GameObject> recorded)
    {
        Canvas owner = panel.GetComponentInParent<Canvas>(true);
        Transform current = panel.transform.parent;

        while (current != null)
        {
            GameObject parentObject = current.gameObject;
            if (!parentObject.activeSelf)
            {
                if (recorded.Add(parentObject))
                {
                    frame.previousStates.Add(new ObjectState
                    {
                        target = parentObject,
                        wasActive = false
                    });
                }

                parentObject.SetActive(true);
            }

            if (owner != null && current == owner.transform)
                break;

            current = current.parent;
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

    private static void RestoreCanvasStates(List<CanvasState> states)
    {
        foreach (CanvasState state in states)
        {
            if (state.target != null)
                state.target.enabled = state.wasEnabled;
        }
    }
}
