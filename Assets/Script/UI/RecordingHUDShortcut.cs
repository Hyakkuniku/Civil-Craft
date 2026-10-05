using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>PC recording visibility only; never pauses gameplay or deactivates HUD controllers.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(20000)]
public sealed class RecordingHUDShortcut : MonoBehaviour
{
    private static RecordingHUDShortcut instance;
    private readonly HashSet<GameObject> hudTargets = new HashSet<GameObject>();
    private sealed class RendererState
    {
        public CanvasRenderer renderer;
        public Graphic graphic;
        public bool naturalCull;
        public bool masked;
    }
    private readonly Dictionary<CanvasRenderer, RendererState> masks = new Dictionary<CanvasRenderer, RendererState>();
    private readonly HashSet<CanvasRenderer> currentRenderers = new HashSet<CanvasRenderer>();
    private readonly List<CanvasRenderer> rendererScratch = new List<CanvasRenderer>();
    private readonly List<CanvasRenderer> obsoleteMasks = new List<CanvasRenderer>();
    private float nextRefresh;
    public bool HudHidden { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (instance != null) return;
        GameObject owner = UIPanelCoordinator.Instance != null
            ? UIPanelCoordinator.Instance.gameObject : new GameObject("Recording HUD Shortcut");
        if (owner.GetComponent<RecordingHUDShortcut>() == null)
            owner.AddComponent<RecordingHUDShortcut>();
#endif
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        DontDestroyOnLoad(transform.root.gameObject);
        RefreshHudTargets();
    }

    private void OnEnable()
    {
        // Register after uGUI's layout/material/clipping pass, so our draw mask
        // cannot be overwritten by that pass. Restore before the next pass.
        _ = CanvasUpdateRegistry.instance;
        SceneManager.sceneLoaded += SceneLoaded;
        Canvas.preWillRenderCanvases += PrepareRendering;
        Canvas.willRenderCanvases += ApplyVisibility;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        Canvas.preWillRenderCanvases -= PrepareRendering;
        Canvas.willRenderCanvases -= ApplyVisibility;
        ReleaseMasks();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The recording preference survives transitions, but scene UI references
        // and temporary draw masks must be rebuilt for the new scene.
        RefreshHudTargets();
        ApplyVisibility();
    }

    private void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && ShouldToggleShortcut(
            keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed,
            keyboard.hKey.wasPressedThisFrame, IsTextInputFocused()))
        {
            RefreshHudTargets();
            ToggleHud();
        }
#endif
        if (HudHidden && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.25f;
            RefreshHudTargets();
        }
    }

    private void LateUpdate() => ApplyVisibility();

    public void ToggleHud() => SetHudHidden(!HudHidden);

    public void SetHudHidden(bool hidden)
    {
        HudHidden = hidden;
        if (!hidden) ReleaseMasks();
        else ApplyVisibility();
    }

    internal static bool ShouldToggleShortcut(bool controlPressed, bool hPressed, bool typing)
        => controlPressed && hPressed && !typing;

    internal static bool IsTextInputFocused()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null) return false;
        TMP_InputField tmp = selected.GetComponentInParent<TMP_InputField>();
        if (tmp != null && tmp.isFocused) return true;
        InputField legacy = selected.GetComponentInParent<InputField>();
        return legacy != null && legacy.isFocused;
    }

    internal void RefreshHudTargets()
    {
        // Don't hide login, main-menu or mode-selection screens. These named
        // canvases are HUD only when they belong to an actual gameplay scene.
        var scenes = new HashSet<Scene>();
        foreach (GameManager manager in FindObjectsOfType<GameManager>(true))
            if (manager != null && manager.gameObject.scene.IsValid()) scenes.Add(manager.gameObject.scene);
        var canvases = new List<Canvas>();
        foreach (Canvas canvas in FindObjectsOfType<Canvas>(true))
            if (canvas != null && (scenes.Contains(canvas.gameObject.scene) ||
                (scenes.Count > 0 && canvas.GetComponent<AchievementPopupNotification>() != null)))
                canvases.Add(canvas);
        RefreshHudTargets(canvases.ToArray());
    }

    internal void RefreshHudTargets(Canvas[] canvases)
    {
        hudTargets.Clear();
        if (canvases != null)
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null) continue;
                Transform root = canvas.transform;
                switch (canvas.name)
                {
                    case "MainCanvas":
                        for (int i = 0; i < root.childCount; i++)
                        {
                            Transform child = root.GetChild(i);
                            if (child.name == "UtilityPanel") AddTarget(child.Find("txt_welcomeArea"));
                            else if (child.name != "DialogueBox" && !IsModalBranch(child.name)) AddTarget(child);
                        }
                        break;
                    case "BuildCanvas":
                    case "TutorialCanvas":
                        for (int i = 0; i < root.childCount; i++)
                        {
                            Transform child = root.GetChild(i);
                            if (!IsModalBranch(child.name)) AddTarget(child);
                        }
                        break;
                    case "ButtonCanvas": AddTarget(root.Find("InteractionButtonContainer")); break;
                    case "BridgeInfoCanvas": AddTarget(root.Find("Panel")); break;
                    case "MultiplayerCanvas": AddTarget(root.Find("multiplayer_Utility_Panel")); break;
                }
                if (canvas.GetComponent<AchievementPopupNotification>() != null) AddTarget(root);
            }

        CollectRenderers();
        obsoleteMasks.Clear();
        foreach (var mask in masks)
            if (mask.Key == null || !currentRenderers.Contains(mask.Key)) obsoleteMasks.Add(mask.Key);
        foreach (CanvasRenderer key in obsoleteMasks)
        {
            RestoreRenderer(masks[key]);
            masks.Remove(key);
        }
        ApplyVisibility();
    }

    private void AddTarget(Transform target)
    {
        if (target != null && target is RectTransform) hudTargets.Add(target.gameObject);
    }

    private static bool IsModalBranch(string name)
    {
        return name == "ChallengeTestIntroduction" || name == "ChallengeSubmitConfirmation" ||
            name == "ChallengeLeaveBuildConfirmation" || name == "ConformationPanel" ||
            name == "ConformationPanel (1)" || name == "SelectionPopupPanel" ||
            name == "SimulationLessonPanel" || name == "PausePanel" || name == "SettingsPanel" ||
            name == "BookAnimationPanel" || name == "Panel_RedoConf" || name == "Panel_Vehicle" ||
            name == "NameRegistrationUI" || name == "Conf" || name == "MobileSettingsPanel";
    }

    internal void ApplyVisibility()
    {
        if (!HudHidden) return;
        bool hidden = UIPanelCoordinator.Instance == null || !UIPanelCoordinator.Instance.HasOpenPanel;
        if (!hidden) { PrepareRendering(); return; }
        // Include dynamically generated labels/TMP submeshes before each draw.
        CollectRenderers();
        foreach (CanvasRenderer renderer in currentRenderers)
        {
            if (renderer == null) continue;
            if (!masks.TryGetValue(renderer, out RendererState state))
            {
                state = new RendererState { renderer = renderer, graphic = renderer.GetComponent<Graphic>() };
                masks[renderer] = state;
            }
            if (!state.masked) state.naturalCull = renderer.cull;
            state.masked = true;
            SetCull(state, true);
        }
    }

    private void CollectRenderers()
    {
        currentRenderers.Clear();
        foreach (GameObject root in hudTargets)
        {
            if (root != null)
            {
                root.GetComponentsInChildren(true, rendererScratch);
                foreach (CanvasRenderer renderer in rendererScratch)
                    if (renderer != null) currentRenderers.Add(renderer);
            }
        }
    }

    internal void PrepareRendering()
    {
        // Let normal UI updates continue while hidden. The next render callback
        // captures their fresh clipping state, then suppresses geometry again.
        foreach (RendererState state in masks.Values) RestoreRenderer(state);
    }

    private void ReleaseMasks()
    {
        PrepareRendering();
        masks.Clear();
    }

    private static void RestoreRenderer(RendererState state)
    {
        if (!state.masked) return;
        state.masked = false;
        SetCull(state, state.naturalCull);
    }

    private static void SetCull(RendererState state, bool culled)
    {
        if (state.renderer == null || state.renderer.cull == culled) return;
        state.renderer.cull = culled;
        if (state.graphic != null) state.graphic.OnCullingChanged();
    }
}
