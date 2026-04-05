using System.Collections.Generic;
using UnityEngine;

// --- NEW: Data structures for saving bridge coordinates ---
[System.Serializable]
public class SerializableVector3 
{
    public float x, y, z;
    public SerializableVector3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    public Vector3 ToVector3() { return new Vector3(x, y, z); }
}

[System.Serializable]
public class SavedPointData 
{
    public int index;
    public SerializableVector3 position;
    public bool isAnchor;
    public bool originalIsAnchor;
}

[System.Serializable]
public class SavedBarData 
{
    public int startPointIndex;
    public int endPointIndex;
    public string materialName; 
}

[System.Serializable]
public class SavedBridgeData 
{
    public string contractId; 
    public List<SavedPointData> points = new List<SavedPointData>();
    public List<SavedBarData> bars = new List<SavedBarData>();
}

[System.Serializable]
public class PlayerData
{
    public string playerName = "Guest";
    public int gold = 0;
    public int exp = 0;
    public int bridgesBuilt = 0;
    public bool hasAlmanac = false; 
    
    public List<string> unlockedLevels = new List<string> { "Tutorial" };
    public List<string> completedContracts = new List<string>();
    public List<string> completedLessons = new List<string>();
    public List<string> unlockedContractMaterials = new List<string>();
    public List<string> unlockedDoors = new List<string>();

    // --- NEW: Holds all permanently built bridges! ---
    public List<SavedBridgeData> savedBridges = new List<SavedBridgeData>();

    public string GetTitle()
    {
        if (exp < 100) return "Novice Builder";
        if (exp < 300) return "Apprentice Engineer";
        if (exp < 600) return "Journeyman";
        if (exp < 1000) return "Master Architect";
        return "Legendary Engineer";
    }
}