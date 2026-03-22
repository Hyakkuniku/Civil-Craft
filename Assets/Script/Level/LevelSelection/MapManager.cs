using System.Collections.Generic;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    [Header("Nodes")]
    public LevelNode[] levels; 
    public int highestUnlockedLevel = 2; // Change this to test drawing more lines

    [Header("Line Geometry")]
    public LineRenderer pathRenderer;
    public int curveResolution = 20; // How smooth the curve is
    public float curveAmount = 1.5f; // How far the curve bows out

    void Start()
    {
        InitializeLevels();
        DrawPathsToUnlockedLevels();
    }

    // Notice: There is NO Update() method anymore. 
    // Your Shader Graph's Time node handles all the animation now!

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

        // Only calculate paths up to the highest unlocked level
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
                pointOnCurve.y += 0.05f; // Keep slightly above the ground
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