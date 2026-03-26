using System.Collections.Generic;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    [Header("Nodes")]
    public LevelNode[] levels; 
    public int highestUnlockedLevel = 2; 

    [Header("Line Geometry")]
    public LineRenderer pathRenderer;
    public int curveResolution = 20; 
    public float curveAmount = 1.5f; 

    private Camera cam;

    void Awake()
    {
        cam = Camera.main;
    }

    void Start()
    {
        InitializeLevels();
        DrawPathsToUnlockedLevels();
    }

    // --- THE FIX: We now use Screen Space to find the closest node! ---
    void Update()
    {
        if (cam == null || levels == null || levels.Length == 0) return;

        // 1. Find the exact center of your screen in 2D pixels
        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        
        float minDistance = float.MaxValue;
        string closestRegion = "";

        // 2. Loop through all nodes and find which one is closest to the center of the screen
        foreach (LevelNode node in levels) 
        {
            if (node == null || !node.gameObject.activeInHierarchy) continue;
            
            // Convert the 3D node into 2D screen coordinates
            Vector3 screenPos = cam.WorldToScreenPoint(node.transform.position);
            
            // If the node is behind the camera, ignore it
            if (screenPos.z < 0) continue; 
            
            Vector2 screenPos2D = new Vector2(screenPos.x, screenPos.y);
            float dist = Vector2.Distance(screenCenter, screenPos2D);
            
            if (dist < minDistance) 
            {
                minDistance = dist;
                closestRegion = node.regionName;
            }
        }

        // 3. Update the UI Title continuously!
        if (MapUIManager.Instance != null && !string.IsNullOrEmpty(closestRegion)) 
        {
            MapUIManager.Instance.UpdateMapTitle(closestRegion);
        }
    }

    void InitializeLevels()
    {
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i].levelID = i + 1;
            
            if (i < highestUnlockedLevel - 1)
            {
                levels[i].isCompleted = true;
                levels[i].isUnlocked = true;
            }
            else if (i == highestUnlockedLevel - 1)
            {
                levels[i].isCompleted = false;
                levels[i].isUnlocked = true;
            }
            else
            {
                levels[i].isUnlocked = false;
            }

            levels[i].UpdateVisuals();
        }
    }

    void DrawPathsToUnlockedLevels()
    {
        List<Vector3> allPathPoints = new List<Vector3>();
        int pathsToDraw = Mathf.Clamp(highestUnlockedLevel - 1, 0, levels.Length - 1);

        for (int i = 0; i < pathsToDraw; i++)
        {
            Vector3 startPos = levels[i].transform.position;
            Vector3 endPos = levels[i + 1].transform.position;
            
            Vector3 midPoint = (startPos + endPos) / 2f;
            Vector3 direction = (endPos - startPos).normalized;
            Vector3 perpendicular = new Vector3(-direction.z, 0, direction.x); 

            float directionMultiplier = (i % 2 == 0) ? 1f : -1f;
            Vector3 controlPoint = midPoint + (perpendicular * curveAmount * directionMultiplier);

            for (int j = 0; j <= curveResolution; j++)
            {
                if (j == 0 && i > 0) continue; 
                
                float t = j / (float)curveResolution;
                Vector3 pointOnCurve = CalculateQuadraticBezierPoint(t, startPos, controlPoint, endPos);
                pointOnCurve.y += 0.05f; 
                allPathPoints.Add(pointOnCurve);
            }
        }

        if (allPathPoints.Count == 0)
        {
            pathRenderer.positionCount = 0;
            return;
        }

        pathRenderer.positionCount = allPathPoints.Count;
        pathRenderer.SetPositions(allPathPoints.ToArray());
    }

    Vector3 CalculateQuadraticBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        Vector3 p = uu * p0;
        p += 2 * u * t * p1;
        p += tt * p2;
        return p;
    }
}