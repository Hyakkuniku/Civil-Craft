using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class LiveLoadVehicle : MonoBehaviour
{
    [Header("Engineering Specifications")]
    public float chassisWeight = 1500f; 
    public Transform centerOfMass;

    [Header("Drive System (Hinge Motors)")]
    [Tooltip("Drag the 4 WHEELS (that have Hinge Joints) here")]
    public List<HingeJoint> wheelJoints = new List<HingeJoint>();
    
    [Tooltip("Target spin speed (Degrees per second)")]
    public float targetSpeed = 800f; 
    [Tooltip("How much force the motor pushes with")]
    public float motorForce = 5000f; 

    [HideInInspector] public bool isDriving = false;

    private Rigidbody rb;
    private float totalCargoWeight = 0f;
    private BridgePhysicsManager physicsManager;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        physicsManager = FindObjectOfType<BridgePhysicsManager>();
        
        if (centerOfMass != null)
        {
            rb.centerOfMass = centerOfMass.localPosition;
        }
    }

    public void SetCargoWeight(float payload)
    {
        totalCargoWeight = payload;
        rb.mass = chassisWeight + totalCargoWeight;
    }

    public void StartDriving()
    {
        if (physicsManager != null && physicsManager.isSimulating)
        {
            isDriving = true;
            Debug.Log("<color=green>TRUCK IS ROLLING WITH MOTORS!</color>");
        }
        else
        {
            Debug.LogWarning("Hit Simulate first!");
        }
    }

    public void StopDriving()
    {
        isDriving = false;
    }

    private void FixedUpdate()
    {
        if (physicsManager != null && physicsManager.isSimulating && isDriving)
        {
            DriveForward();
        }
        else
        {
            ApplyBrakes();
        }
    }

    private void DriveForward()
    {
        foreach (HingeJoint joint in wheelJoints)
        {
            if (joint != null)
            {
                joint.useMotor = true;
                JointMotor motor = joint.motor;
                motor.targetVelocity = targetSpeed; // Spin forward!
                motor.force = motorForce;
                joint.motor = motor;
            }
        }
    }

    private void ApplyBrakes()
    {
        foreach (HingeJoint joint in wheelJoints)
        {
            if (joint != null)
            {
                joint.useMotor = true;
                JointMotor motor = joint.motor;
                motor.targetVelocity = 0; // Lock the wheels at 0 speed
                motor.force = motorForce;
                joint.motor = motor;
            }
        }
    }
}