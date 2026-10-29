using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Records checked, scene-authored story guidance without playing tutorials or advancing story.</summary>
[DisallowMultipleComponent]
public sealed class NPCStoryObjectiveGuidance : MonoBehaviour
{
    [Serializable]
    public sealed class Entry
    {
        public string phaseId;
        public TutorialSequence objective;
        [Tooltip("For a terminal conversation that stays in the same phase after it finishes.")]
        public bool completeAfterDialogue;
        [Tooltip("Optional already-granted feature required before this terminal objective can be completed.")]
        public string completionFeatureId;
    }

    [SerializeField] private NPCProgressionManager npc;
    public List<Entry> entries = new List<Entry>();
    public static int Version { get; private set; }

    private static readonly HashSet<NPCStoryObjectiveGuidance> registry = new HashSet<NPCStoryObjectiveGuidance>();
    private readonly List<Renderer> renderers = new List<Renderer>();
    private PlayerDataManager observedManager;
    private PlayerData observedData;
    private NPCProgressionPhase observedPhase;
    private int observedPhaseIndex = -1;
    private bool observedAuthorized, observedTerminalCompleted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() { registry.Clear(); unchecked { Version++; } }

    private void OnEnable()
    {
        if (npc == null) npc = GetComponent<NPCProgressionManager>();
        renderers.Clear();
        if (npc != null) npc.GetComponentsInChildren(true, renderers);
        observedAuthorized = false;
        Register(); Synchronize();
    }
    private void LateUpdate() { Register(); Synchronize(); }
    private void Register() { if (registry.Add(this)) unchecked { Version++; } }
    private void OnDisable() { Unregister(); }
    private void OnDestroy() { Unregister(); }
    private void Unregister()
    {
        observedAuthorized = false;
        if (registry.Remove(this)) unchecked { Version++; }
    }

    private void Synchronize()
    {
        PlayerDataManager manager = PlayerDataManager.Instance;
        PlayerData data = manager != null ? manager.CurrentData : null;
        bool authorized = Authorized(manager, data, out int phaseIndex);
        NPCProgressionPhase phase = authorized ? npc.AuthoredPhases[phaseIndex] : null;
        bool terminalCompleted = authorized && phaseIndex == npc.CurrentPhaseIndex &&
            IsTerminalPhaseComplete(phase, manager);
        if (observedManager == manager && ReferenceEquals(observedData, data) && observedPhase == phase &&
            observedPhaseIndex == phaseIndex && observedAuthorized == authorized && observedTerminalCompleted == terminalCompleted) return;
        observedManager = manager; observedData = data; observedPhase = phase; observedPhaseIndex = phaseIndex;
        observedAuthorized = authorized; observedTerminalCompleted = terminalCompleted;
        unchecked { Version++; }
        if (!authorized || entries == null) return;

        bool changed = false, hasCheckedObjective = false, newCurrent = false;
        foreach (Entry entry in entries)
        {
            if (!Usable(entry)) continue;
            int index = PhaseIndex(entry.phaseId);
            if (index < 0 || index > phaseIndex) continue;
            hasCheckedObjective = true;
            if (index < phaseIndex || (entry.completeAfterDialogue && index == phaseIndex && terminalCompleted))
                changed |= TutorialObjectiveHistory.Complete(entry.objective, data);
            else
            {
                TutorialObjectiveHistory.Begin(entry.objective, data, out bool added);
                changed |= added;
                newCurrent |= added;
                if (entry.objective.tutorialSteps != null && entry.objective.tutorialSteps.Length > 0)
                    changed |= TutorialObjectiveHistory.RecordStep(entry.objective, entry.objective.tutorialSteps[0], data);
            }
        }
        if (hasCheckedObjective && !data.hasUnlockedObjectiveTracker)
        { data.hasUnlockedObjectiveTracker = true; changed = true; }
        if (changed)
        {
            unchecked { Version++; }
            bool trackerOpen = ObjectiveTrackerUI.Instance != null && ObjectiveTrackerUI.Instance.trackerPanel != null &&
                ObjectiveTrackerUI.Instance.trackerPanel.activeSelf;
            // Alerting already saves and notifies the existing HUD; use one save for this batch.
            if (newCurrent && !trackerOpen) manager.MarkObjectiveAlertUnread();
            else manager.SaveGame();
        }
    }

    private bool Authorized(PlayerDataManager manager, PlayerData data, out int effectivePhaseIndex)
    {
        effectivePhaseIndex = -1;
        if (!Application.isPlaying || !isActiveAndEnabled || npc == null || !npc.isActiveAndEnabled ||
            !gameObject.scene.IsValid() || !gameObject.scene.isLoaded || npc.gameObject.scene != gameObject.scene ||
            npc.CurrentPhase == null || npc.CurrentPhaseIndex < 0 || manager == null || data == null ||
            !ReferenceEquals(manager.CurrentData, data) || ProtectedVisit() || !HasPresentVisual()) return false;
        // A profile switch must not project the previous player's restored NPC into the new save.
        if (!manager.TryGetNPCProgression(npc.ProgressionSaveId, out NPCProgressionSaveData saved) || saved == null) return false;
        int index = saved.currentPhaseIndex;
        if (npc.AuthoredPhases == null || index < 0 || index >= npc.AuthoredPhases.Count || npc.AuthoredPhases[index] == null ||
            !string.Equals(saved.currentPhaseId, npc.AuthoredPhases[index].phaseId, StringComparison.Ordinal)) return false;
        bool settled = index == npc.CurrentPhaseIndex && string.Equals(saved.currentPhaseId, npc.CurrentPhase.phaseId, StringComparison.Ordinal);
        // These audited story routes commit the next destination before walking.
        // Its saved phase is durable proof that the preceding conversation ended.
        bool committedTravel = npc.IsTravelling && saved.wasTravelling && index == npc.CurrentPhaseIndex + 1;
        if (!settled && !committedTravel) return false;
        effectivePhaseIndex = index;
        return true;
    }
    private bool HasPresentVisual()
    {
        if (!NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(npc.transform)) return false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                renderer.GetComponent<TMPro.TMP_Text>() != null) continue;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) return true;
            if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) return true;
        }
        return false;
    }
    private bool IsTerminalPhaseComplete(NPCProgressionPhase phase, PlayerDataManager manager)
    {
        if (phase == null || entries == null) return false;
        foreach (Entry entry in entries)
            if (Usable(entry) && entry.completeAfterDialogue && string.Equals(entry.phaseId, phase.phaseId, StringComparison.Ordinal) &&
                npc.HasCompletedPhaseDialogue(npc.CurrentPhaseIndex) &&
                (string.IsNullOrWhiteSpace(entry.completionFeatureId) || manager.IsFeatureUnlocked(entry.completionFeatureId))) return true;
        return false;
    }
    private bool Usable(Entry entry) => entry != null && entry.objective != null && entry.objective.showInObjectives &&
        entry.objective.gameObject.scene == gameObject.scene && entry.objective.gameObject.scene.isLoaded &&
        !string.IsNullOrWhiteSpace(entry.phaseId);
    private int PhaseIndex(string id)
    {
        if (npc == null || npc.AuthoredPhases == null) return -1;
        for (int i = 0; i < npc.AuthoredPhases.Count; i++)
            if (npc.AuthoredPhases[i] != null && string.Equals(npc.AuthoredPhases[i].phaseId, id, StringComparison.Ordinal)) return i;
        return -1;
    }
    private static bool ProtectedVisit() => FusionConnectionManager.Instance != null &&
        (FusionConnectionManager.Instance.IsGuestInHostWorld || FusionConnectionManager.Instance.IsGuestSaveProtected);

    public static bool CanNavigate(TrackedTask task) => TryTarget(task, out _);
    public static bool TryNavigate(TrackedTask task)
    {
        if (!TryTarget(task, out Transform target)) return false;
        PathGuider.Instance.RouteToSingleTarget(target); // A route never advances a tutorial or an NPC.
        return true;
    }
    private static bool TryTarget(TrackedTask task, out Transform target)
    {
        target = null;
        if (task == null || task.isCompleted || task.isReadyToTurnIn || !TutorialObjectiveHistory.IsSequenceTask(task) ||
            PathGuider.Instance == null || DialogueManager.IsAnyDialogueVisible || QuizUIManager.IsAnyQuizActive ||
            LoadingScreenManager.IsLoading || Time.timeScale <= 0f ||
            (PauseManager.Instance != null && PauseManager.Instance.isPaused) ||
            (GameManager.Instance != null && (GameManager.Instance.IsTransitioning || GameManager.Instance.CurrentState != GameManager.GameState.Normal)) ||
            (UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel &&
                (ObjectiveTrackerUI.Instance == null || UIPanelCoordinator.Instance.CurrentPanel != ObjectiveTrackerUI.Instance.trackerPanel))) return false;
        PlayerDataManager manager = PlayerDataManager.Instance;
        PlayerData data = manager != null ? manager.CurrentData : null;
        foreach (NPCStoryObjectiveGuidance owner in registry)
        {
            if (owner == null || !owner.Authorized(manager, data, out int phaseIndex) || owner.entries == null) continue;
            foreach (Entry entry in owner.entries)
            {
                if (!owner.Usable(entry) || !string.Equals(entry.phaseId, owner.npc.AuthoredPhases[phaseIndex].phaseId, StringComparison.Ordinal) ||
                    !ReferenceEquals(TutorialObjectiveHistory.Find(entry.objective, data), task) ||
                    entry.objective.tutorialSteps == null || entry.objective.tutorialSteps.Length == 0) continue;
                TutorialStep step = entry.objective.tutorialSteps[0];
                if (step?.stepWaypoints == null) continue;
                foreach (GuiderWaypoint point in step.stepWaypoints)
                {
                    if (point?.target == null || point.advancesTutorial || !point.target.gameObject.activeInHierarchy ||
                        point.target.gameObject.scene != owner.gameObject.scene || !NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(point.target)) continue;
                    target = point.target; return true;
                }
            }
        }
        return false;
    }
}
