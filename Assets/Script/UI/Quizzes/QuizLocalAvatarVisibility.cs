using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps the local avatar out of one borrowed quiz shot, not out of the world.
/// Original renderer flags are restored between cameras and at every teardown.
/// </summary>
public sealed class QuizLocalAvatarVisibility
{
    private readonly List<Renderer> candidates = new List<Renderer>(48);
    private readonly List<Renderer> avatarRenderers = new List<Renderer>(48);
    private readonly List<Camera> cameraStack = new List<Camera>(4);
    private bool[] forceOffBefore;
    private Camera ownedCamera;
    private Transform visualRoot;
    private bool scopeApplied;

    public bool Begin(Camera camera, Transform avatarVisualRoot)
    {
        End();
        if (camera == null || avatarVisualRoot == null) return false;
        avatarVisualRoot.GetComponentsInChildren(true, candidates);
        foreach (Renderer visual in candidates)
        {
            // Held cargo can be parented into the hand rig. It is not the
            // player's appearance, and neither are particles/lines/world UI.
            if (visual == null || (!(visual is MeshRenderer) && !(visual is SkinnedMeshRenderer)) ||
                visual.GetComponentInParent<CargoItem>(true) != null) continue;
            avatarRenderers.Add(visual);
        }
        candidates.Clear();
        if (avatarRenderers.Count == 0) return false;
        ownedCamera = camera;
        visualRoot = avatarVisualRoot;
        forceOffBefore = new bool[avatarRenderers.Count];
        RenderPipelineManager.beginCameraRendering += BeforeScriptableCamera;
        RenderPipelineManager.endCameraRendering += AfterScriptableCamera;
        RenderPipelineManager.endFrameRendering += AfterScriptableFrame;
        Camera.onPreCull += BeforeBuiltInCamera;
        Camera.onPostRender += AfterBuiltInCamera;
        return true;
    }

    /// <summary>Recover an interrupted render before the next frame's cameras.</summary>
    public void PrepareForNewFrame()
    {
        RestoreScope();
        cameraStack.Clear();
    }

    public void End()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeScriptableCamera;
        RenderPipelineManager.endCameraRendering -= AfterScriptableCamera;
        RenderPipelineManager.endFrameRendering -= AfterScriptableFrame;
        Camera.onPreCull -= BeforeBuiltInCamera;
        Camera.onPostRender -= AfterBuiltInCamera;
        PrepareForNewFrame();
        ownedCamera = null;
        visualRoot = null;
        forceOffBefore = null;
        avatarRenderers.Clear();
        candidates.Clear();
    }

    private void BeforeScriptableCamera(ScriptableRenderContext context, Camera camera)
    {
        if (GraphicsSettings.currentRenderPipeline != null) BeforeCamera(camera);
    }

    private void AfterScriptableCamera(ScriptableRenderContext context, Camera camera)
    {
        if (GraphicsSettings.currentRenderPipeline != null) AfterCamera(camera);
    }

    private void AfterScriptableFrame(ScriptableRenderContext context, Camera[] cameras) => PrepareForNewFrame();

    private void BeforeBuiltInCamera(Camera camera)
    {
        if (GraphicsSettings.currentRenderPipeline == null) BeforeCamera(camera);
    }

    private void AfterBuiltInCamera(Camera camera)
    {
        if (GraphicsSettings.currentRenderPipeline == null) AfterCamera(camera);
    }

    private void BeforeCamera(Camera camera)
    {
        if (ownedCamera == null || visualRoot == null) { End(); return; }
        if (camera == null) return;
        // Restore while a nested map/portrait camera renders, then resume the
        // outer quiz scope on its return. No other camera inherits hidden meshes.
        RestoreScope();
        cameraStack.Add(camera);
        if (camera == ownedCamera) ApplyScope();
    }

    private void AfterCamera(Camera camera)
    {
        int index = cameraStack.LastIndexOf(camera);
        if (index < 0) return;
        RestoreScope();
        cameraStack.RemoveRange(index, cameraStack.Count - index);
        if (cameraStack.Count > 0 && cameraStack[cameraStack.Count - 1] == ownedCamera) ApplyScope();
    }

    private void ApplyScope()
    {
        if (scopeApplied || ownedCamera == null || visualRoot == null) return;
        for (int i = 0; i < avatarRenderers.Count; i++)
        {
            Renderer visual = avatarRenderers[i];
            if (visual == null) continue;
            forceOffBefore[i] = visual.forceRenderingOff;
            visual.forceRenderingOff = true;
        }
        scopeApplied = true;
    }

    private void RestoreScope()
    {
        if (!scopeApplied) return;
        scopeApplied = false;
        for (int i = 0; i < avatarRenderers.Count; i++)
            if (avatarRenderers[i] != null) avatarRenderers[i].forceRenderingOff = forceOffBefore[i];
    }

    /// <summary>Resolve only this player's cosmetic model, never remote avatars or scene scenery.</summary>
    public static Transform ResolveLocalVisual(PlayerLook look)
    {
        if (look == null) return null;
        PlayerMotor motor = look.GetComponent<PlayerMotor>();
        Transform model = motor != null && motor.playerAnimator != null ? motor.playerAnimator.transform : null;
        if (model != null && model != look.transform && model.IsChildOf(look.transform))
        {
            while (model.parent != null && model.parent != look.transform) model = model.parent;
            if (model.parent == look.transform) return model;
        }
        // Support legacy scenes before their visual-upgrade wiring has run.
        model = look.transform.Find("NewCharacterModel");
        if (model == null) model = look.transform.Find("NewCharacterModel (Player)");
        if (model == null) model = look.transform.Find("Character_w_Clothes_v4");
        return model;
    }
}
