using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactionRadius = 3f;
    [SerializeField] private LayerMask mask;

    private PlayerUI playerUI;
    private Coroutine scanRoutine; // --- NEW: Tracks the physics scanning coroutine ---

    void Awake()
    {
        playerUI = GetComponent<PlayerUI>();
    }

    void Start()
    {
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

    // --- NEW: Start and Stop the scanning coroutine cleanly ---
    private void OnEnable()
    {
        scanRoutine = StartCoroutine(ScanForInteractablesRoutine());
    }

    private void OnDisable()
    {
        if (scanRoutine != null) StopCoroutine(scanRoutine);
    }

    private void HandleEnterBuildMode()
    {
        if (playerUI != null) playerUI.UpdateButtons(new List<Interactable>());
        this.enabled = false; // This automatically triggers OnDisable() and stops the coroutine!
    }

    private void HandleExitBuildMode()
    {
        this.enabled = true; // This automatically triggers OnEnable() and starts the coroutine!
    }

    // --- THE FIX: We only run Physics.OverlapSphere 10 times a second instead of 60-144 times! ---
    private IEnumerator ScanForInteractablesRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(0.1f); 

        while (true)
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

            yield return wait;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}