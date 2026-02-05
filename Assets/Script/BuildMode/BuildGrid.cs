using UnityEngine;

public class BuildGrid : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────
    // 1. PUBLIC / INSPECTOR VARIABLES (what you can tweak in the Editor)
    // ────────────────────────────────────────────────────────────────

    [Header("Grid Settings")]
    public float gridSize = 1f;         // Size of each grid cell (in local units before scaling)
    public int gridWidth = 20;          // How many cells wide the grid mesh is
    public int gridDepth = 20;          // How many cells deep the grid mesh is
    public float gridHeight = 0.01f;    // (Unused now – was for 3D grid height)

    [Header("Visuals")]
    public Material gridMaterial;       // Assign an Unlit/Transparent material here (explained later)
    public Color gridColor = new Color(1f, 1f, 1f, 0.4f); // White with 40% opacity
    public float lineWidth = 0.05f;     // (Unused in current version – could be used for line renderer later)

    [Header("Snapping")]
    public bool snapToGrid = true;      // Whether to snap positions to grid points
    public LayerMask gridLayer = 1;     // (Unused – could be used for raycasts later)

    [Header("Camera Parenting & Distance")]
    [SerializeField] private float distanceFromCamera = 10f;    // How far in front of camera (world units)
    [SerializeField] private float verticalOffset = 0.1f;       // Slight lift above floor to avoid Z-fighting
    [SerializeField] private bool smoothMovement = true;        // Smooth camera-following (lerp)
    [SerializeField] private float smoothSpeed = 15f;           // How fast it lerps to target position

    // ────────────────────────────────────────────────────────────────
    // 2. PRIVATE VARIABLES (internal state)
    // ────────────────────────────────────────────────────────────────

    private BuildLocation parentLocation;       // Reference to the BuildLocation that activated us
    private GameObject gridMeshObject;          // The child GameObject that holds the actual plane mesh
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;          // So we can raycast onto the grid if needed later

    private Camera activeCamera;                // The camera we're following (usually main or location-specific)
    private Transform gridPlaneTransform;       // Quick reference to gridMeshObject.transform

    private const float GRID_Y_OFFSET = 0.02f;  // Tiny height offset so grid doesn't clip into floor

    // ────────────────────────────────────────────────────────────────
    // 3. AWAKE – Currently empty (initialization happens in Initialize())
    // ────────────────────────────────────────────────────────────────
    private void Awake()
    {
        // We'll set this up in Initialize
    }

    // ────────────────────────────────────────────────────────────────
    // 4. INITIALIZE – Called by GameManager / BuildLocation when entering build mode
    // ────────────────────────────────────────────────────────────────
    public void Initialize(BuildLocation location)
    {
        parentLocation = location;

        // Find the best camera to follow
        activeCamera = GetActiveCamera();
        if (activeCamera == null)
        {
            Debug.LogWarning("No active camera found → BuildGrid cannot initialize properly", this);
            gameObject.SetActive(false);
            return;
        }

        // ─── VERY IMPORTANT PART ─────────────────────────────────────
        // Parent the ENTIRE BuildGrid GameObject directly to the CAMERA
        // This makes the grid move/rotate with the camera automatically
        transform.SetParent(activeCamera.transform, false);
        transform.localPosition = Vector3.zero;     // Reset local pos (we'll calculate real pos in Update)
        transform.localRotation = Quaternion.identity;

        // Apply custom scale → makes grid appear VERY flat, narrow & wide
        //   X = 0.09 → very narrow in local X (left-right)
        //   Y = 1    → keeps height (but we rotate it 90° later)
        //   Z = 0.05 → very shallow in depth (forward-back)
        transform.localScale = new Vector3(0.09f, 1f, 0.01f);

        // Create the actual visual grid plane (a child object)
        CreateGrid();

        // Cache reference to the plane
        gridPlaneTransform = gridMeshObject.transform;

        // Rotate the plane 90° on X-axis so it stands upright (like paper in front of camera)
        // After this rotation, local Y becomes world "up", local Z becomes world "depth"
        gridPlaneTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Set initial position
        UpdateGridPosition();

        // Make sure we're visible
        gameObject.SetActive(true);
        gameObject.name = "BuildGrid_CameraAttached_" + location.name;

        Debug.Log("BuildGrid initialized – parented to camera", this);
    }

    // ────────────────────────────────────────────────────────────────
    // 5. LATEUPDATE – Runs every frame after camera has moved
    // ────────────────────────────────────────────────────────────────
    private void LateUpdate()
    {
        if (activeCamera == null || !gameObject.activeSelf)
            return;

        UpdateGridPosition();
    }

    // ────────────────────────────────────────────────────────────────
    // 6. UPDATEGRIDPOSITION – Keeps grid always in front of camera
    // ────────────────────────────────────────────────────────────────
    private void UpdateGridPosition()
    {
        // Direction the camera is looking
        Vector3 forwardDirection = activeCamera.transform.forward;

        // Desired world position: camera pos + forward * distance + slight up lift
        Vector3 targetWorldPos = activeCamera.transform.position 
            + forwardDirection * distanceFromCamera
            + Vector3.up * verticalOffset;

        // Convert that world position into local position relative to our parent (the camera)
        Vector3 targetLocalPos = activeCamera.transform.InverseTransformPoint(targetWorldPos);

        if (smoothMovement)
        {
            // Smoothly move toward target (nice feel, no jitter)
            transform.localPosition = Vector3.Lerp(
                transform.localPosition,
                targetLocalPos,
                Time.deltaTime * smoothSpeed
            );
        }
        else
        {
            // Instant follow (can feel jittery if camera moves fast)
            transform.localPosition = targetLocalPos;
        }

        // Rotation is already handled by parenting to camera
        // The 90° X rotation on the plane child makes it face the camera correctly
    }

    // ────────────────────────────────────────────────────────────────
    // 7. CREATEGIRD – Builds the actual visual plane mesh
    // ────────────────────────────────────────────────────────────────
    private void CreateGrid()
    {
        // Create child GameObject for the plane
        gridMeshObject = new GameObject("GridPlane");
        gridMeshObject.transform.SetParent(transform, false);
        gridMeshObject.transform.localPosition = Vector3.zero;

        // Add required components
        meshFilter   = gridMeshObject.AddComponent<MeshFilter>();
        meshRenderer = gridMeshObject.AddComponent<MeshRenderer>();
        meshCollider = gridMeshObject.AddComponent<MeshCollider>();

        // Create or use material
        if (gridMaterial == null)
        {
            // Fallback: create a simple Unlit transparent material
            gridMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            gridMaterial.color = gridColor;
            gridMaterial.SetColor("_BaseColor", gridColor);
            // Optional: enable transparency
            gridMaterial.SetFloat("_Surface", 1); // Transparent
            gridMaterial.SetFloat("_Blend", 0);   // Alpha
        }

        meshRenderer.material = gridMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Generate the mesh (a simple quad grid)
        Mesh gridMesh = GenerateGridMesh();
        meshFilter.mesh = gridMesh;
        meshCollider.sharedMesh = gridMesh;
    }

    // ────────────────────────────────────────────────────────────────
    // 8. GENERATEGRIDMESH – Creates a flat grid of quads
    // ────────────────────────────────────────────────────────────────
    private Mesh GenerateGridMesh()
    {
        Mesh mesh = new Mesh();
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();

        // Create vertex grid (centered at 0,0,0 in local space)
        for (int z = 0; z <= gridDepth; z++)
        {
            for (int x = 0; x <= gridWidth; x++)
            {
                // Center the grid
                float xPos = (x - gridWidth * 0.5f) * gridSize;
                float zPos = (z - gridDepth * 0.5f) * gridSize;

                // Tiny Y offset to avoid perfect floor clipping
                vertices.Add(new Vector3(xPos, GRID_Y_OFFSET, zPos));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(x / (float)gridWidth, z / (float)gridDepth));
            }
        }

        // Create quads (two triangles per cell)
        for (int z = 0; z < gridDepth; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int current = z * (gridWidth + 1) + x;
                int next = current + 1;

                // First triangle
                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(current + gridWidth + 1);

                // Second triangle
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

    // ────────────────────────────────────────────────────────────────
    // 9. GETNEARESTGRIDPOINT – Main snapping function (used by build system)
    // ────────────────────────────────────────────────────────────────
    public Vector3 GetNearestGridPoint(Vector3 worldPosition)
    {
        if (!snapToGrid) return worldPosition;

        // Convert world point → local to our transform (camera space)
        Vector3 localPos = transform.InverseTransformPoint(worldPosition);

        // Snap to nearest grid cell (gridSize is in local units)
        localPos.x = Mathf.Round(localPos.x / gridSize) * gridSize;
        localPos.z = Mathf.Round(localPos.z / gridSize) * gridSize;
        localPos.y = GRID_Y_OFFSET; // Keep tiny offset

        // Convert back to world space
        return transform.TransformPoint(localPos);
    }

    // ────────────────────────────────────────────────────────────────
    // 10. HELPER – Find the camera we should follow
    // ────────────────────────────────────────────────────────────────
    private Camera GetActiveCamera()
    {
        // Prefer location-specific camera if set and enabled
        if (parentLocation?.locationCamera != null && parentLocation.locationCamera.enabled)
            return parentLocation.locationCamera;

        // Otherwise use main camera
        Camera main = Camera.main;
        if (main != null && main.enabled)
            return main;

        // Last resort: any camera
        return FindObjectOfType<Camera>();
    }

    // ────────────────────────────────────────────────────────────────
    // 11. CLEANUP
    // ────────────────────────────────────────────────────────────────
    private void OnDestroy()
    {
        if (gridMeshObject != null)
            DestroyImmediate(gridMeshObject);
    }

    // Optional editor gizmo to see where grid would be
    private void OnDrawGizmosSelected()
    {
        if (activeCamera == null) return;
        Gizmos.color = new Color(0, 1, 1, 0.5f);
        Vector3 pos = activeCamera.transform.position + activeCamera.transform.forward * distanceFromCamera;
        Gizmos.DrawWireSphere(pos, 0.4f);
        Gizmos.DrawLine(activeCamera.transform.position, pos);
    }
}