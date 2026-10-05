using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Editor-only environment painting. Does not auto-save or erase scene contents.</summary>
public sealed class CivilCraftEnvironmentBuilder : EditorWindow
{
    private const int PageSize = 10;
    [SerializeField] private Transform parent, surface;
    [SerializeField] private CivilCraftEnvironmentPalette palette;
    [SerializeField] private List<GameObject> prefabs = new List<GameObject>(new GameObject[PageSize]);
    [SerializeField] private int selectedIndex, page;
    [SerializeField] private float radius, spacing = 4f, offset, yaw, maximumSlope = 60f;
    [SerializeField] private Vector2 scaleRange = Vector2.one;
    [SerializeField] private bool randomYaw, alignToSurface, seatOnSurface = true;
    private bool painting, previousToolsHidden;
    private int stroke = -1, strokeControl;
    private SceneView strokeView, currentSceneView;
    private static readonly Dictionary<SceneView, int> pendingControlReleases = new Dictionary<SceneView, int>();
    private Vector3 lastPosition;
    private Vector2 scroll;
    private readonly System.Random random = new System.Random();

    static CivilCraftEnvironmentBuilder()
    {
        // Release in the acquiring Scene view's GUI context, even after the
        // builder is closed. Register before any window's painting callback.
        SceneView.duringSceneGui += ReleasePendingControls;
    }

    private static void ReleasePendingControls(SceneView view)
    {
        if (!pendingControlReleases.TryGetValue(view, out int id)) return;
        if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
        pendingControlReleases.Remove(view);
    }

    [MenuItem("Tools/Civil Craft/Environment Builder")]
    public static void Open()
    {
        var window = GetWindow<CivilCraftEnvironmentBuilder>("Environment Builder");
        window.minSize = new Vector2(360f, 480f);
        window.Show();
    }

    private void OnEnable()
    {
        EnsureSlots();
        SceneView.duringSceneGui += DuringSceneGUI;
        Undo.undoRedoPerformed += RepaintViews;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        StopPainting();
        SceneView.duringSceneGui -= DuringSceneGUI;
        Undo.undoRedoPerformed -= RepaintViews;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private void RepaintViews() { Repaint(); SceneView.RepaintAll(); }
    private void OnPlayModeChanged(PlayModeStateChange state) { StopPainting(); Repaint(); }
    private void EnsureSlots()
    {
        if (prefabs == null) prefabs = new List<GameObject>();
        while (prefabs.Count < PageSize) prefabs.Add(null);
        selectedIndex = Mathf.Clamp(selectedIndex, 0, prefabs.Count - 1);
        page = Mathf.Clamp(page, 0, (prefabs.Count - 1) / PageSize);
    }

    private GameObject SelectedPrefab => selectedIndex >= 0 && selectedIndex < prefabs.Count ? prefabs[selectedIndex] : null;
    internal static bool IsPrefab(GameObject prefab) => prefab != null && EditorUtility.IsPersistent(prefab) && PrefabUtility.IsPartOfPrefabAsset(prefab);
    internal static bool IsSceneParent(Transform target) => target != null && !EditorUtility.IsPersistent(target) &&
        target.gameObject.scene.IsValid() && target.gameObject.scene.isLoaded;
    internal static bool IsSupportedParentScale(Transform target)
    {
        if (!IsSceneParent(target)) return false;
        Vector3 scale = target.lossyScale;
        float largest = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        return scale.x > 0f && scale.y > 0f && scale.z > 0f &&
            largest - Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z)) <= largest * .001f;
    }
    private static bool IsSurfaceWithinParent(Transform target, Transform canyon) =>
        IsSceneParent(canyon) && IsSceneParent(target) && target.IsChildOf(canyon);
    private bool CanConfigureSurface => IsSurfaceWithinParent(surface, parent) &&
        surface.TryGetComponent<MeshFilter>(out var mesh) && mesh.sharedMesh != null && surface.GetComponent<MeshRenderer>() != null;

    private void OnGUI()
    {
        EnsureSlots();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("CANYON ENVIRONMENT BUILDER", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Assign your canyon, add its dirt paths, then paint prefab props and houses.\nScene view: 1–9 / 0 select; Q / E rotate 15°; Shift + Q / E rotate 90°; Escape stops; Ctrl + Z undoes a stroke. Alt/right/middle mouse navigate.", MessageType.Info);
        bool canEdit = !EditorApplication.isPlayingOrWillChangePlaymode && PrefabStageUtility.GetCurrentPrefabStage() == null;
        using (new EditorGUI.DisabledScope(!canEdit))
        {
            DrawCanyonSetup();
            EditorGUILayout.Space(8);
            DrawPalette();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
            spacing = Mathf.Max(.1f, EditorGUILayout.FloatField("Drag Spacing (World Units)", spacing));
            radius = Mathf.Max(0f, EditorGUILayout.FloatField("Scatter Radius", radius));
            scaleRange = EditorGUILayout.Vector2Field("Scale Multiplier Min / Max", scaleRange);
            scaleRange.x = Mathf.Max(.01f, scaleRange.x);
            scaleRange.y = Mathf.Max(scaleRange.x, scaleRange.y);
            yaw = EditorGUILayout.FloatField("Y Rotation (Q / E)", yaw);
            randomYaw = EditorGUILayout.Toggle("Random Y Rotation", randomYaw);
            alignToSurface = EditorGUILayout.Toggle("Align To Surface", alignToSurface);
            seatOnSurface = EditorGUILayout.Toggle("Seat On Surface", seatOnSurface);
            offset = EditorGUILayout.FloatField("Surface Offset", offset);
            maximumSlope = EditorGUILayout.Slider("Maximum Surface Slope", maximumSlope, 0f, 90f);
            EditorGUILayout.HelpBox("Houses: scatter 0, scale 1–1, random rotation and alignment off. Plants/rocks: optionally use scatter, rotation and scale variation. Prefab sizes, layers and connections are preserved; scenes are not auto-saved.", MessageType.None);
            bool ready = IsSupportedParentScale(parent) && IsSurfaceWithinParent(surface, parent) && HasSurfaceCollider(surface) && IsPrefab(SelectedPrefab);
            using (new EditorGUI.DisabledScope(!ready))
            {
                bool next = GUILayout.Toggle(painting, painting ? "PAINTING — click to stop" : "Enable Painting", "Button", GUILayout.Height(38));
                if (next != painting) SetPainting(next);
            }
            if (!ready && painting) StopPainting();
        }
        if (!canEdit) EditorGUILayout.HelpBox("Use the main scene outside Play Mode and Prefab Mode.", MessageType.Warning);
        EditorGUILayout.EndScrollView();
    }

    private void DrawCanyonSetup()
    {
        EditorGUILayout.LabelField("Canyon & Surface", EditorStyles.boldLabel);
        Transform next = (Transform)EditorGUILayout.ObjectField("Canyon Parent", parent, typeof(Transform), true);
        if (next != parent) SetCanyon(next);
        if (GUILayout.Button("Use Selected Canyon"))
        {
            if (IsSceneParent(Selection.activeTransform)) SetCanyon(Selection.activeTransform);
            else ShowNotification(new GUIContent("Select a canyon instance in the Hierarchy."));
        }
        Transform target = (Transform)EditorGUILayout.ObjectField(new GUIContent("Surface Mesh", "The actual canyon mesh. Only colliders on this object receive paint hits."), surface, typeof(Transform), true);
        if (target != surface) { EndStroke(); surface = IsSurfaceWithinParent(target, parent) ? target : null; }
        if (parent != null && surface == null && GUILayout.Button("Find Canyon Surface")) surface = FindSurface(parent);
        if (!IsSceneParent(parent)) EditorGUILayout.HelpBox("Assign a scene canyon, not a Project asset.", MessageType.Warning);
        else if (!IsSupportedParentScale(parent)) EditorGUILayout.HelpBox("Canyon Parent needs positive, uniform world scale to preserve prefab proportions. Use an unscaled scene group as the parent, with the canyon mesh inside it.", MessageType.Warning);
        else if (surface == null) EditorGUILayout.HelpBox("Select a Surface Mesh inside the canyon. Imported FBX roots can have their mesh on a child.", MessageType.Warning);
        else if (!HasSurfaceCollider(surface)) EditorGUILayout.HelpBox("The surface needs an enabled, non-trigger 3D collider. Use the button below to add one.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(!CanConfigureSurface))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add / Enable Surface Collider")) ConfigureSurfaceCollider();
            if (GUILayout.Button("Add / Edit Dirt Paths")) ConfigureDirtPaths();
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.HelpBox("Every placed prefab is a direct child of Canyon Parent. Only Surface Mesh is used as ground, so painting cannot stack on previously placed houses or props.", MessageType.None);
    }

    internal void SetCanyon(Transform canyon)
    {
        StopPainting();
        parent = IsSceneParent(canyon) ? canyon : null;
        surface = FindSurface(parent);
    }

    private void DrawPalette()
    {
        EditorGUILayout.LabelField("Number-Key Prefab Palette", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        var profile = (CivilCraftEnvironmentPalette)EditorGUILayout.ObjectField("Palette Profile", palette, typeof(CivilCraftEnvironmentPalette), false);
        if (EditorGUI.EndChangeCheck()) { EndStroke(); palette = profile; if (palette != null) LoadPalette(); }
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(palette == null))
        {
            if (GUILayout.Button("Reload Profile")) LoadPalette();
            if (GUILayout.Button("Save Profile")) SavePalette();
        }
        if (GUILayout.Button("Save As…")) SavePaletteAs();
        EditorGUILayout.EndHorizontal();
        int pages = (prefabs.Count + PageSize - 1) / PageSize;
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(page == 0))
            if (GUILayout.Button("◀", GUILayout.Width(32))) { EndStroke(); page--; }
        EditorGUILayout.LabelField($"Page {page + 1} / {pages}", EditorStyles.centeredGreyMiniLabel);
        using (new EditorGUI.DisabledScope(page >= pages - 1))
            if (GUILayout.Button("▶", GUILayout.Width(32))) { EndStroke(); page++; }
        EditorGUILayout.EndHorizontal();
        for (int index = page * PageSize; index < Mathf.Min(prefabs.Count, (page + 1) * PageSize); index++)
        {
            Color color = GUI.backgroundColor;
            if (index == selectedIndex) GUI.backgroundColor = new Color(1f, .75f, .35f);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (GUILayout.Button(((index % PageSize + 1) % PageSize).ToString(), GUILayout.Width(30), GUILayout.Height(30))) SelectSlot(index);
            Texture thumbnail = prefabs[index] != null ? AssetPreview.GetMiniThumbnail(prefabs[index]) : null;
            GUILayout.Label(thumbnail != null ? new GUIContent(thumbnail) : GUIContent.none, GUILayout.Width(30), GUILayout.Height(30));
            GameObject candidate = (GameObject)EditorGUILayout.ObjectField(prefabs[index], typeof(GameObject), false);
            if (candidate != prefabs[index]) { EndStroke(); prefabs[index] = IsPrefab(candidate) ? candidate : null; }
            if (GUILayout.Button("×", GUILayout.Width(24))) { EndStroke(); prefabs[index] = null; }
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = color;
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add 10 Slots")) { EndStroke(); for (int i = 0; i < PageSize; i++) prefabs.Add(null); page = pages; }
        if (GUILayout.Button("Add Selected Project Prefabs"))
            foreach (Object item in Selection.objects)
                if (item is GameObject prefab && IsPrefab(prefab) && !prefabs.Contains(prefab))
                {
                    int empty = prefabs.FindIndex(value => value == null);
                    if (empty >= 0) prefabs[empty] = prefab; else prefabs.Add(prefab);
                }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("Active: " + (SelectedPrefab != null ? SelectedPrefab.name : "empty slot — assign a prefab"), EditorStyles.boldLabel);
    }

    internal static Transform FindSurface(Transform canyon)
    {
        if (!IsSceneParent(canyon)) return null;
        if (canyon.GetComponent<MeshFilter>() != null || canyon.GetComponent<Collider>() != null) return canyon;
        Transform largest = null;
        float largestSize = -1f;
        foreach (MeshFilter filter in canyon.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer renderer) ||
                (filter.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) continue;
            float size = renderer.bounds.size.sqrMagnitude;
            if (size <= largestSize) continue;
            largest = filter.transform; largestSize = size;
        }
        return largest;
    }

    internal static bool HasSurfaceCollider(Transform target)
    {
        if (!IsSceneParent(target) || !target.gameObject.activeInHierarchy) return false;
        foreach (Collider collider in target.GetComponents<Collider>())
            if (collider.enabled && !collider.isTrigger && (!(collider is MeshCollider mesh) || mesh.sharedMesh != null)) return true;
        return false;
    }

    internal bool Surface(Ray ray, out Vector3 position, out Vector3 normal)
    {
        position = default; normal = Vector3.up;
        if (!IsSurfaceWithinParent(surface, parent) || !surface.gameObject.activeInHierarchy || SceneVisibilityManager.instance.IsHidden(surface.gameObject)) return false;
        float nearest = float.PositiveInfinity;
        // Exact-object collider queries: the canyon remains hittable when it is
        // also the output parent, and its newly painted children are ignored.
        foreach (Collider collider in surface.GetComponents<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || !collider.Raycast(ray, out RaycastHit hit, Mathf.Infinity) ||
                hit.distance >= nearest || Vector3.Angle(Vector3.up, hit.normal) > maximumSlope) continue;
            nearest = hit.distance; position = hit.point; normal = hit.normal;
        }
        return nearest < float.PositiveInfinity;
    }

    internal bool SelectShortcut(KeyCode key)
    {
        int slot;
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) slot = key - KeyCode.Alpha0;
        else if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) slot = key - KeyCode.Keypad0;
        else return false;
        slot = slot == 0 ? 9 : slot - 1;
        int index = page * PageSize + slot;
        if (index < prefabs.Count) SelectSlot(index);
        return true;
    }

    private void SelectSlot(int index) { EndStroke(); selectedIndex = index; RepaintViews(); }
    private void SetPainting(bool enabled)
    {
        if (!enabled) { StopPainting(); return; }
        if (painting) return;
        previousToolsHidden = Tools.hidden; Tools.hidden = true; painting = true;
        Physics.SyncTransforms(); RepaintViews();
    }
    private void StopPainting()
    {
        EndStroke();
        if (painting) Tools.hidden = previousToolsHidden;
        painting = false; SceneView.RepaintAll();
    }

    private void DuringSceneGUI(SceneView view)
    {
        currentSceneView = view;
        try { HandleSceneGUI(view); }
        finally { currentSceneView = null; }
    }

    private void HandleSceneGUI(SceneView view)
    {
        if (!painting) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null || !IsSupportedParentScale(parent)) { StopPainting(); return; }
        Event e = Event.current;
        int control = GUIUtility.GetControlID(typeof(CivilCraftEnvironmentBuilder).GetHashCode(), FocusType.Passive);
        if (strokeView != null && strokeView != view) return;
        if (e.rawType == EventType.MouseUp && e.button == 0) EndStroke();
        if (e.type == EventType.KeyDown && EditorWindow.focusedWindow == view && !EditorGUIUtility.editingTextField && !e.alt && !e.control && !e.command)
        {
            if (e.keyCode == KeyCode.Escape) { StopPainting(); e.Use(); Repaint(); return; }
            if (SelectShortcut(e.keyCode)) { e.Use(); view.Repaint(); return; }
            if (e.keyCode == KeyCode.Q || e.keyCode == KeyCode.E)
            {
                EndStroke(); yaw += (e.keyCode == KeyCode.Q ? -1f : 1f) * (e.shift ? 90f : 15f);
                e.Use(); RepaintViews(); return;
            }
        }
        if (e.alt || e.control || e.command || e.button == 1 || e.button == 2) { EndStroke(); return; }
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
        if (!Surface(HandleUtility.GUIPointToWorldRay(e.mousePosition), out Vector3 position, out Vector3 normal)) return;
        if (e.type == EventType.Repaint)
        {
            Handles.color = IsPrefab(SelectedPrefab) ? new Color(1f, .7f, .15f) : Color.red;
            float size = HandleUtility.GetHandleSize(position);
            Handles.DrawWireDisc(position, normal, Mathf.Max(radius, size * .15f));
            Handles.DrawLine(position, position + normal * size * .5f);
            Handles.Label(position + normal * size * .6f, $"{(selectedIndex % PageSize + 1) % PageSize}: {(SelectedPrefab != null ? SelectedPrefab.name : "Empty slot")}  •  {yaw:0}°");
        }
        if (e.type == EventType.MouseMove) view.Repaint();
        bool down = e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == control && GUIUtility.hotControl == 0;
        bool drag = e.type == EventType.MouseDrag && e.button == 0 && stroke >= 0 && strokeView == view && GUIUtility.hotControl == strokeControl;
        if (!down && !drag) return;
        if (!IsPrefab(SelectedPrefab)) { ShowNotification(new GUIContent("Assign a prefab to the active number slot.")); e.Use(); return; }
        if (down) { BeginStroke(); strokeView = view; strokeControl = control; GUIUtility.hotControl = control; }
        if (down || Vector3.Distance(lastPosition, position) >= spacing)
            if (Place(position, normal) != null) lastPosition = position;
        e.Use(); view.Repaint();
    }

    internal void BeginStroke()
    {
        EndStroke(); Undo.IncrementCurrentGroup(); stroke = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Paint Canyon Environment");
    }

    internal GameObject Place(Vector3 position, Vector3 normal)
    {
        if (!IsSupportedParentScale(parent) || !IsPrefab(SelectedPrefab)) return null;
        if (radius > 0f)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt((float)random.NextDouble()) * radius;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
            Vector3 sample = position + (tangent * Mathf.Cos(angle) + Vector3.Cross(normal, tangent) * Mathf.Sin(angle)) * distance;
            if (!Surface(new Ray(sample + normal * (radius + 10f), -normal), out position, out normal)) return null;
        }
        GameObject instance = PrefabUtility.InstantiatePrefab(SelectedPrefab, parent.gameObject.scene) as GameObject;
        if (instance == null) return null;
        Undo.RegisterCreatedObjectUndo(instance, "Paint Environment Prefab");
        Undo.SetTransformParent(instance.transform, parent, "Parent Under Canyon");
        Undo.RecordObject(instance.transform, "Place Environment Prefab");
        Quaternion tilt = alignToSurface ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
        float rotation = yaw + (randomYaw ? (float)random.NextDouble() * 360f : 0f);
        instance.transform.rotation = tilt * Quaternion.AngleAxis(rotation, Vector3.up) * instance.transform.rotation;
        // Retain the local scale computed by world-preserving parenting. Never
        // assign prefab.localScale under scale-100 imported canyon roots.
        instance.transform.localScale *= Mathf.Lerp(scaleRange.x, scaleRange.y, (float)random.NextDouble());
        instance.transform.position = position;
        if (seatOnSurface)
        {
            float lowest = float.PositiveInfinity;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    lowest = Mathf.Min(lowest, Vector3.Dot(renderer.transform.TransformPoint(corner) - position, normal));
                }
            }
            if (lowest < float.PositiveInfinity) instance.transform.position -= normal * lowest;
        }
        instance.transform.position += normal * offset;
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
        return instance;
    }

    internal void EndStroke()
    {
        if (stroke < 0) return;
        Undo.CollapseUndoOperations(stroke); stroke = -1;
        if (strokeControl != 0 && strokeView != null)
        {
            if (currentSceneView == strokeView && Event.current != null)
            {
                if (GUIUtility.hotControl == strokeControl) GUIUtility.hotControl = 0;
            }
            else { pendingControlReleases[strokeView] = strokeControl; strokeView.Repaint(); }
        }
        strokeView = null; strokeControl = 0;
    }

    internal void ConfigureSurfaceCollider()
    {
        if (!CanConfigureSurface) return;
        Rigidbody body = surface.GetComponentInParent<Rigidbody>();
        if (body != null && !body.isKinematic)
        {
            ShowNotification(new GUIContent("A canyon surface cannot use a non-convex collider with a dynamic Rigidbody."));
            return;
        }
        Mesh surfaceMesh = surface.GetComponent<MeshFilter>().sharedMesh;
        MeshCollider collider = surface.GetComponent<MeshCollider>();
        if (collider == null) collider = Undo.AddComponent<MeshCollider>(surface.gameObject);
        Undo.RecordObject(collider, "Configure Canyon Surface Collider");
        collider.isTrigger = false; collider.convex = false; collider.enabled = true;
        // Apply geometry last, after the collider's native shape flags settle.
        collider.sharedMesh = surfaceMesh;
        if (PrefabUtility.IsPartOfPrefabInstance(collider)) PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
        Physics.SyncTransforms(); SceneView.RepaintAll();
    }

    internal void ConfigureDirtPaths()
    {
        if (!CanConfigureSurface) return;
        StopPainting();
        CanyonDirtPaths paths = surface.GetComponent<CanyonDirtPaths>();
        bool added = paths == null;
        if (added) paths = Undo.AddComponent<CanyonDirtPaths>(surface.gameObject);
        Undo.RecordObject(paths, "Configure Canyon Dirt Paths");
        paths.canyon = surface;
        if (added || paths.surfaceShader == null) paths.surfaceShader = Shader.Find("Civil Craft/Canyon Dirt Surface");
        paths.Refresh();
        if (PrefabUtility.IsPartOfPrefabInstance(paths)) PrefabUtility.RecordPrefabInstancePropertyModifications(paths);
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
        Selection.activeGameObject = surface.gameObject; EditorGUIUtility.PingObject(paths); SceneView.RepaintAll();
    }

    private void LoadPalette()
    {
        if (palette == null) return;
        EndStroke(); prefabs = palette.prefabs != null ? new List<GameObject>(palette.prefabs) : new List<GameObject>();
        radius = Mathf.Max(0f, palette.radius); spacing = Mathf.Max(.1f, palette.spacing); offset = palette.offset; yaw = palette.yaw;
        maximumSlope = Mathf.Clamp(palette.maximumSlope, 0f, 90f);
        scaleRange = new Vector2(Mathf.Max(.01f, palette.scaleRange.x), Mathf.Max(.01f, palette.scaleRange.y));
        scaleRange.y = Mathf.Max(scaleRange.x, scaleRange.y);
        randomYaw = palette.randomYaw; alignToSurface = palette.alignToSurface; seatOnSurface = palette.seatOnSurface;
        selectedIndex = page = 0; EnsureSlots(); RepaintViews();
    }

    private void SavePalette()
    {
        if (palette == null) return;
        Undo.RecordObject(palette, "Save Environment Palette");
        palette.prefabs = new List<GameObject>(prefabs); palette.radius = radius; palette.spacing = spacing;
        palette.offset = offset; palette.yaw = yaw; palette.scaleRange = scaleRange;
        palette.maximumSlope = maximumSlope;
        palette.randomYaw = randomYaw; palette.alignToSurface = alignToSurface; palette.seatOnSurface = seatOnSurface;
        EditorUtility.SetDirty(palette); AssetDatabase.SaveAssetIfDirty(palette);
    }

    private void SavePaletteAs()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Environment Palette", "Canyon Environment Palette", "asset", "Save this palette and its placement settings.", "Assets/Editor");
        if (string.IsNullOrEmpty(path)) return;
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) { ShowNotification(new GUIContent("Choose a new name; existing assets are not overwritten.")); return; }
        palette = CreateInstance<CivilCraftEnvironmentPalette>();
        AssetDatabase.CreateAsset(palette, path); SavePalette(); EditorGUIUtility.PingObject(palette);
    }
}
