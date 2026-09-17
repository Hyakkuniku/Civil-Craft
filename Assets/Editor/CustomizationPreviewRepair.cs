#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CustomizationPreviewRepair
{
    [MenuItem("Tools/Civil Craft/Repair And Verify Customization Preview")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before repairing saved preview references.");

        const string prefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
        GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Repair(prefab);
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        foreach (string path in new[] { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity" })
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            bool wasDirty = scene.isDirty;
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects()) Repair(root);
                if (!wasDirty) EditorSceneManager.SaveScene(scene);
                else Debug.Log("[Preview verification] Repaired loaded references; preserving unsaved scene edits in " + path);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        VerifyDraft();
        Debug.Log("[Preview verification] PASS: all portrait references resolve; draft hat/color changes apply, saved refresh is isolated, and ending preview restores the saved look.");
    }

    private static void Repair(GameObject root)
    {
        foreach (CharacterCustomizationController controller in root.GetComponentsInChildren<CharacterCustomizationController>(true))
        {
            if (!controller.ResolvePreviewMirror())
                throw new InvalidOperationException("Missing portrait mirror: " + controller.name);
            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            SerializedObject data = new SerializedObject(controller);
            PlayerCosmeticMirror mirror = data.FindProperty("previewMirror").objectReferenceValue as PlayerCosmeticMirror;
            if (mirror == null || !mirror.cosmeticBindings.Any(b => b.category == CosmeticCategory.Shirt && b.models.Any(m => m != null)))
                throw new InvalidOperationException("Portrait clothing bindings are missing.");
            Debug.Log("[Preview verification] Bound " + root.scene.path + " / " + controller.name + " to " + mirror.name);
        }
    }

    private static void VerifyDraft()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Loading/NewCharacterPreview.prefab");
        GameObject instance = UnityEngine.Object.Instantiate(source);
        instance.SetActive(false);
        try
        {
            PlayerCosmeticMirror mirror = instance.GetComponent<PlayerCosmeticMirror>();
            string saveBefore = PlayerDataManager.Instance != null ? JsonUtility.ToJson(PlayerDataManager.Instance.CurrentData) : null;
            CosmeticLoadoutData saved = PlayerDataManager.Instance != null
                ? PlayerDataManager.Instance.GetCosmeticLoadoutCopy() : new CosmeticLoadoutData();
            var draft = saved.Clone();
            draft.accessoriesID = "EngineeringHardHat";
            draft.shirtID = "Shirt_Base";
            draft.shirtColor = Color.red;
            mirror.BeginPreview(draft);
            GameObject hat = mirror.cosmeticBindings.First(b => b.cosmeticID == "EngineeringHardHat").models[0];
            if (!hat.activeSelf) throw new InvalidOperationException("Preview hat did not activate.");
            Renderer shirt = mirror.cosmeticBindings.First(b => b.cosmeticID == "Shirt_Base").models[0].GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            shirt.GetPropertyBlock(block, 0);
            int colorProperty = Shader.PropertyToID(shirt.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
            if (block.GetColor(colorProperty) != Color.red) throw new InvalidOperationException("Preview tint did not apply.");
            draft.accessoriesID = "Accessory_None";
            mirror.UpdatePreview(draft);
            if (hat.activeSelf) throw new InvalidOperationException("None did not hide the hat.");
            mirror.RefreshCosmetics();
            if (hat.activeSelf) throw new InvalidOperationException("Saved refresh replaced preview.");
            mirror.EndPreview(true);
            if (mirror.IsPreviewing || hat.activeSelf != (saved.accessoriesID == "EngineeringHardHat"))
                throw new InvalidOperationException("Cancel did not restore the saved outfit.");
            if (PlayerDataManager.Instance != null && saveBefore != JsonUtility.ToJson(PlayerDataManager.Instance.CurrentData))
                throw new InvalidOperationException("Preview changed PlayerData.");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

}
#endif
