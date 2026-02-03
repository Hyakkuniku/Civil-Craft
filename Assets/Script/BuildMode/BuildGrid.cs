using UnityEngine;

public class BuildGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    public float gridSize = 1f;
    public int gridWidth = 20;
    public int gridDepth = 20;
    public float gridHeight = 0.01f;

    [Header("Visuals")]
    public Material gridMaterial;
    public Color gridColor = new Color(1f, 1f, 1f, 0.4f);
    public float lineWidth = 0.05f;

    [Header("Snapping")]
    public bool snapToGrid = true;
    public LayerMask gridLayer = 1;

    [Header("Camera Alignment")]
    public bool alignToCamera = true;     // ← NEW: enable/disable camera alignment
    public float cameraAlignDistance = 15f; // ← NEW: how far camera should be to auto-align

    private BuildLocation parentLocation;
    private GameObject gridMeshObject;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    private const float GRID_Y_OFFSET = 0.02f;

    public void Initialize(BuildLocation location)
    {
        parentLocation = location;
        CreateGrid();
        AlignToCamera();  // ← NEW: align grid to camera
        gameObject.SetActive(true);
        gameObject.name = "BuildGrid_" + location.name;
    }

    private void CreateGrid()
    {
        gridMeshObject = new GameObject("GridPlane");
        gridMeshObject.transform.SetParent(transform, false);
        gridMeshObject.transform.localPosition = Vector3.zero;
        gridMeshObject.transform.localRotation = Quaternion.identity; // reset rotation

        meshFilter   = gridMeshObject.AddComponent<MeshFilter>();
        meshRenderer = gridMeshObject.AddComponent<MeshRenderer>();
        meshCollider = gridMeshObject.AddComponent<MeshCollider>();

        if (gridMaterial == null)
        {
            gridMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            gridMaterial.color = gridColor;
            gridMaterial.SetColor("_BaseColor", gridColor);
        }

        meshRenderer.material = gridMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Mesh gridMesh = GenerateGridMesh();
        meshFilter.mesh = gridMesh;
        meshCollider.sharedMesh = gridMesh;
    }

    // ─────────────────── NEW: CAMERA ALIGNMENT ───────────────────
    private void AlignToCamera()
    {
        if (!alignToCamera) return;

        Camera activeCam = GetActiveCamera();
        if (activeCam == null) return;

        // Calculate grid plane that faces the camera
        Vector3 camPos = activeCam.transform.position;
        Vector3 gridCenter = parentLocation.transform.position;

        // Normal of the grid plane = direction from grid center to camera
        Vector3 gridNormal = (camPos - gridCenter).normalized;

        // Make sure normal points somewhat upwards (for bridges/platforms)
        gridNormal = Vector3.Slerp(gridNormal, Vector3.up, 0.3f);

        // Create rotation that makes this normal face the camera
        Quaternion gridRotation = Quaternion.LookRotation(-gridNormal, Vector3.up);

        // Apply rotation to grid mesh object
        gridMeshObject.transform.rotation = gridRotation;

        Debug.Log($"Grid aligned to camera. Normal: {gridNormal}, Rotation: {gridRotation.eulerAngles}", this);
    }

    private Camera GetActiveCamera()
    {
        // Priority 1: BuildLocation's dedicated camera (if active)
        if (parentLocation.locationCamera != null && parentLocation.locationCamera.enabled)
            return parentLocation.locationCamera;

        // Priority 2: Main camera
        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.enabled)
            return mainCam;

        // Priority 3: First enabled camera in scene
        return FindObjectOfType<Camera>();
    }

    private Mesh GenerateGridMesh()
    {
        Mesh mesh = new Mesh();
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();

        // ─────────────────── VERTICES ───────────────────
        for (int z = 0; z <= gridDepth; z++)
        {
            for (int x = 0; x <= gridWidth; x++)
            {
                float xPos = (x - gridWidth * 0.5f) * gridSize;
                float zPos = (z - gridDepth * 0.5f) * gridSize;
                vertices.Add(new Vector3(xPos, GRID_Y_OFFSET, zPos));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(x / (float)gridWidth, z / (float)gridDepth));
            }
        }

        // ─────────────────── TRIANGLES ───────────────────
        for (int z = 0; z < gridDepth; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int current = z * (gridWidth + 1) + x;
                int next = current + 1;

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(current + gridWidth + 1);

                triangles.Add(next);
                triangles.Add(next + gridWidth + 1);
                triangles.Add(current + gridWidth + 1);
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.normals = normals.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateBounds();

        return mesh;
    }

    public Vector3 GetNearestGridPoint(Vector3 worldPosition)
    {
        if (!snapToGrid) return worldPosition;

        // Transform to local space (takes rotation into account)
        Vector3 localPos = transform.InverseTransformPoint(worldPosition);
        
        // Snap to grid in local XZ plane
        localPos.x = Mathf.Round(localPos.x / gridSize) * gridSize;
        localPos.z = Mathf.Round(localPos.z / gridSize) * gridSize;
        localPos.y = GRID_Y_OFFSET;

        // Transform back to world space
        return transform.TransformPoint(localPos);
    }

    private void OnDestroy()
    {
        if (gridMeshObject != null)
            DestroyImmediate(gridMeshObject);
    }
}