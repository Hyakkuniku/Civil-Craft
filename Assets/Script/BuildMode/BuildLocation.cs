using UnityEngine;
using UnityEngine.UI; 
using System.Collections.Generic;

public class BuildLocation : Interactable 
{
    [Header("UI / Grid")]
    public Image gridImage; 

    [Header("Camera")]
    public Camera locationCamera;  
    public Camera cinematicCamera; 
    public Vector3 cameraPositionOffset = new Vector3(0, 8, -12);
    public Vector3 cameraLookAtOffset   = new Vector3(0, 2, 0);   

    [Header("Behavior")]
    public bool lockPlayerToZone = false;           
    
    [Header("Active Contract")]
    public ContractSO activeContract; 

    [Header("Tutorial Settings")]
    public bool advancesTutorial = false; 

    [Header("Pre-placed Anchors")]
    public List<Point> startingAnchors = new List<Point>();
    public List<Point> endingAnchors = new List<Point>(); 

    [HideInInspector] public List<Bar> bakedBars = new List<Bar>();
    [HideInInspector] public List<Point> bakedPoints = new List<Point>();

    private Transform originalPlayerParent;

    private void Awake()
    {
        if (locationCamera != null) locationCamera.enabled = false;
        if (cinematicCamera != null) cinematicCamera.enabled = false; 
        if (gridImage != null) gridImage.enabled = false; 
    }

    private void Start()
    {
        LoadSavedBridge();

        if (bakedBars.Count == 0) 
        {
            SetBridgeScriptsActive(false);
        }
    }

    private void Update()
    {
        if (activeContract == null) promptMessage = "Requires Contract! Talk to the client.";
        else if (bakedBars.Count > 0) promptMessage = "Redo Bridge (Deletes Old)"; 
        else promptMessage = "Enter Build Mode";
    }

    protected override void Intract()
    {
        TryEnterBuildMode();
    }

    public void TryEnterBuildMode()
    {
        if (activeContract == null) { Debug.LogWarning("<color=red>Access Denied!</color> You cannot build here without a valid contract."); return; }
        
        if (bakedBars.Count > 0) 
        { 
            if (GameManager.Instance != null) GameManager.Instance.ShowRedoConfirmPanel(this); 
            return; 
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null) ActivateBuildMode(player.transform);
    }

    public void DeleteBakedBridge()
    {
        foreach (Bar b in bakedBars) { if (b != null) Destroy(b.gameObject); }
        
        foreach (Point p in bakedPoints) 
        {
            if (p != null) 
            {
                if (startingAnchors.Contains(p) || endingAnchors.Contains(p))
                {
                    p.ConnectedBars.Clear(); 
                    p.enabled = false; 
                }
                else { Destroy(p.gameObject); }
            }
        }
        
        bakedPoints.Clear();
        bakedBars.Clear();

        if (PlayerDataManager.Instance != null && activeContract != null)
        {
            PlayerDataManager.Instance.DeleteSavedBridge(activeContract.name);
        }

        NPCContractGiver[] npcs = FindObjectsOfType<NPCContractGiver>();
        foreach (var npc in npcs) { if (npc.contractToGive == activeContract) npc.isContractCompleted = false; }
    }

    public void ActivateBuildMode(Transform player)
    {
        if (GameManager.Instance == null || activeContract == null) return; 

        BarCreator barCreator = FindObjectOfType<BarCreator>(true);
        if (gridImage != null) gridImage.enabled = (barCreator != null && barCreator.isGridSnappingEnabled);

        SetBridgeScriptsActive(true);

        GameManager.Instance.EnterBuildMode(this, player);

        if (lockPlayerToZone && player != null)
        {
            originalPlayerParent = player.parent;
            player.SetParent(transform);
        }

        if (advancesTutorial && TutorialManager.Instance != null) TutorialManager.Instance.ShowNextStep();
    }

    public void DeactivateBuildMode(Transform player)
    {
        if (gridImage != null) gridImage.enabled = false;
        if (bakedBars.Count == 0) SetBridgeScriptsActive(false);

        if (lockPlayerToZone && originalPlayerParent != null && player != null)
        {
            player.SetParent(originalPlayerParent);
            originalPlayerParent = null;
        }
    }

    public void SetGridVisualActive(bool isActive) { if (gridImage != null) gridImage.enabled = isActive; }

    private void SetBridgeScriptsActive(bool isActive)
    {
        HashSet<Point> bridgePoints = new HashSet<Point>();
        Queue<Point> queue = new Queue<Point>();

        foreach (Point p in startingAnchors) { if (p != null) { p.enabled = true; queue.Enqueue(p); bridgePoints.Add(p); } }
        foreach (Point p in endingAnchors) { if (p != null && !bridgePoints.Contains(p)) { p.enabled = true; queue.Enqueue(p); bridgePoints.Add(p); } }

        while (queue.Count > 0)
        {
            Point current = queue.Dequeue();
            foreach (Bar b in current.ConnectedBars)
            {
                if (b != null)
                {
                    b.enabled = isActive;
                    Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                    if (neighbor != null && !bridgePoints.Contains(neighbor)) { bridgePoints.Add(neighbor); queue.Enqueue(neighbor); }
                }
            }
        }

        foreach (Point p in bridgePoints) p.enabled = isActive;
    }

    public Vector3 GetDesiredCameraPosition() { return transform.position + cameraPositionOffset; }

    public Quaternion GetDesiredCameraRotation()
    {
        Vector3 lookAt = transform.position + cameraLookAtOffset;
        return Quaternion.LookRotation(lookAt - GetDesiredCameraPosition());
    }

    // --- THE FIX: Changed to public so the NPC can force-load it after checking the save ---
    public void LoadSavedBridge()
    {
        if (activeContract == null || PlayerDataManager.Instance == null) return;
        if (bakedBars.Count > 0) return; // Prevent loading a bridge twice
        
        var savedBridge = PlayerDataManager.Instance.GetSavedBridge(activeContract.name);
        if (savedBridge == null || savedBridge.points.Count == 0) return;

        BarCreator creator = FindObjectOfType<BarCreator>(true);
        if (creator == null || creator.pointToInstantiate == null || creator.barToInstantiate == null)
        {
            Debug.LogError("<b>[Bridge Loader]</b> Cannot load bridge: BarCreator or prefabs are missing from the scene!");
            return;
        }

        BridgeMaterialSO[] allMats = Resources.LoadAll<BridgeMaterialSO>("");
        Dictionary<int, Point> indexToPoint = new Dictionary<int, Point>();

        foreach (var ptData in savedBridge.points)
        {
            Vector3 pos = ptData.position.ToVector3();
            Point matchedAnchor = null;

            foreach (var anchor in startingAnchors) { if (anchor != null && Vector3.Distance(anchor.transform.position, pos) < 0.1f) matchedAnchor = anchor; }
            foreach (var anchor in endingAnchors) { if (anchor != null && Vector3.Distance(anchor.transform.position, pos) < 0.1f) matchedAnchor = anchor; }

            if (matchedAnchor != null)
            {
                indexToPoint[ptData.index] = matchedAnchor;
                if (!bakedPoints.Contains(matchedAnchor)) bakedPoints.Add(matchedAnchor);
                matchedAnchor.enabled = false; 
            }
            else
            {
                GameObject newPtObj = Instantiate(creator.pointToInstantiate, pos, Quaternion.identity, creator.pointParent);
                Point newPt = newPtObj.GetComponent<Point>();
                newPt.isAnchor = ptData.isAnchor;
                newPt.originalIsAnchor = ptData.originalIsAnchor;
                newPt.enabled = false; 
                indexToPoint[ptData.index] = newPt;
                bakedPoints.Add(newPt);
            }
        }

        foreach (var barData in savedBridge.bars)
        {
            if (!indexToPoint.ContainsKey(barData.startPointIndex) || !indexToPoint.ContainsKey(barData.endPointIndex)) continue;

            BridgeMaterialSO mat = System.Array.Find(allMats, m => m.name == barData.materialName);
            if (mat == null) continue;

            GameObject newBarObj = Instantiate(creator.barToInstantiate, creator.barParent);
            Bar newBar = newBarObj.GetComponent<Bar>();
            
            newBar.Initialize(mat);
            newBar.startPoint = indexToPoint[barData.startPointIndex];
            newBar.endPoint = indexToPoint[barData.endPointIndex];

            newBar.StartPosition = newBar.startPoint.transform.position;
            newBar.UpdateCreatingBar(newBar.endPoint.transform.position);

            if (!newBar.startPoint.ConnectedBars.Contains(newBar)) newBar.startPoint.ConnectedBars.Add(newBar);
            if (!newBar.endPoint.ConnectedBars.Contains(newBar)) newBar.endPoint.ConnectedBars.Add(newBar);

            newBar.gameObject.layer = LayerMask.NameToLayer("Bridge"); 
            
            if (!mat.isRope) 
            {
                if (mat.isPier)
                {
                    Transform cap = newBar.transform.Find("PierCap");
                    if (cap != null)
                    {
                        Renderer capRend = cap.GetComponentInChildren<Renderer>();
                        if (capRend != null && capRend.gameObject.GetComponent<Collider>() == null)
                        {
                            capRend.gameObject.AddComponent<BoxCollider>();
                        }
                    }

                    foreach (Transform child in newBar.transform)
                    {
                        if (child.name.StartsWith("VisualSegment"))
                        {
                            Renderer segRend = child.GetComponentInChildren<Renderer>();
                            if (segRend != null && segRend.gameObject.GetComponent<Collider>() == null)
                            {
                                segRend.gameObject.AddComponent<BoxCollider>();
                            }
                        }
                    }
                }
                else
                {
                    int spawnCount = mat.isDualBeam ? 2 : 1;
                    float length = Vector3.Distance(newBar.startPoint.transform.position, newBar.endPoint.transform.position);

                    for (int i = 0; i < spawnCount; i++)
                    {
                        BoxCollider col = newBar.gameObject.AddComponent<BoxCollider>();
                        
                        float thickness = mat.isRoad ? 0.05f : 0.2f; 
                        float depth = newBar.visualSize.z; 

                        if (!mat.isDualBeam && depth < 2.0f) depth = 2.0f; 
                        else if (mat.isDualBeam && depth < 0.2f) depth = 0.2f;

                        float zOffsetValue = mat.isDualBeam ? ((i == 0) ? mat.zOffset : -mat.zOffset) : 0f;
                        float physicsLength = length + 0.05f; 
                        
                        col.size = new Vector3(physicsLength, thickness, depth);
                        col.center = new Vector3(0, 0, zOffsetValue);
                    }
                }
            }

            newBar.enabled = false; 
            bakedBars.Add(newBar);
        }

        NPCContractGiver[] npcs = FindObjectsOfType<NPCContractGiver>();
        foreach (var npc in npcs)
        {
            if (npc.contractToGive == activeContract) npc.isContractCompleted = true;
        }

        SetBridgeScriptsActive(false);
    }
}