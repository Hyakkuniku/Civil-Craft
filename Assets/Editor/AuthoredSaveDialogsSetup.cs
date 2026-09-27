using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One-time scene/prefab authoring; no dialog hierarchy is built by player code.</summary>
public static class AuthoredSaveDialogsSetup
{
    private const string AccountPrefabPath = "Assets/Prefabs/UI/AccountSaveChoiceDialog.prefab";
    private const string CloudPrefabPath = "Assets/Prefabs/UI/CloudSaveConflictDialog.prefab";
    private const string MainMenuPath = "Assets/Scenes/Main Menu.unity";
    private const string ManagerPrefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private static readonly string[] ScenePaths =
    {
        MainMenuPath,
        "Assets/Scenes/BHAN HOUSE.unity",
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };

    [MenuItem("Tools/Civil Craft/Author Save Dialogs")]
    public static void AuthorAll()
    {
        AuthorAllInternal();
    }

    private static bool AuthorAllInternal()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return false;

        foreach (string path in ScenePaths)
        {
            Scene loaded = SceneManager.GetSceneByPath(path);
            if (loaded.isLoaded && loaded.isDirty)
            {
                Debug.LogWarning("Save your edits in " + path +
                    " before authoring save dialogs. No scenes were changed.");
                return false;
            }
        }

        GameObject accountPrefab = EnsurePrefab(AccountPrefabPath, false);
        GameObject cloudPrefab = EnsurePrefab(CloudPrefabPath, true);
        if (accountPrefab == null || cloudPrefab == null) return false;

        // Author the source prefab first so any scene instances inherit its child
        // instead of receiving a duplicate added-child override.
        bool complete = AuthorManagerPrefab(cloudPrefab);
        foreach (string path in ScenePaths)
            complete &= AuthorScene(path, accountPrefab, cloudPrefab);

        AssetDatabase.SaveAssets();
        if (complete)
            Debug.Log("Authored account-save and cloud-conflict dialogs are placed in all current player-data scenes.");
        return complete;
    }

    private static GameObject EnsurePrefab(string path, bool cloud)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        GameObject root = BuildDialog(cloud);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        if (prefab == null) Debug.LogError("Could not save authored dialog at " + path);
        return prefab;
    }

    private static GameObject BuildDialog(bool cloud)
    {
        Color ink = cloud ? Color.white : new Color32(73, 56, 44, 255);
        Color frame = cloud ? new Color(0.11f, 0.16f, 0.22f, 1f) : ink;
        Color paper = cloud ? frame : new Color32(255, 251, 246, 255);
        Color primary = cloud ? new Color(0.16f, 0.44f, 0.7f, 1f) : ink;
        Color secondary = cloud ? primary : new Color32(237, 224, 207, 255);
        Sprite rounded = LoadSprite("0ef2271a8dd3d634da5ca501a61772f2");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath("aaadb7eb00eeee74799e9edce7312ddb"));
        if (font == null) font = TMP_Settings.defaultFontAsset;

        GameObject root = new GameObject(cloud ? "Cloud Save Conflict Dialog" :
            "Account Save Choice Dialog", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(AuthoredSaveChoiceDialog));
        root.layer = 5;
        Stretch((RectTransform)root.transform);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = cloud ? 32767 : 32000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = cloud ? new Vector2(1080f, 1920f) :
            new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Image blocker = CreateImage("Dim Background", root.transform,
            cloud ? new Color(0f, 0f, 0f, 0.85f) : new Color(0f, 0f, 0f, 0.7f));
        Stretch(blocker.rectTransform);
        blocker.raycastTarget = true;

        Image border = CreateImage("Dialog Frame", blocker.transform, frame, rounded);
        Center(border.rectTransform, new Vector2(cloud ? 940f : 820f, cloud ? 510f : 480f),
            Vector2.zero);
        border.raycastTarget = false;
        if (!cloud)
        {
            Image interior = CreateImage("Dialog Surface", border.transform, paper, rounded);
            Stretch(interior.rectTransform, new Vector2(8f, 8f), new Vector2(-8f, -8f));
            interior.raycastTarget = false;
            interior.transform.SetAsFirstSibling();
        }

        TMP_Text title = CreateText("Title", border.transform,
            cloud ? "CHOOSE A SAVE" : "ACCOUNT SAVE", font, ink, 44f,
            new Vector2(750f, 60f), new Vector2(0f, 183f));
        title.fontStyle = FontStyles.Bold;
        TMP_Text message = CreateText("Explanation", border.transform,
            cloud ? "This device and your online account have different progress." :
                "Choose how to continue with this account.", font, ink, cloud ? 30f : 32f,
            new Vector2(cloud ? 800f : 700f, 215f), new Vector2(0f, 26f));

        Button left = CreateButton("Left Choice", border.transform, primary, rounded,
            new Vector2(340f, 76f), new Vector2(-190f, -173f));
        Button right = CreateButton("Right Choice", border.transform, secondary, rounded,
            new Vector2(340f, 76f), new Vector2(190f, -173f));
        TMP_Text leftLabel = CreateText("Label", left.transform,
            cloud ? "Keep Device Copy" : "Start Fresh", font,
            cloud ? Color.white : paper, 25f, Vector2.zero, Vector2.zero);
        Stretch(leftLabel.rectTransform, new Vector2(8f, 5f), new Vector2(-8f, -5f));
        TMP_Text rightLabel = CreateText("Label", right.transform,
            cloud ? "Use Online Save" : "Use Guest Save", font,
            cloud ? Color.white : ink, 25f, Vector2.zero, Vector2.zero);
        Stretch(rightLabel.rectTransform, new Vector2(8f, 5f), new Vector2(-8f, -5f));

        root.GetComponent<AuthoredSaveChoiceDialog>().SetAuthoringReferences(
            title, message, leftLabel, rightLabel, left, right);
        root.SetActive(false);
        return root;
    }

    private static bool AuthorScene(string path, GameObject accountPrefab, GameObject cloudPrefab)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        try
        {
            PlayerDataManager playerData = FindInScene<PlayerDataManager>(scene);
            if (playerData == null)
            {
                Debug.LogWarning("No PlayerDataManager was found in " + path);
                return false;
            }

            bool changed = false;
            if (playerData.GetComponentInChildren<AuthoredSaveChoiceDialog>(true) == null)
            {
                GameObject cloud = (GameObject)PrefabUtility.InstantiatePrefab(cloudPrefab, scene);
                cloud.transform.SetParent(playerData.transform, false);
                changed = true;
            }

            if (path == MainMenuPath)
            {
                PlayFabAuthManager auth = FindInScene<PlayFabAuthManager>(scene);
                if (auth == null)
                {
                    Debug.LogError("Main Menu is missing PlayFabAuthManager.");
                    return false;
                }
                else if (auth.saveChoiceDialog == null)
                {
                    GameObject account = (GameObject)PrefabUtility.InstantiatePrefab(accountPrefab, scene);
                    auth.saveChoiceDialog = account.GetComponent<AuthoredSaveChoiceDialog>();
                    EditorUtility.SetDirty(auth);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                return EditorSceneManager.SaveScene(scene);
            }
            return true;
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static bool AuthorManagerPrefab(GameObject cloudPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ManagerPrefabPath);
        if (root == null) return false;
        try
        {
            PlayerDataManager data = root.GetComponentInChildren<PlayerDataManager>(true);
            if (data == null) return false;
            if (data.GetComponentInChildren<AuthoredSaveChoiceDialog>(true) != null)
                return true;

            GameObject cloud = (GameObject)PrefabUtility.InstantiatePrefab(cloudPrefab, data.transform);
            if (cloud == null) return false;
            return PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath) != null;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    private static Sprite LoadSprite(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Image CreateImage(string name, Transform parent, Color color, Sprite sprite = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, TMP_FontAsset font,
        Color color, float size, Vector2 rectSize, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        Center(rect, rectSize, position);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.color = color;
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Min(18f, size);
        text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(string name, Transform parent, Color color, Sprite sprite,
        Vector2 size, Vector2 position)
    {
        Image image = CreateImage(name, parent, color, sprite);
        Center(image.rectTransform, size, position);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    private static void Center(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        Stretch(rect, Vector2.zero, Vector2.zero);
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }
}
