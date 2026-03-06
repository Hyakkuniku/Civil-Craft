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
    }

    void Update()
    {
        // Grab all colliders inside our invisible bubble
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, interactionRadius, mask);
        
        List<Interactable> nearbyInteractables = new List<Interactable>();

        // Loop through everything we caught and add it to our list
        foreach (Collider col in nearbyColliders)
        {
            Interactable interactable = col.GetComponent<Interactable>();
            if (interactable != null)
            {
                nearbyInteractables.Add(interactable);
            }
        }

        // Send the list of nearby objects to the UI to create buttons
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