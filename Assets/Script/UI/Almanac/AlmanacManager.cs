using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class AlmanacCategory
{
    public string categoryName;
    public Button tabButton;
    
    [Header("Alert Integration")]
    [Tooltip("The pulsing exclamation mark on THIS specific tab.")]
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

    [Header("HUD Integration")]
    public GameObject hudOpenButton; 
    public GameObject newAlertIcon; 

    // --- NEW: FRAME-BY-FRAME ANIMATION ---
    [Header("Opening Animation")]
    [Tooltip("Drag the BookAnimationPanel here.")]
    public GameObject animationPanel; 
    [Tooltip("Drag the Image component where the frames will play here.")]
    public Image animationImage; 
    [Tooltip("Drag your 15 frames here in order (Frame 1 to Frame 15).")]
    public Sprite[] bookOpenFrames; 
    [Tooltip("How fast should the frames play? (0.03 = 30fps)")]
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

    private Dictionary<RectTransform, float> originalTabYPositions = new Dictionary<RectTransform, float>();
    private Dictionary<RectTransform, float> targetTabYPositions = new Dictionary<RectTransform, float>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        InitializeBook();
    }

    private void Start()
    {
        if (almanacCanvas != null) almanacCanvas.SetActive(false);
        if (newAlertIcon != null) newAlertIcon.SetActive(false); 
        
        // Ensure animation panel is hidden on start
        if (animationPanel != null) animationPanel.SetActive(false); 

        foreach (var cat in categories)
        {
            if (cat.tabAlertIcon != null) cat.tabAlertIcon.SetActive(false);
        }

        if (hudOpenButton != null && PlayerDataManager.Instance != null)
        {
            hudOpenButton.SetActive(PlayerDataManager.Instance.CurrentData.hasAlmanac);
            PlayerDataManager.Instance.OnAlmanacUnlocked += ShowHudButton;
        }
    }

    private void OnDestroy()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnAlmanacUnlocked -= ShowHudButton;
        }
    }

    private void ShowHudButton()
    {
        if (hudOpenButton != null) hudOpenButton.SetActive(true);
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

    private void InitializeBook()
    {
        for (int i = 0; i < categories.Count; i++)
        {
            int index = i; 
            AlmanacCategory cat = categories[i];

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
        if (newAlertIcon != null && almanacCanvas != null && !almanacCanvas.activeSelf)
        {
            newAlertIcon.SetActive(true);
        }
    }

    public void TriggerTabAlert(string targetCategoryName)
    {
        TriggerAlert();

        foreach (var cat in categories)
        {
            if (cat.categoryName == targetCategoryName)
            {
                if (cat.tabAlertIcon != null) cat.tabAlertIcon.SetActive(true);
                break;
            }
        }
    }

    // --- THE FIX: Now calls the coroutine to play the opening animation ---
    public void OpenAlmanac()
    {
        if (isAnimating) return; // Prevent double-clicking
        StartCoroutine(OpenAlmanacRoutine());
    }

    private IEnumerator OpenAlmanacRoutine()
    {
        isAnimating = true;

        if (newAlertIcon != null) newAlertIcon.SetActive(false);
        
        temporarilyHiddenPanels.Clear();
        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui != null && ui.activeSelf)
            {
                temporarilyHiddenPanels.Add(ui);
                ui.SetActive(false);
            }
        }

        // Play the 15-frame animation forwards!
        if (animationPanel != null && animationImage != null && bookOpenFrames.Length > 0)
        {
            animationPanel.SetActive(true);
            
            for (int i = 0; i < bookOpenFrames.Length; i++)
            {
                animationImage.sprite = bookOpenFrames[i];
                yield return new WaitForSeconds(frameRate);
            }
            
            // Hide the animation panel right as the real Almanac canvas turns on
            animationPanel.SetActive(false);
        }

        if (almanacCanvas != null) almanacCanvas.SetActive(true);
        
        SelectCategory(0);

        if (onFirstOpenTutorial != null)
        {
            onFirstOpenTutorial.TryStartTutorial();
        }

        isAnimating = false;
    }

    // --- THE FIX: Now plays the animation in reverse to close the book! ---
    public void CloseAlmanac()
    {
        if (isAnimating) return;
        StartCoroutine(CloseAlmanacRoutine());
    }

    private IEnumerator CloseAlmanacRoutine()
    {
        isAnimating = true;

        // Hide the real Almanac UI instantly
        if (almanacCanvas != null) almanacCanvas.SetActive(false);

        // Play the 15-frame animation backwards!
        if (animationPanel != null && animationImage != null && bookOpenFrames.Length > 0)
        {
            animationPanel.SetActive(true);
            
            for (int i = bookOpenFrames.Length - 1; i >= 0; i--)
            {
                animationImage.sprite = bookOpenFrames[i];
                yield return new WaitForSeconds(frameRate);
            }
            
            animationPanel.SetActive(false);
        }

        foreach (GameObject ui in temporarilyHiddenPanels)
        {
            if (ui != null) ui.SetActive(true);
        }
        temporarilyHiddenPanels.Clear();

        isAnimating = false;
    }

    public void SelectCategory(int index)
    {
        if (index < 0 || index >= categories.Count || isFlipping) return;

        useVirtualPagination = false;
        OnVirtualPageTurn = null;

        ToggleSpread(currentSpreadIndex, false);

        currentCategoryIndex = index;
        currentSpreadIndex = 0; 

        for (int i = 0; i < categories.Count; i++)
        {
            if (categories[i].tabButton == null) continue;
            
            RectTransform tabRect = categories[i].tabButton.GetComponent<RectTransform>();
            if (i == currentCategoryIndex)
            {
                targetTabYPositions[tabRect] = originalTabYPositions[tabRect] + selectedTabUpOffset;
                
                if (categories[i].tabAlertIcon != null) categories[i].tabAlertIcon.SetActive(false);
            }
            else
            {
                targetTabYPositions[tabRect] = originalTabYPositions[tabRect]; 
            }
        }

        ToggleSpread(currentSpreadIndex, true);
        
        OnCategoryChanged?.Invoke(currentCategoryIndex);
        UpdatePaginationButtons();
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