using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
internal static class BridgeOutlineGpuValidation
{
    [UnityEditor.MenuItem("Tools/Civil Craft/Validate Selection Outline Shader")]
    private static void Validate()
    {
        Material material = Resources.Load<Material>("UI/BridgeSelectionOutline");
        if (material == null) return;
        RenderTexture previous = RenderTexture.active;
        var mask = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32);
        var a = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32);
        var b = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32);
        var result = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32);
        var readback = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        var mesh = new Mesh();
        var commands = new CommandBuffer();
        try
        {
            // Asymmetric L-shaped mesh detects missing masks and filled interiors.
            mesh.vertices = new[] { new Vector3(-0.6f,-0.6f,0), new Vector3(0.6f,-0.6f,0),
                new Vector3(0.6f,-0.1f,0), new Vector3(-0.1f,-0.1f,0),
                new Vector3(-0.1f,0.6f,0), new Vector3(-0.6f,0.6f,0) };
            mesh.triangles = new[] {0,1,2, 0,2,3, 0,3,4, 0,4,5};
            commands.SetRenderTarget(mask);
            commands.ClearRenderTarget(false, true, Color.clear);
            commands.SetGlobalMatrix("_BridgeMaskMVP",
                GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1,1,-1,1,-1,1), true));
            commands.SetViewport(new Rect(0,0,128,128));
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            commands.SetGlobalVector("_BridgeMaskTexelSize", new Vector4(1f/128,1f/128,128,128));
            commands.SetGlobalFloat("_BridgeClosingStep", 1f);
            BridgeSelectionOutline.DrawFullscreen(commands, mask, a, 128,128,1,material);
            BridgeSelectionOutline.DrawFullscreen(commands, a, b, 128,128,2,material);
            BridgeSelectionOutline.DrawFullscreen(commands, b, a, 128,128,3,material);
            BridgeSelectionOutline.DrawFullscreen(commands, a, b, 128,128,4,material);
            commands.SetGlobalTexture("_BridgeOriginalMask", mask);
            commands.SetGlobalColor("_BridgeOutlineColor", Color.white);
            commands.SetGlobalFloat("_BridgeOutlinePixels", 3f);
            commands.SetRenderTarget(result); commands.ClearRenderTarget(false, true, Color.clear);
            BridgeSelectionOutline.DrawFullscreen(commands, b, result,128,128,5,material);
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = result;
            readback.ReadPixels(new Rect(0,0,128,128),0,0); readback.Apply();
            int border = 0;
            foreach (Color32 pixel in readback.GetPixels32()) if (pixel.r > 100) border++;
            RenderTexture.active = mask;
            readback.ReadPixels(new Rect(0,0,128,128),0,0); readback.Apply();
            int body = 0;
            foreach (Color32 pixel in readback.GetPixels32()) if (pixel.r > 100) body++;
            if (body > 100 && border > 100 && border < body)
                Debug.Log($"[BridgeOutline GPU Test] PASS: mesh pixels={body}, visible border pixels={border}, shader passes={material.passCount}.");
            else Debug.LogError($"[BridgeOutline GPU Test] FAIL: mesh pixels={body}, border pixels={border}, shader passes={material.passCount}.");
        }
        finally
        {
            RenderTexture.active = previous;
            commands.Release(); Object.DestroyImmediate(mesh); Object.DestroyImmediate(readback);
            RenderTexture.ReleaseTemporary(mask); RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(b); RenderTexture.ReleaseTemporary(result);
        }
    }
}
#endif

// Draw the real selected meshes into a shared mask. No bounds cages or per-plank shells.
public sealed class BridgeSelectionOutline
{
    private static readonly List<BridgeSelectionOutline> outlines = new List<BridgeSelectionOutline>();
    private static Material outlineMaterial;
    private static readonly SelectionPass selectionPass = new SelectionPass();
    private static readonly int Mask = Shader.PropertyToID("_BridgeSelectionMask");
    private static readonly int TempA = Shader.PropertyToID("_BridgeSelectionTempA");
    private static readonly int TempB = Shader.PropertyToID("_BridgeSelectionTempB");
    private readonly MeshFilter[] meshes;
    private readonly Renderer[] renderers;
    private readonly Point point;
    private readonly Bar bar;

    public BridgeSelectionOutline(Transform root)
    {
        point = root.GetComponent<Point>();
        bar = root.GetComponent<Bar>();
        meshes = root.GetComponentsInChildren<MeshFilter>(true);
        renderers = new Renderer[meshes.Length];
        for (int i = 0; i < meshes.Length; i++) renderers[i] = meshes[i].GetComponent<Renderer>();
        if (outlineMaterial == null) outlineMaterial = Resources.Load<Material>("UI/BridgeSelectionOutline");
        if (outlines.Count == 0) RenderPipelineManager.beginCameraRendering += EnqueueOutline;
        outlines.Add(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        RenderPipelineManager.beginCameraRendering -= EnqueueOutline;
        outlines.Clear();
        outlineMaterial = null;
    }

    public void Draw()
    {
        // Re-register surviving objects when Play Mode uses disabled domain/scene reload.
        if (!outlines.Contains(this))
        {
            if (outlines.Count == 0) RenderPipelineManager.beginCameraRendering += EnqueueOutline;
            outlines.Add(this);
        }
        if (outlineMaterial == null) outlineMaterial = Resources.Load<Material>("UI/BridgeSelectionOutline");
    }

    private bool IsVisible(Camera camera)
    {
        if (point != null)
        {
            if (!point.gameObject.activeInHierarchy) return false;
            // Merge regular joints into the same silhouette as their selected beams.
            // Only fixed anchors are excluded in the tilted view.
            if (point.isAnchor &&
                Mathf.Abs(Vector3.Dot(camera.transform.forward, Vector3.forward)) < 0.996f) return false;
            bool selected = point.isSelected;
            if (!selected)
                foreach (Bar connected in point.ConnectedBars)
                    if (connected != null && connected.isHighlighted && connected.gameObject.activeInHierarchy)
                    { selected = true; break; }
            if (!selected) return false;
        }
        else if (bar == null || !bar.isHighlighted || !bar.gameObject.activeInHierarchy) return false;
        return true;
    }

    private static void EnqueueOutline(ScriptableRenderContext context, Camera camera)
    {
        if (!Application.isPlaying || outlineMaterial == null) return;
        GameManager manager = GameManager.Instance;
        if (manager == null || manager.CurrentState != GameManager.GameState.Building ||
            manager.ActiveBuildLocation == null || camera != manager.ActiveBuildLocation.locationCamera) return;
        BarCreator creator = BuildUIController.Instance != null ? BuildUIController.Instance.barCreator : null;
        if (creator != null && creator.isSimulating) return;
        bool any = false;
        foreach (var outline in outlines) if (outline.IsVisible(camera)) { any = true; break; }
        if (!any) return;
        if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
            data.scriptableRenderer.EnqueuePass(selectionPass);
    }

    private sealed class SelectionPass : ScriptableRenderPass
    {
        public SelectionPass() { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing; }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            RenderOutlines(context, ref renderingData);
        }
    }

    private static void RenderOutlines(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        BarCreator creator = BuildUIController.Instance != null ? BuildUIController.Instance.barCreator : null;

        // Full-resolution silhouettes avoid two-pixel stair steps on distant thin beams.
        // Allocate only while selected, and scale the border with projected world size.
        int width = Mathf.Max(1, renderingData.cameraData.cameraTargetDescriptor.width);
        int height = Mathf.Max(1, renderingData.cameraData.cameraTargetDescriptor.height);
        Vector3 center = Vector3.zero;
        int selectedCount = 0;
        foreach (var outline in outlines)
        {
            if (!outline.IsVisible(camera) || outline.bar == null) continue;
            center += (outline.bar.StartPosition + outline.bar.EndPosition) * 0.5f;
            selectedCount++;
        }
        if (selectedCount > 0) center /= selectedCount;
        else
            foreach (var outline in outlines)
                if (outline.IsVisible(camera) && outline.point != null)
                { center = outline.point.transform.position; break; }
        float depth = Mathf.Max(camera.nearClipPlane, Vector3.Dot(center - camera.transform.position, camera.transform.forward));
        float worldHeight = camera.orthographic ? 2f * camera.orthographicSize :
            2f * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float zoomScale = Mathf.Clamp(height / Mathf.Max(worldHeight, 0.001f) / 24f, 0.25f, 1f);
        var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
            ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
        CommandBuffer cmd = CommandBufferPool.Get("Bridge Mesh Selection Outline");
        try
        {
            cmd.GetTemporaryRT(Mask, width, height, 0, FilterMode.Bilinear, format, RenderTextureReadWrite.Linear);
            cmd.GetTemporaryRT(TempA, width, height, 0, FilterMode.Bilinear, format, RenderTextureReadWrite.Linear);
            cmd.GetTemporaryRT(TempB, width, height, 0, FilterMode.Bilinear, format, RenderTextureReadWrite.Linear);
            cmd.SetRenderTarget(Mask);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetViewport(new Rect(0, 0, width, height));
            Matrix4x4 viewProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            foreach (var outline in outlines)
            {
                if (!outline.IsVisible(camera)) continue;
                for (int i = 0; i < outline.meshes.Length; i++)
                {
                    MeshFilter filter = outline.meshes[i];
                    Renderer renderer = outline.renderers[i];
                    if (filter == null || filter.sharedMesh == null || renderer == null || !renderer.enabled ||
                        !filter.gameObject.activeInHierarchy || (camera.cullingMask & (1 << filter.gameObject.layer)) == 0) continue;
                    cmd.SetGlobalMatrix("_BridgeMaskMVP", viewProjection * filter.transform.localToWorldMatrix);
                    for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                        cmd.DrawMesh(filter.sharedMesh, filter.transform.localToWorldMatrix, outlineMaterial, submesh, 0);
                }
            }
            // Closing fills narrow plank gaps without replacing the silhouette with a rectangle.
            cmd.SetGlobalVector("_BridgeMaskTexelSize", new Vector4(1f / width, 1f / height, width, height));
            // Keep gap closing proportional too, so zooming out doesn't merge thin trusses.
            cmd.SetGlobalFloat("_BridgeClosingStep", 2f * zoomScale);
            DrawFullscreen(cmd, Mask, TempA, width, height, 1);
            DrawFullscreen(cmd, TempA, TempB, width, height, 2);
            DrawFullscreen(cmd, TempB, TempA, width, height, 3);
            DrawFullscreen(cmd, TempA, TempB, width, height, 4);
            Color color = creator != null ? creator.selectionOutlineColor : Color.white;
            color.a = 1f;
            cmd.SetGlobalColor("_BridgeOutlineColor", color);
            cmd.SetGlobalFloat("_BridgeOutlinePixels", Mathf.Max(1f,
                Mathf.Clamp(creator != null ? creator.selectionOutlineWidth : 4f, 2f, 8f) * zoomScale));
            cmd.SetGlobalTexture("_BridgeOriginalMask", Mask);
            // Composite into URP's live color target before its final screen blit.
            RenderTargetIdentifier target = renderingData.cameraData.renderer.cameraColorTargetHandle.nameID;
            DrawFullscreen(cmd, TempB, target,
                renderingData.cameraData.cameraTargetDescriptor.width,
                renderingData.cameraData.cameraTargetDescriptor.height, 5);
            cmd.SetViewport(camera.pixelRect);
            cmd.ReleaseTemporaryRT(Mask); cmd.ReleaseTemporaryRT(TempA); cmd.ReleaseTemporaryRT(TempB);
            context.ExecuteCommandBuffer(cmd);
        }
        finally { CommandBufferPool.Release(cmd); }
    }

    internal static void DrawFullscreen(CommandBuffer cmd, RenderTargetIdentifier source,
        RenderTargetIdentifier target, int width, int height, int pass, Material material = null)
    {
        cmd.SetGlobalTexture("_BridgeMaskSource", source);
        // Explicit Load preserves the scene beneath the alpha-blended border.
        cmd.SetRenderTarget(target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
        cmd.SetViewport(new Rect(0, 0, width, height));
        cmd.DrawProcedural(Matrix4x4.identity, material != null ? material : outlineMaterial,
            pass, MeshTopology.Triangles, 3, 1);
    }

    public void Dispose()
    {
        outlines.Remove(this);
        if (outlines.Count == 0) RenderPipelineManager.beginCameraRendering -= EnqueueOutline;
    }
}

public class Bar : MonoBehaviour
{
    public Vector3 StartPosition; 
    public Vector3 EndPosition; // --- NEW: Stores the end coordinate to repair broken prefabs! ---

    public BridgeMaterialSO materialData;

    public Point startPoint;
    public Point endPoint;

    [SerializeField, HideInInspector] private BuildLocation ownerLocation;
    public BuildLocation OwnerLocation => ownerLocation;

    [HideInInspector] public Vector3 preSimPos;
    [HideInInspector] public Quaternion preSimRot;
    [HideInInspector] public Vector3 visualSize = new Vector3(1f, 0.2f, 0.2f);
    [HideInInspector] public float currentLength = 0f;
    [HideInInspector] public float currentAngle = 0f; 
    
    [HideInInspector] public bool isHighlighted = false; 

    private List<GameObject> visualSegments = new List<GameObject>();
    private float baseLength = 1f; 
    private Vector3 originalScale = Vector3.one;
    
    private GameObject pierCapInstance;
    private Vector3 originalCapScale = Vector3.one;
    
    private float capTopOffset = 0f;
    private float capBottomOffset = 0f;

    private BridgeSelectionOutline selectionOutline;

    private void Awake()
    {
        if (Application.isPlaying && ownerLocation == null && GameManager.Instance != null)
            ownerLocation = GameManager.Instance.ActiveBuildLocation;
    }

    public void AssignOwner(BuildLocation location, bool overwriteExisting = false)
    {
        if (location != null && (ownerLocation == null || overwriteExisting))
            ownerLocation = location;
    }

    public void InferOwnerFromEndpoints()
    {
        if (ownerLocation != null) return;
        if (startPoint != null && startPoint.OwnerLocation != null)
            ownerLocation = startPoint.OwnerLocation;
        else if (endPoint != null && endPoint.OwnerLocation != null)
            ownerLocation = endPoint.OwnerLocation;
    }

    // --- NEW: Re-knits the graph if this Bar was loaded from a Prefab and lost its scene references ---
    public void AutoRepairEndpoints()
    {
        if (startPoint == null || endPoint == null)
        {
            Point[] allPointsInScene = FindObjectsOfType<Point>();
            foreach (Point p in allPointsInScene)
            {
                if (startPoint == null && Vector3.Distance(StartPosition, p.transform.position) < 0.1f)
                {
                    startPoint = p;
                    if (!p.ConnectedBars.Contains(this)) p.ConnectedBars.Add(this);
                }
                
                if (endPoint == null && Vector3.Distance(EndPosition, p.transform.position) < 0.1f)
                {
                    endPoint = p;
                    if (!p.ConnectedBars.Contains(this)) p.ConnectedBars.Add(this);
                }
            }
        }
    }

    private void OnEnable()
    {
        if (startPoint != null && !startPoint.ConnectedBars.Contains(this)) startPoint.ConnectedBars.Add(this);
        if (endPoint != null && !endPoint.ConnectedBars.Contains(this)) endPoint.ConnectedBars.Add(this);
    }

    private void OnDisable()
    {
        if (!gameObject.activeInHierarchy)
        {
            RemoveConnections();
        }
    }

    private void OnDestroy()
    {
        selectionOutline?.Dispose();
        RemoveConnections();
    }

    private void RemoveConnections()
    {
        if (startPoint != null) startPoint.ConnectedBars.Remove(this);
        if (endPoint != null) endPoint.ConnectedBars.Remove(this);
    }

    public void Initialize(BridgeMaterialSO data)
    {
        materialData = data;
        visualSegments.Clear();
        
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(false); 
            Destroy(child.gameObject);
        }
        
        if (materialData.segmentPrefab != null)
        {
            int spawnCount = materialData.isDualBeam ? 2 : 1;

            for (int i = 0; i < spawnCount; i++)
            {
                GameObject newSegment = Instantiate(materialData.segmentPrefab, transform);
                newSegment.name = materialData.isDualBeam ? $"VisualSegment_{i}" : "VisualSegment";
                
                float offsetValue = 0f;
                if (materialData.isDualBeam)
                {
                    offsetValue = (i == 0) ? materialData.zOffset : -materialData.zOffset;
                }
                
                newSegment.transform.localPosition = new Vector3(0, 0, offsetValue);

                var renderer = newSegment.GetComponentInChildren<Renderer>();
                if (renderer != null && i == 0)
                {
                    originalScale = newSegment.transform.localScale;
                    
                    baseLength = materialData.isPier ? renderer.bounds.size.y : renderer.bounds.size.x;
                    visualSize = renderer.bounds.size; 
                    if (baseLength <= 0f) baseLength = 1f; 
                }
                
                newSegment.transform.localScale = Vector3.zero;
                visualSegments.Add(newSegment);
            }
        }

        if (materialData.isPier && materialData.pierCapPrefab != null)
        {
            pierCapInstance = Instantiate(materialData.pierCapPrefab, transform);
            pierCapInstance.name = "PierCap";
            originalCapScale = pierCapInstance.transform.localScale;

            pierCapInstance.transform.position = Vector3.zero;
            pierCapInstance.transform.rotation = Quaternion.identity;
            
            Renderer capRend = pierCapInstance.GetComponentInChildren<Renderer>();
            if (capRend != null)
            {
                capTopOffset = capRend.bounds.max.y;    
                capBottomOffset = capRend.bounds.min.y; 
            }

            pierCapInstance.transform.localScale = Vector3.zero; 
        }

        selectionOutline?.Dispose();
        selectionOutline = null;
    }

    public void SetHighlight(bool highlight, Material highlightMat)
    {
        isHighlighted = highlight;
        if (highlight && selectionOutline == null) selectionOutline = new BridgeSelectionOutline(transform);
        if (highlight)
        {
            if (startPoint != null) startPoint.PrepareSelectionOutline();
            if (endPoint != null) endPoint.PrepareSelectionOutline();
        }
    }

    private void LateUpdate()
    {
        if (isHighlighted) selectionOutline?.Draw();
    }

    public void UpdateCreatingBar(Vector3 ToPosition) 
    {
        EndPosition = ToPosition;
        if (visualSegments.Count == 0 || materialData == null) return;

        Vector3 actualStart = StartPosition;
        Vector3 actualEnd = ToPosition;
        if (ShouldSwapEndpointOrder(actualStart, actualEnd, materialData.isPier))
        {
            actualStart = ToPosition;
            actualEnd = StartPosition;
        }

        // Bars are built on one bridge plane. Keep their length positive and let
        // the rotation carry the full start-to-end direction, including 180 degrees.
        Vector3 direction = actualEnd - actualStart;
        direction.z = 0f;
        float totalDistance = direction.magnitude;
        
        currentLength = totalDistance;
        
        if (totalDistance < 0.01f) 
        {
            foreach (var seg in visualSegments) seg.transform.localScale = Vector3.zero;
            if (pierCapInstance != null) pierCapInstance.transform.localScale = Vector3.zero;
            return;
        }

        if (materialData.isPier)
        {
            currentAngle = 90f; 

            float targetPillarTopY = actualEnd.y;
            if (pierCapInstance != null)
            {
                targetPillarTopY = actualEnd.y - capTopOffset + capBottomOffset; 
            }

            float adjustedDistance = targetPillarTopY - actualStart.y;
            if (adjustedDistance < 0.05f) adjustedDistance = 0.05f; 

            Vector3 midPointAdjusted = actualStart + (Vector3.up * (adjustedDistance / 2f));
            transform.SetPositionAndRotation(midPointAdjusted, Quaternion.identity);

            float scaleMultiplier = adjustedDistance / baseLength;
            Vector3 newScale = new Vector3(
                Mathf.Abs(originalScale.x),
                Mathf.Abs(originalScale.y) * scaleMultiplier,
                Mathf.Abs(originalScale.z));

            foreach (var seg in visualSegments)
            {
                seg.transform.localScale = newScale;
            }

            if (pierCapInstance != null)
            {
                pierCapInstance.transform.localScale = new Vector3(
                    Mathf.Abs(originalCapScale.x),
                    Mathf.Abs(originalCapScale.y),
                    Mathf.Abs(originalCapScale.z));
                
                Vector3 capPos = actualEnd;
                capPos.y -= capTopOffset; 
                
                pierCapInstance.transform.position = capPos;
                pierCapInstance.transform.rotation = Quaternion.identity;
            }
        }
        else
        {
            Vector3 midPoint = (actualStart + actualEnd) * 0.5f;
            midPoint.z = actualStart.z;

            // actualStart/actualEnd are spatially canonical, so this angle never
            // flips merely because the player dragged in the opposite direction.
            float rotationAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            currentAngle = rotationAngle;

            transform.SetPositionAndRotation(midPoint, Quaternion.Euler(0, 0, rotationAngle));

            float scaleMultiplier = totalDistance / Mathf.Max(Mathf.Abs(baseLength), 0.0001f);
            Vector3 newScale = new Vector3(
                Mathf.Abs(originalScale.x) * scaleMultiplier,
                Mathf.Abs(originalScale.y),
                Mathf.Abs(originalScale.z));

            foreach (var seg in visualSegments)
            {
                seg.transform.localScale = newScale;
            }
        }
    }

    /// <summary>
    /// Makes endpoint identity independent of drawing direction. Call this after
    /// assigning startPoint/endPoint and before creating physics joints.
    /// </summary>
    public void NormalizeEndpointOrder()
    {
        if (startPoint == null || endPoint == null || materialData == null)
            return;

        if (ShouldSwapEndpointOrder(
            startPoint.transform.position,
            endPoint.transform.position,
            materialData.isPier))
        {
            Point previousStart = startPoint;
            startPoint = endPoint;
            endPoint = previousStart;
        }

        StartPosition = startPoint.transform.position;
        EndPosition = endPoint.transform.position;
        UpdateCreatingBar(EndPosition);
    }

    private static bool ShouldSwapEndpointOrder(Vector3 first, Vector3 second, bool isPier)
    {
        const float epsilon = 0.0001f;

        if (isPier)
            return first.y > second.y + epsilon;

        if (first.x > second.x + epsilon) return true;
        if (first.x < second.x - epsilon) return false;
        if (first.y > second.y + epsilon) return true;
        if (first.y < second.y - epsilon) return false;
        return first.z > second.z;
    }

    public float GetCost()
    {
        if (materialData == null) return 0f;
        int multiplier = materialData.isDualBeam ? 2 : 1;
        return currentLength * materialData.costPerMeter * multiplier;
    }
}
