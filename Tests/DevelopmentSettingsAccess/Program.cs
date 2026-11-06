using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static void Main()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Check(DevelopmentSettingsAccess.IsSupportedBuild, "Development entry must be available.");
        var existing = new DeveloperDebugManager();
        DeveloperDebugManager.Instance = existing;
        Check(DevelopmentSettingsAccess.OpenMenu() && existing.OpenCount == 1, "Existing menu did not open.");
        Check(Resources.LoadCount == 0 && UnityEngine.Object.InstantiateCount == 0, "Existing menu was duplicated.");
        Check(DevelopmentSettingsAccess.OpenMenu() && existing.OpenCount == 1, "Repeated click reopened the same menu.");

        existing.IsAvailable = false;
        Check(!DevelopmentSettingsAccess.OpenMenu(), "Unavailable menu opened.");
        DeveloperDebugManager.Instance = null;
        Check(!DevelopmentSettingsAccess.OpenMenu() && UnityEngine.Object.InstantiateCount == 0,
            "Missing prefab should fail without creating an object.");
        Check(Debug.WarningCount == 1, "Missing prefab should give a useful report.");
        Resources.Prefab = new GameObject();
        Check(!DevelopmentSettingsAccess.OpenMenu() && UnityEngine.Object.InstantiateCount == 0,
            "Invalid prefab should fail without creating an object.");
        Resources.Prefab.Manager = new DeveloperDebugManager();
        Check(DevelopmentSettingsAccess.OpenMenu(), "Lazy menu creation failed.");
        Check(DeveloperDebugManager.Instance != null && DeveloperDebugManager.Instance.OpenCount == 1,
            "Lazy instance was not opened.");
        Check(UnityEngine.Object.InstantiateCount == 1 && Resources.LastPath == "UI/DeveloperDebugSystem",
            "Unexpected resource or duplicate menu creation.");
        Check(DevelopmentSettingsAccess.OpenMenu() && UnityEngine.Object.InstantiateCount == 1,
            "A later click created a second menu.");
#else
        Check(!DevelopmentSettingsAccess.IsSupportedBuild, "Release entry must be hidden.");
        var existing = new DeveloperDebugManager { IsAvailable = true };
        DeveloperDebugManager.Instance = existing;
        Resources.Prefab = new GameObject { Manager = existing };
        Check(!DevelopmentSettingsAccess.OpenMenu() && existing.OpenCount == 0,
            "Release callback bypassed the button gate.");
        DeveloperDebugManager.Instance = null;
        Check(!DevelopmentSettingsAccess.OpenMenu(), "Release callback lazily created a developer menu.");
        Check(Resources.LoadCount == 0 && UnityEngine.Object.InstantiateCount == 0 && Debug.WarningCount == 0,
            "Release path must not load, instantiate or warn.");
#endif
        Console.WriteLine($"PASS: {checks} development Settings access checks.");
    }
}

public sealed class DeveloperDebugManager
{
    public static DeveloperDebugManager Instance;
    public bool IsAvailable = true;
    public bool IsMenuOpen;
    public int OpenCount;
    public void OpenMenu() { OpenCount++; IsMenuOpen = true; }
}

namespace UnityEngine
{
    public sealed class GameObject
    {
        public DeveloperDebugManager Manager;
        public T GetComponent<T>() where T : class => Manager as T;
    }
    public static class Object
    {
        public static int InstantiateCount;
        public static GameObject Instantiate(GameObject source)
        {
            InstantiateCount++;
            DeveloperDebugManager.Instance = new DeveloperDebugManager();
            return source;
        }
    }
    public static class Resources
    {
        public static GameObject Prefab;
        public static int LoadCount;
        public static string LastPath;
        public static T Load<T>(string path) where T : class
        {
            LoadCount++;
            LastPath = path;
            return Prefab as T;
        }
    }
    public static class Debug
    {
        public static int WarningCount;
        public static void LogWarning(string message) => WarningCount++;
    }
}
