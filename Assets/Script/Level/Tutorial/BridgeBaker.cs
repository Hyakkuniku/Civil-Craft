using UnityEngine;
using System.Collections.Generic;

public class BridgeBaker : MonoBehaviour
{
    [Header("Visuals")]
    [Tooltip("Drag your transparent Ghost Material here!")]
    public Material transparentMaterial;
    public float zOffsetForGhosts = 0.5f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
        {
            BakeCurrentBridge();
        }
    }

    public void BakeCurrentBridge()
    {
        Bar[] allBars = FindObjectsOfType<Bar>();
        if (allBars.Length == 0) return;

        GameObject masterFolder = new GameObject("BAKED_GHOST_BRIDGE");
        masterFolder.transform.position = Vector3.zero; 
        
        int count = 0;

        // --- 1. CLONE THE EXACT VISUAL MESHES ---
        foreach (Bar b in allBars)
        {
            if (!b.gameObject.activeInHierarchy || b.startPoint == null || b.endPoint == null) continue;

            // Create an empty parent to hold the data for this bar
            GameObject ghostObj = new GameObject("Ghost_" + b.materialData.name);
            ghostObj.transform.SetParent(masterFolder.transform, true);
            ghostObj.transform.position = Vector3.zero;

            // Add the lightweight data script for the Tutorial Director
            GhostSegment seg = ghostObj.AddComponent<GhostSegment>();
            seg.startPos = b.startPoint.transform.position;
            seg.endPos = b.endPoint.transform.position;
            seg.requiredMaterial = b.materialData;

            // Find all the 3D Meshes inside the real bar (the stretched wood/pier pieces)
            foreach (MeshFilter mf in b.GetComponentsInChildren<MeshFilter>())
            {
                // Create a clone of the pure visual mesh
                GameObject visualClone = new GameObject("VisualMesh");
                visualClone.transform.SetParent(ghostObj.transform);
                
                // Copy the exact position, rotation, and stretching scale!
                visualClone.transform.position = mf.transform.position + new Vector3(0, 0, zOffsetForGhosts);
                visualClone.transform.rotation = mf.transform.rotation;
                visualClone.transform.localScale = mf.transform.lossyScale;

                // Add the mesh and paint it transparent
                MeshFilter newMf = visualClone.AddComponent<MeshFilter>();
                newMf.sharedMesh = mf.sharedMesh;

                MeshRenderer newMr = visualClone.AddComponent<MeshRenderer>();
                if (transparentMaterial != null)
                {
                    newMr.material = transparentMaterial;
                }
                else
                {
                    // Fallback to original material if you didn't assign a transparent one
                    MeshRenderer originalRend = mf.GetComponent<MeshRenderer>();
                    if (originalRend != null) newMr.sharedMaterial = originalRend.sharedMaterial;
                }
            }

            count++;
        }

        // --- 2. CLONE THE EXACT POINTS (JOINTS) ---
        Point[] allPoints = FindObjectsOfType<Point>();
        foreach (Point p in allPoints)
        {
            // Skip actual red cliffs, but KEEP road joints connected to Piers
            if (!p.gameObject.activeInHierarchy || p.originalIsAnchor) continue; 

            // Create an empty parent
            GameObject pointObj = new GameObject("Ghost_Point");
            pointObj.transform.SetParent(masterFolder.transform, true);
            pointObj.transform.position = Vector3.zero;

            // Copy the exact visual mesh of the dot
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
                if (transparentMaterial != null)
                {
                    newMr.material = transparentMaterial;
                }
                else
                {
                    MeshRenderer originalRend = mf.GetComponent<MeshRenderer>();
                    if (originalRend != null) newMr.sharedMaterial = originalRend.sharedMaterial;
                }
            }
        }

        Debug.Log($"<color=green><b>[SUCCESS]</b></color> Baked {count} pieces! The visuals are flawlessly cloned. Drag 'BAKED_GHOST_BRIDGE' to your files!");
    }
}