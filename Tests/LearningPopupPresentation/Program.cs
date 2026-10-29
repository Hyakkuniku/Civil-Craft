using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string root = Environment.CurrentDirectory;
while (!File.Exists(Path.Combine(root,"ProjectSettings/ProjectVersion.txt"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Run from the Unity project root.");
string Read(string path) => File.ReadAllText(Path.Combine(root,path));
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
int checks = 0;
void Check(bool value,string reason) { checks++; if (!value) throw new InvalidOperationException(reason); }
Dictionary<string,string> Docs(string source) => Regex.Matches(source,@"(?ms)^--- !u!\d+ &(\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)").Cast<Match>().ToDictionary(m=>m.Groups[1].Value,m=>m.Value.Replace("\r\n","\n"));
using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Baseline.json")));
foreach (var entry in baseline.RootElement.GetProperty("Assets").EnumerateObject())
    Check(Hash(File.ReadAllBytes(Path.Combine(root,entry.Name))) == entry.Value.GetString(),"A protected lesson/quiz/contract/material asset changed: " + entry.Name);
foreach (var scene in baseline.RootElement.GetProperty("Scenes").EnumerateObject())
{
    var docs = Docs(Read(scene.Name));
    foreach (var entry in scene.Value.EnumerateObject())
        Check(docs.TryGetValue(entry.Name,out string value) && Hash(Encoding.UTF8.GetBytes(value)) == entry.Value.GetString(),
            "An existing story, contract, tutorial or simulation assignment changed: " + scene.Name + " / " + entry.Name);
}
string layout = Read("Assets/Script/UI/LearningPopupLayout.cs");
foreach (string forbidden in new[] { "new GameObject", "AddComponent<", "FindObjectOfType", "FindObjectsOfType", "Instantiate(", "CompleteContract(", "Discover(", "UnlockFeature(" })
    Check(!layout.Contains(forbidden),"Responsive layout must fit authored UI without rebuilding controls or progressing gameplay: " + forbidden);
foreach (string required in new[] { "Screen.safeArea", "rootCanvas.scaleFactor", "44f / pixelScale", "14f / pixelScale", "preserveAspect = true", "TextOverflowModes.Overflow", "descriptionScroll.viewport", "descriptionScroll.content", "verticalNormalizedPosition = 1f" })
    Check(layout.Contains(required),"Adaptive text, touch or scrolling guard missing: " + required);
Check(layout.Contains("available.width < available.height"),"Portrait readers must stack image and description.");
Check(layout.Contains("hasImage && !stacked") && layout.Contains("imageFrame.gameObject.SetActive(hasImage)"),"No-image readers must reclaim the picture area.");
Check(!layout.Contains("TextOverflowModes.Ellipsis") && !layout.Contains("TextOverflowModes.Truncate"),"Long learning text cannot be silently truncated.");
string item = Read("Assets/Script/UI/ItemUnlockUI.cs"), lesson = Read("Assets/Script/UI/Lessons/LessonUIManager.cs");
Check(item.Contains("materialPopup.dismissButton.onClick.AddListener(OnCollectClicked)") && item.Contains("materialPopup.dismissButton.onClick.RemoveListener(OnCollectClicked)"),"Material acknowledgement retains its paired binding/cleanup.");
Check(item.Contains("MaterialDiscoverySaveManager.Discover(pendingMaterialToDiscover)") && item.Contains("callback?.Invoke()") && item.Contains("rewardVisible = false;"),"Acknowledgement must preserve discovery and the existing guarded continuation callback.");
Check(item.Contains("pendingRewards.Enqueue") && item.Contains("if (pendingRewards.Count > 0)"),"Stacked introductions/rewards retain their queue.");
Check(lesson.Contains("closeButton.onClick.AddListener(CloseLesson)") && lesson.Contains("closeButton.onClick.RemoveListener(CloseLesson)"),"The existing lesson close action is retained.");
Check(lesson.Contains("LessonClosed?.Invoke(closedLesson)") && lesson.Contains("RestorePlayerControls()") && lesson.Contains("RestoreHiddenCanvases()"),"Closing a lesson retains story notification and control/HUD restoration.");
string validation = Read("Assets/Editor/LearningPopupPresentationValidation.cs");
foreach (string guard in new[] { "PlayerDataManager.Instance == saves", "SceneManager.sceneCount == before.Length", "hashes.All", "before[i].isDirty == dirty[i]", "originalComponents", "isTextTruncated", "scrollRange > 10f", "43.9f" })
    Check(validation.Contains(guard),"Native presentation verification guard missing: " + guard);
Check(!validation.Contains(".onClick.Invoke("),"Rendering validation must never invoke gameplay buttons.");
Console.WriteLine($"PASS: {checks} frozen data/story preservation and authored reader integration assertions. Native phone/tablet rendering, full text, scrolling and button targets are checked separately in Unity.");
