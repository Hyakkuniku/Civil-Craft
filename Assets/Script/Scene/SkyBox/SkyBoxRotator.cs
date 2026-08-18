using UnityEngine;

public class SkyboxRotator : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("How fast the skybox rotates. Can be positive or negative.")]
    public float rotationSpeed = 1.2f;

    private Material skyboxMaterial;
    private int rotationPropertyId;
    private float currentRotation = 0f;

    private void Start()
    {
        // 1. Cache the skybox material so we aren't calling RenderSettings every frame
        skyboxMaterial = RenderSettings.skybox;

        // 2. Cache the shader property ID for maximum performance
        rotationPropertyId = Shader.PropertyToID("_Rotation");

        if (skyboxMaterial == null)
        {
            Debug.LogWarning("SkyboxRotator: No skybox material found in RenderSettings!");
        }
    }

    private void Update()
    {
        if (skyboxMaterial != null)
        {
            // Increment the rotation
            currentRotation += Time.deltaTime * rotationSpeed;
            
            // Loop it between 0 and 360 to prevent floating-point math errors if the game runs for hours
            currentRotation %= 360f; 

            // Apply the rotation directly to the cached material
            skyboxMaterial.SetFloat(rotationPropertyId, currentRotation);
        }
    }
    
    private void OnDestroy()
    {
        // Optional safety cleanup: reset rotation if the object is destroyed so it doesn't get stuck
        if (skyboxMaterial != null)
        {
            skyboxMaterial.SetFloat(rotationPropertyId, 0f);
        }
    }
}