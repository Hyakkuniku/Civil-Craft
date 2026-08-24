using UnityEngine;
using UnityEngine.UI; 
using System.Collections;
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

    // --- NEW: CINEMATIC DIVE TARGET ---
    [Header("Cinematic Transition")]
    [Tooltip("Place an empty GameObject exactly on top of the blueprint paper, facing down into it.")]
    public Transform blueprintDiveTarget;
    [Tooltip("How long it takes to dive into the blueprint (in seconds).")]
    public float diveDuration = 1.0f;

    [Header("Behavior")]
    public bool lockPlayerToZone = false;           
    
    [Header("Active Contract")]
    public ContractSO activeContract; 

    [Header("Navigation")]
    [Tooltip("Drag an Empty GameObject placed at the cliff edge here. The rock trail will lead to this exact spot!")]
    public GameObject navigationTarget;

    [Header("Tutorial Settings")]
    public bool advancesTutorial = false; 
    [Tooltip("If assigned, this tutorial will start the moment the player enters this Build Location.")]
    public TutorialSequence onEnterBuildModeTutorial;

    [Header("Pre-placed Anchors")]
    public List<Point> startingAnchors = new List<Point>();
    public List<Point> endingAnchors = new List<Point>(); 

    [HideInInspector] public List<Bar> bakedBars = new List<Bar>();
    [HideInInspector] public List<Point> bakedPoints = new List<Point>();

    private Transform originalPlayerParent;

    private float timeAttackTimer;
    private bool isTimeAttackActive = false;

    private void Awake()
    {
        if (locationCamera != null) locationCamera.enabled = false;
        if (cinematicCamera != null) cinematicCamera.enabled = false; 
        if (gridImage != null) gridImage.enabled = false; 
    }

    private void Start()
    {
        if (activeContract != null && PlayerPrefs.GetInt("LockedContract_" + activeContract.name, 0) == 1)
        {
            activeContract = null; 
        }

        Bar[] allBarsInScene = FindObjectsOfType<Bar>();
        foreach (Bar b in allBarsInScene) 
        {
            b.AutoRepairEndpoints();
        }

        LoadSavedBridge();

        if (bakedBars.Count == 0) 
        {
            SetBridgeScriptsActive(false);
        }
    }

    private void Update()
    {
        if (IsOverworldTutorialBlockingBuild()) promptMessage = "Finish the current tutorial first.";
        else if (activeContract == null) promptMessage = "Requires Contract! Talk to the client.";
        else if (bakedBars.Count > 0) promptMessage = "Redo Bridge (Deletes Old)"; 
        else promptMessage = "Enter Build Mode";

        if (isTimeAttackActive)
        {
            BridgePhysicsManager phys = FindObjectOfType<BridgePhysicsManager>();
            bool isSimulating = phys != null && phys.isSimulating;
            bool isFailed = LevelFailedManager.Instance != null && LevelFailedManager.Instance.isFailed;
            bool isPaused = Time.timeScale == 0f;

            if (!isSimulating && !isFailed && !isPaused)
            {
                timeAttackTimer -= Time.deltaTime;

                if (BuildUIController.Instance != null)
                {
                    BuildUIController.Instance.ShowTimer(true);
                    BuildUIController.Instance.UpdateTimerText("Time Attack: ", timeAttackTimer);
                }

                if (timeAttackTimer <= 0f)
                {
                    timeAttackTimer = 0f;
                    isTimeAttackActive = false; 
                    
                    if (BuildUIController.Instance != null) BuildUIController.Instance.UpdateTimerText("Time Attack: ", 0f);
                    
                    if (activeContract != null)
                    {
                        PlayerPrefs.SetInt("LockedContract_" + activeContract.name, 1);
                        PlayerPrefs.Save();
                        
                        if (LevelFailedManager.Instance != null) 
                        {
                            LevelFailedManager.Instance.TriggerLevelFailed("Time's Up! This contract is permanently locked.", true);
                        }

                        activeContract = null; 
                    }
                }
            }
        }
    }

    protected override void Intract()
    {
        TryEnterBuildMode();
    }

    public void TryEnterBuildMode()
    {
        if (IsOverworldTutorialBlockingBuild())
        {
            Debug.LogWarning("Build Mode is locked while an overworld tutorial is active.");
            return;
        }

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

        if (DynamicNavMeshUpdater.Instance != null)
            DynamicNavMeshUpdater.Instance.UpdateWalkableNavMesh();
    }

    public void ActivateBuildMode(Transform player)
    {
        if (GameManager.Instance == null || activeContract == null || IsOverworldTutorialBlockingBuild()) return;

        if (!GameManager.Instance.EnterBuildMode(this, player)) return;

        BarCreator barCreator = FindObjectOfType<BarCreator>(true);
        if (gridImage != null) gridImage.enabled = (barCreator != null && barCreator.isGridSnappingEnabled);

        SetBridgeScriptsActive(true);
        StartCoroutine(StartBuildTutorialAfterTransition());

        if (lockPlayerToZone && player != null)
        {
            originalPlayerParent = player.parent;
            player.SetParent(transform);
        }

        if (activeContract.isTimeAttack)
        {
            ResetTimeAttack();
        }
    }

    private bool IsOverworldTutorialBlockingBuild()
    {
        return TutorialManager.Instance != null && TutorialManager.Instance.IsTutorialActive &&
               (GameManager.Instance == null || GameManager.Instance.CurrentState == GameManager.GameState.Normal);
    }

    private IEnumerator StartBuildTutorialAfterTransition()
    {
        yield return new WaitUntil(() => GameManager.Instance == null ||
            (GameManager.Instance.ActiveBuildLocation == this && !GameManager.Instance.IsTransitioning));

        if (GameManager.Instance == null || GameManager.Instance.ActiveBuildLocation != this) yield break;
        if (activeContract != null && activeContract.WasTutorialCompletedByCurrentPlayer()) yield break;
        if (onEnterBuildModeTutorial == null || !onEnterBuildModeTutorial.CanStartTutorial()) yield break;

        if (BuildTutorialDirector.Instance != null)
            BuildTutorialDirector.Instance.LockAllUI();

        onEnterBuildModeTutorial.TryStartTutorial();
    }

    /// <summary>
    /// Forced recovery path used when physics fails an unfinished tutorial contract.
    /// It resets saved lesson gates for this sequence chain and replays from step zero.
    /// </summary>
    public bool RestartBuildTutorialAfterFailure()
    {
        if (activeContract == null || !activeContract.IsTutorialForCurrentPlayer() ||
            onEnterBuildModeTutorial == null || TutorialManager.Instance == null)
        {
            return false;
        }

        // A final Play-button step may have already marked one or more chained
        // sequences complete before the simulation failure was known.
        if (PlayerDataManager.Instance != null)
        {
            HashSet<TutorialSequence> sequencesToReset = new HashSet<TutorialSequence>();
            TutorialSequence sequence = onEnterBuildModeTutorial;

            while (sequence != null && sequencesToReset.Add(sequence))
            {
                sequence = sequence.autoStartNextSequence ? sequence.nextSequence : null;
            }

            // Also catch separately-triggered build sequences such as Build2.
            // Overworld/navigation tutorials are left untouched.
            foreach (TutorialSequence sceneSequence in Resources.FindObjectsOfTypeAll<TutorialSequence>())
            {
                if (sceneSequence != null && sceneSequence.gameObject.scene == gameObject.scene &&
                    UsesBuildTutorialDirector(sceneSequence))
                {
                    sequencesToReset.Add(sceneSequence);
                }
            }

            foreach (TutorialSequence buildSequence in sequencesToReset)
                PlayerDataManager.Instance.ResetLessonProgress(buildSequence.lessonName, false);

            PlayerDataManager.Instance.SaveGame();
        }

        if (BuildTutorialDirector.Instance != null) BuildTutorialDirector.Instance.EndTutorial();

        BarCreator barCreator = BuildUIController.Instance != null
            ? BuildUIController.Instance.barCreator
            : FindObjectOfType<BarCreator>();
        if (barCreator != null) barCreator.ClearPlayerPlacedBridge(this);

        if (CommandManager.Instance != null) CommandManager.Instance.ClearHistory();

        if (BuildTutorialDirector.Instance != null)
            BuildTutorialDirector.Instance.PrepareGhostsForTutorialRestart();

        TutorialManager.Instance.RestartTutorial(onEnterBuildModeTutorial);
        return true;
    }

    private static bool UsesBuildTutorialDirector(TutorialSequence sequence)
    {
        if (sequence == null || sequence.tutorialSteps == null) return false;

        foreach (TutorialStep step in sequence.tutorialSteps)
        {
            if (step == null || step.OnStepStart == null) continue;

            for (int i = 0; i < step.OnStepStart.GetPersistentEventCount(); i++)
            {
                if (step.OnStepStart.GetPersistentTarget(i) is BuildTutorialDirector)
                    return true;
            }
        }

        return false;
    }

    public void DeactivateBuildMode(Transform player)
    {
        if (BuildTutorialDirector.Instance != null && BuildTutorialDirector.Instance.isTutorialRunning)
        {
            Debug.LogWarning("Tutorial is active! Exit blocked.");
            return;
        }

        if (gridImage != null) gridImage.enabled = false;
        if (bakedBars.Count == 0) SetBridgeScriptsActive(false);

        if (lockPlayerToZone && originalPlayerParent != null && player != null)
        {
            player.SetParent(originalPlayerParent);
            originalPlayerParent = null;
        }

        isTimeAttackActive = false;
        if (BuildUIController.Instance != null) BuildUIController.Instance.ShowTimer(false);

        if (BuildTutorialDirector.Instance != null)
        {
            BuildTutorialDirector.Instance.EndTutorial();
        }
    }

    public void ResetTimeAttack()
    {
        if (activeContract != null && activeContract.isTimeAttack)
        {
            timeAttackTimer = activeContract.timeAttackDuration;
            isTimeAttackActive = true;
            
            if (BuildUIController.Instance != null)
            {
                BuildUIController.Instance.ShowTimer(true);
                BuildUIController.Instance.UpdateTimerText("Time Attack: ", timeAttackTimer);
            }
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

    public void LoadSavedBridge()
    {
        if (activeContract == null || PlayerDataManager.Instance == null) return;
        if (bakedBars.Count > 0) return; 
        
        var savedBridge = PlayerDataManager.Instance.GetSavedBridge(activeContract.name);
        if (savedBridge == null || savedBridge.points.Count == 0) return;

        BarCreator creator = FindObjectOfType<BarCreator>(true);
        if (creator == null || creator.pointToInstantiate == null || creator.barToInstantiate == null) return;

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
                        if (capRend != null && capRend.gameObject.GetComponent<Collider>() == null) capRend.gameObject.AddComponent<BoxCollider>();
                    }

                    foreach (Transform child in newBar.transform)
                    {
                        if (child.name.StartsWith("VisualSegment"))
                        {
                            Renderer segRend = child.GetComponentInChildren<Renderer>();
                            if (segRend != null && segRend.gameObject.GetComponent<Collider>() == null) segRend.gameObject.AddComponent<BoxCollider>();
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

        if (DynamicNavMeshUpdater.Instance != null)
            DynamicNavMeshUpdater.Instance.UpdateWalkableNavMeshForLocation(this);
    }
}
