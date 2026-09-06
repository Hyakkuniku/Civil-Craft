using System;
using UnityEngine;
using UnityEngine.UI; 
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEngine.EventSystems;

// Shared code-built hierarchy keeps every scene's completion panel consistent.
internal static class CompletionReceiptLayout
{
    private static Sprite roundedSprite;
    internal static void Round(Image image, float radius = 18f)
    {
        if (roundedSprite == null)
        {
            const int size = 64;
            const float corner = 18f;
            var texture = new Texture2D(size,size,TextureFormat.RGBA32,false);
            texture.name = "Completion rounded corners";
            texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size*size];
            for (int y=0;y<size;y++) for (int x=0;x<size;x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x+.5f-size*.5f)-(size*.5f-corner),0);
                float dy = Mathf.Max(Mathf.Abs(y+.5f-size*.5f)-(size*.5f-corner),0);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(corner-Mathf.Sqrt(dx*dx+dy*dy)+.5f)*255);
                pixels[y*size+x] = new Color32(255,255,255,alpha);
            }
            texture.SetPixels32(pixels); texture.Apply(false,true);
            roundedSprite = Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100,0,
                SpriteMeshType.FullRect,new Vector4(19,19,19,19));
        }
        image.sprite = roundedSprite; image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 18f / Mathf.Max(1f,radius);
    }
    internal static readonly Color Ink = new Color32(73,48,29,255);
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
        if (name != "Dash" && name != "Viewport") Round(image);
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
    internal static void Button(Transform parent,string title,float x0,float y0,float x1,float y1,Color color,
        TMP_FontAsset font,UnityEngine.Events.UnityAction action)
    {
        var rect = Panel(parent,title,x0,y0,x1,y1,color);
        var image = rect.GetComponent<Image>(); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.onClick.AddListener(action);
        rect.gameObject.AddComponent<CompletionButtonMotion>();
        Label(rect,"Label",title,.04f,.08f,.96f,.92f,32,font,TextAlignmentOptions.Center);
    }
    internal static void Divider(Transform parent,string name,float x0,float y,float x1)
    {
        var line = Box(parent,name,x0,y,x1,y);
        line.sizeDelta = new Vector2(0,1.2f);
        for (int i=0;i<32;i++)
            Panel(line,"Dash",i/32f,0,(i+.65f)/32f,1,new Color32(112,109,103,150));
    }
}

internal sealed class CompletionButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    private bool hovered;
    private bool pressed;
    private void OnDisable() { hovered = pressed = false; transform.localScale = Vector3.one; }
    private void Update()
    {
        var button = GetComponent<Button>();
        float target = button != null && button.IsInteractable() ? (pressed ? .91f : hovered ? 1.045f : 1f) : 1f;
        transform.localScale = Vector3.Lerp(transform.localScale,Vector3.one*target,1f-Mathf.Exp(-22f*Time.unscaledDeltaTime));
    }
    public void OnPointerEnter(PointerEventData e) { hovered = true; }
    public void OnPointerExit(PointerEventData e) { hovered = pressed = false; }
    public void OnPointerDown(PointerEventData e) { if(e.button == PointerEventData.InputButton.Left) pressed = true; }
    public void OnPointerUp(PointerEventData e) { pressed = false; }
}

internal sealed class CompletionEntranceMotion : MonoBehaviour
{
    private RectTransform frame;
    private RectTransform receipt;
    private CanvasGroup panelGroup;
    private CanvasGroup receiptGroup;
    private CanvasGroup photoGroup;
    private Vector2 receiptPosition;
    private Coroutine animationRoutine;

    internal void Configure(RectTransform panel,RectTransform slip,RectTransform photo)
    {
        frame = panel; receipt = slip; receiptPosition = slip.anchoredPosition;
        panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        receiptGroup = slip.gameObject.AddComponent<CanvasGroup>();
        photoGroup = photo.gameObject.AddComponent<CanvasGroup>();
    }
    private void OnEnable()
    {
        if (frame != null) animationRoutine = StartCoroutine(Reveal());
    }
    private IEnumerator Reveal()
    {
        panelGroup.interactable = false;
        panelGroup.alpha = 0;
        receiptGroup.alpha = photoGroup.alpha = 0;
        frame.localScale = Vector3.one * .78f;
        receipt.anchoredPosition = receiptPosition + Vector2.up * 125f;
        float elapsed = 0;
        while (elapsed < 1.15f)
        {
            elapsed += Time.unscaledDeltaTime;
            panelGroup.alpha = Ease(elapsed/.4f);
            // A small overshoot makes the entrance visible without exceeding the safe margins.
            frame.localScale = Vector3.one * Mathf.LerpUnclamped(.78f,1f,Pop(elapsed/.7f));
            photoGroup.alpha = Ease((elapsed-.22f)/.5f);
            float slip = Pop((elapsed-.4f)/.75f);
            receiptGroup.alpha = Ease((elapsed-.4f)/.35f);
            receipt.anchoredPosition = receiptPosition + Vector2.up * (125f*(1f-slip));
            yield return null;
        }
        Restore(); animationRoutine = null;
    }
    private static float Ease(float t) { t = Mathf.Clamp01(t); return 1f-Mathf.Pow(1f-t,3f); }
    private static float Pop(float t)
    {
        t = Mathf.Clamp01(t)-1f;
        return 1f + 2.2f*t*t*t + 1.2f*t*t;
    }
    private void OnDisable()
    {
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = null; Restore();
    }
    private void Restore()
    {
        if(frame == null) return;
        frame.localScale = Vector3.one; receipt.anchoredPosition = receiptPosition;
        panelGroup.alpha = receiptGroup.alpha = photoGroup.alpha = 1f;
        panelGroup.interactable = true;
    }
}

internal sealed class CompletionSafeArea : MonoBehaviour
{
    private void OnEnable() { Apply(); }
    private void LateUpdate() { Apply(); }
    private void Apply()
    {
        var rect = (RectTransform)transform;
        var parent = rect.parent as RectTransform;
        var canvas = GetComponentInParent<Canvas>();
        if (parent == null || canvas == null || parent.rect.width <= 0 || parent.rect.height <= 0) return;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Rect safe = Screen.safeArea;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,safe.min,camera,out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,safe.max,camera,out Vector2 max);
        rect.anchorMin = new Vector2(Mathf.Clamp01((min.x-parent.rect.xMin)/parent.rect.width),Mathf.Clamp01((min.y-parent.rect.yMin)/parent.rect.height));
        rect.anchorMax = new Vector2(Mathf.Clamp01((max.x-parent.rect.xMin)/parent.rect.width),Mathf.Clamp01((max.y-parent.rect.yMin)/parent.rect.height));
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}

// A real UI mesh, not a generated bitmap: receipt edges stay crisp at different sizes.
internal sealed class CompletionReceiptPaper : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Rect r = rectTransform.rect; float tooth = Mathf.Min(10f,r.height*.02f);
        AddQuad(vh,new Vector2(r.xMin,r.yMin+tooth),new Vector2(r.xMax,r.yMax-tooth));
        int teeth = Mathf.Max(2,Mathf.RoundToInt(r.width/22f));
        for (int i=0;i<teeth;i++)
        {
            float left = Mathf.Lerp(r.xMin,r.xMax,(float)i/teeth);
            float right = Mathf.Lerp(r.xMin,r.xMax,(float)(i+1)/teeth);
            AddTriangle(vh,new Vector2(left,r.yMax-tooth),new Vector2((left+right)*.5f,r.yMax),new Vector2(right,r.yMax-tooth));
            AddTriangle(vh,new Vector2(left,r.yMin+tooth),new Vector2(right,r.yMin+tooth),new Vector2((left+right)*.5f,r.yMin));
        }
    }
    private void AddTriangle(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c)
    {
        int index=vh.currentVertCount; vh.AddVert(a,color,Vector2.zero); vh.AddVert(b,color,Vector2.zero);
        vh.AddVert(c,color,Vector2.zero); vh.AddTriangle(index,index+1,index+2);
    }
    private void AddQuad(VertexHelper vh,Vector2 min,Vector2 max)
    {
        AddTriangle(vh,min,new Vector2(min.x,max.y),max);
        AddTriangle(vh,min,max,new Vector2(max.x,min.y));
    }
}

[DefaultExecutionOrder(-30)] 
public class LevelCompleteManager : MonoBehaviour
{
    public static LevelCompleteManager Instance { get; private set; }

    /// <summary>
    /// Raised after a successfully tested bridge has been saved/baked, Build Mode
    /// has exited, and the completion panel has closed.
    /// </summary>
    public static event Action<ContractSO, BuildLocation> BridgeSavedAtLocation;

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
    public TMP_FontAsset ReceiptFont { get; private set; }
    public Transform receiptContentParent; 
    public GameObject receiptRowPrefab;    

    [Header("Earnings Breakdown UI")]
    public TextMeshProUGUI baseRewardText; 
    public TextMeshProUGUI bonusText;      
    public TextMeshProUGUI penaltyText;    
    public TextMeshProUGUI goldEarnedText; 
    public TextMeshProUGUI expEarnedText;

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

    private Dictionary<string, int> contractGoldRewards = new Dictionary<string, int>();
    private Dictionary<string, int> contractExpRewards = new Dictionary<string, int>();

    private BridgePhysicsManager cachedPhysicsManager;

    private float lastFinalCost = 0f;
    private float lastPeakStress = 0f;
    private TextMeshProUGUI receiptBalanceText;
    private TextMeshProUGUI receiptStampText;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        BuildReceiptLayout();
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
                    // --- THE FIX: Prioritize finding the exact location tied to this specific contract! ---
                    BuildLocation activeLoc = null;
                    BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
                    foreach (var loc in allLocs)
                    {
                        if (loc.gameObject.scene.name != null && loc.activeContract == currentContract)
                        {
                            activeLoc = loc;
                            break;
                        }
                    }
                    
                    if (activeLoc == null && GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation != null)
                    {
                        activeLoc = GameManager.Instance.ActiveBuildLocation;
                    }

                    if (activeLoc != null && IsBridgeConnected(activeLoc))
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

        HashSet<Point> visited = new HashSet<Point>();
        Queue<Point> queue = new Queue<Point>();

        foreach (Point p in loc.startingAnchors)
        {
            if (p != null && p.gameObject.activeSelf)
            {
                visited.Add(p);
                queue.Enqueue(p);
            }
        }

        while (queue.Count > 0)
        {
            Point current = queue.Dequeue();

            if (loc.endingAnchors.Contains(current)) return true;

            foreach (Bar b in current.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf && b.materialData != null && b.materialData.isRoad)
                {
                    BarStressHandler stress = b.GetComponent<BarStressHandler>();
                    if (stress != null && stress.isBroken) continue;

                    Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                    if (neighbor != null && neighbor.gameObject.activeSelf && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return false; 
    }

    private void OnDestroy()
    {
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

    public void ResetCompletionState()
    {
        levelAlreadyCompleted = false;
        currentSimulationFrames = 0;
    }

    public void CompleteLevel(ContractSO currentContract)
    {
        if (levelAlreadyCompleted) return;
        
        levelAlreadyCompleted = true;
        activeContract = currentContract;

        if (cachedPhysicsManager != null)
        {
            cachedPhysicsManager.lockStressTracking = true;
        }

        LiveLoadVehicle vehicle = FindObjectOfType<LiveLoadVehicle>();
        if (vehicle != null)
        {
            vehicle.StopAndFreezeForWin();
        }

        StartCoroutine(TakeSnapshotAndShowUIRoutine(currentContract));
    }

    private IEnumerator TakeSnapshotAndShowUIRoutine(ContractSO currentContract)
    {
        LiveLoadVehicle finishingVehicle = FindObjectOfType<LiveLoadVehicle>();
        while (finishingVehicle != null && finishingVehicle.IsFinishBraking)
            yield return new WaitForFixedUpdate();

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

            byte[] imageBytes = currentBridgePhoto.EncodeToPNG();
            string photoPath = Application.persistentDataPath + "/" + currentContract.ContractID + "_photo.png";
            File.WriteAllBytes(photoPath, imageBytes);
        }

        float totalCalculatedCost = 0f;

        if (receiptContentParent != null && receiptRowPrefab != null)
        {
            foreach (Transform child in receiptContentParent) Destroy(child.gameObject);

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

        int calculatedGold = 0;
        int calculatedExp = 0;
        int bonusGold = 0;
        int budgetPenalty = 0;
        int failPenalty = 0;

        if (LevelFailedManager.Instance != null)
        {
            failPenalty = LevelFailedManager.Instance.currentFailCount * LevelFailedManager.Instance.goldPenaltyPerFail;
        }

        if (currentContract != null && currentContract.IsTutorialForCurrentPlayer())
        {
            calculatedGold = 0;
            calculatedExp = 0;

            if (feedbackText != null) feedbackText.text = "<color=green>Tutorial Complete! Great Job!</color>";
            if (baseRewardText != null) baseRewardText.text = "";
            if (bonusText != null) bonusText.text = "";
            if (penaltyText != null) penaltyText.text = "";
        }
        else if (currentContract != null && IsContractPaid(currentContract.ContractID))
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
            if (currentContract != null && currentContract.IsTutorialForCurrentPlayer()) goldEarnedText.text = "";
            else goldEarnedText.text = $"Total Earnings: {calculatedGold} Gold (Pending)";
        }
        
        if (expEarnedText != null) 
        {
            if (currentContract != null && currentContract.IsTutorialForCurrentPlayer()) expEarnedText.text = "";
            else expEarnedText.text = $"+{calculatedExp} EXP (Pending)";
        }

        if (costText != null) 
        {
            costText.text = $"Total Cost: ₱{Mathf.RoundToInt(finalCost):N0}";
            costText.color = (finalCost > maxBudget) ? new Color32(164, 62, 45, 255) : CompletionReceiptLayout.Ink;
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
            stressText.text = $"Peak Bridge Stress: {Mathf.RoundToInt(peakStress)}%";
            
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
        foreach (var label in new[] { feedbackText, baseRewardText, bonusText, penaltyText, goldEarnedText, expEarnedText })
            if (label != null) label.text = label.text.Replace("<color=green>", "<color=#4C6E2F>")
                .Replace("<color=yellow>", "<color=#9B631B>").Replace("<color=red>", "<color=#A43E2D>");
    }

    private void BuildReceiptLayout()
    {
        if (levelCompletePanel == null) return;
        TMP_FontAsset font = feedbackText != null ? feedbackText.font : TMP_Settings.defaultFontAsset;
        if (receiptSourceFont != null)
        {
            ReceiptFont = TMP_FontAsset.CreateFontAsset(receiptSourceFont);
            ReceiptFont.name = "Fake Receipt Runtime SDF";
            ReceiptFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (font != null) ReceiptFont.fallbackFontAssetTable.Add(font);
        }
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
        CompletionReceiptLayout.Label(paper,"Title","BRIDGE COMPLETE",.025f,.855f,.59f,.975f,60,font,TextAlignmentOptions.Center);
        CompletionReceiptLayout.Label(paper,"Subtitle","Bridge held successfully",.025f,.807f,.59f,.86f,28,font,TextAlignmentOptions.Center);
        var photoSlot = CompletionReceiptLayout.Box(paper,"Bridge Photo Slot",.025f,.32f,.59f,.805f);
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
        var stress = CompletionReceiptLayout.Panel(paper,"Peak Stress Card",.025f,.225f,.59f,.303f,new Color32(229,232,204,255));
        stressText = CompletionReceiptLayout.Label(stress,"Peak Stress","Peak Bridge Stress: 0%",.035f,.08f,.965f,.92f,36,font,TextAlignmentOptions.Center);
        var rewards = CompletionReceiptLayout.Box(paper,"Reward Summary",.03f,.132f,.59f,.212f);
        baseRewardText = CompletionReceiptLayout.Label(rewards,"Base Reward","",0,.52f,.32f,1,21,font);
        bonusText = CompletionReceiptLayout.Label(rewards,"Bonus","",.33f,.52f,.66f,1,21,font);
        penaltyText = CompletionReceiptLayout.Label(rewards,"Penalty","",.67f,.52f,1,1,21,font);
        goldEarnedText = CompletionReceiptLayout.Label(rewards,"Gold Earnings","",0,0,.66f,.5f,23,font);
        expEarnedText = CompletionReceiptLayout.Label(rewards,"EXP Earnings","",.67f,0,1,.5f,23,font);
        var receipt = CompletionReceiptLayout.Box(paper,"Material Receipt",.62f,.12f,.98f,.975f);
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
        CompletionReceiptLayout.Divider(receipt,"Total Divider",.075f,.35f,.925f);
        costText = CompletionReceiptLayout.Label(receipt,"Total Cost","",.08f,.27f,.92f,.335f,29,font,TextAlignmentOptions.MidlineRight);
        budgetText = CompletionReceiptLayout.Label(receipt,"Budget","",.08f,.207f,.92f,.265f,25,font,TextAlignmentOptions.MidlineRight);
        receiptBalanceText = CompletionReceiptLayout.Label(receipt,"Remaining","",.08f,.145f,.92f,.203f,25,font,TextAlignmentOptions.MidlineRight);
        receiptStampText = CompletionReceiptLayout.Label(receipt,"Budget Stamp","WITHIN BUDGET",.10f,.067f,.90f,.125f,25,font,TextAlignmentOptions.Center);
        if (ReceiptFont != null)
            foreach (var label in receipt.GetComponentsInChildren<TextMeshProUGUI>(true)) label.font = ReceiptFont;
        costPercentageText = null; // No unexplained percentage or stress progress bar.
        feedbackText = CompletionReceiptLayout.Label(paper,"Feedback","",.025f,.028f,.47f,.11f,26,font);
        CompletionReceiptLayout.Button(paper,"Retry",.49f,.025f,.69f,.11f,new Color32(239,214,170,255),font,RetrySimulation);
        CompletionReceiptLayout.Button(paper,"Save & Continue",.71f,.025f,.975f,.11f,new Color32(228,157,44,255),font,SaveAndBakeBridge);
        safe.gameObject.AddComponent<CompletionEntranceMotion>().Configure(frame,receipt,photoFrame);
    }

    public void RetrySimulation()
    {
        ResetCompletionState();

        if (cachedPhysicsManager != null) cachedPhysicsManager.StopPhysicsAndReset();
        
        BarCreator creator = FindObjectOfType<BarCreator>();
        if (creator != null) creator.isSimulating = false;

        ClosePanel();
    }

    public void SaveAndBakeBridge()
    {
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
            lastPeakStress);

        if (!bridgeSaved)
        {
            Debug.LogError(
                $"[LevelCompleteManager] '{completedContract.name}' remains unfinished because bridge geometry could not be persisted. " +
                "The baked objects remain in the scene so saving can be retried.", this);
            return;
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
        foreach (var npc in npcs)
        {
            if (npc.contractToGive == completedContract)
            {
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

        // --- THE FIX: Make sure the state is fully reset before moving on! ---
        ResetCompletionState();

        // --- THE FIX: Tell the game we are exiting Build Mode BEFORE we close the panel! ---
        // This ensures the ClosePanel method realizes we are leaving and restores player controls!
        if (GameManager.Instance != null) GameManager.Instance.ExitBuildMode();
        
        ClosePanel();

        if (completedContract != null)
            BridgeSavedAtLocation?.Invoke(completedContract, completedLocation);
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
