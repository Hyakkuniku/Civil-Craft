#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Authored scene UI only. No login, account request, or purchase occurs here.</summary>
[InitializeOnLoad]
public static class WebsiteShopAuthoring
{
    private const string DialogPrefab = "Assets/Prefabs/UI/AccountSaveChoiceDialog.prefab";
    private const string DialogName = "Website Currency Dialog";
    private static bool authoring;
    private static readonly string[] Paths = {
        "Assets/Scenes/Main Menu.unity", "Assets/Scenes/BHAN HOUSE.unity",
        "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity"
    };

    static WebsiteShopAuthoring()
    {
        EditorApplication.delayCall += TryAuthor;
        EditorSceneManager.sceneOpened += (_, __) => {
            if (!authoring) EditorApplication.delayCall += TryAuthor;
        };
    }

    [MenuItem("Tools/Civil Craft/Author Website Shop Buttons")]
    public static void AuthorAll() => Apply();

    private static void TryAuthor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || authoring) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += TryAuthor; return; }
        Apply();
    }

    private static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DialogPrefab);
        if (prefab == null) { Debug.LogWarning("[Website Shop] Authored dialog prefab is missing."); return; }
        Scene previous = SceneManager.GetActiveScene();
        authoring = true;
        try {
        foreach (string path in Paths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened && File.Exists(path))
            {
                string saved = File.ReadAllText(path);
                if (saved.Contains("currencyShopDialog: {fileID:") &&
                    !saved.Contains("currencyShopDialog: {fileID: 0}") &&
                    saved.Contains("m_MethodName: HandleAddDiamondsClicked") &&
                    saved.Contains("m_MethodName: RefreshWalletBalances")) continue;
            }
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            bool wasDirty = scene.isDirty;
            try
            {
                bool changed = false;
                foreach (ShopManager shop in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ShopManager>(true)))
                    changed |= Ensure(shop, scene, prefab);
                if (!changed) continue;
                EditorSceneManager.MarkSceneDirty(scene);
                // Preserve pre-existing unsaved edits. A loaded dirty scene is NEVER
                // auto-saved or closed; its new authored UI can be saved with Ctrl+S.
                if (opened || !wasDirty) EditorSceneManager.SaveScene(scene);
                else Debug.Log("[Website Shop] Added purchase controls to " + scene.name + "; save the scene to keep them with your existing edits.");
            }
            catch (Exception error) { Debug.LogError("[Website Shop] " + path + ": " + error.Message); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
        finally { authoring = false; }
    }

    private static bool Ensure(ShopManager shop, Scene scene, GameObject prefab)
    {
        SerializedObject data = new SerializedObject(shop);
        SerializedProperty dialogField = data.FindProperty("currencyShopDialog");
        if (dialogField == null) return false; // Runtime assembly must finish importing first.
        bool changed = false;
        AuthoredSaveChoiceDialog dialog = dialogField.objectReferenceValue as AuthoredSaveChoiceDialog;
        if (dialog == null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = DialogName;
            instance.SetActive(false);
            dialog = instance.GetComponent<AuthoredSaveChoiceDialog>();
            dialogField.objectReferenceValue = dialog;
            data.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        Transform coin = Find(shop.transform, "AddCurrencyButton");
        if (coin != null && coin.GetComponent<Button>() != null)
            changed |= Wire(coin.GetComponent<Button>(), shop, "HandleAddCurrencyClicked");
        Transform pill = Find(shop.transform, "SecondaryCurrencyPill");
        if (pill == null) return changed;
        Transform existing = pill.Find("AddDiamondsButton");
        if (existing == null)
        {
            GameObject control = new GameObject("AddDiamondsButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            control.layer = pill.gameObject.layer;
            control.transform.SetParent(pill, false);
            RectTransform rect = control.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.76f, .08f); rect.anchorMax = new Vector2(.98f, .92f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = control.GetComponent<Image>();
            image.color = new Color32(233, 161, 42, 255);
            control.GetComponent<Button>().targetGraphic = image;
            GameObject labelObject = new GameObject("Plus", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.layer = control.layer; labelObject.transform.SetParent(control.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            TMP_Text existingText = pill.GetComponentInChildren<TMP_Text>(true);
            label.font = existingText != null ? existingText.font : TMP_Settings.defaultFontAsset;
            label.text = "+"; label.color = new Color32(76, 50, 31, 255);
            label.fontSize = 28; label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            existing = control.transform;
            Transform amount = Find(pill, "SecondaryCurrencyText");
            if (amount is RectTransform amountRect)
            { amountRect.anchorMax = new Vector2(.75f, amountRect.anchorMax.y); amountRect.offsetMax = new Vector2(-2, amountRect.offsetMax.y); }
            changed = true;
        }
        changed |= Wire(existing.GetComponent<Button>(), shop, "HandleAddDiamondsClicked");
        Transform refresh = pill.parent.Find("RefreshCurrencyButton");
        if (refresh == null)
        {
            GameObject control = new GameObject("RefreshCurrencyButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            control.layer = pill.gameObject.layer;
            control.transform.SetParent(pill.parent, false);
            RectTransform rect = control.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.755f, .83f); rect.anchorMax = new Vector2(.925f, .875f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = control.GetComponent<Image>();
            image.color = new Color32(255, 248, 232, 255);
            Image pillImage = pill.GetComponent<Image>();
            if (pillImage != null) { image.sprite = pillImage.sprite; image.type = pillImage.type; }
            control.GetComponent<Button>().targetGraphic = image;
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.layer = control.layer; labelObject.transform.SetParent(control.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            TMP_Text sample = pill.GetComponentInChildren<TMP_Text>(true);
            label.font = sample != null ? sample.font : TMP_Settings.defaultFontAsset;
            label.text = "Refresh balances"; label.color = new Color32(76, 50, 31, 255);
            label.fontSize = 20; label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 20;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            refresh = control.transform;
            changed = true;
        }
        changed |= Wire(refresh.GetComponent<Button>(), shop, "RefreshWalletBalances");
        return changed;
    }

    private static bool Wire(Button button, ShopManager shop, string method)
    {
        if (button == null) return false;
        if (Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(index =>
            button.onClick.GetPersistentTarget(index) == shop && button.onClick.GetPersistentMethodName(index) == method)) return false;
        var callback = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), shop, method);
        UnityEventTools.AddPersistentListener(button.onClick, callback);
        EditorUtility.SetDirty(button);
        return true;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        { Transform found = Find(child, name); if (found != null) return found; }
        return null;
    }
}
#endif
