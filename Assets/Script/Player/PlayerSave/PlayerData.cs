using System.Collections.Generic;
using UnityEngine;

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
    public int schemaVersion = 1;
    public string contractId; 
    public float totalSpent;
    public float maxStress;
    
    public List<SavedPointData> points = new List<SavedPointData>();
    public List<SavedBarData> bars = new List<SavedBarData>();
}

[System.Serializable]
public class NPCProgressionSaveData
{
    [Tooltip("Stable ID of the NPC progression sequence that owns this record.")]
    public string progressionId;

    [Tooltip("Saved list index. The phase ID is preferred when phases are reordered.")]
    public int currentPhaseIndex;

    [Tooltip("Stable phase ID used to survive Inspector list reordering.")]
    public string currentPhaseId;

    [Tooltip("True when the game was saved after travel began but before arrival.")]
    public bool wasTravelling;
}

[System.Serializable]
public class TrackedTask
{
    public string title;
    public string description;
    public bool isTutorial;
    public string contractName; 
    public float budget;
    public float weight;

    public bool isReadyToTurnIn;
    public bool isCompleted;

    public int pendingGold;
    public int pendingExp;
    
    public string targetWaypointName; 
}

[System.Serializable]
public class PlayerData
{
    public string playerName = "Guest";
    public int gold = 0;
    public int exp = 0;
    public bool hasAlmanac = false; 
    public bool hasUnreadAlmanacUnlockAlert = false;
    public bool hasUnreadContractsAlert = false;
    public bool hasUnreadLessonsAlert = false;
    public bool hasUnreadObjectiveAlert = false;

    public int lifetimeBridgesBuilt = 0;
    public int lifetimeGoldEarned = 0;
    public int lifetimeGoldSpent = 0;
    public int lifetimeContractsCompleted = 0;
    
    public string lastSavedScene = "";
    public SerializableVector3 lastSavedPosition;

    public string equippedHatID = ""; 

    // --- NEW: UI Unlock Trackers ---
    public bool hasUnlockedContractsTab = false;
    public bool hasUnlockedLessonsTab = false;
    public bool hasUnlockedObjectiveTracker = false; 

    public List<string> unlockedAchievements = new List<string>();
    public List<TrackedTask> activeQuests = new List<TrackedTask>(); 
    public List<string> unlockedLevels = new List<string> { "Tutorial" };
    public List<string> completedContracts = new List<string>();
    // LessonData archive IDs. This is intentionally separate from completedLessons,
    // which controls TutorialSequence prerequisites and replay behavior.
    public List<string> unlockedLessonIds = new List<string>();
    public List<string> completedLessons = new List<string>();
    public List<string> unlockedContractMaterials = new List<string>();
    public List<string> unlockedDoors = new List<string>();

    public List<SavedBridgeData> savedBridges = new List<SavedBridgeData>();
    public List<NPCProgressionSaveData> npcProgressions = new List<NPCProgressionSaveData>();

    public string GetTitle()
    {
        if (exp < 100) return "Novice Builder";
        if (exp < 300) return "Apprentice Engineer";
        if (exp < 600) return "Journeyman";
        if (exp < 1000) return "Master Architect";
        return "Legendary Engineer";
    }
}
