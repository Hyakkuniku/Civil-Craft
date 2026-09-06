using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Scene-owned UI; the schedule and save survive independently of this view.</summary>
public sealed class DailyRewardsUI : MonoBehaviour
{
    private DailyRewardSchedule schedule;
    private RectTransform safeRoot;
    private GameObject panel;
    private Button launcher, claim;
    private TMP_Text status, launcherText;
    private readonly List<TMP_Text> cards = new List<TMP_Text>();
    private float nextRefresh;
    private InputManager input;
    private PlayerMotor motor;
    private bool oldInput, oldLook, oldMotor, ownsInput;
    private static readonly Color Cream = new Color32(248, 237, 207, 255);
    private static readonly Color Wood = new Color32(76, 50, 31, 255);
    private static readonly Color Gold = new Color32(233, 161, 42, 255);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var config = Resources.Load<DailyRewardSchedule>("DailyRewardSchedule");
        if (config == null || config.gameplayScenes == null || Array.IndexOf(config.gameplayScenes, scene.name) < 0) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.GetComponentInChildren<DailyRewardsUI>(true) != null) return;
        var owner = new GameObject("DailyRewards", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(owner, scene);
        owner.AddComponent<DailyRewardsUI>().Build(config);
    }

    private void Build(DailyRewardSchedule config)
    {
        schedule = config;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        safeRoot = Rect("SafeArea", transform, Vector2.zero, Vector2.one);
        launcher = Button("Daily gifts", safeRoot, new Vector2(.41f,.89f), new Vector2(.59f,.98f), Open);
        launcherText = launcher.GetComponentInChildren<TMP_Text>();
        panel = Rect("DailyRewardsPanel", safeRoot, Vector2.zero, Vector2.one).gameObject;
        panel.AddComponent<Image>().color = new Color(0.12f, .07f, .03f, .92f);
        var body = Rect("CreamPanel", panel.transform, new Vector2(.06f,.07f), new Vector2(.94f,.93f));
        body.gameObject.AddComponent<Image>().color = Cream;
        Label("DAILY BUILDER GIFTS", body, new Vector2(.04f,.83f), new Vector2(.84f,.98f), 34);
        Button("X", body, new Vector2(.88f,.84f), new Vector2(.98f,.97f), Close);
        string policy = config.resetAfterMissedDay ? "Miss a day? Start again at Day 1." : "Missed days keep your progress.";
        Label("A new gift at 00:00 UTC. " + policy, body, new Vector2(.04f,.71f), new Vector2(.96f,.84f), 23);
        var viewport = Rect("Viewport", body, new Vector2(.04f,.25f), new Vector2(.96f,.70f));
        viewport.gameObject.AddComponent<Image>().color = new Color32(216,195,162,255);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("GiftTrack", viewport, Vector2.zero, Vector2.one);
        content.anchorMax = new Vector2(0,1);
        content.pivot = new Vector2(0, .5f);
        int count = config.rewards == null ? 0 : config.rewards.Count;
        content.sizeDelta = new Vector2(count * 164, 0);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = true; scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        for (int i = 0; i < count; i++)
        {
            var card = Rect("Day" + (i + 1), content, new Vector2(0,.04f), new Vector2(0,.96f));
            card.pivot = new Vector2(0,.5f);
            card.anchoredPosition = new Vector2(i * 164 + 6, 0);
            card.sizeDelta = new Vector2(152,0);
            card.gameObject.AddComponent<Image>().color = Cream;
            cards.Add(Label("", card, new Vector2(.05f,.06f), new Vector2(.95f,.94f), 23));
        }
        status = Label("", body, new Vector2(.04f,.04f), new Vector2(.65f,.22f), 23);
        claim = Button("CLAIM GIFT", body, new Vector2(.68f,.06f), new Vector2(.96f,.21f), Claim);
        panel.SetActive(false);
        Refresh();
    }

    private bool Allowed()
    {
        var data = PlayerDataManager.Instance;
        if (input == null) input = FindObjectOfType<InputManager>();
        return data != null && data.CurrentData != null &&
            input != null && (ownsInput || (input.IsPlayerInputEnabled && input.IsLookInputEnabled)) &&
            (string.IsNullOrWhiteSpace(schedule.requiredFeatureId) || data.IsFeatureUnlocked(schedule.requiredFeatureId)) &&
            (GameManager.Instance == null || GameManager.Instance.CurrentState != GameManager.GameState.Building);
    }

    private bool OtherModal() =>
        (UIPanelCoordinator.Instance != null && UIPanelCoordinator.Instance.HasOpenPanel && !UIPanelCoordinator.Instance.IsOpen(panel)) ||
        (ItemUnlockUI.Instance != null && ItemUnlockUI.Instance.popupPanel != null && ItemUnlockUI.Instance.popupPanel.activeInHierarchy) || Time.timeScale == 0;

    private void Update()
    {
        if (schedule == null) return;
        if (ownsInput && !panel.activeSelf) RestoreInput();
        if (panel.activeSelf && !Allowed()) Close();
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .5f; Refresh(); }
    }

    private void Refresh()
    {
        if (Screen.width > 0 && Screen.height > 0)
        {
            Rect safe = Screen.safeArea;
            safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        }
        bool allowed = Allowed();
        launcher.gameObject.SetActive(allowed && !panel.activeSelf && !OtherModal());
        var data = PlayerDataManager.Instance;
        string reason = "Player data is unavailable.";
        int index = 0;
        bool ready = allowed && schedule.TryGetReward(data.CurrentData.dailyRewards, DateTime.UtcNow, out index, out reason);
        launcherText.text = ready ? "Daily gifts  !" : "Daily gifts";
        claim.interactable = ready;
        status.text = ready ? "Day " + (index + 1) + " is ready! Swipe to explore gifts." : reason;
        int completed = ready ? index : (int)Math.Min(cards.Count, Math.Max(0, data == null || data.CurrentData == null || data.CurrentData.dailyRewards == null ? 0 : data.CurrentData.dailyRewards.claims));
        for (int i = 0; i < cards.Count; i++)
        {
            var entry = schedule.rewards[i];
            cards[i].text = "DAY " + (i + 1) + "\n\n" + (entry == null ? "Not configured" : entry.title + "\n" + entry.GetSummary(data != null && data.IsCosmeticUnlocked(entry.cosmeticId))) +
                (ready && i == index ? "\nREADY" : i < completed ? "\nCLAIMED" : "\nUPCOMING");
            cards[i].transform.parent.GetComponent<Image>().color = ready && i == index ? Gold : Cream;
        }
    }

    public void Open()
    {
        if (!Allowed() || OtherModal() || panel.activeSelf) return;
        input = FindObjectOfType<InputManager>();
        motor = FindObjectOfType<PlayerMotor>();
        if (input != null)
        {
            oldInput = input.IsPlayerInputEnabled; oldLook = input.IsLookInputEnabled;
            input.SetPlayerInputEnable(false); input.SetLookEnabled(false);
        }
        if (motor != null) { oldMotor = motor.enabled; motor.enabled = false; }
        ownsInput = true;
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.OpenPanel(panel);
        else panel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (UIPanelCoordinator.Instance != null) UIPanelCoordinator.Instance.ClosePanel(panel);
        else if (panel != null) panel.SetActive(false);
        RestoreInput();
    }

    private void RestoreInput()
    {
        if (!ownsInput) return;
        ownsInput = false;
        bool building = GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Building;
        if (input != null) { input.SetPlayerInputEnable(oldInput && !building); input.SetLookEnabled(oldLook && !building); }
        if (motor != null) motor.enabled = oldMotor && !building;
    }

    private void OnDestroy() { RestoreInput(); }

    private void Claim()
    {
        if (!Allowed()) return;
        claim.interactable = false;
        var ownedBefore = new HashSet<string>(PlayerDataManager.Instance.CurrentData.unlockedCosmeticIDs ?? new List<string>());
        if (!PlayerDataManager.Instance.TryClaimDailyReward(schedule, out DailyRewardEntry reward, out string error))
        {
            Refresh(); status.text = error; nextRefresh = Time.unscaledTime + 4; return;
        }
        // Ownership is already saved. Do not pass a hat ID and grant it a second time.
        string summary = reward.GetSummary(ownedBefore.Contains((reward.cosmeticId ?? "").Trim()));
        if (ItemUnlockUI.Instance != null)
        {
            Close();
            ItemUnlockUI.Instance.ShowReward("Daily gift: " + reward.title + "\n" + summary, reward.icon, "", null);
        }
        else
        {
            Refresh(); status.text = "Gift saved: " + summary;
            nextRefresh = Time.unscaledTime + 5;
        }
    }

    private RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        return rect;
    }
    private TMP_Text Label(string text, Transform parent, Vector2 min, Vector2 max, float size)
    {
        var label = Rect("Label", parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        if (schedule.font != null) label.font = schedule.font;
        label.text = text; label.color = Wood; label.fontSize = size;
        label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = size;
        label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        return label;
    }
    private Button Button(string text, Transform parent, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(text, parent, min, max);
        var image = rect.gameObject.AddComponent<Image>(); image.color = Gold;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.onClick.AddListener(action);
        Label(text, rect, new Vector2(.04f,.04f), new Vector2(.96f,.96f), 25);
        return button;
    }
}
