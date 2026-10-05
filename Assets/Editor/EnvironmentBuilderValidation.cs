using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in editor regressions; all changes and Undo operations use a disposable additive scene.</summary>
public static class EnvironmentBuilderValidation
{
    private const string Request = "Temp/EnvironmentBuilderValidation.request";
    private const string Report = "Temp/EnvironmentBuilderValidation.txt";
    private const string Menu = "Tools/Civil Craft/Validate Environment Builder";
    private const string CactusPath = "Assets/Prefabs/MAPS/Canyon Crossing/Environemnt/Props/cc_cactus1.prefab";
    private const string RockPath = "Assets/Prefabs/MAPS/Canyon Crossing/Environemnt/Props/rock(1).prefab";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(0f, 10000f, 0f);
    private static double nextCheck;
    private static bool running;
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode ||
        EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || Busy) return;
        File.Delete(Request);
        WriteReport();
    }

    [MenuItem(Menu)]
    private static void RunFromMenu() => WriteReport();

    [MenuItem(Menu, true)]
    private static bool CanRunFromMenu() => !Busy;

    private static void WriteReport()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            File.WriteAllText(Report, Run());
            Debug.Log("[Environment Builder] PASS. Report: " + Report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    public static string Run()
    {
        Check(!Busy, "Wait until Play Mode, Prefab Mode, compilation, imports and player builds have stopped.");
        running = true;
        Scene activeScene = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object activeSelection = Selection.activeObject;
        bool toolsHidden = Tools.hidden;
        var scenes = new Dictionary<Scene, SceneSnapshot>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(scene, new SceneSnapshot(scene));
        }
        Scene temporary = default;
        GameObject fixture = null;
        CivilCraftEnvironmentBuilder builder = null;
        CivilCraftEnvironmentPalette profile = null;
        var report = new StringBuilder();
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        int fixtureUndoGroup = Undo.GetCurrentGroup();
        try
        {
            temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            fixture = new GameObject("Environment Builder Validation (Temporary)");
            SceneManager.MoveGameObjectToScene(fixture, temporary);
            fixture.transform.position = Origin;
            builder = ScriptableObject.CreateInstance<CivilCraftEnvironmentBuilder>();
            builder.hideFlags = HideFlags.HideAndDontSave;
            GameObject cactus = AssetDatabase.LoadAssetAtPath<GameObject>(CactusPath);
            GameObject rock = AssetDatabase.LoadAssetAtPath<GameObject>(RockPath);
            Check(CivilCraftEnvironmentBuilder.IsPrefab(cactus) && CivilCraftEnvironmentBuilder.IsPrefab(rock),
                "The authored cactus and rock prefab fixtures must be available as persistent Project assets.");
            var cactusStamp = new AssetSnapshot(CactusPath, cactus);
            var rockStamp = new AssetSnapshot(RockPath, rock);

            ValidateSurface(builder, fixture.transform, cactus, report, out Transform canyon, out Vector3 ground);
            ValidateShortcuts(builder, canyon, ground, cactus, rock, report);
            ValidatePlacementAndStroke(builder, canyon, ground, cactus, rock, report);
            ValidateSetupActions(builder, fixture.transform, report);
            profile = ValidateProfile(builder, cactus, rock, report);
            cactusStamp.AssertUnchanged();
            rockStamp.AssertUnchanged();
        }
        finally
        {
            try
            {
                if (builder != null) builder.EndStroke();
                Undo.FlushUndoRecordObjects();
                // Revert only groups created after entry; never clear or walk the user's older history.
                Undo.RevertAllDownToGroup(fixtureUndoGroup);
            }
            finally
            {
                if (builder != null) Object.DestroyImmediate(builder);
                if (profile != null) Object.DestroyImmediate(profile);
                if (fixture != null) Object.DestroyImmediate(fixture);
                if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
                if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
                Selection.objects = selection;
                Selection.activeObject = activeSelection;
                Tools.hidden = toolsHidden;
                Physics.SyncTransforms();
                Undo.IncrementCurrentGroup();
                running = false;
            }
        }
        Check(SceneManager.sceneCount == scenes.Count, "Validation changed the number of user-loaded scenes.");
        foreach (var scene in scenes) scene.Value.AssertUnchanged(scene.Key);
        Check(SceneManager.GetActiveScene() == activeScene && Tools.hidden == toolsHidden,
            "Validation did not restore the active scene and Scene tools visibility.");
        Check(Selection.activeObject == activeSelection && SameObjects(Selection.objects, selection),
            "Validation did not restore the user's selection.");
        report.AppendLine("PASS: Temporary additive scene and scoped Undo fixtures removed; original loaded scene roots/dirty state, active scene, selection, Scene tools and authored prefab files preserved. No scene/prefab/palette saving, dirt-path rebuild or navigation baking executed.");
        report.AppendLine("Completed UTC: " + DateTime.UtcNow.ToString("O"));
        return report.ToString();
    }

    private static void ValidateSurface(CivilCraftEnvironmentBuilder builder, Transform fixture,
        GameObject projectPrefab, StringBuilder report, out Transform canyon, out Vector3 ground)
    {
        GameObject floor = Primitive(fixture, "Scale-100 rotated Canyon");
        canyon = floor.transform;
        canyon.position = Origin;
        canyon.rotation = Quaternion.Euler(0f, 37f, 0f);
        canyon.localScale = Vector3.one * 100f;
        ground = Origin + Vector3.up * 50f;
        GameObject obstacle = Primitive(canyon, "Larger prop above surface (must be ignored)");
        obstacle.transform.localScale = new Vector3(3f, .02f, 3f);
        obstacle.transform.position = ground + Vector3.up * 5f;
        Ray ray = new Ray(ground + Vector3.up * 50f, Vector3.down);
        Physics.SyncTransforms();
        builder.SetCanyon(canyon);
        Check(Get<Transform>(builder, "surface") == canyon && CivilCraftEnvironmentBuilder.FindSurface(canyon) == canyon,
            "A canyon with its own mesh/collider was replaced by its larger prop child.");
        Check(CivilCraftEnvironmentBuilder.HasSurfaceCollider(canyon) &&
            builder.Surface(ray, out Vector3 hit, out Vector3 normal) && Vector3.Distance(hit, ground) < .01f &&
            Vector3.Angle(normal, Vector3.up) < .01f, "The exact canyon surface was not hit underneath a larger child collider.");
        var collider = floor.GetComponent<BoxCollider>();
        collider.enabled = false;
        Physics.SyncTransforms();
        Check(!CivilCraftEnvironmentBuilder.HasSurfaceCollider(canyon) && !builder.Surface(ray, out _, out _),
            "A disabled surface collider or its active prop child received paint hits.");
        collider.enabled = true;
        collider.isTrigger = true;
        Physics.SyncTransforms();
        Check(!CivilCraftEnvironmentBuilder.HasSurfaceCollider(canyon) && !builder.Surface(ray, out _, out _),
            "A trigger surface received paint hits.");
        collider.isTrigger = false;
        floor.SetActive(false);
        Check(!CivilCraftEnvironmentBuilder.HasSurfaceCollider(canyon) && !builder.Surface(ray, out _, out _),
            "An inactive canyon surface received paint hits.");
        floor.SetActive(true);
        GameObject outside = Primitive(fixture, "Collider outside Canyon");
        outside.transform.position = ground;
        Set(builder, "surface", outside.transform);
        Physics.SyncTransforms();
        Check(!builder.Surface(ray, out _, out _), "A collider outside the assigned canyon received paint hits.");
        Object.DestroyImmediate(outside);
        builder.SetCanyon(projectPrefab.transform);
        Check(Get<Transform>(builder, "parent") == null && Get<Transform>(builder, "surface") == null &&
            CivilCraftEnvironmentBuilder.FindSurface(projectPrefab.transform) == null,
            "A persistent Project prefab was accepted as a scene canyon.");
        builder.SetCanyon(canyon);
        Set(builder, "prefabs", new List<GameObject> { projectPrefab });
        Set(builder, "selectedIndex", 0);
        int children = canyon.childCount;
        Check(CivilCraftEnvironmentBuilder.IsSupportedParentScale(canyon), "A positive uniform scale-100 canyon was rejected.");
        foreach (Vector3 invalidScale in new[] { new Vector3(100f, 80f, 100f), new Vector3(-100f, 100f, 100f), Vector3.zero })
        {
            canyon.localScale = invalidScale;
            Check(!CivilCraftEnvironmentBuilder.IsSupportedParentScale(canyon) &&
                builder.Place(ground, Vector3.up) == null && canyon.childCount == children,
                "Nonuniform, mirrored or zero parent scale was allowed to distort a prefab placement.");
        }
        canyon.localScale = Vector3.one * 100f;
        Quaternion rotation = canyon.rotation;
        canyon.rotation = Quaternion.Euler(0f, 37f, 25f);
        Physics.SyncTransforms();
        Set(builder, "maximumSlope", 20f);
        Check(!builder.Surface(ray, out _, out _), "A 25-degree canyon slope passed a 20-degree slope limit.");
        Set(builder, "maximumSlope", 30f);
        Check(builder.Surface(ray, out _, out Vector3 slopeNormal) &&
            Mathf.Abs(Vector3.Angle(Vector3.up, slopeNormal) - 25f) < .1f,
            "A 25-degree canyon slope failed a 30-degree slope limit.");
        canyon.rotation = rotation;
        Set(builder, "maximumSlope", 60f);
        SceneVisibilityManager.instance.Hide(floor, false);
        Check(!builder.Surface(ray, out _, out _), "A Scene-hidden surface received paint hits.");
        SceneVisibilityManager.instance.Show(floor, false);
        Physics.SyncTransforms();
        Check(builder.Surface(ray, out _, out _), "Restoring collider visibility did not restore the canyon surface.");
        report.AppendLine("PASS: Direct scale-100 rotated canyon surface stays selected and hittable beneath a larger prop child; disabled, trigger, inactive, Scene-hidden, outside-canyon and persistent Project surfaces are rejected. Nonuniform/mirrored/zero parents cannot place props; slope limits reject/accept the same 25-degree terrain deterministically.");
    }

    private static void ValidateShortcuts(CivilCraftEnvironmentBuilder builder, Transform canyon, Vector3 ground,
        GameObject cactus, GameObject rock, StringBuilder report)
    {
        var slots = new List<GameObject>();
        for (int i = 0; i < 20; i++) slots.Add(i % 2 == 0 ? cactus : rock);
        slots[19] = null;
        Set(builder, "prefabs", slots);
        for (int page = 0; page < 2; page++)
        {
            Set(builder, "page", page);
            for (int digit = 0; digit <= 9; digit++)
            {
                int expected = page * 10 + (digit == 0 ? 9 : digit - 1);
                Check(builder.SelectShortcut((KeyCode)((int)KeyCode.Alpha0 + digit)) &&
                    Get<int>(builder, "selectedIndex") == expected, "Alpha digit selected the wrong palette slot/page.");
                Check(builder.SelectShortcut((KeyCode)((int)KeyCode.Keypad0 + digit)) &&
                    Get<int>(builder, "selectedIndex") == expected, "Keypad digit selected the wrong palette slot/page.");
            }
        }
        Check(!builder.SelectShortcut(KeyCode.Space), "A non-digit was accepted as a number shortcut.");
        Set(builder, "page", 1);
        builder.SelectShortcut(KeyCode.Alpha0);
        int count = canyon.childCount;
        Check(builder.Place(ground, Vector3.up) == null && canyon.childCount == count,
            "The empty slot reused the previously selected prefab or created a scene object.");
        report.AppendLine("PASS: Alpha 1–9/0 and keypad 1–9/0 select deterministic slots on both palette pages; non-digits are ignored and an empty slot places nothing.");
    }

    private static void ValidatePlacementAndStroke(CivilCraftEnvironmentBuilder builder, Transform canyon,
        Vector3 ground, GameObject cactus, GameObject rock, StringBuilder report)
    {
        Set(builder, "prefabs", new List<GameObject> { cactus, rock });
        Set(builder, "page", 0);
        Set(builder, "radius", 0f);
        Set(builder, "randomYaw", false);
        Set(builder, "alignToSurface", false);
        Set(builder, "seatOnSurface", true);
        Set(builder, "offset", .25f);
        Set(builder, "scaleRange", Vector2.one);
        Set(builder, "yaw", 73f);
        int before = canyon.childCount;
        Transform existingProp = canyon.GetChild(0);
        Vector3 existingPosition = existingProp.position;
        builder.BeginStroke();
        builder.SelectShortcut(KeyCode.Alpha1);
        // Slot changes end a stroke, just as actual Scene-view keyboard selection does.
        builder.BeginStroke();
        GameObject first = builder.Place(ground + Vector3.left * 8f, Vector3.up);
        // Swap the selected fixture directly to keep both placements in one actual brush stroke.
        Set(builder, "selectedIndex", 1);
        GameObject second = builder.Place(ground + Vector3.right * 8f, Vector3.up);
        builder.EndStroke();
        Undo.FlushUndoRecordObjects();
        Check(first != null && second != null && canyon.childCount == before + 2,
            "A stroke did not create both authored prop prefabs.");
        ValidatePlaced(first, cactus, canyon, ground.y + .25f, 73f, 1f);
        ValidatePlaced(second, rock, canyon, ground.y + .25f, 73f, 1f);
        Undo.IncrementCurrentGroup();
        Undo.PerformUndo();
        Check(first == null && second == null && canyon.childCount == before && existingProp != null &&
            existingProp.position == existingPosition, "One Undo did not remove only the two stroke placements.");
        Undo.PerformRedo();
        Check(canyon.childCount == before + 2 && existingProp.position == existingPosition,
            "One Redo did not restore only the two stroke placements.");
        var restored = new List<GameObject>();
        foreach (Transform child in canyon)
            if (child != existingProp) restored.Add(child.gameObject);
        Check(restored.Count == 2, "Redo restored an unexpected canyon child count.");
        foreach (GameObject instance in restored)
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            Check(source == cactus || source == rock, "Redo lost a prop's prefab connection.");
            ValidatePlaced(instance, (GameObject)source, canyon, ground.y + .25f, 73f, 1f);
        }
        Set(builder, "selectedIndex", 0);
        Set(builder, "scaleRange", new Vector2(1.6f, 1.6f));
        builder.BeginStroke();
        GameObject scaled = builder.Place(ground + Vector3.forward * 12f, Vector3.up);
        builder.EndStroke();
        Undo.FlushUndoRecordObjects();
        ValidatePlaced(scaled, cactus, canyon, ground.y + .25f, 73f, 1.6f);
        report.AppendLine("PASS: Authored cactus/rock instances retain direct Canyon parenting, prefab links, world rotation/scale and bounds seating plus offset under a rotated scale-100 parent; a fixed multiplier preserves authored proportions; one Undo/Redo removes/restores both stroke placements and leaves existing fixture props untouched.");
    }

    private static void ValidatePlaced(GameObject instance, GameObject prefab, Transform canyon,
        float minimumY, float yaw, float scale)
    {
        Check(instance != null && instance.transform.parent == canyon &&
            PrefabUtility.GetCorrespondingObjectFromSource(instance) == prefab,
            "Placement lost its direct canyon parent or source prefab connection.");
        Vector3 expectedScale = prefab.transform.lossyScale * scale;
        Check(Vector3.Distance(instance.transform.lossyScale, expectedScale) < Mathf.Max(.001f, expectedScale.magnitude * .001f),
            "Canyon scaling multiplied the intended authored world scale: " + instance.name);
        Quaternion expectedRotation = Quaternion.AngleAxis(yaw, Vector3.up) * prefab.transform.rotation;
        Check(Quaternion.Angle(instance.transform.rotation, expectedRotation) < .01f,
            "The rotated canyon replaced the prefab's authored world rotation: " + instance.name);
        float lowest = float.PositiveInfinity;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            lowest = Mathf.Min(lowest, renderer.bounds.min.y);
        Check(!float.IsPositiveInfinity(lowest) && Mathf.Abs(lowest - minimumY) < .03f,
            "Renderer bounds were not seated at surface minimum Y plus offset: " + instance.name);
    }

    private static void ValidateSetupActions(CivilCraftEnvironmentBuilder builder, Transform fixture, StringBuilder report)
    {
        var group = new GameObject("FBX-like grouped Canyon");
        group.transform.SetParent(fixture, false);
        group.transform.localPosition = Vector3.right * 500f;
        GameObject mesh = Primitive(group.transform, "Actual Canyon Mesh Child");
        Object.DestroyImmediate(mesh.GetComponent<BoxCollider>());
        mesh.transform.localScale = new Vector3(40f, 1f, 40f);
        GameObject small = Primitive(group.transform, "Smaller Mesh Child");
        Object.DestroyImmediate(small.GetComponent<BoxCollider>());
        builder.SetCanyon(group.transform);
        Check(Get<Transform>(builder, "surface") == mesh.transform &&
            CivilCraftEnvironmentBuilder.FindSurface(group.transform) == mesh.transform,
            "An imported-style group did not find its largest real mesh child.");
        Rigidbody dynamicBody = group.AddComponent<Rigidbody>();
        dynamicBody.useGravity = false;
        builder.ConfigureSurfaceCollider();
        Check(mesh.GetComponent<MeshCollider>() == null,
            "Collider setup added a non-convex surface collider under a dynamic Rigidbody.");
        Object.DestroyImmediate(dynamicBody);
        Undo.IncrementCurrentGroup();
        builder.ConfigureSurfaceCollider();
        Undo.FlushUndoRecordObjects();
        MeshCollider collider = mesh.GetComponent<MeshCollider>();
        Check(collider != null && collider.sharedMesh == mesh.GetComponent<MeshFilter>().sharedMesh &&
            collider.enabled && !collider.isTrigger && !collider.convex,
            "Collider setup did not configure the actual surface mesh.");
        Undo.IncrementCurrentGroup();
        Undo.PerformUndo();
        Check(mesh.GetComponent<MeshCollider>() == null, "Surface collider addition was not undoable.");
        Undo.PerformRedo();
        collider = mesh.GetComponent<MeshCollider>();
        Check(collider != null && collider.sharedMesh == mesh.GetComponent<MeshFilter>().sharedMesh,
            "Surface collider addition was not restored by Redo.");
        collider.enabled = false;
        collider.sharedMesh = null;
        Undo.IncrementCurrentGroup();
        string bodyBeforeConfigure = Describe(mesh.GetComponentInParent<Rigidbody>());
        builder.ConfigureSurfaceCollider();
        string meshesBeforeFlush = "collider=" + Describe(collider.sharedMesh) + ", filter=" + Describe(mesh.GetComponent<MeshFilter>().sharedMesh);
        Undo.FlushUndoRecordObjects();
        bool reusePassed = mesh.GetComponents<MeshCollider>().Length == 1 && mesh.GetComponent<MeshCollider>() == collider && collider.enabled &&
            collider.sharedMesh == mesh.GetComponent<MeshFilter>().sharedMesh;
        string meshesAfterFlush = "collider=" + Describe(collider.sharedMesh) + ", filter=" + Describe(mesh.GetComponent<MeshFilter>().sharedMesh);
        string controlProbe = reusePassed ? string.Empty : ProbeColliderReuse(builder, group.transform, mesh, collider);
        Check(reusePassed,
            "Collider reuse failed: count=" + mesh.GetComponents<MeshCollider>().Length +
            ", same=" + (mesh.GetComponent<MeshCollider>() == collider) + ", enabled=" + collider.enabled +
            ", mesh=" + (collider.sharedMesh == mesh.GetComponent<MeshFilter>().sharedMesh) +
            ", bodyBeforeConfigure=" + bodyBeforeConfigure +
            ", beforeFlush=[" + meshesBeforeFlush + "]" +
            ", afterFlush=[" + meshesAfterFlush + "]" +
            ", parent=" + Get<Transform>(builder, "parent") + ", surface=" + Get<Transform>(builder, "surface") + controlProbe);
        Undo.IncrementCurrentGroup();
        Undo.PerformUndo();
        Check(collider != null && !collider.enabled && collider.sharedMesh == null,
            "Reconfiguring an existing surface collider was not undoable.");
        Undo.PerformRedo();
        Check(collider.enabled && collider.sharedMesh == mesh.GetComponent<MeshFilter>().sharedMesh,
            "Redo did not restore existing surface collider configuration.");

        Undo.IncrementCurrentGroup();
        builder.ConfigureDirtPaths();
        Undo.FlushUndoRecordObjects();
        CanyonDirtPaths paths = mesh.GetComponent<CanyonDirtPaths>();
        Shader dirt = Shader.Find("Civil Craft/Canyon Dirt Surface");
        Check(dirt != null && paths != null && paths.canyon == mesh.transform && paths.surfaceShader == dirt &&
            group.GetComponent<CanyonDirtPaths>() == null, "Dirt paths were not attached to the actual mesh with the referenced shader.");
        Check(mesh.transform.childCount == 0, "Configuring dirt paths synchronously forced a generated surface rebuild.");
        Undo.IncrementCurrentGroup();
        Undo.PerformUndo();
        Check(mesh.GetComponent<CanyonDirtPaths>() == null, "Dirt-path component addition was not undoable.");
        Undo.PerformRedo();
        paths = mesh.GetComponent<CanyonDirtPaths>();
        Check(paths != null && paths.canyon == mesh.transform && paths.surfaceShader == dirt,
            "Redo did not restore the dirt-path configuration.");
        Shader alternate = Shader.Find("Civil Craft/Canyon Road Surface");
        Check(alternate != null, "The authored road shader was not found.");
        paths.surfaceShader = alternate;
        paths.canyon = small.transform;
        paths.width = .13f;
        paths.streets = new[] { new CanyonDirtPaths.Street(new Vector2(.1f, .2f), new Vector2(.7f, .8f)) };
        Undo.IncrementCurrentGroup();
        builder.ConfigureDirtPaths();
        Undo.FlushUndoRecordObjects();
        Check(mesh.GetComponents<CanyonDirtPaths>().Length == 1 && mesh.GetComponent<CanyonDirtPaths>() == paths &&
            paths.canyon == mesh.transform && paths.surfaceShader == alternate && paths.width == .13f &&
            paths.streets.Length == 1 && paths.streets[0].from == new Vector2(.1f, .2f),
            "Repeated dirt-path setup duplicated the component or replaced authored street/shader settings.");
        Undo.IncrementCurrentGroup();
        Undo.PerformUndo();
        Check(paths.canyon == small.transform && paths.surfaceShader == alternate,
            "Reconfiguring the existing dirt-path mesh reference was not undoable.");
        Undo.PerformRedo();
        Check(paths.canyon == mesh.transform && paths.surfaceShader == alternate,
            "Redo did not restore the existing dirt-path mesh reference.");
        report.AppendLine("PASS: FBX-like groups resolve the real largest mesh child; collider and dirt-path actions add or reuse one component on that mesh, reject a dynamic Rigidbody, preserve existing path/shader settings, and support Undo/Redo without forcing a generated path rebuild.");
    }

    private static string ProbeColliderReuse(CivilCraftEnvironmentBuilder builder, Transform group,
        GameObject mesh, MeshCollider collider)
    {
        // Diagnostic controls run only after a real reuse failure and remain inside the disposable fixture.
        Mesh expected = mesh.GetComponent<MeshFilter>().sharedMesh;
        collider.enabled = false;
        collider.sharedMesh = expected;
        string assignedDisabled = Describe(collider.sharedMesh);
        collider.convex = false;
        string afterConvex = Describe(collider.sharedMesh);
        collider.enabled = true;
        string afterEnable = Describe(collider.sharedMesh);
        Physics.SyncTransforms();
        string afterSync = Describe(collider.sharedMesh);
        collider.sharedMesh = expected;
        string assignedLast = Describe(collider.sharedMesh);
        GameObject fresh = Primitive(group, "Fresh existing MeshCollider control");
        Object.DestroyImmediate(fresh.GetComponent<BoxCollider>());
        MeshCollider freshCollider = fresh.AddComponent<MeshCollider>();
        freshCollider.enabled = false;
        freshCollider.sharedMesh = null;
        builder.SetCanyon(fresh.transform);
        Undo.IncrementCurrentGroup();
        builder.ConfigureSurfaceCollider();
        string freshResult = Describe(freshCollider.sharedMesh);
        freshCollider.enabled = false;
        freshCollider.sharedMesh = null;
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(freshCollider, "Probe Surface Collider Configuration");
        string afterRecord = Describe(freshCollider.sharedMesh);
        freshCollider.sharedMesh = fresh.GetComponent<MeshFilter>().sharedMesh;
        string stagedAssignment = Describe(freshCollider.sharedMesh);
        freshCollider.convex = false;
        string stagedConvex = Describe(freshCollider.sharedMesh);
        freshCollider.isTrigger = false;
        string stagedTrigger = Describe(freshCollider.sharedMesh);
        freshCollider.enabled = true;
        string stagedEnable = Describe(freshCollider.sharedMesh);
        PrefabUtility.RecordPrefabInstancePropertyModifications(freshCollider);
        string stagedPrefab = Describe(freshCollider.sharedMesh);
        EditorSceneManager.MarkSceneDirty(fresh.scene);
        string stagedDirty = Describe(freshCollider.sharedMesh);
        Physics.SyncTransforms();
        string stagedSync = Describe(freshCollider.sharedMesh);
        builder.SetCanyon(group);
        return ", controls=[assignedDisabled=" + assignedDisabled + ", afterConvex=" + afterConvex +
            ", afterEnable=" + afterEnable + ", afterSync=" + afterSync + ", assignedLast=" + assignedLast +
            ", freshDisabledReuse=" + freshResult + ", stagedRecord=" + afterRecord +
            ", stagedAssign=" + stagedAssignment + ", stagedConvex=" + stagedConvex +
            ", stagedTrigger=" + stagedTrigger + ", stagedEnable=" + stagedEnable +
            ", stagedPrefab=" + stagedPrefab + ", stagedDirty=" + stagedDirty + ", stagedSync=" + stagedSync + "]";
    }

    private static CivilCraftEnvironmentPalette ValidateProfile(CivilCraftEnvironmentBuilder builder,
        GameObject cactus, GameObject rock, StringBuilder report)
    {
        CivilCraftEnvironmentPalette profile = ScriptableObject.CreateInstance<CivilCraftEnvironmentPalette>();
        profile.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            profile.prefabs = new List<GameObject> { rock, cactus, null };
            profile.radius = 2.5f;
            profile.spacing = 3.5f;
            profile.offset = .4f;
            profile.yaw = 24f;
            profile.maximumSlope = 42f;
            profile.scaleRange = new Vector2(.8f, 1.2f);
            profile.randomYaw = true;
            profile.alignToSurface = true;
            profile.seatOnSurface = false;
            Set(builder, "palette", profile);
            typeof(CivilCraftEnvironmentBuilder).GetMethod("LoadPalette", PrivateInstance).Invoke(builder, null);
            List<GameObject> loaded = Get<List<GameObject>>(builder, "prefabs");
            Check(loaded.Count == 10 && loaded[0] == rock && loaded[1] == cactus && loaded[2] == null &&
                !ReferenceEquals(loaded, profile.prefabs), "Loading a profile did not copy its prefab list into ten padded slots.");
            Check(Get<float>(builder, "radius") == profile.radius && Get<float>(builder, "spacing") == profile.spacing &&
                Get<float>(builder, "offset") == profile.offset && Get<float>(builder, "yaw") == profile.yaw &&
                Get<float>(builder, "maximumSlope") == profile.maximumSlope &&
                Get<Vector2>(builder, "scaleRange") == profile.scaleRange && Get<bool>(builder, "randomYaw") &&
                Get<bool>(builder, "alignToSurface") && !Get<bool>(builder, "seatOnSurface") &&
                Get<int>(builder, "selectedIndex") == 0 && Get<int>(builder, "page") == 0,
                "Loading a profile did not restore all brush settings and reset the selected slot/page.");
            loaded[0] = cactus;
            Check(profile.prefabs.Count == 3 && profile.prefabs[0] == rock && !EditorUtility.IsPersistent(profile),
                "Changing the loaded palette mutated the source profile list.");
            report.AppendLine("PASS: An in-memory profile loads all prefab/brush fields, pads slots, resets page/selection and copies its list independently; no profile asset is created or saved.");
            return profile;
        }
        catch
        {
            Object.DestroyImmediate(profile);
            throw;
        }
    }

    private static GameObject Primitive(Transform parent, string name)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        SceneManager.MoveGameObjectToScene(obj, parent.gameObject.scene);
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static string Describe(Object value) => value == null ? "null" : value.name + "#" + value.GetInstanceID() +
        "(persistent=" + EditorUtility.IsPersistent(value) + ", path=" + AssetDatabase.GetAssetPath(value) + ")";
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static FieldInfo Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, PrivateInstance);
        Check(field != null, "Validation field was not found: " + name);
        return field;
    }

    private static bool SameObjects(Object[] first, Object[] second)
    {
        if (first.Length != second.Length) return false;
        for (int i = 0; i < first.Length; i++) if (first[i] != second[i]) return false;
        return true;
    }

    private sealed class SceneSnapshot
    {
        private readonly bool dirty;
        private readonly GameObject[] roots;
        public SceneSnapshot(Scene scene) { dirty = scene.isDirty; roots = scene.GetRootGameObjects(); }
        public void AssertUnchanged(Scene scene)
        {
            Check(scene.IsValid() && scene.isLoaded && scene.isDirty == dirty && SameObjects(scene.GetRootGameObjects(), roots),
                "Validation changed a user-loaded scene's roots or dirty state: " + scene.path);
        }
    }

    private sealed class AssetSnapshot
    {
        private readonly string path;
        private readonly Object asset;
        private readonly bool dirty;
        private readonly long length, timestamp;
        public AssetSnapshot(string path, Object asset)
        {
            this.path = path;
            this.asset = asset;
            dirty = EditorUtility.IsDirty(asset);
            var file = new FileInfo(path);
            length = file.Length;
            timestamp = file.LastWriteTimeUtc.Ticks;
        }
        public void AssertUnchanged()
        {
            var file = new FileInfo(path);
            Check(EditorUtility.IsDirty(asset) == dirty && file.Length == length && file.LastWriteTimeUtc.Ticks == timestamp,
                "Validation modified an authored prefab asset: " + path);
        }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
