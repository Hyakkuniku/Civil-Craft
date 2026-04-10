using UnityEngine;

public class GhostSegment : MonoBehaviour
{
    public Vector3 startPos;
    public Vector3 endPos;
    public BridgeMaterialSO requiredMaterial;
    
    [HideInInspector] public bool isCovered = false;
}