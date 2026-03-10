using System.Collections.Generic;
using UnityEngine;

public class BridgePhysicsManager : MonoBehaviour
{
    [Header("Physics Settings")]
    public float barColliderThickness = 0.2f;
    public int physicsSolverIterations = 40; 

    [HideInInspector] public bool isSimulating = false;
    [HideInInspector] public List<BarStressHandler> activeStressHandlers = new List<BarStressHandler>();

    private void Start()
    {
        ActivatePhysics();

        // NEW: Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.AddListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.AddListener(HandleExitBuildMode);
        }
    }

    private void OnDestroy()
    {
        // NEW: Unsubscribe to prevent memory leaks
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnterBuildMode.RemoveListener(HandleEnterBuildMode);
            GameManager.Instance.OnExitBuildMode.RemoveListener(HandleExitBuildMode);
        }
    }

    private void HandleEnterBuildMode()
    {
        if (isSimulating) StopPhysicsAndReset();
    }

    private void HandleExitBuildMode()
    {
        if (!isSimulating) ActivatePhysics();
    }

    public void ActivatePhysics()
    {
        if (isSimulating) return;
        isSimulating = true;
        activeStressHandlers.Clear(); 

        foreach (Point p in Point.AllPoints)
        {
            p.preSimPos = p.transform.position;
            p.preSimParent = p.transform.parent;

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = false;
        }

        HashSet<Bar> allBars = new HashSet<Bar>();
        foreach (Point p in Point.AllPoints)
        {
            foreach (Bar b in p.ConnectedBars)
                if (b != null) allBars.Add(b);
        }

        foreach (Bar b in allBars)
        {
            b.preSimPos = b.transform.position;
            b.preSimRot = b.transform.rotation;
        }

        Physics.defaultSolverIterations = physicsSolverIterations;
        Physics.defaultSolverVelocityIterations = 20;

        SetupBarsPhysics(allBars);
        SetupDirectConnections(allBars);
        ResolveAdjacentCollisions(allBars); 
    }

    public void StopPhysicsAndReset()
    {
        if (!isSimulating) return;
        isSimulating = false;
        activeStressHandlers.Clear();

        HashSet<Bar> allBars = new HashSet<Bar>();
        foreach (Point p in Point.AllPoints)
        {
            foreach (Bar b in p.ConnectedBars)
                if (b != null) allBars.Add(b);
        }

        foreach (Bar bar in allBars)
        {
            Joint[] joints = bar.GetComponents<Joint>();
            foreach (Joint j in joints) 
            {
                j.connectedBody = null; 
                Destroy(j);
            }
        }

        foreach (Point p in Point.AllPoints)
        {
            Joint[] joints = p.GetComponents<Joint>();
            foreach (Joint j in joints) 
            {
                j.connectedBody = null; 
                Destroy(j);
            }
        }

        foreach (Point p in Point.AllPoints)
        {
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);

            Collider[] cols = p.GetComponents<Collider>();
            foreach(var col in cols) col.enabled = true; 

            p.transform.SetParent(p.preSimParent);
            p.transform.position = p.preSimPos;
            p.transform.rotation = Quaternion.identity; 

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = true;
        }

        foreach (Bar bar in allBars)
        {
            Rigidbody barRb = bar.GetComponent<Rigidbody>();
            if (barRb != null) Destroy(barRb);

            BoxCollider[] barCols = bar.GetComponents<BoxCollider>();
            foreach (BoxCollider c in barCols) Destroy(c);

            BarStressHandler stress = bar.GetComponent<BarStressHandler>();
            if (stress != null) Destroy(stress);

            bar.transform.position = bar.preSimPos;
            bar.transform.rotation = bar.preSimRot;
        }

        foreach (Bar bar in allBars)
        {
            if (bar.startPoint != null && bar.endPoint != null)
            {
                bar.StartPosition = bar.startPoint.transform.position;
                bar.UpdateCreatingBar(bar.endPoint.transform.position);
            }
        }
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
        return Mathf.Clamp01(maxStress); 
    }

    private void SetupBarsPhysics(HashSet<Bar> allBars)
    {
        foreach (Bar bar in allBars)
        {
            if (bar.GetComponent<Rigidbody>() == null) ApplyPhysicsToBar(bar);
        }
    }

    private void ApplyPhysicsToBar(Bar bar)
    {
        List<Point> endpoints = new List<Point>();
        foreach (Point p in Point.AllPoints)
        {
            if (p.ConnectedBars.Contains(bar)) endpoints.Add(p);
        }

        if (endpoints.Count != 2) return; 

        Point p1 = endpoints[0];
        Point p2 = endpoints[1];

        if (!bar.materialData.isRope)
        {
            float length = Vector3.Distance(p1.transform.position, p2.transform.position);
            
            Rigidbody barRb = bar.GetComponent<Rigidbody>();
            if (barRb == null) barRb = bar.gameObject.AddComponent<Rigidbody>();
            
            barRb.isKinematic = false; 
            barRb.mass = length * bar.materialData.massPerMeter; 
            
            barRb.drag = 0.5f;
            barRb.angularDrag = 0.5f;
            barRb.interpolation = RigidbodyInterpolation.Interpolate;
            barRb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            BoxCollider[] oldCols = bar.GetComponents<BoxCollider>();
            foreach(var c in oldCols) Destroy(c);

            int spawnCount = bar.materialData.isDualBeam ? 2 : 1;

            for (int i = 0; i < spawnCount; i++)
            {
                BoxCollider col = bar.gameObject.AddComponent<BoxCollider>();
                float thickness = bar.visualSize.y > 0.05f ? bar.visualSize.y : barColliderThickness;
                float depth = bar.visualSize.z; 

                if (!bar.materialData.isDualBeam && depth < 2.0f) depth = 2.0f; 
                else if (bar.materialData.isDualBeam && depth < 0.2f) depth = 0.2f;

                float zOffsetValue = bar.materialData.isDualBeam ? ((i == 0) ? bar.materialData.zOffset : -bar.materialData.zOffset) : 0f;
                float physicsLength = length - 0.4f; 
                if (physicsLength < 0.1f) physicsLength = 0.1f; 

                col.size = new Vector3(physicsLength, thickness, depth);
                col.center = new Vector3(0, 0, zOffsetValue);
            }
        }

        bar.gameObject.layer = LayerMask.NameToLayer("Bridge"); 

        BarStressHandler stressHandler = bar.GetComponent<BarStressHandler>();
        if (stressHandler == null) stressHandler = bar.gameObject.AddComponent<BarStressHandler>();
        
        stressHandler.Setup(bar.materialData, p1, p2);
        activeStressHandlers.Add(stressHandler);
    }

    private void SetupDirectConnections(HashSet<Bar> allBars)
    {
        foreach (Point p in Point.AllPoints)
        {
            if (p.ConnectedBars.Count == 0) continue;

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
                if (!bar.materialData.isRope)
                {
                    AttachJoint(bar.gameObject, nodeRb, bar.materialData, p.transform.position);
                }
            }
        }

        foreach (Bar rope in allBars)
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
                ropeSpring.breakForce = rope.materialData.breakForce;

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
                spring.breakForce = mat.breakForce; 
                spring.breakTorque = mat.breakTorque;
            }
            else
            {
                HingeJoint hinge = barObj.AddComponent<HingeJoint>();
                hinge.connectedBody = targetRb;
                hinge.autoConfigureConnectedAnchor = false; 
                hinge.anchor = barObj.transform.InverseTransformPoint(finalAnchorWorld);
                hinge.connectedAnchor = targetRb.transform.InverseTransformPoint(finalAnchorWorld);
                hinge.axis = new Vector3(0, 0, 1); 
                hinge.breakForce = mat.breakForce;
                hinge.breakTorque = mat.breakTorque;
            }
        }
    }

    private void ResolveAdjacentCollisions(HashSet<Bar> allBars)
    {
        List<Collider> bridgeCols = new List<Collider>();
        foreach(Bar b in allBars)
        {
            bridgeCols.AddRange(b.GetComponents<Collider>());
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

    public void Setup(BridgeMaterialSO mat, Point point1, Point point2)
    {
        manager = FindObjectOfType<BridgePhysicsManager>(); 
        material = mat;
        p1 = point1;
        p2 = point2;
        myBar = GetComponent<Bar>();
        
        restLength = Vector3.Distance(p1.transform.position, p2.transform.position);
    }

    public void SetRopeJoint(SpringJoint joint)
    {
        ropeJoint = joint;
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
        if (framesActive < 10) return;

        float currentLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        bool isTension = currentLength > restLength; 
        float maxForceThisFrame = 0f;
        
        Joint breakingJoint = null;
        string breakCause = "";

        if (material.isRope)
        {
            if (ropeJoint != null)
            {
                maxForceThisFrame = ropeJoint.currentForce.magnitude;
                if (maxForceThisFrame > material.maxTension)
                {
                    breakingJoint = ropeJoint;
                    breakCause = "Tension (Rope Snapped)";
                }
            }
        }
        else
        {
            foreach (Joint joint in joints)
            {
                if (joint == null) continue;

                float forceMag = joint.currentForce.magnitude;
                if (forceMag > maxForceThisFrame) maxForceThisFrame = forceMag;

                if (isTension && forceMag > material.maxTension)
                {
                    breakingJoint = joint;
                    breakCause = "Tension (Pulled apart)";
                }
                else if (!isTension && forceMag > material.maxCompression)
                {
                    breakingJoint = joint;
                    breakCause = "Compression (Buckled)";
                }
            }
        }

        float stressLimit = isTension ? material.maxTension : material.maxCompression;
        if (stressLimit <= 0f) stressLimit = 1f; 

        if (material.isRope && !isTension) currentStressPercent = 0f; 
        else currentStressPercent = maxForceThisFrame / stressLimit;

        if (breakingJoint != null && !isBroken)
        {
            BreakBar(breakCause, maxForceThisFrame, breakingJoint);
        }
    }

    private void BreakBar(string cause, float force, Joint brokenJoint)
    {
        if (isBroken) return;
        isBroken = true;
        currentStressPercent = 1f; 

        if (brokenJoint != null) Destroy(brokenJoint);
        
        if (material.isRope && myBar != null)
        {
            myBar.StartPosition = p1.transform.position;
            myBar.UpdateCreatingBar(p1.transform.position + (Vector3.down * restLength));
        }
    }

    private void OnJointBreak(float breakForce)
    {
        if (!isBroken) 
        {
            isBroken = true;
            currentStressPercent = 1f;
        }
    }
}