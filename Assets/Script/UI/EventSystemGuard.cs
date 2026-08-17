using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Runtime safety net for scenes that accidentally contain multiple EventSystems.
/// No scene component or prefab setup is required.
/// </summary>
public static class EventSystemGuard
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RemoveDuplicateEventSystems();
    }

    public static void RemoveDuplicateEventSystems()
    {
        EventSystem[] systems = Object.FindObjectsOfType<EventSystem>(true);
        if (systems.Length <= 1) return;

        EventSystem survivor = IsLoadedSceneObject(EventSystem.current)
            ? EventSystem.current
            : null;

        if (survivor == null)
        {
            foreach (EventSystem system in systems)
            {
                if (IsLoadedSceneObject(system) && system.enabled && system.gameObject.activeInHierarchy)
                {
                    survivor = system;
                    break;
                }
            }
        }

        if (survivor == null)
        {
            foreach (EventSystem system in systems)
            {
                if (IsLoadedSceneObject(system))
                {
                    survivor = system;
                    break;
                }
            }
        }

        if (survivor == null) return;

        foreach (EventSystem duplicate in systems)
        {
            if (duplicate == null || duplicate == survivor || !IsLoadedSceneObject(duplicate)) continue;

            duplicate.enabled = false;
            foreach (BaseInputModule inputModule in duplicate.GetComponents<BaseInputModule>())
                inputModule.enabled = false;

            Debug.LogWarning(
                $"Duplicate EventSystem '{duplicate.name}' in scene '{duplicate.gameObject.scene.name}' was removed. " +
                $"Keeping '{survivor.name}'.",
                duplicate);

            if (ContainsOnlyEventSystemComponents(duplicate.gameObject))
                Object.Destroy(duplicate.gameObject);
            else
            {
                foreach (BaseInputModule inputModule in duplicate.GetComponents<BaseInputModule>())
                    Object.Destroy(inputModule);
                Object.Destroy(duplicate);
            }
        }
    }

    private static bool IsLoadedSceneObject(EventSystem system)
    {
        return system != null && system.gameObject.scene.IsValid() && system.gameObject.scene.isLoaded;
    }

    private static bool ContainsOnlyEventSystemComponents(GameObject target)
    {
        foreach (Component component in target.GetComponents<Component>())
        {
            if (component is Transform || component is EventSystem || component is BaseInputModule) continue;
            return false;
        }

        return true;
    }
}
