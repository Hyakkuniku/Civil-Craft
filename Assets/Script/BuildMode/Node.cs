// Node.cs
using UnityEngine;

[ExecuteInEditMode]
public class Node : MonoBehaviour
{
    [Header("Editor snapping (optional)")]
    public bool snapInEditor = true;
    public float snapSize = 1f;

    // Future: visual feedback, occupied check, etc.
    public bool IsOccupied { get; private set; } = false;

    private void Update()
    {
        if (!Application.isPlaying && snapInEditor && transform.hasChanged)
        {
            transform.hasChanged = false;
            SnapPosition();
        }
    }

    public void SnapPosition()
    {
        transform.position = new Vector3(
            Mathf.Round(transform.position.x / snapSize) * snapSize,
            Mathf.Round(transform.position.y / snapSize) * snapSize,
            Mathf.Round(transform.position.z / snapSize) * snapSize
        );
    }

    // Call this when a segment is connected to this node
    public void MarkAsUsed() => IsOccupied = true;
}