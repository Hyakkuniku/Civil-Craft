using System.Collections;
using UnityEngine;

/// <summary>Displays the saved wardrobe on the Mode Selection presentation model.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerCosmeticMirror))]
public sealed class ModeSelectionCharacterPreview : MonoBehaviour
{
    private PlayerCosmeticMirror mirror;
    private PlayerDataManager savedPlayer;
    private Coroutine waitingForPlayer;

    private void OnEnable()
    {
        mirror = GetComponent<PlayerCosmeticMirror>();
        waitingForPlayer = StartCoroutine(BindSavedPlayer());
    }

    private IEnumerator BindSavedPlayer()
    {
        // Supports both entering from the main menu and opening this scene
        // directly, regardless of the save manager's Awake order.
        while (PlayerDataManager.Instance == null ||
               PlayerDataManager.Instance.CurrentData == null)
            yield return null;

        savedPlayer = PlayerDataManager.Instance;
        savedPlayer.OnCosmeticLookSaved += RefreshAppearance;
        // Account/cloud progress replacement also commits the new saved data.
        savedPlayer.OnSaveCommitted += RefreshAppearance;
        RefreshAppearance();
        waitingForPlayer = null;
    }

    private void Start()
    {
        Animator animator = GetComponentInChildren<Animator>(true);
        if (animator == null) return;

        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.SetFloat("Speed", 0f);
        animator.SetBool("IsGrounded", true);
        animator.SetBool("IsSprinting", false);
        if (animator.HasState(0, Animator.StringToHash("idle")))
            animator.Play("idle", 0, 0f);
    }

    private void RefreshAppearance()
    {
        if (mirror != null) mirror.RefreshCosmetics();
    }

    private void OnDisable()
    {
        if (waitingForPlayer != null)
        {
            StopCoroutine(waitingForPlayer);
            waitingForPlayer = null;
        }

        if (savedPlayer != null)
        {
            savedPlayer.OnCosmeticLookSaved -= RefreshAppearance;
            savedPlayer.OnSaveCommitted -= RefreshAppearance;
            savedPlayer = null;
        }
    }
}
