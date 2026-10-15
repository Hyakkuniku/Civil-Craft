int checks = 0;
void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
var bubble = new PlayerChatBubbleMessage();
Check(!bubble.IsVisible(0) && bubble.Alpha(0) == 0, "Initially empty");
bubble.Show("hello", 10);
Check(bubble.IsVisible(10) && bubble.Text == "hello" && bubble.Alpha(10) == 1, "New message");
Check(bubble.IsVisible(15) && bubble.Alpha(15) == 1, "Five seconds solid");
Check(bubble.IsVisible(15.5f) && bubble.Alpha(15.5f) == .5f, "Last second fades");
Check(!bubble.IsVisible(16) && bubble.Alpha(16) == 0, "Expires exactly at six seconds");
bubble.Show("first", 20); bubble.Show("second", 22);
Check(bubble.Text == "second" && bubble.IsVisible(27) && !bubble.IsVisible(28), "Replacement resets lifetime");
bubble.Clear(); Check(bubble.Text == "" && !bubble.IsVisible(22), "Despawn clears state");
foreach (string empty in new string[] { null, "", " \r\n\t", "\0" })
{ bubble.Show(empty, 0); Check(!bubble.IsVisible(0), "Blank hidden"); }
bubble.Show(new string('x', 500), 0); Check(bubble.Text.Length == 280, "Limit shared with chat");
bubble.Show(new string('x', 279) + "😀", 0); Check(bubble.Text.Length == 279, "Surrogate limit");
bubble.Show("Hello 😀\nfriend\t!\0", 0); Check(bubble.Text == "Hello 😀 friend !", "Normalization");
bubble.Show("<size=999>hello</size>", 0); Check(bubble.Text == "<size=999>hello</size>", "Literal payload retained");
var other = new PlayerChatBubbleMessage(); other.Show("other player", 0);
Check(other.Text != bubble.Text, "Per-player independence");
var history = new SessionChatHistory();
Check(history.Add(1, "Same name", "one") && !history.Add(1, "Same name", "duplicate"), "History sequence regression");
Check(history.Add(2, "Same name", "two") && history.Count == 2, "Duplicate names do not merge history");
string root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root, "Assets"))) root = Directory.GetParent(root)!.FullName;
string network = File.ReadAllText(Path.Combine(root, "Assets/Script/FusionMultiplayerAvatar.Chat.cs"));
string ui = File.ReadAllText(Path.Combine(root, "Assets/Script/PlayerChatBubble.cs"));
Check(network.Contains("Object.InputAuthority, NormalizeMapPlayerName(MapPlayerName), body"), "Server selects sender");
Check(network.Contains("TryGetPlayerObject(sender") && network.Contains("sequence <= lastDeliveredChatSequence"), "No name-based routing or duplicate delivery");
Check(network.Contains("RpcSources.InputAuthority, RpcTargets.StateAuthority") && network.Contains("RpcSources.StateAuthority, RpcTargets.All"), "Authority retained");
Check(network.Contains("ChatMessageReceived?.Invoke(Runner, sequence, name, message)"), "Existing history event unchanged");
Check(ui.Contains("label.richText = false") && ui.Contains("label.parseCtrlCharacters = false"), "Plain text only");
Check(!ui.Contains("AddComponent<GraphicRaycaster>") && ui.Contains("group.blocksRaycasts = false"), "Click-through");
Check(ui.Contains("OverlapsVisibleReservation") && ui.Contains("camera.targetTexture != null"), "Map exclusion");
Check(ui.Contains("Time.unscaledTime") && ui.Contains("OnDisable() => Clear()"), "Pause-independent timeout and cleanup");
Check(ui.Contains("30f * 30f") && ui.Contains("screen.z <= 0f"), "Distance and behind-camera bounds");
Console.WriteLine($"PASS: {checks} chat bubble state and integration checks. Live two-player delivery still requires Unity/Fusion testing.");
