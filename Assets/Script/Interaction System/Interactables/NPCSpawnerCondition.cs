using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class NPCSpawnerCondition : MonoBehaviour
{
    public enum ConditionKind { LessonCompleted, ContractCompleted, NPCAtPhase, FeatureUnlocked }
    public enum MatchMode { All, Any }

    [Serializable]
    public sealed class Condition
    {
        public ConditionKind kind;
        [Tooltip("Lesson/tutorial name, feature ID, or NPC progression save ID (e.g. MainContractNPC).")]
        public string id;
        public ContractSO contract;
        [Tooltip("Exact phase ID, not a list index. Only matches after arrival, not while travelling.")]
        public string phaseId;
        public bool mustBeTrue = true;

        public bool Matches(PlayerDataManager data)
        {
            bool result;
            string key = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
            switch (kind)
            {
                case ConditionKind.LessonCompleted:
                    if (key.Length == 0) return false;
                    result = data.CurrentData.completedLessons != null && data.CurrentData.completedLessons.Contains(key);
                    break;
                case ConditionKind.ContractCompleted:
                    if (contract == null) return false;
                    result = data.HasContractCompletionRecord(contract.ContractID);
                    break;
                case ConditionKind.NPCAtPhase:
                    if (key.Length == 0 || string.IsNullOrWhiteSpace(phaseId)) return false;
                    result = data.TryGetNPCProgression(key, out NPCProgressionSaveData state) &&
                        !state.wasTravelling && string.Equals(state.currentPhaseId, phaseId.Trim(), StringComparison.Ordinal);
                    break;
                case ConditionKind.FeatureUnlocked:
                    if (key.Length == 0) return false;
                    result = data.IsFeatureUnlocked(key);
                    break;
                default: return false;
            }
            return mustBeTrue ? result : !result;
        }
    }

    [Header("Spawn Condition")]
    [Tooltip("The exact name of the lesson/tutorial that must be completed to trigger this.")]
    public string requiredLessonName = "HouseTutorial";

    [Tooltip("If TRUE, the NPC spawns OUTSIDE after the tutorial. If FALSE, the NPC stays hidden INSIDE after the tutorial.")]
    public bool appearAfterLesson = true;

    [Header("Flexible Rules (optional)")]
    [Tooltip("Off preserves the existing Required Lesson / Appear After Lesson setup.")]
    public bool useMultipleConditions;
    public MatchMode matchMode = MatchMode.All;
    public List<Condition> conditions = new List<Condition>();
    [Tooltip("When enabled, matched rules hide the NPC instead of showing it.")]
    public bool hideWhenMatched;
    [Tooltip("Recheck while playing, including when this NPC is inactive.")]
    public bool updateAutomatically = true;

    [Tooltip("Start hidden and wait for a dialogue/event to call SpawnNPC. Bypasses automatic spawn rules.")]
    public bool waitForManualSpawn;
    [Tooltip("Save manual Spawn/Despawn overrides with player progress. Use Conditions clears the saved override.")]
    public bool persistManualOverride;
    [Tooltip("Unique, stable key for this NPC's saved visibility. Required when persistence is enabled.")]
    public string visibilitySaveId;

    [Header("Despawn Rules (optional; take priority)")]
    public bool useDespawnConditions;
    public MatchMode despawnMatchMode = MatchMode.Any;
    public List<Condition> despawnConditions = new List<Condition>();

    [Header("Controlled Object")]
    [Tooltip("Optional NPC root to show/hide. Empty controls this GameObject. Does not instantiate a prefab.")]
    public GameObject npcRoot;

    [Header("Visibility Events")]
    public UnityEvent onShown = new UnityEvent();
    public UnityEvent onHidden = new UnityEvent();

    private bool evaluated;
    private bool lastRuleVisibility;
    private int manualOverride; // 0 rules, 1 shown, -1 hidden.
    private PlayerData restoredVisibilityData;
    private bool pendingVisibilitySave;
    public bool HasManualOverride => manualOverride != 0;
    public bool IsShown => (npcRoot != null ? npcRoot : gameObject).activeSelf;

    private void OnEnable() { NPCSpawnConditionMonitor.Register(this); }
    private void Start() { EvaluateNow(); }
    private void OnDestroy() { NPCSpawnConditionMonitor.Unregister(this); }

    // UnityEvent-friendly methods. Overrides last until UseConditions is called.
    public void ShowNPC() { SetManualOverride(1); ApplyVisibility(true); }
    public void HideNPC() { SetManualOverride(-1); ApplyVisibility(false); }
    public void SpawnNPC() { ShowNPC(); }
    public void DespawnNPC() { HideNPC(); }
    public void SetNPCVisible(bool visible) { if (visible) ShowNPC(); else HideNPC(); }
    public void UseConditions() { SetManualOverride(0); evaluated = false; EvaluateNow(); }
    public void EvaluateNow() { Evaluate(true); }

    internal void TickConditions()
    {
        if (!evaluated || updateAutomatically || persistManualOverride) Evaluate(false);
    }

    private void Evaluate(bool force)
    {
        // enabled remains true when the GameObject is hidden. Debug warping
        // deliberately disables this component and must still bypass the gate.
        if (!enabled) return;
        PlayerDataManager data = PlayerDataManager.Instance;
        if (data == null || data.CurrentData == null) return;
        RestoreVisibility(data);
        if (manualOverride != 0) return;
        if (!force && evaluated && !updateAutomatically) return;

        bool visible;
        if (waitForManualSpawn) visible = false;
        else if (!useMultipleConditions)
        {
            bool done = data.CurrentData.completedLessons != null &&
                data.CurrentData.completedLessons.Contains(requiredLessonName);
            visible = appearAfterLesson ? done : !done;
        }
        else
        {
            // An empty advanced list is incomplete configuration, never auto-show.
            bool matched = MatchesRules(conditions, matchMode, data);
            visible = conditions != null && conditions.Count > 0 && (hideWhenMatched ? !matched : matched);
        }
        if (useDespawnConditions && MatchesRules(despawnConditions, despawnMatchMode, data))
            visible = false;
        bool changed = !evaluated || visible != lastRuleVisibility;
        evaluated = true;
        lastRuleVisibility = visible;
        // Do not repeatedly revive NPCs hidden by an arrival/story event.
        if (force || changed) ApplyVisibility(visible);
    }

    private bool CanPersist => persistManualOverride && !string.IsNullOrWhiteSpace(visibilitySaveId);

    private void SetManualOverride(int value)
    {
        manualOverride = value;
        pendingVisibilitySave = CanPersist;
        PlayerDataManager data = PlayerDataManager.Instance;
        if (pendingVisibilitySave && data != null && data.CurrentData != null)
            SaveVisibility(data);
    }

    private void SaveVisibility(PlayerDataManager data)
    {
        PlayerData save = data.CurrentData;
        if (save.npcVisibility == null) save.npcVisibility = new List<NPCVisibilitySaveData>();
        string key = visibilitySaveId.Trim();
        save.npcVisibility.RemoveAll(entry => entry != null && entry.visibilityId == key);
        if (manualOverride != 0)
            save.npcVisibility.Add(new NPCVisibilitySaveData { visibilityId = key, visible = manualOverride > 0 });
        restoredVisibilityData = save;
        pendingVisibilitySave = false;
        data.SaveGame();
    }

    private void RestoreVisibility(PlayerDataManager data)
    {
        if (!CanPersist) return;
        if (pendingVisibilitySave) { SaveVisibility(data); return; }
        if (ReferenceEquals(restoredVisibilityData, data.CurrentData)) return;
        restoredVisibilityData = data.CurrentData;
        NPCVisibilitySaveData record = data.CurrentData.npcVisibility?.Find(entry =>
            entry != null && entry.visibilityId == visibilitySaveId.Trim());
        manualOverride = record == null ? 0 : record.visible ? 1 : -1;
        evaluated = false;
        // Restore once per loaded save, not every tick: never fight story/debug visibility.
        if (record != null) ApplyVisibility(record.visible);
    }

    private static bool MatchesRules(List<Condition> rules, MatchMode mode, PlayerDataManager data)
    {
        if (rules == null || rules.Count == 0) return false;
        foreach (Condition rule in rules)
        {
            bool passes = rule != null && rule.Matches(data);
            if (mode == MatchMode.All && !passes) return false;
            if (mode == MatchMode.Any && passes) return true;
        }
        return mode == MatchMode.All;
    }

    private void ApplyVisibility(bool visible)
    {
        GameObject target = npcRoot != null ? npcRoot : gameObject;
        if (target.activeSelf == visible) return;
        target.SetActive(visible);
        if (visible) onShown?.Invoke(); else onHidden?.Invoke();
    }
}
