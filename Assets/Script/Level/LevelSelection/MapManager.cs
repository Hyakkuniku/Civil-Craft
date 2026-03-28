using System.Collections.Generic;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    [Header("Nodes")]
    public LevelNode[] levels; 

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

    void Update()
    {
        if (cam == null || levels == null || levels.Length == 0) return;

        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        float minDistance = float.MaxValue;
        string closestRegion = "";

        foreach (LevelNode node in levels) 
        {
            if (node == null || !node.gameObject.activeInHierarchy) continue;
            
            Vector3 screenPos = cam.WorldToScreenPoint(node.transform.position);
            if (screenPos.z < 0) continue; 
            
            Vector2 screenPos2D = new Vector2(screenPos.x, screenPos.y);
            float dist = Vector2.Distance(screenCenter, screenPos2D);
            
            if (dist < minDistance) 
            {
                minDistance = dist;
                closestRegion = node.regionName;
            }
        }

        if (MapUIManager.Instance != null && !string.IsNullOrEmpty(closestRegion)) 
        {
            MapUIManager.Instance.UpdateMapTitle(closestRegion);
        }
    }

    void InitializeLevels()
    {
        List<string> unlockedNames = new List<string> { "Level_1" };
        if (PlayerDataManager.Instance != null && PlayerDataManager.Instance.CurrentData != null)
        {
            unlockedNames = PlayerDataManager.Instance.CurrentData.unlockedLevels;
        }

        for (int i = 0; i < levels.Length; i++)
        {
            levels[i].levelID = i + 1;
            
            // 1. Is it unlocked?
            levels[i].isUnlocked = unlockedNames.Contains(levels[i].sceneName);
            
            // --- THE FIX: Failsafe to ensure Level 1 is ALWAYS unlocked ---
            if (i == 0) levels[i].isUnlocked = true;

            // 2. Is it completed? (Did we unlock the NEXT level?)
            if (i < levels.Length - 1)
            {
                // If the scene name is blank, don't accidentally mark it as completed!
                if (!string.IsNullOrEmpty(levels[i + 1].sceneName))
                {
                    levels[i].isCompleted = unlockedNames.Contains(levels[i + 1].sceneName);
                }
                else
                {
                    levels[i].isCompleted = false;
                }
            }
            else
            {
                levels[i].isCompleted = false; 
            }

            // --- THE FIX: If a level is completed, it MUST be unlocked! ---
            if (levels[i].isCompleted) levels[i].isUnlocked = true;

            levels[i].UpdateVisuals();
        }
    }

    void DrawPathsToUnlockedLevels()
    {
        List<Vector3> allPathPoints = new List<Vector3>();

        for (int i = 0; i < levels.Length - 1; i++)
        {
            if (levels[i].isUnlocked && levels[i + 1].isUnlocked)
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
            else
            {
                break; 
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