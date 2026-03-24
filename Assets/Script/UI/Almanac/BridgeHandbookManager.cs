using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems; 

public enum PageLayout { Standard, TextOnly, FullImage }

[System.Serializable]
public class HandbookSpread
{
    [Header("Left Page")]
    public PageLayout leftLayout;
    public string leftTitle;
    [TextArea(5, 10)] public string leftBodyText;
    public Sprite leftImage;

    [Header("Right Page")]
    public PageLayout rightLayout;
    public string rightTitle;
    [TextArea(5, 10)] public string rightBodyText;
    public Sprite rightImage;
}

[System.Serializable]
public class PageUIGroup
{
    public GameObject standardLayout;
    public TextMeshProUGUI standardTitle;
    public TextMeshProUGUI standardBody;
    public Image standardImage;

    public GameObject textOnlyLayout;
    public TextMeshProUGUI textOnlyTitle;
    public TextMeshProUGUI textOnlyBody;

    public GameObject fullImageLayout;
    public Image fullImageDisplay;
}

public class BridgeHandbookManager : MonoBehaviour
{
    public static BridgeHandbookManager Instance { get; private set; }

    // --- STATIC MEMORY: Remembers book and photos across all levels ---
    public static bool globalHasBook = false;
    public static List<HandbookSpread> globalPages = new List<HandbookSpread>();

    [Header("Main Screen UI")] 
    public Button openHandbookButton;

    [Header("Book UI Reference")]
    public GameObject handbookUIPanel; 
    public GameObject[] otherUIPanelsToHide; 

    [Header("Navigation Buttons")] 
    public Button nextButton;
    public Button prevButton;
    public Button closeButton;

    [Header("Left Page UI")]
    public PageUIGroup leftPageUI;
    
    [Header("Right Page UI")]
    public PageUIGroup rightPageUI;

    [Header("Default Pages (Tutorial)")]
    public List<HandbookSpread> defaultPages = new List<HandbookSpread>();

    private InputManager inputManager;
    private int currentSpreadIndex = 0;
    public bool isBookOpen { get; private set; } = false;

    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();

    private void Awake()
    {
        Instance = this;

        if (globalPages.Count == 0)
        {
            globalPages.AddRange(defaultPages);
        }
    }

    private void Start()
    {
        inputManager = FindObjectOfType<InputManager>();
        
        if (handbookUIPanel != null) handbookUIPanel.SetActive(false);
        if (openHandbookButton != null) openHandbookButton.gameObject.SetActive(globalHasBook); 

        UpdatePageUI();
    }

    public void AcquireBook()
    {
        globalHasBook = true;
        
        if (openHandbookButton != null) openHandbookButton.gameObject.SetActive(true);
        if (BuildUIController.Instance != null) BuildUIController.Instance.LogAction("Acquired the Builder's Handbook! Click the icon to read.");
    }

    public void ToggleBook()
    {
        if (!globalHasBook) return;

        isBookOpen = !isBookOpen;
        if (handbookUIPanel != null) handbookUIPanel.SetActive(isBookOpen);

        if (isBookOpen)
        {
            temporarilyHiddenPanels.Clear();
            foreach (GameObject panel in otherUIPanelsToHide)
            {
                if (panel != null && panel.activeSelf)
                {
                    temporarilyHiddenPanels.Add(panel);
                    panel.SetActive(false);
                }
            }
        }
        else
        {
            foreach (GameObject panel in temporarilyHiddenPanels)
            {
                if (panel != null) panel.SetActive(true);
            }
            temporarilyHiddenPanels.Clear();
        }

        if (inputManager == null) inputManager = FindObjectOfType<InputManager>();

        if (inputManager != null)
        {
            bool shouldEnableInput = !isBookOpen && (GameManager.Instance == null || !GameManager.Instance.IsInBuildMode());
            inputManager.SetPlayerInputEnable(shouldEnableInput);
            inputManager.SetLookEnabled(shouldEnableInput);
        }

        PlayerInteract interact = FindObjectOfType<PlayerInteract>();
        if (interact != null) interact.enabled = !isBookOpen;

        if (isBookOpen) UpdatePageUI();
        else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void NextPage() { TurnPage(1); }
    public void PreviousPage() { TurnPage(-1); }
    public void CloseBook() { if (isBookOpen) ToggleBook(); }

    private void TurnPage(int direction)
    {
        int newIndex = currentSpreadIndex + direction;
        if (newIndex >= 0 && newIndex < globalPages.Count)
        {
            currentSpreadIndex = newIndex;
            UpdatePageUI();
        }
    }

    private void UpdatePageUI()
    {
        if (globalPages.Count == 0) return;

        if (currentSpreadIndex < 0) currentSpreadIndex = 0;
        if (currentSpreadIndex >= globalPages.Count) currentSpreadIndex = globalPages.Count - 1;

        HandbookSpread currentSpread = globalPages[currentSpreadIndex];

        ApplyLayout(currentSpread.leftLayout, leftPageUI, currentSpread.leftTitle, currentSpread.leftBodyText, currentSpread.leftImage);
        ApplyLayout(currentSpread.rightLayout, rightPageUI, currentSpread.rightTitle, currentSpread.rightBodyText, currentSpread.rightImage);

        if (prevButton != null) prevButton.interactable = (currentSpreadIndex > 0);
        if (nextButton != null) nextButton.interactable = (currentSpreadIndex < globalPages.Count - 1);
    }

    private void ApplyLayout(PageLayout layout, PageUIGroup uiGroup, string title, string body, Sprite image)
    {
        if (uiGroup.standardLayout != null) uiGroup.standardLayout.SetActive(false);
        if (uiGroup.textOnlyLayout != null) uiGroup.textOnlyLayout.SetActive(false);
        if (uiGroup.fullImageLayout != null) uiGroup.fullImageLayout.SetActive(false);

        switch (layout)
        {
            case PageLayout.Standard:
                if (uiGroup.standardLayout != null) uiGroup.standardLayout.SetActive(true);
                if (uiGroup.standardTitle != null) uiGroup.standardTitle.text = title;
                if (uiGroup.standardBody != null) uiGroup.standardBody.text = body;
                if (uiGroup.standardImage != null)
                {
                    uiGroup.standardImage.gameObject.SetActive(image != null);
                    uiGroup.standardImage.sprite = image;
                }
                break;

            case PageLayout.TextOnly:
                if (uiGroup.textOnlyLayout != null) uiGroup.textOnlyLayout.SetActive(true);
                if (uiGroup.textOnlyTitle != null) uiGroup.textOnlyTitle.text = title;
                if (uiGroup.textOnlyBody != null) uiGroup.textOnlyBody.text = body;
                break;

            case PageLayout.FullImage:
                if (uiGroup.fullImageLayout != null) uiGroup.fullImageLayout.SetActive(true);
                if (uiGroup.fullImageDisplay != null && image != null) uiGroup.fullImageDisplay.sprite = image;
                break;
        }
    }

    public void RecordBridgeSnapshot(Camera captureCamera, string levelName, string bridgeStats)
    {
        if (captureCamera == null) return;

        // Take a 1920x1080 widescreen photo so the sides don't get cut off!
        int resWidth = 1920;
        int resHeight = 1080;

        RenderTexture rt = new RenderTexture(resWidth, resHeight, 24);
        captureCamera.targetTexture = rt;
        Texture2D screenShot = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
        captureCamera.Render();
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
        screenShot.Apply();
        
        captureCamera.targetTexture = null;
        RenderTexture.active = null; 
        Destroy(rt);

        Sprite bridgePhoto = Sprite.Create(screenShot, new Rect(0, 0, resWidth, resHeight), new Vector2(0.5f, 0.5f));

        HandbookSpread newSpread = new HandbookSpread();
        newSpread.leftLayout = PageLayout.TextOnly;
        newSpread.leftTitle = "Record: " + levelName;
        newSpread.leftBodyText = bridgeStats;

        newSpread.rightLayout = PageLayout.FullImage;
        newSpread.rightImage = bridgePhoto;

        globalPages.Add(newSpread);
        
        currentSpreadIndex = globalPages.Count - 1; 
        if (isBookOpen) UpdatePageUI();
    }
}