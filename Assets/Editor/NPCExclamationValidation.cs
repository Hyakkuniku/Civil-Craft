#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in detached rendering and native hit tests; never starts NPC progression or saves a scene.</summary>
public static class NPCExclamationValidation
{
    private const string Request = "Temp/npc-exclamation-validation-v5.request";
    private const string Report = "Temp/NPCExclamationValidation.txt";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static bool running;
    private static double nextCheck;

    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
        EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= CheckRequest; EditorApplication.update += CheckRequest; }

    private static void CheckRequest()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        ValidateFromMenu();
    }

    [MenuItem("Tools/Civil Craft/NPCs/Validate Exclamation Markers")]
    public static void ValidateFromMenu()
    {
        Directory.CreateDirectory("Temp");
        try { File.WriteAllText(Report, Validate()); Debug.Log("[NPC Exclamation] PASS: " + Report); }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
    }

    private static string Validate()
    {
        Check(!Busy, "Run in idle Edit Mode, outside Prefab Mode.");
        running = true;
        Scene active = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object selected = Selection.activeObject;
        var scenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene source = SceneManager.GetSceneAt(i);
            scenes.Add(source, Tuple.Create(source.isDirty, source.GetRootGameObjects()));
        }
        var activeManagers = (HashSet<NPCProgressionManager>)Field(typeof(NPCProgressionManager), "ActiveManagers").GetValue(null);
        NPCProgressionManager[] managersBefore = activeManagers.ToArray();
        var spawnRegistry = (HashSet<NPCSpawnerCondition>)Field(typeof(NPCSpawnConditionMonitor), "conditions").GetValue(null);
        NPCSpawnerCondition[] spawnConditionsBefore = spawnRegistry.ToArray();
        FieldInfo fusionSingleton = Field(typeof(FusionConnectionManager), "<Instance>k__BackingField");
        object fusionBefore = fusionSingleton.GetValue(null);
        var report = new StringBuilder();
        Scene preview = default;
        try
        {
            // Synchronous Edit Mode fixture only: do not inspect an unrelated live guest session.
            fusionSingleton.SetValue(null, null);
            // Clear before creating fixtures; existing spawn gates are restored
            // synchronously without evaluating rules, firing events or saving.
            spawnRegistry.Clear();
            preview = EditorSceneManager.NewPreviewScene();
            NPCProgressionManager npc = SyntheticNPC(preview);
            ValidateArrival(npc, report);
            ValidatePresence(preview, npc, spawnRegistry, report);
            ValidateProjection(report);
            ValidateHUDSourceGuard(report);
            Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1024, 768), new Vector2Int(1920, 1080) };
            string[] names = { "phone", "tablet", "desktop" };
            for (int i = 0; i < sizes.Length; i++) ValidatePresentation(preview, npc, sizes[i], names[i], report);
        }
        finally
        {
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            spawnRegistry.Clear();
            foreach (NPCSpawnerCondition condition in spawnConditionsBefore) spawnRegistry.Add(condition);
            fusionSingleton.SetValue(null, fusionBefore);
            Selection.objects = selection;
            Selection.activeObject = selected;
            running = false;
        }
        foreach (var pair in scenes)
            Check(pair.Key.isLoaded && pair.Key.isDirty == pair.Value.Item1 &&
                pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Item2), "A source scene changed during detached marker validation.");
        Check(SceneManager.GetActiveScene() == active, "The original active scene changed.");
        Check(activeManagers.SetEquals(managersBefore), "The live NPC registry changed.");
        Check(spawnRegistry.SetEquals(spawnConditionsBefore), "The live spawn-condition registry changed.");
        Check(ReferenceEquals(fusionSingleton.GetValue(null), fusionBefore), "The live multiplayer owner changed.");
        report.Insert(0, "PASS: detached NPC arrival/presence/spawn gates, native vector rendering, physical sizes, map projection, HUD overlap exclusion and click-through checks.\n");
        report.AppendLine("NOTE: No progression activation, coroutine, player input, save, account, network, scene save or released story changes ran.");
        return report.ToString();
    }

    private static NPCProgressionManager SyntheticNPC(Scene scene)
    {
        GameObject root = new GameObject("Synthetic NPC (no progression)");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.enabled = false;
        GameObject visualParent = new GameObject("Detached NPC model parent");
        SceneManager.MoveGameObjectToScene(visualParent, scene);
        visualParent.transform.SetParent(root.transform, false);
        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        model.name = "Detached NPC cube model";
        SceneManager.MoveGameObjectToScene(model, scene);
        model.transform.SetParent(visualParent.transform, false);
        Object.DestroyImmediate(model.GetComponent<Collider>());
        NPCProgressionManager npc = root.AddComponent<NPCProgressionManager>();
        Set(npc, "phases", new List<NPCProgressionPhase>
        {
            new NPCProgressionPhase { phaseId = "checked", showExclamationMark = true },
            new NPCProgressionPhase { phaseId = "unchecked", showExclamationMark = false }
        });
        Set(npc, "currentPhaseIndex", 0);
        root.SetActive(true);
        return npc;
    }

    private static void ValidateArrival(NPCProgressionManager npc, StringBuilder report)
    {
        Check(!new NPCProgressionPhase().showExclamationMark, "The checkbox must default off for legacy story phases.");
        Set(npc, "phaseHasArrived", false);
        Check(!npc.ShouldShowExclamationMark, "The checked phase showed a badge before arriving.");
        Set(npc, "phaseHasArrived", true);
        Check(npc.ShouldShowExclamationMark, "The checked arrived phase did not show a badge.");
        Set(npc, "currentPhaseIndex", 1);
        Check(!npc.ShouldShowExclamationMark, "The unchecked arrived phase showed a badge.");
        Set(npc, "currentPhaseIndex", 0);
        npc.enabled = false;
        Check(!npc.ShouldShowExclamationMark, "A disabled NPC showed a badge.");
        npc.enabled = true;
        npc.gameObject.SetActive(false);
        Check(!npc.ShouldShowExclamationMark, "An inactive NPC showed a badge.");
        npc.gameObject.SetActive(true);
        Set(npc, "currentPhaseIndex", -1);
        Check(!npc.ShouldShowExclamationMark, "An absent phase showed a badge.");
        Set(npc, "currentPhaseIndex", 0);
        Set(npc, "phaseHasArrived", false);
        Check(!npc.ShouldShowExclamationMark, "The next destination appeared before its arrival flag.");
        Set(npc, "phaseHasArrived", true);
        string source = File.ReadAllText("Assets/Script/Client/NPCProgressionManager.cs");
        int gate = source.IndexOf("public bool ShouldShowExclamationMark", StringComparison.Ordinal);
        int end = source.IndexOf(';', gate);
        Check(gate >= 0 && end > gate && source.Substring(gate, end - gate).Contains("!IsTravelling"),
            "The marker gate lost its movement-routine guard.");
        Check((Coroutine)Field(typeof(NPCProgressionManager), "movementRoutine").GetValue(npc) == null,
            "Native arrival fixture must not run a movement coroutine.");
        report.AppendLine("PASS: checkbox defaults off; unarrived, unchecked, absent-phase and disabled/inactive NPCs are hidden; checked arrived NPC is visible. Travelling guard is source-checked without starting a coroutine.");
    }

    private static void ValidatePresence(Scene scene, NPCProgressionManager npc,
        HashSet<NPCSpawnerCondition> spawnRegistry, StringBuilder report)
    {
        Transform visualParent = npc.transform.Find("Detached NPC model parent");
        GameObject model = visualParent.Find("Detached NPC cube model").gameObject;
        MeshRenderer renderer = model.GetComponent<MeshRenderer>();
        Material[] materials = renderer.sharedMaterials;
        MeshFilter filter = model.GetComponent<MeshFilter>();
        Mesh mesh = filter.sharedMesh;
        GameObject conditionObject = null, sibling = null, textObject = null;
        SkinnedMeshRenderer skinned = null;
        SpriteRenderer sprite = null;
        try
        {
            Check(Present(npc) && npc.ShouldShowExclamationMark, "An arrived NPC with an active model did not show its markers.");
            model.SetActive(false);
            Check(!Present(npc) && !npc.ShouldShowExclamationMark, "A hidden NPC model still showed world/map markers.");
            model.SetActive(true);
            renderer.enabled = false;
            Check(!Present(npc) && !npc.ShouldShowExclamationMark, "A disabled model renderer still showed world/map markers.");
            renderer.enabled = true;
            renderer.forceRenderingOff = true;
            Check(!Present(npc) && !npc.ShouldShowExclamationMark, "A force-hidden model renderer still showed world/map markers.");
            renderer.forceRenderingOff = false;
            visualParent.gameObject.SetActive(false);
            Check(!Present(npc) && !npc.ShouldShowExclamationMark, "An inactive visual parent still showed world/map markers.");
            visualParent.gameObject.SetActive(true);
            filter.sharedMesh = null;
            Check(!Present(npc) && !npc.ShouldShowExclamationMark, "An empty MeshFilter still made the NPC model present.");
            filter.sharedMesh = mesh;
            Object.DestroyImmediate(filter); filter = null;
            Check(!Present(npc) && !npc.ShouldShowExclamationMark, "A MeshRenderer without a MeshFilter still made the NPC model present.");
            filter = model.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            Object.DestroyImmediate(renderer);
            renderer = null;
            Check(!Present(npc) && !npc.ShouldShowExclamationMark, "A model without a MeshRenderer still showed world/map markers.");
            sprite = model.AddComponent<SpriteRenderer>();
            Check(!Present(npc), "A non-model SpriteRenderer falsely made the NPC present.");
            Object.DestroyImmediate(sprite); sprite = null;
            textObject = new GameObject("Detached NPC label only", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(textObject, scene);
            textObject.transform.SetParent(npc.transform, false);
            textObject.AddComponent<TMPro.TextMeshPro>();
            Check(textObject.GetComponent<MeshRenderer>() != null && !Present(npc), "A TMP label falsely made a missing NPC model present.");
            Object.DestroyImmediate(textObject); textObject = null;
            skinned = model.AddComponent<SkinnedMeshRenderer>();
            Check(!Present(npc) && !npc.ShouldShowExclamationMark, "An empty SkinnedMeshRenderer still made the NPC model present.");
            skinned.sharedMesh = mesh;
            Check(Present(npc), "An active SkinnedMeshRenderer model was not recognized.");
            skinned.enabled = false;
            Check(!Present(npc), "A disabled skinned model was still recognized as present.");
            skinned.enabled = true;
            skinned.forceRenderingOff = true;
            Check(!Present(npc), "A force-hidden skinned model was still recognized as present.");
            Object.DestroyImmediate(skinned); skinned = null;
            renderer = model.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;

            conditionObject = new GameObject("Detached read-only spawn gate");
            conditionObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(conditionObject, scene);
            NPCSpawnerCondition condition = conditionObject.AddComponent<NPCSpawnerCondition>();
            condition.npcRoot = npc.gameObject;
            condition.waitForManualSpawn = true;
            conditionObject.SetActive(true);
            spawnRegistry.Add(condition);
            int shown = 0, hidden = 0;
            condition.onShown.AddListener(() => shown++);
            condition.onHidden.AddListener(() => hidden++);
            Check(!condition.CanShowAttentionMarkerFor(null) && !NPCSpawnConditionMonitor.CanShowNPCAttentionMarker(null),
                "A null NPC was accepted by the presence/spawn gates.");
            Check(!condition.CanShowAttentionMarkerFor(npc.transform) && !Present(npc) && !npc.ShouldShowExclamationMark,
                "An unresolved wait-for-manual-spawn NPC showed markers before spawning.");
            condition.waitForManualSpawn = false;
            Check(!condition.CanShowAttentionMarkerFor(npc.transform) && !npc.ShouldShowExclamationMark,
                "Unresolved automatic spawn rules falsely confirmed an authored-active NPC.");
            Set(condition, "manualOverride", 1);
            Check(condition.CanShowAttentionMarkerFor(npc.transform) && npc.ShouldShowExclamationMark,
                "A positive manual spawn override did not permit a present NPC's markers.");
            Set(condition, "manualOverride", -1);
            Check(!condition.CanShowAttentionMarkerFor(npc.transform) && !npc.ShouldShowExclamationMark,
                "A negative manual spawn override still permitted an NPC marker.");
            Set(condition, "manualOverride", 0);
            Set(condition, "evaluated", true);
            Set(condition, "lastRuleVisibility", true);
            Check(condition.CanShowAttentionMarkerFor(npc.transform) && npc.ShouldShowExclamationMark,
                "Evaluated allowed spawn rules did not permit a present NPC's markers.");
            Set(condition, "lastRuleVisibility", false);
            Check(!condition.CanShowAttentionMarkerFor(npc.transform) && !npc.ShouldShowExclamationMark,
                "Evaluated denied spawn rules still permitted an NPC marker.");
            condition.enabled = false;
            Check(condition.CanShowAttentionMarkerFor(npc.transform) && npc.ShouldShowExclamationMark,
                "A deliberately disabled/debug-bypassed spawn component still denied the marker.");
            condition.enabled = true;

            sibling = new GameObject("Unrelated controlled sibling");
            SceneManager.MoveGameObjectToScene(sibling, scene);
            condition.npcRoot = sibling;
            Check(condition.CanShowAttentionMarkerFor(npc.transform) && npc.ShouldShowExclamationMark,
                "An unrelated controlled sibling's denied spawn rules hid this NPC's markers.");
            condition.npcRoot = model;
            Check(!condition.CanShowAttentionMarkerFor(npc.transform) && !npc.ShouldShowExclamationMark,
                "A controlled visual descendant's denied spawn rules were ignored.");
            Set(condition, "manualOverride", 1);
            Check(condition.CanShowAttentionMarkerFor(npc.transform) && npc.ShouldShowExclamationMark,
                "A controlled visual descendant did not recover after a positive manual override.");
            model.SetActive(false);
            Check(!condition.CanShowAttentionMarkerFor(npc.transform) && !npc.ShouldShowExclamationMark,
                "A manual override falsely made an inactive controlled visual present.");
            model.SetActive(true);
            Check(condition.CanShowAttentionMarkerFor(npc.transform) && npc.ShouldShowExclamationMark,
                "Markers did not recover after restoring the controlled visual.");
            Check(shown == 0 && hidden == 0 && !(bool)Field(typeof(NPCSpawnerCondition), "pendingVisibilitySave").GetValue(condition) &&
                Field(typeof(NPCSpawnerCondition), "restoredVisibilityData").GetValue(condition) == null &&
                (bool)Field(typeof(NPCSpawnerCondition), "evaluated").GetValue(condition) &&
                !(bool)Field(typeof(NPCSpawnerCondition), "lastRuleVisibility").GetValue(condition) &&
                (int)Field(typeof(NPCSpawnerCondition), "manualOverride").GetValue(condition) == 1,
                "Read-only presence/spawn queries mutated visibility, evaluated rules, dispatched events or queued a save.");
            report.AppendLine("PASS: real native shared marker gate requires active MeshRenderer/SkinnedMeshRenderer NPC visuals, not TMP labels/SpriteRenderers; hidden, missing, disabled, force-hidden and inactive-parent models suppress both marker paths. Read-only spawn gates honor unresolved/manual/evaluated states and controlled hierarchy, ignore unrelated siblings, recover after visibility restoration and do not evaluate/save/dispatch visibility events.");
        }
        finally
        {
            if (conditionObject != null)
            {
                spawnRegistry.Remove(conditionObject.GetComponent<NPCSpawnerCondition>());
                Object.DestroyImmediate(conditionObject);
            }
            if (sibling != null) Object.DestroyImmediate(sibling);
            if (textObject != null) Object.DestroyImmediate(textObject);
            if (skinned != null) Object.DestroyImmediate(skinned);
            if (sprite != null) Object.DestroyImmediate(sprite);
            visualParent.gameObject.SetActive(true);
            model.SetActive(true);
            if (filter == null) filter = model.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            if (renderer == null) renderer = model.AddComponent<MeshRenderer>();
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.sharedMaterials = materials;
            Check(spawnRegistry.Count == 0, "Detached spawn-condition fixture leaked a registry entry.");
        }
        Check(Present(npc) && npc.ShouldShowExclamationMark, "The original detached model/gate state was not restored.");
    }

    private static bool Present(NPCProgressionManager npc) => (bool)Call(npc, "HasPresentNPCVisual");

    private static void ValidateProjection(StringBuilder report)
    {
        Rect image = new Rect(-200, -100, 400, 200);
        Check(Project(new Vector3(.5f, .5f, 1), image, new Rect(0, 0, 1, 1), out Vector2 point) && point == Vector2.zero,
            "Center projection does not follow the map viewport.");
        Check(Project(new Vector3(.75f, .25f, 1), image, new Rect(0, 0, 1, 1), out point) && point == new Vector2(100, -50),
            "Map viewport coordinates were projected incorrectly.");
        Check(Project(new Vector3(.25f, .75f, 1), image, new Rect(1, 1, -1, -1), out point) && point == new Vector2(100, -50),
            "Flipped render-texture UVs were projected incorrectly.");
        Check(Project(new Vector3(.5f, .5f, 1), image, new Rect(.25f, .25f, .5f, .5f), out point) && point == Vector2.zero,
            "Cropped render-texture UVs were projected incorrectly.");
        foreach (Vector3 viewport in new[] { new Vector3(-.1f, .5f, 1), new Vector3(1.1f, .5f, 1),
            new Vector3(.5f, .5f, -1), new Vector3(float.NaN, .5f, 1), new Vector3(.5f, float.PositiveInfinity, 1) })
            Check(!Project(viewport, image, new Rect(0, 0, 1, 1), out point), "An offscreen/invalid NPC was clamped to a false map location.");
        Check(!Project(new Vector3(.1f, .5f, 1), image, new Rect(.25f, .25f, .5f, .5f), out point), "A cropped-out NPC was shown.");
        Check(!Project(new Vector3(.5f, .5f, 1), image, new Rect(0, 0, 0, 1), out point), "Degenerate UVs were accepted.");
        foreach (float pixels in new[] { 22f, 28f })
            foreach (float canvasScale in new[] { .5f, .8f, 1f, 2f })
                foreach (float relativeScale in new[] { .5f, 1f, 1.5f })
                {
                    Vector2 size = MarkerSize(pixels, canvasScale, Vector3.one * relativeScale, Vector3.one);
                    Check(Mathf.Abs(size.x * relativeScale * canvasScale - pixels) < .01f &&
                        Mathf.Abs(size.y * relativeScale * canvasScale - pixels) < .01f, "Map marker physical size changes with Canvas or map scaling.");
                }
        report.AppendLine("PASS: real map projection helper handles center/corners, UV flips/crops, offscreen/behind/invalid input; 22/28 physical pixels survive Canvas and ancestor scaling.");
    }

    private static void ValidatePresentation(Scene scene, NPCProgressionManager npc, Vector2Int size, string name, StringBuilder report)
    {
        GameObject canvasObject = new GameObject("Detached NPC Marker Canvas", typeof(RectTransform));
        canvasObject.SetActive(false);
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvasObject.AddComponent<GraphicRaycaster>();
        RectTransform root = canvasObject.transform as RectTransform;
        root.localScale = Vector3.one;
        root.localRotation = Quaternion.identity;
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f;
        root.anchoredPosition3D = Vector3.zero;
        root.sizeDelta = size;
        GameObject cameraObject = new GameObject("Detached NPC Marker Camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.scene = scene; camera.enabled = false; camera.orthographic = true;
        camera.transform.position = new Vector3(0, 0, -1000);
        camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
        camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.92f, .87f, .76f);
        camera.orthographicSize = size.y * .5f; camera.aspect = (float)size.x / size.y;
        canvas.worldCamera = camera;
        RenderTexture target = new RenderTexture(size.x, size.y, 24);
        target.Create(); camera.targetTexture = target;
        Texture2D mapTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        mapTexture.SetPixel(0, 0, new Color(.10f, .27f, .42f)); mapTexture.Apply();
        var maps = new List<ExpandedMinimapController>();
        var badges = new List<NPCExclamationGraphic>();
        var buttons = new List<Button>();
        GameObject eventObject = null;
        Texture2D captured = null;
        try
        {
            Image ground = Surface(root, "Paper backdrop", new Color(.92f, .87f, .76f));
            ground.rectTransform.sizeDelta = size;
            BuildMap(scene, canvas, root, npc, mapTexture, new Vector2(-size.x * .19f, 0),
                new Vector2(size.x * .55f, size.y * .77f), 28, maps, badges, buttons);
            BuildMap(scene, canvas, root, npc, mapTexture, new Vector2(size.x * .32f, size.y * .28f),
                new Vector2(size.x * .24f, size.y * .26f), 22, maps, badges, buttons);
            Image world = Surface(root, "Gameplay sky", new Color(.47f, .68f, .75f));
            world.rectTransform.sizeDelta = new Vector2(size.x * .24f, size.y * .40f);
            world.rectTransform.anchoredPosition = new Vector2(size.x * .32f, -size.y * .18f);
            Image body = Surface(world.transform, "Synthetic NPC silhouette", new Color(.28f, .22f, .16f));
            body.rectTransform.sizeDelta = new Vector2(36, 80); body.rectTransform.anchoredPosition = new Vector2(0, -22);
            Button underHead = ButtonUnder(world.transform, new Vector2(0, 65), new Vector2(32, 32));
            buttons.Add(underHead);
            NPCExclamationGraphic overhead = Badge(world.transform, "Above NPC", Vector2.one * 32, new Vector2(0, 65));
            badges.Add(overhead);
            foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            canvasObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            ValidateHUDReservations(canvas, maps[1], badges, name, report);
            foreach (Graphic graphic in canvasObject.GetComponentsInChildren<Graphic>()) { graphic.SetAllDirty(); graphic.Rebuild(CanvasUpdate.PreRender); }
            Canvas.ForceUpdateCanvases(); Render(camera, target); Canvas.ForceUpdateCanvases();
            captured = ReadPixels(target);
            float[] diameters = { 28, 22, 32 };
            for (int i = 0; i < badges.Count; i++)
            {
                NPCExclamationGraphic badge = badges[i];
                Vector3[] corners = new Vector3[4]; badge.rectTransform.GetWorldCorners(corners);
                float width = Vector3.Distance(camera.WorldToScreenPoint(corners[0]), camera.WorldToScreenPoint(corners[3]));
                Check(Mathf.Abs(width - diameters[i]) < .1f, "Badge is not its declared physical pixel size: " + badge.name);
                Check(!badge.raycastTarget, "Badge unexpectedly blocks input.");
                Check(badge.depth >= 0 && !badge.canvasRenderer.cull, "Badge is culled or missing native Canvas depth.");
                Vector2 pixel = camera.WorldToScreenPoint(badge.transform.position);
                Check(PixelCounts(captured, pixel, diameters[i], true) > diameters[i] * diameters[i] * .30f,
                    "Gold disc is not visibly rendered at " + name + ": " + badge.name);
                Check(PixelCounts(captured, pixel, diameters[i], false) > diameters[i] * diameters[i] * .13f,
                    "Dark outline/exclamation is not visibly rendered at " + name + ": " + badge.name);
            }
            eventObject = new GameObject("Detached Marker Event System");
            eventObject.SetActive(false); SceneManager.MoveGameObjectToScene(eventObject, scene);
            EventSystem events = eventObject.AddComponent<EventSystem>();
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                var pointer = new PointerEventData(events) { position = camera.WorldToScreenPoint(button.transform.position), button = PointerEventData.InputButton.Left };
                var hits = new List<RaycastResult>(); canvasObject.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Check(hits.All(hit => hit.gameObject.GetComponent<NPCExclamationGraphic>() == null), "A marker captured the native UI raycast.");
                Check(hits.Count > 0 && hits.OrderByDescending(hit => hit.depth).First().gameObject == button.gameObject,
                    "Button directly below the visible marker is blocked.");
                int clicked = 0; button.onClick.AddListener(() => clicked++);
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Check(clicked == 1, "The underlying button did not receive its native click exactly once.");
            }
            File.WriteAllBytes("Temp/npc-exclamation-" + name + ".png", captured.EncodeToPNG());
            report.AppendLine("PASS: " + name + " " + size.x + "x" + size.y + ": visible font-free 22px compact/28px expanded/32px overhead badges, real geometry/pixels and native click-through to underlying buttons.");
        }
        finally
        {
            if (captured != null) Object.DestroyImmediate(captured);
            if (eventObject != null) Object.DestroyImmediate(eventObject);
            camera.targetTexture = null;
            Object.DestroyImmediate(canvasObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(mapTexture);
            target.Release(); Object.DestroyImmediate(target);
        }
    }

    private static void ValidateHUDSourceGuard(StringBuilder report)
    {
        string source = File.ReadAllText("Assets/Script/Client/NPCExclamationMarker.cs");
        int bounds = source.IndexOf("Rect screenBounds = Rect.MinMaxRect(point.x - half, point.y - half,", StringComparison.Ordinal);
        int exclusion = source.IndexOf("if (UIReservedRegionLayout.OverlapsVisibleReservation(screenBounds, 2f)) { Hide(); return; }", StringComparison.Ordinal);
        int activation = source.IndexOf("if (!canvasObject.activeSelf) canvasObject.SetActive(true);", StringComparison.Ordinal);
        Check(bounds >= 0 && source.Substring(bounds).Contains("point.x + half, point.y + half)"),
            "Overhead exclusion must use the full badge bounds, not only its projected center.");
        Check(exclusion > bounds && activation > exclusion,
            "Overhead exclusion must hide and return before activating the badge Canvas.");
        string mapSource = File.ReadAllText("Assets/Script/Player/ExpandedMinimapController.cs");
        Check(!mapSource.Contains("OverlapsVisibleReservation"), "Map-specific badges must not exclude themselves from their map.");
        report.AppendLine("PASS: real overhead source excludes its complete 32px bounds with a 2px HUD gap before activation; map-specific badges retain their independent path. No live LateUpdate or singleton input gates are invoked.");
    }

    private static void ValidateHUDReservations(Canvas canvas, ExpandedMinimapController controller,
        List<NPCExclamationGraphic> badges, string name, StringBuilder report)
    {
        RawImage image = (RawImage)Field(typeof(ExpandedMinimapController), "mapImage").GetValue(controller);
        RectTransform minimap = image.rectTransform;
        var registry = (HashSet<RectTransform>)Field(typeof(UIReservedRegionLayout), "reservedRects").GetValue(null);
        RectTransform[] reservationsBefore = registry.ToArray();
        Vector3 positionBefore = minimap.anchoredPosition3D;
        Vector3 scaleBefore = minimap.localScale;
        Vector2 sizeBefore = minimap.sizeDelta;
        bool activeBefore = minimap.gameObject.activeSelf;
        bool canvasBefore = canvas.enabled;
        int orderBefore = canvas.sortingOrder;
        Vector2[] badgeSizes = badges.Select(badge => badge.rectTransform.sizeDelta).ToArray();
        Vector3[] badgePositions = badges.Select(badge => badge.rectTransform.anchoredPosition3D).ToArray();
        bool[] badgeActive = badges.Select(badge => badge.gameObject.activeSelf).ToArray();
        CanvasGroup mapVisibility = null, ancestorVisibility = null;
        try
        {
            // The fixture is synchronous and owns this detached Canvas. Use the
            // existing registry directly so real LayoutChanging subscribers are
            // not notified or asked to rearrange source-scene UI.
            registry.Clear();
            registry.Add(minimap);
            Check(UIReservedRegionLayout.TryGetScreenRect(minimap, out Rect original), "Detached minimap has no native screen bounds.");
            Check(Mathf.Abs(original.width - sizeBefore.x) < .1f && Mathf.Abs(original.height - sizeBefore.y) < .1f,
                "Detached minimap physical geometry does not match its test resolution.");
            Rect center = Bounds(original.center);
            Check(UIReservedRegionLayout.OverlapsVisibleReservation(center), "A centered overhead badge can cover the minimap.");
            Vector2 edgePoint = new Vector2(original.xMin - 8f, original.center.y);
            Vector2 cornerPoint = new Vector2(original.xMin - 8f, original.yMin - 8f);
            Check(!original.Contains(edgePoint) && UIReservedRegionLayout.OverlapsVisibleReservation(Bounds(edgePoint)),
                "An overhead badge with its center outside can spill across a minimap edge.");
            Check(!original.Contains(cornerPoint) && UIReservedRegionLayout.OverlapsVisibleReservation(Bounds(cornerPoint)),
                "An overhead badge with its center outside can spill across a minimap corner.");
            Rect onePixelGap = new Rect(original.xMax + 1f, original.center.y - 16f, 32f, 32f);
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(onePixelGap) &&
                UIReservedRegionLayout.OverlapsVisibleReservation(onePixelGap, 2f), "The 2px HUD safety gap is not respected.");
            Rect outside = new Rect(original.xMax + 3f, original.center.y - 16f, 32f, 32f);
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(outside, 2f), "An overhead badge outside the minimap is incorrectly hidden.");
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(new Rect(original.xMax, original.center.y - 16f, 32f, 32f)),
                "Non-overlapping edge contact must remain visible without padding.");
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(new Rect(original.center, Vector2.zero)),
                "An empty screen rectangle must not obscure gameplay.");

            minimap.localScale = scaleBefore * .5f;
            Canvas.ForceUpdateCanvases();
            Check(UIReservedRegionLayout.TryGetScreenRect(minimap, out Rect scaled) &&
                Mathf.Abs(scaled.width - original.width * .5f) < .1f && Mathf.Abs(scaled.height - original.height * .5f) < .1f,
                "HUD exclusion did not measure the scaled minimap's physical geometry.");
            Check(UIReservedRegionLayout.OverlapsVisibleReservation(Bounds(scaled.center)), "The scaled minimap center was not reserved.");
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(new Rect(original.xMin + 2f, original.center.y - 16f, 32f, 32f), 2f),
                "HUD exclusion uses stale minimap bounds after scaling.");
            minimap.localScale = scaleBefore;
            minimap.anchoredPosition3D = positionBefore + new Vector3(-original.width - 64f, 0f, 0f);
            Canvas.ForceUpdateCanvases();
            Check(UIReservedRegionLayout.TryGetScreenRect(minimap, out Rect moved) &&
                Mathf.Abs(moved.center.x - original.center.x + original.width + 64f) < .1f,
                "Detached minimap relocation did not update the native screen bounds.");
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f) &&
                UIReservedRegionLayout.OverlapsVisibleReservation(Bounds(moved.center), 2f),
                "HUD exclusion uses stale minimap bounds after repositioning.");
            minimap.anchoredPosition3D = positionBefore;
            Canvas.ForceUpdateCanvases();

            minimap.gameObject.SetActive(false);
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "An inactive minimap incorrectly hides the overhead badge.");
            minimap.gameObject.SetActive(true);
            Check(UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "A restored minimap did not reserve its screen bounds.");
            mapVisibility = minimap.gameObject.AddComponent<CanvasGroup>();
            mapVisibility.alpha = 0f;
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "A transparent minimap incorrectly hides the overhead badge.");
            mapVisibility.alpha = 1f;
            ancestorVisibility = canvas.gameObject.AddComponent<CanvasGroup>();
            ancestorVisibility.alpha = 0f;
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "A hidden minimap parent incorrectly hides the overhead badge.");
            mapVisibility.ignoreParentGroups = true;
            Check(UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "Visible ignore-parent-opacity minimap geometry was not reserved.");
            mapVisibility.ignoreParentGroups = false;
            ancestorVisibility.alpha = 1f;
            canvas.enabled = false;
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "A disabled minimap Canvas incorrectly hides the overhead badge.");
            canvas.enabled = true;
            Check(UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "Overhead exclusion did not recover after restoring the minimap Canvas.");
            registry.Remove(minimap);
            Check(!UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "An unregistered minimap incorrectly hides the overhead badge.");
            registry.Add(minimap);
            Check(UIReservedRegionLayout.OverlapsVisibleReservation(center, 2f), "Overhead exclusion did not recover after restoring the minimap reservation.");

            for (int i = 0; i < 2; i++)
                Check(badges[i].gameObject.activeSelf == badgeActive[i] && badges[i].gameObject.activeInHierarchy &&
                    badges[i].rectTransform.sizeDelta == badgeSizes[i] && badges[i].rectTransform.anchoredPosition3D == badgePositions[i] &&
                    !badges[i].raycastTarget, "HUD exclusion changed an independent map-specific marker.");
            Check(canvas.sortingOrder == orderBefore, "HUD exclusion altered the shared Canvas render order.");
            report.AppendLine("PASS: " + name + ": real HUD geometry excludes centered, edge/corner-spilling 32px overhead badges plus 2px padding; tracks minimap movement/scaling; inactive/transparent/disabled/unregistered maps do not obstruct; restored maps recover. Compact/expanded map badges and Canvas order remain unchanged.");
        }
        finally
        {
            minimap.anchoredPosition3D = positionBefore;
            minimap.localScale = scaleBefore;
            minimap.sizeDelta = sizeBefore;
            minimap.gameObject.SetActive(activeBefore);
            canvas.enabled = canvasBefore;
            if (mapVisibility != null) Object.DestroyImmediate(mapVisibility);
            if (ancestorVisibility != null) Object.DestroyImmediate(ancestorVisibility);
            registry.Clear();
            foreach (RectTransform reservation in reservationsBefore) registry.Add(reservation);
            Check(registry.SetEquals(reservationsBefore), "The live HUD reservation registry changed during detached validation.");
            Canvas.ForceUpdateCanvases();
        }
    }

    private static Rect Bounds(Vector2 center) => new Rect(center - Vector2.one * 16f, Vector2.one * 32f);

    private static void BuildMap(Scene scene, Canvas canvas, RectTransform parent, NPCProgressionManager npc, Texture texture,
        Vector2 center, Vector2 size, float pixels, List<ExpandedMinimapController> controllers,
        List<NPCExclamationGraphic> badges, List<Button> buttons)
    {
        RawImage image = Child(parent, "Synthetic " + (pixels == 22 ? "compact" : "expanded") + " map").AddComponent<RawImage>();
        image.texture = texture; image.raycastTarget = false;
        image.rectTransform.sizeDelta = size; image.rectTransform.anchoredPosition = center;
        Image island = Surface(image.transform, "Low-poly island silhouette", new Color(.61f, .69f, .32f));
        island.rectTransform.sizeDelta = size * new Vector2(.55f, .6f); island.rectTransform.localRotation = Quaternion.Euler(0, 0, 7);
        Button button = ButtonUnder(image.transform, Vector2.zero, Vector2.one * pixels);
        GameObject owner = new GameObject("Detached map projection owner");
        owner.SetActive(false); SceneManager.MoveGameObjectToScene(owner, scene);
        owner.transform.SetParent(parent, false);
        ExpandedMinimapController controller = owner.AddComponent<ExpandedMinimapController>(); controller.enabled = false;
        Set(controller, "mapImage", image); Set(controller, "owningCanvas", canvas);
        Call(controller, "EnsureNPCExclamationLayer");
        RectTransform layer = (RectTransform)Field(typeof(ExpandedMinimapController), "npcMarkerLayer").GetValue(controller);
        Check(layer != null && layer.GetComponent<RectMask2D>() != null && !layer.GetComponent<CanvasGroup>().blocksRaycasts,
            "The real map marker layer is not clipped/noninteractive.");
        RectTransform marker = (RectTransform)Call(controller, "CreateNPCExclamationMapMarker", npc);
        Check(Project(new Vector3(.5f, .5f, 1), image.rectTransform.rect, image.uvRect, out Vector2 position), "Synthetic map center could not project.");
        marker.anchoredPosition = position;
        marker.sizeDelta = MarkerSize(pixels, canvas.scaleFactor, layer.lossyScale, canvas.transform.lossyScale);
        badges.Add(marker.GetComponent<NPCExclamationGraphic>()); buttons.Add(button); controllers.Add(controller);
    }

    private static int PixelCounts(Texture2D texture, Vector2 center, float diameter, bool gold)
    {
        int count = 0, half = Mathf.CeilToInt(diameter * .5f);
        for (int y = Mathf.RoundToInt(center.y) - half; y <= Mathf.RoundToInt(center.y) + half; y++)
            for (int x = Mathf.RoundToInt(center.x) - half; x <= Mathf.RoundToInt(center.x) + half; x++)
            {
                if (x < 0 || y < 0 || x >= texture.width || y >= texture.height) continue;
                Color pixel = texture.GetPixel(x, y);
                // Accommodate linear and gamma render targets without assuming
                // an encoded screenshot's exact RGB values. Gold/ink remain
                // distinct from the pale paper button under the badge.
                if (gold ? pixel.r > .80f && pixel.g > .50f && pixel.b < .70f && pixel.r > pixel.b + .20f :
                    pixel.r < .70f && pixel.g < .60f && pixel.b < .50f) count++;
            }
        return count;
    }

    private static Button ButtonUnder(Transform parent, Vector2 position, Vector2 size)
    {
        Image image = Surface(parent, "Underlying map/game button", new Color(.88f, .80f, .62f));
        image.raycastTarget = true; image.rectTransform.anchoredPosition = position; image.rectTransform.sizeDelta = size;
        Button button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return button;
    }
    private static NPCExclamationGraphic Badge(Transform parent, string name, Vector2 size, Vector2 position)
    {
        GameObject child = Child(parent, name);
        // Match the real overhead/map constructors, which explicitly add their
        // CanvasRenderer before the custom Graphic is initialized.
        child.AddComponent<CanvasRenderer>();
        NPCExclamationGraphic graphic = child.AddComponent<NPCExclamationGraphic>();
        graphic.raycastTarget = false; graphic.rectTransform.sizeDelta = size; graphic.rectTransform.anchoredPosition = position;
        return graphic;
    }
    private static GameObject Child(Transform parent, string name)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
        child.transform.SetParent(parent, false);
        RectTransform rect = child.transform as RectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        return child;
    }
    private static Image Surface(Transform parent, string name, Color color)
    {
        Image graphic = Child(parent, name).AddComponent<Image>(); graphic.color = color; graphic.raycastTarget = false;
        return graphic;
    }
    private static bool Project(Vector3 viewport, Rect image, Rect uv, out Vector2 point)
    {
        object[] arguments = { viewport, image, uv, Vector2.zero };
        bool result = (bool)Method(typeof(ExpandedMinimapController), "TryProjectNPCMapPoint").Invoke(null, arguments);
        point = (Vector2)arguments[3]; return result;
    }
    private static Vector2 MarkerSize(float pixels, float scale, Vector3 layer, Vector3 canvas) =>
        (Vector2)Method(typeof(ExpandedMinimapController), "GetNPCMarkerCanvasSize").Invoke(null, new object[] { pixels, scale, layer, canvas });
    private static FieldInfo Field(Type type, string name) => type.GetField(name, Flags) ?? throw new MissingFieldException(type.Name, name);
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, Flags) ?? throw new MissingMethodException(type.Name, name);
    private static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
    private static object Call(object target, string name, params object[] values) => Method(target.GetType(), name).Invoke(target, values);
    private static void Render(Camera camera, RenderTexture target)
    {
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        else camera.Render();
    }
    private static Texture2D ReadPixels(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active;
        Texture2D texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); return texture; }
        finally { RenderTexture.active = previous; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
