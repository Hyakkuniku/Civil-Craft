using UnityEngine;

public class LevelNode : MonoBehaviour
{
    [Header("Level Data")]
    public int levelID;
    public string levelTitle = "The First Gap";
    public string regionName = "Region 1: The Ravine"; 
    
    [TextArea(3, 5)] 
    public string levelDescription = "A simple gap. Wood is cheap, but steel is strong.";

    [Header("Progression State")]
    public bool isUnlocked;
    public bool isCompleted;

    [Header("Visuals")]
    public MeshRenderer meshRenderer;
    public Material lockedMat;
    public Material unlockedMat;
    public Material completedMat;

    void Awake()
    {
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
    }

    public void UpdateVisuals()
    {
        if (isCompleted) meshRenderer.material = completedMat;
        else if (isUnlocked) meshRenderer.material = unlockedMat;
        else meshRenderer.material = lockedMat;
    }

    public void OnNodeTapped()
    {
        if (!isUnlocked) 
        {
            Debug.Log("Level " + levelID + " is locked!");
            return;
        }
        
        if (MapUIManager.Instance != null)
        {
            MapUIManager.Instance.OpenLevelInfo(levelID, levelTitle, levelDescription);
        }
    }
}