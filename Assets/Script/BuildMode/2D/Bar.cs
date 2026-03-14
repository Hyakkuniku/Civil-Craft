using System.Collections.Generic;
using UnityEngine;

public class Bar : MonoBehaviour
{
    public Vector3 StartPosition; 
    public BridgeMaterialSO materialData;

    public Point startPoint;
    public Point endPoint;

    [HideInInspector] public Vector3 preSimPos;
    [HideInInspector] public Quaternion preSimRot;
    [HideInInspector] public Vector3 visualSize = new Vector3(1f, 0.2f, 0.2f);
    [HideInInspector] public float currentLength = 0f;

    private List<GameObject> visualSegments = new List<GameObject>();
    private float baseLength = 1f; 
    private Vector3 originalScale = Vector3.one;
    
    private GameObject pierCapInstance;
    private Vector3 originalCapScale = Vector3.one;
    
    // We now track BOTH the top and bottom of the T-Cap
    private float capTopOffset = 0f;
    private float capBottomOffset = 0f;

    private void OnEnable()
    {
        if (startPoint != null && !startPoint.ConnectedBars.Contains(this)) startPoint.ConnectedBars.Add(this);
        if (endPoint != null && !endPoint.ConnectedBars.Contains(this)) endPoint.ConnectedBars.Add(this);
    }

    private void OnDisable()
    {
        if (startPoint != null) startPoint.ConnectedBars.Remove(this);
        if (endPoint != null) endPoint.ConnectedBars.Remove(this);
    }

    public void Initialize(BridgeMaterialSO data)
    {
        materialData = data;
        visualSegments.Clear();
        
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(false); 
            Destroy(child.gameObject);
        }
        
        if (materialData.segmentPrefab != null)
        {
            int spawnCount = materialData.isDualBeam ? 2 : 1;

            for (int i = 0; i < spawnCount; i++)
            {
                GameObject newSegment = Instantiate(materialData.segmentPrefab, transform);
                newSegment.name = materialData.isDualBeam ? $"VisualSegment_{i}" : "VisualSegment";
                
                float offsetValue = 0f;
                if (materialData.isDualBeam)
                {
                    offsetValue = (i == 0) ? materialData.zOffset : -materialData.zOffset;
                }
                
                newSegment.transform.localPosition = new Vector3(0, 0, offsetValue);

                var renderer = newSegment.GetComponentInChildren<Renderer>();
                if (renderer != null && i == 0)
                {
                    originalScale = newSegment.transform.localScale;
                    
                    baseLength = materialData.isPier ? renderer.bounds.size.y : renderer.bounds.size.x;
                    visualSize = renderer.bounds.size; 
                    if (baseLength <= 0f) baseLength = 1f; 
                }
                
                newSegment.transform.localScale = Vector3.zero;
                visualSegments.Add(newSegment);
            }
        }

        if (materialData.isPier && materialData.pierCapPrefab != null)
        {
            pierCapInstance = Instantiate(materialData.pierCapPrefab, transform);
            pierCapInstance.name = "PierCap";
            originalCapScale = pierCapInstance.transform.localScale;

            // --- THE FIX: Measure BOTH the top and bottom of the T-Cap ---
            pierCapInstance.transform.position = Vector3.zero;
            pierCapInstance.transform.rotation = Quaternion.identity;
            
            Renderer capRend = pierCapInstance.GetComponentInChildren<Renderer>();
            if (capRend != null)
            {
                capTopOffset = capRend.bounds.max.y;    // The absolute highest point
                capBottomOffset = capRend.bounds.min.y; // The absolute lowest point
            }

            pierCapInstance.transform.localScale = Vector3.zero; 
        }
    }

    public void UpdateCreatingBar(Vector3 ToPosition) 
    {
        if (visualSegments.Count == 0) return;

        Vector3 flatToPosition = ToPosition;
        flatToPosition.z = StartPosition.z; 

        Vector3 dir2D = flatToPosition - StartPosition;
        float totalDistance = dir2D.magnitude;
        
        currentLength = totalDistance;
        
        if (totalDistance < 0.01f) 
        {
            foreach (var seg in visualSegments) seg.transform.localScale = Vector3.zero;
            if (pierCapInstance != null) pierCapInstance.transform.localScale = Vector3.zero;
            return;
        }

        if (materialData.isPier)
        {
            // 1. Position the T-Cap so its TOP edge aligns exactly with the Anchor Node!
            if (pierCapInstance != null)
            {
                pierCapInstance.transform.localScale = originalCapScale;
                
                Vector3 capPos = ToPosition;
                // Shift down by the top offset so the roof of the T is exactly at the node
                capPos.y -= capTopOffset; 
                
                pierCapInstance.transform.position = capPos;
                pierCapInstance.transform.rotation = Quaternion.identity;
            }

            // 2. Shrink the main pillar so it connects exactly to the bottom of the shifted T-cap
            float targetPillarTopY = ToPosition.y;
            if (pierCapInstance != null)
            {
                // ToPosition.y is the top roof. We add the bottom offset to find exactly where the neck ends.
                targetPillarTopY = ToPosition.y - capTopOffset + capBottomOffset; 
            }

            float adjustedDistance = targetPillarTopY - StartPosition.y;
            if (adjustedDistance < 0.05f) adjustedDistance = 0.05f; 

            // 3. Move and stretch the Pillar
            Vector3 midPointAdjusted = StartPosition + (Vector3.up * (adjustedDistance / 2f));
            transform.SetPositionAndRotation(midPointAdjusted, Quaternion.identity);

            float scaleMultiplier = adjustedDistance / baseLength;
            Vector3 newScale = new Vector3(originalScale.x, originalScale.y * scaleMultiplier, originalScale.z);

            foreach (var seg in visualSegments)
            {
                seg.transform.localScale = newScale;
            }
        }
        else
        {
            // Standard Bridge Beam Logic (Wood / Roads)
            Vector3 midPoint = StartPosition + (dir2D / 2f);
            Vector3 angleDir = dir2D;
            if (angleDir.x < 0) angleDir = -angleDir;

            float angle = Mathf.Atan2(angleDir.y, angleDir.x) * Mathf.Rad2Deg;
            transform.SetPositionAndRotation(midPoint, Quaternion.Euler(0, 0, angle));

            float scaleMultiplier = totalDistance / baseLength;
            Vector3 newScale = new Vector3(originalScale.x * scaleMultiplier, originalScale.y, originalScale.z);

            foreach (var seg in visualSegments)
            {
                seg.transform.localScale = newScale;
            }
        }
    }

    public float GetCost()
    {
        if (materialData == null) return 0f;
        int multiplier = materialData.isDualBeam ? 2 : 1;
        return currentLength * materialData.costPerMeter * multiplier;
    }
}