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
    [HideInInspector, SerializeField] private List<CosmeticColorButtonBinding> colorSlots = new List<CosmeticColorButtonBinding>();

    [Header("Authored Item Editor")]
    [SerializeField] private GameObject itemListPanel;
    [SerializeField] private GameObject selectedItemPanel;
    [SerializeField] private TMP_Text selectedItemTitle;
    [SerializeField] private TMP_Text materialHint;
    [SerializeField] private TMP_Text accessoryActionLabel;
    [SerializeField] private Image selectedItemIcon;
    [HideInInspector, SerializeField] private TMP_Dropdown materialDropdown;
    [SerializeField] private List<CosmeticMaterialColorRow> materialColorRows = new List<CosmeticMaterialColorRow>();
    [SerializeField] private ScrollRect materialColorScroll;
    [SerializeField] private Button accessoryActionButton;
    private List<CosmeticBindingUtility.MaterialSlot> selectedMaterials = new List<CosmeticBindingUtility.MaterialSlot>();

    private readonly Dictionary<Button, UnityEngine.Events.UnityAction> tabActions =
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

    public bool IsCustomizationReady => isCustomizationOpen && !isTransitioning &&
        customizationPanel != null && customizationPanel.activeInHierarchy;
    public bool IsCustomizationOpen => isCustomizationOpen || isTransitioning;
    public CosmeticCategory CurrentCategory => currentCategory;

    public bool IsAccessorySelectedInPreview(string cosmeticId)
    {
        return previewLoadout != null && previewLoadout.IsAccessoryEquipped(cosmeticId);
    }

    public RectTransform GetCosmeticOptionTarget(string cosmeticId)
    {
        foreach (CosmeticOptionButton slot in optionSlots)
        {
            if (slot != null && string.Equals(slot.CosmeticId, cosmeticId,
                    StringComparison.OrdinalIgnoreCase))
                return slot.ButtonTarget;
        }
        return null;
    }

    public RectTransform GetSaveLookTarget()
    {
        if (customizationPanel == null) return null;
        foreach (Button button in customizationPanel.GetComponentsInChildren<Button>(true))
        {
            if (button != null && button.name == "SaveLookButton")
                return button.GetComponent<RectTransform>();
        }
        return null;
    }

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
    }

    public void OpenCustomization()
    {
        if (AlmanacManager.Instance == null || !AlmanacManager.Instance.CanEditPlayer)
            return;
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

        if (sharedPortrait != null) sharedPortrait.GetComponent<AlmanacPortraitFit>()?.FitNow();
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

    public void RefreshItemOwnership()
    {
        if (isCustomizationOpen) ShowCategory(currentCategory);
    }

    public void ShowCategory(CosmeticCategory category)
    {
        CloseItemEditor();
        currentCategory = category;
        if (categoryTitle != null) categoryTitle.text = category.ToString().ToUpperInvariant();

        foreach (CosmeticCategoryTabBinding tab in categoryTabs)
            if (tab != null && tab.selectedVisual != null)
                tab.selectedVisual.SetActive(tab.category == category);

        List<CosmeticDefinition> filtered = cosmetics.FindAll(item =>
            item != null && item.category == category);
        selectedDefinition = filtered.Find(IsSelected);

        for (int i = 0; i < optionSlots.Count; i++)
        {
            CosmeticDefinition definition = i < filtered.Count ? filtered[i] : null;
            bool unlocked = IsUnlocked(definition);
            bool selected = IsSelected(definition);
            if (optionSlots[i] != null)
                optionSlots[i].Bind(definition, unlocked, selected, SelectCosmetic);
        }
        RefreshColorSlots();
    }

    private void SelectCosmetic(CosmeticDefinition definition)
    {
        if (definition == null || previewLoadout == null || !IsUnlocked(definition)) return;
        selectedDefinition = definition;
        if (definition.category == CosmeticCategory.Accessories)
        {
            // Opening an already-equipped item's editor must not unequip it.
            // Removal is a separate, explicit action in the item panel.
            previewLoadout.SetAccessoryEquipped(definition.PermanentID, true);
        }
        else
        {
            previewLoadout.SetID(definition.category, definition.PermanentID);
        }
        ApplyPreview();
        ShowCategory(definition.category);
        selectedDefinition = definition;
        OpenItemEditor();
    }

    private void OpenItemEditor()
    {
        if (selectedDefinition == null || selectedItemPanel == null) return;
        if (itemListPanel != null) itemListPanel.SetActive(false);
        selectedItemPanel.SetActive(true);
        if (selectedItemTitle != null) selectedItemTitle.text = selectedDefinition.displayName;
        if (selectedItemIcon != null)
        {
            selectedItemIcon.sprite = selectedDefinition.icon;
            selectedItemIcon.enabled = selectedDefinition.icon != null;
            selectedItemIcon.preserveAspect = true;
        }
        CosmeticModelBinding binding = previewMirror != null ? previewMirror.cosmeticBindings.Find(item =>
            item != null && string.Equals(item.cosmeticID, selectedDefinition.PermanentID,
                StringComparison.OrdinalIgnoreCase)) : null;
        selectedMaterials = CosmeticBindingUtility.GetMaterialSlots(binding);
        if (selectedMaterials.Count > materialColorRows.Count)
            Debug.LogError("[Customization] Author more color rows for this item's materials.", this);
        if (materialHint != null) materialHint.text = selectedMaterials.Count == 0
            ? "This item has no colors to edit."
            : "Pick a color for each part. Changes stay in your preview until Save Look.";
        UpdateAccessoryAction();
        RefreshColorSlots();
        if (materialColorScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(materialColorScroll.content);
            materialColorScroll.StopMovement();
            materialColorScroll.verticalNormalizedPosition = 1;
        }
    }

    public void CloseItemEditor()
    {
        if (selectedItemPanel != null) selectedItemPanel.SetActive(false);
        if (itemListPanel != null) itemListPanel.SetActive(true);
        selectedMaterials.Clear();
    }

    public void ToggleSelectedAccessory()
    {
        if (previewLoadout == null || selectedDefinition == null ||
            selectedDefinition.category != CosmeticCategory.Accessories || !IsUnlocked(selectedDefinition)) return;
        previewLoadout.SetAccessoryEquipped(selectedDefinition.PermanentID,
            !previewLoadout.IsAccessoryEquipped(selectedDefinition.PermanentID));
        ApplyPreview();
        UpdateAccessoryAction();
    }

    public void ResetItemColors()
    {
        if (previewLoadout == null || selectedDefinition == null) return;
        foreach (var slot in selectedMaterials)
            previewLoadout.SetMaterialColor(selectedDefinition.PermanentID, slot.Key, Color.clear);
        ApplyPreview(); RefreshColorSlots();
    }

    private void UpdateAccessoryAction()
    {
        bool accessory = selectedDefinition != null && selectedDefinition.category == CosmeticCategory.Accessories &&
            !string.Equals(selectedDefinition.PermanentID, "Accessory_None", StringComparison.OrdinalIgnoreCase);
        if (accessoryActionButton != null) accessoryActionButton.gameObject.SetActive(accessory);
        if (accessoryActionLabel != null && accessory)
            accessoryActionLabel.text = previewLoadout.IsAccessoryEquipped(selectedDefinition.PermanentID) ? "UNEQUIP ITEM" : "EQUIP ITEM";
    }

    private void SelectMaterialColor(int materialIndex, int index)
    {
        if (previewLoadout == null || selectedDefinition == null ||
            selectedDefinition.availableColors == null ||
            index < 0 || index >= selectedDefinition.availableColors.Length ||
            materialIndex < 0 || materialIndex >= selectedMaterials.Count) return;

        previewLoadout.SetMaterialColor(selectedDefinition.PermanentID,
            selectedMaterials[materialIndex].Key, selectedDefinition.availableColors[index]);
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
            IsSelected(definition) ||
            (PlayerDataManager.Instance != null &&
             PlayerDataManager.Instance.IsCosmeticUnlocked(definition.PermanentID)));
    }

    private bool IsSelected(CosmeticDefinition definition)
    {
        if (definition == null || previewLoadout == null) return false;
        if (definition.category == CosmeticCategory.Accessories)
            return previewLoadout.IsAccessoryEquipped(definition.PermanentID);
        return string.Equals(previewLoadout.GetID(definition.category),
            definition.PermanentID, StringComparison.Ordinal);
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
        for (int i = 0; i < materialColorRows.Count; i++)
        {
            CosmeticMaterialColorRow row = materialColorRows[i];
            if (row == null) continue;
            bool visible = i < selectedMaterials.Count;
            row.gameObject.SetActive(visible);
            if (!visible) continue;
            Color selected = previewLoadout != null && selectedDefinition != null
                ? previewLoadout.GetMaterialColor(selectedDefinition.PermanentID, selectedMaterials[i].Key, currentCategory)
                : Color.clear;
            row.Bind(i, colors, selected, SelectMaterialColor);
        }
    }

    private void InitializeButtons()
    {
        if (initialized) return;
        initialized = true;
        if (materialDropdown != null) materialDropdown.gameObject.SetActive(false);
        foreach (var old in colorSlots)
            if (old != null && old.button != null) old.button.gameObject.SetActive(false);

        foreach (CosmeticCategoryTabBinding tab in categoryTabs)
        {
            if (tab == null || tab.button == null) continue;
            CosmeticCategory captured = tab.category;
            UnityEngine.Events.UnityAction action = () => ShowCategory(captured);
            tab.button.onClick.AddListener(action);
            tabActions[tab.button] = action;
        }

    }

    private IEnumerator TransitionToCustomization()
    {
        isTransitioning = true;
        isCustomizationOpen = true;
        PreparePortraitForTransition(profilePortraitAnchor);

        Vector2 startPosition = sharedPortrait != null ? sharedPortrait.anchoredPosition : Vector2.zero;
        Vector2 startSize = sharedPortrait != null ? sharedPortrait.sizeDelta : Vector2.zero;
        GetLayerRect(customizationPortraitAnchor, out Vector2 targetPosition, out Vector2 availableSize);
        // Fit the shared image without changing its aspect ratio. The authored
        // wardrobe frame is responsive and can be narrower than the Profile.
        Vector2 targetSize = FitPortraitSize(availableSize);

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
        targetSize = FitPortraitSize(targetSize);

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
        sharedPortrait.GetComponent<AlmanacPortraitFit>()?.FitNow();
        if (transitionLayer != null) transitionLayer.gameObject.SetActive(false);
    }

    private Vector2 FitPortraitSize(Vector2 available)
    {
        RawImage image = sharedPortrait != null ? sharedPortrait.GetComponent<RawImage>() : null;
        float aspect = image != null && image.texture != null
            ? (float)image.texture.width / Mathf.Max(1, image.texture.height)
            : sharedPortrait != null && sharedPortrait.rect.height > 0 ? sharedPortrait.rect.width / sharedPortrait.rect.height : 1f;
        return available.x / Mathf.Max(.01f, available.y) > aspect
            ? new Vector2(available.y * aspect, available.y) : new Vector2(available.x, available.x / Mathf.Max(.01f, aspect));
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
