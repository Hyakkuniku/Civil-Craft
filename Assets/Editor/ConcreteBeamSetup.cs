#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Authors the new material using the existing timber button/layout.</summary>
[InitializeOnLoad]
public static class ConcreteBeamSetup
{
    private const string MaterialPath = "Assets/BridgeBuilder/Data/Resources/ConcreteBeam.asset";
    private const string SelectedPath = "Assets/Elements/UI/bm_ui/concrete beam selected.png";
    private const string PrefabPath = "Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab";
    private const string VersionKey = "CivilCraft.ConcreteBeam.Authored.v2";
    private const string BalanceVersionKey = "CivilCraft.ConcreteMaterials.Balance.v1";
    static ConcreteBeamSetup()
    {
        EditorApplication.delayCall += TrySetup;
        EditorApplication.delayCall += TryValidateBalance;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += TrySetup;
                EditorApplication.delayCall += TryValidateBalance;
            }
        };
    }
    private static void TryValidateBalance()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(BalanceVersionKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += TryValidateBalance; return; }
        // Validation uses an isolated preview scene, never the player's open scene or save.
        ValidateBalance();
        SessionState.SetBool(BalanceVersionKey, true);
    }
    [MenuItem("Tools/Civil Craft/Validate Concrete Material Balance")]
    public static void ValidateBalance()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        BridgeMaterialSO beam = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(MaterialPath);
        BridgeMaterialSO road = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Concrete Road.asset");
        if (beam == null || road == null) { Debug.LogError("[Concrete Balance] Materials missing."); return; }
        ValidateConcreteDeckSupport(beam);
        ValidateLongConcreteTruss(beam, road);
    }
    private static void TrySetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(VersionKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += TrySetup; return; }
        Setup();
    }
    [MenuItem("Tools/Civil Craft/Set Up Concrete Beam")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        BridgeMaterialSO material = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>(MaterialPath);
        Sprite selected = AssetDatabase.LoadAssetAtPath<Sprite>(SelectedPath);
        if (material == null || material.segmentPrefab == null || material.materialIcon == null || selected == null)
        { Debug.LogError("[Concrete Beam] Material, visual prefab, or button pictures are missing."); return; }
        bool ready = true;
        Scene active = SceneManager.GetActiveScene();
        foreach (string path in new[] { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity", "Assets/Scenes/Multiplayer/Multiplayer.unity" })
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty)
            { ready = false; Debug.LogWarning("[Concrete Beam] Save " + scene.name + ", then run Tools/Civil Craft/Set Up Concrete Beam."); continue; }
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                bool changed = false;
                foreach (GameObject root in scene.GetRootGameObjects()) changed |= ConfigureButton(root, material, selected);
                if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (ConfigureButton(prefab, material, selected)) PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        ValidateVisual(material);
        ValidateConcreteDeckSupport(material);
        if (ready) SessionState.SetBool(VersionKey, true);
        Debug.Log("[Concrete Beam] Material buttons authored with selected/unselected pictures. Existing contract allowances are unchanged.");
    }
    private static bool ConfigureButton(GameObject root, BridgeMaterialSO material, Sprite selected)
    {
        MaterialButtonTrigger existing = null, timber = null;
        foreach (MaterialButtonTrigger trigger in root.GetComponentsInChildren<MaterialButtonTrigger>(true))
        {
            if (trigger.buttonMaterial == material) existing = trigger;
            if (trigger.buttonMaterial != null && trigger.buttonMaterial.Id == "Beam") timber = trigger;
        }
        bool created = existing == null;
        if (created)
        {
            if (timber == null) return false;
            GameObject source = timber.parentWrapper != null ? timber.parentWrapper : timber.gameObject;
            // Never clone a whole dock: only the individual timber wrapper.
            if (source.GetComponentsInChildren<MaterialButtonTrigger>(true).Length != 1)
            { Debug.LogError("[Concrete Beam] Timber wrapper contains multiple materials; leaving this dock unchanged.", source); return false; }
            GameObject clone = Object.Instantiate(source, source.transform.parent, false);
            clone.name = "Concrete Beam";
            clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            clone.SetActive(true);
            existing = clone.GetComponentInChildren<MaterialButtonTrigger>(true);
            existing.gameObject.name = "Concrete Beam Button";
            existing.buttonMaterial = material;
            // Instance cloning remaps the wrapper, badge, and image references.
        }
        if (!created && existing.defaultSprite == material.materialIcon && existing.selectedOutlineSprite == selected) return false;
        existing.defaultSprite = material.materialIcon;
        existing.selectedOutlineSprite = selected;
        if (existing.buttonImage != null)
        {
            existing.buttonImage.sprite = material.materialIcon;
            existing.buttonImage.preserveAspect = true;
            existing.buttonImage.color = Color.white;
        }
        if (existing.badgeContainer != null) existing.badgeContainer.SetActive(false);
        Button button = existing.GetComponent<Button>();
        if (button != null)
        {
            SerializedObject serialized = new SerializedObject(button);
            SerializedProperty calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            for (int i = 0; i < calls.arraySize; i++)
            {
                SerializedProperty call = calls.GetArrayElementAtIndex(i);
                if (call.FindPropertyRelative("m_MethodName").stringValue == "OnMaterialSelected")
                    call.FindPropertyRelative("m_Arguments.m_ObjectArgument").objectReferenceValue = material;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(existing);
        return true;
    }
    private static void ValidateVisual(BridgeMaterialSO material)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject parent = new GameObject("Concrete Beam Validation");
        SceneManager.MoveGameObjectToScene(parent, preview);
        try
        {
            Bar bar = parent.AddComponent<Bar>();
            bar.StartPosition = Vector3.zero;
            bar.Initialize(material);
            bar.UpdateCreatingBar(new Vector3(8f, 0f, 0f));
            Renderer[] renderers = parent.GetComponentsInChildren<Renderer>();
            if (renderers.Length != 2) { Debug.LogError("[Concrete Beam] Expected two side-beam visuals."); return; }
            foreach (Renderer renderer in renderers)
            {
                Bounds bounds = renderer.bounds;
                if (Mathf.Abs(bounds.size.x - 8f) > .1f || bounds.size.y <= .01f || bounds.size.z <= .01f ||
                    Mathf.Abs(bounds.center.x - 4f) > .1f || Mathf.Abs(bounds.center.y) > .1f)
                { Debug.LogError("[Concrete Beam] Visual does not fit the placed endpoints: " + bounds); return; }
            }
            Debug.Log($"[Concrete Beam] PASS: two 8m visuals; size={renderers[0].bounds.size}, cost={bar.GetCost():0}, placed mass/m={material.GetPlacedMassPerMeter():0}, tension={material.maxTension:0}N, compression={material.maxCompression:0}N.");
        }
        finally { Object.DestroyImmediate(parent); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void ValidateConcreteDeckSupport(BridgeMaterialSO beam)
    {
        BridgeMaterialSO road = AssetDatabase.LoadAssetAtPath<BridgeMaterialSO>("Assets/BridgeBuilder/Data/Resources/Concrete Road.asset");
        if (road == null) { Debug.LogError("[Concrete Beam] Concrete Road is missing."); return; }
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject parent = new GameObject("Concrete Deck Support Validation");
        SceneManager.MoveGameObjectToScene(parent, preview);
        try
        {
            Point[] points = {
                TestPoint(parent.transform, new Vector3(-8f, 0f, 0f), true),
                TestPoint(parent.transform, Vector3.zero, false),
                TestPoint(parent.transform, new Vector3(8f, 0f, 0f), true),
                TestPoint(parent.transform, new Vector3(0f, 4f, 0f), false)
            };
            Bar[] bars = {
                TestBar(parent.transform, road, points[0], points[1]),
                TestBar(parent.transform, road, points[1], points[2]),
                TestBar(parent.transform, beam, points[0], points[3]),
                TestBar(parent.transform, beam, points[3], points[2]),
                TestBar(parent.transform, beam, points[1], points[3])
            };
            var normal = DeterministicBridgeStressSolver.Analyze(points, bars, 12000f, false, 31);
            var overload = DeterministicBridgeStressSolver.Analyze(points, bars, 2000000f, false, 31);
            var unbraced = DeterministicBridgeStressSolver.Analyze(points, new[] { bars[0], bars[1] }, 12000f, false, 31);
            if (normal == null || !normal.IsValid || !normal.IsStructurallyStable || normal.PeakStructuralStress >= 1f ||
                overload == null || overload.PeakStructuralStress <= 1f || unbraced == null || unbraced.IsStructurallyStable)
            { Debug.LogError("[Concrete Beam] Deck-support regression failed; inspect material limits and test geometry."); return; }
            Debug.Log($"[Concrete Beam] PASS: 16m triangulated Concrete Road supports a 12,000kg live load including dead weight (peak stress {normal.PeakStructuralStress:P1}); extreme overload exceeds capacity, and removing supports leaves the deck unstable.");
        }
        finally { Object.DestroyImmediate(parent); EditorSceneManager.ClosePreviewScene(preview); }
    }
    private static Point TestPoint(Transform parent, Vector3 position, bool anchor)
    {
        GameObject go = new GameObject("Test Joint");
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        Point point = go.AddComponent<Point>();
        point.isAnchor = point.originalIsAnchor = anchor;
        return point;
    }
    private static void ValidateLongConcreteTruss(BridgeMaterialSO beam, BridgeMaterialSO road)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject parent = new GameObject("50m Concrete Balance Validation");
        SceneManager.MoveGameObjectToScene(parent, preview);
        try
        {
            // Five 10m deck panels with 6m/8m-high Warren bracing, like the recorded bridge.
            foreach (float height in new[] { 6f, 8f })
            {
                List<Point> points = new List<Point>();
                List<Bar> bars = new List<Bar>();
                List<Bar> deck = new List<Bar>();
                Point[] lower = new Point[6], upper = new Point[5];
                for (int i = 0; i < lower.Length; i++)
                {
                    lower[i] = TestPoint(parent.transform, new Vector3(i * 10f, 0f, 0f), i == 0 || i == 5);
                    points.Add(lower[i]);
                }
                for (int i = 0; i < upper.Length; i++)
                {
                    upper[i] = TestPoint(parent.transform, new Vector3(i * 10f + 5f, height, 0f), false);
                    points.Add(upper[i]);
                    Bar roadBar = TestBar(parent.transform, road, lower[i], lower[i + 1]);
                    bars.Add(roadBar); deck.Add(roadBar);
                    bars.Add(TestBar(parent.transform, beam, lower[i], upper[i]));
                    bars.Add(TestBar(parent.transform, beam, upper[i], lower[i + 1]));
                    if (i > 0) bars.Add(TestBar(parent.transform, beam, upper[i - 1], upper[i]));
                }
                foreach (float load in new[] { 0f, 2000f, 12000f, 2000000f })
                {
                    var result = DeterministicBridgeStressSolver.Analyze(points, bars, load, false, 101);
                    bool overload = load > 12000f;
                    if (result == null || !result.IsValid || !result.IsStructurallyStable ||
                        (!overload && result.PeakStructuralStress >= .9f) ||
                        (overload && result.PeakStructuralStress <= 1f))
                    { Debug.LogError($"[Concrete Balance] FAIL: 50m span, height={height}, load={load}kg."); return; }
                    Debug.Log($"[Concrete Balance] PASS: 50m span, height={height}m, load={load}kg, peak={result.PeakStructuralStress:P1}.");
                }
                var unsupported = DeterministicBridgeStressSolver.Analyze(points, deck, 2000f, false, 31);
                if (unsupported == null || unsupported.IsStructurallyStable)
                { Debug.LogError("[Concrete Balance] Unsupported deck regression failed."); return; }
                Debug.Log("[Concrete Balance] PASS: unsupported deck remains unstable.");
                foreach (Bar bar in bars) Object.DestroyImmediate(bar.gameObject);
                foreach (Point point in points) Object.DestroyImmediate(point.gameObject);
            }
        }
        finally { Object.DestroyImmediate(parent); EditorSceneManager.ClosePreviewScene(preview); }
    }
    private static Bar TestBar(Transform parent, BridgeMaterialSO material, Point start, Point end)
    {
        GameObject go = new GameObject("Test Member");
        go.transform.SetParent(parent, false);
        Bar bar = go.AddComponent<Bar>();
        bar.materialData = material; bar.startPoint = start; bar.endPoint = end;
        return bar;
    }
}
#endif
