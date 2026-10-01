#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class BuildModeMaterialDockAuthoring
{
    private const string PillSpritePath =
        "Assets/Elements/UI/bm_ui/Rounded Corners - Coco Code package/Rounded Corners - Coco Code package/Rounded10px@2x.png";
    private const string ManagersPrefabPath =
        "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/CanyonCrossing.unity",
        "Assets/Scenes/BHAN HOUSE.unity"
    };

    [MenuItem("Tools/Civil Craft/Author Build Mode Material Scrollbars")]
    public static void AuthorMaterialDocks()
    {
        foreach (string path in ScenePaths)
            AuthorScene(path);

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(ManagersPrefabPath);
        try
        {
            ScrollRect dock = FindMaterialDock(prefabRoot);
            if (dock != null)
            {
                ConfigureDock(dock);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, ManagersPrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[Build Mode UI] Material docks narrowed with white in-panel scroll handles.");
    }

    private static void AuthorScene(string path)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedForSetup = !scene.IsValid() || !scene.isLoaded;
        if (openedForSetup)
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        else if (scene.isDirty)
        {
            Debug.LogWarning($"[Build Mode UI] {path} has unsaved editor changes; save it, then run Tools/Civil Craft/Author Build Mode Material Scrollbars.");
            return;
        }

        try
        {
            ScrollRect dock = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                dock = FindMaterialDock(root);
                if (dock != null) break;
            }

            if (dock == null)
            {
                Debug.LogWarning($"[Build Mode UI] MaterialsScrollPanel not found in {path}.");
                return;
            }

            ConfigureDock(dock);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedForSetup)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static ScrollRect FindMaterialDock(GameObject root)
    {
        foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true))
        {
            if (scroll.name != "MaterialsScrollPanel") continue;
            Canvas canvas = scroll.GetComponentInParent<Canvas>(true);
            if (canvas != null && canvas.name == "BuildCanvas") return scroll;
        }
        return null;
    }

    private static void ConfigureDock(ScrollRect scroll)
    {
        RectTransform dockRect = scroll.GetComponent<RectTransform>();
        Vector2 position = dockRect.anchoredPosition;
        position.x = 340f;
        dockRect.anchoredPosition = position;
        dockRect.sizeDelta = new Vector2(600f, 112f);

        RectTransform viewport = scroll.viewport;
        if (viewport != null)
        {
            Vector2 size = viewport.sizeDelta;
            size.x = 600f;
            viewport.sizeDelta = size;
            Vector2 viewportPosition = viewport.anchoredPosition;
            viewportPosition.y = 22.3f;
            viewport.anchoredPosition = viewportPosition;
        }

        Transform existing = dockRect.Find("MaterialScrollbar");
        GameObject track = existing != null
            ? existing.gameObject
            : CreateUIObject("MaterialScrollbar", dockRect, typeof(Image), typeof(Scrollbar));

        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0.5f);
        trackRect.anchoredPosition = new Vector2(0f, 7f);
        trackRect.sizeDelta = new Vector2(-20f, 10f);

        Image trackImage = track.GetComponent<Image>();
        trackImage.sprite = null;
        trackImage.type = Image.Type.Simple;
        trackImage.color = new Color(1f, 1f, 1f, 0f);
        trackImage.raycastTarget = true;

        Transform sliding = trackRect.Find("Sliding Area");
        if (sliding == null)
            sliding = CreateUIObject("Sliding Area", trackRect).transform;
        RectTransform slidingRect = (RectTransform)sliding;
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
        slidingRect.offsetMin = new Vector2(2f, 2f);
        slidingRect.offsetMax = new Vector2(-2f, -2f);

        Transform handle = sliding.Find("Handle");
        if (handle == null)
            handle = CreateUIObject("Handle", slidingRect, typeof(Image)).transform;
        RectTransform handleRect = (RectTransform)handle;
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;

        Image handleImage = handle.GetComponent<Image>();
        handleImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PillSpritePath);
        handleImage.type = handleImage.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        handleImage.pixelsPerUnitMultiplier = 6f;
        handleImage.color = Color.white;
        handleImage.raycastTarget = true;

        Scrollbar scrollbar = track.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.LeftToRight;
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        scrollbar.numberOfSteps = 0;
        scrollbar.value = 0f;

        scroll.horizontal = true;
        scroll.horizontalScrollbar = scrollbar;
        scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.horizontalScrollbarSpacing = 0f;
    }

    private static GameObject CreateUIObject(string name, Transform parent, params System.Type[] components)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        if (go.scene != parent.gameObject.scene)
            SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        foreach (System.Type component in components)
            go.AddComponent(component);
        return go;
    }
}
#endif
