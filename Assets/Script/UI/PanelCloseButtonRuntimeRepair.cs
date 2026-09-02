using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reconnects modal close buttons after scene hierarchy/prefab changes.
/// Persistent Inspector events remain the primary wiring; these listeners are a
/// runtime safety net so a copied panel cannot trap the player behind its UI.
/// </summary>
public static class PanelCloseButtonRuntimeRepair
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RepairSceneButtons()
    {
        PauseManager pause = Object.FindObjectOfType<PauseManager>(true);
        AlmanacManager almanac = Object.FindObjectOfType<AlmanacManager>(true);
        AchievementUIManager achievements = Object.FindObjectOfType<AchievementUIManager>(true);
        ShopManager shop = Object.FindObjectOfType<ShopManager>(true);
        SettingsManager settings = Object.FindObjectOfType<SettingsManager>(true);
        LiveLoadVehicle vehicle = Object.FindObjectOfType<LiveLoadVehicle>(true);

        foreach (Button button in Object.FindObjectsOfType<Button>(true))
        {
            string closeMethod = FindCloseMethod(button);
            if (string.IsNullOrEmpty(closeMethod))
                continue;

            MakeClickable(button);

            if (closeMethod == nameof(PauseManager.ResumeGame) && pause != null)
                button.onClick.AddListener(pause.ResumeGame);
            else if (closeMethod == nameof(AlmanacManager.CloseAlmanac) && almanac != null)
                button.onClick.AddListener(almanac.CloseAlmanac);
            else if (closeMethod == nameof(AchievementUIManager.ClosePanel) && achievements != null)
                button.onClick.AddListener(achievements.ClosePanel);
            else if (closeMethod == nameof(ShopManager.CloseShop) && shop != null)
                button.onClick.AddListener(shop.CloseShop);
            else if (closeMethod == nameof(SettingsManager.CloseSettings) && settings != null)
                button.onClick.AddListener(settings.CloseSettings);
            else if (closeMethod == nameof(LiveLoadVehicle.CloseInfoPanel))
            {
                if (vehicle != null)
                    button.onClick.AddListener(vehicle.CloseInfoPanel);
                else
                    button.onClick.AddListener(() => CloseContainingPanel(button.transform));
            }
            // LessonUIManager already binds its serialized close button in Awake.
        }
    }

    private static string FindCloseMethod(Button button)
    {
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            string method = button.onClick.GetPersistentMethodName(i);
            if (method == nameof(PauseManager.ResumeGame) ||
                method == nameof(AlmanacManager.CloseAlmanac) ||
                method == nameof(AchievementUIManager.ClosePanel) ||
                method == nameof(ShopManager.CloseShop) ||
                method == nameof(SettingsManager.CloseSettings) ||
                method == nameof(LessonUIManager.CloseLesson) ||
                method == nameof(LiveLoadVehicle.CloseInfoPanel))
            {
                return method;
            }
        }

        return string.Empty;
    }

    private static void MakeClickable(Button button)
    {
        button.interactable = true;
        if (button.targetGraphic != null)
            button.targetGraphic.raycastTarget = true;

        CanvasGroup[] groups = button.GetComponentsInParent<CanvasGroup>(true);
        foreach (CanvasGroup group in groups)
        {
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }

    private static void CloseContainingPanel(Transform child)
    {
        Transform panel = child;
        while (panel != null && panel.name != "Panel_Vehicle")
            panel = panel.parent;

        if (panel == null)
            return;

        if (UIPanelCoordinator.Instance != null)
            UIPanelCoordinator.Instance.ClosePanel(panel.gameObject);
        else
            panel.gameObject.SetActive(false);
    }
}
