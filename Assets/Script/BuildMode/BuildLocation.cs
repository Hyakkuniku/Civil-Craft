using UnityEngine;
using UnityEngine.UI; 
using System.Collections.Generic;

public class BuildLocation : Interactable 
{
    [Header("UI / Grid")]
    public Image gridImage; 

    [Header("Camera")]
    public Camera locationCamera;  
    [Tooltip("A fixed camera perfectly framed for the final completion screenshot. (Optional)")]
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
    [Tooltip("Drag the starting cliff anchor points (e.g., Left Side) here.")]
    public List<Point> startingAnchors = new List<Point>();
    
    // --- NEW: Ending Anchors ---
    [Tooltip("Drag the ending cliff anchor points (e.g., Right Side) here.")]
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
        if (bakedBars.Count == 0) 
        {
            SetBridgeScriptsActive(false);
        }
    }

    private void Update()
    {
        if (activeContract == null)
        {
            promptMessage = "Requires Contract! Talk to the client.";
        }
        else if (bakedBars.Count > 0)
        {
            promptMessage = "Redo Bridge (Deletes Old)"; 
        }
        else
        {
            promptMessage = "Enter Build Mode";
        }
    }

    protected override void Intract()
    {
        TryEnterBuildMode();
    }

    public void TryEnterBuildMode()
    {
        if (activeContract == null)
        {
            Debug.LogWarning("<color=red>Access Denied!</color> You cannot build here without a valid contract.");
            return;
        }

        if (bakedBars.Count > 0)
        {
            if (GameManager.Instance != null) GameManager.Instance.ShowRedoConfirmPanel(this);
            return;
        }

        PlayerMotor player = FindObjectOfType<PlayerMotor>();
        if (player != null)
        {
            ActivateBuildMode(player.transform);
        }
    }

    public void DeleteBakedBridge()
    {
        foreach (Bar b in bakedBars) 
        {
            if (b != null) Destroy(b.gameObject);
        }
        
        foreach (Point p in bakedPoints) 
        {
            if (p != null) 
            {
                // Cleanly wipe memory from BOTH anchor sides
                if (startingAnchors.Contains(p) || endingAnchors.Contains(p))
                {
                    p.ConnectedBars.Clear(); 
                    p.enabled = false; 
                }
                else
                {
                    Destroy(p.gameObject);
                }
            }
        }
        
        bakedPoints.Clear();
        bakedBars.Clear();

        NPCContractGiver[] npcs = FindObjectsOfType<NPCContractGiver>();
        foreach (var npc in npcs)
        {
            if (npc.contractToGive == activeContract) npc.isContractCompleted = false;
        }
    }

    public void ActivateBuildMode(Transform player)
    {
        if (GameManager.Instance == null || activeContract == null) return; 

        BarCreator barCreator = FindObjectOfType<BarCreator>();
        if (gridImage != null) 
        {
            gridImage.enabled = (barCreator != null && barCreator.isGridSnappingEnabled);
        }

        SetBridgeScriptsActive(true);

        GameManager.Instance.EnterBuildMode(this, player);

        if (lockPlayerToZone && player != null)
        {
            originalPlayerParent = player.parent;
            player.SetParent(transform);
        }

        if (advancesTutorial && TutorialManager.Instance != null)
        {
            TutorialManager.Instance.ShowNextStep();
        }
    }

    public void DeactivateBuildMode(Transform player)
    {
        if (gridImage != null) gridImage.enabled = false;

        if (bakedBars.Count == 0)
        {
            SetBridgeScriptsActive(false);
        }

        if (lockPlayerToZone && originalPlayerParent != null && player != null)
        {
            player.SetParent(originalPlayerParent);
            originalPlayerParent = null;
        }
    }

    public void SetGridVisualActive(bool isActive)
    {
        if (gridImage != null)
        {
            gridImage.enabled = isActive;
        }
    }

    private void SetBridgeScriptsActive(bool isActive)
    {
        HashSet<Point> bridgePoints = new HashSet<Point>();
        Queue<Point> queue = new Queue<Point>();

        foreach (Point p in startingAnchors)
        {
            if (p != null) { p.enabled = true; queue.Enqueue(p); bridgePoints.Add(p); }
        }
        // Wake up the ending anchors too!
        foreach (Point p in endingAnchors)
        {
            if (p != null && !bridgePoints.Contains(p)) { p.enabled = true; queue.Enqueue(p); bridgePoints.Add(p); }
        }

        while (queue.Count > 0)
        {
            Point current = queue.Dequeue();
            foreach (Bar b in current.ConnectedBars)
            {
                if (b != null)
                {
                    b.enabled = isActive;
                    Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                    if (neighbor != null && !bridgePoints.Contains(neighbor))
                    {
                        bridgePoints.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        foreach (Point p in bridgePoints)
        {
            p.enabled = isActive;
        }
    }

    public Vector3 GetDesiredCameraPosition()
    {
        return transform.position + cameraPositionOffset;
    }

    public Quaternion GetDesiredCameraRotation()
    {
        Vector3 lookAt = transform.position + cameraLookAtOffset;
        return Quaternion.LookRotation(lookAt - GetDesiredCameraPosition());
    }
}