using System.Collections.Generic;
using UnityEngine;

/// <summary>Dialogue's own visibility mask; feature and modal owners retain control of activeSelf.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10000)]
public sealed class DialogueHudButtonGate : MonoBehaviour
{
    private static readonly HashSet<DialogueHudButtonGate> gates = new HashSet<DialogueHudButtonGate>();
    [SerializeField, HideInInspector] private CanvasGroup dialogueMask;
    [SerializeField, HideInInspector] private AlmanacManager almanac;
    [SerializeField, HideInInspector] private ObjectiveTrackerUI objectives;
    [SerializeField, HideInInspector] private ShopButtonTrigger shop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() { gates.Clear(); }

    public static void BindAlmanac(GameObject button, AlmanacManager owner)
    {
        if (button == null || owner == null) return;
        DialogueHudButtonGate gate = GetGate(button);
        gate.almanac = owner; gate.objectives = null; gate.shop = null;
        gate.ApplyVisibility();
    }

    public static void BindObjectives(GameObject button, ObjectiveTrackerUI owner)
    {
        if (button == null || owner == null) return;
        DialogueHudButtonGate gate = GetGate(button);
        gate.objectives = owner; gate.almanac = null; gate.shop = null;
        gate.ApplyVisibility();
    }

    public static void BindShop(GameObject button, ShopButtonTrigger owner)
    {
        if (button == null || owner == null) return;
        DialogueHudButtonGate gate = GetGate(button);
        gate.shop = owner; gate.almanac = null; gate.objectives = null;
        gate.ApplyVisibility();
    }

    private static DialogueHudButtonGate GetGate(GameObject button)
    {
        DialogueHudButtonGate gate = button.GetComponent<DialogueHudButtonGate>();
        if (gate == null) gate = button.AddComponent<DialogueHudButtonGate>();
        gates.Add(gate);
        return gate;
    }

    public static bool IsBoundButton(GameObject button)
    {
        if (button == null) return false;
        DialogueHudButtonGate gate = button.GetComponent<DialogueHudButtonGate>();
        return gate != null && (gate.almanac != null || gate.objectives != null || gate.shop != null);
    }

    public static void RefreshAll()
    {
        gates.RemoveWhere(gate => gate == null);
        foreach (DialogueHudButtonGate gate in gates)
            if (gate.isActiveAndEnabled) gate.ApplyVisibility();
    }

    private void OnEnable() { gates.Add(this); ApplyVisibility(); }
    private void LateUpdate() { gates.Add(this); ApplyVisibility(); }
    private void OnDisable() { if (dialogueMask != null) SetMask(false); }
    private void OnDestroy() { gates.Remove(this); if (dialogueMask != null) SetMask(false); }

    private void ApplyVisibility()
    {
        bool allowed = almanac != null && AlmanacManager.Instance == almanac &&
            almanac.hudOpenButton == gameObject && almanac.CanShowHudOpenButton;
        allowed |= objectives != null && ObjectiveTrackerUI.Instance == objectives &&
            objectives.openTrackerButton == gameObject && objectives.CanShowHudEntry;
        allowed |= shop != null && shop.gameObject == gameObject && shop.CanShowHudEntry;
        SetMask(DialogueManager.IsAnyDialogueVisible || !allowed);
    }

    private void SetMask(bool hidden)
    {
        if (dialogueMask == null)
        {
            // A separate group avoids snapshotting or overwriting an authored
            // fade, lock, or modal group's state when the conversation ends.
            dialogueMask = gameObject.AddComponent<CanvasGroup>();
            dialogueMask.hideFlags = HideFlags.HideInInspector;
            dialogueMask.ignoreParentGroups = false;
        }
        float alpha = hidden ? 0f : 1f;
        if (dialogueMask.alpha != alpha) dialogueMask.alpha = alpha;
        if (dialogueMask.interactable == hidden) dialogueMask.interactable = !hidden;
        if (dialogueMask.blocksRaycasts == hidden) dialogueMask.blocksRaycasts = !hidden;
    }
}
