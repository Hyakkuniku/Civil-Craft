#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Detached typography checks; never opens a live panel or modifies player data.</summary>
[InitializeOnLoad]
public static class ContractTitlePresentationValidation
{
    private const string Request = "Temp/contract-title-presentation.request";
    private const string Report = "Logs/contract-title-presentation.txt";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    private static double next;
    private static bool running;
    static ContractTitlePresentationValidation() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            AssetDatabase.IsAssetImportWorkerProcess() || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(Request); Run();
    }
    [MenuItem("Tools/Civil Craft/Contracts/Validate Contract Title Presentation")]
    public static void Run()
    {
        running = true;
        var report = new StringBuilder();
        Scene active = SceneManager.GetActiveScene(), temporary = default;
        var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .ToDictionary(scene => scene, scene => Tuple.Create(scene.isDirty, scene.GetRootGameObjects()));
        Object[] selection = Selection.objects;
        PlayerDataManager saves = PlayerDataManager.Instance;
        string player = saves != null && saves.CurrentData != null ? JsonUtility.ToJson(saves.CurrentData) : null;
        ObjectiveTrackerUI trackerSingleton = ObjectiveTrackerUI.Instance;
        GameObject root = null;
        var originals = new Dictionary<Object, string>();
        try
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run in idle Edit Mode.");
            ContractSO[] contracts = AssetDatabase.FindAssets("t:ContractSO", new[] { "Assets/BridgeBuilder/Data/Contract" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<ContractSO>)
                .Where(contract => !string.IsNullOrWhiteSpace(contract.displayName)).ToArray();
            Require(contracts.Length == 16, "Apply the 16 requested contract display names first.");
            ObjectiveTrackerUI source = Source<ObjectiveTrackerUI>(value => value.titleText != null && value.questTabPrefab != null);
            ContractAlmanacTab book = Source<ContractAlmanacTab>(value => value.titleText != null);
            LeaderboardPanelUI leaderboard = Source<LeaderboardPanelUI>(value => Field(value, "contractDropdown") != null);
            LevelFailedManager failed = Source<LevelFailedManager>();
            Require(source.titleText != null && source.questTabPrefab != null && book.titleText != null,
                "Authored Objectives or Almanac title sources are missing.");
            TMP_Dropdown dropdown = (TMP_Dropdown)Field(leaderboard, "contractDropdown");
            TMP_Text failure = failed.levelFailedPanel.GetComponentsInChildren<TMP_Text>(true)
                .Single(text => text.name == "Contract Name" && text.transform.parent.name == "Failure Header");
            foreach (Object value in new Object[] { source.titleText, book.titleText, dropdown.captionText, dropdown.itemText, failure })
                if (value != null) originals[value] = EditorJsonUtility.ToJson(value);
            temporary = EditorSceneManager.NewPreviewScene();
            root = New(temporary, "Detached contract title canvas", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            RectTransform viewport = (RectTransform)root.transform;
            GameObject owner = New(temporary, "Inactive contract title styling owner"); owner.SetActive(false);
            ObjectiveTrackerUI copy = owner.AddComponent<ObjectiveTrackerUI>();
            copy.titleText = Text(source.titleText, viewport, "Detached objective typography source");
            copy.detailsPanel = Rect(viewport, "Detached objective details", new Vector2(1060, 770)).gameObject;
            Call(copy, "EnsureContractOfferLayout");
            TMP_Text offer = (TMP_Text)Field(copy, "offerTitleLabel");
            copy.detailsPanel.SetActive(true);
            TMP_Text almanac = CopyChain(book.titleText, viewport);
            typeof(ContractAlmanacTab).GetMethod("ConfigureText", Private).Invoke(null,
                new object[] { almanac, 46f, 24f, 48f, FontStyles.Bold, TextAlignmentOptions.TopLeft, Color.black });
            typeof(ContractAlmanacTab).GetMethod("SetNormalizedRect", Private).Invoke(null,
                new object[] { almanac.rectTransform, new Vector2(.11f, .75f), new Vector2(.92f, .90f) });
            almanac.enableWordWrapping = true;
            TMP_Text failureText = CopyChain(failure, viewport);
            TMP_Text caption = dropdown.captionText != null ? CopyChain(dropdown.captionText, viewport) : null;
            TMP_Text item = dropdown.itemText != null ? CopyChain(dropdown.itemText, viewport) : null;
            GameObject popup = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/AchievementPopupCanvas.prefab");
            GameObject inactivePopup = new GameObject("Inactive detached notification owner"); inactivePopup.transform.SetParent(viewport, false); inactivePopup.SetActive(false);
            GameObject popupClone = Object.Instantiate(popup, inactivePopup.transform, false);
            AchievementPopupNotification notificationController = popupClone.GetComponent<AchievementPopupNotification>();
            Call(notificationController, "ResolveVisualReferences");
            TMP_Text notification = (TMP_Text)Field(notificationController, "achievementNameText");
            // Keep the game controller inactive, but activate its typography by
            // moving only this owned text into the disposable active Canvas.
            notification.rectTransform.SetParent(viewport, false);
            notification.gameObject.SetActive(true);
            Type requestType = typeof(AchievementPopupNotification).GetNestedType("PopupRequest", BindingFlags.NonPublic);
            object notificationRequest = Activator.CreateInstance(requestType);
            requestType.GetField("kind").SetValue(notificationRequest, Enum.Parse(requestType.GetField("kind").FieldType, "Almanac"));
            requestType.GetField("detail").SetValue(notificationRequest, "Contract added to Almanac");
            var map = Rect(viewport, "Detached map contract name", new Vector2(260f, 44f)).gameObject.AddComponent<TextMeshProUGUI>();
            map.alignment = TextAlignmentOptions.Left; map.fontStyle = FontStyles.Bold;
            map.enableAutoSizing = true; map.fontSizeMin = 12f; map.fontSizeMax = 24f;
            // The expanded map installs its controller only when gameplay starts.
            // Use the same resource font without starting that live controller.
            map.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Bekind Sans SDF") ?? TMP_Settings.defaultFontAsset;
            map.fontSharedMaterial = map.font.material;
            GameObject tabObject = Object.Instantiate(source.questTabPrefab, viewport, false);
            foreach (MonoBehaviour script in tabObject.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(script is Graphic) && !(script is Selectable) && !(script is ObjectiveTabButton)) script.enabled = false;
            ObjectiveTabButton tab = tabObject.GetComponent<ObjectiveTabButton>();
            Require(tab != null && tab.questTitleText != null, "Authored objective row text is missing.");
            tabObject.SetActive(true); ((RectTransform)tabObject.transform).sizeDelta = new Vector2(960f, 126f);
            int checks = 0;
            foreach (Vector2 size in new[] { new Vector2(2340, 1080), new Vector2(1440, 1080) })
            {
                float scale = Mathf.Sqrt(size.x / 1920f * size.y / 1080f);
                viewport.sizeDelta = size / scale;
                Canvas.ForceUpdateCanvases();
                foreach (ContractSO contract in contracts)
                {
                    string title = contract.DisplayName;
                    foreach (string status in new[] { "CONTRACT OFFER", "READY TO TURN IN", "COMPLETED" })
                    { offer.text = "<size=18>" + status + "</size>\n" + title; Fits(offer, "Objectives " + title); checks++; }
                    almanac.text = title; Fits(almanac, "Almanac " + title); checks++;
                    failureText.text = "CONTRACT TEST  •  " + title.ToUpperInvariant(); Fits(failureText, "Failure report " + title); checks++;
                    if (caption != null) { caption.text = title; Fits(caption, "Leaderboard caption " + title); checks++; }
                    if (item != null) { item.text = title; item.gameObject.SetActive(true); Fits(item, "Leaderboard option " + title); checks++; }
                    requestType.GetField("title").SetValue(notificationRequest, title); Call(notificationController, "Populate", notificationRequest);
                    Fits(notification, "Almanac notification " + title); checks++;
                    map.text = title; Fits(map, "Map marker " + title); checks++;
                    foreach (bool done in new[] { false, true })
                    {
                        tab.Setup(new TrackedTask { title = title, contractName = string.Empty, isCompleted = done });
                        Fits(tab.questTitleText, "Objective history " + title); checks++;
                    }
                }
                report.AppendLine("PASS: " + size.x + "x" + size.y + ": all 16 full titles fit Objectives offer/status/history, Almanac, failure header and authored leaderboard text with real fonts.");
            }
            report.AppendLine("PASS: " + checks + " actual TMP layout checks; no truncated or missing title glyphs.");
        }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (temporary.IsValid()) EditorSceneManager.ClosePreviewScene(temporary);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            Selection.objects = selection;
            try
            {
                Require(SceneManager.sceneCount == scenes.Count, "Source scene count changed.");
                foreach (var pair in scenes) Require(pair.Key.isDirty == pair.Value.Item1 &&
                    pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Item2), "Source scene roots or dirty flags changed.");
                Require(originals.All(pair => EditorJsonUtility.ToJson(pair.Key) == pair.Value), "Source title typography changed.");
                Require(PlayerDataManager.Instance == saves && (player == null || JsonUtility.ToJson(saves.CurrentData) == player) &&
                    ObjectiveTrackerUI.Instance == trackerSingleton, "Live manager or player state changed.");
                report.AppendLine("PASS: source scenes, typography, selection, manager singletons and player data preserved. No callbacks, scene loads, purchases, saves or progression actions ran.");
            }
            catch (Exception error) { report.AppendLine("FAIL SAFETY: " + error); }
            Directory.CreateDirectory("Logs"); File.WriteAllText(Report, report.ToString()); running = false;
        }
    }
    private static T Source<T>(Func<T, bool> predicate = null) where T : Component
    {
        T result = Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(value => value != null &&
            value.gameObject.scene.IsValid() && value.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(value.gameObject.scene) &&
            (predicate == null || predicate(value)));
        Require(result != null, "No fully wired authored " + typeof(T).Name + " exists in a loaded scene.");
        return result;
    }
    private static TMP_Text CopyChain(TMP_Text source, RectTransform parent)
    {
        Canvas canvas = source.GetComponentInParent<Canvas>(true);
        var chain = new Stack<RectTransform>();
        for (Transform current = source.transform.parent; current != null && current != canvas.rootCanvas.transform; current = current.parent)
            if (current is RectTransform rect) chain.Push(rect);
        while (chain.Count > 0)
        {
            RectTransform original = chain.Pop(), clone = Rect(parent, "Detached " + original.name, original.sizeDelta);
            CopyRect(original, clone); parent = clone;
        }
        return Text(source, parent, "Detached " + source.name);
    }
    private static TextMeshProUGUI Text(TMP_Text source, RectTransform parent, string name)
    {
        RectTransform rect = Rect(parent, name, source.rectTransform.sizeDelta);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        EditorUtility.CopySerialized(source, text); CopyRect(source.rectTransform, rect);
        text.enabled = true; rect.gameObject.SetActive(true); return text;
    }
    private static RectTransform Rect(Transform parent, string name, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.sizeDelta = size; return rect;
    }
    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax; target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta; target.anchoredPosition3D = source.anchoredPosition3D;
        target.localScale = source.localScale; target.localRotation = source.localRotation;
    }
    private static GameObject New(Scene scene, string name, params Type[] components)
    { var value = new GameObject(name, components); SceneManager.MoveGameObjectToScene(value, scene); return value; }
    private static void Fits(TMP_Text text, string label)
    {
        Require(text != null && text.font != null, "Missing authored title text/font: " + label);
        Canvas.ForceUpdateCanvases(); text.ForceMeshUpdate(true, true);
        Require(text.textInfo != null && text.textInfo.characterInfo != null, "Title text did not initialize: " + label);
        Require(text.rectTransform.rect.width > 10f && text.rectTransform.rect.height > 10f, "Empty title bounds: " + label);
        Require(text.textInfo.characterCount > 0 && !text.isTextOverflowing, "Title does not fit: " + label +
            "; bounds=" + text.rectTransform.rect + "; font=" + text.fontSize + "; lines=" + text.textInfo.lineCount);
        Require(text.textInfo.characterInfo.Take(text.textInfo.characterCount).Any(character => character.isVisible), "No rendered title glyphs: " + label);
    }
    private static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
