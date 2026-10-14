using System.Text.RegularExpressions;

internal static partial class Program
{
    private static void TestSavedSceneHierarchy()
    {
        var scene = new SceneDocuments(Read("Assets/Scenes/Main Menu.unity"));
        string metadata = Read("Assets/Script/UI/MainMenuUIController.cs.meta");
        string controllerGuid = Regex.Match(metadata, @"(?m)^guid: ([a-f0-9]{32})\r?$").Groups[1].Value;
        Check(controllerGuid.Length == 32, "Original menu controller has valid Unity metadata.");
        string controller = scene.SingleDocument(114,
            body => body.Contains("guid: " + controllerGuid, StringComparison.Ordinal), "original menu controller");
        string dropdownId = SceneDocuments.Reference(controller, "dropdownPanel");
        string dropdown = scene.Document(dropdownId);
        string dropdownObjectId = SceneDocuments.Reference(dropdown, "m_GameObject");
        Check(SceneDocuments.Name(scene.Document(dropdownObjectId)) == "MainMenuDropdownPanel",
            "Saved controller still points to the authored original dropdown.");

        string heroObject = scene.SingleDocument(1,
            body => SceneDocuments.Name(body) == "MainMenuHeroBoard", "hero board");
        string heroObjectId = scene.SingleId(1,
            body => SceneDocuments.Name(body) == "MainMenuHeroBoard", "hero board");
        string heroTransform = scene.SingleTransformFor(heroObjectId);
        string heroParentId = SceneDocuments.Reference(heroTransform, "m_Father");
        string dropdownParentId = SceneDocuments.Reference(dropdown, "m_Father");
        Check(heroParentId != "0" && heroParentId == dropdownParentId,
            "Saved hero and original dropdown share the same MainPanel parent, above the existing container input layer.");
        Check(SceneDocuments.Name(scene.Document(SceneDocuments.Reference(scene.Document(dropdownParentId), "m_GameObject"))) == "MainPanel",
            "The existing dropdown branch remains under MainPanel, not reparented directly onto the root Canvas.");
        Check(SceneDocuments.Scalar(heroObject, "m_IsActive") == "0",
            "Old paper hero board is kept recoverable but inactive; its paper/tape cannot render behind the centered intro.");

        var ancestors = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string currentId = heroParentId;
        while (currentId != "0")
        {
            if (!visited.Add(currentId)) throw new InvalidDataException("Saved menu hierarchy has a transform cycle.");
            string transform = scene.Document(currentId);
            ancestors.Add(SceneDocuments.Name(scene.Document(SceneDocuments.Reference(transform, "m_GameObject"))));
            currentId = SceneDocuments.Reference(transform, "m_Father");
            if (visited.Count > 128) throw new InvalidDataException("Saved menu transform ancestry exceeds 128 levels.");
        }
        Check(ancestors.Count >= 3 && ancestors[0] == "MainPanel" && ancestors[1] == "Container" && ancestors.Contains("MainMenuCanvas"),
            "Hero preserves MainPanel → Container → MainMenuCanvas ancestry instead of sitting behind the fullscreen input backdrop.");

        string presentationGuid = Regex.Match(Read("Assets/Script/UI/MainMenuPresentationLayout.cs.meta"),
            @"(?m)^guid: ([a-f0-9]{32})\r?$").Groups[1].Value;
        Check(presentationGuid.Length == 32, "Presentation component has valid Unity metadata.");
        string layout = scene.SingleDocument(114,
            body => body.Contains("guid: " + presentationGuid, StringComparison.Ordinal), "presentation layout");
        string heroTransformId = scene.SingleTransformIdFor(heroObjectId);
        Check(SceneDocuments.Reference(layout, "splashBoard") == heroTransformId &&
            SceneDocuments.Reference(layout, "dropdownPanel") == dropdownId,
            "Saved layout uses the actual hero and original dropdown, not detached fixture references.");

        Check(SceneDocuments.OptionalScalar(layout, "centeredIntro") == "1", "Saved menu uses the centered, container-free first screen.");
        foreach (string field in new[] { "title", "edition", "playButton" })
        {
            string transform = scene.Document(SceneDocuments.Reference(layout, field));
            string itemObject = scene.Document(SceneDocuments.Reference(transform, "m_GameObject"));
            Check(SceneDocuments.Reference(transform, "m_Father") == dropdownParentId,
                $"Saved centered {field} is a direct MainPanel sibling, not nested in the old paper container.");
            Check(SceneDocuments.Scalar(itemObject, "m_IsActive") == "1", $"Saved centered {field} starts active.");
            if (field != "playButton")
            {
                string[] text = scene.TextDocuments(new HashSet<string>(new[] { SceneDocuments.Reference(transform, "m_GameObject") }, StringComparer.Ordinal)).ToArray();
                Check(text.Length == 1 && SceneDocuments.Scalar(text[0], "m_Enabled") == "1",
                    $"Saved centered {field} has one enabled text component.");
                if (text.Length == 1)
                    Check(SceneDocuments.Text(text[0]) == (field == "title" ? "Civil Craft" : "bridge edition"),
                        $"Saved centered {field} uses its original Civil Craft/bridge edition caption.");
            }
        }

        VerifyPrimaryLabels(scene, layout);
    }

    private static void VerifyPrimaryLabels(SceneDocuments scene, string layout)
    {
        var secondaryNames = new HashSet<string>(new[] { "Tile Caption", "Tile Kicker", "Unlock Hint" }, StringComparer.Ordinal);
        foreach ((string field, string caption) in new[]
        {
            ("playButton", SceneDocuments.OptionalScalar(layout, "centeredIntro") == "1" ? "Press To Play" : "PLAY"), ("modeButton", "PLAY & EXPLORE"),
            ("achievementButton", "ACHIEVEMENTS"), ("storeButton", "STORE"),
            ("settingsButton", "SETTINGS"), ("loginButton", "ACCOUNT"),
            ("creditsButton", "CREDITS"), ("backButton", "BACK"), ("quitButton", "QUIT")
        })
        {
            string buttonTransformId = SceneDocuments.Reference(layout, field);
            HashSet<string> descendantIds = scene.DescendantGameObjectIds(buttonTransformId);
            string[] primary = scene.TextDocuments(descendantIds)
                .Where(text => !secondaryNames.Contains(SceneDocuments.Name(scene.Document(SceneDocuments.Reference(text, "m_GameObject")))))
                .Where(text => SceneDocuments.Scalar(text, "m_Enabled") == "1" &&
                    SceneDocuments.Scalar(scene.Document(SceneDocuments.Reference(text, "m_GameObject")), "m_IsActive") == "1")
                .ToArray();
            Check(primary.Length == 1, $"Saved {field} has exactly one enabled, active primary label, excluding secondary captions/kickers.");
            if (primary.Length != 1) continue;
            Check(SceneDocuments.Text(primary[0]) == caption,
                $"Saved {field} primary label reads {caption}, not an inactive/secondary caption selected during repeat authoring.");
            float maximum = float.Parse(SceneDocuments.Scalar(primary[0], "m_fontSizeMax"), System.Globalization.CultureInfo.InvariantCulture);
            float minimum = float.Parse(SceneDocuments.Scalar(primary[0], "m_fontSizeMin"), System.Globalization.CultureInfo.InvariantCulture);
            Check(float.IsFinite(maximum) && float.IsFinite(minimum) && maximum >= minimum && minimum > 0f,
                $"Saved {field} primary label has valid positive font sizing.");
            if (field == "quitButton")
            {
                string color = SceneDocuments.Scalar(primary[0], "m_fontColor");
                float Channel(string name) => float.Parse(Regex.Match(color,
                    @"\b" + name + @": ([0-9eE+.-]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float red = Channel("r"), green = Channel("g"), blue = Channel("b"), alpha = Channel("a");
                Check(red >= .6f && red > green * 2f && red > blue * 2f && alpha >= .95f,
                    "Saved Quit primary TEXT is opaque red, not only its button/background.");
                Check(Math.Abs(red - 193f / 255f) < .002f && Math.Abs(green - 45f / 255f) < .002f &&
                    Math.Abs(blue - 45f / 255f) < .002f,
                    "Saved Quit primary text uses the requested C12D2D red.");
            }
            if (field == "playButton")
            {
                string color = SceneDocuments.Scalar(primary[0], "m_fontColor");
                float Channel(string name) => float.Parse(Regex.Match(color,
                    @"\b" + name + @": ([0-9eE+.-]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                Check(new[] { "r", "g", "b", "a" }.All(channel => Channel(channel) >= .99f),
                    "Centered Press To Play text stays plain white rather than a gold card caption.");
                string buttonObjectId = SceneDocuments.Reference(scene.Document(buttonTransformId), "m_GameObject");
                string[] images = scene.ImageDocuments(buttonObjectId).ToArray();
                Check(images.Length == 1 && SceneDocuments.Scalar(images[0], "m_Enabled") == "1" &&
                    SceneDocuments.Scalar(images[0], "m_RaycastTarget") == "1" &&
                    Regex.IsMatch(SceneDocuments.Scalar(images[0], "m_Color"), @"\ba: 0(?:\.0+)?\}"),
                    "Centered Play has one transparent bounded raycast surface, not a visible background/container.");
                Check(descendantIds.SelectMany(scene.ImageDocuments).All(image =>
                    SceneDocuments.Scalar(image, "m_Enabled") == "0" ||
                    float.Parse(Regex.Match(SceneDocuments.Scalar(image, "m_Color"), @"\ba: ([0-9eE+.-]+)")
                        .Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) <= .001f),
                    "Centered Play retains no enabled opaque child face, border or decorative card background.");
            }
            if (field == "modeButton")
            {
                string[] secondary = scene.TextDocuments(descendantIds)
                    .Where(text => secondaryNames.Contains(SceneDocuments.Name(scene.Document(SceneDocuments.Reference(text, "m_GameObject")))))
                    .Where(text => SceneDocuments.Scalar(text, "m_Enabled") == "1").ToArray();
                Check(secondary.All(text => maximum > float.Parse(SceneDocuments.Scalar(text, "m_fontSizeMax"),
                    System.Globalization.CultureInfo.InvariantCulture)),
                    "Saved Play & Explore primary text remains larger than its decorative detail captions.");
            }
        }
    }

    // Index by document offsets, not hard-coded Unity file IDs or repeated full
    // scene scans. Only the requested small YAML documents are materialized.
    private sealed class SceneDocuments
    {
        private readonly string source;
        private readonly Dictionary<string, Entry> documents = new(StringComparer.Ordinal);
        private readonly record struct Entry(int Type, int Start, int Length);

        public SceneDocuments(string source)
        {
            this.source = source;
            MatchCollection headers = Regex.Matches(source, @"(?m)^--- !u!(\d+) &(-?\d+)(?: stripped)?\r?$");
            for (int index = 0; index < headers.Count; index++)
            {
                Match header = headers[index];
                int end = index + 1 < headers.Count ? headers[index + 1].Index : source.Length;
                documents.Add(header.Groups[2].Value, new Entry(int.Parse(header.Groups[1].Value), header.Index, end - header.Index));
            }
        }
        public string Document(string id)
        {
            if (!documents.TryGetValue(id, out Entry entry)) throw new InvalidDataException("Saved Unity fileID is missing: " + id);
            return source.Substring(entry.Start, entry.Length);
        }
        public string SingleId(int type, Func<string, bool> predicate, string label)
        {
            string[] matches = documents.Where(pair => pair.Value.Type == type && predicate(Document(pair.Key)))
                .Select(pair => pair.Key).ToArray();
            Check(matches.Length == 1, "Saved scene contains exactly one " + label + ".");
            if (matches.Length != 1) throw new InvalidDataException("Cannot uniquely resolve saved " + label + ".");
            return matches[0];
        }
        public string SingleDocument(int type, Func<string, bool> predicate, string label) => Document(SingleId(type, predicate, label));
        public string SingleTransformIdFor(string gameObjectId)
        {
            string[] matches = documents.Where(pair => (pair.Value.Type == 4 || pair.Value.Type == 224) &&
                Regex.IsMatch(Document(pair.Key), @"(?m)^  m_GameObject: \{fileID: " + Regex.Escape(gameObjectId) + @"\}\r?$"))
                .Select(pair => pair.Key).ToArray();
            Check(matches.Length == 1, "Saved GameObject has exactly one transform.");
            if (matches.Length != 1) throw new InvalidDataException("Cannot uniquely resolve GameObject transform.");
            return matches[0];
        }
        public string SingleTransformFor(string gameObjectId) => Document(SingleTransformIdFor(gameObjectId));
        public HashSet<string> DescendantGameObjectIds(string rootTransformId)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(); pending.Push(rootTransformId);
            while (pending.Count > 0)
            {
                string id = pending.Pop();
                if (!visited.Add(id)) throw new InvalidDataException("Saved button descendants contain a cycle.");
                string transform = Document(id);
                ids.Add(Reference(transform, "m_GameObject"));
                foreach (Match child in Regex.Matches(transform, @"(?m)^  - \{fileID: (-?\d+)\}\r?$"))
                    pending.Push(child.Groups[1].Value);
            }
            return ids;
        }
        public IEnumerable<string> TextDocuments(HashSet<string> gameObjectIds)
        {
            foreach (KeyValuePair<string, Entry> pair in documents)
            {
                if (pair.Value.Type != 114) continue;
                string body = Document(pair.Key);
                if (!body.Contains("  m_text:", StringComparison.Ordinal)) continue;
                if (gameObjectIds.Contains(Reference(body, "m_GameObject"))) yield return body;
            }
        }
        public IEnumerable<string> ImageDocuments(string gameObjectId)
        {
            foreach (KeyValuePair<string, Entry> pair in documents)
            {
                if (pair.Value.Type != 114) continue;
                string body = Document(pair.Key);
                if (!body.Contains("  m_Color:", StringComparison.Ordinal) || !body.Contains("  m_Type:", StringComparison.Ordinal)) continue;
                Match owner = Regex.Match(body, @"(?m)^  m_GameObject: \{fileID: (-?\d+)\}\r?$");
                if (owner.Success && owner.Groups[1].Value == gameObjectId) yield return body;
            }
        }
        public static string Name(string document) => Regex.Match(document, @"(?m)^  m_Name: ([^\r\n]+)\r?$").Groups[1].Value;
        public static string Scalar(string document, string field)
        {
            Match match = Regex.Match(document, @"(?m)^  " + Regex.Escape(field) + @": ([^\r\n]*)\r?$");
            if (!match.Success) throw new InvalidDataException("Saved Unity scalar is missing: " + field);
            return match.Groups[1].Value;
        }
        public static string OptionalScalar(string document, string field) =>
            Regex.Match(document, @"(?m)^  " + Regex.Escape(field) + @": ([^\r\n]*)\r?$").Groups[1].Value;
        public static string Text(string document)
        {
            string text = Scalar(document, "m_text");
            if (text.StartsWith('"')) return System.Text.Json.JsonSerializer.Deserialize<string>(text);
            if (text.StartsWith('\'')) return text[1..^1].Replace("''", "'", StringComparison.Ordinal);
            return text;
        }
        public static string Reference(string document, string field)
        {
            Match match = Regex.Match(document, @"(?m)^  " + Regex.Escape(field) + @": \{fileID: (-?\d+)\}\r?$");
            if (!match.Success) throw new InvalidDataException("Saved Unity reference is missing: " + field);
            return match.Groups[1].Value;
        }
    }
}
