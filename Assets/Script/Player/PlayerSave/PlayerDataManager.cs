using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;

public class PlayerDataManager : MonoBehaviour
{
    public static PlayerDataManager Instance { get; private set; }
    public PlayerData CurrentData { get; private set; }
    public Action OnAlmanacUnlocked;
    private string saveFilePath;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); return; }

        saveFilePath = Application.persistentDataPath + "/playerSaveData.json";
        LoadGame();
    }

    public void SaveGame()
    {
        string json = JsonUtility.ToJson(CurrentData, true);
        File.WriteAllText(saveFilePath, json);
    }

    public void LoadGame()
    {
        if (File.Exists(saveFilePath))
        {
            string json = File.ReadAllText(saveFilePath);
            CurrentData = JsonUtility.FromJson<PlayerData>(json);
        }
        else
        {
            CurrentData = new PlayerData();
            CurrentData.playerName = PlayerPrefs.GetString("SavedPlayerName", "Guest"); 
            SaveGame();
        }
    }

    [ContextMenu("Delete Save Data")] 
    public void DeleteSaveData()
    {
        if (File.Exists(saveFilePath)) File.Delete(saveFilePath);
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        CurrentData = new PlayerData();
        CurrentData.playerName = "Guest";
        SaveGame();
    }

    public void AddGold(int amount) { CurrentData.gold += amount; SaveGame(); }
    public bool SpendGold(int amount) 
    { 
        if (CurrentData.gold >= amount) { CurrentData.gold -= amount; SaveGame(); return true; }
        return false;
    }
    public void AddExp(int amount) { CurrentData.exp += amount; SaveGame(); }
    public void AddBridgeBuilt() { CurrentData.bridgesBuilt++; SaveGame(); }
    
    public void UnlockLevel(string levelName) { if (!CurrentData.unlockedLevels.Contains(levelName)) { CurrentData.unlockedLevels.Add(levelName); SaveGame(); } }
    public void CompleteContract(string contractName) { if (!CurrentData.completedContracts.Contains(contractName)) { CurrentData.completedContracts.Add(contractName); SaveGame(); } }
    public void CompleteLesson(string lessonName) { if (!CurrentData.completedLessons.Contains(lessonName)) { CurrentData.completedLessons.Add(lessonName); SaveGame(); } }
    public void UnlockMaterialForContract(string contractName, string materialName) { string key = contractName + "_" + materialName; if (!CurrentData.unlockedContractMaterials.Contains(key)) { CurrentData.unlockedContractMaterials.Add(key); SaveGame(); } }
    public bool IsMaterialUnlockedForContract(string contractName, string materialName) { string key = contractName + "_" + materialName; return CurrentData.unlockedContractMaterials.Contains(key); }
    public void UnlockAlmanac() { if (!CurrentData.hasAlmanac) { CurrentData.hasAlmanac = true; SaveGame(); OnAlmanacUnlocked?.Invoke(); } }
    public void UnlockDoor(string doorID) { if (!CurrentData.unlockedDoors.Contains(doorID)) { CurrentData.unlockedDoors.Add(doorID); SaveGame(); } }
    public bool IsDoorUnlocked(string doorID) { return CurrentData.unlockedDoors.Contains(doorID); }

    // --- NEW: Save/Load Bridge Methods ---
    public void SaveBridgeData(string contractId, List<Point> points, List<Bar> bars)
    {
        CurrentData.savedBridges.RemoveAll(b => b.contractId == contractId); // Clear old save for this level

        SavedBridgeData newSave = new SavedBridgeData { contractId = contractId };
        Dictionary<Point, int> pointToIndex = new Dictionary<Point, int>();
        
        for(int i = 0; i < points.Count; i++)
        {
            pointToIndex[points[i]] = i;
            newSave.points.Add(new SavedPointData {
                index = i,
                position = new SerializableVector3(points[i].transform.position),
                isAnchor = points[i].isAnchor,
                originalIsAnchor = points[i].originalIsAnchor
            });
        }

        foreach(Bar b in bars)
        {
            if(b.startPoint != null && b.endPoint != null && pointToIndex.ContainsKey(b.startPoint) && pointToIndex.ContainsKey(b.endPoint))
            {
                newSave.bars.Add(new SavedBarData {
                    startPointIndex = pointToIndex[b.startPoint],
                    endPointIndex = pointToIndex[b.endPoint],
                    materialName = b.materialData.name
                });
            }
        }

        CurrentData.savedBridges.Add(newSave);
        SaveGame();
    }

    public SavedBridgeData GetSavedBridge(string contractId)
    {
        return CurrentData.savedBridges.Find(b => b.contractId == contractId);
    }

    public void DeleteSavedBridge(string contractId)
    {
        CurrentData.savedBridges.RemoveAll(b => b.contractId == contractId);
        SaveGame();
    }
}