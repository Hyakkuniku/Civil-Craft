using TMPro;
using UnityEngine;

/// <summary>Fits scene-authored menu art. Never opens panels or handles gameplay/account state.</summary>
[DisallowMultipleComponent]
public sealed class MainMenuPresentationLayout : MonoBehaviour
{
    [HideInInspector] public int authoredVersion;
    [Tooltip("Use the original centered title and plain Press To Play, without the paper board.")]
    public bool centeredIntro;
    public RectTransform canvasRect, splashBoard, splashSurface, title, edition, tagline;
    public RectTransform playButton, playLabel, footer, bridgeMotif, tapeLeft, tapeRight;
    public RectTransform dropdownPanel, dropdownSurface, dropdownHeading, dropdownSubtitle;
    public RectTransform modeButton, achievementButton, storeButton, achievementLocked, storeLocked;
    public RectTransform settingsButton, loginButton, creditsButton, backButton, quitButton, versionLabel;

    private Rect lastCanvasRect, lastSafeArea;
    private Vector2Int lastScreen;
    private bool applied;

    private void OnEnable() { applied = false; ApplyLayout(); }
    private void LateUpdate()
    {
        if (canvasRect == null) return;
        if (!applied || lastCanvasRect != canvasRect.rect || lastSafeArea != Screen.safeArea ||
            lastScreen.x != Screen.width || lastScreen.y != Screen.height) ApplyLayout();
    }

    public void ApplyLayout()
    {
        if (canvasRect == null) return;
        ApplyLayoutForPreview(Available(canvasRect));
        lastCanvasRect = canvasRect.rect;
        lastSafeArea = Screen.safeArea;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        applied = true;
    }

    // Editor previews supply their own safe rectangle instead of reading the
    // operator's physical monitor/Game view. No screen or player settings change.
    public void ApplyLayoutForPreview(Rect available)
    {
        if (available.width < 320f || available.height < 320f) return;
        MainMenuLayoutFrame frame = MainMenuLayoutPolicy.Build(available.width, available.height);
        Vector2 center = available.center - (canvasRect != null ? canvasRect.rect.center : Vector2.zero);
        if (centeredIntro)
        {
            Place(title, frame.Title, center);
            Place(edition, frame.Edition, center);
            Place(playButton, frame.Play, center);
            Stretch(playLabel, .04f, .04f, .96f, .96f);
            Font(title, 100f * frame.IntroUnit, 74f * frame.IntroUnit, TextAlignmentOptions.Center);
            Font(edition, 54f * frame.IntroUnit, 36f * frame.IntroUnit, TextAlignmentOptions.Center);
            Font(playLabel, 60f * frame.IntroUnit, 42f * frame.IntroUnit, TextAlignmentOptions.Center);
        }
        else
        {
        Place(splashBoard, frame.Splash, center);
        Stretch(splashSurface, .008f, .008f, .992f, .992f);
        Stretch(edition, .08f, .87f, .92f, .94f);
        Stretch(bridgeMotif, .18f, .69f, .82f, .845f);
        Stretch(title, .07f, .365f, .93f, .69f);
        Stretch(tagline, .08f, .245f, .92f, .35f);
        Stretch(playButton, .08f, .08f, .92f, .215f);
        Stretch(playLabel, .05f, .08f, .95f, .92f);
        Stretch(footer, .08f, .015f, .92f, .067f);
        Font(title, 100f * frame.SplashUnit, 64f * frame.SplashUnit, TextAlignmentOptions.Center);
        Font(edition, 26f * frame.SplashUnit, 20f * frame.SplashUnit, TextAlignmentOptions.Center);
        Font(tagline, 29f * frame.SplashUnit, 24f * frame.SplashUnit, TextAlignmentOptions.Center);
        Font(playLabel, 42f * frame.SplashUnit, 32f * frame.SplashUnit, TextAlignmentOptions.Center);
        Font(footer, 20f * frame.SplashUnit, 16f * frame.SplashUnit, TextAlignmentOptions.Center);
        Tape(tapeLeft, false, frame.SplashUnit);
        Tape(tapeRight, true, frame.SplashUnit);
        FitBridgeMotif(bridgeMotif);
        }

        if (dropdownPanel != null)
        {
            // MainMenuUIController owns its slide position and button entrance
            // scales. Only size changes here, never the animated position/scale.
            dropdownPanel.sizeDelta = new Vector2(frame.Dropdown.Width, frame.Dropdown.Height);
            Stretch(dropdownSurface, .006f, .009f, .994f, .991f);
            Stretch(dropdownHeading, .05f, .86f, .95f, .965f);
            Stretch(dropdownSubtitle, .05f, .805f, .95f, .86f);
            Font(dropdownHeading, 48f * frame.MenuUnit, 36f * frame.MenuUnit, TextAlignmentOptions.Center);
            Font(dropdownSubtitle, 23f * frame.MenuUnit, 19f * frame.MenuUnit, TextAlignmentOptions.Center);
            Tile(modeButton, .045f, .27f, .52f, .78f, 46f, frame.MenuUnit);
            Tile(achievementButton, .54f, .535f, .955f, .78f, 36f, frame.MenuUnit);
            Tile(achievementLocked, .54f, .535f, .955f, .78f, 36f, frame.MenuUnit);
            Tile(storeButton, .54f, .27f, .955f, .515f, 34f, frame.MenuUnit);
            Tile(storeLocked, .54f, .27f, .955f, .515f, 34f, frame.MenuUnit);
            Tile(settingsButton, .045f, .055f, .27f, .23f, 28f, frame.MenuUnit);
            Tile(loginButton, .29f, .055f, .515f, .23f, 28f, frame.MenuUnit);
            Tile(creditsButton, .535f, .055f, .735f, .23f, 28f, frame.MenuUnit);
            Tile(backButton, .755f, .055f, .955f, .23f, 28f, frame.MenuUnit);
            FitBridgeMotif(modeButton != null ? modeButton.Find("MainMenuModeBridgeMotif") as RectTransform : null);
        }
        Place(quitButton, frame.Quit, center);
        Place(versionLabel, frame.Version, center);
        if (quitButton != null)
            Font(quitButton.GetComponentInChildren<TMP_Text>(true)?.rectTransform,
                28f * frame.IntroUnit, 22f * frame.IntroUnit, TextAlignmentOptions.Center);
        Font(versionLabel, 25f * frame.IntroUnit, 20f * frame.IntroUnit, TextAlignmentOptions.MidlineLeft);
    }

    private static Rect Available(RectTransform parent)
    {
        Rect area = parent.rect;
        Canvas canvas = parent.GetComponentInParent<Canvas>(true);
        if (!Application.isPlaying || canvas == null || Screen.width <= 0 || Screen.height <= 0) return area;
        canvas = canvas.rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.min, camera, out Vector2 min) &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Screen.safeArea.max, camera, out Vector2 max))
        {
            min = Vector2.Max(min, area.min); max = Vector2.Min(max, area.max);
            // The existing slide controller targets parent center. Fit a safe
            // centered rectangle so a notch cannot clip that animated board.
            Vector2 center = area.center;
            float halfWidth = Mathf.Min(center.x - min.x, max.x - center.x);
            float halfHeight = Mathf.Min(center.y - min.y, max.y - center.y);
            if (halfWidth >= 160f && halfHeight >= 160f)
                area = new Rect(center.x - halfWidth, center.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
        }
        return area;
    }

    private static void Tile(RectTransform rect, float left, float bottom, float right, float top, float size, float unit)
    {
        if (rect == null) return;
        Stretch(rect, left, bottom, right, top);
        foreach (TMP_Text label in rect.GetComponentsInChildren<TMP_Text>(true))
        {
            bool detail = label.name == "Tile Caption" || label.name == "Unlock Hint" ||
                label.name == "MainMenuModeHint" || label.name == "MainMenuLockedNote";
            bool kicker = label.name == "Tile Kicker";
            float max = (kicker ? 19f : detail ? (size > 40f ? 26f : 22f) : size) * unit;
            Font(label.rectTransform, max, max * .78f, TextAlignmentOptions.Center);
        }
    }

    private static void Font(RectTransform rect, float max, float min, TextAlignmentOptions alignment)
    {
        if (rect == null) return;
        TMP_Text text = rect.GetComponent<TMP_Text>();
        if (text == null) return;
        text.fontSize = max; text.enableAutoSizing = true;
        text.fontSizeMax = max; text.fontSizeMin = min;
        text.alignment = alignment; text.raycastTarget = false;
    }

    private static void FitBridgeMotif(RectTransform parent)
    {
        if (parent == null) return;
        Vector2[] points = { new Vector2(.05f, .15f), new Vector2(.23f, .66f),
            new Vector2(.5f, .87f), new Vector2(.77f, .66f), new Vector2(.95f, .15f) };
        for (int i = 0; i < 4; i++)
        {
            Line(parent, "TrussUpper" + i, points[i], points[i + 1]);
            Line(parent, "TrussDiagonal" + i, points[i], new Vector2(points[i + 1].x, .15f));
        }
        Line(parent, "BridgeDeck", points[0], points[4]);
        for (int i = 1; i < 4; i++) Line(parent, "TrussUpright" + i, points[i], new Vector2(points[i].x, .15f));
    }

    private static void Line(RectTransform parent, string name, Vector2 from, Vector2 to)
    {
        RectTransform rect = parent.Find(name) as RectTransform;
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = (from + to) * .5f;
        rect.pivot = Vector2.one * .5f; rect.anchoredPosition = Vector2.zero;
        Vector2 delta = Vector2.Scale(to - from, parent.rect.size);
        rect.sizeDelta = new Vector2(delta.magnitude, Mathf.Max(2f, parent.rect.width / 80f));
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private static void Tape(RectTransform rect, bool right, float unit)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = new Vector2(right ? 1f : 0f, 1f);
        rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = new Vector2(132f, 38f) * unit;
        rect.anchoredPosition = new Vector2(right ? -60f : 60f, -8f) * unit;
        rect.localRotation = Quaternion.Euler(0f, 0f, right ? -12f : 12f);
    }

    private static void Place(RectTransform rect, MainMenuLayoutBox box, Vector2 center)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = center + new Vector2(box.X, box.Y);
        rect.sizeDelta = new Vector2(box.Width, box.Height);
    }

    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
        rect.pivot = Vector2.one * .5f;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
