using System.Collections.Generic;
using UnityEngine;

public class BridgePhysicsManager : MonoBehaviour
{
    [Header("Physics Settings")]
    public float barColliderThickness = 0.2f;
    public int physicsSolverIterations = 30; 

    [HideInInspector] public bool isSimulating = false;

    // ────────────────────────────────────────────────────────────
    // NEW: Automatically start physics when the game loads!
    // This hides the anchors and makes the bridge solid immediately.
    // ────────────────────────────────────────────────────────────
    private void Start()
    {
        ActivatePhysics();
    }

    public void ActivatePhysics()
    {
        if (isSimulating) return;
        isSimulating = true;

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
        Physics.defaultSolverVelocityIterations = 15;

        SetupBarsPhysics(allBars);
        SetupDirectConnections();
        ResolveAdjacentCollisions(); 

        Debug.Log("<color=green>Physics Activated!</color>");
    }

    public void StopPhysicsAndReset()
    {
        if (!isSimulating) return;
        isSimulating = false;

        foreach (Point p in Point.AllPoints)
        {
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb != null) 
            {
                rb.isKinematic = true; 
                Destroy(rb);
            }

            Collider[] cols = p.GetComponents<Collider>();
            foreach(var col in cols) col.enabled = true; 

            p.transform.SetParent(p.preSimParent);
            p.transform.position = p.preSimPos;

            Renderer r = p.GetComponentInChildren<Renderer>();
            if (r != null) r.enabled = true;
        }

        HashSet<Bar> allBars = new HashSet<Bar>();
        foreach (Point p in Point.AllPoints)
        {
            foreach (Bar b in p.ConnectedBars)
                if (b != null) allBars.Add(b);
        }

        foreach (Bar bar in allBars)
        {
            Joint[] joints = bar.GetComponents<Joint>();
            foreach (Joint j in joints) Destroy(j);

            BarStressHandler stress = bar.GetComponent<BarStressHandler>();
            if (stress != null) Destroy(stress);

            BoxCollider[] barCols = bar.GetComponents<BoxCollider>();
            foreach (BoxCollider c in barCols) Destroy(c);

            Rigidbody barRb = bar.GetComponent<Rigidbody>();
            if (barRb != null) 
            {
                barRb.isKinematic = true;
                Destroy(barRb);
            }

            bar.transform.position = bar.preSimPos;
            bar.transform.rotation = bar.preSimRot;
        }

        Debug.Log("<color=yellow>Bridge Reset. Back to Build Mode!</color>");
    }

    private void SetupBarsPhysics(HashSet<Bar> allBars)
    {
        foreach (Bar bar in allBars)
        {
            if (bar.GetComponent<Rigidbody>() == null)
            {
                ApplyPhysicsToBar(bar);
            }
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

        float length = Vector3.Distance(p1.transform.position, p2.transform.position);
        
        Rigidbody barRb = bar.gameObject.AddComponent<Rigidbody>();
        barRb.mass = length * bar.materialData.massPerMeter; 
        barRb.drag = 0.1f;
        barRb.angularDrag = 0.1f;
        barRb.interpolation = RigidbodyInterpolation.Interpolate;
        barRb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        int spawnCount = bar.materialData.isDualBeam ? 2 : 1;

        for (int i = 0; i < spawnCount; i++)
        {
            BoxCollider col = bar.gameObject.AddComponent<BoxCollider>();
            
            float thickness = bar.visualSize.y > 0.05f ? bar.visualSize.y : barColliderThickness;
            float depth = bar.visualSize.z; 

            if (!bar.materialData.isDualBeam && depth < 2.0f)
            {
                depth = 2.0f; 
            }
            else if (bar.materialData.isDualBeam && depth < 0.2f)
            {
                depth = 0.2f;
            }

            float zOffsetValue = 0f;
            if (bar.materialData.isDualBeam)
            {
                zOffsetValue = (i == 0) ? bar.materialData.zOffset : -bar.materialData.zOffset;
            }

            float physicsLength = length - 0.4f; 
            if (physicsLength < 0.1f) physicsLength = 0.1f; 

            col.size = new Vector3(physicsLength, thickness, depth);
            col.center = new Vector3(0, 0, zOffsetValue);
        }

        bar.gameObject.layer = LayerMask.NameToLayer("Bridge"); 

        BarStressHandler stressHandler = bar.gameObject.AddComponent<BarStressHandler>();
        stressHandler.Setup(bar.materialData, p1, p2);
    }

    private void SetupDirectConnections()
    {
        foreach (Point p in Point.AllPoints)
        {
            if (p.ConnectedBars.Count == 0) continue;

            Collider[] oldCols = p.GetComponents<Collider>();
            foreach(var col in oldCols) col.enabled = false; 

            if (p.isAnchor)
            {
                Rigidbody anchorRb = p.gameObject.AddComponent<Rigidbody>();
                anchorRb.isKinematic = true;

                foreach (Bar bar in p.ConnectedBars)
                {
                    AttachJoint(bar.gameObject, anchorRb, bar.materialData, p.transform.position);
                }
            }
            else
            {
                Bar hubBar = p.ConnectedBars[0];
                Rigidbody hubRb = hubBar.GetComponent<Rigidbody>();

                for (int i = 1; i < p.ConnectedBars.Count; i++)
                {
                    Bar attachedBar = p.ConnectedBars[i];
                    AttachJoint(attachedBar.gameObject, hubRb, attachedBar.materialData, p.transform.position);
                }

                p.transform.SetParent(hubBar.transform, true);
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

    private void ResolveAdjacentCollisions()
    {
        foreach (Point p in Point.AllPoints)
        {
            for (int i = 0; i < p.ConnectedBars.Count; i++)
            {
                Collider[] colsA = p.ConnectedBars[i].GetComponents<Collider>();
                if (colsA.Length == 0) continue;

                for (int j = i + 1; j < p.ConnectedBars.Count; j++)
                {
                    Collider[] colsB = p.ConnectedBars[j].GetComponents<Collider>();
                    
                    foreach (Collider cA in colsA)
                    {
                        foreach (Collider cB in colsB)
                        {
                            Physics.IgnoreCollision(cA, cB, true);
                        }
                    }
                }
            }
        }
    }
}

public class BarStressHandler : MonoBehaviour
{
    private BridgeMaterialSO material;
    private Point p1;
    private Point p2;
    
    private float restLength;
    private Joint[] joints;
    private bool isBroken = false;
    private int framesActive = 0; 

    public void Setup(BridgeMaterialSO mat, Point point1, Point point2)
    {
        material = mat;
        p1 = point1;
        p2 = point2;
        
        restLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        joints = GetComponents<Joint>();
    }

    private void FixedUpdate()
    {
        if (isBroken || p1 == null || p2 == null) return;

        framesActive++;
        if (framesActive < 10) return;

        float currentLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        bool isTension = currentLength > restLength; 

        foreach (Joint joint in joints)
        {
            if (joint == null) continue;

            float forceMag = joint.currentForce.magnitude;

            if (isTension && forceMag > material.maxTension)
            {
                BreakBar("Tension (Pulled apart)", forceMag);
                break; 
            }
            else if (!isTension && forceMag > material.maxCompression)
            {
                BreakBar("Compression (Buckled)", forceMag);
                break;
            }
        }
    }

    private void BreakBar(string cause, float force)
    {
        if (isBroken) return;
        isBroken = true;

        Debug.Log($"<color=orange>Stress limit reached!</color> Bar snapped due to {cause}. Force: {force}");

        foreach (Joint j in joints)
        {
            if (j != null) Destroy(j);
        }
    }

    private void OnJointBreak(float breakForce)
    {
        if (!isBroken) BreakBar("Shear/Torque (Native Joint Break)", breakForce);
    }
}