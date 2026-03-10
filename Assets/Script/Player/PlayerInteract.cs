using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactionRadius = 3f;
    [SerializeField] private LayerMask mask;

    private PlayerUI playerUI;

    void Start()
    {
        playerUI = GetComponent<PlayerUI>();

        // NEW: Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HandleExitBuildMode);
        }
    }

    private void HandleEnterBuildMode()
    {
        // Instantly clear the screen of any interact buttons before disabling itself!
        if (playerUI != null) playerUI.UpdateButtons(new List<Interactable>());
        this.enabled = false;
    }

    private void HandleExitBuildMode()
    {
        this.enabled = true;
    }

    void Update()
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, interactionRadius, mask);
        List<Interactable> nearbyInteractables = new List<Interactable>();

        foreach (Collider col in nearbyColliders)
        {
            Interactable interactable = col.GetComponent<Interactable>();
            if (interactable != null)
            {
                nearbyInteractables.Add(interactable);
            }
        }

        if (playerUI != null)
        {
            playerUI.UpdateButtons(nearbyInteractables);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}