#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AchievementPopupSceneSetup
{
    private const string PrefabFolder = "Assets/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/AchievementPopupCanvas.prefab";
    private const string SessionKey = "CivilCraft.AchievementPopupSceneSetup.v1";

    private static readonly string[] TargetScenes =
    {
        "Assets/Scenes/Main Menu.unity",
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    static AchievementPopupSceneSetup()
    {
        EditorApplication.delayCall += RunAutomaticSetupOnce;
    }

    [MenuItem("Tools/Civil Craft/Setup Achievement Popup In Scenes")]
    public static void SetupAchievementPopupInScenes()
    {
        GameObject popupPrefab = CreateOrLoadPopupPrefab();
        if (popupPrefab == null) return;

        foreach (string scenePath in TargetScenes)
            AddPopupToScene(scenePath, popupPrefab);

        AssetDatabase.SaveAssets();
        Debug.Log("[AchievementPopupSceneSetup] Popup prefab and scene instances are ready.");
    }

    private static void RunAutomaticSetupOnce()
    {
        if (SessionState.GetBool(SessionKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RunAutomaticSetupOnce;
            return;
        }

        SetupAchievementPopupInScenes();
        SessionState.SetBool(SessionKey, true);
    }

    private static GameObject CreateOrLoadPopupPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;

        EnsureFolder("Assets/Prefabs", "UI");

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");

        GameObject canvasObject = new GameObject(
            "AchievementPopupCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(AchievementPopupNotification));
        canvasObject.transform.localScale = Vector3.one;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject popup = CreateUIObject(
            "AchievementPopup", canvasObject.transform,
            typeof(Image), typeof(CanvasGroup), typeof(Outline));
        RectTransform popupRect = popup.GetComponent<RectTransform>();
        popupRect.anchorMin = new Vector2(0.5f, 1f);
        popupRect.anchorMax = new Vector2(0.5f, 1f);
        popupRect.pivot = new Vector2(0.5f, 1f);
        popupRect.sizeDelta = new Vector2(760f, 164f);
        popupRect.anchoredPosition = new Vector2(0f, 190f);

        Image background = popup.GetComponent<Image>();
        background.color = new Color(0.18f, 0.12f, 0.10f, 0.97f);
        background.raycastTarget = false;

        CanvasGroup group = popup.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        Outline outline = popup.GetComponent<Outline>();
        outline.effectColor = new Color(0.93f, 0.66f, 0.24f, 0.9f);
        outline.effectDistance = new Vector2(3f, -3f);

        GameObject accent = CreateUIObject("GoldAccent", popup.transform, typeof(Image));
        RectTransform accentRect = accent.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(12f, 0f);
        Image accentImage = accent.GetComponent<Image>();
        accentImage.color = new Color(0.93f, 0.66f, 0.24f, 1f);
        accentImage.raycastTarget = false;

        GameObject iconObject = CreateUIObject("AchievementIcon", popup.transform, typeof(Image));
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(86f, 0f);
        iconRect.sizeDelta = new Vector2(104f, 104f);
        Image icon = iconObject.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        TMP_Text heading = CreateText(
            "Heading", popup.transform, "ACHIEVEMENT UNLOCKED", font,
            25f, FontStyles.Bold, new Color(0.93f, 0.66f, 0.24f, 1f));
        SetTextRect(heading.rectTransform, new Vector2(158f, -20f), 38f);

        TMP_Text achievementName = CreateText(
            "AchievementName", popup.transform, "Achievement", font,
            38f, FontStyles.Bold, new Color(1f, 0.94f, 0.78f, 1f));
        SetTextRect(achievementName.rectTransform, new Vector2(158f, -57f), 52f);

        TMP_Text reward = CreateText(
            "Reward", popup.transform, "Reward", font,
            22f, FontStyles.Normal, new Color(0.88f, 0.82f, 0.70f, 1f));
        SetTextRect(reward.rectTransform, new Vector2(158f, -111f), 30f);

        AchievementPopupNotification controller =
            canvasObject.GetComponent<AchievementPopupNotification>();
        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("popupRoot").objectReferenceValue = popup;
        serializedController.FindProperty("popupRect").objectReferenceValue = popupRect;
        serializedController.FindProperty("popupGroup").objectReferenceValue = group;
        serializedController.FindProperty("backgroundImage").objectReferenceValue = background;
        serializedController.FindProperty("accentImage").objectReferenceValue = accentImage;
        serializedController.FindProperty("popupOutline").objectReferenceValue = outline;
        serializedController.FindProperty("iconImage").objectReferenceValue = icon;
        serializedController.FindProperty("headingText").objectReferenceValue = heading;
        serializedController.FindProperty("achievementNameText").objectReferenceValue = achievementName;
        serializedController.FindProperty("rewardText").objectReferenceValue = reward;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        popup.SetActive(false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(canvasObject, PrefabPath);
        Object.DestroyImmediate(canvasObject);
        return prefab;
    }

    private static void AddPopupToScene(string scenePath, GameObject popupPrefab)
    {
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedForSetup = !scene.IsValid() || !scene.isLoaded;
        if (openedForSetup)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        bool alreadyPresent = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "AchievementPopupCanvas")
            {
                alreadyPresent = true;
                break;
            }
        }

        if (!alreadyPresent)
        {
            PrefabUtility.InstantiatePrefab(popupPrefab, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (openedForSetup)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static GameObject CreateUIObject(
        string objectName, Transform parent, params System.Type[] components)
    {
        GameObject result = new GameObject(objectName, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        foreach (System.Type component in components)
        {
            if (component != typeof(RectTransform)) result.AddComponent(component);
        }
        return result;
    }

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        TMP_FontAsset font,
        float size,
        FontStyles style,
        Color color)
    {
        GameObject owner = CreateUIObject(objectName, parent, typeof(TextMeshProUGUI));
        TMP_Text text = owner.GetComponent<TMP_Text>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.Left;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static void SetTextRect(RectTransform rect, Vector2 topLeft, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeft;
        rect.sizeDelta = new Vector2(-topLeft.x - 32f, height);
    }
}
#endif
