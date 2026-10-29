using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int checks;
    private sealed record CopyCase(string Label, string Original, params string[] Concepts);
    private static readonly CopyCase[] Cases =
    {
        new("build welcome", "Welcome to <b><#E09500>Build Mode!</color></b> In this tutorial, you'll learn the basic tools needed to design, build, and test your first bridge.", "Build Mode", "bridge"),
        new("camera", "Use the <b><#E09500>2D Camera</color></b> for a clear side view. It helps you inspect the structure and align its components. Tap the <b><#E09500>Camera Button</color></b> to reset the camera tilt.", "Camera", "side", "reset"),
        new("cost", "This is the current <b><#E09500>construction cost</color></b>. It displays the total cost of the materials currently used in your bridge.", "cost", "material"),
        new("budget usage", "Compare the construction cost with the <b><#E09500>budget progress bar</color></b> to see how much of your available budget has been used and how much remains.", "budget", "used|remaining|left"),
        new("budget limit", "This is the <b><#E09500>budget limit</color></b>. Stay within it to complete the project. Using too many materials may prevent you from meeting the contract requirements.", "budget", "within|under|limit"),
        new("statistics", "Open the <b><#E09500>Bridge Statistics</color></b> panel. It shows live load, dead load, road length, member count, estimated capacity, efficiency, and factor of safety.", "Open", "Bridge Statistics", "load|capacity|safety"),
        new("grid", "The <b><#E09500>Grid</color></b> turns the construction grid on or off and helps align components accurately. Toggle it once to continue.", "Grid", "toggle|tap", "once"),
        new("material inspection", "Press and hold any <b><#E09500>material</color></b> to inspect its name, cost, mass, strength limit, maximum length, and type.", "hold", "material", "name", "cost", "mass", "strength limit", "maximum length", "type"),
        new("beam complete", "<b><#E09500>BEAM BRIDGE COMPLETE!</color></b> Excellent! Your first Beam Bridge is complete. You connected the anchors using a roadway and structural supports. Now test whether it can carry the required load.", "Beam Bridge", "test", "load"),
        new("first truss section", "<b><#E09500>FIRST TRUSS SECTION COMPLETE.</color></b>\n\nExcellent! You've connected several <b><#E09500>Wood Beams</color></b> to form your first truss section. Remember what Professor Bhan explained earlier. The triangular arrangement helps the connected members maintain their structural shape. Now let's learn how to build more efficiently.", "truss section", "triang", "select|edit|efficient"),
        new("selection", "When a structure contains several members, rebuilding each one individually can take time. The <b><#E09500>Selection Tool</color></b> allows you to select multiple bridge components.", "Selection Tool", "multiple|several|group"),
        new("copy", "You can duplicate selected components instead of rebuilding them individually. Press <b><#E09500>Copy</color></b> to create a duplicate of the selected truss section.", "Copy", "selected", "truss"),
        new("paste guide", "The copied components are ready to be placed. Use <b><#E09500>Paste</color></b> to position the copied truss section on the <b><#E09500>highlighted guide</color></b>.", "copied", "guide", "position|align|place|move"),
        new("paste confirmation", "Position the copied truss section on the highlighted guide, then press <b><#E09500>Paste</color></b> to confirm.", "guide", "Paste", "confirm"),
        new("truss complete", "<b><#E09500>TRUSS STRUCTURE COMPLETE.</color></b>\n\nExcellent! You've built a <b><#E09500>Truss Bridge</color></b> and used the editing tools to duplicate its structure efficiently. Your bridge is now ready for testing. Remember what you learned about <b><#E09500>equilibrium</color></b>. Watch how the connected structural members and supports respond as the vehicle crosses.", "Truss", "equilibrium", "test|Play", "support|member|structur"),
        new("erase introduction", "First, meet the <b><#E09500>Erase Tool</color></b>. Use it to remove any bridge piece you don’t need.", "Erase Tool", "remove"),
        new("move introduction", "Next, meet the <b><#E09500>Move Tool</color></b>. Use it to reposition any bridge piece exactly where you need it.", "Move Tool", "reposition|move|drag"),
        new("materials archive", "The material archive records useful properties and grows whenever you discover a new construction material.", "material", "propert"),
        new("lessons archive", "New engineering lessons are added here as you complete activities and discover new concepts.", "lesson"),
        new("shop categories", "Use the <b><#E09500>CATEGORY TABS</color></b> to browse builders, materials, tools, decorations, vehicles, and bundles.", "CATEGORY TABS", "browse"),
        new("shop price", "Each item card shows what the item is for. Select an item to review its <b><#E09500>PRICE</color></b> before buying.", "item", "PRICE", "buy|purchase"),
        new("test success", "<b><#E09500>BRIDGE SUCCESSFULLY TESTED!</color></b> Your Beam Bridge safely carried the <b><#E09500>200 kg vehicle</color></b> across the canyon. Your first bridge has passed the structural simulation.", "Beam Bridge", "200 kg"),
        new("receipt", "Review the <b><#E09500>MATERIAL RECEIPT</color></b> to compare your total cost with the contract budget. You can also check the bridge's peak stress.", "MATERIAL RECEIPT", "cost", "budget", "stress"),
        new("save and return", "Choose <b><#E09500>RETRY</color></b> to improve the design, or <b><#E09500>SAVE & CONTINUE</color></b> to record your completed Training Beam Bridge in the Almanac. Your training contract will be ready to turn in.", "RETRY", "SAVE & CONTINUE", "Almanac", "turn in|turn-in|Bhan")
    };

    private static void Main()
    {
        string root = FindRoot();
        string[] protectedPaths = { "Assets/Scenes/CanyonCrossing.unity", "Assets/Scenes/BHAN HOUSE.unity", "Assets/Editor/BeamTutorialBuildAuthoring.cs", "Assets/Editor/TrussTutorialBuildAuthoring.cs" };
        var before = protectedPaths.ToDictionary(path => path, path => Hash(Path.Combine(root, path)));
        foreach (CopyCase item in Cases)
        {
            string original = item.Original;
            string compact = TutorialInstructionText.Compact(original);
            Check(!string.IsNullOrWhiteSpace(compact), "Known instructions remain meaningful: " + item.Label);
            if (item.Label == "erase introduction" || item.Label == "move introduction")
                Check(compact == original, "Already-short Erase/Move introductions remain intact: " + item.Label);
            else Check(Plain(compact).Length < Plain(original).Length, "Stock instruction is visibly shorter: " + item.Label);
            if (item.Label == "paste guide") Check(!compact.Contains("press", StringComparison.OrdinalIgnoreCase), "Positioning preview never tells the player to confirm Paste before the actual confirm step.");
            foreach (string concept in item.Concepts)
                Check(concept.Split('|').Any(option => Plain(compact).Contains(option, StringComparison.OrdinalIgnoreCase)), "Core action/concept survives: " + item.Label + " / " + concept);
            Check(Balanced(compact), "Inline emphasis is balanced: " + item.Label);
            Check(TutorialInstructionText.Compact(compact) == compact, "Repeated presentation is exactly idempotent: " + item.Label);
            Check(original == item.Original, "Formatting never mutates its input fixture: " + item.Label);
        }
        foreach (string custom in new[]
        {
            "", "  ", "My custom tutorial instruction.", "<b>Custom Wood Beam guidance</b>.",
            "Use the Selection Tool for the custom bridge; do not replace this copy.",
            "<b><#AA5500>Creator text</color></b> keeps whitespace.\n\nNext paragraph.",
            "Welcome to <b><#E09500>Build Mode!</color></b> This is a different authored introduction."
        })
        {
            Check(TutorialInstructionText.Compact(custom) == custom, "Unknown/custom/empty copy is an exact pass-through, never keyword-rewritten.");
            Check(TutorialInstructionText.Compact(TutorialInstructionText.Compact(custom)) == custom, "Custom copy remains stable after repeated presentation.");
        }
        Check(TutorialInstructionText.Compact(null) == null || TutorialInstructionText.Compact(null) == string.Empty, "Null display text is handled safely.");
        foreach (var entry in before) Check(Hash(Path.Combine(root, entry.Key)) == entry.Value, "Display calls cannot edit the authored scene/factory source: " + entry.Key);
        SourceGuards(root);
        Console.WriteLine($"PASS: {checks} production compact-instruction assertions. Exact stock-copy rewrites, key concepts, tool introductions, markup, custom passthrough, idempotence and runtime-only integration verified; no scene, asset, save or account changed.");
    }
    private static void SourceGuards(string root)
    {
        string helper = File.ReadAllText(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialInstructionText.cs"));
        foreach (string forbidden in new[] { "UnityEngine", "PlayerPrefs", "SaveGame", "PlayerDataManager", "OnStepStart", "ShowNextStep", "PlayTutorial", "AddListener", "RemoveListener", "SetActive", "AssetDatabase", "EditorUtility", "System.IO", "File.", "WriteAllText", "new TutorialStep", "screenPosition", "requiredAction" })
            Check(!helper.Contains(forbidden, StringComparison.Ordinal), "Text-only helper cannot change actions, progression, input or source files: " + forbidden);
        string manager = File.ReadAllText(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialManager.cs"));
        string show = Method(manager, "private void ShowTutorialStep(");
        int compact = show.IndexOf("TutorialInstructionText.Compact(step.message)", StringComparison.Ordinal);
        int layout = show.IndexOf("ApplyCenteredTutorialLayout(step.screenPosition)", StringComparison.Ordinal);
        int animate = show.IndexOf("AnimatePanel", StringComparison.Ordinal);
        Check(compact >= 0 && layout > compact && animate > layout, "Actual runtime text is compacted before measurement and entrance animation.");
        Check(show.Contains("step.OnStepStart?.Invoke()"), "Existing authored OnStepStart callbacks remain connected.");
        Check(show.Contains("nextButton.SetActive(step.showNextButton)"), "Continue visibility stays controlled by authored action gates.");
        Check(manager.Contains("trackedButton.onClick.AddListener(trackedButtonAction)"), "Real tool-click tracking remains connected.");
        string native = File.ReadAllText(Path.Combine(root, "Assets/Editor/TutorialCenterLayoutValidation.cs"));
        string validateMessage = Method(native, "private static void ValidateMessage(");
        int compactNative = validateMessage.IndexOf("message = TutorialInstructionText.Compact(message)", StringComparison.Ordinal);
        int assignNative = validateMessage.IndexOf("text.text = message", StringComparison.Ordinal);
        Check(compactNative >= 0 && assignNative > compactNative, "Native layout previews measure the same compacted presentation copy as gameplay.");
    }
    private static string Plain(string value) => Regex.Replace(Regex.Replace(value ?? string.Empty, "<[^>]+>", string.Empty), @"\s+", " ").Trim();
    private static bool Balanced(string value)
    {
        var stack = new Stack<string>();
        foreach (Match match in Regex.Matches(value, "<([^>]+)>"))
        {
            string tag = match.Groups[1].Value;
            if (tag.Equals("br", StringComparison.OrdinalIgnoreCase) || tag.EndsWith('/')) continue;
            if (tag.StartsWith('/'))
            {
                string close = tag[1..].ToLowerInvariant();
                if (stack.Count == 0 || stack.Pop() != close) return false;
            }
            else stack.Push(tag.StartsWith('#') || tag.StartsWith("color=", StringComparison.OrdinalIgnoreCase) ? "color" : tag.Split('=')[0].ToLowerInvariant());
        }
        return stack.Count == 0;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Method(string text, string signature)
    {
        int start = text.IndexOf(signature, StringComparison.Ordinal), depth = 0;
        if (start < 0) throw new InvalidOperationException("Production method missing: " + signature);
        int opening = text.IndexOf('{', start);
        for (int i = opening; i < text.Length; i++)
        { if (text[i] == '{') depth++; else if (text[i] == '}' && --depth == 0) return text[start..(i + 1)]; }
        throw new InvalidOperationException("Unterminated production method: " + signature);
    }
    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/Level/Tutorial/TutorialManager.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project not found.");
    }
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
}
