using UnityEngine;
using System.Collections.Generic;

public class BridgeBaker : MonoBehaviour
{
    [Header("Target Location")]
    [Tooltip("Assign the BuildLocation you want to bake. If left empty, it will automatically target the active one.")]
    public BuildLocation targetLocation;

    [Header("Ghost Visuals (For 'B' Key)")]
    [Tooltip("Drag your transparent Ghost Material here!")]
    public Material transparentMaterial;
    public float zOffsetForGhosts = 0.5f;

    void Update()
    {
        // Press B for Ghost (Tutorial) Blueprints
        if (Input.GetKeyDown(KeyCode.B))
        {
            BakeGhostBridge();
        }

        // Press N for Normal (Interactive) Starter Bridges
        if (Input.GetKeyDown(KeyCode.N))
        {
            BakeInteractiveStarterBridge();
        }
    }

    /// <summary>
    /// Bakes a fully interactive bridge that players can build upon, delete, and interact with.
    /// Retains default colors, Point/Bar scripts, and automatically hides nodes outside build mode.
    /// </summary>
    public void BakeInteractiveStarterBridge()
    {
        BuildLocation locToBake = GetLocationToBake();
        if (locToBake == null) return;

        HashSet<Point> connectedPoints = new HashSet<Point>();
        HashSet<Bar> connectedBars = new HashSet<Bar>();
        GatherConnectedBridge(locToBake, connectedPoints, connectedBars);

        if (connectedBars.Count == 0)
        {
            Debug.LogWarning("No active bars found. Draw a bridge first!");
            return;
        }

        GameObject masterFolder = new GameObject($"STARTER_BRIDGE_{locToBake.gameObject.name}");
        masterFolder.transform.position = Vector3.zero;

        Dictionary<Point, Point> pointMap = new Dictionary<Point, Point>();

        // --- 1. CLONE POINTS ---
        foreach (Point p in connectedPoints)
        {
            if (p.originalIsAnchor)
            {
                // Do not duplicate the terrain's permanent red anchors; just map them directly
                pointMap[p] = p;
            }
            else
            {
                // Deep clone the Point GameObject
                GameObject newPointObj = Instantiate(p.gameObject, p.transform.position, p.transform.rotation, masterFolder.transform);
                newPointObj.name = "Starter_Point";
                Point newPoint = newPointObj.GetComponent<Point>();
                
                // Strip out any physics components that might have been added during a live simulation
                foreach (var j in newPointObj.GetComponentsInChildren<Joint>()) DestroyImmediate(j);
                foreach (var rb in newPointObj.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
                foreach (var col in newPointObj.GetComponents<CapsuleCollider>()) DestroyImmediate(col);

                // Reset state
                newPoint.ConnectedBars.Clear();
                newPoint.isSelected = false;
                newPoint.UpdateMaterial();
                
                pointMap[p] = newPoint;
            }
        }

        // --- 2. CLONE BARS & REWIRE GRAPH ---
        int count = 0;
        foreach (Bar b in connectedBars)
        {
            // Deep clone the Bar GameObject
            GameObject newBarObj = Instantiate(b.gameObject, b.transform.position, b.transform.rotation, masterFolder.transform);
            newBarObj.name = $"Starter_{b.materialData.name}";
            Bar newBar = newBarObj.GetComponent<Bar>();

            // Strip out any physics components added during a live simulation
            foreach (var j in newBarObj.GetComponentsInChildren<Joint>()) DestroyImmediate(j);
            foreach (var rb in newBarObj.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
            if (newBarObj.GetComponent<BarStressHandler>()) DestroyImmediate(newBarObj.GetComponent<BarStressHandler>());
            
            // Strip out root colliders added by the physics manager (leave visual child colliders alone)
            foreach (var col in newBarObj.GetComponents<BoxCollider>())
            {
                if (col.gameObject == newBarObj) DestroyImmediate(col);
            }

            // Rewire the start and end points to use our newly cloned points
            newBar.startPoint = pointMap[b.startPoint];
            newBar.endPoint = pointMap[b.endPoint];
            newBar.StartPosition = newBar.startPoint.transform.position;
            
            // Remove any selection highlighting
            newBar.isHighlighted = false;
            newBar.SetHighlight(false, null);

            // Tell the new Points that this Bar is connected to them
            if (!newBar.startPoint.ConnectedBars.Contains(newBar)) newBar.startPoint.ConnectedBars.Add(newBar);
            if (!newBar.endPoint.ConnectedBars.Contains(newBar)) newBar.endPoint.ConnectedBars.Add(newBar);

            count++;
        }

        Debug.Log($"<color=cyan><b>[SUCCESS]</b></color> Baked {count} interactive pieces for {locToBake.gameObject.name}! Drag 'STARTER_BRIDGE_{locToBake.gameObject.name}' to your files before exiting Play Mode!");
    }

    /// <summary>
    /// Bakes a transparent, non-interactive "Ghost" blueprint used purely for the Tutorial tracing system.
    /// </summary>
    public void BakeGhostBridge()
    {
        BuildLocation locToBake = GetLocationToBake();
        if (locToBake == null) return;

        HashSet<Point> connectedPoints = new HashSet<Point>();
        HashSet<Bar> connectedBars = new HashSet<Bar>();
        GatherConnectedBridge(locToBake, connectedPoints, connectedBars);

        if (connectedBars.Count == 0)
        {
            Debug.LogWarning("No active bars found. Draw a bridge first!");
            return;
        }

        GameObject masterFolder = new GameObject($"BAKED_GHOST_BRIDGE_{locToBake.gameObject.name}");
        masterFolder.transform.position = Vector3.zero; 
        
        int count = 0;

        // --- 1. CLONE THE EXACT VISUAL MESHES FOR BARS ---
        foreach (Bar b in connectedBars)
        {
            if (b.startPoint == null || b.endPoint == null) continue;

            GameObject ghostObj = new GameObject("Ghost_" + b.materialData.name);
            ghostObj.transform.SetParent(masterFolder.transform, true);
            ghostObj.transform.position = Vector3.zero;

            GhostSegment seg = ghostObj.AddComponent<GhostSegment>();
            seg.startPos = b.startPoint.transform.position;
            seg.endPos = b.endPoint.transform.position;
            seg.requiredMaterial = b.materialData;

            foreach (MeshFilter mf in b.GetComponentsInChildren<MeshFilter>())
            {
                GameObject visualClone = new GameObject("VisualMesh");
                visualClone.transform.SetParent(ghostObj.transform);
                
                visualClone.transform.position = mf.transform.position + new Vector3(0, 0, zOffsetForGhosts);
                visualClone.transform.rotation = mf.transform.rotation;
                visualClone.transform.localScale = mf.transform.lossyScale;

                MeshFilter newMf = visualClone.AddComponent<MeshFilter>();
                newMf.sharedMesh = mf.sharedMesh;

                MeshRenderer newMr = visualClone.AddComponent<MeshRenderer>();
                if (transparentMaterial != null) newMr.material = transparentMaterial;
                else
                {
                    MeshRenderer originalRend = mf.GetComponent<MeshRenderer>();
                    if (originalRend != null) newMr.sharedMaterial = originalRend.sharedMaterial;
                }
            }
            count++;
        }

        // --- 2. CLONE THE EXACT VISUAL MESHES FOR POINTS ---
        foreach (Point p in connectedPoints)
        {
            if (p.originalIsAnchor) continue; 

            GameObject pointObj = new GameObject("Ghost_Point");
            pointObj.transform.SetParent(masterFolder.transform, true);
            pointObj.transform.position = Vector3.zero;

            MeshFilter mf = p.GetComponentInChildren<MeshFilter>();
            if (mf != null)
            {
                GameObject visualClone = new GameObject("VisualMesh");
                visualClone.transform.SetParent(pointObj.transform);
                
                visualClone.transform.position = mf.transform.position + new Vector3(0, 0, zOffsetForGhosts);
                visualClone.transform.rotation = mf.transform.rotation;
                visualClone.transform.localScale = mf.transform.lossyScale;

                MeshFilter newMf = visualClone.AddComponent<MeshFilter>();
                newMf.sharedMesh = mf.sharedMesh;

                MeshRenderer newMr = visualClone.AddComponent<MeshRenderer>();
                if (transparentMaterial != null) newMr.material = transparentMaterial;
                else
                {
                    MeshRenderer originalRend = mf.GetComponent<MeshRenderer>();
                    if (originalRend != null) newMr.sharedMaterial = originalRend.sharedMaterial;
                }
            }
        }

        Debug.Log($"<color=green><b>[SUCCESS]</b></color> Baked {count} ghosts for {locToBake.gameObject.name}! Drag 'BAKED_GHOST_BRIDGE_{locToBake.gameObject.name}' to your files before exiting Play Mode!");
    }

    // --- HELPER METHODS ---

    private BuildLocation GetLocationToBake()
    {
        BuildLocation loc = targetLocation;
        if (loc == null && GameManager.Instance != null)
        {
            loc = GameManager.Instance.ActiveBuildLocation;
        }

        if (loc == null)
        {
            Debug.LogWarning("No Build Location specified or active. Cannot bake bridge.");
        }
        return loc;
    }

    private void GatherConnectedBridge(BuildLocation loc, HashSet<Point> points, HashSet<Bar> bars)
    {
        Queue<Point> queue = new Queue<Point>();

        foreach (Point p in loc.startingAnchors)
        {
            if (p != null && p.gameObject.activeInHierarchy) { points.Add(p); queue.Enqueue(p); }
        }

        foreach (Point p in loc.endingAnchors)
        {
            if (p != null && p.gameObject.activeInHierarchy && !points.Contains(p)) { points.Add(p); queue.Enqueue(p); }
        }

        while (queue.Count > 0)
        {
            Point current = queue.Dequeue();
            foreach (Bar b in current.ConnectedBars)
            {
                if (b != null && b.gameObject.activeInHierarchy && !bars.Contains(b))
                {
                    bars.Add(b);
                    Point neighbor = (b.startPoint == current) ? b.endPoint : b.startPoint;
                    
                    if (neighbor != null && neighbor.gameObject.activeInHierarchy && !points.Contains(neighbor))
                    {
                        points.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }
    }
}