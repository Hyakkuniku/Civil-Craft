using System;
using UnityEngine;
using UnityEngine.UI; 
using TMPro;
using System.Collections.Generic;
using System.Collections;

// Shared helpers for dynamic material receipt rows; full result panels are authored in the Editor.
public static class CompletionReceiptLayout
{
    internal static readonly Color Ink = new Color32(73,48,29,255);
#if UNITY_EDITOR
    private static Sprite roundedSprite;
    public static void SetRoundedSprite(Sprite sprite) { roundedSprite = sprite; }
    internal static void Round(Image image, float radius = 18f)
    {
        if (roundedSprite == null)
            throw new InvalidOperationException("Author the level-result rounded sprite before building UI.");
        image.sprite = roundedSprite; image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 18f / Mathf.Max(1f,radius);
    }
#endif
    internal static RectTransform Box(Transform parent, string name, float x0,float y0,float x1,float y1)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false); rect.anchorMin = new Vector2(x0,y0); rect.anchorMax = new Vector2(x1,y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    internal static RectTransform Panel(Transform parent,string name,float x0,float y0,float x1,float y1,Color color)
    {
        var rect = Box(parent,name,x0,y0,x1,y1);
        var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
#if UNITY_EDITOR
        if (name != "Dash" && name != "Viewport") Round(image);
#endif
        return rect;
    }
    internal static TextMeshProUGUI Label(Transform parent,string name,string value,float x0,float y0,float x1,float y1,
        float size,TMP_FontAsset font,TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var text = Box(parent,name,x0,y0,x1,y1).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.color = Ink; text.fontSize = size;
        text.enableAutoSizing = true; text.fontSizeMin = size * .65f; text.fontSizeMax = size;
        text.alignment = alignment; text.raycastTarget = false;
        return text;
    }
#if UNITY_EDITOR
    internal static Button Button(Transform parent,string title,float x0,float y0,float x1,float y1,Color color,
        TMP_FontAsset font,UnityEngine.Events.UnityAction action)
    {
        var rect = Panel(parent,title,x0,y0,x1,y1,color);
        var image = rect.GetComponent<Image>(); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, action);
        rect.gameObject.AddComponent<CompletionButtonMotion>();
        Label(rect,"Label",title,.04f,.08f,.96f,.92f,32,font,TextAlignmentOptions.Center);
        return button;
    }
#endif
    internal static void Divider(Transform parent,string name,float x0,float y,float x1)
    {
        var line = Box(parent,name,x0,y,x1,y);
        line.sizeDelta = new Vector2(0,1.2f);
        for (int i=0;i<32;i++)
            Panel(line,"Dash",i/32f,0,(i+.65f)/32f,1,new Color32(112,109,103,150));
    }
}


public class LevelCompleteManager : MonoBehaviour
{
    public static LevelCompleteManager Instance { get; private set; }
    private bool IsMultiplayerScene => gameObject.scene.name == "Multiplayer";

    /// <summary>
    /// Raised after a successfully tested bridge has been saved/baked, Build Mode
    /// has exited, and the completion panel has closed.
    /// </summary>
    public static event Action<ContractSO, BuildLocation> BridgeSavedAtLocation;
    /// <summary>Raised only after the active contract's existing completion checks have passed.</summary>
    public static event Action<ContractSO, BuildLocation> SimulationSucceeded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSimulationLessonEvents()
    {
        SimulationSucceeded = null;
    }

    [Header("UI References")]
    public GameObject levelCompletePanel;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI costText;   
    public TextMeshProUGUI costPercentageText; 
    public TextMeshProUGUI budgetText; 
    public TextMeshProUGUI stressText;
    
    [Header("Receipt UI System")]
    [Tooltip("Source font used to create the receipt's dynamic TextMeshPro font.")]
    public Font receiptSourceFont;
    public Sprite receiptBackground;
    [Header("Star Artwork")]
    public Sprite earnedStarSprite;
    public Sprite unearnedStarSprite;

    [Header("First Completion Tutorial")]
    [SerializeField, Tooltip("Tutorial shown on the player's first successful bridge completion. Its Lesson Name makes it run only once per save.")]
    private TutorialSequence firstCompletionTutorial;
    [SerializeField] private bool showFirstCompletionTutorial = true;

    public TMP_FontAsset ReceiptFont { get; private set; }
    public Transform receiptContentParent; 
    public GameObject receiptRowPrefab;    

    [Header("Earnings Breakdown UI")]
    public TextMeshProUGUI baseRewardText; 
    public TextMeshProUGUI bonusText;      
    public TextMeshProUGUI penaltyText;    
    public TextMeshProUGUI goldEarnedText; 
    public TextMeshProUGUI expEarnedText;
    private Image goldEarnedIcon;
    private Image expEarnedIcon;

    [Header("Photo Display")]
    public RawImage bridgePhotoDisplay; 
    private Texture2D currentBridgePhoto; 

    [Header("Gameplay Elements to Hide")]
    public List<GameObject> uiElementsToHide = new List<GameObject>();

    private List<GameObject> temporarilyHiddenPanels = new List<GameObject>();
    private bool levelAlreadyCompleted = false;
    private bool wasSimulating = false; 

    public int currentSimulationFrames { get; private set; } = 0;

    private ContractSO activeContract;
    private HashSet<string> alreadyPaidContracts = new HashSet<string>();
    private readonly HashSet<string> tutorialTestsPassedThisSession = new HashSet<string>();

    private Dictionary<string, int> contractGoldRewards = new Dictionary<string, int>();
    private Dictionary<string, int> contractExpRewards = new Dictionary<string, int>();

    private BridgePhysicsManager cachedPhysicsManager;
    private ContractSO timerLocationContract;
    private BuildLocation timerBridgeLocation;
    private readonly HashSet<Point> connectivityVisited = new HashSet<Point>();
    private readonly Queue<Point> connectivityQueue = new Queue<Point>();

    private float lastFinalCost = 0f;
    private float lastPeakStress = 0f;
    private ContractStarResult lastStarResult;
    private readonly Graphic[] starIcons = new Graphic[3];
    private readonly TextMeshProUGUI[] starLabels = new TextMeshProUGUI[3];
    private Coroutine starAnimation;
    private TextMeshProUGUI rewardStatusText;
    private TextMeshProUGUI receiptBalanceText;
    private TextMeshProUGUI receiptStampText;
    private RectTransform completionTitleTutorialTarget;
    private RectTransform completionStarsTutorialTarget;
    private RectTransform completionReceiptTutorialTarget;
    private RectTransform completionSaveTutorialTarget;
    private Button multiplayerBackButton;
    private Button multiplayerSaveButton;
    private TextMeshProUGUI guestWaitingText;
    private bool guestSessionCompletionVisible;
    private GameObject guestReceiptPlaceholder;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        BindAuthoredLayout();
        InitializeReceiptFont();
        EnsureFirstCompletionTutorialDraft();
        if (levelCompletePanel != null)
            UIReservedRegionLayout.Register(levelCompletePanel.transform as RectTransform);
        if (levelCompletePanel != null) levelCompletePanel.SetActive(false); 
    }

    private void Start()
    {
        cachedPhysicsManager = FindObjectOfType<BridgePhysicsManager>();
    }

    private void Update()
    {
        if (cachedPhysicsManager == null) cachedPhysicsManager = FindObjectOfType<BridgePhysicsManager>();
        bool isSimulating = cachedPhysicsManager != null && cachedPhysicsManager.isSimulating;

        if (isSimulating && !wasSimulating)
        {
            ResetCompletionState();
        }
        
        if (!isSimulating && wasSimulating)
        {
            if (BuildUIController.Instance != null) BuildUIController.Instance.ShowTimer(false);
        }
        
        wasSimulating = isSimulating;
    }

    private void FixedUpdate()
    {
        if (cachedPhysicsManager == null) return;
        
        if (!cachedPhysicsManager.isSimulating)
        {
            currentSimulationFrames = 0;
            return;
        }

        bool isSimulating = cachedPhysicsManager.isSimulating;

        if (isSimulating && !levelAlreadyCompleted)
        {
            ContractSO currentContract = GameManager.Instance != null ? GameManager.Instance.CurrentContract : null;

            if (currentContract != null && currentContract.winCondition == ContractSO.WinCondition.Timer)
            {
                if (LevelFailedManager.Instance == null || !LevelFailedManager.Instance.isFailed)
                {
                    // The contract and location cannot change within one test.
                    // Resolve the exact authored location once, not on every physics tick.
                    if (timerLocationContract != currentContract || timerBridgeLocation == null)
                    {
                        timerLocationContract = currentContract;
                        timerBridgeLocation = null;
                        foreach (BuildLocation loc in Resources.FindObjectsOfTypeAll<BuildLocation>())
                        {
                            if (loc != null && loc.gameObject.scene.name != null &&
                                loc.activeContract == currentContract)
                            {
                                timerBridgeLocation = loc;
                                break;
                            }
                        }
                        if (timerBridgeLocation == null && GameManager.Instance != null)
                            timerBridgeLocation = GameManager.Instance.ActiveBuildLocation;
                    }

                    if (timerBridgeLocation != null && IsBridgeConnected(timerBridgeLocation))
                    {
                        currentSimulationFrames++; 
                        
                        float requiredTime = currentContract.requiredIntactTime;
                        float elapsedTime = currentSimulationFrames * Time.fixedDeltaTime;
                        float timeRemaining = requiredTime - elapsedTime;
                        if (timeRemaining < 0) timeRemaining = 0;

                        if (BuildUIController.Instance != null)
                        {
                            BuildUIController.Instance.ShowTimer(true);
                            BuildUIController.Instance.UpdateTimerText("Hold Bridge: ", timeRemaining);
                        }

                        int requiredFrames = Mathf.RoundToInt(requiredTime / Time.fixedDeltaTime);

                        if (currentSimulationFrames >= requiredFrames)
                        {
                            if (BuildUIController.Instance != null)
                            {
                                BuildUIController.Instance.ShowTimer(false); 
                            }
                            
                            if (ObjectiveTrackerUI.Instance != null)
                            {
                                ObjectiveTrackerUI.Instance.descriptionText.text = $"<color=green>Bridge Tested!</color> Return to {currentContract.clientName}.";
                            }

                            CompleteLevel(currentContract);
                        }
                    }
                    else
                    {
                        currentSimulationFrames = 0;
                        
                        if (BuildUIController.Instance != null)
                        {
                            BuildUIController.Instance.ShowTimer(true);
                            BuildUIController.Instance.UpdateTimerText("Hold Bridge: ", currentContract.requiredIntactTime);
                        }
                    }
                }
            }
        }
    }

    private bool IsBridgeConnected(BuildLocation loc)
    {
        if (loc == null || loc.startingAnchors.Count == 0) return false;
        if (loc.endingAnchors.Count == 0) return false;

        connectivityVisited.Clear();
        connectivityQueue.Clear();

        foreach (Point p in loc.startingAnchors)
        {
            if (p != null && p.gameObject.activeSelf)
            {
                connectivityVisited.Add(p);
                connectivityQueue.Enqueue(p);
            }
        }

        while (connectivityQueue.Count > 0)
        {
            Point current = connectivityQueue.Dequeue();

            if (loc.endingAnchors.Contains(current)) return true;

            foreach (Bar b in current.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf && b.materialData != null && b.materialData.isRoad)
                {
                    BarStressHandler stress = b.GetComponent<BarStressHandler>();
                    if (stress != null && stress.isBroken) continue;

                    Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                    if (neighbor != null && neighbor.gameObject.activeSelf && !connectivityVisited.Contains(neighbor))
                    {
                        connectivityVisited.Add(neighbor);
                        connectivityQueue.Enqueue(neighbor);
                    }
                }
            }
        }

        return false; 
    }

    private void OnDestroy()
    {
        if (levelCompletePanel != null)
            UIReservedRegionLayout.Unregister(levelCompletePanel.transform as RectTransform);
        if (Instance == this) Instance = null;
        if (currentBridgePhoto != null) Destroy(currentBridgePhoto);
        if (ReceiptFont != null)
        {
            foreach (Texture2D atlas in ReceiptFont.atlasTextures) if (atlas != null) Destroy(atlas);
            if (ReceiptFont.material != null) Destroy(ReceiptFont.material);
            Destroy(ReceiptFont);
        }
    }

    public int GetContractGold(string contractName) { return contractGoldRewards.ContainsKey(contractName) ? contractGoldRewards[contractName] : 0; }
    public int GetContractExp(string contractName) { return contractExpRewards.ContainsKey(contractName) ? contractExpRewards[contractName] : 0; }

    public void MarkContractAsPaid(string contractName)
    {
        if (!string.IsNullOrEmpty(contractName)) alreadyPaidContracts.Add(contractName);
    }

    public bool IsContractPaid(string contractName)
    {
        if (string.IsNullOrEmpty(contractName)) return false;
        if (alreadyPaidContracts.Contains(contractName)) return true;

        if (PlayerDataManager.Instance != null &&
            PlayerDataManager.Instance.HasContractCompletionRecord(contractName))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// True after this tutorial contract has passed its bridge test in the current
    /// scene session, even if the player chose Retry instead of Save & Continue.
    /// This prevents a later failed retry from restarting the guided build sequence.
    /// </summary>
    public bool HasPassedTutorialTestThisSession(ContractSO contract)
    {
        return contract != null &&
               !string.IsNullOrWhiteSpace(contract.ContractID) &&
               tutorialTestsPassedThisSession.Contains(contract.ContractID);
    }

    private void CompleteBuildTutorialAfterSuccessfulTest(ContractSO contract)
    {
        BuildLocation activeLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;
        bool isOptionalReplay = activeLocation != null && activeLocation.IsTutorialReplayActive;
        bool isFirstTutorialRun = contract != null && contract.IsTutorialForCurrentPlayer();
        if (!isFirstTutorialRun && !isOptionalReplay) return;

        if (isFirstTutorialRun && !string.IsNullOrWhiteSpace(contract.ContractID))
            tutorialTestsPassedThisSession.Add(contract.ContractID);

        if (isOptionalReplay)
            activeLocation.CompleteTutorialReplay();

        TutorialManager tutorial = TutorialManager.Instance;
        BuildTutorialDirector director = BuildTutorialDirector.Instance;

        // The final Play instruction can still be the active tutorial step when
        // the completion popup opens. Finish it now so the popup does not suspend
        // it and restore its UI/build locks when the player chooses Retry.
        if (tutorial != null && tutorial.IsTutorialActive && director != null &&
            director.isTutorialRunning &&
            !string.IsNullOrWhiteSpace(tutorial.CurrentLessonName))
        {
            tutorial.CompleteTutorialIfPlaying(tutorial.CurrentLessonName);
        }
        else if (director != null && director.isTutorialRunning)
        {
            director.EndTutorial();
        }
    }

    public void ResetCompletionState()
    {
        if (IsMultiplayerScene && FusionConnectionManager.Instance != null &&
            FusionConnectionManager.Instance.IsHosting)
            FusionMultiplayerAvatar.FindLocalHostAvatar()?.QueueHostCompletion(default);
        timerLocationContract = null;
        timerBridgeLocation = null;
        connectivityVisited.Clear();
        connectivityQueue.Clear();
        lastStarResult = null;
        if (starAnimation != null) StopCoroutine(starAnimation);
        starAnimation = null;
        levelAlreadyCompleted = false;
        currentSimulationFrames = 0;
    }

    public void CompleteLevel(ContractSO currentContract)
    {
        CompleteLevelForVehicle(currentContract, null);
    }

    /// <summary>
    /// Completes a live-load test using the exact vehicle that entered its finish
    /// trigger. This prevents another inactive cart in the shared scene from being
    /// selected by FindObjectOfType and stopped instead.
    /// </summary>
    public void CompleteLevelForVehicle(ContractSO currentContract, LiveLoadVehicle finishingVehicle)
    {
        if (levelAlreadyCompleted) return;

        // Keep every completion route consistent (finish trigger, timer, cargo,
        // and authored UnityEvents). A bridge cannot pass merely because a
        // folding or excessively sagged roadway happened to reach the goal.
        if (!BridgePhysicsManager.DebugInvincibleBridge &&
            cachedPhysicsManager != null && cachedPhysicsManager.isSimulating &&
            !cachedPhysicsManager.IsDeterministicStructureStable)
        {
            if (LevelFailedManager.Instance != null)
            {
                LevelFailedManager.Instance.TriggerLevelFailed("Structurally Unstable Bridge!");
            }
            return;
        }
        
        levelAlreadyCompleted = true;
        activeContract = currentContract;
        BuildLocation completedLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;
        SimulationSucceeded?.Invoke(currentContract, completedLocation);
        CompleteBuildTutorialAfterSuccessfulTest(currentContract);

        if (cachedPhysicsManager != null)
        {
            cachedPhysicsManager.lockStressTracking = true;
        }

        if (finishingVehicle == null && (currentContract == null ||
            currentContract.liveLoadMode != ContractSO.LiveLoadMode.PlayerCarriedCargo))
            finishingVehicle = FindVehicleForContract(currentContract);

        if (finishingVehicle != null)
        {
            finishingVehicle.StopAndFreezeForWin();
        }

        StartCoroutine(TakeSnapshotAndShowUIRoutine(
            currentContract,
            finishingVehicle,
            completedLocation));
    }

    private static LiveLoadVehicle FindVehicleForContract(ContractSO currentContract)
    {
        LiveLoadVehicle fallback = null;
        foreach (LiveLoadVehicle vehicle in FindObjectsOfType<LiveLoadVehicle>())
        {
            if (fallback == null) fallback = vehicle;
            if (currentContract != null && vehicle.assignedContract == currentContract)
                return vehicle;
        }
        return fallback;
    }

    private IEnumerator TakeSnapshotAndShowUIRoutine(
        ContractSO currentContract,
        LiveLoadVehicle finishingVehicle,
        BuildLocation completedLocation)
    {
        while (finishingVehicle != null && finishingVehicle.IsFinishBraking)
            yield return new WaitForFixedUpdate();

        // The success event has already replaced any queued informational text
        // with the verified outcome. Keep the build UI visible until that message
        // has had its configured reading time, then continue to the receipt.
        while (SimulationLessonPresenter.GetRemainingOutcomeReadTime(completedLocation) > 0f)
            yield return null;

        temporarilyHiddenPanels.Clear();
        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui != null && ui.activeSelf)
            {
                temporarilyHiddenPanels.Add(ui);
                ui.SetActive(false);
            }
        }

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(false);
            inputObj.SetLookEnabled(false);
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = false;

        yield return new WaitForEndOfFrame();

        if (currentContract != null)
        {
            Camera snapCam = null;
            BuildLocation targetLoc = null;
            BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
            
            foreach (var loc in allLocs)
            {
                if (loc.gameObject.scene.name != null && loc.activeContract == currentContract)
                {
                    targetLoc = loc;
                    snapCam = loc.cinematicCamera != null ? loc.cinematicCamera : loc.locationCamera;
                    break;
                }
            }

            Texture2D screenImage;

            if (snapCam != null)
            {
                int resWidth = 1920;
                int resHeight = 1080;
                
                RenderTexture rt = new RenderTexture(resWidth, resHeight, 24);
                snapCam.targetTexture = rt;
                
                bool wasEnabled = snapCam.enabled;
                snapCam.enabled = true;

                bool locGridWasOn = targetLoc != null && targetLoc.gridImage != null && targetLoc.gridImage.enabled;
                if (locGridWasOn) targetLoc.gridImage.enabled = false;

                snapCam.Render();
                
                RenderTexture.active = rt;
                screenImage = new Texture2D(resWidth, resHeight, TextureFormat.RGB24, false);
                screenImage.ReadPixels(new Rect(0, 0, resWidth, resHeight), 0, 0);
                screenImage.Apply();
                
                snapCam.enabled = wasEnabled;
                snapCam.targetTexture = null;
                RenderTexture.active = null;
                Destroy(rt);

                if (locGridWasOn) targetLoc.gridImage.enabled = true;
            }
            else
            {
                screenImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                screenImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                screenImage.Apply();
            }

            if (currentBridgePhoto != null) Destroy(currentBridgePhoto);
            currentBridgePhoto = screenImage;

            if (bridgePhotoDisplay != null) bridgePhotoDisplay.texture = currentBridgePhoto;

        }

        float totalCalculatedCost = 0f;

        if (receiptContentParent != null && receiptRowPrefab != null)
        {
            if (guestReceiptPlaceholder != null) guestReceiptPlaceholder.SetActive(false);
            foreach (Transform child in receiptContentParent)
                if (guestReceiptPlaceholder == null || child.gameObject != guestReceiptPlaceholder)
                    Destroy(child.gameObject);

            Dictionary<BridgeMaterialSO, float> materialUsage = new Dictionary<BridgeMaterialSO, float>();
            HashSet<Bar> countedBars = new HashSet<Bar>();

            foreach (Point p in Point.AllPoints)
            {
                if (!p.gameObject.activeSelf || !p.enabled) continue;
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b.materialData != null && !countedBars.Contains(b))
                    {
                        countedBars.Add(b);
                    }
                }
            }

            if (currentContract != null)
            {
                BuildLocation targetLoc = null;
                BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
                foreach (var loc in allLocs)
                {
                    if (loc.gameObject.scene.name != null && loc.activeContract == currentContract)
                    {
                        targetLoc = loc;
                        break;
                    }
                }

                if (targetLoc != null)
                {
                    foreach (Bar b in targetLoc.bakedBars)
                    {
                        if (b != null && b.materialData != null && !countedBars.Contains(b)) countedBars.Add(b);
                    }

                    HashSet<Point> visitedPoints = new HashSet<Point>();
                    Queue<Point> queue = new Queue<Point>();

                    foreach (Point anchor in targetLoc.startingAnchors)
                    {
                        if (anchor != null) { visitedPoints.Add(anchor); queue.Enqueue(anchor); }
                    }
                    foreach (Point anchor in targetLoc.endingAnchors)
                    {
                        if (anchor != null && !visitedPoints.Contains(anchor)) { visitedPoints.Add(anchor); queue.Enqueue(anchor); }
                    }

                    while (queue.Count > 0)
                    {
                        Point current = queue.Dequeue();
                        foreach (Bar b in current.ConnectedBars)
                        {
                            if (b != null && b.gameObject.activeSelf && b.materialData != null && !countedBars.Contains(b))
                            {
                                countedBars.Add(b);

                                Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                                if (neighbor != null && !visitedPoints.Contains(neighbor))
                                {
                                    visitedPoints.Add(neighbor);
                                    queue.Enqueue(neighbor);
                                }
                            }
                        }
                    }
                }
            }

            foreach (Bar b in countedBars)
            {
                if (!materialUsage.ContainsKey(b.materialData)) materialUsage[b.materialData] = 0f;
                int multiplier = b.materialData.isDualBeam ? 2 : 1;
                materialUsage[b.materialData] += (b.currentLength * multiplier);
                totalCalculatedCost += (b.currentLength * b.materialData.costPerMeter * multiplier);
            }

            foreach (var kvp in materialUsage)
            {
                GameObject rowObj = Instantiate(receiptRowPrefab, receiptContentParent);
                ReceiptRowUI rowUI = rowObj.GetComponent<ReceiptRowUI>();
                if (rowUI != null) rowUI.Setup(kvp.Key, kvp.Value);
            }
        }

        SimulationLessonPresenter.HideForResultOverlay();
        UIReservedRegionLayout.NotifyLayoutChanging();
        if (levelCompletePanel != null) levelCompletePanel.SetActive(true);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX("Level_Complete");

        float maxBudget = currentContract != null ? currentContract.budget : 0f;
        int baseGoldReward = currentContract != null ? currentContract.goldReward : 0;
        int baseExpReward = currentContract != null ? currentContract.expReward : 0;

        float finalCost = totalCalculatedCost;
        if (finalCost == 0f && BuildUIController.Instance != null) finalCost = BuildUIController.Instance.GetTotalCost();

        lastFinalCost = finalCost;

        float costPercentage = 0f;
        if (maxBudget > 0f)
        {
            costPercentage = (finalCost / maxBudget) * 100f;
        }

        float peakStress = 0f;
        if (cachedPhysicsManager != null)
            peakStress = cachedPhysicsManager.GetPeakDisplayedBridgeStress() * 100f;

        lastPeakStress = peakStress;
        lastStarResult = ContractStarResult.Grade(true, finalCost, maxBudget,
            currentContract != null ? currentContract.efficiencyStarBudgetRatio : 0.85f,
            peakStress, currentContract != null ? currentContract.strengthStarMaxStress : 60f,
            cachedPhysicsManager != null && cachedPhysicsManager.HadBrokenPartsThisRun,
            cachedPhysicsManager != null);
        ShowStarResults(currentContract);

        int calculatedGold = 0;
        int calculatedExp = 0;
        int bonusGold = 0;
        int budgetPenalty = 0;
        int failPenalty = 0;

        if (LevelFailedManager.Instance != null)
        {
            failPenalty = LevelFailedManager.Instance.currentFailCount * LevelFailedManager.Instance.goldPenaltyPerFail;
        }

        if (currentContract != null && IsContractPaid(currentContract.ContractID))
        {
            calculatedGold = 0;
            calculatedExp = 0;

            if (feedbackText != null) feedbackText.text = "<color=yellow>Redesign Successful! (Rewards already claimed)</color>";
            if (baseRewardText != null) baseRewardText.text = "Base Reward: 0";
            if (bonusText != null) bonusText.text = "Bonus: 0";
            if (penaltyText != null) penaltyText.text = "Penalty: 0";
        }
        else
        {
            calculatedGold = baseGoldReward;
            calculatedExp = baseExpReward;

            if (finalCost <= maxBudget)
            {
                bonusGold = Mathf.RoundToInt((maxBudget - finalCost) * 0.2f); 
                calculatedGold += bonusGold;
                
                if (feedbackText != null) feedbackText.text = "<color=green>Excellent Engineering!</color>";
                if (bonusText != null) bonusText.text = $"Bonus (Under Budget): <color=green>+{bonusGold}</color>";
            }
            else
            {
                budgetPenalty = Mathf.RoundToInt((finalCost - maxBudget) * 0.5f);
                
                if (feedbackText != null) feedbackText.text = "<color=red>Over Budget! The client isn't happy.</color>";
                if (bonusText != null) bonusText.text = $"Bonus: 0";
            }

            int totalPenalty = budgetPenalty + failPenalty;
            calculatedGold -= totalPenalty;
            if (calculatedGold < 0) calculatedGold = 0; 

            if (baseRewardText != null) baseRewardText.text = $"Base Reward: {baseGoldReward}";

            if (penaltyText != null)
            {
                if (totalPenalty > 0)
                {
                    string pText = "Penalty";
                    if (budgetPenalty > 0 && failPenalty > 0) pText += " (Over Budget & Fails)";
                    else if (budgetPenalty > 0) pText += " (Over Budget)";
                    else if (failPenalty > 0) pText += $" ({LevelFailedManager.Instance.currentFailCount} Fails)";

                    penaltyText.text = $"{pText}: <color=red>-{totalPenalty}</color>";
                }
                else
                {
                    penaltyText.text = "Penalty: 0";
                }
            }
        }

        if (currentContract != null)
        {
            contractGoldRewards[currentContract.ContractID] = calculatedGold;
            contractExpRewards[currentContract.ContractID] = calculatedExp;
        }

        if (goldEarnedText != null) 
        {
            goldEarnedText.text = $"Total Earnings: {calculatedGold} Gold (Pending)";
        }
        
        if (expEarnedText != null) 
        {
            expEarnedText.text = $"+{calculatedExp} EXP (Pending)";
        }

        if (costText != null) 
        {
            costText.text = $"Total Cost: ₱{Mathf.RoundToInt(finalCost):N0}";
            costText.color = finalCost > maxBudget
                ? (Color)new Color32(164, 62, 45, 255)
                : CompletionReceiptLayout.Ink;
        }
        
        if (costPercentageText != null)
        {
            costPercentageText.text = $"({Mathf.RoundToInt(costPercentage)}%)";
            costPercentageText.color = (finalCost > maxBudget) ? Color.red : Color.white;
        }
        
        if (budgetText != null) 
        {
            budgetText.text = $"Budget: ₱{Mathf.RoundToInt(maxBudget):N0}";
        }

        if (stressText != null)
        {
            stressText.text = $"Peak Bridge Stress: {peakStress:0.0}%";
            
            stressText.color = peakStress >= 100f ? new Color32(164, 62, 45, 255) :
                peakStress >= 50f ? new Color32(155, 99, 27, 255) : new Color32(76, 110, 47, 255);
        }
        if (receiptBalanceText != null)
            receiptBalanceText.text = $"{(finalCost > maxBudget ? "Over budget" : "Remaining")}   ₱{Mathf.RoundToInt(Mathf.Abs(maxBudget - finalCost)):N0}";
        if (receiptStampText != null)
        {
            receiptStampText.text = finalCost > maxBudget ? "OVER BUDGET" : "WITHIN BUDGET";
            receiptStampText.color = finalCost > maxBudget ? new Color32(164, 62, 45, 255) : new Color32(76, 110, 47, 255);
        }
        // Legacy reward messages contain bright rich-text colors intended for black panels.
        bool tutorialResult = currentContract != null && currentContract.IsTutorialForCurrentPlayer();
        bool paidResult = currentContract != null && IsContractPaid(currentContract.ContractID);
        if (rewardStatusText != null)
            rewardStatusText.text = paidResult ? "ALREADY CLAIMED" :
                tutorialResult ? "TUTORIAL REWARD" : "PENDING CLAIM";
        bool earnsRewards = !paidResult;
        if (baseRewardText != null) baseRewardText.text = $"BASE\n{(earnsRewards ? baseGoldReward : 0):N0}";
        if (bonusText != null) bonusText.text = $"BONUS\n+{(earnsRewards ? bonusGold : 0):N0}";
        if (penaltyText != null) penaltyText.text = $"DEDUCTIONS\n-{(earnsRewards ? budgetPenalty + failPenalty : 0):N0}";
        if (goldEarnedText != null) goldEarnedText.text = $"TOTAL  {calculatedGold:N0}";
        if (expEarnedText != null) expEarnedText.text = $"+{calculatedExp:N0}";
        if (goldEarnedIcon != null)
            goldEarnedIcon.gameObject.SetActive(goldEarnedIcon.sprite != null);
        if (expEarnedIcon != null)
            expEarnedIcon.gameObject.SetActive(expEarnedIcon.sprite != null);
        if (feedbackText != null && paidResult) feedbackText.text = "Redesign complete. Best stars kept.";
        if (feedbackText != null && tutorialResult) feedbackText.text = "Tutorial complete. Great job!";
        if (IsMultiplayerScene)
        {
            if (rewardStatusText != null) rewardStatusText.text = "SESSION ONLY";
            if (baseRewardText != null) baseRewardText.text = "BASE\n0";
            if (bonusText != null) bonusText.text = "BONUS\n+0";
            if (penaltyText != null) penaltyText.text = "DEDUCTIONS\n-0";
            if (goldEarnedText != null) goldEarnedText.text = "TOTAL  0";
            if (expEarnedText != null) expEarnedText.text = "+0";
        }
        foreach (var label in new[] { feedbackText, baseRewardText, bonusText, penaltyText, goldEarnedText, expEarnedText })
            if (label != null) label.text = label.text.Replace("<color=green>", "<color=#4C6E2F>")
                .Replace("<color=yellow>", "<color=#9B631B>").Replace("<color=red>", "<color=#A43E2D>");

        TryStartFirstCompletionTutorial(currentContract);
        if (IsMultiplayerScene && FusionConnectionManager.Instance != null &&
            FusionConnectionManager.Instance.IsHosting && lastStarResult != null)
        {
            int flags = (lastStarResult.completed ? 1 : 0) |
                (lastStarResult.efficient ? 2 : 0) |
                (lastStarResult.strong ? 4 : 0) |
                (lastStarResult.hadBrokenParts ? 8 : 0);
            FusionMultiplayerAvatar.FindLocalHostAvatar()?.QueueHostCompletion(
                new FusionMultiplayerAvatar.SessionCompletionSnapshot
                {
                    Visible = 1,
                    ContractHash = FusionMultiplayerAvatar.StableHash(
                        currentContract != null ? currentContract.ContractID : null),
                    StarFlags = flags,
                    Cost = finalCost,
                    Budget = maxBudget,
                    PeakStress = peakStress,
                    CostTarget = lastStarResult.costTarget,
                    StressTarget = lastStarResult.stressTarget
                });
        }
    }

    private void ShowStarResults(ContractSO contract)
    {
        if (lastStarResult == null || starIcons[0] == null) return;
        int best = contract != null && PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetContractStars(contract.ContractID) : 0;
        starLabels[0].text = IsMultiplayerScene
            ? $"<b>{lastStarResult.Stars}/3 earned</b>\nThis room only"
            : $"<b>{lastStarResult.Stars}/3 earned</b>\nSaved best: {best}/3";
        starLabels[1].text = $"<b>₱{lastStarResult.totalCost:N0}</b>\nGoal: ≤ ₱{lastStarResult.costTarget:N0}";
        starLabels[2].text = $"<b>{lastStarResult.peakStress:0.#}% peak</b>\nGoal: ≤ {lastStarResult.stressTarget:0.#}%\n{(lastStarResult.hadBrokenParts ? "Parts broken" : "No broken parts")}";
        if (starAnimation != null) StopCoroutine(starAnimation);
        starAnimation = StartCoroutine(RevealStars(lastStarResult));
    }

    private IEnumerator RevealStars(ContractStarResult result)
    {
        Color muted = new Color32(155,143,119,255);
        Color earned = new Color32(225,154,31,255);
        foreach (var star in starIcons)
        {
            if (star is Image image) image.sprite = unearnedStarSprite;
            star.color = star is Image ? Color.white : muted;
            star.rectTransform.localScale = Vector3.one;
        }
        yield return new WaitForSecondsRealtime(0.25f);
        bool[] awarded = { result.completed, result.efficient, result.strong };
        for (int i = 0; i < starIcons.Length; i++)
        {
            if (!awarded[i]) continue;
            bool usesArtwork = starIcons[i] is Image;
            if (starIcons[i] is Image image) image.sprite = earnedStarSprite;
            float elapsed = 0f;
            while (elapsed < 0.28f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.28f);
                starIcons[i].color = usesArtwork ? Color.white : Color.Lerp(muted, earned, t);
                starIcons[i].rectTransform.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.24f);
                yield return null;
            }
            starIcons[i].rectTransform.localScale = Vector3.one;
            starIcons[i].color = usesArtwork ? Color.white : earned;
            yield return new WaitForSecondsRealtime(0.08f);
        }
        starAnimation = null;
    }

    private void BindAuthoredLayout()
    {
        if (levelCompletePanel == null) return;
        Transform paper = levelCompletePanel.transform.Find("Completion Safe Area/Wood Frame/Cream Panel");
        if (paper == null)
        {
            Debug.LogError("Level Complete has no authored receipt layout. Use Tools/Civil Craft/Author Level Result Dialogs.", this);
            return;
        }

        feedbackText = FindUI<TextMeshProUGUI>(paper, "Feedback");
        completionTitleTutorialTarget = FindUI<RectTransform>(paper, "Title");
        completionStarsTutorialTarget = FindUI<RectTransform>(paper, "Contract Stars");
        completionReceiptTutorialTarget = FindUI<RectTransform>(paper, "Material Receipt");
        Transform rewards = paper.Find("Reward Summary");
        rewardStatusText = FindUI<TextMeshProUGUI>(rewards, "Claim Status");
        baseRewardText = FindUI<TextMeshProUGUI>(rewards, "Base Reward");
        bonusText = FindUI<TextMeshProUGUI>(rewards, "Bonus");
        penaltyText = FindUI<TextMeshProUGUI>(rewards, "Penalty");
        goldEarnedText = FindUI<TextMeshProUGUI>(rewards, "Gold Earnings");
        expEarnedText = FindUI<TextMeshProUGUI>(rewards, "EXP Earnings");
        goldEarnedIcon = FindUI<Image>(rewards, "Gold Earnings Icon");
        expEarnedIcon = FindUI<Image>(rewards, "EXP Earnings Icon");
        if (goldEarnedIcon != null)
        {
            goldEarnedIcon.sprite = CurrencyIconCatalog.Get(CurrencyIconKind.Coin);
            goldEarnedIcon.enabled = goldEarnedIcon.sprite != null;
        }
        if (expEarnedIcon != null)
        {
            expEarnedIcon.sprite = CurrencyIconCatalog.Get(CurrencyIconKind.Experience);
            expEarnedIcon.enabled = expEarnedIcon.sprite != null;
        }
        bridgePhotoDisplay = FindUI<RawImage>(paper, "Bridge Photo Slot/Bridge Photo Frame/Rounded Photo Mask/Bridge Photo");
        stressText = FindUI<TextMeshProUGUI>(paper, "Peak Stress Card/Peak Stress");
        Transform receipt = paper.Find("Material Receipt");
        receiptContentParent = receipt != null ? receipt.Find("Receipt Scroll/Viewport/Receipt Items") : null;
        costText = FindUI<TextMeshProUGUI>(receipt, "Total Cost");
        budgetText = FindUI<TextMeshProUGUI>(receipt, "Budget");
        receiptBalanceText = FindUI<TextMeshProUGUI>(receipt, "Remaining");
        receiptStampText = FindUI<TextMeshProUGUI>(receipt, "Budget Stamp");
        guestReceiptPlaceholder = receiptContentParent != null
            ? receiptContentParent.Find("Host Bridge Receipt")?.gameObject : null;
        costPercentageText = null;

        for (int i = 0; i < 3; i++)
        {
            Transform column = paper.Find("Contract Stars/Star Criterion " + (i + 1));
            starIcons[i] = FindUI<Graphic>(column, "Star");
            starLabels[i] = FindUI<TextMeshProUGUI>(column, "Requirement");
        }

        if (IsMultiplayerScene)
        {
            multiplayerBackButton = FindUI<Button>(paper, "Back to Build");
            multiplayerSaveButton = FindUI<Button>(paper, "Save for Session");
            guestWaitingText = FindUI<TextMeshProUGUI>(paper, "Guest Waiting");
            completionSaveTutorialTarget = null;
        }
        else completionSaveTutorialTarget = FindUI<RectTransform>(paper, "Save & Continue");
    }

    private static T FindUI<T>(Transform root, string path) where T : Component
    {
        Transform child = root != null ? root.Find(path) : null;
        return child != null ? child.GetComponent<T>() : null;
    }

    private void InitializeReceiptFont()
    {
        if (receiptSourceFont == null) return;
        TMP_FontAsset fallback = feedbackText != null ? feedbackText.font : TMP_Settings.defaultFontAsset;
        ReceiptFont = TMP_FontAsset.CreateFontAsset(receiptSourceFont);
        ReceiptFont.name = "Fake Receipt Runtime SDF";
        ReceiptFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
        if (fallback != null) ReceiptFont.fallbackFontAssetTable.Add(fallback);
        Transform receipt = completionReceiptTutorialTarget;
        if (receipt != null)
            foreach (TextMeshProUGUI label in receipt.GetComponentsInChildren<TextMeshProUGUI>(true))
                label.font = ReceiptFont;
    }

#if UNITY_EDITOR
    public void AuthorReceiptLayout()
    {
        if (levelCompletePanel == null) return;
        TMP_FontAsset font = feedbackText != null ? feedbackText.font : TMP_Settings.defaultFontAsset;
        foreach (Transform child in levelCompletePanel.transform) child.gameObject.SetActive(false);
        var root = levelCompletePanel.GetComponent<RectTransform>();
        if (root == null) return;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero; root.localScale = Vector3.one;
        var background = root.GetComponent<Image>();
        if (background == null) background = root.gameObject.AddComponent<Image>();
        background.sprite = null; background.color = new Color(0.08f, 0.055f, 0.035f, 0.78f);
        background.raycastTarget = true;
        var safe = CompletionReceiptLayout.Box(root, "Completion Safe Area", 0, 0, 1, 1);
        safe.gameObject.AddComponent<CompletionSafeArea>();
        var frame = CompletionReceiptLayout.Panel(safe, "Wood Frame", .04f,.045f,.96f,.955f, new Color32(90,55,31,255));
        var paper = CompletionReceiptLayout.Panel(frame, "Cream Panel", .004f,.007f,.996f,.993f, new Color32(248,233,204,255));
        completionTitleTutorialTarget = CompletionReceiptLayout.Label(
            paper,"Title","BRIDGE COMPLETE",.025f,.905f,.59f,.985f,54,font,TextAlignmentOptions.Center).rectTransform;
        var starRow = CompletionReceiptLayout.Box(paper,"Contract Stars",.025f,.74f,.59f,.90f);
        completionStarsTutorialTarget = starRow;
        string[] criteria = { "COMPLETION", "EFFICIENCY", "STRENGTH" };
        for (int i = 0; i < 3; i++)
        {
            var column = CompletionReceiptLayout.Panel(starRow,"Star Criterion " + (i + 1),
                i/3f+.006f,0,(i+1)/3f-.006f,1,new Color32(239,220,184,255));
            var icon = CompletionReceiptLayout.Box(column,"Star",.14f,.79f,.14f,.79f);
            icon.sizeDelta = new Vector2(44f,44f);
            CompletionReceiptLayout.Label(column,"Criterion",criteria[i],.26f,.66f,.97f,.94f,22,font);
            var rule = CompletionReceiptLayout.Box(column,"Divider",.07f,.62f,.93f,.62f);
            rule.sizeDelta = new Vector2(0,1.5f);
            rule.gameObject.AddComponent<Image>().color = new Color32(161,126,82,110);
            if (earnedStarSprite != null && unearnedStarSprite != null)
            {
                Image starImage = icon.gameObject.AddComponent<Image>();
                starImage.sprite = unearnedStarSprite;
                starImage.preserveAspect = true;
                starImage.color = Color.white;
                starIcons[i] = starImage;
            }
            else starIcons[i] = icon.gameObject.AddComponent<CompletionStarGraphic>();
            starIcons[i].raycastTarget = false;
            starLabels[i] = CompletionReceiptLayout.Label(column,"Requirement","",.05f,.035f,.95f,.60f,24,font,TextAlignmentOptions.Center);
            starLabels[i].fontSizeMin = 22f;
        }
        var photoSlot = CompletionReceiptLayout.Box(paper,"Bridge Photo Slot",.025f,.365f,.59f,.725f);
        var photoFrame = CompletionReceiptLayout.Panel(photoSlot,"Bridge Photo Frame",0,0,1,1, new Color32(131,105,78,255));
        // Fit the entire frame, not the image inside it: no thick pillarbox sidebars.
        var aspect = photoFrame.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = 16f/9f;
        var photoClip = CompletionReceiptLayout.Box(photoFrame,"Rounded Photo Mask",0,0,1,1);
        photoClip.offsetMin = new Vector2(3,3); photoClip.offsetMax = new Vector2(-3,-3);
        var clipImage = photoClip.gameObject.AddComponent<Image>();
        clipImage.raycastTarget = false; CompletionReceiptLayout.Round(clipImage,15f);
        photoClip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var photo = CompletionReceiptLayout.Box(photoClip,"Bridge Photo",0,0,1,1);
        bridgePhotoDisplay = photo.gameObject.AddComponent<RawImage>();
        bridgePhotoDisplay.raycastTarget = false;
        var stress = CompletionReceiptLayout.Panel(paper,"Peak Stress Card",.025f,.315f,.59f,.358f,new Color32(229,232,204,255));
        stressText = CompletionReceiptLayout.Label(stress,"Peak Stress","Peak Bridge Stress: 0%",.035f,0,.965f,1,28,font,TextAlignmentOptions.Center);
        var rewards = CompletionReceiptLayout.Panel(paper,"Reward Summary",.025f,.13f,.59f,.30f,new Color32(243,226,195,255));
        CompletionReceiptLayout.Label(rewards,"Heading","REWARDS",.035f,.77f,.43f,.98f,24,font);
        rewardStatusText = CompletionReceiptLayout.Label(rewards,"Claim Status","",.44f,.77f,.965f,.98f,22,font,TextAlignmentOptions.MidlineRight);
        CompletionReceiptLayout.Divider(rewards,"Rewards Header Divider",.035f,.73f,.965f);
        baseRewardText = CompletionReceiptLayout.Label(rewards,"Base Reward","",.03f,.29f,.32f,.70f,26,font,TextAlignmentOptions.Center);
        bonusText = CompletionReceiptLayout.Label(rewards,"Bonus","",.34f,.29f,.65f,.70f,26,font,TextAlignmentOptions.Center);
        penaltyText = CompletionReceiptLayout.Label(rewards,"Penalty","",.67f,.29f,.97f,.70f,26,font,TextAlignmentOptions.Center);
        CompletionReceiptLayout.Divider(rewards,"Rewards Total Divider",.035f,.25f,.965f);
        goldEarnedIcon = CurrencyIconCatalog.EnsureIcon(
            rewards, "Gold Earnings Icon", CurrencyIconKind.Coin,
            new Vector2(.035f, .035f), new Vector2(.085f, .225f));
        goldEarnedText = CompletionReceiptLayout.Label(rewards,"Gold Earnings","",.09f,.015f,.69f,.235f,28,font);
        expEarnedIcon = CurrencyIconCatalog.EnsureIcon(
            rewards, "EXP Earnings Icon", CurrencyIconKind.Experience,
            new Vector2(.70f, .035f), new Vector2(.75f, .225f));
        expEarnedText = CompletionReceiptLayout.Label(rewards,"EXP Earnings","",.755f,.015f,.965f,.235f,28,font,TextAlignmentOptions.MidlineRight);
        var receipt = CompletionReceiptLayout.Box(paper,"Material Receipt",.62f,.12f,.98f,.975f);
        completionReceiptTutorialTarget = receipt;
        if (receiptBackground != null)
        {
            var image = receipt.gameObject.AddComponent<Image>();
            image.sprite = receiptBackground; image.color = Color.white; image.raycastTarget = false;
        }
        else receipt.gameObject.AddComponent<CompletionReceiptPaper>().color = new Color32(235,235,235,255);
        CompletionReceiptLayout.Label(receipt,"Receipt Heading","MATERIAL RECEIPT",.08f,.865f,.92f,.94f,30,font,TextAlignmentOptions.Center);
        CompletionReceiptLayout.Label(receipt,"Item Column","ITEM",.07f,.785f,.6f,.85f,23,font);
        CompletionReceiptLayout.Label(receipt,"Amount Column","AMOUNT",.61f,.785f,.93f,.85f,23,font,TextAlignmentOptions.MidlineRight);
        CompletionReceiptLayout.Divider(receipt,"Header Divider",.075f,.775f,.925f);
        var scrollRect = CompletionReceiptLayout.Box(receipt,"Receipt Scroll",.065f,.37f,.935f,.755f);
        var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
        var viewport = CompletionReceiptLayout.Panel(scrollRect,"Viewport",0,0,1,1,new Color(1,1,1,.001f));
        viewport.GetComponent<Image>().raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = CompletionReceiptLayout.Box(viewport,"Receipt Items",0,1,1,1);
        content.pivot = new Vector2(.5f,1); content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true; layout.childControlWidth = true;
        layout.childForceExpandHeight = false; layout.childForceExpandWidth = true; layout.spacing = 8;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
        receiptContentParent = content;
        guestReceiptPlaceholder = new GameObject("Host Bridge Receipt",
            typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        guestReceiptPlaceholder.transform.SetParent(content, false);
        guestReceiptPlaceholder.GetComponent<LayoutElement>().preferredHeight = 84f;
        TextMeshProUGUI guestLabel = guestReceiptPlaceholder.GetComponent<TextMeshProUGUI>();
        guestLabel.font = font;
        guestLabel.fontSize = 25f;
        guestLabel.color = CompletionReceiptLayout.Ink;
        guestLabel.alignment = TextAlignmentOptions.Center;
        guestLabel.raycastTarget = false;
        guestLabel.text = "Host bridge result\nSaved in this room only";
        guestReceiptPlaceholder.SetActive(false);
        CompletionReceiptLayout.Divider(receipt,"Total Divider",.075f,.35f,.925f);
        costText = CompletionReceiptLayout.Label(receipt,"Total Cost","",.08f,.27f,.92f,.335f,29,font,TextAlignmentOptions.MidlineRight);
        budgetText = CompletionReceiptLayout.Label(receipt,"Budget","",.08f,.207f,.92f,.265f,25,font,TextAlignmentOptions.MidlineRight);
        receiptBalanceText = CompletionReceiptLayout.Label(receipt,"Remaining","",.08f,.145f,.92f,.203f,25,font,TextAlignmentOptions.MidlineRight);
        receiptStampText = CompletionReceiptLayout.Label(receipt,"Budget Stamp","WITHIN BUDGET",.10f,.067f,.90f,.125f,25,font,TextAlignmentOptions.Center);
        costPercentageText = null; // No unexplained percentage or stress progress bar.
        feedbackText = CompletionReceiptLayout.Label(paper,"Feedback","",.025f,.028f,.47f,.11f,26,font);
        if (IsMultiplayerScene)
        {
            multiplayerBackButton = CompletionReceiptLayout.Button(paper,"Back to Build",.49f,.025f,.69f,.11f,
                new Color32(239,214,170,255),font,RetrySimulation);
            multiplayerSaveButton = CompletionReceiptLayout.Button(paper,"Save for Session",.71f,.025f,.975f,.11f,
                new Color32(228,157,44,255),font,SaveBridgeForSession);
            guestWaitingText = CompletionReceiptLayout.Label(paper,"Guest Waiting",
                "Waiting for host to save or retry...",.49f,.025f,.975f,.11f,
                25,font,TextAlignmentOptions.Center);
            guestWaitingText.gameObject.SetActive(false);
            completionSaveTutorialTarget = null;
        }
        else
        {
            CompletionReceiptLayout.Button(paper,"Retry",.49f,.025f,.69f,.11f,
                new Color32(239,214,170,255),font,RetrySimulation);
            Button saveButton = CompletionReceiptLayout.Button(
                paper,"Save & Continue",.71f,.025f,.975f,.11f,
                new Color32(228,157,44,255),font,SaveAndBakeBridge);
            completionSaveTutorialTarget = saveButton.transform as RectTransform;
        }
        safe.gameObject.AddComponent<CompletionEntranceMotion>().Configure(frame,receipt,photoFrame);
    }
#endif

    public void RetrySimulation()
    {
        if (IsFirstCompletionTutorialBlockingActions()) return;
        if (GameManager.Instance != null && !GameManager.Instance.RestoreCompletedCargoTestForRetry()) return;
        CompleteFirstCompletionTutorialIfActive();

        ResetCompletionState();

        if (cachedPhysicsManager != null) cachedPhysicsManager.StopPhysicsAndReset();
        
        BarCreator creator = FindObjectOfType<BarCreator>();
        if (creator != null) creator.isSimulating = false;

        ClosePanel();
    }

    public void SaveAndBakeBridge()
    {
        if (IsMultiplayerScene)
        {
            Debug.LogWarning("[Multiplayer] Bridge saving is disabled in this scene.", this);
            return;
        }

        if (!levelAlreadyCompleted || lastStarResult == null || !lastStarResult.completed) return;
        if (IsFirstCompletionTutorialBlockingActions()) return;
        CompleteFirstCompletionTutorialIfActive();

        ContractSO completedContract = activeContract;
        BuildLocation completedLocation = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation
            : null;

        if (completedLocation == null && completedContract != null)
        {
            BuildLocation[] allLocations = FindObjectsOfType<BuildLocation>(true);
            foreach (BuildLocation location in allLocations)
            {
                if (location.activeContract == completedContract)
                {
                    completedLocation = location;
                    break;
                }
            }
        }

        if (completedContract == null || completedLocation == null ||
            cachedPhysicsManager == null || PlayerDataManager.Instance == null)
        {
            Debug.LogError(
                "[LevelCompleteManager] Bridge finalization stopped because the contract, build location, " +
                "physics manager, or player data manager is missing.", this);
            return;
        }

        // Capture this before any completion/reward mutation below. A redesign may
        // happen after reloading the game, so the persistent completed-contract list
        // is authoritative; the local alreadyPaidContracts cache is not enough.
        bool wasContractAlreadyCompleted = IsContractPaid(completedContract.ContractID);

        // Transaction order is important: capture -> validate -> persist geometry
        // must succeed before contract completion, rewards, alerts, or NPC movement.
        // Reset first so saved coordinates represent the player's original design,
        // not the bridge's deformed pose at the end of the load test.
        cachedPhysicsManager.StopPhysicsAndReset();

        if (!cachedPhysicsManager.BakeBridge(completedContract))
        {
            Debug.LogError(
                $"[LevelCompleteManager] '{completedContract.name}' was not completed because its bridge could not be baked.",
                this);
            return;
        }

        bool bridgeSaved = PlayerDataManager.Instance.SaveBridgeData(
            completedContract.ContractID,
            completedLocation.bakedPoints,
            completedLocation.bakedBars,
            lastFinalCost,
            lastPeakStress,
            lastStarResult);

        if (!bridgeSaved)
        {
            Debug.LogError(
                $"[LevelCompleteManager] '{completedContract.name}' remains unfinished because bridge geometry could not be persisted. " +
                "The baked objects remain in the scene so saving can be retried.", this);
            return;
        }

        // The tested image becomes persistent only after its bridge geometry is
        // safely committed. Account sessions will queue it for PlayFab upload;
        // guest sessions retain the same pending record for a later import.
        if (currentBridgePhoto != null)
        {
            byte[] photoBytes = currentBridgePhoto.EncodeToPNG();
            if (!PlayerDataManager.Instance.TrySaveBridgePhoto(
                    completedContract.ContractID, photoBytes))
                Debug.LogWarning(
                    $"[LevelCompleteManager] Bridge saved, but its photo could not be queued for '{completedContract.name}'.",
                    this);
        }

        // The new geometry is now safely persisted. Only at this point may the
        // previous scene bridge be permanently removed.
        completedLocation.CommitBridgeRedesign();

        // A bridge-build achievement advances when a valid bridge successfully
        // finishes the level and is persisted. Contract turn-in is tracked
        // separately by CompleteContract as ContractsCompleted.
        PlayerDataManager.Instance.AddBridgeBuilt();
        PlayerDataManager.Instance.TryUnlockBuildLocationAchievement(
            completedLocation.completionAchievement,
            completedContract);

        if (LevelFailedManager.Instance != null) LevelFailedManager.Instance.ResetFailCount();

        NPCContractGiver[] npcs = FindObjectsOfType<NPCContractGiver>();
        NPCContractGiver returnGiver = null;
        foreach (var npc in npcs)
        {
            if (npc.contractToGive == completedContract)
            {
                returnGiver = npc;
                if (!wasContractAlreadyCompleted)
                {
                    npc.isContractCompleted = true;
                }
            }
        }

        if (completedContract.autoCollectReward && !wasContractAlreadyCompleted)
        {
            int earnedGold = GetContractGold(completedContract.ContractID);
            int earnedExp = GetContractExp(completedContract.ContractID);

            bool completionSaved = PlayerDataManager.Instance.CompleteContract(
                completedContract.ContractID,
                earnedGold,
                earnedExp);

            if (!completionSaved &&
                !PlayerDataManager.Instance.IsContractCompleted(completedContract.ContractID))
            {
                Debug.LogError(
                    $"[LevelCompleteManager] Bridge geometry was saved, but completion for '{completedContract.name}' could not be persisted.",
                    this);
                return;
            }

            MarkContractAsPaid(completedContract.ContractID);
            
            if (ObjectiveTrackerUI.Instance != null)
            {
                ObjectiveTrackerUI.Instance.ClearObjective(completedContract);
            }
        }

        // Persist the unread objective update before exiting Build Mode or changing scenes.
        // This does not depend on ObjectiveTrackerUI being present in the current scene.
        if (!completedContract.autoCollectReward && !wasContractAlreadyCompleted)
            PlayerDataManager.Instance.MarkObjectiveAlertUnread();

        if (ObjectiveTrackerUI.Instance != null &&
            !completedContract.autoCollectReward && !wasContractAlreadyCompleted)
        {
            ObjectiveTrackerUI.Instance.NotifyBridgeBuilt(completedContract.ContractID);
        }
        
        if (CommandManager.Instance != null) CommandManager.Instance.ClearHistory();

        // Saving, rather than merely passing the test, is the authoritative point
        // at which the completed contract's live load leaves the overworld.
        LiveLoadVehicle.ScheduleHideForSavedContract(completedContract);

        // --- THE FIX: Make sure the state is fully reset before moving on! ---
        ResetCompletionState();

        // --- THE FIX: Tell the game we are exiting Build Mode BEFORE we close the panel! ---
        // This ensures the ClosePanel method realizes we are leaving and restores player controls!
        if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode();
        
        ClosePanel();

        if (completedContract != null)
            BridgeSavedAtLocation?.Invoke(completedContract, completedLocation);
        if (!completedContract.autoCollectReward && !wasContractAlreadyCompleted && returnGiver != null)
            StartCoroutine(ShowTurnInGuideAfterExit(returnGiver, completedContract));
    }

    public void SaveBridgeForSession()
    {
        if (!IsMultiplayerScene || FusionConnectionManager.Instance == null ||
            !FusionConnectionManager.Instance.IsHosting)
        {
            Debug.LogWarning("[Multiplayer] Only the active room host can save a session bridge.", this);
            return;
        }
        if (!levelAlreadyCompleted || lastStarResult == null || !lastStarResult.completed) return;

        BuildLocation location = GameManager.Instance != null
            ? GameManager.Instance.ActiveBuildLocation : null;
        ContractSO contract = location != null ? location.activeContract : null;
        if (location == null || contract == null || cachedPhysicsManager == null)
        {
            Debug.LogError("[Multiplayer] Cannot save the session bridge without its active build location, contract, and physics manager.", this);
            return;
        }

        // Keep only the scene objects. Never call PlayerDataManager, complete a
        // contract, grant rewards, or create a bridge file in Multiplayer.
        cachedPhysicsManager.StopPhysicsAndReset();
        if (!cachedPhysicsManager.BakeBridge())
        {
            Debug.LogError("[Multiplayer] Session bridge bake failed; the build remains available to retry.", this);
            return;
        }

        location.CommitBridgeRedesign();
        if (CommandManager.Instance != null) CommandManager.Instance.ClearHistory();
        if (LevelFailedManager.Instance != null) LevelFailedManager.Instance.ResetFailCount();
        ResetCompletionState();
        if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode();
        ClosePanel();
        Debug.Log("[Multiplayer] Bridge saved for this room only. It will disappear when the session ends.", this);
    }

    public void ShowGuestSessionCompletion(FusionMultiplayerAvatar.SessionCompletionSnapshot result,
        FusionMultiplayerAvatar source)
    {
        if (!IsMultiplayerScene || FusionConnectionManager.Instance == null ||
            !FusionConnectionManager.Instance.IsClientConnected ||
            levelCompletePanel == null || guestSessionCompletionVisible) return;

        guestSessionCompletionVisible = true;
        temporarilyHiddenPanels.Clear();
        foreach (GameObject ui in uiElementsToHide)
        {
            if (ui == null || !ui.activeSelf) continue;
            temporarilyHiddenPanels.Add(ui);
            ui.SetActive(false);
        }

        InputManager input = FindObjectOfType<InputManager>();
        if (input != null)
        {
            input.SetPlayerInputEnable(false);
            input.SetLookEnabled(false);
        }
        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = false;

        if (multiplayerBackButton != null) multiplayerBackButton.gameObject.SetActive(false);
        if (multiplayerSaveButton != null) multiplayerSaveButton.gameObject.SetActive(false);
        if (guestWaitingText != null) guestWaitingText.gameObject.SetActive(true);

        lastStarResult = new ContractStarResult
        {
            completed = (result.StarFlags & 1) != 0,
            efficient = (result.StarFlags & 2) != 0,
            strong = (result.StarFlags & 4) != 0,
            hadBrokenParts = (result.StarFlags & 8) != 0,
            totalCost = result.Cost,
            peakStress = result.PeakStress,
            costTarget = result.CostTarget,
            stressTarget = result.StressTarget
        };

        if (feedbackText != null) feedbackText.text = "Host's bridge passed the test.";
        if (costText != null)
        {
            costText.text = $"Total Cost: ₱{Mathf.RoundToInt(result.Cost):N0}";
            costText.color = result.Cost > result.Budget
                ? new Color32(164, 62, 45, 255) : CompletionReceiptLayout.Ink;
        }
        if (budgetText != null) budgetText.text = $"Budget: ₱{Mathf.RoundToInt(result.Budget):N0}";
        if (stressText != null)
        {
            stressText.text = $"Peak Bridge Stress: {result.PeakStress:0.0}%";
            stressText.color = result.PeakStress >= 100f ? new Color32(164, 62, 45, 255) :
                result.PeakStress >= 50f ? new Color32(155, 99, 27, 255) :
                new Color32(76, 110, 47, 255);
        }
        if (receiptBalanceText != null)
            receiptBalanceText.text = $"{(result.Cost > result.Budget ? "Over budget" : "Remaining")}   " +
                $"₱{Mathf.RoundToInt(Mathf.Abs(result.Budget - result.Cost)):N0}";
        if (receiptStampText != null)
        {
            bool overBudget = result.Cost > result.Budget;
            receiptStampText.text = overBudget ? "OVER BUDGET" : "WITHIN BUDGET";
            receiptStampText.color = overBudget ? new Color32(164, 62, 45, 255) :
                new Color32(76, 110, 47, 255);
        }
        if (rewardStatusText != null) rewardStatusText.text = "SESSION ONLY";
        if (baseRewardText != null) baseRewardText.text = "BASE\n0";
        if (bonusText != null) bonusText.text = "BONUS\n+0";
        if (penaltyText != null) penaltyText.text = "DEDUCTIONS\n-0";
        if (goldEarnedText != null) goldEarnedText.text = "TOTAL  0";
        if (expEarnedText != null) expEarnedText.text = "+0";

        if (receiptContentParent != null)
        {
            if (guestReceiptPlaceholder != null) guestReceiptPlaceholder.SetActive(false);
            foreach (Transform child in receiptContentParent)
                if (guestReceiptPlaceholder == null || child.gameObject != guestReceiptPlaceholder)
                    Destroy(child.gameObject);
            int rows = source != null
                ? source.PopulateGuestReceiptRows(receiptContentParent, receiptRowPrefab,
                    result.ContractHash) : 0;
            if (rows == 0 && guestReceiptPlaceholder != null)
                guestReceiptPlaceholder.SetActive(true);
        }

        CaptureGuestBridgePhoto(result.ContractHash);
        SimulationLessonPresenter.HideForResultOverlay();
        UIReservedRegionLayout.NotifyLayoutChanging();
        levelCompletePanel.SetActive(true);
        ShowStarResults(null);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX("Level_Complete");
    }

    public void HideGuestSessionCompletion()
    {
        if (!guestSessionCompletionVisible) return;
        guestSessionCompletionVisible = false;
        if (guestReceiptPlaceholder != null) guestReceiptPlaceholder.SetActive(false);
        if (multiplayerBackButton != null) multiplayerBackButton.gameObject.SetActive(true);
        if (multiplayerSaveButton != null) multiplayerSaveButton.gameObject.SetActive(true);
        if (guestWaitingText != null) guestWaitingText.gameObject.SetActive(false);
        if (starAnimation != null) StopCoroutine(starAnimation);
        starAnimation = null;
        ClosePanel();
    }

    private void CaptureGuestBridgePhoto(int contractHash)
    {
        if (currentBridgePhoto != null) Destroy(currentBridgePhoto);
        currentBridgePhoto = null;
        if (bridgePhotoDisplay != null) bridgePhotoDisplay.texture = null;
        Camera camera = null;
        foreach (BuildLocation location in FindObjectsOfType<BuildLocation>(true))
        {
            if (location == null || location.gameObject.scene != gameObject.scene ||
                location.activeContract == null ||
                FusionMultiplayerAvatar.StableHash(location.activeContract.ContractID) != contractHash)
                continue;
            camera = location.cinematicCamera != null
                ? location.cinematicCamera : location.locationCamera;
            break;
        }
        if (camera == null || bridgePhotoDisplay == null) return;

        RenderTexture render = new RenderTexture(960, 540, 24);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        bool wasEnabled = camera.enabled;
        try
        {
            camera.targetTexture = render;
            camera.enabled = true;
            camera.Render();
            RenderTexture.active = render;
            Texture2D photo = new Texture2D(960, 540, TextureFormat.RGB24, false);
            photo.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            photo.Apply();
            currentBridgePhoto = photo;
            bridgePhotoDisplay.texture = photo;
        }
        finally
        {
            camera.enabled = wasEnabled;
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Destroy(render);
        }
    }

    private IEnumerator ShowTurnInGuideAfterExit(NPCContractGiver giver, ContractSO contract)
    {
        // Let the exit transition and reward popups restore the overworld UI first.
        yield return null;
        while ((GameManager.Instance != null && GameManager.Instance.IsInBuildMode()) ||
               (UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel))
            yield return null;
        if (giver != null && TutorialManager.Instance != null &&
            PlayerDataManager.Instance != null && !PlayerDataManager.Instance.IsContractCompleted(contract.ContractID))
            TutorialManager.Instance.GuideToContractGiver(giver, contract);
    }

    private void TryStartFirstCompletionTutorial(ContractSO completedContract)
    {
        if (IsMultiplayerScene ||
            !showFirstCompletionTutorial || firstCompletionTutorial == null ||
            completedContract == null || TutorialManager.Instance == null ||
            PlayerDataManager.Instance == null || PlayerDataManager.Instance.CurrentData == null ||
            PlayerDataManager.Instance.CurrentData.lifetimeBridgesBuilt > 0)
        {
            return;
        }

        BindFirstCompletionTutorialTargets();
        Canvas.ForceUpdateCanvases();
        TutorialManager.Instance.PlayPriorityTutorial(firstCompletionTutorial);
    }

    private void EnsureFirstCompletionTutorialDraft()
    {
        if (firstCompletionTutorial == null)
        {
            GameObject sequenceObject = new GameObject("Sequence_FirstLevelCompletion");
            sequenceObject.transform.SetParent(transform, false);
            firstCompletionTutorial = sequenceObject.AddComponent<TutorialSequence>();
        }

        if (firstCompletionTutorial.tutorialSteps != null &&
            firstCompletionTutorial.tutorialSteps.Length > 0)
        {
            return;
        }

        firstCompletionTutorial.lessonName = "Sequence_FirstLevelCompletion";
        firstCompletionTutorial.requiredPreviousLesson = string.Empty;
        firstCompletionTutorial.playOnStart = false;
        firstCompletionTutorial.nextSequence = null;
        firstCompletionTutorial.autoStartNextSequence = false;
        firstCompletionTutorial.tutorialSteps = new[]
        {
            CreateFirstCompletionStep(
                "<b><#E09500>BRIDGE COMPLETE!</color></b> This report summarizes how your bridge performed."),
            CreateFirstCompletionStep(
                "The three result cards show <b><#E09500>COMPLETION</color></b>, <b><#E09500>EFFICIENCY</color></b>, and <b><#E09500>STRENGTH</color></b>. Your best star result is saved."),
            CreateFirstCompletionStep(
                "Review the <b><#E09500>MATERIAL RECEIPT</color></b> to compare your total cost with the contract budget. You can also check the bridge's peak stress."),
            CreateFirstCompletionStep(
                "Choose <b><#E09500>RETRY</color></b> to improve the design, or <b><#E09500>SAVE & CONTINUE</color></b> to keep the bridge and finish this build location.",
                false)
        };
    }

    private static TutorialStep CreateFirstCompletionStep(string message, bool showNextButton = true)
    {
        return new TutorialStep
        {
            message = message,
            screenPosition = TutorialPosition.Center,
            showNextButton = showNextButton,
            canSkip = true,
            lockLook = false,
            lockJump = false,
            stepWaypoints = new List<GuiderWaypoint>(),
            worldHighlightObject = null,
            usePointer = false,
            pointerTarget = null,
            pointerOffset = new Vector2(0f, 80f),
            pointerRotation = 180f,
            advanceOnClick = false,
            requiredAction = TutorialStepAction.None
        };
    }

    private void BindFirstCompletionTutorialTargets()
    {
        TutorialStep[] steps = firstCompletionTutorial != null
            ? firstCompletionTutorial.tutorialSteps
            : null;
        if (steps == null) return;

        BindTutorialPointer(steps, 0, completionTitleTutorialTarget);
        BindTutorialPointer(steps, 1, completionStarsTutorialTarget);
        BindTutorialPointer(steps, 2, completionReceiptTutorialTarget);
        BindTutorialPointer(steps, 3, completionSaveTutorialTarget);
    }

    private static void BindTutorialPointer(TutorialStep[] steps, int index, RectTransform target)
    {
        if (index < 0 || index >= steps.Length || steps[index] == null) return;
        steps[index].pointerTarget = target;
        steps[index].usePointer = target != null;
    }

    private bool IsFirstCompletionTutorialBlockingActions()
    {
        if (firstCompletionTutorial == null || firstCompletionTutorial.tutorialSteps == null ||
            firstCompletionTutorial.tutorialSteps.Length == 0 || TutorialManager.Instance == null ||
            !TutorialManager.Instance.IsPlayingSequence(firstCompletionTutorial))
        {
            return false;
        }

        int finalStepIndex = firstCompletionTutorial.tutorialSteps.Length - 1;
        if (TutorialManager.Instance.CurrentStepIndex >= finalStepIndex) return false;

        Debug.Log("[LevelCompleteManager] Finish the completion-screen introduction before choosing an action.", this);
        return true;
    }

    private void CompleteFirstCompletionTutorialIfActive()
    {
        if (firstCompletionTutorial == null || TutorialManager.Instance == null ||
            !TutorialManager.Instance.IsPlayingSequence(firstCompletionTutorial))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(firstCompletionTutorial.lessonName))
            TutorialManager.Instance.CompleteTutorialIfPlaying(firstCompletionTutorial.lessonName);
    }

    public void ClosePanel()
    {
        if (levelCompletePanel != null) levelCompletePanel.SetActive(false);

        foreach (GameObject ui in temporarilyHiddenPanels)
        {
            if (ui != null) ui.SetActive(true);
        }
        temporarilyHiddenPanels.Clear();

        bool isBuilding = (GameManager.Instance != null && GameManager.Instance.IsInBuildMode());
        bool shouldEnableInput = !isBuilding;

        InputManager inputObj = FindObjectOfType<InputManager>();
        if (inputObj != null)
        {
            inputObj.SetPlayerInputEnable(shouldEnableInput);
            inputObj.SetLookEnabled(shouldEnableInput);
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) player.enabled = shouldEnableInput;
    }
}
