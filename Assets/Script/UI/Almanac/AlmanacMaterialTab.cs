using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AlmanacMaterialTab : MonoBehaviour
{
    public List<BridgeMaterialSO> GetAllMaterials()
    {
        return BuildDatabase();
    }

    [Header("Material Database")]
    [Tooltip("Optional manual list. If empty, all BridgeMaterialSO assets in Resources are found automatically.")]
    [SerializeField] private List<BridgeMaterialSO> allMaterials = new List<BridgeMaterialSO>();

    [Header("Grid")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private AlmanacMaterialButton materialButtonPrefab;
    [Tooltip("When off, only materials acknowledged with GOT IT are listed.")]
    [SerializeField] private bool showLockedMaterials;
    [Tooltip("Sort names within each discovery/category group. Discovered materials appear first, ordered Roads, Beams, Piers, then Ropes.")]
    [SerializeField] private bool sortAlphabetically = true;

    [Header("Empty State")]
    [SerializeField] private GameObject emptyStateRoot;
    [SerializeField] private TMP_Text emptyStateText;

    private readonly List<AlmanacMaterialButton> spawnedButtons =
        new List<AlmanacMaterialButton>();

    private void OnEnable()
    {
        if (PlayerDataManager.Instance != null)
        {
            PlayerDataManager.Instance.OnMaterialDiscovered += HandleMaterialDiscovered;
            PlayerDataManager.Instance.MarkMaterialsAlmanacRead();
        }

        RefreshMaterials();
    }

    private void OnDisable()
    {
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.OnMaterialDiscovered -= HandleMaterialDiscovered;
    }

    public void RefreshMaterials()
    {
        ClearSpawnedButtons();

        if (buttonContainer == null || materialButtonPrefab == null)
        {
            Debug.LogWarning("[AlmanacMaterialTab] Button Container or Material Button Prefab is missing.", this);
            SetEmptyState(true, "Material archive is not configured.");
            return;
        }

        List<BridgeMaterialSO> database = BuildDatabase();

        foreach (BridgeMaterialSO material in database)
        {
            bool discovered = MaterialDiscoverySaveManager.IsDiscovered(material);
            if (!discovered && !showLockedMaterials) continue;

            AlmanacMaterialButton entry = Instantiate(materialButtonPrefab, buttonContainer, false);
            entry.Configure(material, discovered, OpenMaterial);
            entry.gameObject.SetActive(true);
            spawnedButtons.Add(entry);
        }

        SetEmptyState(spawnedButtons.Count == 0,
            showLockedMaterials ? "No materials are available." : "Discover materials while exploring.");
    }

    private List<BridgeMaterialSO> BuildDatabase()
    {
        List<BridgeMaterialSO> database = new List<BridgeMaterialSO>();
        HashSet<string> seenIds = new HashSet<string>();

        AddUnique(allMaterials, database, seenIds);
        AddUnique(Resources.LoadAll<BridgeMaterialSO>(string.Empty), database, seenIds);
        SortDatabase(database);
        return database;
    }

    private void SortDatabase(List<BridgeMaterialSO> database)
    {
        // Snapshot discovery once per refresh. Almanac knowledge is independent
        // of the current contract's building restrictions.
        var discovered = new HashSet<BridgeMaterialSO>();
        var authoredOrder = new Dictionary<BridgeMaterialSO, int>();
        for (int i = 0; i < database.Count; i++)
        {
            BridgeMaterialSO material = database[i];
            authoredOrder.Add(material, i);
            if (MaterialDiscoverySaveManager.IsDiscovered(material)) discovered.Add(material);
        }

        database.Sort((a, b) =>
        {
            int availability = discovered.Contains(b).CompareTo(discovered.Contains(a));
            if (availability != 0) return availability;
            int category = MaterialToolbarOrdering.Category(a).CompareTo(MaterialToolbarOrdering.Category(b));
            if (category != 0) return category;
            if (sortAlphabetically)
            {
                int name = string.Compare(a.GetDisplayName(), b.GetDisplayName(),
                    System.StringComparison.OrdinalIgnoreCase);
                if (name != 0) return name;
                int id = string.Compare(a.Id, b.Id, System.StringComparison.Ordinal);
                if (id != 0) return id;
            }
            return authoredOrder[a].CompareTo(authoredOrder[b]);
        });
    }

    private static void AddUnique(
        IEnumerable<BridgeMaterialSO> source,
        ICollection<BridgeMaterialSO> destination,
        ISet<string> seenIds)
    {
        if (source == null) return;
        foreach (BridgeMaterialSO material in source)
        {
            if (material == null || string.IsNullOrWhiteSpace(material.Id) || !seenIds.Add(material.Id))
                continue;
            destination.Add(material);
        }
    }

    private void OpenMaterial(BridgeMaterialSO material)
    {
        if (!MaterialDiscoverySaveManager.IsDiscovered(material)) return;
        LessonUIManager reader = LessonUIManager.Instance;
        if (reader == null)
        {
            Debug.LogWarning("[AlmanacMaterialTab] The scrollable reference reader (LessonUIManager) is not available.", this);
            return;
        }

        // The full reference uses the existing scrollable lesson canvas.
        // Close the Almanac first so the book cannot cover or compete with it.
        if (AlmanacManager.Instance != null &&
            AlmanacManager.Instance.Panel != null &&
            AlmanacManager.Instance.Panel.activeInHierarchy)
        {
            AlmanacManager.Instance.CloseAlmanacThen(
                () => reader.ShowReference(material.GetDisplayName(), material.AlmanacImage,
                    ItemUnlockUI.BuildMaterialReferenceDetails(material)));
            return;
        }

        reader.ShowReference(material.GetDisplayName(), material.AlmanacImage,
            ItemUnlockUI.BuildMaterialReferenceDetails(material));
    }

    private void HandleMaterialDiscovered(string materialId)
    {
        RefreshMaterials();
    }

    private void ClearSpawnedButtons()
    {
        foreach (AlmanacMaterialButton entry in spawnedButtons)
        {
            if (entry != null) Destroy(entry.gameObject);
        }
        spawnedButtons.Clear();
    }

    private void SetEmptyState(bool visible, string message)
    {
        if (emptyStateRoot != null) emptyStateRoot.SetActive(visible);
        if (emptyStateText != null) emptyStateText.text = message;
    }
}
