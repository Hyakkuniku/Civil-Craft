using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class CosmeticCategoryTabBinding
{
    public CosmeticCategory category;
    public Button button;
    public GameObject selectedVisual;
}

[Serializable]
public sealed class CosmeticColorButtonBinding
{
    public Button button;
    public Image colorImage;
    public GameObject selectedVisual;
}

[DisallowMultipleComponent]
public sealed class CharacterCustomizationController : MonoBehaviour
{
    [Header("Authored Panels")]
    [SerializeField] private GameObject customizationPanel;
    [SerializeField] private GameObject almanacPanel;
    [SerializeField] private TMP_Text categoryTitle;

    [Header("Profile To Wardrobe Transition")]
    [SerializeField] private RectTransform sharedPortrait;
    [SerializeField] private RectTransform profilePortraitAnchor;
    [SerializeField] private RectTransform customizationPortraitAnchor;
    [SerializeField] private RectTransform transitionLayer;
    [SerializeField] private CanvasGroup customizationGroup;
    [SerializeField] private List<CanvasGroup> profileGroups = new List<CanvasGroup>();
    [SerializeField, Min(0.1f)] private float transitionDuration = 0.4f;
    [SerializeField, Range(0f, 1f)] private float controlsFadeDelay = 0.18f;

    [Header("Shared Photobooth")]
    [SerializeField] private PlayerCosmeticMirror previewMirror;

    [Header("Catalog")]
    [SerializeField] private List<CosmeticDefinition> cosmetics = new List<CosmeticDefinition>();
    [SerializeField] private List<CosmeticCategoryTabBinding> categoryTabs = new List<CosmeticCategoryTabBinding>();
    [SerializeField] private List<CosmeticOptionButton> optionSlots = new List<CosmeticOptionButton>();
    [SerializeField] private List<CosmeticColorButtonBinding> colorSlots = new List<CosmeticColorButtonBinding>();

    private readonly Dictionary<Button, UnityEngine.Events.UnityAction> tabActions =
        new Dictionary<Button, UnityEngine.Events.UnityAction>();
    private readonly Dictionary<Button, UnityEngine.Events.UnityAction> colorActions =
        new Dictionary<Button, UnityEngine.Events.UnityAction>();

    private CosmeticLoadoutData originalLoadout;
    private CosmeticLoadoutData previewLoadout;
    private CosmeticCategory currentCategory = CosmeticCategory.Accessories;
    private CosmeticDefinition selectedDefinition;
    private bool initialized;
    private bool isTransitioning;
    private bool isCustomizationOpen;
    private Coroutine transitionRoutine;

    private Transform portraitOriginalParent;
    private int portraitOriginalSiblingIndex;
    private Vector2 portraitOriginalAnchorMin;
    private Vector2 portraitOriginalAnchorMax;
    private Vector2 portraitOriginalPivot;
    private Vector2 portraitOriginalAnchoredPosition;
    private Vector2 portraitOriginalSizeDelta;
    private Vector3 portraitOriginalLocalScale;
    private Quaternion portraitOriginalLocalRotation;

    private void Awake()
    {
        InitializeButtons();
        if (customizationGroup == null && customizationPanel != null)
            customizationGroup = customizationPanel.GetComponent<CanvasGroup>();
        SetProfileGroups(1f, true);
        if (customizationGroup != null)
        {
            customizationGroup.alpha = 0f;
            customizationGroup.interactable = false;
            customizationGroup.blocksRaycasts = false;
        }
        // The authored panel starts inactive. Do not deactivate it in Awake:
        // Awake can run inside the first OpenCustomization SetActive call.
    }

    private void OnDisable()
    {
        // Almanac close buttons may disable the canvas without going through
        // Cancel. Always return the one shared portrait to the Profile page.
        if (isCustomizationOpen || isTransitioning)
        {
            if (previewMirror != null)
                previewMirror.EndPreview(true);
            RestorePortraitImmediately();
            SetProfileGroups(1f, true);
            isCustomizationOpen = false;
            isTransitioning = false;
        }

        transitionRoutine = null;
        if (customizationGroup != null)
        {
            customizationGroup.alpha = 0f;
            customizationGroup.interactable = false;
            customizationGroup.blocksRaycasts = false;
        }

        // If the Almanac canvas itself was closed, keep this sub-page closed
        // when the book is opened again.
        if (customizationPanel != null && customizationPanel.activeSelf)
            customizationPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        foreach (var entry in tabActions)
            if (entry.Key != null) entry.Key.onClick.RemoveListener(entry.Value);
        foreach (var entry in colorActions)
            if (entry.Key != null) entry.Key.onClick.RemoveListener(entry.Value);
    }

    public void OpenCustomization()
    {
        if (isTransitioning || isCustomizationOpen) return;
        if (!ResolvePreviewMirror())
        {
            Debug.LogError("[Customization] The shared portrait has no cosmetic display model. Assign its rotation target and PlayerCosmeticMirror.", this);
            return;
        }
        InitializeButtons();
        originalLoadout = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetCosmeticLoadoutCopy()
            : new CosmeticLoadoutData();
        previewLoadout = originalLoadout.Clone();
        ApplyDefaultSelections(previewLoadout);

        CapturePortraitLayout();

        if (customizationPanel != null)
        {
            customizationPanel.SetActive(true);
            customizationPanel.transform.SetAsLastSibling();
        }
        if (customizationGroup != null)
        {
            customizationGroup.alpha = 0f;
            customizationGroup.interactable = false;
            customizationGroup.blocksRaycasts = false;
        }
        ShowCategory(CosmeticCategory.Accessories);
        if (previewMirror != null) previewMirror.BeginPreview(previewLoadout);

        Canvas.ForceUpdateCanvases();
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(TransitionToCustomization());
    }

    public void CancelCustomization()
    {
        if (isTransitioning || !isCustomizationOpen) return;
        if (previewMirror != null)
            previewMirror.EndPreview(true);
        BeginReturnTransition();
    }

    public void SaveLook()
    {
        if (isTransitioning || !isCustomizationOpen || previewLoadout == null) return;
        if (PlayerDataManager.Instance != null)
        {
            if (!PlayerDataManager.Instance.SaveCosmeticLoadout(previewLoadout)) return;
        }
        else if (PlayerCosmetics.Instance != null)
            PlayerCosmetics.Instance.ApplyLoadout(previewLoadout);
        if (previewMirror != null)
            previewMirror.EndPreview(true);
        BeginReturnTransition();
    }

    public void ShowCategory(int category) => ShowCategory((CosmeticCategory)category);

    public void ShowCategory(CosmeticCategory category)
    {
        currentCategory = category;
        if (categoryTitle != null) categoryTitle.text = category.ToString().ToUpperInvariant();

        foreach (CosmeticCategoryTabBinding tab in categoryTabs)
            if (tab != null && tab.selectedVisual != null)
                tab.selectedVisual.SetActive(tab.category == category);

        List<CosmeticDefinition> filtered = cosmetics.FindAll(item =>
            item != null && item.category == category);
        string selectedID = previewLoadout != null ? previewLoadout.GetID(category) : string.Empty;
        selectedDefinition = filtered.Find(item =>
            string.Equals(item.PermanentID, selectedID, StringComparison.Ordinal));

        for (int i = 0; i < optionSlots.Count; i++)
        {
            CosmeticDefinition definition = i < filtered.Count ? filtered[i] : null;
            bool unlocked = IsUnlocked(definition);
            bool selected = definition != null &&
                string.Equals(definition.PermanentID, selectedID, StringComparison.Ordinal);
            if (optionSlots[i] != null)
                optionSlots[i].Bind(definition, unlocked, selected, SelectCosmetic);
        }
        RefreshColorSlots();
    }

    private void SelectCosmetic(CosmeticDefinition definition)
    {
        if (definition == null || previewLoadout == null || !IsUnlocked(definition)) return;
        selectedDefinition = definition;
        previewLoadout.SetID(definition.category, definition.PermanentID);
        ApplyPreview();
        ShowCategory(definition.category);
    }

    private void SelectColor(int index)
    {
        if (previewLoadout == null || selectedDefinition == null ||
            selectedDefinition.availableColors == null ||
            index < 0 || index >= selectedDefinition.availableColors.Length) return;

        previewLoadout.SetColor(currentCategory, selectedDefinition.availableColors[index]);
        ApplyPreview();
        RefreshColorSlots();
    }

    private void ApplyPreview()
    {
        if (previewMirror != null) previewMirror.UpdatePreview(previewLoadout);
    }

    // Resolve through the actual portrait, not a canvas search: PHOTOBOOTH is
    // a separate scene object and may be inactive while the book is closed.
    public bool ResolvePreviewMirror()
    {
        AlmanacPortraitRotator rotator = sharedPortrait != null
            ? sharedPortrait.GetComponent<AlmanacPortraitRotator>() : null;
        if (rotator != null && rotator.RotationTarget != null)
        {
            PlayerCosmeticMirror portraitMirror =
                rotator.RotationTarget.GetComponentInChildren<PlayerCosmeticMirror>(true);
            if (portraitMirror != null) previewMirror = portraitMirror;
        }
        // Older prefab copies did not wire the rotator either. Match the
        // portrait's RenderTexture to its camera within this scene only.
        RawImage portrait = sharedPortrait != null ? sharedPortrait.GetComponent<RawImage>() : null;
        if (previewMirror == null && portrait != null && portrait.texture is RenderTexture texture)
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.targetTexture != texture) continue;
                    PlayerCosmeticMirror cameraMirror = camera.GetComponentInParent<PlayerCosmeticMirror>(true);
                    if (cameraMirror != null)
                    {
                        previewMirror = cameraMirror;
                        return true;
                    }
                }
            }
        }
        return previewMirror != null;
    }

    private bool IsUnlocked(CosmeticDefinition definition)
    {
        return definition != null && (definition.unlockedByDefault ||
            (previewLoadout != null && string.Equals(
                previewLoadout.GetID(definition.category), definition.PermanentID,
                StringComparison.Ordinal)) ||
            (PlayerDataManager.Instance != null &&
             PlayerDataManager.Instance.IsCosmeticUnlocked(definition.PermanentID)));
    }

    private void ApplyDefaultSelections(CosmeticLoadoutData loadout)
    {
        foreach (CosmeticCategory category in Enum.GetValues(typeof(CosmeticCategory)))
        {
            if (!string.IsNullOrWhiteSpace(loadout.GetID(category))) continue;
            CosmeticDefinition defaultItem = cosmetics.Find(item =>
                item != null && item.category == category && item.unlockedByDefault);
            if (defaultItem != null) loadout.SetID(category, defaultItem.PermanentID);
        }
    }

    private void RefreshColorSlots()
    {
        Color[] colors = selectedDefinition != null ? selectedDefinition.availableColors : null;
        Color selectedColor = previewLoadout != null
            ? previewLoadout.GetColor(currentCategory)
            : Color.clear;

        for (int i = 0; i < colorSlots.Count; i++)
        {
            CosmeticColorButtonBinding slot = colorSlots[i];
            if (slot == null || slot.button == null) continue;
            bool visible = colors != null && i < colors.Length;
            slot.button.gameObject.SetActive(visible);
            if (!visible) continue;
            if (slot.colorImage != null)
                slot.colorImage.color = colors[i].a <= 0.001f ? Color.white : colors[i];
            if (slot.selectedVisual != null)
                slot.selectedVisual.SetActive(ColorDistance(colors[i], selectedColor) < 0.02f);
        }
    }

    private void InitializeButtons()
    {
        if (initialized) return;
        initialized = true;

        foreach (CosmeticCategoryTabBinding tab in categoryTabs)
        {
            if (tab == null || tab.button == null) continue;
            CosmeticCategory captured = tab.category;
            UnityEngine.Events.UnityAction action = () => ShowCategory(captured);
            tab.button.onClick.AddListener(action);
            tabActions[tab.button] = action;
        }

        for (int i = 0; i < colorSlots.Count; i++)
        {
            CosmeticColorButtonBinding slot = colorSlots[i];
            if (slot == null || slot.button == null) continue;
            int captured = i;
            UnityEngine.Events.UnityAction action = () => SelectColor(captured);
            slot.button.onClick.AddListener(action);
            colorActions[slot.button] = action;
        }
    }

    private IEnumerator TransitionToCustomization()
    {
        isTransitioning = true;
        isCustomizationOpen = true;
        PreparePortraitForTransition(profilePortraitAnchor);

        Vector2 startPosition = sharedPortrait != null ? sharedPortrait.anchoredPosition : Vector2.zero;
        Vector2 startSize = sharedPortrait != null ? sharedPortrait.sizeDelta : Vector2.zero;
        GetLayerRect(customizationPortraitAnchor, out Vector2 targetPosition, out _);
        // The same RawImage must keep the Profile dimensions. Resizing it to a
        // tall wardrobe frame changes its aspect and makes the model look stretched.
        Vector2 targetSize = startSize;

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float time = Mathf.Clamp01(elapsed / transitionDuration);
            float eased = EaseInOut(time);
            SetPortraitRect(Vector2.LerpUnclamped(startPosition, targetPosition, eased),
                Vector2.LerpUnclamped(startSize, targetSize, eased));

            SetProfileGroups(1f - eased, false);
            if (customizationGroup != null)
            {
                float fadeTime = Mathf.InverseLerp(controlsFadeDelay, 1f, time);
                customizationGroup.alpha = EaseOut(fadeTime);
            }
            yield return null;
        }

        // Keep the shared portrait on the transition layer while editing. This
        // preserves the exact Profile dimensions instead of stretching to fill
        // the customization placeholder.
        SetProfileGroups(0f, false);
        if (customizationGroup != null)
        {
            customizationGroup.alpha = 1f;
            customizationGroup.interactable = true;
            customizationGroup.blocksRaycasts = true;
        }
        SetTransitionPortraitInteraction(true);
        isTransitioning = false;
        transitionRoutine = null;
    }

    private void BeginReturnTransition()
    {
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(TransitionToProfile());
    }

    private IEnumerator TransitionToProfile()
    {
        isTransitioning = true;
        SetTransitionPortraitInteraction(false);
        if (customizationGroup != null)
        {
            customizationGroup.interactable = false;
            customizationGroup.blocksRaycasts = false;
        }

        PreparePortraitForTransition(customizationPortraitAnchor);
        Vector2 startPosition = sharedPortrait != null ? sharedPortrait.anchoredPosition : Vector2.zero;
        Vector2 startSize = sharedPortrait != null ? sharedPortrait.sizeDelta : Vector2.zero;
        GetLayerRect(profilePortraitAnchor, out Vector2 targetPosition, out Vector2 targetSize);

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float time = Mathf.Clamp01(elapsed / transitionDuration);
            float eased = EaseInOut(time);
            SetPortraitRect(Vector2.LerpUnclamped(startPosition, targetPosition, eased),
                Vector2.LerpUnclamped(startSize, targetSize, eased));
            SetProfileGroups(eased, false);
            if (customizationGroup != null)
                customizationGroup.alpha = 1f - EaseIn(time);
            yield return null;
        }

        RestorePortraitImmediately();
        SetProfileGroups(1f, true);
        isCustomizationOpen = false;
        isTransitioning = false;
        transitionRoutine = null;

        if (customizationPanel != null) customizationPanel.SetActive(false);
        if (almanacPanel != null && almanacPanel.activeInHierarchy)
            almanacPanel.transform.SetAsLastSibling();
    }

    private void CapturePortraitLayout()
    {
        if (sharedPortrait == null || portraitOriginalParent != null) return;
        portraitOriginalParent = sharedPortrait.parent;
        portraitOriginalSiblingIndex = sharedPortrait.GetSiblingIndex();
        portraitOriginalAnchorMin = sharedPortrait.anchorMin;
        portraitOriginalAnchorMax = sharedPortrait.anchorMax;
        portraitOriginalPivot = sharedPortrait.pivot;
        portraitOriginalAnchoredPosition = sharedPortrait.anchoredPosition;
        portraitOriginalSizeDelta = sharedPortrait.sizeDelta;
        portraitOriginalLocalScale = sharedPortrait.localScale;
        portraitOriginalLocalRotation = sharedPortrait.localRotation;
    }

    private void PreparePortraitForTransition(RectTransform currentAnchor)
    {
        if (sharedPortrait == null || transitionLayer == null) return;
        GetLayerRect(sharedPortrait, out Vector2 position, out Vector2 size);
        if (currentAnchor != null && sharedPortrait.parent == currentAnchor)
            GetLayerRect(currentAnchor, out position, out size);

        transitionLayer.gameObject.SetActive(true);
        SetTransitionPortraitInteraction(false);
        transitionLayer.SetAsLastSibling();
        sharedPortrait.SetParent(transitionLayer, false);
        sharedPortrait.anchorMin = sharedPortrait.anchorMax = new Vector2(0.5f, 0.5f);
        sharedPortrait.pivot = new Vector2(0.5f, 0.5f);
        sharedPortrait.localScale = Vector3.one;
        sharedPortrait.localRotation = Quaternion.identity;
        SetPortraitRect(position, size);
        sharedPortrait.SetAsLastSibling();
    }

    private void AttachPortraitTo(RectTransform anchor)
    {
        if (sharedPortrait == null || anchor == null) return;
        sharedPortrait.SetParent(anchor, false);
        sharedPortrait.anchorMin = Vector2.zero;
        sharedPortrait.anchorMax = Vector2.one;
        sharedPortrait.pivot = new Vector2(0.5f, 0.5f);
        sharedPortrait.offsetMin = Vector2.zero;
        sharedPortrait.offsetMax = Vector2.zero;
        sharedPortrait.localScale = Vector3.one;
        sharedPortrait.localRotation = Quaternion.identity;
        if (transitionLayer != null) transitionLayer.gameObject.SetActive(false);
    }

    private void SetTransitionPortraitInteraction(bool enabled)
    {
        if (transitionLayer == null) return;
        CanvasGroup group = transitionLayer.GetComponent<CanvasGroup>();
        if (group == null) return;
        group.interactable = enabled;
        group.blocksRaycasts = enabled;
    }

    private void RestorePortraitImmediately()
    {
        if (sharedPortrait == null || portraitOriginalParent == null) return;
        sharedPortrait.SetParent(portraitOriginalParent, false);
        sharedPortrait.SetSiblingIndex(Mathf.Clamp(portraitOriginalSiblingIndex, 0,
            Mathf.Max(0, portraitOriginalParent.childCount - 1)));
        sharedPortrait.anchorMin = portraitOriginalAnchorMin;
        sharedPortrait.anchorMax = portraitOriginalAnchorMax;
        sharedPortrait.pivot = portraitOriginalPivot;
        sharedPortrait.anchoredPosition = portraitOriginalAnchoredPosition;
        sharedPortrait.sizeDelta = portraitOriginalSizeDelta;
        sharedPortrait.localScale = portraitOriginalLocalScale;
        sharedPortrait.localRotation = portraitOriginalLocalRotation;
        if (transitionLayer != null) transitionLayer.gameObject.SetActive(false);
    }

    private void GetLayerRect(RectTransform source, out Vector2 position, out Vector2 size)
    {
        position = Vector2.zero;
        size = Vector2.zero;
        if (source == null || transitionLayer == null) return;

        Vector3 worldCenter = source.TransformPoint(source.rect.center);
        Vector3 localCenter = transitionLayer.InverseTransformPoint(worldCenter);
        position = new Vector2(localCenter.x, localCenter.y);

        Vector3 sourceScale = source.lossyScale;
        Vector3 layerScale = transitionLayer.lossyScale;
        size = new Vector2(
            source.rect.width * Mathf.Abs(sourceScale.x) / Mathf.Max(0.0001f, Mathf.Abs(layerScale.x)),
            source.rect.height * Mathf.Abs(sourceScale.y) / Mathf.Max(0.0001f, Mathf.Abs(layerScale.y)));
    }

    private void SetPortraitRect(Vector2 position, Vector2 size)
    {
        if (sharedPortrait == null) return;
        sharedPortrait.anchoredPosition = position;
        sharedPortrait.sizeDelta = size;
    }

    private void SetProfileGroups(float alpha, bool interactable)
    {
        foreach (CanvasGroup group in profileGroups)
        {
            if (group == null) continue;
            group.alpha = alpha;
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }
    }

    private static float EaseInOut(float time) =>
        time < 0.5f ? 4f * time * time * time : 1f - Mathf.Pow(-2f * time + 2f, 3f) * 0.5f;

    private static float EaseIn(float time) => time * time * time;
    private static float EaseOut(float time) => 1f - Mathf.Pow(1f - time, 3f);

    private static float ColorDistance(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) +
        Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
}
