using System.Collections.Generic;

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
    
    // --- NEW: Keeps track of which doors you have permanently unlocked ---
    public List<string> unlockedDoors = new List<string>();

    public string GetTitle()
    {
        if (exp < 100) return "Novice Builder";
        if (exp < 300) return "Apprentice Engineer";
        if (exp < 600) return "Journeyman";
        if (exp < 1000) return "Master Architect";
        return "Legendary Engineer";
    }
}