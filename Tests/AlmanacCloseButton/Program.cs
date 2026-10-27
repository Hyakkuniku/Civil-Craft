using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

internal static class Program
{
    private static int checks;
    private static void Check(bool success, string message) { checks++; if (!success) throw new InvalidOperationException(message); }
    private static GameObject Child(Transform parent, string name, params Type[] kinds)
    { var child = new GameObject(name, kinds); child.transform.SetParent(parent, false); return child; }
    private static bool ColorIs(Color value, Color32 expected) => Math.Abs(value.r - expected.r / 255f) < .0001f &&
        Math.Abs(value.g - expected.g / 255f) < .0001f && Math.Abs(value.b - expected.b / 255f) < .0001f && Math.Abs(value.a - expected.a / 255f) < .0001f;
    private static string Snapshot(Transform root) => root.gameObject.name + ":" + root.gameObject.activeSelf + ":" +
        root.gameObject.components.Count + ":" + string.Join("/", root.children.Select(Snapshot));
    private static void Main()
    {
        MissingAndDirectButton(); WrapperAndCallbacks(); WrapperRecognition(); MissingArtAndFont(); SourceGuards();
        Console.WriteLine($"PASS: {checks} production Almanac CLOSE styling/button-preservation assertions and integration guards. UI/TMP hierarchy is a boundary double; native rendering/hit tests remain separate. No scene, save, account, reader progression or network accessed.");
    }
    private static Button ButtonAt(Transform parent, string name = "Close_Button")
    {
        var owner = Child(parent, name, typeof(Image), typeof(Button));
        owner.transform.sizeDelta = new Vector2(160, 84);
        Image oldX = owner.GetComponent<Image>(); oldX.sprite = new Sprite { name = "Existing X artwork" }; oldX.color = Color.white;
        Button button = owner.GetComponent<Button>(); button.targetGraphic = oldX; return button;
    }
    private static void MissingAndDirectButton()
    {
        Check(AlmanacCloseButtonStyle.Apply(null, DialogueManager.Font) == null, "Missing close buttons fail safely without creating substitute controls.");
        var canvas = new GameObject("Book Canvas", typeof(Canvas)); canvas.transform.sizeDelta = new Vector2(1920, 1080);
        var page = Child(canvas.transform, "Large Page Panel", typeof(RawImage)); page.transform.sizeDelta = new Vector2(1800, 900);
        Texture paper = new() { name = "Book artwork" }; page.GetComponent<RawImage>().texture = paper;
        Button close = ButtonAt(page.transform); RectTransform originalRect = (RectTransform)close.transform;
        originalRect.localScale = new Vector3(2f, .5f, 1f); originalRect.localRotation = new Quaternion(0f, 0f, 1f, 0f);
        object originalNavigation = close.navigation; bool originalInteractable = close.interactable;
        RectTransform control = AlmanacCloseButtonStyle.Apply(close, DialogueManager.Font);
        Check(ReferenceEquals(control, originalRect), "A direct prefab button retains its own layout root, not the surrounding page.");
        Check(page.GetComponent<RawImage>().enabled && ReferenceEquals(page.GetComponent<RawImage>().texture, paper) && page.transform.sizeDelta == new Vector2(1800, 900),
            "Direct button styling never hides or resizes the large book artwork.");
        Check(ReferenceEquals(close.transform, originalRect) && close.transform.parent == page.transform && ReferenceEquals(close.navigation, originalNavigation) && close.interactable == originalInteractable,
            "Button identity, parent, keyboard navigation and interaction state remain unchanged.");
        Check(originalRect.localRotation == Quaternion.identity && originalRect.localScale == Vector3.one, "The previous upside-down/squished X transform cannot rotate or distort the CLOSE label.");
        VerifyStyle(close, control, DialogueManager.Font);
        string first = Snapshot(canvas.transform); AlmanacCloseButtonStyle.Apply(close, DialogueManager.Font);
        Check(Snapshot(canvas.transform) == first, "Repeated styling does not create duplicate labels, faces, shadows or buttons.");
        TMP_FontAsset replacement = new() { name = "Updated shared UI font" }; AlmanacCloseButtonStyle.Apply(close, replacement);
        Check(close.GetComponentsInChildren<TMP_Text>(true).Single(text => text.text == "CLOSE").font == replacement, "A later font import updates the existing CLOSE label instead of duplicating it.");
        close.interactable = false; AlmanacCloseButtonStyle.Apply(close, replacement);
        Check(!close.interactable, "Visual styling cannot unlock a close button disabled by the tutorial/gameplay owner.");
    }
    private static void WrapperAndCallbacks()
    {
        var canvas = new GameObject("Existing Almanac Canvas", typeof(Canvas)); canvas.transform.sizeDelta = new Vector2(1920, 1080);
        AlmanacManager book = canvas.AddComponent<AlmanacManager>(); book.almanacCanvas = canvas;
        var wrapper = Child(canvas.transform, "Existing Close Artwork Wrapper", typeof(RawImage)); wrapper.transform.sizeDelta = new Vector2(160, 84);
        wrapper.transform.localScale = new Vector3(.5f, 2f, 1f); wrapper.transform.localRotation = new Quaternion(0f, 0f, 1f, 0f);
        Texture oval = new() { name = "Existing oval background" }; wrapper.GetComponent<RawImage>().texture = oval;
        Button close = ButtonAt(wrapper.transform);
        ((RectTransform)close.transform).anchorMin = Vector2.zero; ((RectTransform)close.transform).anchorMax = Vector2.one;
        ((RectTransform)close.transform).sizeDelta = Vector2.zero;
        int extraPersistent = 0, extraRuntime = 0;
        close.onClick.persistent.Add((book, "CloseAlmanac", book.CloseAlmanac));
        close.onClick.persistent.Add((new object(), "OtherAuthoredCallback", () => extraPersistent++));
        close.onClick.AddListener(() => extraRuntime++);
        var originalEvent = close.onClick; var originalRect = close.transform;
        var pointerTarget = close.transform; var persistentBefore = close.onClick.persistent.ToArray(); var runtimeBefore = close.onClick.runtime.ToArray();
        canvas.SetActive(false);
        RectTransform control = AlmanacCloseButtonStyle.Apply(close, DialogueManager.Font);
        Check(control == wrapper.transform, "The small stretched single-button RawImage wrapper is the real layout root.");
        Check(!wrapper.GetComponent<RawImage>().enabled && ReferenceEquals(wrapper.GetComponent<RawImage>().texture, oval), "The old oval is hidden without deleting or changing the source texture.");
        Check(!canvas.activeSelf && close.gameObject.activeSelf, "Preparing CLOSE does not open the book or change the authored button activation.");
        Check(ReferenceEquals(close.transform, originalRect) && ReferenceEquals(pointerTarget, close.transform) && close.transform.parent == wrapper.transform,
            "All authored tutorial close-pointer references keep the same Button RectTransform and hierarchy.");
        Check(ReferenceEquals(close.onClick, originalEvent) && close.onClick.persistent.SequenceEqual(persistentBefore) && close.onClick.runtime.SequenceEqual(runtimeBefore),
            "All persistent/dynamic close callbacks and their exact event object are preserved.");
        Check(book.closes == 0 && extraPersistent == 0 && extraRuntime == 0, "Styling alone cannot close the book, complete a step or invoke callbacks.");
        VerifyStyle(close, control, DialogueManager.Font);
        canvas.SetActive(true); close.Click();
        Check(book.closes == 1 && extraPersistent == 1 && extraRuntime == 1, "One ordinary tap invokes the original CloseAlmanac action and extra listeners exactly once.");
        string before = Snapshot(canvas.transform); AlmanacCloseButtonStyle.Apply(close, DialogueManager.Font); close.Click();
        Check(book.closes == 2 && extraPersistent == 2 && extraRuntime == 2 && Snapshot(canvas.transform) == before,
            "Reapplying style cannot duplicate click listeners or layer objects.");
        Check(wrapper.transform.anchorMin == Vector2.one && wrapper.transform.anchorMax == Vector2.one && wrapper.transform.pivot == Vector2.one,
            "The styled close control remains anchored at the book's upper-right edge.");
        Check(wrapper.transform.localRotation == Quaternion.identity && wrapper.transform.localScale == Vector3.one,
            "The old wrapper's nonuniform scale and rotation cannot distort the new rectangular control.");
        Check(((RectTransform)close.transform).anchorMin == Vector2.zero && ((RectTransform)close.transform).anchorMax == Vector2.one &&
            ((RectTransform)close.transform).sizeDelta == Vector2.zero, "The original nested close hit target stretches over the new full rectangular button.");
    }
    private static void WrapperRecognition()
    {
        foreach (string shape in new[] { "large", "extra-child", "fixed-child", "no-raw", "stretch-parent", "zero-width", "negative-height", "too-wide", "too-tall" })
        {
            var canvas = new GameObject("Book");
            var parent = Child(canvas.transform, "Potential wrapper", shape == "no-raw" ? Array.Empty<Type>() : new[] { typeof(RawImage) });
            parent.transform.sizeDelta = shape == "large" ? new Vector2(900, 600) : new Vector2(160, 84);
            if (shape == "stretch-parent") parent.transform.anchorMax = Vector2.one;
            if (shape == "zero-width") parent.transform.sizeDelta = new Vector2(0, 84);
            if (shape == "negative-height") parent.transform.sizeDelta = new Vector2(160, -1);
            if (shape == "too-wide") parent.transform.sizeDelta = new Vector2(321, 84);
            if (shape == "too-tall") parent.transform.sizeDelta = new Vector2(160, 201);
            Button button = ButtonAt(parent.transform);
            ((RectTransform)button.transform).anchorMin = Vector2.zero;
            ((RectTransform)button.transform).anchorMax = shape == "fixed-child" ? Vector2.zero : Vector2.one;
            if (shape != "fixed-child") ((RectTransform)button.transform).sizeDelta = Vector2.zero;
            if (shape == "extra-child") Child(parent.transform, "Unrelated content", typeof(Image));
            Vector2 size = parent.transform.sizeDelta;
            Check(AlmanacCloseButtonStyle.Apply(button, DialogueManager.Font) == button.transform, "Non-wrapper layouts must never resize or hide another panel: " + shape);
            Check(parent.transform.sizeDelta == size && (parent.GetComponent<RawImage>() == null || parent.GetComponent<RawImage>().enabled),
                "Unrecognized parent artwork stays unchanged: " + shape);
        }
    }
    private static void MissingArtAndFont()
    {
        var page = new GameObject("Book Panel"); page.transform.sizeDelta = new Vector2(1800, 900);
        var root = Child(page.transform, "Existing Close Without Image", typeof(Button));
        Button close = root.GetComponent<Button>(); var originalEvent = close.onClick;
        Check(AlmanacCloseButtonStyle.Apply(close, null) == root.transform && root.GetComponent<Image>() != null &&
            ReferenceEquals(close.onClick, originalEvent), "A missing X graphic is repaired on the same Button without replacing callbacks or requiring a font import.");
        TextMeshProUGUI label = close.GetComponentsInChildren<TextMeshProUGUI>(true).Single(text => text.text == "CLOSE");
        label.font = DialogueManager.Font; AlmanacCloseButtonStyle.Apply(close, null);
        Check(label.font == DialogueManager.Font, "A temporarily unavailable font resource cannot clear the label's existing usable typeface.");
        Check(close.GetComponentsInChildren<Shadow>(true).Length == 1 && close.GetComponentsInChildren<Button>(true).Length == 1,
            "Missing-art/repeated setup still has one shadow and one original Button, never an extra interactive overlay.");
    }
    private static void VerifyStyle(Button button, RectTransform control, TMP_FontAsset font)
    {
        Check(control.sizeDelta == new Vector2(190, 76) && control.anchoredPosition == new Vector2(-18, -12), "CLOSE matches SKIP's readable 190×76 silhouette with an upper-right inset.");
        Image background = button.GetComponent<Image>();
        Check(background != null && background.sprite == DialogueManager.Rounded && background.type == Image.Type.Sliced &&
            ColorIs(background.color, new Color32(145, 84, 48, 255)), "The X artwork becomes the shared rounded brown SKIP frame.");
        Check(ReferenceEquals(button.targetGraphic, background) && background.raycastTarget, "The same full Button graphic remains the only close hit target.");
        Image face = button.GetComponentsInChildren<Image>(true).Single(image => image != background && ColorIs(image.color, new Color32(239, 216, 167, 255)));
        Check(face.sprite == DialogueManager.Rounded && !face.raycastTarget && face.rectTransform.offsetMin == new Vector2(5, 8) && face.rectTransform.offsetMax == new Vector2(-5, -5),
            "The cream inner face uses the same inset and rounded sprite as SKIP and cannot intercept taps.");
        TMP_Text label = button.GetComponentsInChildren<TMP_Text>(true).Single(text => text.text == "CLOSE");
        Check(label.font == font && label.fontSize == 32 && label.fontSizeMin == 22 && label.fontSizeMax == 32 && label.enableAutoSizing &&
            label.fontStyle == FontStyles.Bold && label.alignment == TextAlignmentOptions.Center && !label.raycastTarget && ColorIs(label.color, new Color32(132, 74, 43, 255)),
            "The explicit CLOSE label keeps SKIP typography/color and stays click-through.");
        Shadow shadow = button.GetComponent<Shadow>();
        Check(shadow != null && ColorIs(shadow.effectColor, new Color32(103, 58, 35, 180)) && shadow.effectDistance == new Vector2(0, -5), "The rectangular close control matches SKIP's shadow.");
        Check(button.colors.fadeDuration == .08f && ColorIs(button.colors.normalColor, new Color32(255, 255, 255, 255)) &&
            ColorIs(button.colors.pressedColor, new Color32(229, 202, 151, 255)), "Existing Button feedback is styled with the shared SKIP colors.");
    }
    private static void SourceGuards()
    {
        string root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "Assets/Script/UI/Almanac/AlmanacManager.cs"))) root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException();
        string helper = File.ReadAllText(Path.Combine(root, "Assets/Script/UI/Almanac/AlmanacCloseButtonStyle.cs"));
        var helperTree = CSharpSyntaxTree.ParseText(helper).GetRoot();
        foreach (string forbidden in new[] { "TutorialHoldToSkip", "IPointerDownHandler", "PlayerDataManager", "SaveGame", "CompleteLesson", "CloseAlmanac(", "LoadScene", "AssetDatabase", "FindObjectsOfType" })
            Check(!helper.Contains(forbidden), "Button styling cannot own hold gestures, scene searches, closing or gameplay state: " + forbidden);
        Check(!helperTree.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call => call.Expression.ToString().Contains("onClick", StringComparison.Ordinal)),
            "The style helper cannot add/remove/invoke any existing click listeners.");
        string managerPath = "Assets/Script/UI/Almanac/AlmanacManager.cs";
        string current = File.ReadAllText(Path.Combine(root, managerPath));
        var currentType = CSharpSyntaxTree.ParseText(current).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "AlmanacManager");
        string baseline;
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--no-optional-locks"); start.ArgumentList.Add("show"); start.ArgumentList.Add("HEAD:" + managerPath);
        using (Process git = Process.Start(start))
        { baseline = git.StandardOutput.ReadToEnd(); string error = git.StandardError.ReadToEnd(); git.WaitForExit(); Check(git.ExitCode == 0, "Read-only baseline source must be available: " + error); }
        var baselineType = CSharpSyntaxTree.ParseText(baseline).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == "AlmanacManager");
        foreach (string name in new[] { "CloseAlmanac", "CloseAlmanacThen", "CloseAlmanacRoutine", "IsRequiredFirstOpenTutorialBlockingClose", "IsTabUnlockTutorialBlockingClose" })
        {
            MethodDeclarationSyntax Method(ClassDeclarationSyntax type) => type.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == name);
            Check(SyntaxFactory.AreEquivalent(Method(currentType), Method(baselineType)), "Existing reader/tutorial/quiz-close behavior must remain syntax-equivalent apart from trivia: " + name);
        }
        var ensure = currentType.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "EnsureCloseButtonStyle").ToFullString();
        Check(ensure.Contains("CloseAlmanac") && ensure.Contains("GetPersistentTarget") && ensure.Contains("GetPersistentMethodName") && ensure.Contains("AlmanacCloseButtonStyle.Apply"),
            "Runtime integration styles only the existing close callback owned by this Almanac.");
        var awake = currentType.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Awake").ToFullString();
        Check(awake.IndexOf("InitializeBook()", StringComparison.Ordinal) < awake.IndexOf("EnsureCloseButtonStyle()", StringComparison.Ordinal),
            "The existing book is initialized before styling its authored close control.");
        foreach (var method in currentType.Members.OfType<MethodDeclarationSyntax>().Where(method => method.Identifier.ValueText is "Update" or "LateUpdate"))
            Check(!method.ToFullString().Contains("EnsureCloseButtonStyle()"), "No per-frame scene/hierarchy restyling is introduced.");
    }
}
