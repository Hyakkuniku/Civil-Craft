using UnityEngine;

/// <summary>Development-only entry point shared by every scene's authored Settings button.</summary>
public static class DevelopmentSettingsAccess
{
    public const string ResourcePath = "UI/DeveloperDebugSystem";

    public static bool IsSupportedBuild
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }
    }

    public static bool OpenMenu()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        DeveloperDebugManager manager = DeveloperDebugManager.Instance;
        if (manager == null)
        {
            // Main Menu and direct Bhan's House starts do not contain the Canyon
            // developer system. Create the shared authored copy only on demand.
            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null || prefab.GetComponent<DeveloperDebugManager>() == null)
            {
                Debug.LogWarning("The authored developer menu is missing. Run the Settings Dev button setup.");
                return false;
            }
            UnityEngine.Object.Instantiate(prefab);
            manager = DeveloperDebugManager.Instance;
        }

        if (manager == null || !manager.IsAvailable) return false;
        if (!manager.IsMenuOpen) manager.OpenMenu();
        return manager.IsMenuOpen;
#else
        // A serialized callback cannot bypass the release-build visibility gate.
        return false;
#endif
    }
}
