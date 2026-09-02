using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum AlmanacTabType { General, Contracts, Lessons }

public enum TabVisibility { Normal, AlwaysHidden }

[System.Serializable]
public class AlmanacCategory
{
    public string categoryName;
    public AlmanacTabType tabType = AlmanacTabType.General;
    
    [Header("Visibility Control")]
    [Tooltip("Set to Always Hidden if you want to disable this tab while developing it!")]
    public TabVisibility visibilityMode = TabVisibility.Normal; 
    
    public Button tabButton;
    
    [Header("Tab Visuals")]
    public Sprite inactiveSprite;
    public Sprite activeSprite;

    [Header("Alert Integration")]
    public GameObject tabAlertIcon;

    [Header("Page Containers")]
    public Transform leftPageZone;
    public Transform rightPageZone;

    [HideInInspector] public List<GameObject> leftPages = new List<GameObject>();
    [HideInInspector] public List<GameObject> rightPages = new List<GameObject>();
}

public class AlmanacManager : MonoBehaviour
{
    public static AlmanacManager Instance { get; private set; }
    public GameObject Panel => almanacCanvas;

    [Header("HUD Integration")]
    public GameObject hudOpenButton; 
    public GameObject newAlertIcon; 

    [Header("Opening Animation")]
    public GameObject animationPanel; 
    public Image animationImage; 
    public Sprite[] bookOpenFrames; 
    [Min(0.01f)]
    public float frameRate = 0.03f; 
    private bool isAnimating = false;

    [Header("Tutorial Integration")]
    public TutorialSequence onFirstOpenTutorial;

    [Header("UI Management")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();
    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();

    [Header("UI Panels")]
    public GameObject almanacCanvas;

    [Header("Tabs & Categories")]
    public List<AlmanacCategory> categories = new List<AlmanacCategory>();
    public float selectedTabUpOffset = 15f; 
    public float tabTransitionSpeed = 10f;

    [Header("Pagination & Animation")]
    public Button prevButton;
    public Button nextButton;
    public float flipDuration = 0.25f; 

    [HideInInspector] public bool useVirtualPagination = false;
    [HideInInspector] public bool virtualHasNext = false;
    [HideInInspector] public bool virtualHasPrev = false;
    private System.Action<bool> OnVirtualPageTurn;
    
    public System.Action<int> OnCategoryChanged; 

    private int currentCategoryIndex = 0;
    private int currentSpreadIndex = 0; 
    private bool isFlipping = false;
    private InputManager menuInputManager;
    private bool restoreMovementAfterClose;
    private bool restoreLookAfterClose;
    private bool hasCapturedMenuInput;

    private Dictionary<RectTransform, float> originalTabYPositions = new Dictionary<RectTransform, float>();
    private Dictionary<RectTransform, float> targetTabYPositions = new Dictionary<RectTransform, float>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        // Scene UI must start closed regardless of saved-player state or script
        // execution order. Doing this in Awake avoids an active Almanac being visible
        // for a frame before Start, especially after clearing data and reloading.
        if (almanacCanvas != null) almanacCanvas.SetActive(false);
        if (animationPanel != null) animationPanel.SetActive(false);
        if (newAlertIcon != null) newAlertIcon.SetActive(false);

        InitializeBook();
    }

    private void Start()
    {
        if (almanacCanvas != null) almanacCanvas.SetActive(false);
        if (newAlertIcon != null) newAlertIcon.SetActive(false); 
        
        if (animationPanel != null) animationPanel.SetActive(false); 

        foreach (var cat in categories)
        {
            if (cat.tabAlertIcon != null) cat.tabAlertIcon.SetActive(false);
            
            if (cat.tabButton != null && cat.inactiveSprite != null)
            {
                Image tabImage = cat.tabButton.GetComponent<Image>();
                if (tabImage != null) tabImage.sprite = cat.inactiveSprite;
            }
        }

        if (PlayerDataManager.Instance != null)
        {
            bool playerHasAlmanac = PlayerDataManager.Instance.CurrentData.hasAlmanac;
            if (hudOpenButton != null) hudOpenButton.SetActive(playerHasAlmanac);

            PlayerDataManager.Instance.OnAlmanacUnlocked += ShowHudButton;
            PlayerDataManager.Instance.OnAlmanacAlertsChanged += RefreshPersistentAlerts;
        }

        EvaluateTabUnlocks();
        RefreshPersistentAlerts();
    }

    private void OnDestroy()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnAlmanacUnlocked -= ShowHudButton;
            PlayerDataManager.Instance.OnAlmanacAlertsChanged -= RefreshPersistentAlerts;
        }

        RestoreMenuInput();
    }

    private void ShowHudButton()
    {
        if (hudOpenButton != null) hudOpenButton.SetActive(true);
        RefreshPersistentAlerts();
    }

    private void Update()
    {
        foreach (var kvp in targetTabYPositions)
        {
            RectTransform tabRect = kvp.Key;
            float targetY = kvp.Value;
            
            Vector2 currentPos = tabRect.anchoredPosition;
            currentPos.y = Mathf.Lerp(currentPos.y, targetY, Time.deltaTime * tabTransitionSpeed);
            tabRect.anchoredPosition = currentPos;
        }

    }

    // ==========================================
    // UNLOCK & VISIBILITY LOGIC
    // ==========================================

    public void EvaluateTabUnlocks()
    {
        if (PlayerDataManager.Instance == null) return;
        PlayerData data = PlayerDataManager.Instance.CurrentData;
        bool changed = false;

        if (!data.hasUnlockedContractsTab && PlayerDataManager.Instance.HasAnyCompletedContract())
        {
            data.hasUnlockedContractsTab = true;
            changed = true;
        }

        if (!data.hasUnlockedLessonsTab && data.unlockedLessonIds != null && data.unlockedLessonIds.Count > 0)
        {
            data.hasUnlockedLessonsTab = true;
            changed = true;
        }

        if (changed) PlayerDataManager.Instance.SaveGame();
        RefreshTabVisibility(data);
    }

    private void RefreshTabVisibility(PlayerData data)
    {
        foreach (var cat in categories)
        {
            if (cat.tabButton != null)
            {
                bool shouldBeVisible = true;
                
                if (cat.tabType == AlmanacTabType.Contracts) shouldBeVisible = data.hasUnlockedContractsTab;
                if (cat.tabType == AlmanacTabType.Lessons) shouldBeVisible = data.hasUnlockedLessonsTab;
                if (!IsSupportedCategory(cat)) shouldBeVisible = false;

                if (cat.visibilityMode == TabVisibility.AlwaysHidden)
                {
                    shouldBeVisible = false;
                }

                cat.tabButton.gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    private void TriggerTabAlertByType(AlmanacTabType targetType)
    {
        if (PlayerDataManager.Instance == null) return;

        if (targetType == AlmanacTabType.Contracts)
            PlayerDataManager.Instance.MarkContractsAlmanacUnread();
        else if (targetType == AlmanacTabType.Lessons)
        {
            // Lesson unlock code owns creation of unread state. Do not manufacture
            // a false alert through a generic UI call.
            RefreshPersistentAlerts();
        }
    }

    public void RefreshPersistentAlerts()
    {
        PlayerData data = PlayerDataManager.Instance != null ? PlayerDataManager.Instance.CurrentData : null;
        bool hasAlmanac = data != null && data.hasAlmanac;
        bool bookIsOpen = almanacCanvas != null && almanacCanvas.activeSelf;
        bool hasGenuineUnreadContent = data != null &&
            (data.hasUnreadAlmanacUnlockAlert || data.hasUnreadContractsAlert ||
             data.hasUnreadLessonsAlert);

        if (data != null)
            RefreshTabVisibility(data);

        if (hudOpenButton != null) hudOpenButton.SetActive(hasAlmanac);
        if (newAlertIcon != null)
            newAlertIcon.SetActive(hasAlmanac && hasGenuineUnreadContent && !bookIsOpen);

        foreach (AlmanacCategory category in categories)
        {
            if (category.tabAlertIcon == null) continue;

            bool unread = false;
            if (data != null && category.tabType == AlmanacTabType.Contracts)
                unread = data.hasUnreadContractsAlert;
            else if (data != null && category.tabType == AlmanacTabType.Lessons)
                unread = data.hasUnreadLessonsAlert;

            category.tabAlertIcon.SetActive(hasAlmanac && unread && IsSupportedCategory(category) &&
                category.visibilityMode != TabVisibility.AlwaysHidden);
        }
    }

    // ==========================================

    private void InitializeBook()
    {
        for (int i = 0; i < categories.Count; i++)
        {
            int index = i; 
            AlmanacCategory cat = categories[i];

            if (!IsSupportedCategory(cat))
            {
                if (cat.tabButton != null) cat.tabButton.gameObject.SetActive(false);
                if (cat.tabAlertIcon != null) cat.tabAlertIcon.SetActive(false);
                continue;
            }

            if (cat.tabButton != null)
            {
                RectTransform rect = cat.tabButton.GetComponent<RectTransform>();
                originalTabYPositions[rect] = rect.anchoredPosition.y;
                targetTabYPositions[rect] = rect.anchoredPosition.y;
                cat.tabButton.onClick.AddListener(() => SelectCategory(index));
            }

            if (cat.leftPageZone != null)
            {
                foreach (Transform child in cat.leftPageZone)
                {
                    cat.leftPages.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                    
                    RectTransform pageRect = child.GetComponent<RectTransform>();
                    if (pageRect != null) pageRect.pivot = new Vector2(1f, 0.5f);
                }
            }

            if (cat.rightPageZone != null)
            {
                foreach (Transform child in cat.rightPageZone)
                {
                    cat.rightPages.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                    
                    RectTransform pageRect = child.GetComponent<RectTransform>();
                    if (pageRect != null) pageRect.pivot = new Vector2(0f, 0.5f);
                }
            }
        }

        if (prevButton != null) prevButton.onClick.AddListener(() => TurnPage(false));
        if (nextButton != null) nextButton.onClick.AddListener(() => TurnPage(true));
    }

    public void TriggerAlert()
    {
        // Alerts are derived from saved unread content. A generic UI call must not
        // manufacture an unread state when nothing new was added.
        RefreshPersistentAlerts();
    }

    public void TriggerTabAlert(string targetCategoryName)
    {
        foreach (AlmanacCategory category in categories)
        {
            if (category.categoryName == targetCategoryName && IsSupportedCategory(category))
            {
                TriggerTabAlertByType(category.tabType);
                break;
            }
        }
    }

    public void OpenAlmanac()
    {
        if (isAnimating) return; 
        
        EvaluateTabUnlocks(); 
        StartCoroutine(OpenAlmanacRoutine());
    }

    private IEnumerator OpenAlmanacRoutine()
    {
        isAnimating = true;
        CaptureAndDisableMenuInput();

        if (PlayerDataManager.Instance != null) PlayerDataManager.Instance.MarkAlmanacOpened();
        RefreshPersistentAlerts();

        bool coordinatedAnimation = UIPanelCoordinator.Instance != null && animationPanel != null;
        if (coordinatedAnimation)
            UIPanelCoordinator.Instance.OpenPanel(animationPanel, false);

        // CanyonCrossing's animation panel lives under MainCanvas, which is also in
        // uiElementsToHide. Hide its siblings instead of disabling its parent canvas.
        HideUiElementsPreservingAnimation();
        yield return PlayBookAnimation(true);

        if (UIPanelCoordinator.Instance != null)
        {
            // Restore before taking the coordinator snapshot so it can accurately
            // restore the HUD after the Almanac closes.
            RestoreTemporarilyHiddenPanels();
            if (coordinatedAnimation)
                UIPanelCoordinator.Instance.ClosePanel(animationPanel);
            UIPanelCoordinator.Instance.OpenPanel(almanacCanvas, false);
        }

        if (almanacCanvas != null) almanacCanvas.SetActive(true);
        
        SelectFirstVisibleCategory();

        if (onFirstOpenTutorial != null)
        {
            onFirstOpenTutorial.TryStartTutorial();
        }

        isAnimating = false;
    }
    
    private void SelectFirstVisibleCategory()
    {
        for (int i = 0; i < categories.Count; i++)
        {
            if (categories[i].tabButton != null && categories[i].tabButton.gameObject.activeInHierarchy)
            {
                SelectCategory(i);
                return;
            }
        }
        SelectCategory(0);
    }

    public void CloseAlmanac()
    {
        if (isAnimating) return;
        StartCoroutine(CloseAlmanacRoutine());
    }

    private IEnumerator CloseAlmanacRoutine()
    {
        isAnimating = true;

        if (almanacCanvas != null) almanacCanvas.SetActive(false);

        if (UIPanelCoordinator.Instance != null)
        {
            // Swap directly from the Almanac frame to an animation frame. Both
            // operations happen before rendering, preventing a one-frame HUD flash.
            UIPanelCoordinator.Instance.ClosePanel(almanacCanvas);
            if (animationPanel != null)
                UIPanelCoordinator.Instance.OpenPanel(animationPanel, false);
            HideUiElementsPreservingAnimation();
        }

        yield return PlayBookAnimation(false);
        RestoreTemporarilyHiddenPanels();
        if (UIPanelCoordinator.Instance != null && animationPanel != null)
            UIPanelCoordinator.Instance.ClosePanel(animationPanel);
        RestoreMenuInput();

        isAnimating = false;
    }

    private void CaptureAndDisableMenuInput()
    {
        menuInputManager = FindObjectOfType<InputManager>();
        if (menuInputManager == null) return;

        restoreMovementAfterClose = menuInputManager.IsPlayerInputEnabled;
        restoreLookAfterClose = menuInputManager.IsLookInputEnabled;
        hasCapturedMenuInput = true;
        menuInputManager.SetPlayerInputEnable(false);
        menuInputManager.SetLookEnabled(false);
    }

    private void RestoreMenuInput()
    {
        if (!hasCapturedMenuInput) return;
        if (menuInputManager != null)
        {
            menuInputManager.SetPlayerInputEnable(restoreMovementAfterClose);
            menuInputManager.SetLookEnabled(restoreLookAfterClose);
        }

        hasCapturedMenuInput = false;
        menuInputManager = null;
    }

    private IEnumerator PlayBookAnimation(bool opening)
    {
        if (animationPanel == null || animationImage == null ||
            bookOpenFrames == null || bookOpenFrames.Length == 0)
        {
            yield break;
        }

        int firstIndex = opening ? 0 : bookOpenFrames.Length - 1;
        animationImage.sprite = bookOpenFrames[firstIndex];
        animationPanel.SetActive(true);

        float delay = Mathf.Max(0.01f, frameRate);
        if (opening)
        {
            for (int i = 0; i < bookOpenFrames.Length; i++)
            {
                if (bookOpenFrames[i] != null) animationImage.sprite = bookOpenFrames[i];
                yield return new WaitForSecondsRealtime(delay);
            }
        }
        else
        {
            for (int i = bookOpenFrames.Length - 1; i >= 0; i--)
            {
                if (bookOpenFrames[i] != null) animationImage.sprite = bookOpenFrames[i];
                yield return new WaitForSecondsRealtime(delay);
            }
        }

        animationPanel.SetActive(false);
    }

    private void HideUiElementsPreservingAnimation()
    {
        temporarilyHiddenPanels.Clear();

        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui == null || !ui.activeSelf) continue;

            bool containsAnimation = animationPanel != null &&
                                     (ui == animationPanel ||
                                      animationPanel.transform.IsChildOf(ui.transform));
            if (!containsAnimation)
            {
                TrackAndHide(ui);
                continue;
            }

            // Keep the containing Canvas active. Only its animation branch remains.
            foreach (Transform child in ui.transform)
            {
                bool isAnimationBranch = child == animationPanel.transform ||
                                         animationPanel.transform.IsChildOf(child);
                if (!isAnimationBranch) TrackAndHide(child.gameObject);
            }
        }
    }

    private void TrackAndHide(GameObject target)
    {
        if (target == null || !target.activeSelf || temporarilyHiddenPanels.Contains(target)) return;
        temporarilyHiddenPanels.Add(target);
        target.SetActive(false);
    }

    private void RestoreTemporarilyHiddenPanels()
    {
        foreach (GameObject ui in temporarilyHiddenPanels)
        {
            if (ui != null) ui.SetActive(true);
        }
        temporarilyHiddenPanels.Clear();
    }

    public void SelectCategory(int index)
    {
        if (index < 0 || index >= categories.Count || isFlipping || !IsSupportedCategory(categories[index])) return;

        useVirtualPagination = false;
        OnVirtualPageTurn = null;

        ToggleSpread(currentSpreadIndex, false);

        currentCategoryIndex = index;
        currentSpreadIndex = 0; 

        if (PlayerDataManager.Instance != null)
        {
            AlmanacTabType selectedType = categories[currentCategoryIndex].tabType;
            if (selectedType == AlmanacTabType.Contracts)
                PlayerDataManager.Instance.MarkContractsAlmanacRead();
            else if (selectedType == AlmanacTabType.Lessons)
                PlayerDataManager.Instance.MarkLessonsAlmanacRead();
        }

        for (int i = 0; i < categories.Count; i++)
        {
            if (categories[i].tabButton == null) continue;
            
            RectTransform tabRect = categories[i].tabButton.GetComponent<RectTransform>();
            Image tabImage = categories[i].tabButton.GetComponent<Image>();

            if (i == currentCategoryIndex)
            {
                targetTabYPositions[tabRect] = originalTabYPositions[tabRect] + selectedTabUpOffset;
                if (categories[i].tabAlertIcon != null) categories[i].tabAlertIcon.SetActive(false);
                
                if (tabImage != null && categories[i].activeSprite != null) 
                {
                    tabImage.sprite = categories[i].activeSprite;
                }
            }
            else
            {
                targetTabYPositions[tabRect] = originalTabYPositions[tabRect]; 
                
                if (tabImage != null && categories[i].inactiveSprite != null) 
                {
                    tabImage.sprite = categories[i].inactiveSprite;
                }
            }
        }

        ToggleSpread(currentSpreadIndex, true);
        
        OnCategoryChanged?.Invoke(currentCategoryIndex);
        UpdatePaginationButtons();
    }

    private static bool IsSupportedCategory(AlmanacCategory category)
    {
        if (category == null) return false;
        return category.tabType == AlmanacTabType.General ||
               category.tabType == AlmanacTabType.Contracts ||
               category.tabType == AlmanacTabType.Lessons;
    }

    private void TurnPage(bool goingForward)
    {
        if (useVirtualPagination)
        {
            OnVirtualPageTurn?.Invoke(goingForward);
            return;
        }

        if (isFlipping) return;

        AlmanacCategory currentCat = categories[currentCategoryIndex];
        int maxSpreads = Mathf.Max(currentCat.leftPages.Count, currentCat.rightPages.Count);

        if (goingForward && currentSpreadIndex < maxSpreads - 1)
        {
            StartCoroutine(FlipPageRoutine(true));
        }
        else if (!goingForward && currentSpreadIndex > 0)
        {
            StartCoroutine(FlipPageRoutine(false));
        }
    }

    private IEnumerator FlipPageRoutine(bool goingForward)
    {
        isFlipping = true;
        AlmanacCategory currentCat = categories[currentCategoryIndex];
        int nextSpread = currentSpreadIndex + (goingForward ? 1 : -1);

        bool leftChanges = GetClampedIndex(currentCat.leftPages, currentSpreadIndex) != GetClampedIndex(currentCat.leftPages, nextSpread);
        bool rightChanges = GetClampedIndex(currentCat.rightPages, currentSpreadIndex) != GetClampedIndex(currentCat.rightPages, nextSpread);

        GameObject oldLeft = GetClampedPage(currentCat.leftPages, currentSpreadIndex);
        GameObject oldRight = GetClampedPage(currentCat.rightPages, currentSpreadIndex);
        
        GameObject newLeft = GetClampedPage(currentCat.leftPages, nextSpread);
        GameObject newRight = GetClampedPage(currentCat.rightPages, nextSpread);

        GameObject liftingPage = null;
        if (goingForward && rightChanges) liftingPage = oldRight; 
        else if (!goingForward && leftChanges) liftingPage = oldLeft; 

        if (liftingPage != null)
        {
            float elapsed = 0f;
            float targetAngle = goingForward ? 90f : -90f;

            while (elapsed < flipDuration)
            {
                elapsed += Time.deltaTime;
                liftingPage.transform.localRotation = Quaternion.Euler(0, Mathf.Lerp(0, targetAngle, elapsed / flipDuration), 0);
                yield return null;
            }
            liftingPage.transform.localRotation = Quaternion.Euler(0, targetAngle, 0);
        }
        else yield return new WaitForSeconds(flipDuration); 

        if (leftChanges && oldLeft != null) oldLeft.SetActive(false);
        if (rightChanges && oldRight != null) oldRight.SetActive(false);

        currentSpreadIndex = nextSpread;

        if (newLeft != null) 
        { 
            newLeft.SetActive(true); 
            if (!leftChanges) newLeft.transform.localRotation = Quaternion.identity; 
        }
        if (newRight != null) 
        { 
            newRight.SetActive(true); 
            if (!rightChanges) newRight.transform.localRotation = Quaternion.identity; 
        }

        GameObject landingPage = null;
        if (goingForward && leftChanges) landingPage = newLeft;
        else if (!goingForward && rightChanges) landingPage = newRight;

        if (landingPage != null)
        {
            float elapsed = 0f;
            float startAngle = goingForward ? -90f : 90f;
            
            landingPage.transform.localRotation = Quaternion.Euler(0, startAngle, 0);

            while (elapsed < flipDuration)
            {
                elapsed += Time.deltaTime;
                landingPage.transform.localRotation = Quaternion.Euler(0, Mathf.Lerp(startAngle, 0, elapsed / flipDuration), 0);
                yield return null;
            }
            landingPage.transform.localRotation = Quaternion.identity;
        }
        else yield return new WaitForSeconds(flipDuration);

        UpdatePaginationButtons();
        isFlipping = false;
    }

    private void ToggleSpread(int spreadIndex, bool state)
    {
        AlmanacCategory currentCat = categories[currentCategoryIndex];

        GameObject leftPage = GetClampedPage(currentCat.leftPages, spreadIndex);
        GameObject rightPage = GetClampedPage(currentCat.rightPages, spreadIndex);

        if (leftPage != null) 
        {
            leftPage.SetActive(state);
            if (state) leftPage.transform.localRotation = Quaternion.identity;
        }
        if (rightPage != null) 
        {
            rightPage.SetActive(state);
            if (state) rightPage.transform.localRotation = Quaternion.identity;
        }
    }

    private int GetClampedIndex(List<GameObject> pageList, int requestedIndex)
    {
        if (pageList == null || pageList.Count == 0) return -1;
        return Mathf.Clamp(requestedIndex, 0, pageList.Count - 1);
    }

    private GameObject GetClampedPage(List<GameObject> pageList, int requestedIndex)
    {
        int safeIndex = GetClampedIndex(pageList, requestedIndex);
        if (safeIndex != -1) return pageList[safeIndex];
        return null; 
    }

    public void EnableVirtualPagination(System.Action<bool> callback)
    {
        useVirtualPagination = true;
        OnVirtualPageTurn = callback;
    }

    public void DisableVirtualPagination(System.Action<bool> callback)
    {
        if (OnVirtualPageTurn == callback)
        {
            useVirtualPagination = false;
            OnVirtualPageTurn = null;
            ForceUpdatePaginationUI();
        }
    }

    public void ForceUpdatePaginationUI()
    {
        UpdatePaginationButtons();
    }

    private void UpdatePaginationButtons()
    {
        if (useVirtualPagination)
        {
            if (prevButton != null) prevButton.interactable = virtualHasPrev;
            if (nextButton != null) nextButton.interactable = virtualHasNext;
            return;
        }

        if (categories == null || categories.Count == 0 || currentCategoryIndex < 0 || currentCategoryIndex >= categories.Count) return;

        AlmanacCategory currentCat = categories[currentCategoryIndex];
        int maxSpreads = Mathf.Max(currentCat.leftPages.Count, currentCat.rightPages.Count);

        if (prevButton != null) prevButton.interactable = (currentSpreadIndex > 0);
        if (nextButton != null) nextButton.interactable = (currentSpreadIndex < maxSpreads - 1);
    }
}
