using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic visibility gate for any permanently unlockable feature. Attach this to
/// an always-existing object, enter the same feature ID used by a contract reward,
/// and assign the objects that should appear or disappear after it is earned.
/// </summary>
[DisallowMultipleComponent]
public sealed class PersistentFeatureGate : MonoBehaviour
{
    [SerializeField, Tooltip("Stable ID used by the contract reward, for example: shop, minimap, or photo_mode.")]
    private string featureId;

    [SerializeField, Tooltip("If empty, this GameObject is used as the reveal target.")]
    private List<GameObject> revealWhenUnlocked = new List<GameObject>();

    [SerializeField, Tooltip("Optional locked visuals that disappear after the feature is earned.")]
    private List<GameObject> hideWhenUnlocked = new List<GameObject>();

    private bool subscribed;

    private void Start()
    {
        Subscribe();
        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (!subscribed || PlayerDataManager.Instance == null) return;
        PlayerDataManager.Instance.OnFeatureUnlocksChanged -= RefreshVisibility;
        subscribed = false;
    }

    public void RefreshVisibility()
    {
        bool unlocked = PlayerDataManager.Instance != null &&
                        PlayerDataManager.Instance.IsFeatureUnlocked(featureId);

        if (revealWhenUnlocked == null || revealWhenUnlocked.Count == 0)
        {
            if (gameObject.activeSelf != unlocked)
                gameObject.SetActive(unlocked);
        }
        else
        {
            SetObjectsActive(revealWhenUnlocked, unlocked);
        }

        SetObjectsActive(hideWhenUnlocked, !unlocked);
    }

    /// <summary>Inspector-callable alternative for pickups, dialogue, or tutorials.</summary>
    public void UnlockFeature()
    {
        if (PlayerDataManager.Instance == null)
        {
            Debug.LogWarning("[PersistentFeatureGate] PlayerDataManager is unavailable.", this);
            return;
        }

        PlayerDataManager.Instance.UnlockFeature(featureId);
        RefreshVisibility();
    }

    private void Subscribe()
    {
        if (subscribed || PlayerDataManager.Instance == null) return;
        PlayerDataManager.Instance.OnFeatureUnlocksChanged += RefreshVisibility;
        subscribed = true;
    }

    private static void SetObjectsActive(List<GameObject> objects, bool active)
    {
        if (objects == null) return;
        foreach (GameObject target in objects)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }
    }
}
