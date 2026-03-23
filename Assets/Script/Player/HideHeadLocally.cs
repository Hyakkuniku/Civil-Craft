using UnityEngine;
using UnityEngine.Rendering;

public class LocalHeadHider : MonoBehaviour
{
    [Header("Meshes to Hide Locally")]
    [Tooltip("Drag the Head, Hair, Eyes, Hat, etc. into this list.")]
    public Renderer[] headMeshesToHide;

    void Start()
    {
        // Automatically hide the head when the game starts (assuming we start in 1st person)
        // NOTE: In multiplayer, wrap this in an "if (isLocalPlayer)" check!
        HideHeadForFirstPerson();

        // --- NEW: Listen to the GameManager for Build Mode changes! ---
        if (GameManager.Instance != null)
        {
            // When we enter build mode, show the head!
            GameManager.Instance.OnEnterBuildMode.AddListener(ShowHeadForThirdPerson);
            
            // When we exit build mode, hide the head again!
            GameManager.Instance.OnExitBuildMode.AddListener(HideHeadForFirstPerson);
        }
    }

    void OnDestroy()
    {
        // --- NEW: Always clean up your listeners when the object is destroyed ---
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(ShowHeadForThirdPerson);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HideHeadForFirstPerson);
        }
    }

    // Call this when switching TO First-Person View (or returning from Build Mode)
    public void HideHeadForFirstPerson()
    {
        foreach (Renderer mesh in headMeshesToHide)
        {
            if (mesh != null)
            {
                // Makes the head invisible but keeps the shadow
                mesh.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }
    }

    // Call this when switching TO Third-Person View (or entering Build Mode)
    public void ShowHeadForThirdPerson()
    {
        foreach (Renderer mesh in headMeshesToHide)
        {
            if (mesh != null)
            {
                // Makes the head fully visible again!
                mesh.shadowCastingMode = ShadowCastingMode.On;
            }
        }
    }
}