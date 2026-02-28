using System.Collections.Generic;
using UnityEngine;

public class BridgePhysicsManager : MonoBehaviour
{
    [Header("Physics Settings")]
    public float barColliderThickness = 0.2f;
    [Tooltip("How heavy the connection nodes should be. Higher = less wobbling.")]
    public float pointMass = 5f; 

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            ActivatePhysics();
        }
    }

    public void ActivatePhysics()
    {
        SetupPoints();
        SetupBars();
        ResolveAdjacentCollisions(); 
        Debug.Log("<color=green>Physics Activated! Dual-pins and Stress limits applied.</color>");
    }

    private void SetupPoints()
    {
        foreach (Point p in Point.AllPoints)
        {
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = p.gameObject.AddComponent<Rigidbody>();
                rb.mass = pointMass; 
                rb.drag = 0.5f;
                rb.angularDrag = 0.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }

            if (p.isAnchor) rb.isKinematic = true;

            Collider existingCollider = p.GetComponent<Collider>();
            if (existingCollider != null && !(existingCollider is SphereCollider))
            {
                Destroy(existingCollider);
            }

            if (p.GetComponent<SphereCollider>() == null)
            {
                SphereCollider col = p.gameObject.AddComponent<SphereCollider>();
                col.radius = 0.15f;
            }
        }
    }

    private void SetupBars()
    {
        HashSet<Bar> allBars = new HashSet<Bar>();
        foreach (Point p in Point.AllPoints)
        {
            foreach (Bar b in p.ConnectedBars)
            {
                if (b != null) allBars.Add(b);
            }
        }

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

        BoxCollider barCol = bar.gameObject.AddComponent<BoxCollider>();
        float zDepth = bar.materialData.isDualBeam ? (bar.materialData.zOffset * 2f + barColliderThickness) : barColliderThickness;
        barCol.size = new Vector3(length, barColliderThickness, zDepth);

        Physics.IgnoreCollision(barCol, p1.GetComponent<Collider>());
        Physics.IgnoreCollision(barCol, p2.GetComponent<Collider>());

        AttachJointsToPoint(bar.gameObject, p1.GetComponent<Rigidbody>(), bar.materialData, p1.transform.position);
        AttachJointsToPoint(bar.gameObject, p2.GetComponent<Rigidbody>(), bar.materialData, p2.transform.position);

        // Setup the new custom stress handler
        BarStressHandler stressHandler = bar.gameObject.AddComponent<BarStressHandler>();
        stressHandler.Setup(bar.materialData, p1, p2);
    }

    private void AttachJointsToPoint(GameObject barObj, Rigidbody targetPointRb, BridgeMaterialSO mat, Vector3 anchorWorldPosition)
    {
        int jointCount = mat.isDualBeam ? 2 : 1;

        for (int i = 0; i < jointCount; i++)
        {
            float zOffsetValue = 0f;
            if (mat.isDualBeam)
            {
                zOffsetValue = (i == 0) ? mat.zOffset : -mat.zOffset;
            }

            Vector3 finalAnchorWorld = anchorWorldPosition + new Vector3(0, 0, zOffsetValue);

            if (mat.useSpring)
            {
                SpringJoint spring = barObj.AddComponent<SpringJoint>();
                spring.connectedBody = targetPointRb;
                spring.anchor = barObj.transform.InverseTransformPoint(finalAnchorWorld);
                spring.spring = mat.spring;
                spring.damper = mat.damper;
                spring.breakForce = mat.breakForce; 
                spring.breakTorque = mat.breakTorque;
                spring.enablePreprocessing = false; 
            }
            else
            {
                HingeJoint hinge = barObj.AddComponent<HingeJoint>();
                hinge.connectedBody = targetPointRb;
                hinge.anchor = barObj.transform.InverseTransformPoint(finalAnchorWorld);
                hinge.axis = new Vector3(0, 0, 1); 
                hinge.breakForce = mat.breakForce;
                hinge.breakTorque = mat.breakTorque;
                hinge.enablePreprocessing = false; 
            }
        }
    }

    private void ResolveAdjacentCollisions()
    {
        foreach (Point p in Point.AllPoints)
        {
            for (int i = 0; i < p.ConnectedBars.Count; i++)
            {
                Collider colA = p.ConnectedBars[i].GetComponent<Collider>();
                if (colA == null) continue;

                for (int j = i + 1; j < p.ConnectedBars.Count; j++)
                {
                    Collider colB = p.ConnectedBars[j].GetComponent<Collider>();
                    if (colB != null)
                    {
                        Physics.IgnoreCollision(colA, colB, true);
                    }
                }
            }
        }
    }
}

// --- NEW STRESS HANDLER ---
public class BarStressHandler : MonoBehaviour
{
    private BridgeMaterialSO material;
    private Point p1;
    private Point p2;
    
    private float restLength;
    private Joint[] joints;
    private bool isBroken = false;

    public void Setup(BridgeMaterialSO mat, Point point1, Point point2)
    {
        material = mat;
        p1 = point1;
        p2 = point2;
        
        // Save the exact length the bar was built at
        restLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        joints = GetComponents<Joint>();
    }

    private void FixedUpdate()
    {
        if (isBroken || p1 == null || p2 == null) return;

        // Compare current distance between the nodes to the original length
        float currentLength = Vector3.Distance(p1.transform.position, p2.transform.position);
        bool isTension = currentLength > restLength; // Stretching = Tension, Shrinking = Compression

        foreach (Joint joint in joints)
        {
            if (joint == null) continue;

            float forceMag = joint.currentForce.magnitude;

            if (isTension && forceMag > material.maxTension)
            {
                BreakBar("Tension (Pulled apart)", forceMag);
                break; // Stop checking other joints if we break
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

        // Destroy all joints on this bar to let it fall physically
        foreach (Joint j in joints)
        {
            if (j != null) Destroy(j);
        }

        // Optional: Add some snapping visual effect here before destroying
        Destroy(gameObject, 2f);
    }

    // Fallback in case Unity's native shear limit is hit before axial limits
    private void OnJointBreak(float breakForce)
    {
        if (!isBroken) BreakBar("Shear/Torque (Native Joint Break)", breakForce);
    }
}