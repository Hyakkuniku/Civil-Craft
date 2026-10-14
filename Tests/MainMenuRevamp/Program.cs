using System.Text.RegularExpressions;

internal static partial class Program
{
    private static int checks;
    private static readonly List<string> Failures = new();
    private static readonly string Root = FindRoot();

    private static int Main(string[] args)
    {
        try
        {
            TestLayouts();
            TestRejectedInputs();
            TestSourceIsolation();
            TestSavedSceneHierarchy();
            if (Failures.Count > 0)
                throw new InvalidOperationException($"{Failures.Count} failed checks:\n" + string.Join("\n", Failures.Take(35)));
            Console.WriteLine($"MainMenuRevamp: {checks} checks passed (actual production geometry, read-only source guards and saved-scene hierarchy; no Unity session, input, network, accounts or saves).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void TestLayouts()
    {
        foreach ((float width, float height) in new[]
        {
            (1920f, 1080f), (1600f, 900f), (2160f, 1080f), (2560f, 1080f),
            (3840f, 1080f), (2048f, 1536f), (1280f, 960f), (1024f, 768f),
            (960f, 1280f), (900f, 1600f), (640f, 960f), (480f, 800f),
            (320f, 480f), (800f, 900f), (1920f, 930f), (1720f, 1080f),
            (720f, 1280f), (600f, 1200f), (1200f, 600f), (3000f, 1600f),
            (1662.77f, 1247.08f), (2119f, 978f), (1920f, 320f),
            (320f, 320f), (320f, 900f), (1590f, 980f)
        }) VerifyLayout(width, height);

        var samples = new Random(761043);
        for (int index = 0; index < 500; index++)
            VerifyLayout(samples.Next(320, 4001), samples.Next(320, 2401));
    }

    private static void VerifyLayout(float width, float height)
    {
        MainMenuLayoutFrame frame = MainMenuLayoutPolicy.Build(width, height);
        MainMenuLayoutFrame again = MainMenuLayoutPolicy.Build(width, height);
        var boxes = new (string name, MainMenuLayoutBox box, MainMenuLayoutBox again)[]
        {
            ("Splash", frame.Splash, again.Splash),
            ("Title", frame.Title, again.Title),
            ("Edition", frame.Edition, again.Edition),
            ("Play", frame.Play, again.Play),
            ("Dropdown", frame.Dropdown, again.Dropdown),
            ("Quit", frame.Quit, again.Quit),
            ("Version", frame.Version, again.Version)
        };
        float epsilon = Math.Max(width, height) * .0001f;
        foreach ((string name, MainMenuLayoutBox box, MainMenuLayoutBox repeat) in boxes)
        {
            string context = $"{name} at {width}x{height}";
            Check(float.IsFinite(box.X) && float.IsFinite(box.Y), context + " has finite position.");
            Check(float.IsFinite(box.Width) && float.IsFinite(box.Height) && box.Width > 0f && box.Height > 0f,
                context + " has finite positive size.");
            Check(box.X - box.Width * .5f >= -width * .5f - epsilon &&
                  box.X + box.Width * .5f <= width * .5f + epsilon &&
                  box.Y - box.Height * .5f >= -height * .5f - epsilon &&
                  box.Y + box.Height * .5f <= height * .5f + epsilon,
                context + " fits inside the supplied safe area.");
            Check(box.X == repeat.X && box.Y == repeat.Y && box.Width == repeat.Width && box.Height == repeat.Height,
                context + " placement is deterministic.");
        }

        // These are the existing Updates controls, not a duplicate implementation
        // of the new policy. Include the floating badge above the entry button.
        float updateUnit = Math.Clamp(Math.Min(width / 1600f, height / 900f), .55f, 1f);
        var updates = new MainMenuLayoutBox(width * .5f - 163f * updateUnit,
            -height * .5f + 68f * updateUnit, 270f * updateUnit, 80f * updateUnit);
        var badge = new MainMenuLayoutBox(updates.X, -height * .5f + 132f * updateUnit,
            232f * updateUnit, 44f * updateUnit);
        Check(Contains(frame.Updates, updates, epsilon) && Contains(frame.Updates, badge, epsilon),
            $"Reserved Updates region includes its actual entry and floating badge at {width}x{height}.");
        Check(float.IsFinite(frame.SplashUnit) && frame.SplashUnit > 0f && frame.SplashUnit <= 1f &&
            float.IsFinite(frame.MenuUnit) && frame.MenuUnit > 0f && frame.MenuUnit <= 1f,
            $"Layout units are finite, positive and at most one at {width}x{height}.");
        foreach ((string name, MainMenuLayoutBox box, _) in boxes)
        {
            Check(!Overlaps(box, updates, epsilon), $"{name} never covers Updates at {width}x{height}.");
            Check(!Overlaps(box, badge, epsilon), $"{name} never covers the NEW UPDATE badge at {width}x{height}.");
        }
        Check(!Overlaps(frame.Splash, frame.Quit, epsilon) && !Overlaps(frame.Splash, frame.Version, epsilon),
            $"Splash does not cover its footer controls at {width}x{height}.");
        Check(!Overlaps(frame.Dropdown, frame.Quit, epsilon) && !Overlaps(frame.Dropdown, frame.Version, epsilon),
            $"Dropdown does not cover footer controls at {width}x{height}.");
        Check(!Overlaps(frame.Quit, frame.Version, epsilon), $"Footer controls do not overlap at {width}x{height}.");
        Check(frame.Title.X == 0f && frame.Edition.X == 0f && frame.Play.X == 0f,
            $"Title, edition and Play are horizontally centered at {width}x{height}.");
        Check(frame.Title.Y > frame.Edition.Y && frame.Edition.Y > frame.Play.Y &&
            !Overlaps(frame.Title, frame.Edition, epsilon) && !Overlaps(frame.Title, frame.Play, epsilon) &&
            !Overlaps(frame.Edition, frame.Play, epsilon),
            $"Centered intro title, edition and Play rows remain distinct at {width}x{height}.");
        Check(float.IsFinite(frame.IntroUnit) && frame.IntroUnit > 0f && frame.IntroUnit <= 1f &&
            frame.IntroUnit == Math.Min(1f, Math.Min(width / 1600f, height / 900f)),
            $"Centered intro sizing uses the valid Canvas scale independently of the inactive legacy paper bounds at {width}x{height}.");
    }

    private static bool Overlaps(MainMenuLayoutBox left, MainMenuLayoutBox right, float epsilon) =>
        Math.Abs(left.X - right.X) < (left.Width + right.Width) * .5f - epsilon &&
        Math.Abs(left.Y - right.Y) < (left.Height + right.Height) * .5f - epsilon;

    private static bool Contains(MainMenuLayoutBox outer, MainMenuLayoutBox inner, float epsilon) =>
        outer.Left <= inner.Left + epsilon && outer.Right >= inner.Right - epsilon &&
        outer.Bottom <= inner.Bottom + epsilon && outer.Top >= inner.Top - epsilon;

    private static void TestRejectedInputs()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MinValue, -1f, 0f, 100f, 319.9f })
        {
            foreach (bool badWidth in new[] { true, false })
            {
                try
                {
                    MainMenuLayoutPolicy.Build(badWidth ? invalid : 1600f, badWidth ? 900f : invalid);
                    Check(false, "Invalid Canvas dimension must reject: " + invalid);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    Check(exception.ParamName == (badWidth ? "width" : "height"),
                        "Invalid Canvas dimension reports the matching width/height argument.");
                }
            }
        }
    }

    private static void TestSourceIsolation()
    {
        string policy = Read("Assets/Script/UI/MainMenuLayoutPolicy.cs");
        string layout = Read("Assets/Script/UI/MainMenuPresentationLayout.cs");
        string authoring = Read("Assets/Editor/MainMenuRevampAuthoring.cs");
        Check(!policy.Contains("using UnityEngine") && !policy.Contains("using UnityEditor"),
            "Geometry policy is System-only and can be tested outside Unity.");
        foreach (string forbidden in new[] { "UnityWebRequest", "HttpClient", "Application.OpenURL", "PlayerPrefs", "File.Write", "PlayFabClientAPI", "PlayFabServerAPI", "CloudSaveManager", "GameWalletService", "SaveGame", "SessionTicket" })
        {
            Check(!policy.Contains(forbidden, StringComparison.Ordinal), "Geometry policy never touches gameplay authority: " + forbidden);
            Check(!layout.Contains(forbidden, StringComparison.Ordinal), "Runtime presentation never touches gameplay authority: " + forbidden);
        }
        foreach (string forbidden in new[] { "Instantiate(", "AddComponent", "GameObject.Create", "new GameObject", "AddListener", "RemoveListener", "SetActive(", "StartCoroutine", "LoadScene(", "ButtonClickedEvent" })
            Check(!layout.Contains(forbidden, StringComparison.Ordinal), "Runtime layout does not create UI or change menu actions/state: " + forbidden);
        Check(layout.Contains("MainMenuLayoutPolicy.Build") && layout.Contains("Screen.safeArea") && layout.Contains("lastCanvasRect"),
            "Runtime uses the tested policy and recalculates for changed safe area/Canvas bounds.");
        Check(!Regex.IsMatch(layout, @"dropdownPanel\s*\.\s*(?:anchoredPosition|localScale|localPosition)\s*="),
            "Presentation sizing never overwrites the existing dropdown slide animation.");
        Check(!Regex.IsMatch(layout, @"\.\s*localScale\s*="),
            "Presentation preserves the original button entrance scales.");
        Check(layout.Contains("ApplyLayoutForPreview"), "Native fixture can supply its own geometry without changing screen/player settings.");

        Check(authoring.Contains("Temp/main-menu-centered-intro.request") && authoring.Contains("File.Exists(Request)"),
            "Editor authoring is request-driven rather than unconditional scene mutation.");
        Check(authoring.Contains("EditorApplication.isPlayingOrWillChangePlaymode") && authoring.Contains("BuildPipeline.isBuildingPlayer"),
            "Editor authoring avoids Play Mode, builds and imports.");
        Check(authoring.Contains("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset") &&
            authoring.Contains("Assets/Elements/UI/LevelResultRounded.png"),
            "Menu reuses existing Bekind typography and rounded game UI artwork.");
        Check(Regex.IsMatch(authoring, @"if\s*\(\s*!alreadyDirty\s*\)\s*\{\s*Check\(EditorSceneManager\.SaveScene"),
            "Already-dirty Main Menu is not automatically saved over user edits.");
        Check(authoring.Contains("PENDING SAVE") && authoring.Contains("Undo.RevertAllDownToGroup") && authoring.Contains("VerifyRollback"),
            "Dirty scenes have a save handoff and failed authoring verifies undo rollback.");
        Check(authoring.Contains("JsonUtility.ToJson(button.onClick)") &&
            Regex.IsMatch(authoring, @"JsonUtility\.ToJson\(pair\.Key\.onClick\)\s*==\s*pair\.Value"),
            "Editor validation compares every pre-existing button's serialized callbacks.");
        Check(authoring.Contains("VerifyPreserved") && authoring.Contains("WithoutHideList") && authoring.Contains("uiToHideWhileDropdownOpen") &&
            authoring.Contains("expected.SequenceEqual(now)"),
            "Existing menu configuration and hide-list entries are compared; inactive authored hero removal is presentation-only.");
        Check(authoring.Contains("layout.splashBoard.parent == layout.dropdownPanel.parent") &&
            authoring.Contains("dropdown.parent == dropdownParent"),
            "Hero shares the original menu branch and dropdown hierarchy is preserved above its container input layer.");
        Check(authoring.Contains("Detached Existing Container Input Layer") && authoring.Contains("backdropImage.raycastTarget = true"),
            "Native fixture includes the original full-screen input backdrop instead of testing only isolated controls.");
        Check(Regex.IsMatch(authoring, @"pair\.Key\.activeSelf\s*==\s*\(\s*centered\s*&&\s*pair\.Key\s*==\s*board\s*\?\s*false\s*:\s*pair\.Value\s*\)"),
            "Existing initial active states are compared with only the recoverable paper board intentionally deactivated.");
        Check(authoring.Contains("NewPreviewScene") && authoring.Contains("script.enabled = false") &&
            authoring.Contains("new Button.ButtonClickedEvent()") && authoring.Contains("camera.scene = preview"),
            "Native previews isolate cloned controls, disable non-UI behavior and strip fixture callbacks.");
        Check(Regex.Matches(authoring, @"\bBuildCenteredIntro\(\s*canvas\s*,\s*menu\s*,\s*dropdown\s*,\s*layout\s*\)").Count >= 2 &&
            authoring.Contains("PrimaryLabel(button)"),
            "Intro-only authoring exercises a second build pass with explicit primary-label selection before saving.");
        Check(authoring.Contains("PresentationSignature(dropdown)==dropdownPresentation"),
            "Intro-only authoring checks the existing dropdown presentation remains unchanged.");
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) Failures.Add(message);
    }
    private static string FindRoot()
    {
        DirectoryInfo directory = new(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/UI/MainMenuUIController.cs"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Run from the Unity project root or a child directory.");
    }
}
