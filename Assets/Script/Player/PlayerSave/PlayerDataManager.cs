using UnityEngine;
using System.IO;
using System;

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
    public void AddExp(int amount) { CurrentData.exp += amount; SaveGame(); }
    public void AddBridgeBuilt() { CurrentData.bridgesBuilt++; SaveGame(); }
    
    public void UnlockLevel(string levelName)
    {
        if (!CurrentData.unlockedLevels.Contains(levelName))
        {
            CurrentData.unlockedLevels.Add(levelName);
            SaveGame();
        }
    }

    public void CompleteContract(string contractName)
    {
        if (!CurrentData.completedContracts.Contains(contractName))
        {
            CurrentData.completedContracts.Add(contractName);
            SaveGame();
        }
    }

    // --- NEW: Saves the completed lesson! ---
    public void CompleteLesson(string lessonName)
    {
        if (!CurrentData.completedLessons.Contains(lessonName))
        {
            CurrentData.completedLessons.Add(lessonName);
            SaveGame();
        }
    }

    public void UnlockAlmanac()
    {
        if (!CurrentData.hasAlmanac)
        {
            CurrentData.hasAlmanac = true;
            SaveGame();
            OnAlmanacUnlocked?.Invoke();
        }
    }
}