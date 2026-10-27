using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

int checks = 0;
void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
bool Near(float a, float b) => Math.Abs(a - b) < .002f;
var slot = new AlmanacTutorialSkipSlot();
foreach (float scale in new[] { .5f, 1f, 1.35f, 2f })
foreach (var pixels in new[] { new Rect(985, 630, 190, 76), new Rect(1710, 975, 190, 76), new Rect(760, 1210, 230, 92) })
{
    var parent = new RectTransform { canvas = new Canvas(), pixelsPerUnit = scale, screenOrigin = new Vector2(31, 17), rect = new Rect(-700, -340, 1920, 1080) };
    var skip = new RectTransform { parent = parent, canvas = parent.canvas, anchorMin = Vector2.one, anchorMax = Vector2.one,
        pivot = Vector2.one, anchoredPosition = new Vector2(-36, -28), sizeDelta = new Vector2(190, 76), localScale = new Vector3(.8f, .8f, 1) };
    var close = new RectTransform { canvas = new Canvas(), screenRect = pixels };
    var group = close.gameObject.AddComponent<CanvasGroup>(); group.alpha = .37f; group.interactable = false; group.ignoreParentGroups = true;
    Check(slot.TryPlace(skip, close), "Valid overlay slot must project across phone/tablet Canvas scales.");
    Vector2 center = (skip.anchoredPosition + parent.rect.center) * scale + parent.screenOrigin;
    Check(Near(center.x, pixels.center.x) && Near(center.y, pixels.center.y) && Near(skip.sizeDelta.x * scale, pixels.width) && Near(skip.sizeDelta.y * scale, pixels.height),
        "SKIP matches CLOSE's actual screen bounds, including a noncenter parent pivot and safe-area offset.");
    Check(!group.interactable && !group.blocksRaycasts && group.alpha == 0 && close.gameObject.activeInHierarchy,
        "CLOSE is hidden/blocked without disabling its target or changing hierarchy state.");
    close.screenRect = new Rect(pixels.x - 13, pixels.y - 7, pixels.width + 4, pixels.height + 2);
    Check(slot.TryPlace(skip, close), "A moving/resizing book refreshes its slot without reacquiring the saved state.");
    Check(slot.Release() && Near(group.alpha, .37f) && !group.interactable && group.blocksRaycasts && group.ignoreParentGroups,
        "Release restores the exact prior CLOSE group settings.");
    Check(Near(skip.anchoredPosition.x, -36) && Near(skip.anchoredPosition.y, -28) && Near(skip.localScale.x, .8f) &&
        Near(skip.sizeDelta.x, 190) && Near(skip.anchorMin.x, 1) && Near(skip.pivot.x, 1), "Release restores the original global SKIP layout.");
    Check(!slot.Release(), "Repeated teardown is idempotent.");
}
var destination = new RectTransform { canvas = new Canvas() };
var target = new RectTransform { parent = destination, canvas = destination.canvas };
var firstClose = new RectTransform { canvas = new Canvas() };
var secondClose = new RectTransform { canvas = new Canvas() };
Check(slot.TryPlace(target, firstClose) && slot.TryPlace(target, secondClose), "Changing book controls rebinds the slot safely.");
Check(firstClose.GetComponent<CanvasGroup>().alpha == 1 && secondClose.GetComponent<CanvasGroup>().alpha == 0,
    "Changing controls restores the previous CLOSE before hiding the new one.");
slot.Release();
secondClose.canvas.renderMode = RenderMode.ScreenSpaceCamera;
Check(!slot.TryPlace(target, secondClose) && secondClose.GetComponent<CanvasGroup>().alpha == 1,
    "A missing configured camera cannot conceal CLOSE using an unproven placement.");
secondClose.canvas.worldCamera = new Camera();
Check(slot.TryPlace(target, secondClose), "A configured camera is accepted without a scene camera search."); slot.Release();
secondClose.screenRect = new Rect(float.NaN, 0, 190, 76);
Check(!slot.TryPlace(target, secondClose) && secondClose.GetComponent<CanvasGroup>().alpha == 1,
    "Invalid projection cannot hide the usable CLOSE control.");

string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialManager.cs"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Unity root not found.");
var manager = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "Assets/Script/Level/Tutorial/TutorialManager.cs")))
    .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(item => item.Identifier.ValueText == "TutorialManager");
string Method(string name) => manager.Members.OfType<MethodDeclarationSyntax>().Single(item => item.Identifier.ValueText == name).ToFullString();
string integration = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Integration.cs.txt"))
    .Replace("// ACTUAL_MEMBERS", Method("RefreshTutorialSkipSlot") + Method("ReleaseTutorialSkipSlot"));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToList();
references.Add(MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location));
var compilation = CSharpCompilation.Create("AlmanacSkipSlotIntegration_" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(integration) }, references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream(); var emitted = compilation.Emit(output);
if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
try { checks += (int)Assembly.Load(output.ToArray()).GetType("IntegrationScenarios").GetMethod("Run").Invoke(null, null); }
catch (TargetInvocationException error) { throw new Exception("Actual tutorial SKIP ownership policy failed.", error.InnerException); }
foreach (string name in new[] { "CompleteTutorial", "SuspendCurrentTutorial", "RestartTutorial", "OnDisable", "OnDestroy" })
    Check(Method(name).Contains("ReleaseTutorialSkipSlot();"), "Every completion/suspension/restart/disable/teardown path releases the shared CLOSE slot: " + name);
Check(!Method("RefreshTutorialSkipSlot").Contains("FindObject"), "Per-frame slot ownership cannot search the scene.");
Console.WriteLine($"PASS: {checks} actual helper/manager ownership, cross-Canvas placement, prior-state restoration and lifecycle checks. Engine projection/Canvas objects and book ownership are boundary doubles; no live UI or player data touched.");
