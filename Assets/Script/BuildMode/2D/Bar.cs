using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bar : MonoBehaviour
{
    public Vector2 StartPosition;
    public BridgeMaterialSO materialData;

    private GameObject visualSegment;
    private float baseLength = 1f; 
    private Vector3 originalScale = Vector3.one;

    public void Initialize(BridgeMaterialSO data)
    {
        materialData = data;
        
        if (materialData.segmentPrefab != null)
        {
            visualSegment = Instantiate(materialData.segmentPrefab, transform);
            visualSegment.name = "VisualSegment";
            
            var renderer = visualSegment.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                baseLength = renderer.bounds.size.x;
                if (baseLength == 0) baseLength = 1f; 
            }
            
            originalScale = visualSegment.transform.localScale;
        }
    }

    public void UpdateCreatingBar(Vector2 ToPosition)
    {
        if (visualSegment == null) return;

        Vector2 dir = ToPosition - StartPosition;
        float totalDistance = dir.magnitude;
        
        if (totalDistance < 0.01f) return;

        float angle = Vector2.SignedAngle(Vector2.right, dir);
        Quaternion rotation = Quaternion.Euler(0, 0, angle);

        Vector2 midPoint = StartPosition + (dir / 2f);
        visualSegment.transform.position = midPoint;
        visualSegment.transform.rotation = rotation;

        float scaleMultiplier = totalDistance / baseLength;
        visualSegment.transform.localScale = new Vector3(
            originalScale.x * scaleMultiplier, 
            originalScale.y, 
            originalScale.z
        );
    }
}