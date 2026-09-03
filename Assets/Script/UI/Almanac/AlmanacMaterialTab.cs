using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AlmanacMaterialTab : MonoBehaviour
{
    [Header("Material Database")]
    [Tooltip("Optional manual list. If empty, all BridgeMaterialSO assets in Resources are found automatically.")]
    [SerializeField] private List<BridgeMaterialSO> allMaterials = new List<BridgeMaterialSO>();

    [Header("Grid")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private AlmanacMaterialButton materialButtonPrefab;
    [Tooltip("When off, only materials acknowledged with GOT IT are listed.")]
    [SerializeField] private bool showLockedMaterials;
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
        if (sortAlphabetically)
        {
            database.Sort((a, b) => string.Compare(
                a.GetDisplayName(), b.GetDisplayName(),
                System.StringComparison.OrdinalIgnoreCase));
        }

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
        return database;
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
        ItemUnlockUI materialPopup = ItemUnlockUI.Instance;
        if (materialPopup == null)
        {
            Debug.LogWarning("[AlmanacMaterialTab] ItemUnlockUI is not available.", this);
            return;
        }

        // The material detail window uses a separate high-priority popup canvas.
        // Close the Almanac first so the book cannot cover or compete with it.
        if (AlmanacManager.Instance != null &&
            AlmanacManager.Instance.Panel != null &&
            AlmanacManager.Instance.Panel.activeInHierarchy)
        {
            AlmanacManager.Instance.CloseAlmanacThen(
                () => materialPopup.ShowMaterialIntroduction(material, null, "CLOSE"));
            return;
        }

        materialPopup.ShowMaterialIntroduction(material, null, "CLOSE");
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
