using TMPro;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private const string Short = "Select the <b><color=#E09500>Wood Beam</color></b>.";
    private const string Long = "Excellent! You've connected several <color=#E09500>Wood Beams</color> to form your first truss section. Remember what Professor Bhan explained earlier. The triangular arrangement helps the connected members maintain their structural shape. Now let's learn how to build more efficiently.";

    private static void Main()
    {
        Check(!TutorialCenterLayout.ApplyWithin(null, null, Vector2.zero, Vector2.zero, Vector2.zero, new Rect()), "Absent UI must safely decline layout.");
        foreach ((float width, float height) in new[]
        {
            (1920f, 1080f), (1920f, 900f), (1280f, 720f), (1600f, 900f),
            (2560f, 1080f), (3840f, 1080f), (2048f, 1536f), (1024f, 768f),
            (900f, 1600f), (720f, 1280f), (480f, 800f), (320f, 480f),
            (1920f, 337f), (600f, 300f), (800f, 400f)
        })
        foreach (bool low in new[] { false, true })
        foreach (bool notched in new[] { false, true })
        foreach (string message in new[] { "", Short, Long, string.Join("\n", Enumerable.Repeat(Long, 12)) })
            Verify(width, height, low, notched, message);

        var random = new Random(305193);
        for (int i = 0; i < 200; i++)
            Verify(random.Next(320, 4001), random.Next(300, 2201), i % 2 == 0, i % 3 == 0, i % 4 == 0 ? Short : Long,
                new Vector2((float)random.NextDouble(), (float)random.NextDouble()));
        TestGrowthAndReset();
        TestRejectedBindings();
        TestSafeAreaConversion();
        TestNestedText();
        SourceGuards();
        Console.WriteLine($"PASS: {checks} production centered-tutorial layout checks and integration guards. Geometry and TMP measurement boundaries only; real font and rendering checked separately in Unity. No scenes, accounts, saves, or network touched.");
    }

    private static void Verify(float width, float height, bool low, bool notched, string message, Vector2? customPivot = null)
    {
        var parent = new RectTransform(width, height);
        var panel = new RectTransform(1080, 140) { parent = parent, pivot = customPivot ?? new Vector2(.5f, .5f) };
        var text = new TextMeshProUGUI(panel, message);
        var font = text.font;
        object color = text.color;
        Vector2 min = panel.anchorMin, max = panel.anchorMax, pivot = panel.pivot;
        Rect safe = notched ? new Rect(parent.rect.xMin + width * .05f, parent.rect.yMin + height * .04f, width * .90f, height * .91f) : parent.rect;
        Vector2 authored = new(0, height * .3f), authoredSize = new(1080, 140), offset = new(0, low ? -520 : 0);
        bool fits = TutorialCenterLayout.ApplyWithin(panel, text, authored, authoredSize, offset, safe);
        string context = $"{width}x{height}, low={low}, notch={notched}, length={message.Length}";
        Rect actual = Bounds(panel);
        Check(Contains(safe, actual), "Paper panel stays within safe parent bounds: " + context);
        Check(float.IsFinite(actual.x) && float.IsFinite(actual.y) && float.IsFinite(actual.width) && float.IsFinite(actual.height) && actual.width > 0 && actual.height > 0,
            "Layout geometry is positive and finite: " + context);
        Check(panel.parent == parent && Same(min, panel.anchorMin) && Same(max, panel.anchorMax) && Same(pivot, panel.pivot),
            "Layout preserves parent, anchors, and pivot: " + context);
        Check(text.text == message && text.font == font && text.color == color && text.richText,
            "Message, inline emphasis, font, and color stay untouched: " + context);
        Check(text.fontSize >= 26 && text.fontSize <= 30, "Text size stays inside the readable bound: " + context);
        Check(text.enableWordWrapping && !text.enableAutoSizing, "Wrapping is measured instead of uncontrolled shrink-to-fit: " + context);
        Check(text.rectTransform.rect.width > 0 && text.rectTransform.rect.height > 0,
            "Text receives a positive content rectangle: " + context);
        float measuredHeight = text.GetPreferredValues(message, text.rectTransform.rect.width, float.PositiveInfinity).y;
        if (fits)
            Check(measuredHeight <= text.rectTransform.rect.height + .1f,
                "A successful fit contains the full measured message: " + context);
        else
            Check(text.overflowMode != TextOverflowModes.Overflow,
                "Extreme text capacity never leaks beyond its paper: " + context);
        if ((message == Short || message == Long) && width >= 900 && height >= 720)
            Check(fits, "Real short/long tutorial copy fits phone/tablet reference canvases without truncation: " + context);
        string before = Snapshot(panel, text);
        bool fitsAgain = TutorialCenterLayout.ApplyWithin(panel, text, authored, authoredSize, offset, safe);
        Check(fits == fitsAgain && before == Snapshot(panel, text), "Repeated layout does not drift: " + context);
    }

    private static void TestGrowthAndReset()
    {
        var parent = new RectTransform(1920, 1080);
        var panel = new RectTransform(1080, 140) { parent = parent };
        var text = new TextMeshProUGUI(panel, Short);
        Vector2 authoredPosition = new(0, 220), authoredSize = new(1080, 140);
        Check(TutorialCenterLayout.ApplyWithin(panel, text, authoredPosition, authoredSize, Vector2.zero, parent.rect), "Short message fits.");
        float shortHeight = panel.rect.height, shortTop = Bounds(panel).yMax;
        string shortSnapshot = Snapshot(panel, text);
        text.text = Long;
        Vector2 longPreferred = text.GetPreferredValues(Long, text.rectTransform.rect.width, float.PositiveInfinity);
        Check(longPreferred.x > text.rectTransform.rect.width,
            "Regression fixture exposes TMP's unwrapped preferred width for ordinary long tutorial copy.");
        Check(TutorialCenterLayout.ApplyWithin(panel, text, authoredPosition, authoredSize, Vector2.zero, parent.rect), "Long message fits.");
        Check(text.GetPreferredValues(Long, text.rectTransform.rect.width, float.PositiveInfinity).x > text.rectTransform.rect.width &&
              text.GetPreferredValues(Long, text.rectTransform.rect.width, float.PositiveInfinity).y <= text.rectTransform.rect.height,
            "A message with unwrapped preferred width beyond the paper still fits using its wrapped preferred height.");
        Check(panel.rect.height > shortHeight, "Long message grows its paper rather than overflowing.");
        Check(Math.Abs(Bounds(panel).yMax - shortTop) < .05f, "Paper grows down from the authored top edge.");
        text.text = Short;
        TutorialCenterLayout.ApplyWithin(panel, text, authoredPosition, authoredSize, Vector2.zero, parent.rect);
        Check(shortSnapshot == Snapshot(panel, text), "Returning to short copy restores the original short layout.");
    }

    private static void TestRejectedBindings()
    {
        var parent = new RectTransform(1920, 1080);
        var panel = new RectTransform(1080, 140) { parent = parent };
        var foreign = new RectTransform(1920, 1080);
        var text = new TextMeshProUGUI(foreign, Short);
        string before = Snapshot(panel, text);
        Check(!TutorialCenterLayout.ApplyWithin(panel, text, Vector2.zero, new Vector2(1080, 140), Vector2.zero, parent.rect), "Unrelated TMP labels cannot be restyled by the card.");
        Check(before == Snapshot(panel, text), "Rejected unrelated binding has no partial mutation.");
        text.rectTransform.parent = panel;
        Check(!TutorialCenterLayout.ApplyWithin(panel, null, Vector2.zero, new Vector2(1080, 140), Vector2.zero, parent.rect), "Missing text safely declines.");
        Check(!TutorialCenterLayout.ApplyWithin(panel, text, Vector2.zero, Vector2.zero, Vector2.zero, parent.rect), "Missing authored size safely declines.");
        Check(!TutorialCenterLayout.ApplyWithin(panel, text, Vector2.zero, new Vector2(1080, 140), Vector2.zero, new Rect(3000, 3000, 300, 300)), "Nonintersecting safe area safely declines.");
        Check(!TutorialCenterLayout.ApplyWithin(panel, text, Vector2.zero, new Vector2(1080, 140), Vector2.zero, new Rect(0, 0, 40, 40)), "Unusable small safe area safely declines.");
        panel.parent = null;
        Check(!TutorialCenterLayout.Apply(panel, text, Vector2.zero, new Vector2(1080, 140), Vector2.zero), "Missing rectangular parent safely declines.");
    }

    private static void TestSafeAreaConversion()
    {
        var parent = new RectTransform(1920, 1080) { ancestorCanvas = new Canvas { renderMode = RenderMode.ScreenSpaceOverlay } };
        var panel = new RectTransform(1080, 140) { parent = parent };
        var text = new TextMeshProUGUI(panel, Long);
        Screen.width = 1920; Screen.height = 1080;
        Screen.safeArea = new Rect(120, 80, 1700, 940);
        Check(TutorialCenterLayout.Apply(panel, text, new Vector2(950, -520), new Vector2(1080, 140), Vector2.zero), "Runtime safe-area conversion fits a measured message.");
        Check(Contains(new Rect(-840, -460, 1700, 940), Bounds(panel)), "Actual Apply path respects a notched screen safe rectangle.");
        Screen.safeArea = new Rect(0, 0, 0, 0);
        Check(TutorialCenterLayout.Apply(panel, text, Vector2.zero, new Vector2(1080, 140), Vector2.zero), "Unavailable safe-area dimensions fall back to screen bounds.");
        Check(Contains(parent.rect, Bounds(panel)), "Safe-area fallback remains in the parent.");
        Screen.safeArea = new Rect(0, 0, 1920, 1080);
    }

    private static void TestNestedText()
    {
        var parent = new RectTransform(1920, 1080);
        var panel = new RectTransform(1080, 140) { parent = parent, anchorMin = new Vector2(.2f, .7f), anchorMax = new Vector2(.8f, .7f), pivot = new Vector2(.2f, 1) };
        var paper = new RectTransform { parent = panel, anchorMin = Vector2.zero, anchorMax = Vector2.one, sizeDelta = Vector2.zero };
        var text = new TextMeshProUGUI(paper, Long);
        Check(TutorialCenterLayout.ApplyWithin(panel, text, Vector2.zero, new Vector2(1080, 140), Vector2.zero, parent.rect), "A stretched descendant paper supports the real text binding.");
        Check(Contains(parent.rect, Bounds(panel)), "Stretched anchors and top-aligned pivot stay inside the safe parent.");
        Check(text.rectTransform.parent == paper && paper.parent == panel, "Nested cream/text hierarchy is preserved.");
        Check(text.GetPreferredValues(text.text, text.rectTransform.rect.width, float.PositiveInfinity).y <= text.rectTransform.rect.height, "Nested text receives measured vertical room.");
    }

    private static Rect Bounds(RectTransform node)
    {
        Rect parent = ((RectTransform)node.parent).rect;
        float anchorX = parent.xMin + parent.width * Mathf.Lerp(node.anchorMin.x, node.anchorMax.x, node.pivot.x);
        float anchorY = parent.yMin + parent.height * Mathf.Lerp(node.anchorMin.y, node.anchorMax.y, node.pivot.y);
        return new Rect(anchorX + node.anchoredPosition.x - node.rect.width * node.pivot.x,
            anchorY + node.anchoredPosition.y - node.rect.height * node.pivot.y, node.rect.width, node.rect.height);
    }
    private static bool Same(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .1f && inner.xMax <= outer.xMax + .1f && inner.yMin >= outer.yMin - .1f && inner.yMax <= outer.yMax + .1f;
    private static string Snapshot(RectTransform panel, TextMeshProUGUI text) => panel.anchoredPosition + ";" + panel.rect.width + ";" + panel.rect.height + ";" + text.rectTransform.rect.width + ";" + text.rectTransform.rect.height + ";" + text.fontSize + ";" + text.overflowMode;
    private static void Check(bool valid, string message) { checks++; if (!valid) throw new InvalidOperationException(message); }

    private static void SourceGuards()
    {
        string root = FindRoot();
        string helper = File.ReadAllText(Path.Combine(root, "Assets/Script/UI/TutorialCenterLayout.cs"));
        foreach (string forbidden in new[] { "PlayerPrefs", "SaveGame", "PlayFab", "LoadScene", "UnityWebRequest", "AddListener", "RemoveListener", "OnStepStart", "NextStep", "CompleteTutorial", "SetParent", "new GameObject", "SetActive", "nextButton", "skipButton", "leftPanel" })
            Check(!helper.Contains(forbidden, StringComparison.Ordinal), "Layout never changes progression or unrelated UI: " + forbidden);
        Check(helper.Contains("GetPreferredValues"), "Layout measures actual preferred TMP dimensions.");
        Check(!helper.Contains("preferred.x <= usableWidth"),
            "Unwrapped TMP preferred width must not reject a successfully wrapped message.");
        Check(helper.Contains("Screen.safeArea") && helper.Contains("ScreenPointToLocalPointInRectangle"), "Runtime converts the device safe area into the parent coordinate system.");
        // Integration ordering is checked after the manager hook is added; do not use the UI helper as a replacement for the tutorial state machine.
        string manager = File.ReadAllText(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialManager.cs"));
        Check(manager.Contains("TutorialCenterLayout.Apply"), "TutorialManager calls the tested presentation helper.");
        Check(manager.Contains("step.OnStepStart?.Invoke()") && manager.Contains("trackedButton.onClick.AddListener(trackedButtonAction)"), "The existing authored callbacks and semantic button tracking remain connected.");
        Check(manager.Contains("case TutorialPosition.LowCenter:") && manager.Contains("return centerPanel;"), "Center and LowCenter retain the same authored panel.");
        Check(manager.Contains("nextButton.SetActive(step.showNextButton)"), "The existing Continue visibility is still governed by each authored step.");
        string show = Method(manager, "private void ShowTutorialStep(");
        int messageAssignment = show.IndexOf("activeText.text = TutorialInstructionText.Compact(step.message)", StringComparison.Ordinal);
        int layoutCall = show.IndexOf("ApplyCenteredTutorialLayout(step.screenPosition)", StringComparison.Ordinal);
        int firstAnimation = show.IndexOf("AnimatePanel", StringComparison.Ordinal);
        Check(messageAssignment >= 0 && layoutCall > messageAssignment && layoutCall < firstAnimation,
            "Each new message is assigned, measured, and sized before its entrance animation captures the target.");
        string update = Method(manager, "private void Update()");
        int refreshLayout = update.IndexOf("RefreshCenteredTutorialLayout()", StringComparison.Ordinal);
        int pointerOwnership = update.IndexOf("TryRefreshTutorialPointer()", StringComparison.Ordinal);
        int tracingGate = update.IndexOf("buildDirector.isTracingStep", StringComparison.Ordinal);
        Check(refreshLayout >= 0 && pointerOwnership > refreshLayout && tracingGate > refreshLayout,
            "Screen resizing is handled before build-pointer ownership and tracing gates.");
        string refresh = Method(manager, "private void RefreshCenteredTutorialLayout()");
        Check(refresh.Contains("IsBannerTransitioning") && refresh.Contains("CurrentStep") && refresh.Contains("Screen.safeArea") && refresh.Contains("parent.rect.size"),
            "Responsive checks use the active centered step, avoid animation fighting, and notice Canvas/safe-area changes.");
        string apply = Method(manager, "private void ApplyCenteredTutorialLayout(");
        Check(apply.Contains("position != TutorialPosition.Center && position != TutorialPosition.LowCenter") &&
              apply.Contains("position == TutorialPosition.LowCenter ? lowCenterOffset : Vector2.zero"),
            "Only Center/LowCenter invoke the helper and LowCenter keeps its authored offset.");
        Check(apply.Contains("UIReservedRegionLayout.NotifyLayoutChanging()"), "Overhead/HUD reservations track the grown paper rectangle.");
        string cache = Method(manager, "private void CacheTutorialPanelPositions()");
        Check(cache.Contains("if (!hasCachedCenterPanelPosition)") && cache.Contains("centerPanelDefaultSize = centerPanelRect.rect.size"),
            "Original size is cached once, not overwritten by the previous step's expanded size.");
        Check(!apply.Contains("nextButton") && !apply.Contains("skipButton") && !apply.Contains("leftPanel"),
            "The centered hook does not reposition Continue, Skip, or the left banner.");
    }

    private static string Method(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Missing production method: " + signature);
        int body = source.IndexOf('{', start), depth = 0;
        bool inString = false, escaped = false;
        for (int index = body; index < source.Length; index++)
        {
            char current = source[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') inString = false;
                continue;
            }
            if (current == '"') inString = true;
            else if (current == '{') depth++;
            else if (current == '}' && --depth == 0) return source.Substring(start, index - start + 1);
        }
        throw new InvalidOperationException("Unclosed production method: " + signature);
    }

    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/Level/Tutorial/TutorialManager.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
