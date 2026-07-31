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
                pointMap[p] = p;
            }
            else
            {
                GameObject newPointObj = Instantiate(p.gameObject, p.transform.position, p.transform.rotation, masterFolder.transform);
                newPointObj.name = "Starter_Point";
                Point newPoint = newPointObj.GetComponent<Point>();
                
                foreach (var j in newPointObj.GetComponentsInChildren<Joint>()) DestroyImmediate(j);
                foreach (var rb in newPointObj.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
                foreach (var col in newPointObj.GetComponents<CapsuleCollider>()) DestroyImmediate(col);

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
            GameObject newBarObj = Instantiate(b.gameObject, b.transform.position, b.transform.rotation, masterFolder.transform);
            newBarObj.name = $"Starter_{b.materialData.name}";
            Bar newBar = newBarObj.GetComponent<Bar>();

            foreach (var j in newBarObj.GetComponentsInChildren<Joint>()) DestroyImmediate(j);
            foreach (var rb in newBarObj.GetComponentsInChildren<Rigidbody>()) DestroyImmediate(rb);
            if (newBarObj.GetComponent<BarStressHandler>()) DestroyImmediate(newBarObj.GetComponent<BarStressHandler>());
            
            foreach (var col in newBarObj.GetComponents<BoxCollider>())
            {
                if (col.gameObject == newBarObj) DestroyImmediate(col);
            }

            newBar.startPoint = pointMap[b.startPoint];
            newBar.endPoint = pointMap[b.endPoint];
            
            // --- CRITICAL FIX: Save both coordinates so the Prefab can fix itself later ---
            newBar.StartPosition = newBar.startPoint.transform.position;
            newBar.EndPosition = newBar.endPoint.transform.position; 
            
            newBar.isHighlighted = false;
            newBar.SetHighlight(false, null);

            if (!newBar.startPoint.ConnectedBars.Contains(newBar)) newBar.startPoint.ConnectedBars.Add(newBar);
            if (!newBar.endPoint.ConnectedBars.Contains(newBar)) newBar.endPoint.ConnectedBars.Add(newBar);

            count++;
        }

        Debug.Log($"<color=cyan><b>[SUCCESS]</b></color> Baked {count} interactive pieces! Drag 'STARTER_BRIDGE_{locToBake.gameObject.name}' to your files before exiting Play Mode!");
    }

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

        Debug.Log($"<color=green><b>[SUCCESS]</b></color> Baked {count} ghosts! Drag 'BAKED_GHOST_BRIDGE_{locToBake.gameObject.name}' to your files before exiting Play Mode!");
    }

    private BuildLocation GetLocationToBake()
    {
        BuildLocation loc = targetLocation;
        if (loc == null && GameManager.Instance != null)
        {
            loc = GameManager.Instance.ActiveBuildLocation;
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