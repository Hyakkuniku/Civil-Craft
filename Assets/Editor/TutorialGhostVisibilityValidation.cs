using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, read-only inspection of authored tutorial ghosts. Never saves the scene.</summary>
public static class TutorialGhostVisibilityValidation
{
    private const string ScenePath = "Assets/Scenes/CanyonCrossing.unity";
    private const string InspectReport = "Temp/TutorialGhostVisibilityInspect.txt";
    private const string InspectRequest = "Temp/TutorialGhostVisibilityInspect.request";
    private const string ValidationRequest = "Temp/TutorialGhostVisibilityValidation.request";
    private const string ValidationReport = "Temp/TutorialGhostVisibilityValidation.txt";
    private static double nextCheck;
    private static bool running;
    private static bool Busy => running || EditorApplication.isPlayingOrWillChangePlaymode ||
        EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer ||
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    [InitializeOnLoadMethod]
    private static void WatchRequests()
    {
        EditorApplication.update -= CheckRequests;
        EditorApplication.update += CheckRequests;
    }

    private static void CheckRequests()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (Busy) return;
        if (File.Exists(InspectRequest))
        {
            File.Delete(InspectRequest);
            InspectFromCommandLine();
        }
        if (Busy || !File.Exists(ValidationRequest)) return;
        File.Delete(ValidationRequest);
        ValidateFromCommandLine();
    }

    public static void InspectFromCommandLine()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            File.WriteAllText(InspectReport, Inspect());
            Debug.Log("[Tutorial Ghost] Read-only inspection written to " + InspectReport);
        }
        catch (Exception error)
        {
            File.WriteAllText(InspectReport, "FAIL: " + error);
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Civil Craft/Inspect Truss Tutorial Ghosts")]
    private static void InspectFromMenu() => InspectFromCommandLine();

    [MenuItem("Tools/Civil Craft/Validate Truss Tutorial Ghosts")]
    private static void ValidateFromMenu() => ValidateFromCommandLine();

    public static void ValidateFromCommandLine()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            File.WriteAllText(ValidationReport, Validate());
            Debug.Log("[Tutorial Ghost] PASS. Isolated regression report: " + ValidationReport);
        }
        catch (Exception error)
        {
            File.WriteAllText(ValidationReport, "FAIL: " + error);
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    private static string Validate()
    {
        Check(!Busy, "Run the isolated fixture in idle Edit Mode, outside imports, compilation, Prefab Mode and player builds.");
        running = true;
        Scene activeScene = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object activeSelection = Selection.activeObject;
        Point[] allPoints = Point.AllPoints.ToArray();
        var scenes = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
        for (int index = 0; index < SceneManager.sceneCount; index++)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            scenes.Add(scene, Tuple.Create(scene.isDirty, scene.GetRootGameObjects()));
        }
        Scene temporary = default;
        GameObject fixture = null;
        Material material = null;
        var report = new StringBuilder();
        try
        {
            temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            fixture = new GameObject("Tutorial Ghost Validation (Disposable)");
            SceneManager.MoveGameObjectToScene(fixture, temporary);
            fixture.transform.position = new Vector3(0f, 10000f, 0f);
            ValidateAnchorFit(fixture.transform, report);
            ValidateVisualActivation(fixture.transform, report, out material);
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (material != null) Object.DestroyImmediate(material);
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
            Selection.objects = selection;
            Selection.activeObject = activeSelection;
            running = false;
        }
        Check(SceneManager.sceneCount == scenes.Count, "Fixture changed the user's loaded scene count.");
        foreach (var scene in scenes)
        {
            Check(scene.Key.isDirty == scene.Value.Item1, "Fixture changed a loaded scene's dirty state.");
            Check(scene.Key.GetRootGameObjects().SequenceEqual(scene.Value.Item2), "Fixture changed user-authored scene roots.");
        }
        Check(Point.AllPoints.SequenceEqual(allPoints), "Fixture changed the existing global Point registry.");
        Check(SceneManager.GetActiveScene() == activeScene && Selection.objects.SequenceEqual(selection) &&
            Selection.activeObject == activeSelection, "Fixture did not restore active scene and selection.");
        report.AppendLine("PASS: Disposable additive fixture removed; original scenes, roots, dirty state, active scene, selection and Point.AllPoints preserved. No player-data/singleton mutation, live tutorial event, scene save, asset save or material-file change.");
        report.AppendLine("Completed UTC: " + DateTime.UtcNow.ToString("O"));
        return report.ToString();
    }

    private static void ValidateAnchorFit(Transform fixture, StringBuilder report)
    {
        Transform container = Child(fixture, "Baked 30m Blueprint").transform;
        GhostSegment leftHalf = Child(container, "Ghost Left Half").AddComponent<GhostSegment>();
        GhostSegment rightHalf = Child(container, "Ghost Right Half").AddComponent<GhostSegment>();
        leftHalf.startPos = new Vector3(98f, 0f, 0f);
        leftHalf.endPos = rightHalf.startPos = new Vector3(113f, 0f, 0f);
        rightHalf.endPos = new Vector3(128f, 0f, 0f);
        Point left = Child(fixture, "Current Left Anchor").AddComponent<Point>();
        Point right = Child(fixture, "Current Right Anchor").AddComponent<Point>();
        left.transform.position = new Vector3(98.598965f, 0f, 0f);
        right.transform.position = new Vector3(128.583858f, 0f, 0f);
        Vector3 leftBefore = left.transform.position, rightBefore = right.transform.position;
        GhostSegment[] ghosts = { leftHalf, rightHalf, null };
        var anchors = new List<Point> { left, right, left, null };
        Check(Fit(ghosts, anchors, 1.25f, .1f, out Vector3 correction), "Known uniform authored-anchor drift was not detected.");
        float expected = ((98.598965f - 98f) + (128.583858f - 128f)) * .5f;
        Check(Vector3.Distance(correction, new Vector3(expected, 0f, 0f)) < .0001f,
            "Correction differs from the consistent anchor translation.");
        Check(Vector3.Distance(leftHalf.startPos + correction, leftBefore) < .01f &&
            Vector3.Distance(rightHalf.endPos + correction, rightBefore) < .01f,
            "Translated baked endpoints do not fit both current anchors.");
        foreach (GhostSegment ghost in ghosts)
        {
            if (ghost == null) continue;
            ghost.startPos += correction;
            ghost.endPos += correction;
        }
        Check(!Fit(ghosts, anchors, 1.25f, .1f, out Vector3 second) && second.sqrMagnitude < .000001f,
            "Re-entering an already aligned blueprint applies a second translation.");
        leftHalf.startPos = leftBefore;
        rightHalf.endPos = rightBefore;
        Check(!Fit(ghosts, anchors, 1.25f, .1f, out Vector3 rebaked) && rebaked == Vector3.zero,
            "An exactly rebaked blueprint is moved.");
        leftHalf.startPos = new Vector3(98f, 0f, 0f);
        rightHalf.endPos = new Vector3(128f, 0f, 0f);
        right.transform.position = new Vector3(128.1f, 0f, 0f);
        Check(!Fit(ghosts, anchors, 1.25f, .1f, out Vector3 inconsistent) && inconsistent == Vector3.zero,
            "Contradictory anchor corrections are accepted as a rigid translation.");
        Check(!Fit(ghosts, new List<Point> { left, left, null }, 1.25f, .1f, out _),
            "One distinct anchor incorrectly permits blueprint alignment.");
        right.transform.position = new Vector3(131f, 0f, 0f);
        Check(!Fit(ghosts, anchors, 1.25f, .1f, out _), "An out-of-range bridge is snapped to unrelated anchors.");
        right.transform.position = rightBefore;
        Check(left.transform.position == leftBefore && right.transform.position == rightBefore,
            "Pure fit mutated authored anchors.");
        report.AppendLine("PASS: Known 98/128m baked endpoints fit current 98.598965/128.583858m anchors with a consistent ~0.5914m translation. Duplicate/null anchors/endpoints are safe; repeat/rebaked offsets are zero; contradictory spans, one distinct anchor and out-of-range anchors are rejected. Real anchor positions remain unchanged.");
    }

    private static void ValidateVisualActivation(Transform fixture, StringBuilder report, out Material material)
    {
        material = null;
        Transform container = Child(fixture, "Inactive Blueprint").transform;
        Transform group = Child(container, "Inactive Segment Group").transform;
        GhostSegment segment = Child(group, "Ghost_Segment").AddComponent<GhostSegment>();
        Transform visualGroup = Child(segment.transform, "Inactive Visual Group").transform;
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Inactive Segment Visual";
        visual.transform.SetParent(visualGroup, false);
        Renderer renderer = visual.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        Check(shader != null, "Fixture requires a built-in unlit shader.");
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        string colorProperty = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        Color before = new Color(.2f, .8f, 1f, .35f);
        material.SetColor(colorProperty, before);
        renderer.sharedMaterial = material;
        renderer.enabled = false;
        renderer.forceRenderingOff = true;
        visual.SetActive(false);
        visualGroup.gameObject.SetActive(false);
        segment.gameObject.SetActive(false);
        group.gameObject.SetActive(false);
        GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        point.name = "Ghost_Point_Endpoint";
        point.transform.SetParent(container, false);
        point.SetActive(false);
        GameObject unrelated = GameObject.CreatePrimitive(PrimitiveType.Cube);
        unrelated.name = "Unrelated Inactive Sibling";
        unrelated.transform.SetParent(container, false);
        unrelated.SetActive(false);
        BoxCollider2D collider2D = visual.AddComponent<BoxCollider2D>();
        container.gameObject.SetActive(false);

        InvokeStatic("ActivateGhostHierarchy", container.gameObject);
        Check(container.gameObject.activeSelf && group.gameObject.activeSelf && segment.gameObject.activeInHierarchy &&
            visualGroup.gameObject.activeSelf && visual.activeInHierarchy && renderer.enabled && !renderer.forceRenderingOff,
            "A required inactive segment/visual path was not restored.");
        Check(point.activeInHierarchy, "An authored Ghost_Point endpoint remains hidden.");
        Check(!unrelated.activeSelf, "An unrelated inactive sibling was exposed by ghost activation.");
        Check(renderer.sharedMaterial == material && material.GetColor(colorProperty) == before,
            "Activation changes the existing ghost material or its transparency.");
        InvokeStatic("MakeGhostContainerNonBlocking", container);
        Check(container.GetComponentsInChildren<Collider>(true).All(collider => !collider.enabled) && !collider2D.enabled,
            "An activated ghost collider still blocks placement or picking.");
        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        Check(ignoreRaycast < 0 || container.GetComponentsInChildren<Transform>(true).All(child => child.gameObject.layer == ignoreRaycast),
            "Ghost picking layers were not made nonblocking.");
        container.gameObject.SetActive(false);
        visual.SetActive(false);
        visualGroup.gameObject.SetActive(false);
        renderer.enabled = false;
        renderer.forceRenderingOff = true;
        InvokeStatic("ActivateGhostHierarchy", container.gameObject);
        Check(visual.activeInHierarchy && renderer.enabled && !renderer.forceRenderingOff && !unrelated.activeSelf,
            "A cached/replayed ghost does not restore its required visual path.");
        InvokeStatic("ActivateGhostHierarchy", new object[] { null });
        report.AppendLine("PASS: Initial and replay activation restore inactive containers, nested segment groups, nested mesh visuals, disabled/force-hidden renderers and Ghost_Point endpoints. Unrelated inactive siblings stay hidden; shared material/alpha are untouched. Owned 3D/2D colliders and raycast layers cannot block building.");
    }

    private static bool Fit(GhostSegment[] ghosts, List<Point> anchors, float distance, float residual, out Vector3 offset)
    {
        object[] arguments = { ghosts, anchors, distance, residual, Vector3.zero };
        bool result = (bool)InvokeStatic("TryGetBlueprintAnchorOffset", arguments);
        offset = (Vector3)arguments[4];
        return result;
    }

    private static object InvokeStatic(string name, params object[] arguments)
    {
        MethodInfo method = typeof(BuildTutorialDirector).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        Check(method != null, "Expected ghost regression seam is missing: " + name);
        return method.Invoke(null, arguments);
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string Inspect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Inspect the authored ghosts in Edit Mode, outside a player build.");
        Scene activeScene = SceneManager.GetActiveScene();
        Object[] selection = Selection.objects;
        Object activeSelection = Selection.activeObject;
        var snapshots = new Dictionary<Scene, bool>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene existing = SceneManager.GetSceneAt(i);
            snapshots.Add(existing, existing.isDirty);
        }
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        var report = new StringBuilder();
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var roots = scene.GetRootGameObjects();
            TutorialSequence[] sequences = roots.SelectMany(root => root.GetComponentsInChildren<TutorialSequence>(true)).ToArray();
            BuildLocation[] sites = roots.SelectMany(root => root.GetComponentsInChildren<BuildLocation>(true)).ToArray();
            report.AppendLine("Read-only authored tutorial inspection UTC: " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("Scene: " + scene.path + "; root count=" + roots.Length + "; dirty=" + scene.isDirty);
            foreach (TutorialSequence sequence in sequences.Where(sequence => sequence.lessonName == "Sequence_Build2" ||
                         sequence.name == "Sequence_Build2" || sequence.name.IndexOf("LessonBuild2", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                report.AppendLine("\nSEQUENCE " + Path(sequence.transform) + "; lesson=" + sequence.lessonName +
                    "; prerequisite=" + sequence.requiredPreviousLesson + "; active=" + sequence.gameObject.activeInHierarchy);
                TutorialStep[] steps = sequence.tutorialSteps ?? Array.Empty<TutorialStep>();
                var serialized = new SerializedObject(sequence);
                SerializedProperty serializedSteps = serialized.FindProperty("tutorialSteps");
                for (int index = 0; index < steps.Length; index++)
                {
                    TutorialStep step = steps[index];
                    if (step == null) continue;
                    report.AppendLine("STEP " + index + ": " + step.message.Replace('\n', ' ') + "; action=" + step.requiredAction);
                    var ghostContainers = new HashSet<GameObject>();
                    if (step.worldHighlightObject != null)
                    {
                        report.AppendLine("  worldHighlight=" + Path(step.worldHighlightObject.transform));
                        if (step.worldHighlightObject.GetComponentInChildren<GhostSegment>(true) != null)
                            ghostContainers.Add(step.worldHighlightObject);
                    }
                    UnityEvent stepEvent = step.OnStepStart;
                    SerializedProperty calls = serializedSteps?.GetArrayElementAtIndex(index)
                        .FindPropertyRelative("OnStepStart.m_PersistentCalls.m_Calls");
                    for (int callIndex = 0; stepEvent != null && callIndex < stepEvent.GetPersistentEventCount(); callIndex++)
                    {
                        Object target = stepEvent.GetPersistentTarget(callIndex);
                        string method = stepEvent.GetPersistentMethodName(callIndex);
                        SerializedProperty call = calls != null && callIndex < calls.arraySize ? calls.GetArrayElementAtIndex(callIndex) : null;
                        Object argument = call?.FindPropertyRelative("m_Arguments.m_ObjectArgument")?.objectReferenceValue;
                        bool boolArgument = call?.FindPropertyRelative("m_Arguments.m_BoolArgument")?.boolValue ?? false;
                        report.AppendLine("  EVENT " + callIndex + " target=" + ObjectLabel(target) + " method=" + method +
                            " state=" + stepEvent.GetPersistentListenerState(callIndex) + " objectArgument=" + ObjectLabel(argument) +
                            " boolArgument=" + boolArgument);
                        GameObject targetObject = target is GameObject go ? go : (target as Component)?.gameObject;
                        if (targetObject != null && targetObject.GetComponentInChildren<GhostSegment>(true) != null)
                            ghostContainers.Add(targetObject);
                        if (argument is GameObject argumentObject && argumentObject.GetComponentInChildren<GhostSegment>(true) != null)
                            ghostContainers.Add(argumentObject);
                    }
                    foreach (GameObject container in ghostContainers) InspectGhost(container, sites, report);
                }
                foreach (BuildLocation site in sites.Where(site => site.onEnterBuildModeTutorial == sequence))
                    InspectSite(site, report);
            }
            report.AppendLine("\nALL TUTORIAL SITES");
            foreach (BuildLocation site in sites.Where(site => site.onEnterBuildModeTutorial != null)) InspectSite(site, report);
        }
        finally
        {
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            Selection.objects = selection;
            Selection.activeObject = activeSelection;
        }
        if (SceneManager.sceneCount != snapshots.Count) throw new InvalidOperationException("Inspection changed the loaded scene count.");
        foreach (var snapshot in snapshots)
            if (snapshot.Key.isDirty != snapshot.Value) throw new InvalidOperationException("Inspection changed a loaded scene dirty state.");
        report.AppendLine("PASS: No scene/asset save, runtime event invocation, ghost activation, player data or authored object mutation. Original loaded scenes, dirty state, active scene and selection restored.");
        return report.ToString();
    }

    private static void InspectGhost(GameObject container, BuildLocation[] sites, StringBuilder report)
    {
        GhostSegment[] ghosts = container.GetComponentsInChildren<GhostSegment>(true);
        Renderer[] renderers = container.GetComponentsInChildren<Renderer>(true);
        report.AppendLine("  GHOST " + Path(container.transform) + " id=" + GlobalObjectId.GetGlobalObjectIdSlow(container) +
            "; segment count=" + ghosts.Length + "; renderer count=" + renderers.Length + "; inactive visuals=" +
            renderers.Count(renderer => !renderer.gameObject.activeInHierarchy) + "; disabled renderers=" + renderers.Count(renderer => !renderer.enabled));
        for (Transform parent = container.transform; parent != null; parent = parent.parent)
            report.AppendLine("    PARENT " + Path(parent) + " self=" + parent.gameObject.activeSelf + " hierarchy=" +
                parent.gameObject.activeInHierarchy + " position=" + V(parent.position) + " scale=" + V(parent.lossyScale));
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            Bounds rendererBounds = WorldBounds(renderer);
            if (!hasBounds) { bounds = rendererBounds; hasBounds = true; }
            else bounds.Encapsulate(rendererBounds);
        }
        report.AppendLine("    Visual world bounds center=" + V(bounds.center) + " size=" + V(bounds.size));
        for (int index = 0; index < ghosts.Length; index++)
        {
            GhostSegment ghost = ghosts[index];
            report.AppendLine("    SEGMENT " + index + " " + Path(ghost.transform) + " activeSelf=" + ghost.gameObject.activeSelf +
                " activeHierarchy=" + ghost.gameObject.activeInHierarchy + " enabled=" + ghost.enabled +
                " endpoints=" + V(ghost.startPos) + " -> " + V(ghost.endPos) + " material=" + ObjectLabel(ghost.requiredMaterial));
        }
        foreach (Renderer renderer in renderers.Take(12))
        {
            report.AppendLine("    RENDERER " + Path(renderer.transform) + " enabled=" + renderer.enabled +
                " activeSelf=" + renderer.gameObject.activeSelf + " activeHierarchy=" + renderer.gameObject.activeInHierarchy +
                " layer=" + renderer.gameObject.layer + " bounds=" + V(WorldBounds(renderer).center));
            foreach (Material material in renderer.sharedMaterials)
                report.AppendLine("      MATERIAL " + ObjectLabel(material) + " shader=" + (material != null ? material.shader.name : "null") +
                    " color=" + (material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").ToString() :
                    material != null && material.HasProperty("_Color") ? material.GetColor("_Color").ToString() : "n/a"));
        }
        foreach (BuildLocation site in sites.OrderBy(site => AnchorDistance(site, bounds.center)).Take(3))
        {
            report.AppendLine("    NEARBY SITE " + Path(site.transform) + " distance=" + AnchorDistance(site, bounds.center).ToString("F2"));
            InspectSite(site, report);
            Camera camera = site.locationCamera;
            if (camera != null)
            {
                int visible = 0;
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                foreach (Renderer renderer in renderers)
                    if ((camera.cullingMask & (1 << renderer.gameObject.layer)) != 0 &&
                        GeometryUtility.TestPlanesAABB(planes, WorldBounds(renderer))) visible++;
                report.AppendLine("      Camera-visible renderer bounds=" + visible + "/" + renderers.Length);
            }
        }
    }

    private static float AnchorDistance(BuildLocation site, Vector3 center)
    {
        var anchors = (site.startingAnchors ?? new List<Point>()).Concat(site.endingAnchors ?? new List<Point>()).Where(point => point != null).ToArray();
        if (anchors.Length == 0) return Vector3.Distance(site.transform.position, center);
        Vector3 midpoint = Vector3.zero;
        foreach (Point anchor in anchors) midpoint += anchor.transform.position;
        return Vector3.Distance(midpoint / anchors.Length, center);
    }

    private static void InspectSite(BuildLocation site, StringBuilder report)
    {
        report.AppendLine("      SITE " + Path(site.transform) + " active=" + site.gameObject.activeInHierarchy +
            " contract=" + ObjectLabel(site.activeContract) + " tutorial=" + ObjectLabel(site.onEnterBuildModeTutorial));
        foreach (Point anchor in (site.startingAnchors ?? new List<Point>()).Concat(site.endingAnchors ?? new List<Point>()))
            if (anchor != null) report.AppendLine("        ANCHOR " + Path(anchor.transform) + " position=" + V(anchor.transform.position));
        Camera camera = site.locationCamera;
        if (camera != null) report.AppendLine("        CAMERA " + Path(camera.transform) + " enabled=" + camera.enabled + " active=" +
            camera.gameObject.activeInHierarchy + " position=" + V(camera.transform.position) + " rotation=" + V(camera.transform.eulerAngles) +
            " orthographic=" + camera.orthographic + " size=" + camera.orthographicSize + " aspect=" + camera.aspect +
            " near=" + camera.nearClipPlane + " far=" + camera.farClipPlane + " cullingMask=" + camera.cullingMask);
    }

    private static Bounds WorldBounds(Renderer renderer)
    {
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return renderer.bounds;
        Bounds local = filter.sharedMesh.bounds;
        Bounds world = new Bounds(renderer.transform.TransformPoint(local.center), Vector3.zero);
        Vector3 min = local.min, max = local.max;
        for (int bits = 0; bits < 8; bits++) world.Encapsulate(renderer.transform.TransformPoint(new Vector3(
            (bits & 1) == 0 ? min.x : max.x, (bits & 2) == 0 ? min.y : max.y, (bits & 4) == 0 ? min.z : max.z)));
        return world;
    }

    private static string ObjectLabel(Object target)
    {
        if (target == null) return "null";
        string location = target is Component component ? Path(component.transform) : target is GameObject go ? Path(go.transform) : AssetDatabase.GetAssetPath(target);
        return target.name + " (" + target.GetType().Name + ") [" + location + "]";
    }

    private static string Path(Transform transform)
    {
        var names = new List<string>();
        for (Transform current = transform; current != null; current = current.parent) names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static string V(Vector3 vector) => vector.ToString("F3");
}
