using UnityEngine;

public class UIModelRotator : MonoBehaviour
{
    [Tooltip("How fast the model spins in the UI")]
    public float rotationSpeed = 30f;

    void Update()
    {
        // Slowly rotates the model around its Y axis
        transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
    }
}