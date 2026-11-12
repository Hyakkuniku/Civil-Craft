using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Read-only descriptions shared by contract, inspection and simulation UI.</summary>
public static class ContractLiveLoadPresentation
{
    public static string ShortSummary(ContractSO contract)
    {
        if (contract == null) return string.Empty;
        if (!contract.HasMultipleLiveLoads) return contract.liveLoadWeight.ToString("N0") + " kg";
        return (contract.liveLoadTests != null ? contract.liveLoadTests.Count : 0) + " vehicles · " +
            (contract.vehicleTestMode == ContractSO.VehicleTestMode.Sequential ? "Sequential" : "Together");
    }

    public static string Requirements(ContractSO contract)
    {
        if (contract == null || !contract.HasMultipleLiveLoads) return string.Empty;
        var text = new StringBuilder();
        text.Append("<b>LIVE LOAD TESTS — ").Append(contract.vehicleTestMode == ContractSO.VehicleTestMode.Sequential ? "SEQUENTIAL" : "TOGETHER").Append("</b>\n");
        if (contract.liveLoadTests == null) return text.ToString();
        for (int index = 0; index < contract.liveLoadTests.Count; index++)
        {
            ContractLiveLoadEntry entry = contract.liveLoadTests[index];
            if (entry == null) continue;
            if (index > 0) text.Append('\n');
            text.Append(index + 1).Append(". ").Append(Escape(Label(entry.displayName, index))).Append(" — ")
                .Append(entry.emptyWeightKg.ToString("N0")).Append(" kg empty");
            if (entry.allowCargo)
            {
                text.Append(" + loaded cargo");
                if (entry.minimumLoadedCargo > 0) text.Append(" (at least ").Append(entry.minimumLoadedCargo).Append(" items)");
            }
            if (contract.vehicleTestMode == ContractSO.VehicleTestMode.Together)
                text.Append("\nStarts after ").Append(entry.departureDelay.ToString("0.##")).Append(" s");
        }
        text.Append("\n\nEvery vehicle must finish. ");
        text.Append(contract.vehicleTestMode == ContractSO.VehicleTestMode.Sequential
            ? "The bridge keeps its condition between vehicles."
            : "Departure times use simulation time, after settling.");
        return text.ToString();
    }

    public static string SessionStatus(LiveLoadTestSession session)
    {
        if (session == null) return string.Empty;
        int passed = 0, focus = -1;
        var participants = session.Participants;
        for (int index = 0; index < participants.Count; index++)
        {
            var participant = participants[index];
            if (participant.State == LiveLoadTestParticipantState.Passed) passed++;
            if (participant.Definition.Vehicle == session.FocusVehicle) focus = index;
        }
        if (session.Failed) return "LIVE LOAD TEST FAILED\n" + Escape(session.FailureReason);
        if (session.AllPassed) return "ALL LIVE LOADS PASSED · " + passed + "/" + participants.Count;
        if (focus < 0) return "LIVE LOADS · " + passed + "/" + participants.Count + " passed · Waiting to depart";
        return "Vehicle " + (focus + 1) + "/" + participants.Count + " — " +
            Escape(participants[focus].Definition.DisplayName) + "\n" + passed + "/" + participants.Count + " passed";
    }

    public static string ResultSummary(LiveLoadTestSession session)
    {
        if (session == null) return string.Empty;
        var text = new StringBuilder("<b>LIVE LOAD RESULTS</b>");
        foreach (var participant in session.Participants)
        {
            string state = participant.State == LiveLoadTestParticipantState.Passed ? "PASSED" :
                participant.State == LiveLoadTestParticipantState.Failed ? "FAILED" : "NOT COMPLETED";
            text.Append('\n').Append(Escape(participant.Definition.DisplayName)).Append(" · ")
                .Append(participant.Definition.WeightKg.ToString("N0")).Append(" kg · ").Append(state);
        }
        return text.ToString();
    }

    /// <summary>Adds one measured row to the receipt's existing vertical scroll layout.</summary>
    public static void AppendReceiptResults(Transform receiptContent, TMP_FontAsset font, LiveLoadTestSession session)
    {
        if (session == null) return;
        AppendReceiptResults(receiptContent, font, ResultSummary(session));
    }

    public static void AppendReceiptResults(Transform receiptContent, TMP_FontAsset font, string resultText)
    {
        if (receiptContent == null || string.IsNullOrWhiteSpace(resultText)) return;
        const string rowName = "Multiple Live Load Results";
        Transform existing = receiptContent.Find(rowName);
        RectTransform row = existing != null ? existing as RectTransform :
            new GameObject(rowName, typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement), typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
        row.SetParent(receiptContent, false);
        TextMeshProUGUI label = row.GetComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = 22f; label.enableAutoSizing = false;
        label.color = new Color32(73, 48, 29, 255); label.alignment = TextAlignmentOptions.TopLeft;
        label.enableWordWrapping = true; label.overflowMode = TextOverflowModes.Overflow;
        label.margin = new Vector4(12f, 12f, 12f, 12f); label.raycastTarget = false;
        label.text = resultText;
        RectTransform content = receiptContent as RectTransform;
        float width = content != null && content.rect.width > 80f ? content.rect.width : 480f;
        float height = label.GetPreferredValues(label.text, width, 0f).y + 26f;
        LayoutElement element = row.GetComponent<LayoutElement>(); element.minHeight = height; element.preferredHeight = height;
        element.flexibleWidth = 1f; row.sizeDelta = new Vector2(width, height);
        row.SetAsLastSibling();
    }

    public static float PreviewWeight(BuildLocation location, ContractSO contract)
    {
        if (contract == null) return 0f;
        if (!contract.HasMultipleLiveLoads) return LiveLoadVehicle.GetContractTestWeight(contract);
        float weight = 0f;
        if (contract.liveLoadTests == null) return weight;
        foreach (ContractLiveLoadEntry entry in contract.liveLoadTests)
        {
            if (entry == null) continue;
            float value = entry.emptyWeightKg;
            if (location != null && location.liveLoadVehicles != null)
                foreach (ContractLiveLoadBinding binding in location.liveLoadVehicles)
                    if (binding != null && binding.entryId == entry.entryId && binding.vehicle != null)
                    { value = binding.vehicle.TotalTestWeight; break; }
            weight = contract.vehicleTestMode == ContractSO.VehicleTestMode.Sequential ? Mathf.Max(weight, value) : weight + value;
        }
        return weight;
    }

    public static string Label(string value, int index) => string.IsNullOrWhiteSpace(value) ? "Vehicle " + (index + 1) : value.Trim();
    private static string Escape(string value) => string.IsNullOrEmpty(value) ? string.Empty : value.Replace("<", "‹").Replace(">", "›");

    /// <summary>Uses the existing project-brief slot as a touch-scrollable requirements page.</summary>
    public static void ShowScrollableBrief(TextMeshProUGUI source, ContractSO contract, string brief)
    {
        bool multiple = contract != null && contract.HasMultipleLiveLoads;
        string environment = EnvironmentalRequirements(contract);
        bool scroll = multiple || environment.Length > 0;
        string text = scroll ? "<size=18><color=#765A43>PROJECT BRIEF · DRAG TO SCROLL</color></size>\n" + brief : string.Empty;
        if (environment.Length > 0) text += "\n\n" + environment;
        if (multiple) text += "\n\n" + Requirements(contract);
        ShowScrollableText(source, scroll, text);
    }

    private static string EnvironmentalRequirements(ContractSO contract)
    {
        BridgeEnvironmentalTest test = contract != null && contract.liveLoadMode == ContractSO.LiveLoadMode.Vehicle
            ? contract.environmentalTest : null;
        if (test == null || !test.IsEnabled) return string.Empty;
        test = test.Freeze();
        string conditions = test.HasWind && test.HasEarthquake ? "Wind gusts and earthquake shaking" :
            test.HasWind ? "Wind gusts" : "Earthquake shaking";
        return $"<b>SOLO SIMULATION CONDITIONS</b>\n{conditions} start {test.startDelay:0.#} s after settling and last {test.duration:0.#} s.";
    }

    /// <summary>Touch-scrolls a frozen multiplayer roster in its existing authored text slot.</summary>
    public static void ShowScrollableText(TextMeshProUGUI source, bool enabled, string text)
    {
        if (source == null) return;
        Transform existing = source.transform.parent.Find("Multi Live Load Requirements");
        source.gameObject.SetActive(!enabled);
        if (!enabled) { if (existing != null) existing.gameObject.SetActive(false); return; }
        RectTransform root;
        TextMeshProUGUI label;
        ScrollRect scroll;
        if (existing == null)
        {
            root = new GameObject("Multi Live Load Requirements", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect)).GetComponent<RectTransform>();
            root.SetParent(source.transform.parent, false);
            Image background = root.GetComponent<Image>(); background.color = Color.clear; background.raycastTarget = true;
            RectTransform content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(root, false);
            label = content.gameObject.AddComponent<TextMeshProUGUI>(); label.raycastTarget = false;
            scroll = root.GetComponent<ScrollRect>(); scroll.viewport = root; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }
        else
        {
            root = (RectTransform)existing; scroll = root.GetComponent<ScrollRect>(); label = scroll.content.GetComponent<TextMeshProUGUI>();
            if (root.gameObject.activeSelf && label.text == text) return;
        }
        root.anchorMin = source.rectTransform.anchorMin; root.anchorMax = source.rectTransform.anchorMax;
        root.pivot = source.rectTransform.pivot; root.anchoredPosition = source.rectTransform.anchoredPosition;
        root.sizeDelta = source.rectTransform.sizeDelta; root.localScale = Vector3.one;
        label.font = source.font; label.fontSize = source.fontSize; label.color = source.color;
        label.fontStyle = FontStyles.Normal; label.enableWordWrapping = true; label.enableAutoSizing = false;
        label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft;
        label.margin = new Vector4(5f, 8f, 12f, 14f);
        bool changed = label.text != text || !root.gameObject.activeSelf;
        label.text = text;
        RectTransform contentRect = label.rectTransform;
        contentRect.anchorMin = new Vector2(0f, 1f); contentRect.anchorMax = Vector2.one; contentRect.pivot = new Vector2(.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, Mathf.Max(root.rect.height, label.GetPreferredValues(label.text, root.rect.width, 0f).y + 24f));
        root.gameObject.SetActive(true);
        if (changed) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
    }
}
