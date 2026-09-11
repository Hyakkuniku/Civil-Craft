using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections.Generic;

public class ContractAlmanacTab : MonoBehaviour
{
    private static readonly Color Ink = new Color(0.25f, 0.16f, 0.10f, 1f);
    private static readonly Color MutedInk = new Color(0.47f, 0.35f, 0.25f, 1f);
    private static readonly Color Accent = new Color(0.66f, 0.36f, 0.13f, 1f);
    private static readonly Color CardTint = new Color(0.91f, 0.84f, 0.72f, 0.72f);
    private static readonly Color EarnedStar = new Color(0.91f, 0.58f, 0.10f, 1f);
    private static readonly Color UnearnedStar = new Color(0.63f, 0.57f, 0.48f, 0.55f);
    private const string MinimapFeatureId = "minimap";

    [Header("Master Contract List")]
    public List<ContractSO> allGameContracts;

    [Header("Left Page (Photo)")]
    public RawImage snapshotImage;
    public TextMeshProUGUI snapshotCaptionText;

    [Header("Right Page (Details)")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI clientText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI rewardsText;

    [Header("Pagination")]
    public TextMeshProUGUI pageCounterText;

    [Header("Presentation")]
    [SerializeField, Min(0.1f)] private float pageTransitionDuration = 0.24f;
    [SerializeField, Min(0f)] private float pageSlideDistance = 34f;

    private List<ContractSO> completedContractsList = new List<ContractSO>();
    private readonly List<AnimatedElement> animatedElements = new List<AnimatedElement>();
    private int currentIndex = 0;
    private bool presentationBuilt;
    private bool isChangingPage;
    private Coroutine pageTransition;
    private Texture2D loadedSnapshot;
    private readonly ContractAlmanacStarGraphic[] starIcons = new ContractAlmanacStarGraphic[3];
    private RectTransform starPresentationRoot;
    private TextMeshProUGUI starSummaryText;
    private Button seeInMapButton;
    private ContractSO displayedContract;

    private sealed class AnimatedElement
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Vector2 homePosition;
    }

    private void Awake()
    {
        EnsurePresentation();
    }

    private void OnEnable()
    {
        EnsurePresentation();
        ResetAnimatedElements();

        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.RegisterContracts(allGameContracts);
            PlayerDataManager.Instance.CheckAllAchievements();
            PlayerDataManager.Instance.OnMinimapUnlockChanged += HandleMapUnlockChanged;
            PlayerDataManager.Instance.OnFeatureUnlocksChanged += HandleMapUnlockChanged;
        }

        // Listen to the Almanac Manager!
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.OnCategoryChanged += CheckIfActiveTab;
        }
        
        // Run a manual check just in case we were opened directly
        CheckIfActiveTab(0); 
    }

    private void OnDisable()
    {
        if (pageTransition != null)
        {
            StopCoroutine(pageTransition);
            pageTransition = null;
        }
        isChangingPage = false;
        ResetAnimatedElements();

        // Stop listening and release the buttons if we get turned off
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.OnCategoryChanged -= CheckIfActiveTab;
            AlmanacManager.Instance.DisableVirtualPagination(HandleVirtualPageTurn);
        }

        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnMinimapUnlockChanged -= HandleMapUnlockChanged;
            PlayerDataManager.Instance.OnFeatureUnlocksChanged -= HandleMapUnlockChanged;
        }
    }

    private void OnDestroy()
    {
        ReleaseLoadedSnapshot();
    }

    private void CheckIfActiveTab(int categoryIndex)
    {
        // THE FIX: If our Title Text is physically visible on the screen, that means we are the active tab!
        if (titleText != null && titleText.gameObject.activeInHierarchy)
        {
            if (AlmanacManager.Instance != null)
            {
                AlmanacManager.Instance.EnableVirtualPagination(HandleVirtualPageTurn);
            }
            RefreshContractList();
        }
    }

    private void HandleVirtualPageTurn(bool goingForward)
    {
        if (isChangingPage) return;
        if (goingForward) ShowNextContract();
        else ShowPreviousContract();
    }

    public void RefreshContractList()
    {
        if (PlayerDataManager.Instance == null) return;

        completedContractsList.Clear();
        foreach (ContractSO contract in allGameContracts)
        {
            if (contract != null && PlayerDataManager.Instance.IsContractCompleted(contract.ContractID))
            {
                completedContractsList.Add(contract);
            }
        }

        if (completedContractsList.Count == 0)
        {
            ClearDetails();
        }
        else
        {
            currentIndex = 0;
            DisplayContract(currentIndex);
            PlayEntranceAnimation();
        }
    }

    public void ShowNextContract()
    {
        if (!isChangingPage && currentIndex < completedContractsList.Count - 1)
        {
            ChangeContract(currentIndex + 1, 1f);
        }
    }

    public void ShowPreviousContract()
    {
        if (!isChangingPage && currentIndex > 0)
        {
            ChangeContract(currentIndex - 1, -1f);
        }
    }

    private void ChangeContract(int nextIndex, float direction)
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            currentIndex = nextIndex;
            DisplayContract(currentIndex);
            return;
        }

        pageTransition = StartCoroutine(ChangeContractRoutine(nextIndex, direction));
    }

    private IEnumerator ChangeContractRoutine(int nextIndex, float direction)
    {
        isChangingPage = true;
        yield return AnimateElements(1f, 0f, 0f, -direction * pageSlideDistance * 0.55f,
            pageTransitionDuration * 0.42f);

        currentIndex = nextIndex;
        DisplayContract(currentIndex);
        SetAnimatedElements(0f, direction * pageSlideDistance);

        yield return AnimateElements(0f, 1f, direction * pageSlideDistance, 0f,
            pageTransitionDuration * 0.58f);

        ResetAnimatedElements();
        isChangingPage = false;
        pageTransition = null;
    }

    private void DisplayContract(int index)
    {
        ContractSO contract = completedContractsList[index];
        displayedContract = contract;

        if (titleText != null) titleText.text = contract.name;
        string clientName = string.IsNullOrWhiteSpace(contract.clientName) ? "UNKNOWN CLIENT" : contract.clientName.Trim();
        if (clientText != null) clientText.text = "COMMISSIONED BY  /  " + clientName.ToUpperInvariant();
        if (descriptionText != null) descriptionText.text = contract.jobDescription;
        if (rewardsText != null)
            rewardsText.text = "<b>" + contract.goldReward.ToString("N0") + " G</b>   CONTRACT PAY\n" +
                               "<b>" + contract.expReward.ToString("N0") + " XP</b>   EXPERIENCE";
        UpdateStarDisplay(contract);
        UpdateSeeInMapVisibility();

        if (snapshotImage != null)
        {
            string photoPath = Application.persistentDataPath + "/" + contract.ContractID + "_photo.png";
            if (!File.Exists(photoPath))
            {
                // Compatibility with bridge photos saved before stable contract IDs existed.
                photoPath = Application.persistentDataPath + "/" + contract.name + "_photo.png";
            }
            
            if (File.Exists(photoPath))
            {
                ReleaseLoadedSnapshot();
                byte[] bytes = File.ReadAllBytes(photoPath);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                
                snapshotImage.texture = tex;
                snapshotImage.color = Color.white;
                loadedSnapshot = tex;
                if (snapshotCaptionText != null) snapshotCaptionText.text = clientName + "'s Bridge";
            }
            else
            {
                ReleaseLoadedSnapshot();
                snapshotImage.texture = null;
                snapshotImage.color = Color.black; 
                if (snapshotCaptionText != null) snapshotCaptionText.text = "Photo Missing";
            }
        }

        if (pageCounterText != null)
            pageCounterText.text = "PROJECT  " + (index + 1).ToString("00") + "  /  " + completedContractsList.Count.ToString("00");

        // Tell the Almanac Manager if the virtual buttons should be greyed out!
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.virtualHasPrev = (index > 0);
            AlmanacManager.Instance.virtualHasNext = (index < completedContractsList.Count - 1);
            AlmanacManager.Instance.ForceUpdatePaginationUI();
        }
    }

    private void ClearDetails()
    {
        displayedContract = null;
        ReleaseLoadedSnapshot();
        if (titleText != null) titleText.text = "No completed contracts yet";
        if (clientText != null) clientText.text = "";
        if (descriptionText != null) descriptionText.text = "Complete a contract to add its project record and bridge photograph to this archive.";
        if (rewardsText != null) rewardsText.text = "";
        
        if (snapshotImage != null) { snapshotImage.texture = null; snapshotImage.color = new Color(0,0,0,0); }
        if (snapshotCaptionText != null) snapshotCaptionText.text = "";
        if (pageCounterText != null) pageCounterText.text = "PROJECT  00  /  00";
        UpdateStarDisplay(null);
        UpdateSeeInMapVisibility();

        // Grey out the arrows since there are 0 contracts!
        if (AlmanacManager.Instance != null)
        {
            AlmanacManager.Instance.virtualHasPrev = false;
            AlmanacManager.Instance.virtualHasNext = false;
            AlmanacManager.Instance.ForceUpdatePaginationUI();
        }
    }

    private void EnsurePresentation()
    {
        if (presentationBuilt || snapshotImage == null || snapshotCaptionText == null ||
            titleText == null || clientText == null || descriptionText == null ||
            rewardsText == null || pageCounterText == null)
            return;

        presentationBuilt = true;

        RectTransform photoFrame = snapshotImage.transform.parent as RectTransform;
        RectTransform leftRoot = snapshotCaptionText.transform.parent as RectTransform;
        RectTransform rightRoot = titleText.transform.parent as RectTransform;

        if (photoFrame != null)
        {
            SetNormalizedRect(photoFrame, new Vector2(0.10f, 0.31f), new Vector2(0.90f, 0.76f));
            Image frameImage = photoFrame.GetComponent<Image>();
            if (frameImage != null) frameImage.color = new Color(0.98f, 0.95f, 0.88f, 1f);

            Outline frameOutline = photoFrame.GetComponent<Outline>();
            if (frameOutline == null) frameOutline = photoFrame.gameObject.AddComponent<Outline>();
            frameOutline.effectColor = new Color(0.35f, 0.21f, 0.12f, 0.34f);
            frameOutline.effectDistance = new Vector2(3f, -3f);
        }

        RectTransform snapshotRect = snapshotImage.rectTransform;
        Stretch(snapshotRect, 14f, 14f, 14f, 14f);
        snapshotImage.raycastTarget = false;

        ConfigureText(snapshotCaptionText, 32f, 25f, 36f, FontStyles.Bold,
            TextAlignmentOptions.Top, Ink);
        SetNormalizedRect(snapshotCaptionText.rectTransform,
            new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.28f));

        ConfigureText(titleText, 44f, 31f, 46f, FontStyles.Bold,
            TextAlignmentOptions.TopLeft, Ink);
        SetNormalizedRect(titleText.rectTransform,
            new Vector2(0.11f, 0.75f), new Vector2(0.92f, 0.90f));

        ConfigureText(clientText, 18f, 15f, 20f, FontStyles.Bold,
            TextAlignmentOptions.TopLeft, Accent);
        SetNormalizedRect(clientText.rectTransform,
            new Vector2(0.11f, 0.66f), new Vector2(0.92f, 0.73f));

        ConfigureText(descriptionText, 27f, 21f, 30f, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, Ink);
        descriptionText.enableWordWrapping = true;
        descriptionText.lineSpacing = 7f;
        SetNormalizedRect(descriptionText.rectTransform,
            new Vector2(0.11f, 0.39f), new Vector2(0.90f, 0.59f));

        ConfigureText(rewardsText, 21f, 17f, 23f, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, Ink);
        SetNormalizedRect(rewardsText.rectTransform,
            new Vector2(0.14f, 0.19f), new Vector2(0.52f, 0.31f));

        ConfigureText(pageCounterText, 16f, 14f, 18f, FontStyles.Bold,
            TextAlignmentOptions.BottomRight, MutedInk);
        SetNormalizedRect(pageCounterText.rectTransform,
            new Vector2(0.56f, 0.07f), new Vector2(0.90f, 0.13f));

        if (leftRoot != null) BuildLeftDecor(leftRoot, snapshotCaptionText);
        if (rightRoot != null)
        {
            BuildRightDecor(rightRoot, titleText);
            BuildContractResultAndMapAction(rightRoot, titleText);
        }

        RegisterAnimatedElement(photoFrame);
        RegisterAnimatedElement(snapshotCaptionText.rectTransform);
        RegisterAnimatedElement(titleText.rectTransform);
        RegisterAnimatedElement(clientText.rectTransform);
        RegisterAnimatedElement(descriptionText.rectTransform);
        RegisterAnimatedElement(rewardsText.rectTransform);
        RegisterAnimatedElement(starPresentationRoot);
        if (seeInMapButton != null)
            RegisterAnimatedElement(seeInMapButton.transform as RectTransform);
        RegisterAnimatedElement(pageCounterText.rectTransform);

        UpdateStarDisplay(displayedContract);
        UpdateSeeInMapVisibility();
    }

    private void BuildLeftDecor(RectTransform parent, TMP_Text fontTemplate)
    {
        RectTransform decor = CreateDecorRoot(parent, "ContractPhotoDecor_Runtime");
        CreateDecorText(decor, "ArchiveLabel", "COMPLETED WORK  /  FIELD RECORD", fontTemplate,
            new Vector2(0.10f, 0.80f), new Vector2(0.90f, 0.85f), 15f,
            FontStyles.Bold, Accent, TextAlignmentOptions.MidlineLeft);
        CreateDecorImage(decor, "Rule", new Vector2(0.10f, 0.785f),
            new Vector2(0.90f, 0.789f), Accent);
    }

    private void BuildRightDecor(RectTransform parent, TMP_Text fontTemplate)
    {
        RectTransform decor = CreateDecorRoot(parent, "ContractDetailsDecor_Runtime");
        CreateDecorText(decor, "RecordLabel", "PROJECT RECORD  /  COMPLETE", fontTemplate,
            new Vector2(0.11f, 0.91f), new Vector2(0.92f, 0.96f), 15f,
            FontStyles.Bold, Accent, TextAlignmentOptions.MidlineLeft);
        CreateDecorImage(decor, "TitleMarker", new Vector2(0.075f, 0.755f),
            new Vector2(0.084f, 0.90f), Accent);
        CreateDecorImage(decor, "HeaderRule", new Vector2(0.11f, 0.635f),
            new Vector2(0.90f, 0.639f), new Color(Accent.r, Accent.g, Accent.b, 0.45f));
        CreateDecorText(decor, "BriefLabel", "PROJECT BRIEF", fontTemplate,
            new Vector2(0.11f, 0.59f), new Vector2(0.90f, 0.64f), 14f,
            FontStyles.Bold, MutedInk, TextAlignmentOptions.MidlineLeft);

        CreateDecorImage(decor, "RewardCard", new Vector2(0.075f, 0.17f),
            new Vector2(0.545f, 0.33f), CardTint);
        CreateDecorImage(decor, "RewardMarker", new Vector2(0.075f, 0.17f),
            new Vector2(0.088f, 0.33f), Accent);
        CreateDecorImage(decor, "StarCard", new Vector2(0.565f, 0.17f),
            new Vector2(0.92f, 0.33f), new Color(CardTint.r, CardTint.g, CardTint.b, 0.58f));
    }

    private void BuildContractResultAndMapAction(RectTransform parent, TMP_Text fontTemplate)
    {
        GameObject starObject = new GameObject("ContractStars_Runtime", typeof(RectTransform));
        starObject.layer = parent.gameObject.layer;
        starPresentationRoot = starObject.GetComponent<RectTransform>();
        starPresentationRoot.SetParent(parent, false);
        SetNormalizedRect(starPresentationRoot, new Vector2(0.565f, 0.17f), new Vector2(0.92f, 0.33f));

        starSummaryText = CreateRuntimeText(starPresentationRoot, "BestResult", "BEST RESULT   0 / 3",
            fontTemplate, new Vector2(0.08f, 0.68f), new Vector2(0.92f, 0.96f), 14f,
            FontStyles.Bold, MutedInk, TextAlignmentOptions.Center);

        for (int i = 0; i < starIcons.Length; i++)
        {
            GameObject star = new GameObject("Star_" + (i + 1), typeof(RectTransform),
                typeof(CanvasRenderer), typeof(ContractAlmanacStarGraphic));
            star.layer = parent.gameObject.layer;
            RectTransform rect = star.GetComponent<RectTransform>();
            rect.SetParent(starPresentationRoot, false);
            float center = 0.27f + i * 0.23f;
            rect.anchorMin = rect.anchorMax = new Vector2(center, 0.36f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(42f, 42f);

            starIcons[i] = star.GetComponent<ContractAlmanacStarGraphic>();
            starIcons[i].color = UnearnedStar;
            starIcons[i].raycastTarget = false;
        }

        GameObject buttonObject = new GameObject("SeeInMap_Runtime", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
        buttonObject.layer = parent.gameObject.layer;
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.SetParent(parent, false);
        SetNormalizedRect(buttonRect, new Vector2(0.075f, 0.065f), new Vector2(0.43f, 0.145f));

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = Accent;
        buttonImage.raycastTarget = true;
        Outline outline = buttonObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.30f, 0.17f, 0.09f, 0.55f);
        outline.effectDistance = new Vector2(2f, -2f);

        seeInMapButton = buttonObject.GetComponent<Button>();
        seeInMapButton.targetGraphic = buttonImage;
        ColorBlock colors = seeInMapButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.10f, 1.06f, 0.96f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
        seeInMapButton.colors = colors;
        seeInMapButton.onClick.AddListener(OpenDisplayedContractInMap);

        CreateRuntimeText(buttonRect, "Label", "SEE IN MAP", fontTemplate,
            Vector2.zero, Vector2.one, 18f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center);
    }

    private static TextMeshProUGUI CreateRuntimeText(RectTransform parent, string objectName,
        string value, TMP_Text template, Vector2 anchorMin, Vector2 anchorMax, float size,
        FontStyles style, Color color, TextAlignmentOptions alignment)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        child.layer = parent.gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetNormalizedRect(rect, anchorMin, anchorMax);

        TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
        if (template != null)
        {
            text.font = template.font;
            text.fontSharedMaterial = template.fontSharedMaterial;
        }
        text.text = value;
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(10f, size - 4f);
        text.fontSizeMax = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private void UpdateStarDisplay(ContractSO contract)
    {
        int earned = contract != null && PlayerDataManager.Instance != null
            ? Mathf.Clamp(PlayerDataManager.Instance.GetContractStars(contract.ContractID), 0, 3)
            : 0;

        if (starSummaryText != null)
            starSummaryText.text = "BEST RESULT   " + earned + " / 3";

        for (int i = 0; i < starIcons.Length; i++)
        {
            if (starIcons[i] != null)
                starIcons[i].color = i < earned ? EarnedStar : UnearnedStar;
        }
    }

    private void HandleMapUnlockChanged()
    {
        UpdateSeeInMapVisibility();
    }

    private void UpdateSeeInMapVisibility()
    {
        if (seeInMapButton != null)
            seeInMapButton.gameObject.SetActive(displayedContract != null && IsMapUnlocked());
    }

    private static bool IsMapUnlocked()
    {
        PlayerDataManager data = PlayerDataManager.Instance;
        return data != null && data.CurrentData != null &&
               (data.CurrentData.hasUnlockedMinimap || data.IsFeatureUnlocked(MinimapFeatureId));
    }

    private void OpenDisplayedContractInMap()
    {
        ContractSO contract = displayedContract;
        if (contract == null || !IsMapUnlocked()) return;

        System.Action openMap = () =>
        {
            ExpandedMinimapController map = FindObjectOfType<ExpandedMinimapController>(true);
            if (map == null || !map.OpenAtContract(contract))
            {
                Debug.LogWarning(
                    $"[ContractAlmanac] Could not show '{contract.name}' on the current scene's map.",
                    this);
            }
        };

        if (AlmanacManager.Instance != null)
            AlmanacManager.Instance.CloseAlmanacThen(openMap);
        else
            openMap();
    }

    private static RectTransform CreateDecorRoot(RectTransform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null) return existing as RectTransform;

        GameObject root = new GameObject(objectName, typeof(RectTransform));
        root.layer = parent.gameObject.layer;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Stretch(rect, 0f, 0f, 0f, 0f);
        rect.SetAsFirstSibling();
        return rect;
    }

    private static void CreateDecorImage(RectTransform parent, string objectName,
        Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.layer = parent.gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetNormalizedRect(rect, anchorMin, anchorMax);
        Image image = child.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static void CreateDecorText(RectTransform parent, string objectName, string value,
        TMP_Text template, Vector2 anchorMin, Vector2 anchorMax, float size,
        FontStyles style, Color color, TextAlignmentOptions alignment)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        child.layer = parent.gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetNormalizedRect(rect, anchorMin, anchorMax);

        TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
        if (template != null)
        {
            text.font = template.font;
            text.fontSharedMaterial = template.fontSharedMaterial;
        }
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
    }

    private static void ConfigureText(TMP_Text text, float size, float minSize, float maxSize,
        FontStyles style, TextAlignmentOptions alignment, Color color)
    {
        if (text == null) return;
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = minSize;
        text.fontSizeMax = maxSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
    }

    private static void SetNormalizedRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (rect == null) return;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void RegisterAnimatedElement(RectTransform rect)
    {
        if (rect == null) return;
        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
        animatedElements.Add(new AnimatedElement
        {
            rect = rect,
            group = group,
            homePosition = rect.anchoredPosition
        });
    }

    private void PlayEntranceAnimation()
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy || isChangingPage) return;
        if (pageTransition != null) StopCoroutine(pageTransition);
        SetAnimatedElements(0f, pageSlideDistance * 0.45f);
        pageTransition = StartCoroutine(EntranceRoutine());
    }

    private IEnumerator EntranceRoutine()
    {
        isChangingPage = true;
        yield return AnimateElements(0f, 1f, pageSlideDistance * 0.45f, 0f,
            pageTransitionDuration);
        ResetAnimatedElements();
        isChangingPage = false;
        pageTransition = null;
    }

    private IEnumerator AnimateElements(float fromAlpha, float toAlpha,
        float fromOffset, float toOffset, float duration)
    {
        duration = Mathf.Max(0.01f, duration);
        const float stagger = 0.018f;
        float total = duration + Mathf.Max(0, animatedElements.Count - 1) * stagger;
        float elapsed = 0f;

        while (elapsed < total)
        {
            elapsed += Time.unscaledDeltaTime;
            for (int i = 0; i < animatedElements.Count; i++)
            {
                AnimatedElement element = animatedElements[i];
                if (element == null || element.rect == null || element.group == null) continue;
                float local = Mathf.Clamp01((elapsed - i * stagger) / duration);
                float eased = 1f - Mathf.Pow(1f - local, 3f);
                element.group.alpha = Mathf.Lerp(fromAlpha, toAlpha, eased);
                element.rect.anchoredPosition = element.homePosition +
                    Vector2.right * Mathf.Lerp(fromOffset, toOffset, eased);
            }
            yield return null;
        }

        SetAnimatedElements(toAlpha, toOffset);
    }

    private void SetAnimatedElements(float alpha, float horizontalOffset)
    {
        foreach (AnimatedElement element in animatedElements)
        {
            if (element == null || element.rect == null || element.group == null) continue;
            element.group.alpha = alpha;
            element.rect.anchoredPosition = element.homePosition + Vector2.right * horizontalOffset;
        }
    }

    private void ResetAnimatedElements()
    {
        SetAnimatedElements(1f, 0f);
    }

    private void ReleaseLoadedSnapshot()
    {
        if (loadedSnapshot == null) return;
        if (snapshotImage != null && snapshotImage.texture == loadedSnapshot)
            snapshotImage.texture = null;
        Destroy(loadedSnapshot);
        loadedSnapshot = null;
    }
}

/// <summary>A font-independent five-point star for the contract archive.</summary>
[DisallowMultipleComponent]
internal sealed class ContractAlmanacStarGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.48f;

        vh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i < 10; i++)
        {
            float angle = (90f - i * 36f) * Mathf.Deg2Rad;
            float pointRadius = radius * (i % 2 == 0 ? 1f : 0.45f);
            Vector2 point = center +
                            new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * pointRadius;
            vh.AddVert(point, color, Vector2.zero);
        }

        for (int i = 0; i < 10; i++)
            vh.AddTriangle(0, i + 1, (i + 1) % 10 + 1);
    }
}
