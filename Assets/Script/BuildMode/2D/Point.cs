using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class Point : MonoBehaviour
{
    public bool Runtime = true; 
    public bool isAnchor = false;

    public Material defaultMaterial;
    public Material anchorMaterial;

    public List<Bar> ConnectedBars = new List<Bar>();
    public static readonly List<Point> AllPoints = new List<Point>();

    private Renderer pointRenderer;

    // ADDED: Snapshot variables for Restarting
    [HideInInspector] public Vector3 preSimPos;
    [HideInInspector] public Transform preSimParent;

    private void Awake()
    {
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
        if (!Runtime && transform.hasChanged)
        {
            transform.hasChanged = false;
            Vector3Int snapped = Vector3Int.RoundToInt(transform.position);
            transform.position = new Vector3(snapped.x, snapped.y, snapped.z);
        }
    }

    private void OnValidate()
    {
        UpdateMaterial();
    }

    public void UpdateMaterial()
    {
        if (pointRenderer == null) pointRenderer = GetComponentInChildren<Renderer>();
        
        if (pointRenderer != null)
        {
            if (isAnchor && anchorMaterial != null)
                pointRenderer.sharedMaterial = anchorMaterial;
            else if (!isAnchor && defaultMaterial != null)
                pointRenderer.sharedMaterial = defaultMaterial;
        }
    }

    private void OnDrawGizmos()
    {
        if (isAnchor)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawCube(transform.position, Vector3.one * 0.4f);
        }
        else if (!Runtime)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(transform.position, 0.2f);
        }
    }
}