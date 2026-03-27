using System.Collections.Generic;

[System.Serializable]
public class PlayerData
{
    public string playerName = "Guest";
    public int gold = 0;
    public int exp = 0;
    public int bridgesBuilt = 0;
    
    // Tracks if the player has picked up the physical book
    public bool hasAlmanac = false; 
    
    public List<string> unlockedLevels = new List<string> { "Level_1" };

    public string GetTitle()
    {
        if (exp < 100) return "Novice Builder";
        if (exp < 300) return "Apprentice Engineer";
        if (exp < 600) return "Journeyman";
        if (exp < 1000) return "Master Architect";
        return "Legendary Engineer";
    }
}