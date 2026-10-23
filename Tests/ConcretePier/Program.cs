using System.Reflection;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private const string Material = "Assets/Script/BuildMode/BridgeMaterialSO.cs";
    private const string Contract = "Assets/Script/Client/ContractSO.cs";
    private const string UI = "Assets/Script/BuildMode/2D/BuildUIController.cs";
    private const string Button = "Assets/Script/UI/MaterialButtonTrigger.cs";
    private const string Creator = "Assets/Script/BuildMode/2D/BarCreator.cs";
    private const string Clipboard = "Assets/Script/BuildMode/2D/ClipboardManager.cs";
    private const string Asset = "Assets/BridgeBuilder/Data/Resources/Concrete Pier.asset";
    private const string AssetGuid = "7fbcf5a7e459d434f970d2c976504d5c";
    private const string MarshContract = "Assets/BridgeBuilder/Data/Contract/TownRiver/Marsh Contract.asset";
    private const string WoodPierGuid = "b98aa9ba752ac28449feebc76f256bf2";
    private const string CroppedDefault = "Assets/Elements/UI/bm_ui/pier concrete not selected.png";
    private const string CroppedSelected = "Assets/Elements/UI/bm_ui/concrete pier2 selected (1).png";
    private static string root;
    private static int checks;

    private static int Main(string[] args)
    {
        try
        {
            Check(args.All(value => value == "--source-only"), "Supported option: --source-only.");
            root = FindRoot();
            Preservation(!args.Contains("--source-only"));
            CroppedSpritePreservation();
            ExecuteProductionMethods();
            ActionBoundaryGuards();
            RegistrationSourceGuards();
            if (!args.Contains("--source-only")) { ConfiguredAsset(); ConfiguredSharedUI(); }
            Console.WriteLine($"PASS: {checks} Concrete Pier {(args.Contains("--source-only") ? "source/policy/preservation" : "source/policy/preservation/configured-asset")} assertions. " +
                "Real material, contract-hidden, UI-selection and purchase methods executed with engine/wallet boundary doubles. " +
                "No live Unity scene, account, purchase, player save, network or asset was modified by this runner.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    private static void CroppedSpritePreservation()
    {
        // These are additional user assets, not replacements for the immutable
        // original sprite/source baseline. Assignment must not rewrite either PNG.
        using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CroppedSprites.json")));
        foreach (JsonProperty file in baseline.RootElement.GetProperty("files").EnumerateObject())
        {
            string path = Path.Combine(root, file.Name);
            Check(File.Exists(path), "New cropped sprite asset is missing: " + file.Name);
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == file.Value.GetString(),
                "Assigning cropped sprites must preserve their captured pixels/metadata: " + file.Name);
        }
        Check(Guid(CroppedDefault) == "91e91c24e8c573e4ba73a19586500fcd" && Guid(CroppedSelected) == "e859e4466a733644c8291b45c979e1ce",
            "The two new cropped sprites must retain their user-created GUIDs.");
        foreach ((string path, int width, int height) in new[] { (CroppedDefault, 515, 596), (CroppedSelected, 529, 621) })
        {
            byte[] png = File.ReadAllBytes(Path.Combine(root, path));
            Check(png.Length > 24 && png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "Cropped artwork must remain a PNG: " + path);
            Check(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) == width &&
                BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) == height,
                "Cropped sprite canvas must retain the supplied dimensions, not be stretched/re-encoded: " + path);
        }
    }

    private static void Preservation(bool requireMarshReplacement)
    {
        using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baselines.json")));
        foreach (string group in new[] { "originalUserAssets", "legacyMaterialAssets", "existingContractAssets", "restorationAndCatalogSources" })
            foreach (JsonProperty file in baseline.RootElement.GetProperty(group).EnumerateObject())
            {
                string path = Path.Combine(root, file.Name);
                Check(File.Exists(path), "Preserved file is missing: " + file.Name);
                if (file.Name == MarshContract)
                {
                    CheckMarshPreservation(File.ReadAllText(path), file.Value.GetString(), requireMarshReplacement);
                    continue;
                }
                Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == file.Value.GetString(),
                    "This task must preserve the captured file byte-for-byte: " + file.Name);
            }
        string[] contracts = Directory.GetFiles(Path.Combine(root, "Assets/BridgeBuilder/Data/Contract"), "*.asset", SearchOption.AllDirectories);
        Check(contracts.Length == baseline.RootElement.GetProperty("existingContractAssets").EnumerateObject().Count(),
            "Concrete Pier wiring must not add, remove or replace an existing contract asset.");
        foreach (string path in contracts)
            if (Path.GetFullPath(path) != Path.GetFullPath(Path.Combine(root, MarshContract)))
                Check(!File.ReadAllText(path).Contains(AssetGuid, StringComparison.Ordinal), "No other current contract may gain the new material: " + path);
        foreach (JsonProperty file in baseline.RootElement.GetProperty("legacyMaterialAssets").EnumerateObject())
            Check(OptionalField(Read(file.Name), "requiresExplicitContractAllowance") != "1", "The new opt-in flag must not restrict an existing material.");
        Check(Guid(Asset) == AssetGuid, "The user's new Concrete Pier asset identity must be retained.");
        Check(Read("Assets/Script/ChallengeBuildWorkspace.cs").Contains("Object.Instantiate(definition)", StringComparison.Ordinal),
            "Session challenges must clone the authored contract instead of deriving opt-in allowances from a global material catalog.");
        var workspace = Parse(Read("Assets/Script/ChallengeBuildWorkspace.cs"));
        Check(!workspace.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(node => node.Left.ToString().EndsWith("allowedMaterials", StringComparison.Ordinal)) &&
            !workspace.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(node => node.Expression.ToString().EndsWith("allowedMaterials.Add", StringComparison.Ordinal)),
            "Runtime challenge workspaces must not automatically opt new materials into every contract.");
        Check(Method(Type("Assets/Script/ChallengeBridgeSubmission.cs", "ChallengeBridgeSubmissionRules"), "Validate").ToString()
            .Contains("definition.IsMaterialHidden(material)", StringComparison.Ordinal),
            "Submitted challenge graphs must retain the authoritative contract-hidden guard.");
    }

    private static void CheckMarshPreservation(string source, string originalHash, bool requireReplacement)
    {
        string normalized = NormalizeAuthorizedMarshAllowance(source, requireReplacement);
        if (HashText(normalized) == originalHash) { Check(true, "Original Marsh bytes retained apart from the authorized GUID."); return; }
        // Unity's explicit Save Project reformatted this one previously quoted
        // asset. Keep the original baseline AND all three immutable snapshots;
        // do not replace the baseline or accept arbitrary new serialization.
        string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
        string original = Fixture("MarshOriginal.asset.txt"), before = Fixture("MarshNativeBefore.asset.txt"), after = Fixture("MarshNativeAfter.asset.txt");
        string proof = Fixture("MarshNative.proof.txt");
        Check(originalHash == "4FBE5D5DC66A9BCC440389E9029FB98592689C091C3AC24A441B7170CD21FA2A" && HashText(original) == originalHash,
            "The original pre-serialization Marsh contract baseline must remain unchanged.");
        Check(HashText(before) == "A8E9014EA720C189712F3E6491A2E6AB90FD9F439FC1E5A125537124D0E1AB89" &&
            HashText(after) == "8549F67E22391A5777F6D97B661513D838D29BEA930444C972B2214E073E801D", "Native before/after fixtures must retain their exact captured whole-file hashes.");
        Check(CanonicalMarshScalars(original).SequenceEqual(CanonicalMarshScalars(before)),
            "The original-to-native-before change must be only YAML quote/line folding, with every contract scalar unchanged.");
        Check(before.Replace(WoodPierGuid, AssetGuid, StringComparison.Ordinal) == after && Regex.Matches(before, Regex.Escape(WoodPierGuid)).Count == 1,
            "Captured native application must change exactly one material GUID and no other byte.");
        Check(HashText(proof) == "E121B7781F7098F798D47B4DBAC6EE9D0B8802A562439C4ECE1171DDE3F9980D" &&
            proof.Contains("Protected serialized-field snapshots: Marsh=688, contract=91, material=37.", StringComparison.Ordinal) &&
            proof.Contains("APPLIED: only the existing pier material pointer, emphasized name and one allowance pointer changed; maxPieces, all numbers and all other fields retained.", StringComparison.Ordinal) &&
            proof.Contains("PASS: requested narrow scope checked.", StringComparison.Ordinal) && !proof.Contains("FAILED", StringComparison.Ordinal),
            "The exact successful native report must prove all 91 non-target serialized contract fields were protected.");
        Check(HashText(source) == HashText(after) || (!requireReplacement && HashText(source) == HashText(before)),
            "Only the exact authorized native-after Marsh serialization is accepted; no broad rebaseline.");
    }

    private static string[] CanonicalMarshScalars(string source)
    {
        // This constrained fixture normalizer handles this captured Unity asset,
        // not arbitrary YAML: fold continuation lines and decode its quoted
        // scalar strings. Structure, keys, order, arrays and references stay.
        var tokens = new List<string>();
        foreach (string line in source.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            string value = line.Trim();
            if (value.Length == 0) continue;
            bool openMap = tokens.Count > 0 && tokens[^1].Count(character => character == '{') > tokens[^1].Count(character => character == '}');
            bool continuation = line.StartsWith(' ') && !value.StartsWith('-') && !Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*:");
            if (tokens.Count > 0 && (openMap || continuation)) tokens[^1] += " " + value;
            else tokens.Add(value);
        }
        return tokens.Select(token =>
        {
            Match scalar = Regex.Match(token, @"^(?<prefix>(?:- )?(?:[A-Za-z_][A-Za-z0-9_]*:\s*)?)(?<value>.*)$");
            string value = scalar.Groups["value"].Value;
            if (value.StartsWith('"') && value.EndsWith('"')) value = JsonSerializer.Deserialize<string>(value);
            return scalar.Groups["prefix"].Value + value;
        }).ToArray();
    }

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string NormalizeAuthorizedMarshAllowance(string source, bool requireReplacement)
    {
        // Preserve the original task baseline. The only later authorized asset
        // delta is this one exact third-allowance identity, not a new baseline.
        Match list = Regex.Match(source, @"(?ms)^  allowedMaterials:\r?\n(?<body>.*?)(?=^  [a-zA-Z]|\z)");
        Check(list.Success, "Marsh's existing allowed-material list must remain present.");
        string[] references = Regex.Matches(list.Groups["body"].Value, @"guid: ([a-f0-9]{32})").Cast<Match>().Select(value => value.Groups[1].Value).ToArray();
        Check(references.Length == 3 && references[0] == "57a46c6632dd61b4094902963cf1c5be" && references[1] == "9377a255283b8cc499761c693baf53dd" &&
            (references[2] == AssetGuid || (!requireReplacement && references[2] == WoodPierGuid)),
            "Only Marsh's exact third Wood Pier allowance may become Concrete Pier; Road/Beam/order remain unchanged.");
        int replacements = Regex.Matches(source, Regex.Escape(AssetGuid)).Count;
        Check(replacements == (references[2] == AssetGuid ? 1 : 0), "Concrete Pier must occur only once in Marsh's allowance, not in unrelated fields.");
        Check(Regex.Matches(list.Groups["body"].Value, @"(?m)^    maxPieces: 0\s*$").Count == 3, "All three Marsh allowances must retain maxPieces zero.");
        return replacements == 0 ? source : source.Replace(AssetGuid, WoodPierGuid, StringComparison.Ordinal);
    }

    private static void ExecuteProductionMethods()
    {
        ClassDeclarationSyntax material = Type(Material, "BridgeMaterialSO");
        ClassDeclarationSyntax contract = Type(Contract, "ContractSO");
        ClassDeclarationSyntax ui = Type(UI, "BuildUIController");
        string predicate = Method(material, "CanUseInContract").ToFullString();
        var predicateNode = Method(material, "CanUseInContract");
        Check(predicateNode.Body.Statements.First() is IfStatementSyntax first &&
            first.Condition.ToString() == "!requiresExplicitContractAllowance" && first.Statement.ToString().Contains("return true", StringComparison.Ordinal),
            "Legacy materials must immediately retain the original allowance/purchase behavior.");
        Check(!predicate.Contains("PlayerData", StringComparison.Ordinal) && !predicate.Contains("Wallet", StringComparison.Ordinal) &&
            !predicate.Contains("unlockCost", StringComparison.Ordinal), "The pure opt-in predicate must not grant purchased entitlements or charge a wallet.");
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"))
            .Replace("/*ALLOWANCE*/", Type(Contract, "MaterialAllowance").ToFullString(), StringComparison.Ordinal)
            .Replace("/*CONTRACT_HIDDEN*/", Method(contract, "IsMaterialHidden").ToFullString(), StringComparison.Ordinal)
            .Replace("/*UI_METHODS*/", string.Join("\n", new[] { "CanSelectMaterial", "PromptUnlockMaterial", "ConfirmUnlockMaterial", "CancelUnlockMaterial" }
                .Select(name => Method(ui, name).ToFullString())), StringComparison.Ordinal)
            .Replace("/*COPIED_BAR*/", Type(Clipboard, "CopiedBarInfo").ToFullString(), StringComparison.Ordinal)
            .Replace("/*CLIPBOARD_GUARD*/", Method(Type(Clipboard, "ClipboardManager"), "HasContractPermissionForCopiedMaterials").ToFullString(), StringComparison.Ordinal)
            .Replace("/*DRAW_GUARD*/", Method(Type(Creator, "BarCreator"), "HasContractMaterialOptIn").ToFullString(), StringComparison.Ordinal);
        SyntaxTree[] trees = {
            CSharpSyntaxTree.ParseText(Read(Material)),
            CSharpSyntaxTree.ParseText(boundary),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt")))
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ConcretePierPolicies_" + System.Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        Check(result.Success, "Actual production source isolated compilation: " + string.Join("; ", result.Diagnostics.Where(value => value.Severity == DiagnosticSeverity.Error)));
        try
        {
            checks += (int)Assembly.Load(output.ToArray()).GetType("ConcretePierScenarios").GetMethod("Run").Invoke(null, null);
        }
        catch (TargetInvocationException error) { throw new InvalidOperationException("Production opt-in/purchase scenario: " + error.InnerException?.Message, error.InnerException); }
    }

    private static void ActionBoundaryGuards()
    {
        ClassDeclarationSyntax button = Type(Button, "MaterialButtonTrigger"), ui = Type(UI, "BuildUIController");
        ClassDeclarationSyntax creator = Type(Creator, "BarCreator"), clipboard = Type(Clipboard, "ClipboardManager");
        Check(Method(Type(Contract, "ContractSO"), "IsMaterialHidden").ToString().Contains("!material.CanUseInContract(this)", StringComparison.Ordinal),
            "Contract hidden policy must fold in the explicit opt-in predicate.");
        var evaluate = Method(button, "EvaluateMaterialRestriction");
        Before(evaluate, "buttonMaterial.CanUseInContract", "parentWrapper.SetActive", "Button availability must be checked before enabling its slot.");
        Check(evaluate.ToString().Contains("isMaterialHidden = !hasContractOptIn", StringComparison.Ordinal),
            "The opt-in slot must hide, not become a purchasable greyed-out button.");
        Before(Method(ui, "CanSelectMaterial"), "material.CanUseInContract", "PlayerDataManager.Instance.IsMaterialUnlockedForContract",
            "A purchased entitlement must not bypass opt-in selection.");
        Before(Method(ui, "PromptUnlockMaterial"), "btn.buttonMaterial.CanUseInContract", "unlockMaterialPanel.SetActive",
            "Direct unavailable opt-in purchase prompts must be rejected before opening.");
        foreach (string charge in new[] { "wallet.PurchaseMaterial", "PlayerDataManager.Instance.TryPurchaseMaterialForContract" })
            Before(Method(ui, "ConfirmUnlockMaterial"), "pendingMaterial.CanUseInContract", charge,
                "Opt-in availability must be rechecked before invoking a payment owner.");
        Check(Method(ui, "ConfirmUnlockMaterial").ToString().Contains("pendingUnlockContract != currentContract", StringComparison.Ordinal),
            "A stale opt-in material prompt cannot charge another contract.");
        var selection = Method(creator, "SetActiveMaterial");
        InvocationExpressionSyntax selectionGate = Invocation(selection, "HasContractMaterialOptIn");
        AssignmentExpressionSyntax selected = selection.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single(value => value.Left.ToString() == "activeMaterial");
        Check(selectionGate.SpanStart < selected.SpanStart, "Direct material callbacks must be gated before active selection changes.");
        Before(Method(creator, "CreateGhostPierBar"), "HasContractMaterialOptIn", "Instantiate", "Unavailable piers must not create preview geometry.");
        Before(Method(creator, "OnPointerDown"), "HasContractMaterialOptIn", "Instantiate", "Opt-in gate must precede pier-foundation point creation.");
        Before(Method(creator, "StartBarCreation"), "HasContractMaterialOptIn", "Instantiate", "Direct manual construction must gate before creating a member.");
        Before(Method(creator, "FinishBarCreation"), "HasContractMaterialOptIn", "ResolveDrawingEndpoint", "An already-started gesture must be rechecked before committing geometry.");
        Check(Method(creator, "Update").ToString().Contains("currentBar.materialData", StringComparison.Ordinal) &&
            Method(creator, "FinishBarCreation").ToString().Contains("currentBar.materialData", StringComparison.Ordinal),
            "Stale current-bar material cannot bypass checks by changing only the public active material field.");
        Check(Invocation(Method(creator, "TryStartNodeAutoDraw"), "HasContractMaterialOptIn") != null &&
            Invocation(Method(creator, "CanContinueAutoDraw"), "HasContractMaterialOptIn") != null,
            "Auto draw must gate both its start and every continuation.");
        Check(Method(creator, "HasContractMaterialOptIn").ToString().Contains("material.CanUseInContract", StringComparison.Ordinal),
            "Placement must call the shared production predicate.");
        Check(Method(clipboard, "HasContractPermissionForCopiedMaterials").ToString().Contains("bar.mat.CanUseInContract(contract)", StringComparison.Ordinal),
            "Clipboard validation must use the same opt-in predicate for all copied materials.");
        Before(Method(clipboard, "CutSelected"), "HasContractPermissionForCopiedMaterials", "barCreator.DeleteBar",
            "Reject the complete cut before deleting any source member.");
        Before(Method(clipboard, "CopySelected"), "HasContractPermissionForCopiedMaterials", "CreatePasteGhosts",
            "Copied unavailable materials must not create preview geometry.");
        Before(Method(clipboard, "CreatePasteGhosts"), "HasContractPermissionForCopiedMaterials", "Instantiate",
            "Direct preview creation must check every copied material before creating nodes.");
        Check(Invocation(Method(clipboard, "UpdatePasteGhostsWorldPosition"), "HasContractPermissionForCopiedMaterials") != null,
            "A contract change must invalidate an existing paste preview.");
        Check(Invocation(Method(clipboard, "HasValidCopiedContent"), "HasContractPermissionForCopiedMaterials") != null &&
            Invocation(Method(clipboard, "HasPasteContext"), "HasValidCopiedContent") != null,
            "Permission must flow through content validation into paste context.");
        Before(Method(clipboard, "ExecutePaste"), "HasPasteContext", "Instantiate", "Atomic paste must validate context/materials before creating any real node.");
        Before(Method(clipboard, "ExecutePaste"), "HasPasteContext", "existingBar.gameObject.SetActive", "Atomic paste must validate before disabling/replacing an old member.");
        Before(Method(clipboard, "ConfirmOverride"), "HasPasteContext", "ExecutePaste", "A delayed replacement confirmation must recheck the current contract.");
        Check(!Method(Type("Assets/Script/BuildMode/2D/Bar.cs", "Bar"), "Initialize").ToString().Contains("CanUseInContract", StringComparison.Ordinal),
            "The universal Bar initializer must remain usable by save/network reconstruction.");
    }

    private static void ConfiguredAsset()
    {
        string source = Read(Asset);
        Check(Field(source, "m_Script").Contains(Guid(Material), StringComparison.Ordinal), "Concrete Pier must retain BridgeMaterialSO.");
        foreach ((string field, string value) in new[] { ("isPier", "1"), ("requiresExplicitContractAllowance", "1"), ("isRoad", "0"), ("isRope", "0") })
            Check(Field(source, field) == value, "Configured Concrete Pier must use " + field + " = " + value + ".");
        string materialId = OptionalField(source, "materialId");
        Check(string.IsNullOrWhiteSpace(materialId) || materialId == "Concrete Pier", "Keep the user's original save identity Concrete Pier, whether fallback or explicitly recorded.");
        Check(Field(source, "materialIcon").Contains(Guid(CroppedDefault), StringComparison.Ordinal),
            "Concrete Pier's default icon must use the newly supplied cropped unselected picture.");
        string segmentGuid = ReferenceGuid(Field(source, "segmentPrefab")), capGuid = ReferenceGuid(Field(source, "pierCapPrefab"));
        string prefabRoot = Path.Combine(root, "Assets/BridgeBuilder/Prefabs/materials/map2");
        string Resolve(string guid) => Directory.GetFiles(prefabRoot, "*.prefab.meta", SearchOption.AllDirectories)
            .Single(path => Regex.IsMatch(File.ReadAllText(path), "(?m)^guid: " + Regex.Escape(guid) + "\\s*$"))[..^5];
        string segment = Resolve(segmentGuid), cap = Resolve(capGuid);
        Check(!segment.EndsWith("SLAB_LOW.Bottom.prefab", StringComparison.OrdinalIgnoreCase) &&
            !cap.EndsWith("SLAB_LOW.Top.prefab", StringComparison.OrdinalIgnoreCase), "Use additive normalized wrappers; do not repurpose the supplied original prefabs.");
        Check(File.ReadAllText(segment).Contains(Guid("Assets/BridgeBuilder/Prefabs/materials/map2/SLAB_LOW.Bottom.prefab"), StringComparison.Ordinal),
            "The segment wrapper must retain a reference to the user's supplied bottom model.");
        Check(File.ReadAllText(cap).Contains(Guid("Assets/BridgeBuilder/Prefabs/materials/map2/SLAB_LOW.Top.prefab"), StringComparison.Ordinal),
            "The cap wrapper must retain a reference to the user's supplied top model.");
    }

    private static void ConfiguredSharedUI()
    {
        string prefab = Read("Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab");
        var blocks = Regex.Matches(prefab, @"(?ms)^--- !u!\d+ &(?<id>-?\d+)[^\r\n]*\r?\n(?<body>.*?)(?=^--- !u!|\z)")
            .Cast<Match>().ToDictionary(value => value.Groups["id"].Value, value => value.Groups["body"].Value, StringComparer.Ordinal);
        string buttonGuid = Guid(Button), fitGuid = Guid("Assets/Script/UI/MaterialIconAspectLayout.cs");
        string normalGuid = Guid(CroppedDefault), selectedGuid = Guid(CroppedSelected);
        var triggers = blocks.Where(pair => OptionalField(pair.Value, "m_Script").Contains(buttonGuid, StringComparison.Ordinal) &&
            OptionalField(pair.Value, "buttonMaterial").Contains(AssetGuid, StringComparison.Ordinal)).ToArray();
        Check(triggers.Length == 1, "The shared material dock must contain exactly one Concrete Pier entry, not replace a legacy button.");
        string trigger = triggers.Single().Value;
        Check(Field(trigger, "defaultSprite").Contains(normalGuid, StringComparison.Ordinal) &&
            Field(trigger, "selectedOutlineSprite").Contains(selectedGuid, StringComparison.Ordinal), "New input cell must retain both supplied icon states.");
        string wrapperId = FileId(Field(trigger, "parentWrapper")), wrapper = blocks[wrapperId];
        Check(Field(wrapper, "m_Name") == "Concrete Pier" && Field(wrapper, "m_IsActive") == "0", "The new catalogue cell must be authored inactive until a contract explicitly opts in.");
        string buttonGo = blocks[FileId(Field(trigger, "m_GameObject"))];
        string[] buttonComponents = Regex.Matches(buttonGo, @"component: \{fileID: (-?\d+)\}").Cast<Match>().Select(value => value.Groups[1].Value).ToArray();
        string inputRectId = buttonComponents.Single(id => blocks[id].StartsWith("RectTransform:", StringComparison.Ordinal));
        string inputRect = blocks[inputRectId];
        Check(Field(inputRect, "m_AnchorMin") == "{x: 0, y: 0}" && Field(inputRect, "m_AnchorMax") == "{x: 1, y: 1}" &&
            Field(inputRect, "m_SizeDelta") == "{x: 0, y: 0}", "The new clickable surface must cover its whole material slot.");
        string image = blocks[FileId(Field(trigger, "buttonImage"))];
        Check(Field(image, "m_Sprite").Contains(normalGuid, StringComparison.Ordinal) && Field(image, "m_PreserveAspect") == "1" &&
            Field(image, "m_RaycastTarget") == "0", "Artwork must preserve aspect and leave clicks to the full-cell surface.");
        string iconGoId = FileId(Field(image, "m_GameObject"));
        string[] iconComponents = Regex.Matches(blocks[iconGoId], @"component: \{fileID: (-?\d+)\}").Cast<Match>().Select(value => value.Groups[1].Value).ToArray();
        string fitter = iconComponents.Select(id => blocks[id]).Single(body => OptionalField(body, "m_Script").Contains(fitGuid, StringComparison.Ordinal));
        Check(FileId(Field(fitter, "fitBox")) == inputRectId && FileId(Field(fitter, "selectionMotionRoot")) == inputRectId &&
            Field(fitter, "selectedSprite").Contains(selectedGuid, StringComparison.Ordinal), "Aspect/selection compensation must reference this entry's own fixed input cell.");
        Check(Field(fitter, "registrationDefaultSprite").Contains(normalGuid, StringComparison.Ordinal),
            "The registered default must be the new cropped unselected sprite, not a different material.");
        (double frameX, double frameY) = VectorField(fitter, "registeredPixelCanvasSize");
        (double defaultX, double defaultY) = VectorField(fitter, "defaultPixelCenterOffset");
        (double selectedX, double selectedY) = VectorField(fitter, "selectedPixelCenterOffset");
        double selectedScale = double.Parse(Field(fitter, "selectedRegistrationScale"), CultureInfo.InvariantCulture);
        Check(frameX == 560 && frameY == 658, "Both cropped images must share the approved padded virtual pixel frame.");
        Check(Math.Abs(selectedScale - 565d / 534d) < .00001d,
            "The selected source body's original size difference needs a uniform correction, not a squashed axis.");
        Check(Math.Abs(defaultX - (9d - 11d * selectedScale)) < .0002d &&
            Math.Abs(defaultY - (6.5d - 13.5d * selectedScale)) < .0002d && selectedX == 0 && selectedY == 0,
            "Cropped body centers must remain registered after the selected artwork's uniform scale correction.");
        Check(Math.Abs(defaultX) + 515d * .5d <= frameX * .5d &&
            Math.Abs(defaultY) + 596d * .5d <= frameY * .5d &&
            Math.Abs(selectedX) + 529d * selectedScale * .5d <= frameX * .5d &&
            Math.Abs(selectedY) + 621d * selectedScale * .5d <= frameY * .5d,
            "Both whole source canvases, including the selected outline, must fit the shared virtual frame.");
        ClassDeclarationSyntax fit = Type("Assets/Script/UI/MaterialIconAspectLayout.cs", "MaterialIconAspectLayout");
        string refresh = Method(fit, "RefreshNow").ToString();
        Check(!refresh.Contains("FindObject", StringComparison.Ordinal) && !refresh.Contains("GameManager", StringComparison.Ordinal) &&
            !refresh.Contains("PlayerData", StringComparison.Ordinal), "The one-icon fitter must not search scenes or mutate gameplay/player state.");
    }

    private static void RegistrationSourceGuards()
    {
        ClassDeclarationSyntax fit = Type("Assets/Script/UI/MaterialIconAspectLayout.cs", "MaterialIconAspectLayout");
        var variables = fit.Members.OfType<FieldDeclarationSyntax>().SelectMany(field => field.Declaration.Variables)
            .ToDictionary(variable => variable.Identifier.ValueText, StringComparer.Ordinal);
        Check(variables.TryGetValue("selectedRegistrationScale", out VariableDeclaratorSyntax scale) && scale.Initializer?.Value.ToString() == "1f",
            "Existing unconfigured components must retain a neutral default selected scale.");
        Check(variables.TryGetValue("registeredPixelCanvasSize", out VariableDeclaratorSyntax frame) && frame.Initializer == null &&
            variables.TryGetValue("registrationDefaultSprite", out VariableDeclaratorSyntax normal) && normal.Initializer == null,
            "Registration must be opt-in: legacy components retain a zero frame and no registered default sprite.");
        MethodDeclarationSyntax refresh = Method(fit, "RefreshNow");
        string Local(string name) => refresh.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == name).Initializer.Value.ToString();
        string gate = Regex.Replace(Local("registeredPair"), @"\s+", string.Empty);
        Check(gate.Contains("registrationDefaultSprite!=null", StringComparison.Ordinal) &&
            gate.Contains("selectedSprite!=null", StringComparison.Ordinal) &&
            gate.Contains("image.sprite==registrationDefaultSprite||image.sprite==selectedSprite", StringComparison.Ordinal) &&
            gate.Contains("registeredPixelCanvasSize.x!=0f||registeredPixelCanvasSize.y!=0f", StringComparison.Ordinal),
            "Only the explicitly registered pair and nonzero frame may use registration fitting.");
        IfStatementSyntax registered = refresh.DescendantNodes().OfType<IfStatementSyntax>().Single(statement => statement.Condition.ToString() == "registeredPair");
        Check(registered.Else != null && registered.Else.Statement.ToString().Contains("spriteRect.width / spriteRect.height", StringComparison.Ordinal),
            "Unregistered and unrelated sprites must retain the original independent aspect-fit branch.");
        Check(Local("imagePixelScale") == "selected ? selectedRegistrationScale : 1f" &&
            Local("registeredImageSize") == "spriteRect.size * imagePixelScale",
            "The selected artwork correction must multiply the complete sprite size by one uniform scalar.");
        Check(Regex.Replace(Local("pixelsToCanvas"), @"\s+", string.Empty) ==
            "Mathf.Min(width/registeredPixelCanvasSize.x,height/registeredPixelCanvasSize.y)",
            "Both states must fit the same virtual frame using one common source-pixel scale.");
        Check(registered.Statement.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(value =>
            value.Left.ToString() == "fitted" && value.Right.ToString() == "registeredImageSize * pixelsToCanvas") &&
            registered.Statement.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(value =>
            value.Left.ToString() == "center" && value.OperatorToken.Text == "+=" && value.Right.ToString() == "pixelOffset * pixelsToCanvas"),
            "Fitting and registration offsets must use the same pixel scale without independent X/Y artwork stretching.");
        Check(refresh.ToString().Contains("image.preserveAspect = true", StringComparison.Ordinal) &&
            refresh.ToString().Contains("selectionMotionRoot.anchoredPosition = Vector2.zero", StringComparison.Ordinal),
            "The source aspect ratio and stationary full-cell touch surface must remain enforced.");
        Check(!refresh.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(value =>
            value.Expression.ToString().Contains("AssetDatabase", StringComparison.Ordinal) ||
            value.Expression.ToString().Contains("EncodeTo", StringComparison.Ordinal) ||
            value.Expression.ToString().Contains("GetPixels", StringComparison.Ordinal) ||
            value.Expression.ToString().Contains("FindObject", StringComparison.Ordinal)),
            "Runtime registration must not rewrite/readback textures, import assets or search the scene.");
    }

    private static (double x, double y) VectorField(string source, string field)
    {
        Match value = Regex.Match(Field(source, field), @"^\{x: (?<x>[-+0-9.eE]+), y: (?<y>[-+0-9.eE]+)\}$");
        Check(value.Success, "Missing valid serialized vector " + field);
        return (double.Parse(value.Groups["x"].Value, CultureInfo.InvariantCulture),
            double.Parse(value.Groups["y"].Value, CultureInfo.InvariantCulture));
    }

    private static void Before(MethodDeclarationSyntax method, string guard, string mutation, string message)
    {
        InvocationExpressionSyntax first = Invocation(method, guard), second = Invocation(method, mutation);
        Check(first != null && second != null && first.SpanStart < second.SpanStart, message);
    }
    private static InvocationExpressionSyntax Invocation(MethodDeclarationSyntax method, string name) => method?.DescendantNodes()
        .OfType<InvocationExpressionSyntax>().FirstOrDefault(value => value.Expression.ToString() == name || value.Expression.ToString().EndsWith("." + name, StringComparison.Ordinal));
    private static SyntaxNode Parse(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        Check(!tree.GetDiagnostics().Any(value => value.Severity == DiagnosticSeverity.Error), "Production syntax must parse without errors.");
        return tree.GetRoot();
    }
    private static ClassDeclarationSyntax Type(string path, string name) => Parse(Read(path)).DescendantNodes().OfType<ClassDeclarationSyntax>().SingleOrDefault(value => value.Identifier.ValueText == name);
    private static MethodDeclarationSyntax Method(ClassDeclarationSyntax type, string name, bool required = true)
    {
        MethodDeclarationSyntax method = type?.Members.OfType<MethodDeclarationSyntax>().SingleOrDefault(value => value.Identifier.ValueText == name);
        if (required) Check(method != null, "Missing actual production method " + type?.Identifier.ValueText + "." + name);
        return method;
    }
    private static string Read(string path) => File.ReadAllText(Path.Combine(root, path));
    private static string OptionalField(string source, string name) => Regex.Match(source, "(?m)^  " + Regex.Escape(name) + @":([^\r\n]*)").Groups[1].Value.Trim();
    private static string Field(string source, string name)
    { string value = OptionalField(source, name); Check(value.Length > 0, "Missing configured field " + name); return value; }
    private static string ReferenceGuid(string value)
    { Match match = Regex.Match(value, @"guid: ([a-f0-9]{32})"); Check(match.Success, "Missing Unity asset reference GUID"); return match.Groups[1].Value; }
    private static string FileId(string value)
    { Match match = Regex.Match(value, @"fileID: (-?\d+)"); Check(match.Success && match.Groups[1].Value != "0", "Missing nonzero Unity object reference"); return match.Groups[1].Value; }
    private static string Guid(string path)
    { Match match = Regex.Match(Read(path + ".meta"), @"(?m)^guid: ([a-f0-9]{32})"); Check(match.Success, "Missing asset GUID: " + path); return match.Groups[1].Value; }
    private static string FindRoot()
    {
        for (DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, Material))) return directory.FullName;
        throw new InvalidOperationException("Run this test from the Civil-Craft repository.");
    }
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }
}
