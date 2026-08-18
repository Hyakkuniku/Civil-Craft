using UnityEngine;
using UnityEngine.Rendering;

public class LocalHeadHider : MonoBehaviour
{
    [Header("Meshes to Hide Locally")]
    [Tooltip("Drag the Head, Hair, Eyes, Hat, etc. into this list.")]
    public Renderer[] headMeshesToHide;

    [Header("Minimap Setup (No Layers Needed!)")]
    [Tooltip("Drag your MinimapCamera here. We will magically turn the head on just for this camera!")]
    public Camera minimapCamera;

    // We need to remember if the player is currently SUPPOSED to be in First Person
    private bool isCurrentlyInFirstPerson = true;

    void Start()
    {
        HideHeadForFirstPerson();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(ShowHeadForThirdPerson);
            GameManager.Instance.OnExitBuildMode.AddListener(HideHeadForFirstPerson);
        }
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(ShowHeadForThirdPerson);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HideHeadForFirstPerson);
        }
    }

    // --- THE MAGIC: Intercepting the cameras ---
    void OnEnable()
    {
        // Hooks for Unity's Built-In Pipeline
        Camera.onPreCull += OnCameraPreCull;
        Camera.onPostRender += OnCameraPostRender;
        
        // Hooks for Unity's URP/HDRP Pipeline
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    void OnDisable()
    {
        // Always clean up your listeners!
        Camera.onPreCull -= OnCameraPreCull;
        Camera.onPostRender -= OnCameraPostRender;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    // 1. Right before a camera renders, check if it's the Minimap. If yes, show the head!
    private void OnCameraPreCull(Camera cam) { if (cam == minimapCamera) ShowHeadInstantly(); }
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam) { if (cam == minimapCamera) ShowHeadInstantly(); }

    // 2. Right after the Minimap finishes, hide the head again (but ONLY if we are in 1st Person Mode!)
    private void OnCameraPostRender(Camera cam) { if (cam == minimapCamera && isCurrentlyInFirstPerson) HideHeadInstantly(); }
    private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam) { if (cam == minimapCamera && isCurrentlyInFirstPerson) HideHeadInstantly(); }


    // --- YOUR ORIGINAL LOGIC ---
    public void HideHeadForFirstPerson()
    {
        isCurrentlyInFirstPerson = true;
        HideHeadInstantly();
    }

    public void ShowHeadForThirdPerson()
    {
        isCurrentlyInFirstPerson = false;
        ShowHeadInstantly();
    }

    private void HideHeadInstantly()
    {
        foreach (Renderer mesh in headMeshesToHide)
        {
            if (mesh != null) mesh.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }

    private void ShowHeadInstantly()
    {
        foreach (Renderer mesh in headMeshesToHide)
        {
            if (mesh != null) mesh.shadowCastingMode = ShadowCastingMode.On;
        }
    }
}