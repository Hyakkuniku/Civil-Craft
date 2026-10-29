using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string text) { } }
    public enum RuntimeInitializeLoadType { SubsystemRegistration, BeforeSceneLoad }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public class MonoBehaviour
    {
        public bool enabled = true;
        public GameObject gameObject = new GameObject();
        protected static void DontDestroyOnLoad(GameObject root) { }
    }
    public class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public bool IsChildOf(Transform target) { for (var p = parent; p != null; p = p.parent) if (p == target) return true; return false; }
    }
    public class GameObject
    {
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public Transform transform;
        public SceneManagement.Scene scene = new SceneManagement.Scene();
        public GameObject(string name = "fixture") { transform = new Transform { gameObject = this }; }
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : new() => new T();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Array.Empty<T>();
    }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.Events
{
    public sealed class UnityEvent { public int Calls; public void Invoke() { Calls++; } }
}
namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public class Scene
    {
        public bool isLoaded = true;
        public bool IsValid() => true;
        public UnityEngine.GameObject[] GetRootGameObjects() => Array.Empty<UnityEngine.GameObject>();
    }
    public static class SceneManager { public static event Action<Scene, LoadSceneMode> sceneLoaded { add { } remove { } } }
}
public sealed class ContractSO { public string ContractID; }
public sealed class NPCProgressionSaveData { public bool wasTravelling; public string currentPhaseId; }
public sealed class NPCVisibilitySaveData { public string visibilityId; public bool visible; }
public sealed class PlayerData
{
    public List<string> completedLessons = new List<string>();
    public List<NPCVisibilitySaveData> npcVisibility = new List<NPCVisibilitySaveData>();
    public HashSet<string> completedContracts = new HashSet<string>(StringComparer.Ordinal);
}
public sealed class PlayerDataManager
{
    public static PlayerDataManager Instance;
    public PlayerData CurrentData = new PlayerData();
    public int SaveCalls;
    public bool HasContractCompletionRecord(string id) => CurrentData.completedContracts.Contains(id);
    public bool IsFeatureUnlocked(string feature) => false;
    public bool TryGetNPCProgression(string id, out NPCProgressionSaveData state) { state = null; return false; }
    public void SaveGame() { SaveCalls++; }
}
