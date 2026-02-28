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
        Debug.Log("<color=green>Physics Activated! Dual-pins applied to prevent twisting.</color>");
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

        // Attach the joints using our new dual-pin logic!
        AttachJointsToPoint(bar.gameObject, p1.GetComponent<Rigidbody>(), bar.materialData, p1.transform.position);
        AttachJointsToPoint(bar.gameObject, p2.GetComponent<Rigidbody>(), bar.materialData, p2.transform.position);

        bar.gameObject.AddComponent<BarStressHandler>();
    }

    // THE FIX: This now checks if the beam is wide (DualBeam). 
    // If it is, it attaches TWO separate pins at the exact visual offsets so it can't twist!
    private void AttachJointsToPoint(GameObject barObj, Rigidbody targetPointRb, BridgeMaterialSO mat, Vector3 anchorWorldPosition)
    {
        int jointCount = mat.isDualBeam ? 2 : 1;

        for (int i = 0; i < jointCount; i++)
        {
            float zOffsetValue = 0f;
            if (mat.isDualBeam)
            {
                // Push one joint to the back, and one to the front
                zOffsetValue = (i == 0) ? mat.zOffset : -mat.zOffset;
            }

            // Calculate the exact 3D world position of where the pin should go
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

public class BarStressHandler : MonoBehaviour
{
    private void OnJointBreak(float breakForce)
    {
        Debug.Log($"<color=orange>Stress limit reached!</color> Bar snapped with force: {breakForce}");
        Destroy(gameObject, 2f);
    }
}