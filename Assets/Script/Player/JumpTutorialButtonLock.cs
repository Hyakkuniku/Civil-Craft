using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Gives the existing Jump button the same disabled ColorTint as Run while a
/// tutorial locks jumping. Does not replace its click event or input authority.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class JumpTutorialButtonLock : MonoBehaviour
{
    // A separate group owns only this tutorial restriction. Never overwrite the
    // Button's interactable flag or another panel's CanvasGroup when releasing it.
    private CanvasGroup tutorialGate;
    private bool wasLocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneBootstrap()
    {
        // Remove first because Enter Play Mode may keep the scripting domain.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BindLoadedScenes()
    {
        // Also handles Enter Play Mode with scene reload disabled. Binding is
        // idempotent, so the initial sceneLoaded notification cannot duplicate it.
        for (int i = 0; i < SceneManager.sceneCount; i++)
            BindScene(SceneManager.GetSceneAt(i));
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindScene(scene);
    }

    private static void BindScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                if (!InvokesPlayerJump(button)) continue;
                JumpTutorialButtonLock visual = button.GetComponent<JumpTutorialButtonLock>();
                if (visual == null) visual = button.gameObject.AddComponent<JumpTutorialButtonLock>();
                if (visual.isActiveAndEnabled) visual.RefreshLock();
            }
        }
    }

    private static bool InvokesPlayerJump(Button button)
    {
        // Inspector wiring, not the object's name, identifies the authored Jump
        // control. Keep its event untouched, including a legacy missing target.
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            if (button.onClick.GetPersistentMethodName(i) != nameof(PlayerMotor.Jump)) continue;
            Object target = button.onClick.GetPersistentTarget(i);
            if (target == null || target is PlayerMotor) return true;
        }
        return false;
    }

    private void OnEnable()
    {
        RefreshLock();
    }

    private void LateUpdate()
    {
        // No hierarchy searches, allocations, or time-scale dependency per frame.
        bool locked = TutorialManager.Instance != null && TutorialManager.Instance.IsJumpLocked;
        if (locked != wasLocked || (locked && tutorialGate == null)) ApplyLock(locked);
    }

    private void RefreshLock()
    {
        ApplyLock(TutorialManager.Instance != null && TutorialManager.Instance.IsJumpLocked);
    }

    private void ApplyLock(bool locked)
    {
        wasLocked = locked;
        if (wasLocked && tutorialGate == null)
        {
            tutorialGate = gameObject.AddComponent<CanvasGroup>();
            tutorialGate.alpha = 1f;
            tutorialGate.interactable = false;
            tutorialGate.blocksRaycasts = true;
            tutorialGate.ignoreParentGroups = false;
        }
        // Selectable derives its Disabled state from CanvasGroups, preserving
        // the authored gray/half-alpha ColorBlock exactly like Run's lock tint.
        if (tutorialGate != null) tutorialGate.enabled = wasLocked;
    }

    private void OnDisable()
    {
        if (tutorialGate != null) tutorialGate.enabled = false;
        wasLocked = false;
    }

    private void OnDestroy()
    {
        if (tutorialGate == null) return;
        tutorialGate.enabled = false;
        if (Application.isPlaying) Destroy(tutorialGate);
        else DestroyImmediate(tutorialGate);
    }
}
