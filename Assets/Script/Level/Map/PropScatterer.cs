using UnityEngine;

public class PropScatterer : MonoBehaviour
{
    [Header("What to Spawn")]
    [Tooltip("Drag your rocks, bushes, and cactus prefabs here!")]
    public GameObject[] propPrefabs;

    [Header("Spawn Settings")]
    [Tooltip("How many props should we try to spawn?")]
    public int spawnCount = 200;
    [Tooltip("The size of the area to scatter them in (X and Z).")]
    public Vector2 areaSize = new Vector2(100f, 100f);
    
    [Header("Randomization")]
    [Tooltip("Minimum and Maximum size of the props.")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.5f);

    [Header("Placement Rules")]
    [Tooltip("The layer your Sand Plane is on.")]
    public LayerMask groundLayer;
    [Tooltip("Layers to AVOID (like your Canyons, Roads, or Bridges).")]
    public LayerMask avoidLayers;

    [Header("Organization")]
    [Tooltip("An empty GameObject to hold all the spawned props so your Hierarchy stays clean!")]
    public Transform propParentFolder;

    // --- This magic tag creates a button in the script's Right-Click menu! ---
    [ContextMenu("🎲 Scatter Props Now!")]
    public void ScatterProps()
    {
        if (propPrefabs == null || propPrefabs.Length == 0)
        {
            Debug.LogWarning("You forgot to add prefabs to the Prop Scatterer!");
            return;
        }

        if (propParentFolder == null) propParentFolder = this.transform;

        int successfulSpawns = 0;

        for (int i = 0; i < spawnCount; i++)
        {
            // 1. Pick a random X and Z coordinate within our area
            float randomX = transform.position.x + Random.Range(-areaSize.x / 2f, areaSize.x / 2f);
            float randomZ = transform.position.z + Random.Range(-areaSize.y / 2f, areaSize.y / 2f);

            // 2. Start high up in the sky and shoot a laser straight down
            Vector3 rayStart = new Vector3(randomX, transform.position.y + 100f, randomZ);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 300f))
            {
                // 3. Did we hit something we are supposed to avoid? (Like a road or canyon)
                if (((1 << hit.collider.gameObject.layer) & avoidLayers) != 0)
                {
                    continue; // Skip this one and try again!
                }

                // 4. Did we hit the sand?
                if (((1 << hit.collider.gameObject.layer) & groundLayer) != 0)
                {
                    // Pick a random prop from your list
                    GameObject prefabToSpawn = propPrefabs[Random.Range(0, propPrefabs.Length)];

                    // Spawn it!
                    GameObject newProp = Instantiate(prefabToSpawn, hit.point, Quaternion.identity, propParentFolder);

                    // Give it a random rotation so they don't all face the same way
                    newProp.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                    // Give it a random size so the forest looks natural
                    float randomScale = Random.Range(scaleRange.x, scaleRange.y);
                    newProp.transform.localScale = new Vector3(randomScale, randomScale, randomScale);

                    successfulSpawns++;
                }
            }
        }

        Debug.Log($"<color=green>Successfully scattered {successfulSpawns} props!</color>");
    }

    // --- Another magic button to instantly delete the forest if you don't like it ---
    [ContextMenu("🧹 Clear All Props")]
    public void ClearProps()
    {
        if (propParentFolder == null) return;

        // DestroyImmediate is used in the Editor instead of standard Destroy
        int childCount = propParentFolder.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(propParentFolder.GetChild(i).gameObject);
        }
        Debug.Log("Props cleared. Ready to try again!");
    }

    // Draws a blue box in the Editor so you can see exactly where things will spawn!
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawCube(transform.position, new Vector3(areaSize.x, 1f, areaSize.y));
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 1f, areaSize.y));
    }
}