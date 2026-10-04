using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>One-shot authoring: reuses the saved gameplay shop, without changing it.</summary>
public static class MainMenuShopWiring
{
    private const string Path = "Assets/Scenes/Main Menu.unity";
    private const string Request = "Temp/main-menu-shop-wiring-v2.request";
    private const string Report = "Temp/MainMenuShopWiringValidation.txt";
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Check; EditorApplication.update += Check; }
    private static void Check()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SceneManager.GetSceneByPath(Path).isDirty)
        {
            File.WriteAllText(Report, "WAIT: Save Main Menu scene edits first. No unsaved scene has been overwritten."); return;
        }
        File.Delete(Request);
        try { Author(); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    [MenuItem("Tools/Civil Craft/Wire Main Menu Store To Shop")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode first.");
        Scene scene = SceneManager.GetSceneByPath(Path);
        if (scene.isDirty) throw new Exception("Save Main Menu scene edits first.");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Path, OpenSceneMode.Additive);
        try
        {
            var controller = All<MainMenuUIController>(scene).Single();
            var store = All<Button>(scene).Single(b => b.name == "StoreButton");
            int sounds = Count(store, "PlaySFX");
            var data = new SerializedObject(controller);
            string unlock = data.FindProperty("storeFeatureId").stringValue;
            ShopManager shop = All<ShopManager>(scene).SingleOrDefault();
            if (shop == null)
            {
                // Unity 2022.3 supports additive loading, not OpenPreviewScene.
                // Only read the source; never save or close a user's loaded scene.
                Scene source = SceneManager.GetSceneByPath("Assets/Scenes/CanyonCrossing.unity");
                bool sourceOpened = !source.IsValid() || !source.isLoaded;
                if (sourceOpened)
                    source = EditorSceneManager.OpenScene("Assets/Scenes/CanyonCrossing.unity", OpenSceneMode.Additive);
                try
                {
                    var original = All<ShopManager>(source).Single();
                    Canvas canvas = store.GetComponentInParent<Canvas>(true);
                    if (canvas == null) throw new Exception("Store has no Canvas.");
                    var copy = Object.Instantiate(original.gameObject, canvas.transform, false);
                    copy.name = "MainMenuShopSystem";
                    shop = copy.GetComponent<ShopManager>();
                    var rect = (RectTransform)copy.transform;
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    rect.pivot = new Vector2(.5f, .5f); rect.localScale = Vector3.one;
                    rect.localRotation = Quaternion.identity;
                    copy.SetActive(true);
                    var shopData = new SerializedObject(shop);
                    shopData.FindProperty("onOpenTutorial").objectReferenceValue = null;
                    shopData.FindProperty("blockClosingUntilFinalTutorialStep").boolValue = false;
                    shopData.ApplyModifiedPropertiesWithoutUndo();
                }
                finally { if (sourceOpened) EditorSceneManager.CloseScene(source, true); }
            }
            // The saved shop's top-right close artwork has no Button component.
            // Make this menu copy's close control actionable without changing the source.
            Transform closeArtwork = shop.GetComponentsInChildren<Transform>(true)
                .Single(t => t.name == "CloseButton");
            Button closeButton = closeArtwork.GetComponent<Button>();
            if (closeButton == null)
            {
                Graphic graphic = closeArtwork.GetComponent<Graphic>();
                if (graphic == null) throw new Exception("Shop close artwork has no Graphic.");
                graphic.raycastTarget = true;
                closeButton = closeArtwork.gameObject.AddComponent<Button>();
                closeButton.targetGraphic = graphic;
                UnityEventTools.AddPersistentListener(closeButton.onClick, shop.CloseShop);
            }
            shop.Panel.SetActive(false);
            var configuration = new SerializedObject(shop);
            ((GameObject)configuration.FindProperty("purchaseConfirmationPanel").objectReferenceValue).SetActive(false);
            ((GameObject)configuration.FindProperty("purchaseFeedbackPanel").objectReferenceValue).SetActive(false);
            data.Update();
            data.FindProperty("shopManager").objectReferenceValue = shop;
            data.FindProperty("storeButton").objectReferenceValue = store.gameObject;
            data.ApplyModifiedPropertiesWithoutUndo();
            // Replace only the wrong action, never the Click sound or other events.
            for (int i = store.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                if (store.onClick.GetPersistentTarget(i) == controller &&
                    (store.onClick.GetPersistentMethodName(i) == nameof(MainMenuUIController.OnAchievementsClicked) ||
                     store.onClick.GetPersistentMethodName(i) == nameof(MainMenuUIController.OnStoreClicked)))
                    UnityEventTools.RemovePersistentListener(store.onClick, i);
            UnityEventTools.AddPersistentListener(store.onClick, controller.OnStoreClicked);
            // No second runtime open listener should be installed on this button.
            var trigger = store.GetComponent<ShopButtonTrigger>();
            if (trigger != null)
            {
                var triggerData = new SerializedObject(trigger);
                triggerData.FindProperty("wireButtonAutomatically").boolValue = false;
                triggerData.ApplyModifiedPropertiesWithoutUndo();
            }
            if (Count(store, nameof(MainMenuUIController.OnStoreClicked)) != 1 ||
                Count(store, nameof(MainMenuUIController.OnAchievementsClicked)) != 0 || sounds != Count(store, "PlaySFX"))
                throw new Exception("Store listener replacement or click sound preservation failed.");
            if (unlock != new SerializedObject(controller).FindProperty("storeFeatureId").stringValue)
                throw new Exception("Store unlock ID changed.");
            Validate(shop, scene);
            EditorUtility.SetDirty(controller); EditorUtility.SetDirty(store); EditorUtility.SetDirty(shop);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Report, "PASS: Main Menu Store persistently wired to OnStoreClicked -> its own scene-authored ShopManager. Achievements callback removed; click sound and unlock ID preserved. Catalog, category tabs, currency, purchase/feedback panels and close callbacks verified. No cross-scene references, world changes, account calls, or purchases. Live open/close and purchase testing still required.\n");
            Debug.Log("[Main Menu shop wiring] " + File.ReadAllText(Report));
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    private static int Count(Button button, string method) => Enumerable.Range(0, button.onClick.GetPersistentEventCount())
        .Count(i => button.onClick.GetPersistentMethodName(i) == method);
    private static void Validate(ShopManager shop, Scene scene)
    {
        var data = new SerializedObject(shop);
        foreach (string field in new[] { "shopPanel", "currencyText", "itemGridContent", "itemCardPrefab",
            "purchaseConfirmationPanel", "confirmationTitleText", "confirmationPriceText", "confirmationIconImage",
            "purchaseFeedbackPanel", "purchaseFeedbackMessageText" })
            if (data.FindProperty(field).objectReferenceValue == null) throw new Exception("Shop reference missing: " + field);
        if (shop.AllItems.Count == 0 || shop.AllItems.Any(item => item == null)) throw new Exception("Shop catalog missing.");
        if (data.FindProperty("categoryTabs").arraySize != 5) throw new Exception("Shop category tabs missing.");
        if (data.FindProperty("onOpenTutorial").objectReferenceValue != null) throw new Exception("Story tutorial assigned to menu shop.");
        var close = shop.GetComponentsInChildren<Button>(true).Where(b => b.name == "CloseButton" || b.name == "BackButton").ToArray();
        if (close.Length != 2 || close.Any(b => !Enumerable.Range(0, b.onClick.GetPersistentEventCount())
            .Any(i => b.onClick.GetPersistentTarget(i) == shop && b.onClick.GetPersistentMethodName(i) == nameof(ShopManager.CloseShop))))
            throw new Exception("Shop close/back callback does not point to the menu shop.");
        foreach (Component component in shop.GetComponentsInChildren<Component>(true))
        {
            if (component == null) throw new Exception("Missing component in menu shop.");
            var iterator = new SerializedObject(component).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                var value = iterator.objectReferenceValue;
                GameObject obj = value is Component target ? target.gameObject : value as GameObject;
                if (obj != null && obj.scene.IsValid() && obj.scene != scene)
                    throw new Exception("Cross-scene shop reference: " + component.name + "/" + iterator.propertyPath);
            }
        }
    }
}
