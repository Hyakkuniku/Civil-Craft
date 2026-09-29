using System;
using System.Collections;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Scene-authored leaderboard UI. Rows and controls are saved in Mode Selection;
/// only their contents change at runtime. No client-side statistic writes occur.
/// </summary>
public sealed class LeaderboardPanelUI : MonoBehaviour
{
    private const int TopCount = 15;

    [SerializeField, HideInInspector] private int authoredLayoutVersion;

    [Header("Authored UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Dropdown contractDropdown;
    [SerializeField] private Toggle efficientToggle;
    [SerializeField] private Toggle strongestToggle;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text personalBestText;
    [SerializeField] private ScrollRect leaderboardScroll;
    [SerializeField] private LeaderboardRowUI[] rows = new LeaderboardRowUI[TopCount];

    [Header("Contract Catalog")]
    [Tooltip("Populated from ContractSO assets by the one-time scene authoring tool. New contracts can be added here without changing the UI.")]
    [SerializeField] private List<ContractSO> contracts = new List<ContractSO>();

    private readonly List<ContractSO> displayedContracts = new List<ContractSO>();
    private int requestGeneration;
    private Coroutine dropdownReadyCoroutine;

    private void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (contractDropdown != null) contractDropdown.onValueChanged.AddListener(OnSelectionChanged);
        if (efficientToggle != null) efficientToggle.onValueChanged.AddListener(OnRankingChanged);
        if (strongestToggle != null) strongestToggle.onValueChanged.AddListener(OnRankingChanged);
        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        ++requestGeneration;
        if (openButton != null) openButton.onClick.RemoveListener(Open);
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (contractDropdown != null) contractDropdown.onValueChanged.RemoveListener(OnSelectionChanged);
        if (efficientToggle != null) efficientToggle.onValueChanged.RemoveListener(OnRankingChanged);
        if (strongestToggle != null) strongestToggle.onValueChanged.RemoveListener(OnRankingChanged);
    }

    public void Open()
    {
        if (panel == null) return;
        // TMP_Dropdown initializes its popup tween in Start, one frame after an
        // inactive modal first becomes active. A same-frame tap otherwise throws.
        if (contractDropdown != null) contractDropdown.interactable = false;
        panel.SetActive(true);
        PopulateContracts();
        Refresh();
        if (dropdownReadyCoroutine != null) StopCoroutine(dropdownReadyCoroutine);
        dropdownReadyCoroutine = StartCoroutine(EnableDropdownAfterStart());
    }

    public void Close()
    {
        ++requestGeneration;
        if (dropdownReadyCoroutine != null)
        {
            StopCoroutine(dropdownReadyCoroutine);
            dropdownReadyCoroutine = null;
        }
        if (panel != null) panel.SetActive(false);
    }

    private IEnumerator EnableDropdownAfterStart()
    {
        yield return null;
        if (panel != null && panel.activeSelf && contractDropdown != null)
            contractDropdown.interactable = true;
        dropdownReadyCoroutine = null;
    }

    private void PopulateContracts()
    {
        if (contractDropdown == null) return;
        string selectedId = SelectedContract != null ? SelectedContract.ContractID : null;
        displayedContracts.Clear();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        AddContracts(contracts, seen);
        if (PlayerDataManager.Instance != null)
            AddContracts(PlayerDataManager.Instance.allGameContracts, seen);

        displayedContracts.Sort((a, b) => string.Compare(a.name, b.name,
            StringComparison.OrdinalIgnoreCase));
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>(
            displayedContracts.Count);
        int selection = 0;
        for (int i = 0; i < displayedContracts.Count; i++)
        {
            ContractSO contract = displayedContracts[i];
            options.Add(new TMP_Dropdown.OptionData(contract.name));
            if (contract.ContractID == selectedId) selection = i;
        }
        contractDropdown.ClearOptions();
        contractDropdown.AddOptions(options);
        contractDropdown.SetValueWithoutNotify(selection);
        contractDropdown.RefreshShownValue();
    }

    private void AddContracts(IEnumerable<ContractSO> source, HashSet<string> seen)
    {
        if (source == null) return;
        foreach (ContractSO contract in source)
        {
            if (contract == null || contract.hideFromLeaderboard ||
                string.IsNullOrWhiteSpace(contract.ContractID) ||
                !seen.Add(contract.ContractID)) continue;
            displayedContracts.Add(contract);
        }
    }

    private ContractSO SelectedContract => contractDropdown != null &&
        contractDropdown.value >= 0 && contractDropdown.value < displayedContracts.Count
            ? displayedContracts[contractDropdown.value] : null;

    private bool IsStrongest => strongestToggle != null && strongestToggle.isOn;

    private void OnSelectionChanged(int _) => Refresh();

    private void OnRankingChanged(bool selected)
    {
        if (selected) Refresh();
    }

    private void Refresh()
    {
        int generation = ++requestGeneration;
        ClearRows();
        ContractSO contract = SelectedContract;
        bool strongest = IsStrongest;
        if (descriptionText != null)
            descriptionText.text = strongest
                ? "Lowest peak stress among successful bridges"
                : "Lowest cost among successful bridges";

        if (contract == null)
        {
            SetStatus("No contracts available.");
            if (personalBestText != null) personalBestText.text = string.Empty;
            return;
        }

        ShowPersonalBest(contract, strongest);
        if (!PlayFabClientAPI.IsClientLoggedIn())
        {
            SetStatus("Sign in to see the top 15 builders.");
            return;
        }

        string statistic = LeaderboardScoreCodec.StatisticName(contract.ContractID, strongest);
        SetStatus("Loading top 15...");
        PlayFabClientAPI.GetLeaderboard(new GetLeaderboardRequest {
            StatisticName = statistic,
            StartPosition = 0,
            MaxResultsCount = TopCount
        }, result =>
        {
            if (this == null || generation != requestGeneration || panel == null || !panel.activeSelf)
                return;

            int shown = 0;
            if (result.Leaderboard != null)
            {
                foreach (PlayerLeaderboardEntry entry in result.Leaderboard)
                {
                    if (shown >= rows.Length || shown >= TopCount) break;
                    if (!LeaderboardScoreCodec.TryDecode(entry.StatValue, strongest,
                        out int cost, out float stress)) continue;
                    LeaderboardRowUI row = rows[shown++];
                    if (row == null) continue;
                    row.gameObject.SetActive(true);
                    row.Bind(entry.Position + 1, entry.DisplayName, cost, stress);
                }
            }
            SetStatus(shown == 0 ? "No ranked bridges yet for this contract." : string.Empty);
            if (leaderboardScroll != null) leaderboardScroll.verticalNormalizedPosition = 1f;
        }, error =>
        {
            if (this == null || generation != requestGeneration || panel == null || !panel.activeSelf)
                return;
            if (error.Error == PlayFabErrorCode.StatisticNotFound)
            {
                SetStatus("No ranked bridges yet for this contract.");
                return;
            }
            Debug.LogWarning("[Leaderboards] Could not load top 15: " + error.ErrorMessage, this);
            SetStatus("Rankings unavailable. Please try again later.");
        });
    }

    private void ShowPersonalBest(ContractSO contract, bool strongest)
    {
        if (personalBestText == null) return;
        ContractLeaderboardData record = PlayerDataManager.Instance != null
            ? PlayerDataManager.Instance.GetLeaderboardRecord(contract.ContractID) : null;
        LeaderboardRunData run = strongest ? record?.strongest : record?.mostEfficient;
        personalBestText.text = run == null
            ? "YOUR BEST  —  No successful saved bridge yet"
            : $"YOUR BEST  ₱{Mathf.RoundToInt(run.cost):N0}  •  {run.peakStress:0.0}% peak stress";
    }

    private void ClearRows()
    {
        if (rows == null) return;
        foreach (LeaderboardRowUI row in rows)
            if (row != null) row.gameObject.SetActive(false);
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }
}
