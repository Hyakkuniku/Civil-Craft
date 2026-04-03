using System; 
using System.Collections.Generic;
using UnityEngine;

public class BridgePhysicsManager : MonoBehaviour
{
    public event Action OnSimulationStarted;
    public event Action OnSimulationStopped;

    [Header("Physics Settings")]
    public float barColliderThickness = 0.2f;
    public int physicsSolverIterations = 40; 

    [Header("Stress Visualizer Colors")]
    public bool enableVisualizer = true;
    public Color warningColor = Color.yellow;
    public Color criticalColor = Color.red;
    public Color brokenColor = Color.black;

    [HideInInspector] public bool isSimulating = false;
    [HideInInspector] public List<BarStressHandler> activeStressHandlers = new List<BarStressHandler>();

    [HideInInspector] public float peakStressThisRun = 0f;

    private HashSet<Point> simPoints = new HashSet<Point>();
    private HashSet<Bar> simBars = new HashSet<Bar>();

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HandleExitBuildMode);
        }
    }

    private void FixedUpdate()
    {
        if (isSimulating)
        {
            float currentMax = GetMaxBridgeStress();
            if (currentMax > peakStressThisRun)
            {
                peakStressThisRun = currentMax;
            }
        }
    }

    private void GatherActiveBridgeData(out HashSet<Point> outPoints, out HashSet<Bar> outBars)
    {
        outPoints = new HashSet<Point>();
        outBars = new HashSet<Bar>();

        foreach (Point p in Point.AllPoints)
        {
            if (p != null && p.gameObject.activeSelf && p.enabled) outPoints.Add(p);
        }

        foreach (Point p in outPoints)
        {
            foreach (Bar b in p.ConnectedBars)
            {
                if (b != null && b.gameObject.activeSelf && b.enabled) outBars.Add(b);
            }
        }

        foreach (Bar b in outBars)
        {
            if (b.startPoint != null && b.startPoint.enabled) outPoints.Add(b.startPoint);
            if (b.endPoint != null && b.endPoint.enabled) outPoints.Add(b.endPoint);
        }
    }

    private void HandleEnterBuildMode()
    {
        if (isSimulating) StopPhysicsAndReset();
        else SetNodesVisible(true);
    }

    private void HandleExitBuildMode()
    {
        SetNodesVisible(false);
    }

    private void SetNodesVisible(bool isVisible)
    {
        HashSet<Point> points;
        HashSet<Bar> bars;
        GatherActiveBridgeData(out points, out bars);

        foreach (Point p in Point.AllPoints)
        {
            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = isVisible && p.gameObject.activeSelf && points.Contains(p);
        }
    }

    public void ActivatePhysics()
    {
        if (isSimulating) return;
        
        isSimulating = true;
        OnSimulationStarted?.Invoke(); 

        activeStressHandlers.Clear(); 
        peakStressThisRun = 0f;

        GatherActiveBridgeData(out simPoints, out simBars);

        foreach (Point p in simPoints)
        {
            p.preSimPos = p.transform.position;
            p.preSimParent = p.transform.parent;

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = false;
        }

        foreach (Bar b in simBars)
        {
            b.preSimPos = b.transform.position;
            b.preSimRot = b.transform.rotation;
        }

        Physics.defaultSolverIterations = physicsSolverIterations;
        Physics.defaultSolverVelocityIterations = 20;

        SetupBarsPhysics(simBars);
        SetupDirectConnections(simBars, simPoints);
        
        ResolveAdjacentCollisions(simBars); 
    }

    public void StopPhysicsAndReset()
    {
        if (!isSimulating) return;
        
        isSimulating = false;
        OnSimulationStopped?.Invoke(); 

        activeStressHandlers.Clear();

        foreach (Bar bar in simBars)
        {
            if (bar == null) continue;
            foreach (Joint j in bar.GetComponentsInChildren<Joint>()) { j.connectedBody = null; Destroy(j); }
            foreach (Rigidbody rb in bar.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        }

        foreach (Point p in simPoints)
        {
            if (p == null) continue;
            foreach (Joint j in p.GetComponentsInChildren<Joint>()) { j.connectedBody = null; Destroy(j); }
            foreach (Rigidbody rb in p.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        }

        bool isCurrentlyBuilding = GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Building;

        foreach (Point p in simPoints)
        {
            if (p == null) continue;
            
            Collider[] cols = p.GetComponentsInChildren<Collider>();
            foreach(var col in cols) col.enabled = true; 

            p.transform.SetParent(p.preSimParent);
            p.transform.position = p.preSimPos;
            p.transform.rotation = Quaternion.identity; 

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = isCurrentlyBuilding && p.gameObject.activeSelf;
        }

        foreach (Bar bar in simBars)
        {
            if (bar == null) continue;
            
            if (bar.materialData != null && bar.materialData.isPier)
            {
                Transform cap = bar.transform.Find("PierCap");
                if (cap != null)
                {
                    Renderer capRend = cap.GetComponentInChildren<Renderer>();
                    if (capRend != null)
                    {
                        BoxCollider bc = capRend.GetComponent<BoxCollider>();
                        if (bc != null) Destroy(bc);
                    }
                }

                foreach (Transform child in bar.transform)
                {
                    if (child.name.StartsWith("VisualSegment"))
                    {
                        Renderer segRend = child.GetComponentInChildren<Renderer>();
                        if (segRend != null)
                        {
                            BoxCollider bc = segRend.GetComponent<BoxCollider>();
                            if (bc != null) Destroy(bc);
                        }
                    }
                }
            }
            else
            {
                BoxCollider[] parentCols = bar.GetComponents<BoxCollider>();
                foreach (BoxCollider c in parentCols) Destroy(c);
            }

            BarStressHandler stress = bar.GetComponent<BarStressHandler>();
            if (stress != null) Destroy(stress);

            bar.transform.position = bar.preSimPos;
            bar.transform.rotation = bar.preSimRot;
            
            if (bar.gameObject.activeSelf && bar.startPoint != null && bar.endPoint != null)
            {
                bar.StartPosition = bar.startPoint.transform.position;
                bar.UpdateCreatingBar(bar.endPoint.transform.position);
            }
        }

        simPoints.Clear();
        simBars.Clear();
    }

    // --- THE FIX: Smart Baking! ---
    // Now accepts the activeContract, spider-webs the ravine, and bakes everything even if it's asleep!
    public void BakeBridge(ContractSO contract = null)
    {
        HashSet<Point> bakePoints = new HashSet<Point>();
        HashSet<Bar> bakeBars = new HashSet<Bar>();

        BuildLocation targetLoc = null;

        if (contract != null)
        {
            BuildLocation[] allLocs = Resources.FindObjectsOfTypeAll<BuildLocation>();
            foreach (var loc in allLocs)
            {
                if (loc.gameObject.scene.name != null && loc.activeContract == contract)
                {
                    targetLoc = loc;
                    break;
                }
            }
        }
        else if (GameManager.Instance != null && GameManager.Instance.ActiveBuildLocation != null)
        {
            targetLoc = GameManager.Instance.ActiveBuildLocation;
        }

        if (targetLoc == null) 
        {
            Debug.LogWarning("<b>[Baker]</b> Cannot bake bridge: Target location not found!");
            return;
        }

        // 1. Gather all awake points/bars first (if called from inside Build Mode)
        foreach (Point p in Point.AllPoints)
        {
            if (p != null && p.gameObject.activeSelf && p.enabled)
            {
                bakePoints.Add(p);
                foreach (Bar b in p.ConnectedBars)
                {
                    if (b != null && b.gameObject.activeSelf && b.enabled) bakeBars.Add(b);
                }
            }
        }

        // 2. Spider-web to gather sleeping points/bars (if called from outside Build Mode)
        foreach (Point anchor in targetLoc.startingAnchors)
        {
            if (anchor != null)
            {
                bakePoints.Add(anchor);
                Queue<Point> queue = new Queue<Point>();
                queue.Enqueue(anchor);

                while (queue.Count > 0)
                {
                    Point current = queue.Dequeue();
                    foreach (Bar b in current.ConnectedBars)
                    {
                        if (b != null && b.gameObject.activeSelf && !bakeBars.Contains(b))
                        {
                            bakeBars.Add(b);
                            Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                            if (neighbor != null && !bakePoints.Contains(neighbor))
                            {
                                bakePoints.Add(neighbor);
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }
        }

        // 3. Keep previously baked bars in case we are modifying an existing bridge
        foreach(Bar b in targetLoc.bakedBars) { if (b != null) bakeBars.Add(b); }
        foreach(Point p in targetLoc.bakedPoints) { if (p != null) bakePoints.Add(p); }

        targetLoc.bakedPoints.Clear();
        targetLoc.bakedBars.Clear();

        // 4. Freeze Physics
        foreach (Point p in bakePoints)
        {
            if (p == null) continue;
            foreach (Rigidbody rb in p.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true; rb.useGravity = false; rb.velocity = Vector3.zero;
            }
        }
        foreach (Bar b in bakeBars)
        {
            if (b == null) continue;
            foreach (Rigidbody rb in b.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true; rb.useGravity = false; rb.velocity = Vector3.zero;
            }
        }

        // 5. Destroy Physics and Move to Baked Lists
        foreach (Point p in bakePoints)
        {
            if (p == null) continue;
            foreach (var j in p.GetComponentsInChildren<Joint>()) Destroy(j);
            foreach (var rb in p.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            p.enabled = false; 
            targetLoc.bakedPoints.Add(p); 
        }

        foreach (Bar b in bakeBars)
        {
            if (b == null) continue;
            foreach (var j in b.GetComponentsInChildren<Joint>()) Destroy(j);
            foreach (var rb in b.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            if (b.GetComponent<BarStressHandler>() != null) Destroy(b.GetComponent<BarStressHandler>());
            b.enabled = false; 
            targetLoc.bakedBars.Add(b); 
        }

        activeStressHandlers.Clear();
        isSimulating = false;
        simPoints.Clear();
        simBars.Clear();

        OnSimulationStopped?.Invoke(); 
    }

    public float GetMaxBridgeStress()
    {
        float maxStress = 0f;
        foreach (var handler in activeStressHandlers)
        {
            if (handler == null) continue;
            if (handler.isBroken) return 1f; 

            if (handler.currentStressPercent > maxStress)
            {
                maxStress = handler.currentStressPercent;
            }
        }
        
        if (maxStress > peakStressThisRun) 
        {
            peakStressThisRun = maxStress;
        }

        return Mathf.Clamp01(maxStress); 
    }

    private void SetupBarsPhysics(HashSet<Bar> activeBars)
    {
        foreach (Bar bar in activeBars)
        {
            if (bar.GetComponent<Rigidbody>() == null) ApplyPhysicsToBar(bar);
        }
    }

    private void ApplyPhysicsToBar(Bar bar)
    {
        List<Point> endpoints = new List<Point>();
        
        foreach (Point p in simPoints)
        {
            if (p.gameObject.activeSelf && p.ConnectedBars.Contains(bar)) endpoints.Add(p);
        }

        if (endpoints.Count != 2) return; 

        Point p1 = endpoints[0];
        Point p2 = endpoints[1];

        if (!bar.materialData.isRope)
        {
            float length = Vector3.Distance(p1.transform.position, p2.transform.position);
            
            Rigidbody barRb = bar.GetComponent<Rigidbody>();
            if (barRb == null) barRb = bar.gameObject.AddComponent<Rigidbody>();
            
            barRb.isKinematic = bar.materialData.isPier; 
            barRb.mass = length * bar.materialData.massPerMeter; 
            
            barRb.drag = 0.5f;
            barRb.angularDrag = 0.5f;
            barRb.interpolation = RigidbodyInterpolation.Interpolate;
            barRb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            BoxCollider[] oldCols = bar.GetComponents<BoxCollider>();
            foreach(var c in oldCols) Destroy(c);

            if (bar.materialData.isPier)
            {
                Transform cap = bar.transform.Find("PierCap");
                if (cap != null)
                {
                    Renderer capRend = cap.GetComponentInChildren<Renderer>();
                    if (capRend != null && capRend.gameObject.GetComponent<Collider>() == null)
                    {
                        capRend.gameObject.AddComponent<BoxCollider>();
                    }
                }

                foreach (Transform child in bar.transform)
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
                int spawnCount = bar.materialData.isDualBeam ? 2 : 1;
                for (int i = 0; i < spawnCount; i++)
                {
                    BoxCollider col = bar.gameObject.AddComponent<BoxCollider>();
                    
                    float thickness = bar.materialData.isRoad ? 0.05f : barColliderThickness;
                    float depth = bar.visualSize.z; 

                    if (!bar.materialData.isDualBeam && depth < 2.0f) depth = 2.0f; 
                    else if (bar.materialData.isDualBeam && depth < 0.2f) depth = 0.2f;

                    float zOffsetValue = bar.materialData.isDualBeam ? ((i == 0) ? bar.materialData.zOffset : -bar.materialData.zOffset) : 0f;
                    float physicsLength = length + 0.05f; 
                    
                    col.size = new Vector3(physicsLength, thickness, depth);
                    col.center = new Vector3(0, 0, zOffsetValue);
                }
            }
        }

        bar.gameObject.layer = LayerMask.NameToLayer("Bridge"); 

        BarStressHandler stressHandler = bar.GetComponent<BarStressHandler>();
        if (stressHandler == null) stressHandler = bar.gameObject.AddComponent<BarStressHandler>();
        
        stressHandler.Setup(bar.materialData, p1, p2);
        activeStressHandlers.Add(stressHandler);
    }

    private void SetupDirectConnections(HashSet<Bar> activeBars, HashSet<Point> activePoints)
    {
        foreach (Point p in activePoints)
        {
            if (!p.gameObject.activeSelf || p.ConnectedBars.Count == 0) continue; 

            Collider[] oldCols = p.GetComponents<Collider>();
            foreach(var col in oldCols) col.enabled = false; 

            Rigidbody nodeRb = p.GetComponent<Rigidbody>();
            if (nodeRb == null) nodeRb = p.gameObject.AddComponent<Rigidbody>();
            
            if (p.isAnchor)
            {
                nodeRb.isKinematic = true;
            }
            else
            {
                nodeRb.isKinematic = false; 
                
                float calculatedMass = 0.5f;
                foreach (Bar bar in p.ConnectedBars)
                {
                    if (bar == null || !bar.gameObject.activeSelf) continue; 
                    
                    float len = Vector3.Distance(bar.startPoint.transform.position, bar.endPoint.transform.position);
                    calculatedMass += (len * bar.materialData.massPerMeter) * 0.5f;
                }
                
                nodeRb.mass = calculatedMass;
                nodeRb.drag = 0.5f;
                nodeRb.angularDrag = 0.5f;
                nodeRb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            foreach (Bar bar in p.ConnectedBars)
            {
                if (bar == null || !bar.gameObject.activeSelf) continue; 
                
                if (!bar.materialData.isRope)
                {
                    AttachJoint(bar.gameObject, nodeRb, bar.materialData, p.transform.position);
                }
            }
        }

        foreach (Bar rope in activeBars)
        {
            if (!rope.materialData.isRope) continue;

            Rigidbody rbA = rope.startPoint.GetComponent<Rigidbody>();
            Rigidbody rbB = rope.endPoint.GetComponent<Rigidbody>();

            if (rbA != null && rbB != null)
            {
                SpringJoint ropeSpring = rbA.gameObject.AddComponent<SpringJoint>();
                ropeSpring.connectedBody = rbB;
                ropeSpring.autoConfigureConnectedAnchor = false;

                ropeSpring.anchor = rbA.transform.InverseTransformPoint(rope.startPoint.transform.position);
                ropeSpring.connectedAnchor = rbB.transform.InverseTransformPoint(rope.endPoint.transform.position);

                float length = Vector3.Distance(rope.startPoint.transform.position, rope.endPoint.transform.position);

                ropeSpring.maxDistance = length;
                ropeSpring.minDistance = 0f;
                ropeSpring.spring = rope.materialData.spring > 0 ? rope.materialData.spring : 5000f;
                ropeSpring.damper = rope.materialData.damper > 0 ? rope.materialData.damper : 500f; 

                BarStressHandler stressHandler = rope.GetComponent<BarStressHandler>();
                if (stressHandler != null) stressHandler.SetRopeJoint(ropeSpring);
            }
        } 
    }

    private void AttachJoint(GameObject barObj, Rigidbody targetRb, BridgeMaterialSO mat, Vector3 anchorWorldPosition)
    {
        int jointCount = mat.isDualBeam ? 2 : 1;

        for (int i = 0; i < jointCount; i++)
        {
            float zOffsetValue = mat.isDualBeam ? ((i == 0) ? mat.zOffset : -mat.zOffset) : 0f;
            Vector3 finalAnchorWorld = anchorWorldPosition + new Vector3(0, 0, zOffsetValue);

            if (mat.useSpring)
            {
                SpringJoint spring = barObj.AddComponent<SpringJoint>();
                spring.connectedBody = targetRb;
                spring.autoConfigureConnectedAnchor = false; 
                spring.anchor = barObj.transform.InverseTransformPoint(finalAnchorWorld);
                spring.connectedAnchor = targetRb.transform.InverseTransformPoint(finalAnchorWorld);
                spring.spring = mat.spring;
                spring.damper = mat.damper;
            }
            else
            {
                HingeJoint hinge = barObj.AddComponent<HingeJoint>();
                hinge.connectedBody = targetRb;
                hinge.autoConfigureConnectedAnchor = false; 
                hinge.anchor = barObj.transform.InverseTransformPoint(finalAnchorWorld);
                hinge.connectedAnchor = targetRb.transform.InverseTransformPoint(finalAnchorWorld);
                hinge.axis = new Vector3(0, 0, 1); 
            }
        }
    }

    private void ResolveAdjacentCollisions(HashSet<Bar> activeBars)
    {
        List<Collider> bridgeCols = new List<Collider>();
        foreach(Bar b in activeBars)
        {
            bridgeCols.AddRange(b.GetComponentsInChildren<Collider>());
        }

        for (int i = 0; i < bridgeCols.Count; i++)
        {
            for (int j = i + 1; j < bridgeCols.Count; j++)
            {
                Physics.IgnoreCollision(bridgeCols[i], bridgeCols[j], true);
            }
        }
    }
}

public class BarStressHandler : MonoBehaviour
{
    private BridgePhysicsManager manager; 
    private BridgeMaterialSO material;
    private Point p1;
    private Point p2;
    private Bar myBar;
    
    private float restLength;
    private Joint[] joints; 
    private SpringJoint ropeJoint; 
    
    [HideInInspector] public bool isBroken = false;
    [HideInInspector] public float currentStressPercent = 0f; 
    
    private int framesActive = 0; 
    private float smoothedForce = 0f;

    private Renderer[] childRenderers;
    private Color[] originalColors;

    public void Setup(BridgeMaterialSO mat, Point point1, Point point2)
    {
        manager = FindObjectOfType<BridgePhysicsManager>(); 
        material = mat;
        p1 = point1;
        p2 = point2;
        myBar = GetComponent<Bar>();
        
        restLength = Vector3.Distance(p1.transform.position, p2.transform.position);

        childRenderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[childRenderers.Length];
        
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (childRenderers[i].material.HasProperty("_Color"))
                originalColors[i] = childRenderers[i].material.color;
            else if (childRenderers[i].material.HasProperty("_BaseColor"))
                originalColors[i] = childRenderers[i].material.GetColor("_BaseColor");
            else
                originalColors[i] = Color.white;
        }
    }

    public void SetRopeJoint(SpringJoint joint)
    {
        ropeJoint = joint;
    }

    private void OnDestroy()
    {
        if (childRenderers == null) return;
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (childRenderers[i] != null) SetBarColor(originalColors[i], i);
        }
    }

    private void FixedUpdate()
    {
        if (isBroken || p1 == null || p2 == null) return;

        if (material.isRope && myBar != null)
        {
            myBar.StartPosition = p1.transform.position;
            myBar.UpdateCreatingBar(p2.transform.position);
        }

        if (!material.isRope && (joints == null || joints.Length == 0))
        {
            joints = GetComponents<Joint>();
            if (joints == null || joints.Length == 0) return; 
        }

        framesActive++;
        if (framesActive < 30) return;

        float currentLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        bool isTension = currentLength > restLength; 
        float maxForceThisFrame = 0f;
        
        Joint breakingJoint = null;
        string breakCause = "";

        if (material.isRope)
        {
            if (ropeJoint != null) maxForceThisFrame = ropeJoint.currentForce.magnitude;
        }
        else
        {
            foreach (Joint joint in joints)
            {
                if (joint == null) continue;
                float forceMag = joint.currentForce.magnitude;
                if (forceMag > maxForceThisFrame) maxForceThisFrame = forceMag;
            }
        }

        float absoluteLimit = isTension ? material.maxTension : material.maxCompression;
        if (absoluteLimit <= 0f) absoluteLimit = 1f;

        float maxStressChangePerFrame = (absoluteLimit * 5f) * Time.fixedDeltaTime;
        
        smoothedForce = Mathf.MoveTowards(smoothedForce, maxForceThisFrame, maxStressChangePerFrame);
        
        if (maxForceThisFrame < smoothedForce)
        {
            smoothedForce = Mathf.Lerp(smoothedForce, maxForceThisFrame, Time.fixedDeltaTime * 15f);
        }

        if (material.isRope)
        {
            if (smoothedForce > material.maxTension)
            {
                breakingJoint = ropeJoint;
                breakCause = "Tension (Rope Snapped)";
            }
        }
        else
        {
            if (isTension && smoothedForce > material.maxTension)
            {
                breakingJoint = joints[0]; 
                breakCause = "Tension (Pulled apart)";
            }
            else if (!isTension && smoothedForce > material.maxCompression)
            {
                breakingJoint = joints[0];
                breakCause = "Compression (Buckled)";
            }
        }

        float stressLimit = isTension ? material.maxTension : material.maxCompression;
        if (stressLimit <= 0f) stressLimit = 1f; 

        if (material.isRope && !isTension) currentStressPercent = 0f; 
        else currentStressPercent = smoothedForce / stressLimit;

        if (manager.enableVisualizer)
        {
            UpdateStressVisuals();
        }

        if (breakingJoint != null && !isBroken)
        {
            BreakBar(breakCause, smoothedForce, breakingJoint);
        }
    }

    private void UpdateStressVisuals()
    {
        if (childRenderers == null || childRenderers.Length == 0) return;

        for (int i = 0; i < childRenderers.Length; i++)
        {
            Color stressColor;

            if (currentStressPercent < 0.5f)
            {
                stressColor = Color.Lerp(originalColors[i], manager.warningColor, currentStressPercent * 2f);
            }
            else
            {
                stressColor = Color.Lerp(manager.warningColor, manager.criticalColor, (currentStressPercent - 0.5f) * 2f);
            }

            SetBarColor(stressColor, i);
        }
    }

    private void BreakBar(string cause, float force, Joint brokenJoint)
    {
        if (isBroken) return;
        isBroken = true;
        currentStressPercent = 1f; 

        if (brokenJoint != null) Destroy(brokenJoint);
        
        for (int i = 0; i < childRenderers.Length; i++) SetBarColor(manager.brokenColor, i);
        
        if (material.isRope && myBar != null)
        {
            myBar.StartPosition = p1.transform.position;
            myBar.UpdateCreatingBar(p1.transform.position + (Vector3.down * restLength));
        }
    }

    private void SetBarColor(Color targetColor, int index)
    {
        if (childRenderers[index] == null) return;

        if (childRenderers[index].material.HasProperty("_Color"))
        {
            childRenderers[index].material.color = targetColor;
        }
        else if (childRenderers[index].material.HasProperty("_BaseColor"))
        {
            childRenderers[index].material.SetColor("_BaseColor", targetColor);
        }
    }

    private void OnJointBreak(float breakForce)
    {
        if (!isBroken) 
        {
            isBroken = true;
            currentStressPercent = 1f;
            for (int i = 0; i < childRenderers.Length; i++) SetBarColor(manager.brokenColor, i);
        }
    }
}