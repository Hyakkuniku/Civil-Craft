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

                if (i == 0)
                {
                    originalScale = newSegment.transform.localScale;
                    
                    var renderer = newSegment.GetComponentInChildren<Renderer>();
                    if (renderer != null)
                    {
                        baseLength = renderer.bounds.size.x;
                        visualSize = renderer.bounds.size; 
                        if (baseLength <= 0f) baseLength = 1f; 
                    }
                }
                
                newSegment.transform.localScale = Vector3.zero;
                visualSegments.Add(newSegment);
            }
        }
    }

    public void UpdateCreatingBar(Vector3 ToPosition) 
    {
        if (visualSegments.Count == 0) return;

        // --- THE FIX: Force the bar to stay perfectly flat on the 2D Z-plane! ---
        Vector3 flatToPosition = ToPosition;
        flatToPosition.z = StartPosition.z; 

        Vector3 dir2D = flatToPosition - StartPosition;
        float totalDistance = dir2D.magnitude;
        
        currentLength = totalDistance;
        
        if (totalDistance < 0.01f) 
        {
            foreach (var seg in visualSegments) seg.transform.localScale = Vector3.zero;
            return;
        }

        Vector3 midPoint = StartPosition + (dir2D / 2f);
        
        // --- THE FIX: Standard 2D Clock Rotation ---
        float angle = Mathf.Atan2(dir2D.y, dir2D.x) * Mathf.Rad2Deg;
        transform.SetPositionAndRotation(midPoint, Quaternion.Euler(0, 0, angle));

        float scaleMultiplier = totalDistance / baseLength;
        Vector3 newScale = new Vector3(originalScale.x * scaleMultiplier, originalScale.y, originalScale.z);

        foreach (var seg in visualSegments)
        {
            seg.transform.localScale = newScale;
        }
    }

    public float GetCost()
    {
        if (materialData == null) return 0f;
        int multiplier = materialData.isDualBeam ? 2 : 1;
        return currentLength * materialData.costPerMeter * multiplier;
    }
}