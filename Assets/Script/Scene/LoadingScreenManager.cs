using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// One persistent transition overlay used by every scene. It is bootstrapped
/// before the first scene, so opening any gameplay scene directly in the Editor
/// still gets the same loading flow.
/// </summary>
[DisallowMultipleComponent]
public sealed class LoadingScreenManager : MonoBehaviour
{
    private const string AssetResourcePath = "Loading/LoadingScreenAssets";
    private const string FontResourcePath = "Fonts & Materials/Bekind Sans SDF";
    private const int PreviewLayer = 31;

    public static LoadingScreenManager Instance { get; private set; }
    public static bool IsLoading => Instance != null && Instance.isLoading;

    private static readonly string[] LoadingTips =
    {
        "TIP: Triangles help distribute forces and keep bridge frames rigid.",
        "TIP: Shorter unsupported spans are usually stronger than one long span.",
        "TIP: Test your bridge before turning in a contract.",
        "TIP: Watch the stress colors to find parts that need reinforcement.",
        "RECOMMENDATION: Place supports near the areas that bend the most.",
        "RECOMMENDATION: Build efficiently and keep an eye on the contract budget."
    };

    private readonly Color backgroundColor = new Color32(239, 231, 216, 255);
    private readonly Color accentColor = new Color32(224, 139, 43, 255);
    private readonly Color trackColor = new Color32(214, 201, 183, 255);
    private readonly Color textColor = new Color32(61, 45, 34, 255);

    private GameObject presentationRoot;
    private CanvasGroup canvasGroup;
    private Image progressFill;
    private RectTransform progressTrack;
    private RectTransform progressShadow;
    private RectTransform loadingTipPanel;
    private RectTransform loadingTipShadow;
    private TextMeshProUGUI percentageText;
    private TextMeshProUGUI tipText;
    private RawImage playerImage;
    private RectTransform playerRect;
    private Camera previewCamera;
    private Transform previewStage;
    private GameObject previewModel;
    private RenderTexture previewTexture;
    private Texture2D roundedUiTexture;
    private Sprite roundedUiSprite;
    private TMP_FontAsset loadingFont;
    private LoadingScreenAssets assets;
    private bool isLoading;
    private int lastDisplayedPercent = -1;
    private float currentProgress;
    private float lastLayoutWidth = -1f;
#if UNITY_EDITOR
    private AsyncOperation observedSceneLoad;
    private float previousLoadingFrameTime;
    private float previousSceneLoadProgress;
    private string loadingPhase;
    private readonly List<SkinnedMeshRenderer> diagnosticGarmentRenderers = new List<SkinnedMeshRenderer>();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static void LoadScene(string sceneName)
    {
        EnsureInstance();
        if (Instance == null)
        {
            Debug.LogError("[LoadingScreen] Could not create the global loading manager.");
            return;
        }

        Instance.BeginLoad(sceneName);
    }

    private static void EnsureInstance()
    {
        if (Instance != null) return;

        LoadingScreenManager existing = FindObjectOfType<LoadingScreenManager>(true);
        if (existing != null)
        {
            Instance = existing;
            return;
        }

        GameObject managerObject = new GameObject("Global Loading Screen");
        Instance = managerObject.AddComponent<LoadingScreenManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        assets = Resources.Load<LoadingScreenAssets>(AssetResourcePath);
        loadingFont = Resources.Load<TMP_FontAsset>(FontResourcePath);
        BuildPresentation();
        SetVisible(false, true);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
        }

        if (roundedUiSprite != null) Destroy(roundedUiSprite);
        if (roundedUiTexture != null) Destroy(roundedUiTexture);
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases += UpdatePresentationLayout;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= UpdatePresentationLayout;
    }

    private void BeginLoad(string sceneName)
    {
        if (isLoading) return;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[LoadingScreen] Scene name is empty.", this);
            return;
        }

        if (!SceneExists(sceneName))
        {
            Debug.LogError($"[LoadingScreen] Scene '{sceneName}' is not enabled in Build Settings.", this);
            return;
        }

        Time.timeScale = 1f;
        isLoading = true;
        StartCoroutine(LoadRoutine(sceneName));
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        float shownAt = Time.realtimeSinceStartup;
        float minimumDisplaySeconds = assets != null
            ? Mathf.Max(0f, assets.minimumDisplaySeconds)
            : 1.5f;
        SetLoadingTip();
        SetProgress(0f);
        SetVisible(true, false);
        canvasGroup.alpha = 0f;

        // Render the overlay before any optional preview setup or scene-loading
        // work can occupy the main thread.
        float fadeElapsed = 0f;
        while (fadeElapsed < 0.18f)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Clamp01(fadeElapsed / 0.18f);
            yield return null;
        }

        canvasGroup.alpha = 1f;
        // Keep the runner in the persistent loading hierarchy and give its
        // Animator a rendered frame before the destination scene starts loading.
        try
        {
            PreparePlayerPreview();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[LoadingScreen] Optional player preview failed: {exception.Message}", this);
            ReleasePreviewModel();
            if (playerImage != null) playerImage.enabled = false;
        }
        yield return null;
#if UNITY_EDITOR
        LogPreviewRenderDiagnostics();
        loadingPhase = "Scene loading";
#endif

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
#if UNITY_EDITOR
        observedSceneLoad = operation;
#endif
        if (operation == null)
        {
            Debug.LogError($"[LoadingScreen] Unity could not start loading '{sceneName}'.", this);
            // A failed door load is not an arrival. Do not let a later scene
            // load inherit its spawn target or complete its arrival lesson.
            DoorTransition.ClearPendingArrival();
            PlayerSpawnManager.targetSpawnPointName = "";
            SetVisible(false, true);
            ReleasePreviewModel();
            isLoading = false;
            yield break;
        }

        operation.allowSceneActivation = false;
        float displayedProgress = 0f;

        // LoadSceneAsync stops at 0.9 while activation is disabled. Reserve the
        // remainder for activation and scene-owned startup work, not a timer.
        while (operation.progress < 0.9f)
        {
            float target = Mathf.Clamp01(operation.progress / 0.9f) * 0.82f;
            displayedProgress = AdvanceProgress(
                displayedProgress, target, shownAt, minimumDisplaySeconds, false);
            SetProgress(displayedProgress);
            yield return null;
        }

        // Do not wait for the visual bar to catch up before allowing activation.
        // Awake and OnEnable run during this phase, under the loading overlay.
#if UNITY_EDITOR
        loadingPhase = "Scene activation";
#endif
        operation.allowSceneActivation = true;
        while (!operation.isDone)
        {
            displayedProgress = AdvanceProgress(
                displayedProgress, 0.88f, shownAt, minimumDisplaySeconds, false);
            SetProgress(displayedProgress);
            yield return null;
        }

        // Start runs on the first destination-scene frame. Give it that frame,
        // then observe the scene's explicit readiness signals until they finish.
#if UNITY_EDITOR
        loadingPhase = "Scene initialization";
#endif
        yield return null;

        Scene destination = SceneManager.GetActiveScene();
        List<LevelResetManager> resetManagers = CollectReadinessComponents<LevelResetManager>(destination);
        List<DynamicNavMeshUpdater> navMeshUpdaters = CollectReadinessComponents<DynamicNavMeshUpdater>(destination);
        List<RavineNavMeshLinks> ravineLinks = CollectReadinessComponents<RavineNavMeshLinks>(destination);
        List<CloudManager> cloudManagers = CollectReadinessComponents<CloudManager>(destination);

        while (true)
        {
            float initializationProgress = GetInitializationProgress(
                resetManagers, navMeshUpdaters, ravineLinks, cloudManagers,
                out bool ready);
            float target = ready ? 1f : 0.90f + 0.09f * initializationProgress;
            displayedProgress = AdvanceProgress(
                displayedProgress, target, shownAt, minimumDisplaySeconds, ready);
            SetProgress(displayedProgress);
            if (ready && displayedProgress >= 1f)
                break;
            yield return null;
        }

        SetProgress(1f);

        fadeElapsed = 0f;
        while (fadeElapsed < 0.22f)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = 1f - Mathf.Clamp01(fadeElapsed / 0.22f);
            yield return null;
        }

        SetVisible(false, true);
        ReleasePreviewModel();
        isLoading = false;
#if UNITY_EDITOR
        observedSceneLoad = null;
        previousLoadingFrameTime = 0f;
        loadingPhase = null;
#endif
    }

#if UNITY_EDITOR
    private void LateUpdate()
    {
        if (!isLoading) return;
        float now = Time.realtimeSinceStartup;
        float gap = now - previousLoadingFrameTime;
        if (previousLoadingFrameTime > 0f && gap > 0.5f)
        {
            float currentSceneProgress = observedSceneLoad != null
                ? observedSceneLoad.progress : -1f;
            Debug.LogWarning(
                $"[LoadingScreen Diagnostic] No loading-screen frame for {gap:F2}s " +
                $"during {loadingPhase}; displayed={lastDisplayedPercent}%, " +
                $"scene progress {previousSceneLoadProgress:F2} -> {currentSceneProgress:F2}.", this);
        }
        previousLoadingFrameTime = now;
        previousSceneLoadProgress = observedSceneLoad != null
            ? observedSceneLoad.progress : -1f;
    }

    private void LogPreviewOutfitDiagnostics()
    {
        if (previewModel == null) return;
        diagnosticGarmentRenderers.Clear();
        PlayerCosmeticMirror mirror = previewModel.GetComponentInChildren<PlayerCosmeticMirror>(true);
        CosmeticLoadoutData loadout = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetCosmeticLoadoutCopy() : null;
        Debug.Log($"[LoadingScreen Diagnostic] Saved shirt={loadout?.shirtID ?? "<none>"}, " +
                  $"pants={loadout?.pantsID ?? "<none>"}; preview mirror={mirror != null}.", this);
        if (mirror == null) return;

        foreach (CosmeticModelBinding binding in mirror.cosmeticBindings)
        {
            if (binding == null || (binding.category != CosmeticCategory.Shirt &&
                                    binding.category != CosmeticCategory.Pants) || binding.models == null)
                continue;
            foreach (GameObject model in binding.models)
            {
                if (model == null || !model.activeInHierarchy) continue;
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    diagnosticGarmentRenderers.Add(renderer);
                    Debug.Log($"[LoadingScreen Diagnostic] {binding.category}/{binding.cosmeticID}: " +
                              $"model={model.name}, renderer={renderer.name}, active={renderer.gameObject.activeInHierarchy}, " +
                              $"enabled={renderer.enabled}, visible={renderer.isVisible}, " +
                              $"mesh={renderer.sharedMesh?.name ?? "<none>"}, " +
                              $"bounds={renderer.bounds}, offscreen={renderer.updateWhenOffscreen}.", this);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Color baseColor = material.HasProperty("_BaseColor")
                            ? material.GetColor("_BaseColor")
                            : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                        Debug.Log($"[LoadingScreen Diagnostic] Material {material.name}: " +
                                  $"shader={material.shader?.name ?? "<none>"}, " +
                                  $"queue={material.renderQueue}, alpha={baseColor.a:F3}.", this);
                    }
                }
            }
        }
    }

    private void LogPreviewRenderDiagnostics()
    {
        if (previewCamera == null || diagnosticGarmentRenderers.Count == 0) return;
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(previewCamera);
        foreach (SkinnedMeshRenderer renderer in diagnosticGarmentRenderers)
        {
            if (renderer == null) continue;
            Debug.Log($"[LoadingScreen Diagnostic] Rendered {renderer.name}: " +
                      $"active={renderer.gameObject.activeInHierarchy}, enabled={renderer.enabled}, " +
                      $"visible={renderer.isVisible}, inFrustum={GeometryUtility.TestPlanesAABB(planes, renderer.bounds)}, " +
                      $"layer={renderer.gameObject.layer}, offscreen={renderer.updateWhenOffscreen}, " +
                      $"bounds={renderer.bounds}.", this);
        }
    }
#endif

    private static float AdvanceProgress(
        float current, float realTarget, float shownAt,
        float minimumDisplaySeconds, bool ready)
    {
        float elapsed = Time.realtimeSinceStartup - shownAt;
        float durationProgress = minimumDisplaySeconds > 0f
            ? Mathf.Clamp01(elapsed / minimumDisplaySeconds)
            : 1f;
        float presentationCap = Mathf.SmoothStep(0f, 1f, durationProgress);
        float target = Mathf.Min(realTarget, presentationCap);

        // The time curve only limits how quickly a fast load can be presented;
        // it can never advance beyond the real stage. After a slow load, catch
        // up promptly instead of imposing another minimum-duration wait.
        float speed = ready && durationProgress >= 1f
            ? 8f
            : minimumDisplaySeconds > 0f
                ? Mathf.Max(2.4f, 1.6f / minimumDisplaySeconds)
                : 2.4f;
        // A single long scene-integration frame should not jump the bar across
        // a large distance on the first frame that Unity can render again.
        float animationDelta = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        return Mathf.MoveTowards(
            current, Mathf.Max(current, target), speed * animationDelta);
    }

    private static List<T> CollectReadinessComponents<T>(Scene scene) where T : Behaviour
    {
        List<T> result = new List<T>();
        foreach (T component in FindObjectsOfType<T>(true))
        {
            if (component != null && component.gameObject.scene == scene && component.isActiveAndEnabled)
                result.Add(component);
        }
        return result;
    }

    private static float GetInitializationProgress(
        List<LevelResetManager> resetManagers,
        List<DynamicNavMeshUpdater> navMeshUpdaters,
        List<RavineNavMeshLinks> ravineLinks,
        List<CloudManager> cloudManagers,
        out bool ready)
    {
        float completed = 0f;
        int count = 0;
        ready = true;

        foreach (LevelResetManager manager in resetManagers)
        {
            if (manager == null || !manager.isActiveAndEnabled) continue;
            count++;
            if (manager.IsInitialized) completed++;
            else ready = false;
        }

        foreach (DynamicNavMeshUpdater updater in navMeshUpdaters)
        {
            if (updater == null || !updater.isActiveAndEnabled) continue;
            count++;
            completed += updater.StartupProgress;
            if (!updater.IsStartupReady)
                ready = false;
        }

        foreach (RavineNavMeshLinks links in ravineLinks)
        {
            if (links == null || !links.isActiveAndEnabled) continue;
            count++;
            if (links.IsInitialized) completed++;
            else
                ready = false;
        }

        foreach (CloudManager clouds in cloudManagers)
        {
            if (clouds == null || !clouds.isActiveAndEnabled) continue;
            count++;
            completed += clouds.InitialSpawnProgress;
            if (!clouds.IsInitialSpawnComplete)
                ready = false;
        }

        return count > 0 ? Mathf.Clamp01(completed / count) : 1f;
    }

    private void BuildPresentation()
    {
        presentationRoot = new GameObject("Loading Presentation", typeof(RectTransform));
        presentationRoot.transform.SetParent(transform, false);

        Canvas canvas = presentationRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = presentationRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        presentationRoot.AddComponent<GraphicRaycaster>();

        canvasGroup = presentationRoot.AddComponent<CanvasGroup>();
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        RectTransform background = CreateImage(
            presentationRoot.transform,
            "Background",
            backgroundColor,
            Vector2.zero,
            Vector2.zero);
        Stretch(background);

        roundedUiSprite = CreateRoundedUiSprite();

        RectTransform barShadow = CreateImage(
            background,
            "Progress Shadow",
            new Color32(61, 45, 34, 26),
            new Vector2(1416f, 46f),
            new Vector2(0f, 64f));
        progressShadow = barShadow;
        Image barShadowImage = barShadow.GetComponent<Image>();
        barShadowImage.sprite = roundedUiSprite;
        barShadowImage.type = Image.Type.Sliced;

        GameObject rawImageObject = new GameObject(
            "Running Player",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage));
        rawImageObject.transform.SetParent(background, false);
        playerRect = rawImageObject.GetComponent<RectTransform>();
        SetCentered(playerRect, new Vector2(320f, 320f), new Vector2(-700f, 65f));
        playerRect.pivot = new Vector2(0.5f, 0f);
        playerImage = rawImageObject.GetComponent<RawImage>();
        playerImage.color = Color.white;
        playerImage.raycastTarget = false;

        progressTrack = CreateImage(
            background,
            "Progress Track",
            trackColor,
            new Vector2(1400f, 30f),
            new Vector2(0f, 70f));
        Image trackImage = progressTrack.GetComponent<Image>();
        trackImage.sprite = roundedUiSprite;
        trackImage.type = Image.Type.Sliced;

        progressFill = CreateImage(
            progressTrack,
            "Progress Fill",
            accentColor,
            Vector2.zero,
            Vector2.zero).GetComponent<Image>();
        RectTransform fillRect = progressFill.rectTransform;
        Stretch(fillRect);
        fillRect.pivot = new Vector2(0f, 0.5f);
        progressFill.sprite = roundedUiSprite;
        progressFill.type = Image.Type.Sliced;
        progressFill.enabled = false;

        percentageText = CreateText(
            background,
            "Percentage",
            "0%",
            40f,
            textColor,
            new Vector2(220f, 60f),
            new Vector2(0f, -42f));
        percentageText.fontStyle = FontStyles.Bold;

        RectTransform tipShadow = CreateImage(
            background,
            "Tip Shadow",
            new Color32(61, 45, 34, 18),
            new Vector2(1180f, 96f),
            new Vector2(0f, -143f));
        loadingTipShadow = tipShadow;
        Image tipShadowImage = tipShadow.GetComponent<Image>();
        tipShadowImage.sprite = roundedUiSprite;
        tipShadowImage.type = Image.Type.Sliced;

        RectTransform tipPanel = CreateImage(
            background,
            "Tip Card",
            new Color32(250, 243, 231, 255),
            new Vector2(1170f, 92f),
            new Vector2(0f, -137f));
        loadingTipPanel = tipPanel;
        Image tipPanelImage = tipPanel.GetComponent<Image>();
        tipPanelImage.sprite = roundedUiSprite;
        tipPanelImage.type = Image.Type.Sliced;

        tipText = CreateText(
            tipPanel,
            "Loading Tip",
            LoadingTips[0],
            27f,
            new Color32(92, 72, 53, 255),
            new Vector2(1080f, 72f),
            Vector2.zero);

        // Keep the complete runner visible above the loading track.
        playerRect.SetAsLastSibling();

        BuildPreviewStage();
    }

    private void BuildPreviewStage()
    {
        GameObject stageObject = new GameObject("Player Preview Stage");
        stageObject.transform.SetParent(transform, false);
        stageObject.transform.position = new Vector3(10000f, 10000f, 10000f);
        previewStage = stageObject.transform;

        int textureSize = QualitySettings.GetQualityLevel() <= 1 ? 256 : 512;
        previewTexture = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32)
        {
            name = "Loading Player Preview",
            antiAliasing = textureSize >= 512 ? 2 : 1,
            useMipMap = false,
            autoGenerateMips = false
        };
        previewTexture.Create();
        playerImage.texture = previewTexture;

        GameObject cameraObject = new GameObject("Preview Camera");
        cameraObject.transform.SetParent(previewStage, false);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCamera.cullingMask = 1 << PreviewLayer;
        // The loading character lives outside the destination world. Its camera
        // must not use that scene's baked occlusion data when activation occurs.
        previewCamera.useOcclusionCulling = false;
        previewCamera.aspect = (float)previewTexture.width / previewTexture.height;
        previewCamera.fieldOfView = 30f;
        previewCamera.nearClipPlane = assets != null && assets.playerPreviewNearClipPlane > 0f
            ? Mathf.Clamp(assets.playerPreviewNearClipPlane, 0.01f, 0.05f)
            : 0.01f;
        previewCamera.farClipPlane = 50f;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = textureSize >= 512;
        previewCamera.targetTexture = previewTexture;
        previewCamera.enabled = false;

        GameObject lightObject = new GameObject("Preview Light");
        lightObject.transform.SetParent(previewStage, false);
        lightObject.transform.localRotation = Quaternion.Euler(35f, -35f, 0f);
        Light previewLight = lightObject.AddComponent<Light>();
        previewLight.type = LightType.Directional;
        previewLight.intensity = 1.05f;
        previewLight.color = Color.white;
        previewLight.cullingMask = 1 << PreviewLayer;
        previewLight.shadows = LightShadows.None;
    }

    private void PreparePlayerPreview()
    {
        ReleasePreviewModel();

        // Every transition should show the same wardrobe-capable character.
        // A live scene visual may still be an older/base rig even when a saved
        // cosmetic loadout exists, so only use it if the preview prefab fails.
        if (assets != null && assets.fallbackPlayerPrefab != null)
        {
            try
            {
                // Use the non-generic overload here. A stale/broken prefab reference can otherwise
                // throw InvalidCastException inside Unity's generic Instantiate wrapper and abort
                // the scene load before LoadSceneAsync gets a chance to start.
                UnityEngine.Object clone = Instantiate(
                    (UnityEngine.Object)assets.fallbackPlayerPrefab,
                    previewStage,
                    false);
                previewModel = clone as GameObject;
                if (previewModel != null)
                {
                    previewModel.name = "Loading Player (Saved Appearance)";
                    ApplySavedPreviewAppearance(previewModel, null);
#if UNITY_EDITOR
                    LogPreviewOutfitDiagnostics();
#endif
                    StripGameplayComponents(previewModel);
                }
                else if (clone != null)
                {
                    Destroy(clone);
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    $"[LoadingScreen] Could not create the optional player preview. " +
                    $"The scene will continue loading without it. {exception.Message}",
                    this);
                previewModel = null;
            }
        }

        if (previewModel == null)
        {
            GameObject source = FindLivePlayerVisual();
            if (source != null)
            {
                previewModel = Instantiate(source, previewStage, false);
                previewModel.name = "Loading Player (Scene Fallback)";
                ApplySavedPreviewAppearance(previewModel, source);
#if UNITY_EDITOR
                LogPreviewOutfitDiagnostics();
#endif
                StripGameplayComponents(previewModel);
            }
        }

        if (previewModel == null)
        {
            if (playerImage != null) playerImage.enabled = false;
            return;
        }

        if (playerImage != null) playerImage.enabled = true;
        SetLayerRecursively(previewModel.transform, PreviewLayer);
        previewModel.transform.localPosition = Vector3.zero;
        // The preview camera looks down +Z. A +90-degree yaw presents the
        // character in profile, facing screen-right along the loading bar.
        previewModel.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        previewModel.transform.localScale = Vector3.one;
        previewModel.SetActive(true);

        LoadingPlayerPreview preview = previewModel.GetComponent<LoadingPlayerPreview>();
        if (preview == null) preview = previewModel.AddComponent<LoadingPlayerPreview>();
        preview.BindPreviewCamera(previewCamera);
        preview.BeginRunning(assets != null ? assets.playerAnimatorController : null);

        FramePreviewModel();
        previewCamera.enabled = true;
    }

    private static GameObject FindLivePlayerVisual()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return null;

        Animator animator = player.GetComponentsInChildren<Animator>(true)
            .FirstOrDefault(candidate =>
                candidate != null &&
                candidate.gameObject.activeInHierarchy &&
                candidate.runtimeAnimatorController != null &&
                candidate.runtimeAnimatorController.name == "PlayerAnimator");

        if (animator == null) return null;

        // Generic animation requires the Animator to live on Base_Rig, but the
        // visible model, clothing categories, and cosmetic objects are siblings
        // higher in the hierarchy. Clone the complete visual that is directly
        // below Player instead of cloning only the skeleton.
        Transform visualRoot = animator.transform;
        while (visualRoot.parent != null && visualRoot.parent != player.transform)
            visualRoot = visualRoot.parent;

        return visualRoot.parent == player.transform
            ? visualRoot.gameObject
            : animator.gameObject;
    }

    private static void ApplySavedPreviewAppearance(GameObject clone, GameObject source)
    {
        if (clone == null || PlayerDataManager.Instance == null) return;
        CosmeticLoadoutData loadout = PlayerDataManager.Instance.GetCosmeticLoadoutCopy();

        PlayerCosmeticMirror mirror = clone.GetComponentInChildren<PlayerCosmeticMirror>(true);
        if (mirror != null)
        {
            mirror.ApplyLoadout(loadout);
            return;
        }

        PlayerCosmetics sourceCosmetics = source != null
            ? source.GetComponentInParent<PlayerCosmetics>() : null;
        if (sourceCosmetics == null || sourceCosmetics.cosmeticBindings == null) return;

        List<CosmeticModelBinding> clonedBindings = new List<CosmeticModelBinding>();
        foreach (CosmeticModelBinding binding in sourceCosmetics.cosmeticBindings)
        {
            if (binding == null) continue;
            CosmeticModelBinding cloned = new CosmeticModelBinding
            {
                cosmeticID = binding.cosmeticID,
                category = binding.category,
                defaultWhenEmpty = binding.defaultWhenEmpty
            };
            if (binding.models != null)
                foreach (GameObject model in binding.models)
                {
                    GameObject cloneModel = FindMatchingPreviewObject(model, source.transform, clone.transform);
                    if (cloneModel != null) cloned.models.Add(cloneModel);
                }
            clonedBindings.Add(cloned);
        }
        CosmeticBindingUtility.Apply(clonedBindings, loadout);
    }

    private static GameObject FindMatchingPreviewObject(GameObject original, Transform sourceRoot, Transform cloneRoot)
    {
        if (original == null || !original.transform.IsChildOf(sourceRoot)) return null;

        Stack<int> siblingPath = new Stack<int>();
        Transform current = original.transform;
        while (current != sourceRoot)
        {
            siblingPath.Push(current.GetSiblingIndex());
            current = current.parent;
        }

        current = cloneRoot;
        while (siblingPath.Count > 0)
        {
            int childIndex = siblingPath.Pop();
            if (childIndex >= current.childCount) return null;
            current = current.GetChild(childIndex);
        }
        return current.gameObject;
    }

    private static void StripGameplayComponents(GameObject clone)
    {
        foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            // Destroy is deferred until the end of the frame. Disable first so
            // copied cosmetic/gameplay callbacks cannot change the preview look
            // while the loading overlay is already visible.
            behaviour.enabled = false;
            Destroy(behaviour);
        }
        foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
            Destroy(collider);
        foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true))
            Destroy(body);
        foreach (AudioSource source in clone.GetComponentsInChildren<AudioSource>(true))
            Destroy(source);
        foreach (Camera camera in clone.GetComponentsInChildren<Camera>(true))
            Destroy(camera.gameObject);
        foreach (Light light in clone.GetComponentsInChildren<Light>(true))
            Destroy(light.gameObject);

        // The live first-person player hides its head from the gameplay camera.
        // Restore every renderer for the third-person loading preview.
        foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.allowOcclusionWhenDynamic = false;

            // Clothing and shoes are separate skinned meshes. Keep them updating
            // in the preview even if their imported bounds are too small.
            if (renderer is SkinnedMeshRenderer skinnedRenderer)
                skinnedRenderer.forceMatrixRecalculationPerRender = true;
        }
    }

    private void FramePreviewModel()
    {
        Bounds bounds = default;
        LoadingPlayerPreview preview = previewModel.GetComponent<LoadingPlayerPreview>();
        bool hasVisibleRenderer = preview != null && preview.TryGetPreviewBounds(out bounds);
        // Framing uses the whole running cycle's actual geometry, not the larger
        // per-renderer culling overrides used to keep modular garments visible.
        if (!hasVisibleRenderer)
        {
            foreach (Renderer renderer in previewModel.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!hasVisibleRenderer)
                {
                    bounds = renderer.bounds;
                    hasVisibleRenderer = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }
        }
        if (!hasVisibleRenderer) return;

        Vector3 center = previewStage.InverseTransformPoint(bounds.center);
        float halfFovTangent = Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float distance = Mathf.Max(Mathf.Max(1f, bounds.size.y) * 0.5f / halfFovTangent,
            bounds.extents.x / (halfFovTangent * Mathf.Max(0.01f, previewCamera.aspect)));
        distance = distance * 1.08f + bounds.extents.z;

        previewCamera.transform.localPosition = center + new Vector3(0f, 0f, -distance);
        previewCamera.transform.LookAt(previewStage.TransformPoint(center));
        previewCamera.farClipPlane = Mathf.Max(50f, distance + bounds.extents.z + 1f);

        // Height-based framing alone can put a deep hat or other accessory in
        // front of the near plane. Move back only if the visible outfit's bounds
        // actually cross it; normal framing remains unchanged.
        Vector3 forward = previewCamera.transform.forward;
        Vector3 extents = bounds.extents;
        float nearestDepth = Vector3.Dot(bounds.center - previewCamera.transform.position, forward) -
            Mathf.Abs(forward.x) * extents.x -
            Mathf.Abs(forward.y) * extents.y -
            Mathf.Abs(forward.z) * extents.z;
        float minimumDepth = previewCamera.nearClipPlane + 0.02f;
        if (nearestDepth < minimumDepth)
            previewCamera.transform.position -= forward * (minimumDepth - nearestDepth);
    }

    private void ReleasePreviewModel()
    {
        if (previewCamera != null) previewCamera.enabled = false;
        if (previewModel != null) Destroy(previewModel);
        previewModel = null;
    }

    private void SetVisible(bool visible, bool immediate)
    {
        if (presentationRoot == null) return;
        if (immediate) canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
        presentationRoot.SetActive(visible);
    }

    private void SetProgress(float progress)
    {
        progress = Mathf.Clamp01(progress);
        currentProgress = progress;
        UpdatePresentationLayout();
        if (progressFill != null)
        {
            RectTransform fillRect = progressFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(progress, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            progressFill.enabled = progress > 0.001f;
        }
        int percent = progress >= 1f ? 100 : Mathf.Min(99, Mathf.FloorToInt(progress * 100f));
        if (percentageText != null && percent != lastDisplayedPercent)
        {
            percentageText.text = percent + "%";
            lastDisplayedPercent = percent;
        }

        UpdateRunnerPosition();
    }

    private void UpdatePresentationLayout()
    {
        if (presentationRoot == null || progressTrack == null) return;
        float canvasWidth = ((RectTransform)presentationRoot.transform).rect.width;
        if (canvasWidth <= 0f || Mathf.Abs(canvasWidth - lastLayoutWidth) < 0.1f) return;
        lastLayoutWidth = canvasWidth;
        float sideMargin = Mathf.Max(48f, canvasWidth * 0.1f);
        float trackWidth = Mathf.Min(1400f, Mathf.Max(1f, canvasWidth - sideMargin * 2f));
        progressTrack.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, trackWidth);
        if (progressShadow != null)
            progressShadow.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, trackWidth + 16f);
        float tipWidth = Mathf.Min(1170f, Mathf.Max(1f, canvasWidth - 96f));
        if (loadingTipPanel != null)
            loadingTipPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, tipWidth);
        if (loadingTipShadow != null)
            loadingTipShadow.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, tipWidth + 10f);
        if (tipText != null)
            tipText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(1f, tipWidth - 90f));
        UpdateRunnerPosition();
    }

    private void UpdateRunnerPosition()
    {
        if (playerRect != null && progressTrack != null)
        {
            float halfWidth = progressTrack.rect.width * 0.5f;
            // The runner is a full RawImage, not a point at the fill edge. Keep
            // both ends inside the bar so tablets cannot crop its head or feet.
            float endInset = playerRect.rect.width * 0.5f + 12f;
            float travelExtent = Mathf.Max(0f, halfWidth - endInset);
            Vector2 runnerPosition = playerRect.anchoredPosition;
            runnerPosition.x = Mathf.Lerp(-travelExtent, travelExtent, currentProgress);
            runnerPosition.y = progressTrack.anchoredPosition.y +
                               progressTrack.rect.height * 0.5f - 2f;
            playerRect.anchoredPosition = runnerPosition;
        }
    }

    private void SetLoadingTip()
    {
        if (tipText == null || LoadingTips.Length == 0) return;
        string selectedTip = LoadingTips[Random.Range(0, LoadingTips.Length)];
        int separator = selectedTip.IndexOf(':');
        tipText.text = separator > 0
            ? $"<color=#E08B2B><b>{selectedTip.Substring(0, separator)}</b></color>{selectedTip.Substring(separator)}"
            : selectedTip;
    }

    private Sprite CreateRoundedUiSprite()
    {
        const int width = 64;
        const int height = 32;
        const float radius = height * 0.5f;

        roundedUiTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Loading UI Rounded Rectangle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[width * height];
        Vector2 leftCenter = new Vector2(radius, radius);
        Vector2 rightCenter = new Vector2(width - radius, radius);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 pixelCenter = new Vector2(x + 0.5f, y + 0.5f);
                Vector2 nearestCenter = pixelCenter.x < radius ? leftCenter :
                    pixelCenter.x > width - radius ? rightCenter :
                    new Vector2(pixelCenter.x, radius);
                float distance = Vector2.Distance(pixelCenter, nearestCenter);
                float alpha = Mathf.Clamp01(radius + 0.5f - distance);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        roundedUiTexture.SetPixels(pixels);
        roundedUiTexture.Apply(false, true);
        return Sprite.Create(
            roundedUiTexture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
    }

    private static bool SceneExists(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.Equals(
                Path.GetFileNameWithoutExtension(path),
                sceneName,
                System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static RectTransform CreateImage(
        Transform parent,
        string name,
        Color color,
        Vector2 size,
        Vector2 position)
    {
        GameObject imageObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        imageObject.transform.SetParent(parent, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        SetCentered(rect, size, position);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return rect;
    }

    private TextMeshProUGUI CreateText(
        Transform parent,
        string name,
        string value,
        float fontSize,
        Color color,
        Vector2 size,
        Vector2 position)
    {
        GameObject textObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (loadingFont != null) text.font = loadingFont;
        text.text = value;
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(16f, fontSize - 10f);
        text.fontSizeMax = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        SetCentered(text.rectTransform, size, position);
        return text;
    }

    private static void SetCentered(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect)
    {
        Stretch(rect, Vector2.zero, Vector2.zero);
    }

    private static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = minimum;
        rect.offsetMax = maximum;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}
