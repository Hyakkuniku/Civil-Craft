using UnityEngine;

public class BuildGridManager : MonoBehaviour
{
    [Header("Grid Settings")]
    public Vector3 gridOffset = Vector3.zero;  // Offset from BuildLocation position
    public bool autoSizeGridToBounds = true;
    
    [Header("Visual")]
    public bool showGridGizmos = true;

    private void OnDrawGizmosSelected()
    {
        if (!showGridGizmos) return;

        Gizmos.color = Color.yellow;
        Bounds bounds = GetComponent<Collider>()?.bounds ?? new Bounds(transform.position, Vector3.one * 10f);
        
        Vector3 size = bounds.size;
        Vector3 center = bounds.center + gridOffset;
        
        // Draw grid gizmos
        float cellSize = 1f;
        int width = Mathf.RoundToInt(size.x / cellSize);
        int depth = Mathf.RoundToInt(size.z / cellSize);
        
        for (int x = 0; x <= width; x++)
        {
            Gizmos.DrawLine(
                center + new Vector3((x - width * 0.5f) * cellSize, 0, -size.z * 0.5f),
                center + new Vector3((x - width * 0.5f) * cellSize, 0, size.z * 0.5f)
            );
        }
        
        for (int z = 0; z <= depth; z++)
        {
            Gizmos.DrawLine(
                center + new Vector3(-size.x * 0.5f, 0, (z - depth * 0.5f) * cellSize),
                center + new Vector3(size.x * 0.5f, 0, (z - depth * 0.5f) * cellSize)
            );
        }
    }
}