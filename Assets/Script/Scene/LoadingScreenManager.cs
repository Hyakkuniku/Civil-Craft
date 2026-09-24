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
        StartCoroutine(LoadRoutine(sceneName));
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        isLoading = true;
        PreparePlayerPreview();
        SetLoadingTip();
        SetProgress(0f);
        SetVisible(true, true);

        float fadeElapsed = 0f;
        while (fadeElapsed < 0.18f)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Clamp01(fadeElapsed / 0.18f);
            yield return null;
        }

        canvasGroup.alpha = 1f;
        float shownAt = Time.realtimeSinceStartup;
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        if (operation == null)
        {
            Debug.LogError($"[LoadingScreen] Unity could not start loading '{sceneName}'.", this);
            SetVisible(false, true);
            isLoading = false;
            yield break;
        }

        operation.allowSceneActivation = false;
        float displayedProgress = 0f;
        float progressVelocity = 0f;
        const float minimumVisibleSeconds = 1.35f;

        // Keep the display tied to Unity's real async progress, but ease out
        // the large steps reported by AsyncOperation so the bar and runner glide.
        while (operation.progress < 0.9f || Time.realtimeSinceStartup - shownAt < minimumVisibleSeconds)
        {
            float realProgress = Mathf.Clamp01(operation.progress / 0.9f);
            float elapsed = Time.realtimeSinceStartup - shownAt;
            float presentationProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsed / minimumVisibleSeconds));
            float target = Mathf.Min(realProgress, presentationProgress);
            displayedProgress = Mathf.SmoothDamp(
                displayedProgress,
                target,
                ref progressVelocity,
                0.14f,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            SetProgress(displayedProgress);
            yield return null;
        }

        // Finish the last few percent instead of snapping directly to 100%.
        progressVelocity = 0f;
        while (displayedProgress < 0.997f)
        {
            displayedProgress = Mathf.SmoothDamp(
                displayedProgress,
                1f,
                ref progressVelocity,
                0.09f,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            SetProgress(displayedProgress);
            yield return null;
        }

        SetProgress(1f);
        yield return new WaitForSecondsRealtime(0.14f);
        operation.allowSceneActivation = true;

        while (!operation.isDone)
            yield return null;

        // Give PlayerSpawnManager and the new scene's canvases one frame to settle.
        yield return null;

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
    }

    private void BuildPresentation()
    {
        presentationRoot = new GameObject("Loading Presentation", typeof(RectTransform));
        presentationRoot.transform.SetParent(transform, false);

        Canvas canvas = presentationRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 10;

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
        Image tipShadowImage = tipShadow.GetComponent<Image>();
        tipShadowImage.sprite = roundedUiSprite;
        tipShadowImage.type = Image.Type.Sliced;

        RectTransform tipPanel = CreateImage(
            background,
            "Tip Card",
            new Color32(250, 243, 231, 255),
            new Vector2(1170f, 92f),
            new Vector2(0f, -137f));
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
        previewCamera.fieldOfView = 30f;
        previewCamera.nearClipPlane = 0.05f;
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
        preview.BeginRunning(assets != null ? assets.playerAnimatorController : null);

        FramePreviewModel();
        ExpandAnimatedRendererBounds();
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
            Destroy(behaviour);
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
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.allowOcclusionWhenDynamic = false;

            // Clothing and shoes are separate skinned meshes. The preview stage
            // lives far away from the gameplay camera, so Unity may otherwise
            // cull one of those meshes for part of the sprint animation and make
            // the bare body feet appear intermittently.
            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                skinnedRenderer.updateWhenOffscreen = true;
                skinnedRenderer.forceMatrixRecalculationPerRender = true;
            }
        }
    }

    private void FramePreviewModel()
    {
        Renderer[] renderers = previewModel.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Vector3 center = previewStage.InverseTransformPoint(bounds.center);
        float height = Mathf.Max(1f, bounds.size.y);
        float distance = height / (2f * Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad));
        distance *= 1.08f;

        previewCamera.transform.localPosition = center + new Vector3(0f, height * 0.04f, -distance);
        previewCamera.transform.LookAt(previewStage.TransformPoint(center + Vector3.up * height * 0.03f));
    }

    private void ExpandAnimatedRendererBounds()
    {
        // Clothing and footwear are separate skinned meshes. Their imported
        // bounds only cover the bind pose and can leave the animated shoes
        // outside the bounds during the sprint cycle. Unity then culls the
        // complete shoe renderer while the body's bare feet remain visible.
        // Expand after framing so these safety bounds do not zoom the camera out.
        foreach (SkinnedMeshRenderer renderer in
                 previewModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Bounds bounds = renderer.localBounds;
            bounds.extents *= 4f;
            renderer.localBounds = bounds;
            renderer.updateWhenOffscreen = true;
            renderer.forceMatrixRecalculationPerRender = true;
        }
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
        if (progressFill != null)
        {
            RectTransform fillRect = progressFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(progress, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            progressFill.enabled = progress > 0.001f;
        }
        if (percentageText != null) percentageText.text = Mathf.RoundToInt(progress * 100f) + "%";

        if (playerRect != null && progressTrack != null)
        {
            float halfWidth = progressTrack.rect.width * 0.5f;
            Vector2 runnerPosition = playerRect.anchoredPosition;
            runnerPosition.x = Mathf.Lerp(-halfWidth, halfWidth, progress);
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
