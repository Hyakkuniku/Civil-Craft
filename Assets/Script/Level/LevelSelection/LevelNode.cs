using UnityEngine;
using UnityEngine.EventSystems; // Required to check for UI overlaps

public class LevelNode : MonoBehaviour
{
    public int levelID;
    public bool isUnlocked;
    public bool isCompleted;

    [Header("Visuals")]
    public MeshRenderer meshRenderer;
    public Material lockedMat;
    public Material unlockedMat;
    public Material completedMat;

    void Awake()
    {
        // Auto-assign the MeshRenderer if it wasn't assigned in the Inspector
        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }
    }

    public void UpdateVisuals()
    {
        if (isCompleted) meshRenderer.material = completedMat;
        else if (isUnlocked) meshRenderer.material = unlockedMat;
        else meshRenderer.material = lockedMat;
    }

    void OnMouseDown()
    {
        // FIX 1: Prevent clicking the 3D node if the mouse/finger is over a UI element
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return; 
        }

        // FIX 2: Prevent clicking if the level is locked
        if (!isUnlocked) return;
        
        // Tell the UI Manager to open the popup
        if (UIManager.Instance != null)
        {
            UIManager.Instance.OpenLevelInfo(levelID);
        }
        else
        {
            Debug.LogError("UIManager Instance is missing! Make sure the UIManager script is in your scene.");
        }
    }
}