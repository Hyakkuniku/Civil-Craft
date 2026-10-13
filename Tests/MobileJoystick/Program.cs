using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

internal static partial class Program
{
    private static int checks;
    private const float Epsilon = 0.0003f;

    private static void Main()
    {
        TestPointerOwnership();
        TestFixedPlacement();
        TestFloatingPlacement();
        TestFloatingReversal();
        TestFloatingBoundaries();
        TestCancelAndRestart();
        TestInvalidInputs();
        TestScaledCoordinates();
        TestNoStationaryDrift();
        TestPreferences();
        TestRuntimeBoundaries();
        Console.WriteLine($"All {checks} mobile joystick checks passed (actual geometry, input routing and settings source; no Unity session or input/network access).");
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Near(Vector2 actual, Vector2 expected, string message, float epsilon = Epsilon)
    {
        Check(float.IsFinite(actual.x) && float.IsFinite(actual.y) &&
            Vector2.Distance(actual, expected) <= epsilon,
            $"{message}: expected {expected}, got {actual}");
    }

    private static void ResetState(MobileJoystickMotion motion, string message)
    {
        Check(!motion.HasPointer, message + ": pointer released");
        Check(motion.PointerId == int.MinValue, message + ": invalid sentinel restored");
        Near(motion.Value, Vector2.zero, message + ": movement stopped");
        Near(motion.Origin, Vector2.zero, message + ": origin cleared");
        Near(motion.HandlePosition, Vector2.zero, message + ": handle cleared");
    }

    private static void TestPointerOwnership()
    {
        var motion = new MobileJoystickMotion();
        Check(!motion.HasPointer, "A fresh joystick does not own a pointer.");
        Check(!motion.Move(4, new Vector2(10, 10), 40), "Movement before touchdown is ignored.");
        Check(!motion.End(4), "Release before touchdown is ignored.");
        Check(motion.TryBegin(4, new Vector2(10, 20), new Vector2(50, 60), true), "First finger is accepted.");
        Check(motion.HasPointer && motion.PointerId == 4 && motion.IsFixed, "Pointer and fixed mode are captured.");
        Check(!motion.TryBegin(5, new Vector2(90, 90), Vector2.zero, false), "Second finger cannot replace the first.");
        Check(!motion.TryBegin(4, new Vector2(90, 90), Vector2.zero, false), "Duplicate touchdown cannot reset an active drag.");
        Check(!motion.Move(5, new Vector2(90, 90), 40), "Unowned pointer cannot move the joystick.");
        Check(!motion.End(5), "Unowned pointer cannot end the joystick.");
        Check(motion.PointerId == 4 && motion.IsFixed, "Rejected pointer operations keep original ownership/mode.");
        Near(motion.Origin, new Vector2(50, 60), "Rejected pointer operations keep placement");
        Near(motion.Value, Vector2.zero, "Rejected pointer operations keep value");
        Check(motion.Move(4, new Vector2(30, 20), 40), "Owned pointer moves.");
        Near(motion.Value, new Vector2(0.5f, 0), "Owned movement is normalized by radius");
        Check(motion.End(4), "Owned pointer can release.");
        ResetState(motion, "Matching release");
        Check(motion.TryBegin(5, new Vector2(90, 90), Vector2.zero, false), "A different finger can begin after release.");
        Check(motion.PointerId == 5 && !motion.IsFixed, "New drag captures new finger/mode.");
    }

    private static void TestFixedPlacement()
    {
        var motion = new MobileJoystickMotion();
        Vector2 home = new Vector2(-200, -300);
        Vector2 touchdown = new Vector2(-180, -320);
        Check(motion.TryBegin(-1, touchdown, home, true), "Mouse pointer -1 can use fixed joystick.");
        Near(motion.Origin, home, "Fixed origin is authored position, not finger touchdown");
        Near(motion.HandlePosition, home, "Fixed handle starts centered");
        Near(motion.Value, Vector2.zero, "Off-center fixed touchdown does not jump movement");
        Check(motion.Move(-1, touchdown, 50), "Stationary fixed input is accepted.");
        Near(motion.Value, Vector2.zero, "Stationary fixed touchdown stays neutral");
        Check(motion.Move(-1, touchdown + new Vector2(15, 20), 50), "Fixed movement inside radius.");
        Near(motion.Value, new Vector2(0.3f, 0.4f), "Fixed value is relative touchdown delta");
        Near(motion.HandlePosition, home + new Vector2(15, 20), "Fixed handle moves from home");
        Near(motion.Origin, home, "Fixed base never follows finger");
        Check(motion.Move(-1, touchdown + new Vector2(150, 200), 50), "Fixed movement beyond radius.");
        Near(motion.Value, new Vector2(0.6f, 0.8f), "Fixed diagonal input clamps to unit circle");
        Near(motion.HandlePosition, home + new Vector2(30, 40), "Fixed handle clamps to radius");
        Near(motion.Origin, home, "Fixed origin remains unchanged after a long drag");
        Check(motion.Move(-1, touchdown + new Vector2(-150, 0), 50), "Fixed reverse movement is accepted.");
        Near(motion.Value, new Vector2(-1, 0), "Fixed reverse direction updates immediately");
        Near(motion.HandlePosition, home + new Vector2(-50, 0), "Fixed reverse handle stays clamped");
        Check(motion.Move(-1, touchdown, 50), "Fixed return to touchdown.");
        Near(motion.Value, Vector2.zero, "Fixed return to touchdown is neutral");
        Near(motion.HandlePosition, home, "Fixed handle returns home");
    }

    private static void TestFloatingPlacement()
    {
        var motion = new MobileJoystickMotion();
        Vector2 start = new Vector2(120, 400);
        Check(motion.TryBegin(8, start, new Vector2(-900, -900), false), "Floating touch starts away from authored joystick.");
        Near(motion.Origin, start, "Floating origin is first touch");
        Near(motion.HandlePosition, start, "Floating handle starts beneath finger");
        Near(motion.Value, Vector2.zero, "Floating touchdown is neutral");
        Check(motion.Move(8, start + new Vector2(15, 20), 50), "Floating movement inside radius.");
        Near(motion.Origin, start, "Floating base stays while within radius");
        Near(motion.HandlePosition, start + new Vector2(15, 20), "Floating handle tracks finger inside radius");
        Near(motion.Value, new Vector2(0.3f, 0.4f), "Floating movement within radius is proportional");
        Vector2 point = start + new Vector2(150, 200);
        Check(motion.Move(8, point, 50), "Floating movement beyond radius.");
        Near(motion.Origin, point - new Vector2(30, 40), "Floating base follows excess drag distance");
        Near(motion.HandlePosition, point, "Floating handle continues to track finger after following");
        Near(motion.Value, new Vector2(0.6f, 0.8f), "Floating excess drag stays unit normalized");
        Check(Vector2.Distance(motion.Origin, motion.HandlePosition) <= 50 + Epsilon, "Floating handle never exceeds radius from moving origin.");
        Check(motion.Move(8, point, 50), "Repeated floating point is accepted.");
        Near(motion.Origin, point - new Vector2(30, 40), "Following does not shift again at a stationary finger");
        Near(motion.Value, new Vector2(0.6f, 0.8f), "Stationary finger retains movement rather than re-centering each frame");
    }

    private static void TestFloatingReversal()
    {
        var motion = new MobileJoystickMotion();
        Check(motion.TryBegin(0, Vector2.zero, Vector2.zero, false), "Pointer ID zero is valid.");
        Check(motion.Move(0, new Vector2(200, 0), 50), "Long rightward floating drag.");
        Near(motion.Origin, new Vector2(150, 0), "Floating rightward base follows");
        Check(motion.Move(0, new Vector2(150, 0), 50), "Finger returns to shifted origin.");
        Near(motion.Value, Vector2.zero, "Returning to shifted base is neutral");
        Check(motion.Move(0, new Vector2(125, 0), 50), "Reverse by half radius.");
        Near(motion.Value, new Vector2(-0.5f, 0), "Floating reversal is relative current base, not initial touch");
        Near(motion.Origin, new Vector2(150, 0), "Base stays within reverse radius");
        Check(motion.Move(0, new Vector2(-200, 0), 50), "Long leftward reversal.");
        Near(motion.Origin, new Vector2(-150, 0), "Floating base follows in reverse");
        Near(motion.Value, new Vector2(-1, 0), "Floating leftward reversal is normalized");
        Near(motion.HandlePosition, new Vector2(-200, 0), "Floating handle remains under finger on reversal");
    }

    private static void TestCancelAndRestart()
    {
        foreach (bool fixedMode in new[] { true, false })
        {
            var motion = new MobileJoystickMotion();
            Check(motion.TryBegin(9, new Vector2(10, 20), new Vector2(-30, -40), fixedMode), "Cancel fixture begins.");
            Check(motion.Move(9, new Vector2(300, 400), 50), "Cancel fixture moves.");
            motion.Cancel();
            ResetState(motion, "Focus/disable/settings cancellation");
            motion.Cancel();
            ResetState(motion, "Repeated cancellation is safe");
            Check(!motion.Move(9, new Vector2(600, 700), 50), "Old finger cannot revive canceled movement.");
            Check(!motion.End(9), "Old finger release does not claim state after cancellation.");
            Check(motion.TryBegin(10, new Vector2(100, 200), new Vector2(-10, -20), !fixedMode), "Next drag can start in changed mode.");
            Check(motion.IsFixed == !fixedMode && motion.PointerId == 10, "Mode changes apply to next touchdown.");
            Near(motion.Value, Vector2.zero, "Restarted drag has no stale movement");
            Near(motion.Origin, !fixedMode ? new Vector2(-10, -20) : new Vector2(100, 200), "Restart has correct new mode origin");
        }
    }

    private static void TestFloatingBoundaries()
    {
        var bounds = new Rect(40, 30, 320, 480);
        foreach (Vector2 touchdown in new[] {
            new Vector2(0, 0), new Vector2(499, 599), new Vector2(120, 200),
            new Vector2(40, 510), new Vector2(360, 30) })
        {
            var motion = new MobileJoystickMotion();
            Check(motion.SetFloatingBounds(bounds), "Idle bounded geometry accepts finite origin limits.");
            Check(motion.TryBegin(1, touchdown, new Vector2(-200, -100), false), "Bounded floating geometry begins away from joystick.");
            Vector2 initial = new Vector2(Mathf.Clamp(touchdown.x, bounds.xMin, bounds.xMax),
                Mathf.Clamp(touchdown.y, bounds.yMin, bounds.yMax));
            Near(motion.Origin, initial, "Boundary touchdown clamps only the UI origin");
            Near(motion.HandlePosition, initial, "Boundary touchdown handle starts centered");
            Near(motion.Value, Vector2.zero, "Clamped touchdown never creates unintended walking input");
            Check(!motion.SetFloatingBounds(new Rect(-900, -900, 1800, 1800)), "An active pointer cannot have bounds replaced mid-drag.");
            for (int frame = 0; frame < 60; frame++)
                Check(motion.Move(1, touchdown, 50), "Stationary boundary touchdown stays accepted.");
            Near(motion.Origin, initial, "Repeated stationary edge touch does not drift origin");
            Near(motion.Value, Vector2.zero, "Repeated stationary edge touch stays neutral");

            Check(motion.Move(1, touchdown + new Vector2(2000, 0), 50), "Owned finger can continue right past UI region.");
            Near(motion.Origin, new Vector2(bounds.xMax, initial.y), "Long rightward drag cannot send joystick into right side");
            Near(motion.Value, new Vector2(1, 0), "Clamped right edge keeps full rightward player input");
            Near(motion.HandlePosition, new Vector2(bounds.xMax + 50, initial.y), "Handle stays one radius from edge origin");
            Vector2 rightEdgeOrigin = motion.Origin;
            for (int frame = 0; frame < 120; frame++)
                Check(motion.Move(1, touchdown + new Vector2(2000, 0), 50), "Stationary right-edge hold stays accepted.");
            Near(motion.Origin, rightEdgeOrigin, "Held finger beyond right edge never accumulates UI drift");
            Near(motion.Value, new Vector2(1, 0), "Right edge does not cancel sustained walking input");

            Vector2 pointerOffset = touchdown - initial;
            Check(motion.Move(1, rightEdgeOrigin + pointerOffset, 50), "Finger can return to bounded current origin.");
            Near(motion.Value, Vector2.zero, "Return to bounded origin is neutral");
            Check(motion.Move(1, rightEdgeOrigin + pointerOffset - new Vector2(25, 0), 50), "Bounded stick reverses within radius.");
            Near(motion.Value, new Vector2(-0.5f, 0), "Bounded reverse direction stays relative to current origin");
            Check(motion.Move(1, touchdown - new Vector2(2000, 0), 50), "Long leftward reversal is accepted.");
            Near(motion.Origin, new Vector2(bounds.xMin, initial.y), "Long reverse clamps to left safe boundary");
            Near(motion.Value, new Vector2(-1, 0), "Left boundary preserves reverse walking input");

            foreach (Vector2 extreme in new[] { new Vector2(-4000, -4000), new Vector2(4000, 4000),
                new Vector2(-4000, 4000), new Vector2(4000, -4000) })
            {
                Check(motion.Move(1, extreme, 50), "Extreme diagonal drag is safely bounded.");
                Check(motion.Origin.x >= bounds.xMin && motion.Origin.x <= bounds.xMax &&
                    motion.Origin.y >= bounds.yMin && motion.Origin.y <= bounds.yMax,
                    "Diagonal motion keeps origin inside complete safe rectangle.");
                Check(motion.Value.magnitude <= 1 + Epsilon, "Bounded diagonal output never exceeds unit circle.");
                Check(Vector2.Distance(motion.HandlePosition, motion.Origin) <= 50 + Epsilon,
                    "Bounded diagonal handle never exceeds movement radius.");
                Vector2 origin = motion.Origin, value = motion.Value, handle = motion.HandlePosition;
                Check(motion.Move(1, extreme, 50), "Repeated diagonal extreme is accepted.");
                Near(motion.Origin, origin, "Stationary diagonal extreme does not drift");
                Near(motion.Value, value, "Stationary diagonal extreme preserves movement");
                Near(motion.HandlePosition, handle, "Stationary diagonal extreme preserves bounded handle");
            }
            Check(motion.End(1), "Bounded pointer can end normally.");
            Check(motion.TryBegin(2, new Vector2(-100, -200), Vector2.zero, false), "Next unbounded pointer starts after cancellation.");
            Near(motion.Origin, new Vector2(-100, -200), "Cancellation clears stale floating bounds");
        }

        foreach (Rect invalid in new[] { new Rect(float.NaN, 0, 1, 1), new Rect(0, float.PositiveInfinity, 1, 1),
            new Rect(0, 0, -1, 1), new Rect(0, 0, 1, -1), new Rect(0, 0, float.NaN, 1),
            new Rect(0, 0, 1, float.PositiveInfinity) })
        {
            var motion = new MobileJoystickMotion();
            Check(!motion.SetFloatingBounds(invalid), "Nonfinite or inverted UI bounds are rejected.");
            Check(!motion.HasPointer, "Rejected bounds do not capture a pointer.");
        }
        var fixedMotion = new MobileJoystickMotion();
        Check(fixedMotion.SetFloatingBounds(bounds), "Fixed regression fixture can set prior floating limits.");
        Check(fixedMotion.TryBegin(3, new Vector2(100, 200), new Vector2(-400, -100), true), "Fixed joystick remains usable outside floating origin limits.");
        Check(fixedMotion.Move(3, new Vector2(900, 200), 50), "Fixed drag remains usable with prior floating bounds.");
        Near(fixedMotion.Origin, new Vector2(-400, -100), "Fixed authored location ignores floating limits");
        Near(fixedMotion.Value, new Vector2(1, 0), "Fixed output remains unchanged");
        foreach (Rect collapsed in new[] { new Rect(40, 30, 0, 480), new Rect(40, 30, 320, 0), new Rect(40, 30, 0, 0) })
        {
            var narrow = new MobileJoystickMotion();
            Check(narrow.SetFloatingBounds(collapsed), "Collapsed safe-area axis is valid and cannot enlarge the allowed UI region.");
            Check(narrow.TryBegin(1, Vector2.zero, Vector2.zero, false), "Collapsed-origin fixture begins.");
            Near(narrow.Value, Vector2.zero, "Collapsed safe-area touchdown stays neutral");
            Check(narrow.Move(1, new Vector2(4000, 4000), 50), "Collapsed safe region still supports movement.");
            Check(narrow.Origin.x >= collapsed.xMin && narrow.Origin.x <= collapsed.xMax &&
                narrow.Origin.y >= collapsed.yMin && narrow.Origin.y <= collapsed.yMax,
                "Collapsed safe-region axis never lets origin escape.");
            Check(narrow.Value.magnitude > 0 && narrow.Value.magnitude <= 1 + Epsilon,
                "Collapsed bounds constrain UI, not the player's movement input.");
        }
        var cleared = new MobileJoystickMotion();
        Check(cleared.SetFloatingBounds(bounds), "Clear-bounds fixture establishes limits.");
        cleared.ClearFloatingBounds();
        Check(cleared.TryBegin(1, new Vector2(-900, -900), Vector2.zero, false), "Explicit clear restores unrestricted geometry.");
        Near(cleared.Origin, new Vector2(-900, -900), "Cleared bounds do not leak into later mode");
        var cannotClear = new MobileJoystickMotion();
        Check(cannotClear.SetFloatingBounds(bounds), "Owned clear-bound fixture establishes limits.");
        Check(cannotClear.TryBegin(1, new Vector2(100, 100), Vector2.zero, false), "Owned clear-bound fixture begins.");
        cannotClear.ClearFloatingBounds();
        Check(cannotClear.Move(1, new Vector2(4000, 100), 50), "Moving after ignored active-pointer clear still works.");
        Near(cannotClear.Origin, new Vector2(bounds.xMax, 100), "Bounds cannot be cleared out from under an owned pointer");

        foreach (float scale in new[] { 0.25f, 0.75f, 1f, 2f, 4f })
        foreach (float radians in new[] { 0f, MathF.PI / 4, MathF.PI / 2, MathF.PI, MathF.PI * 1.5f })
        {
            var scaledBounds = new Rect(bounds.x * scale, bounds.y * scale, bounds.width * scale, bounds.height * scale);
            Vector2 touchdown = new Vector2(0, 600) * scale;
            float radius = 50 * scale;
            var scaled = new MobileJoystickMotion();
            Check(scaled.SetFloatingBounds(scaledBounds), "Phone/tablet scaled geometry establishes valid limits.");
            Check(scaled.TryBegin(1, touchdown, Vector2.zero, false), "Scaled edge touchdown is accepted.");
            Near(scaled.Value, Vector2.zero, "Scaled edge touchdown remains neutral");
            Vector2 direction = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
            Check(scaled.Move(1, touchdown + direction * (2000 * scale), radius), "Scaled extreme drag is accepted.");
            Check(scaled.Origin.x >= scaledBounds.xMin && scaled.Origin.x <= scaledBounds.xMax &&
                scaled.Origin.y >= scaledBounds.yMin && scaled.Origin.y <= scaledBounds.yMax,
                "Every canvas scale retains full rectangle boundary.");
            Check(scaled.Value.magnitude <= 1 + Epsilon, "Scaled bounded output remains unit normalized.");
            Check(Vector2.Distance(scaled.HandlePosition, scaled.Origin) <= radius + Epsilon,
                "Scaled bounded handle respects actual movement radius.");
            Vector2 origin = scaled.Origin, value = scaled.Value;
            Check(scaled.Move(1, touchdown + direction * (2000 * scale), radius), "Scaled repeated extreme is accepted.");
            Near(scaled.Origin, origin, "Scaled boundary hold does not drift", Epsilon * MathF.Max(1, scale));
            Near(scaled.Value, value, "Scaled boundary hold preserves movement");
        }
    }

    private static void TestInvalidInputs()
    {
        var invalidPositions = new[] {
            new Vector2(float.NaN, 0), new Vector2(0, float.NaN),
            new Vector2(float.PositiveInfinity, 0), new Vector2(0, float.NegativeInfinity)
        };
        foreach (bool fixedMode in new[] { true, false })
        {
            foreach (Vector2 invalid in invalidPositions)
            {
                var motion = new MobileJoystickMotion();
                Check(!motion.TryBegin(3, invalid, Vector2.zero, fixedMode), "Nonfinite touchdown is rejected.");
                Check(!motion.HasPointer, "Rejected touchdown does not capture a finger.");
            }
            var active = new MobileJoystickMotion();
            Check(!active.TryBegin(int.MinValue, Vector2.zero, Vector2.zero, fixedMode), "Reserved pointer sentinel cannot claim.");
            Check(active.TryBegin(3, new Vector2(10, 20), new Vector2(100, 200), fixedMode), "Finite invalid-input fixture begins.");
            Check(active.Move(3, new Vector2(30, 40), 50), "Finite fixture establishes nonzero movement.");
            Vector2 origin = active.Origin, handle = active.HandlePosition, value = active.Value;
            foreach (Vector2 invalid in invalidPositions)
            {
                Check(!active.Move(3, invalid, 50), "Nonfinite movement position is ignored.");
                Preserved(active, origin, handle, value);
            }
            foreach (float invalidRange in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Check(!active.Move(3, new Vector2(99, 99), invalidRange), "Invalid radius is ignored.");
                Preserved(active, origin, handle, value);
            }
        }
        foreach (Vector2 invalid in invalidPositions)
        {
            var motion = new MobileJoystickMotion();
            Check(!motion.TryBegin(3, Vector2.zero, invalid, true), "Nonfinite fixed origin is rejected.");
            Check(!motion.HasPointer, "Invalid fixed origin does not capture a finger.");
        }

        static void Preserved(MobileJoystickMotion motion, Vector2 origin, Vector2 handle, Vector2 value)
        {
            Check(motion.HasPointer && motion.PointerId == 3, "Invalid input leaves owning pointer intact.");
            Near(motion.Origin, origin, "Invalid input preserves origin");
            Near(motion.HandlePosition, handle, "Invalid input preserves handle");
            Near(motion.Value, value, "Invalid input preserves normalized movement");
        }
    }

    private static void TestScaledCoordinates()
    {
        // Emulate canvas-scaled local positions for small phones through tablets.
        foreach (float scale in new[] { 0.25f, 0.75f, 1f, 2f, 4f })
        foreach (bool fixedMode in new[] { true, false })
        foreach (float length in new[] { 0f, 0.1f, 0.5f, 1f, 2f, 10f })
        foreach (float radians in new[] { 0f, MathF.PI / 4, MathF.PI / 2, MathF.PI, MathF.PI * 1.5f })
        {
            Vector2 start = new Vector2(70, 130) * scale;
            Vector2 home = new Vector2(-140, -300) * scale;
            float radius = 50 * scale;
            Vector2 direction = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
            Vector2 delta = direction * (radius * length);
            Vector2 point = start + delta;
            var motion = new MobileJoystickMotion();
            Check(motion.TryBegin(3, start, home, fixedMode), "Scaled coordinate fixture begins.");
            Check(motion.Move(3, point, radius), "Scaled coordinate fixture moves.");
            Near(motion.Value, direction * MathF.Min(length, 1f), "Value is independent of screen/canvas scale");
            Check(motion.Value.magnitude <= 1f + Epsilon, "Normalized output never exceeds unit circle.");
            Near(motion.HandlePosition, fixedMode ? home + direction * (radius * MathF.Min(length, 1f)) : point,
                "Scaled handle follows expected fixed/floating path", Epsilon * MathF.Max(1, scale));
            Near(motion.Origin, fixedMode ? home : length <= 1 ? start : point - direction * radius,
                "Scaled base remains fixed or follows excess distance", Epsilon * MathF.Max(1, scale));
        }
    }

    private static void TestNoStationaryDrift()
    {
        foreach (bool fixedMode in new[] { true, false })
        {
            var motion = new MobileJoystickMotion();
            Check(motion.TryBegin(1, Vector2.zero, new Vector2(15, 25), fixedMode), "No-drift fixture begins.");
            Vector2 point = new Vector2(800, 600);
            Check(motion.Move(1, point, 50), "No-drift fixture has a long drag.");
            Vector2 origin = motion.Origin, handle = motion.HandlePosition, value = motion.Value;
            for (int frame = 0; frame < 600; frame++)
                Check(motion.Move(1, point, 50), "Stationary finger remains accepted.");
            Near(motion.Origin, origin, "Ten seconds of repeated stationary frames do not drift base");
            Near(motion.HandlePosition, handle, "Ten seconds of repeated stationary frames do not drift handle");
            Near(motion.Value, value, "Ten seconds of repeated stationary frames preserve normalized movement");
        }
    }

    private static void TestPreferences()
    {
        string root = RepositoryRoot();
        SyntaxNode syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,
            "Assets/Script/SettingsScripts/SettingsManager.cs"))).GetRoot();
        ClassDeclarationSyntax settings = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "MobileJoystickSettings");
        string source = "using UnityEngine;\n" + settings.NormalizeWhitespace().ToFullString();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(PlayerPrefs).Assembly.Location));
        var compilation = CSharpCompilation.Create("MobileJoystickSettingsChecks", new[] { CSharpSyntaxTree.ParseText(source) },
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var binary = new MemoryStream();
        var emitted = compilation.Emit(binary);
        Check(emitted.Success, "Actual joystick settings class compiles: " + string.Join("\n", emitted.Diagnostics));
        Type actual = Assembly.Load(binary.ToArray()).GetType("MobileJoystickSettings", true);
        PropertyInfo fixedProperty = actual.GetProperty("IsFixed");
        MethodInfo setFixed = actual.GetMethod("SetFixed");
        MethodInfo reset = actual.GetMethod("ResetListeners", BindingFlags.Static | BindingFlags.NonPublic);
        EventInfo changed = actual.GetEvent("Changed");
        string key = (string)actual.GetField("PreferenceKey").GetRawConstantValue();
        bool IsFixed() => (bool)fixedProperty.GetValue(null);
        void Set(bool value) => setFixed.Invoke(null, new object[] { value });

        PlayerPrefs.Reset();
        Check(key == "FixedJoystick", "Preference uses dedicated device-local joystick key.");
        Check(IsFixed(), "Fresh installation defaults to existing fixed behavior.");
        int notifications = 0;
        bool lastMode = true;
        Action<bool> listener = value => { notifications++; lastMode = value; };
        changed.AddEventHandler(null, listener);
        Set(true);
        Check(notifications == 0, "Reapplying fixed preference does not emit spurious movement-reset notification.");
        Check(PlayerPrefs.Ints[key] == 1 && PlayerPrefs.Saves == 1, "Fixed mode saves explicit checked preference.");
        Set(false);
        Check(!IsFixed() && PlayerPrefs.Ints[key] == 0, "Unchecked preference selects floating mode.");
        Check(notifications == 1 && !lastMode && PlayerPrefs.Saves == 2, "Mode change persists and notifies running joystick once.");
        Set(false);
        Check(notifications == 1 && !IsFixed(), "Repeated floating preference does not reset a drag again.");
        Set(true);
        Check(IsFixed() && notifications == 2 && lastMode, "Switching back to fixed persists and notifies.");
        Check(PlayerPrefs.Ints.Count == 1, "Joystick setting changes no unrelated preferences.");
        Check(reset != null && reset.GetCustomAttributes().Any(attribute => attribute is RuntimeInitializeOnLoadMethodAttribute),
            "Subsystem initialization clears stale listeners when domain reload is disabled.");
        reset.Invoke(null, null);
        Check(IsFixed(), "Resetting runtime listeners preserves player preference.");
        Set(false);
        Check(!IsFixed() && notifications == 2, "Old listeners do not survive the next Play session.");
        changed.AddEventHandler(null, listener);
        Set(true);
        Check(notifications == 3 && lastMode, "Fresh session can register a current listener.");
        changed.RemoveEventHandler(null, listener);
        Set(false);
        Check(notifications == 3, "Disabled joystick can unsubscribe safely.");
        reset.Invoke(null, null);
    }

    private static string RepositoryRoot()
    {
        string directory = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(directory, "Assets/Script/SettingsScripts/SettingsManager.cs")))
            directory = Directory.GetParent(directory)?.FullName ?? throw new InvalidOperationException("Run inside the Unity repository.");
        return directory;
    }
}
