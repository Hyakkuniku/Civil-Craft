using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class Point : MonoBehaviour
{
    [Tooltip("If false, this node was manually placed and won't be deleted by BarCreator cleanup.")]
    public bool Runtime = true; 
    public List<Bar> ConnectedBars = new List<Bar>();

    public static readonly List<Point> AllPoints = new List<Point>();

    private void OnEnable()
    {
        if (!AllPoints.Contains(this)) AllPoints.Add(this);
    }

    private void OnDisable()
    {
        AllPoints.Remove(this);
    }

    private void Update()
    {
        // Enforce 3D grid snapping in the editor (Preserves Z)
        if (!Runtime && transform.hasChanged)
        {
            transform.hasChanged = false;
            
            Vector3Int snapped = Vector3Int.RoundToInt(transform.position);
            transform.position = new Vector3(snapped.x, snapped.y, snapped.z);
        }
    }
}