using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class Point : MonoBehaviour
{
    [Tooltip("If false, this node was manually placed and won't be deleted by BarCreator cleanup.")]
    public bool Runtime = true; 

    [Header("Physics")]
    [Tooltip("If true, this point is locked in place and acts as a foundation/anchor for the bridge.")]
    public bool isAnchor = false;

    [Header("Visuals (Player Facing)")]
    [Tooltip("Material used for normal, placeable points.")]
    public Material defaultMaterial;
    [Tooltip("Material used to show the player this is an immovable anchor.")]
    public Material anchorMaterial;

    public List<Bar> ConnectedBars = new List<Bar>();

    public static readonly List<Point> AllPoints = new List<Point>();

    private Renderer pointRenderer;

    private void Awake()
    {
        // CHANGED: Now looks for the Renderer on the child object
        pointRenderer = GetComponentInChildren<Renderer>();
    }

    private void OnEnable()
    {
        if (!AllPoints.Contains(this)) AllPoints.Add(this);
        UpdateMaterial();
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

    // Automatically called when you change values in the Inspector
    private void OnValidate()
    {
        UpdateMaterial();
    }

    // Swaps the actual mesh material so the player can see it
    public void UpdateMaterial()
    {
        // CHANGED: Also updated here to ensure it finds the child's Renderer
        if (pointRenderer == null) pointRenderer = GetComponentInChildren<Renderer>();
        
        if (pointRenderer != null)
        {
            if (isAnchor && anchorMaterial != null)
            {
                pointRenderer.sharedMaterial = anchorMaterial;
            }
            else if (!isAnchor && defaultMaterial != null)
            {
                pointRenderer.sharedMaterial = defaultMaterial;
            }
        }
    }

    // VISUAL AID: Draws shapes in the Unity Editor so you can easily see your anchors
    private void OnDrawGizmos()
    {
        if (isAnchor)
        {
            // Anchors show as red cubes
            Gizmos.color = Color.red;
            Gizmos.DrawCube(transform.position, Vector3.one * 0.4f);
        }
        else if (!Runtime)
        {
            // Manual non-anchor points show as yellow spheres
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(transform.position, 0.2f);
        }
    }
}