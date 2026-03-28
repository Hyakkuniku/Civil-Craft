using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactionRadius = 3f;
    [SerializeField] private LayerMask mask;
    
    [Tooltip("The maximum number of interactables the player can stand near at exactly the same time.")]
    [SerializeField] private int maxInteractables = 10; 

    private PlayerUI playerUI;
    private Coroutine scanRoutine; 

    // --- NEW: Pre-allocate memory so we don't generate Garbage! ---
    private Collider[] hitColliders;
    private List<Interactable> nearbyInteractables;

    void Awake()
    {
        playerUI = GetComponent<PlayerUI>();
        
        // Create the memory buckets ONCE when the game starts
        hitColliders = new Collider[maxInteractables];
        nearbyInteractables = new List<Interactable>(maxInteractables);
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
        this.enabled = false; 
    }

    private void HandleExitBuildMode()
    {
        this.enabled = true; 
    }

    private IEnumerator ScanForInteractablesRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(0.1f); 

        while (true)
        {
            // 1. Clear the reusable list instead of making a new one
            nearbyInteractables.Clear();

            // 2. Use NonAlloc! This fills our existing array instead of creating a new one in RAM.
            int numColliders = Physics.OverlapSphereNonAlloc(transform.position, interactionRadius, hitColliders, mask);

            // 3. Only loop through the ones we actually hit
            for (int i = 0; i < numColliders; i++)
            {
                Interactable interactable = hitColliders[i].GetComponent<Interactable>();
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