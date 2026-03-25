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

            pierCapInstance.transform.position = Vector3.zero;
            pierCapInstance.transform.rotation = Quaternion.identity;
            
            Renderer capRend = pierCapInstance.GetComponentInChildren<Renderer>();
            if (capRend != null)
            {
                capTopOffset = capRend.bounds.max.y;    
                capBottomOffset = capRend.bounds.min.y; 
            }

            pierCapInstance.transform.localScale = Vector3.zero; 
        }
    }

    public void UpdateCreatingBar(Vector3 ToPosition) 
    {
        if (visualSegments.Count == 0) return;

        Vector3 actualStart = StartPosition;
        Vector3 actualEnd = ToPosition;
        if (materialData.isPier && actualStart.y > actualEnd.y)
        {
            actualStart = ToPosition;
            actualEnd = StartPosition;
        }

        Vector3 flatToPosition = actualEnd;
        flatToPosition.z = actualStart.z; 

        Vector3 dir2D = flatToPosition - actualStart;
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
            float targetPillarTopY = actualEnd.y;
            if (pierCapInstance != null)
            {
                targetPillarTopY = actualEnd.y - capTopOffset + capBottomOffset; 
            }

            float adjustedDistance = targetPillarTopY - actualStart.y;
            if (adjustedDistance < 0.05f) adjustedDistance = 0.05f; 

            // --- BUG FIX: ALWAYS move the parent first BEFORE positioning the child T-Cap ---
            Vector3 midPointAdjusted = actualStart + (Vector3.up * (adjustedDistance / 2f));
            transform.SetPositionAndRotation(midPointAdjusted, Quaternion.identity);

            float scaleMultiplier = adjustedDistance / baseLength;
            Vector3 newScale = new Vector3(originalScale.x, originalScale.y * scaleMultiplier, originalScale.z);

            foreach (var seg in visualSegments)
            {
                seg.transform.localScale = newScale;
            }

            // --- Now that the parent is settled, place the T-Cap in world space ---
            if (pierCapInstance != null)
            {
                pierCapInstance.transform.localScale = originalCapScale;
                
                Vector3 capPos = actualEnd;
                capPos.y -= capTopOffset; 
                
                pierCapInstance.transform.position = capPos;
                pierCapInstance.transform.rotation = Quaternion.identity;
            }
        }
        else
        {
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