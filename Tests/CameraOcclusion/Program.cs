using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int guards;

    private static void Main()
    {
        string root = FindRoot();
        string profile = File.ReadAllText(Path.Combine(root, "Assets/Script/Performance/CanyonOcclusionProfile.cs"));
        string policy = File.ReadAllText(Path.Combine(root, "Assets/Script/Performance/CanyonCameraOcclusionPolicy.cs"));
        string boundary = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Boundary.cs.txt"));
        string scenarios = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios.cs.txt"));
        string fps = File.ReadAllText(Path.Combine(root, "Assets/Script/Performance/FPSViewer.cs"));
        string sample = Class(fps, "FrameRateSample").ToFullString();
        int checks = Execute(false, boundary, profile, policy, scenarios, sample);
        checks += Execute(true, boundary, profile, policy, scenarios, sample);
        string development = File.ReadAllText(Path.Combine(root, "Assets/Script/Performance/DevelopmentPerformanceSettings.cs"));
        var release = CSharpSyntaxTree.ParseText(development).GetRoot();
        Require(!release.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == "CreateOverlay" || method.Identifier.ValueText == "CloneRow"),
            "Release builds must not compile the development UI/overlay factories.");
        Require(!development.Contains("PlayerPrefs") && !development.Contains("enableGameplayOcclusion ="),
            "Development toggles must not persist preferences or modify the authored scene switch.");
        CheckSourceBoundaries(profile, policy);
        CheckIntegrationBoundaries(root);
        Console.WriteLine($"PASS: {checks} actual-source camera eligibility, coverage, owner-suspension, callback and lifecycle assertions; {guards} structural safety guards. No live Unity scene, bake, player/account saves, or network access.");
    }

    private static void CheckIntegrationBoundaries(string root)
    {
        string Source(string relative) => File.ReadAllText(Path.Combine(root, relative));
        var follow = Class(Source("Assets/Script/Player/MinimapFollow.cs"), "MinimapFollow");
        Require(Method(follow, "OnEnable").Contains("minimapCamera.useOcclusionCulling = false"),
            "The shared compact/expanded map camera must opt out when activated.");
        var map = Class(Source("Assets/Script/Player/MinimapUnlockController.cs"), "MinimapUnlockController");
        string fallback = Method(map, "CreateFallbackMinimapCamera");
        Require(fallback.IndexOf("camera.useOcclusionCulling = false", StringComparison.Ordinal) >
            fallback.IndexOf("AddComponent<Camera>()", StringComparison.Ordinal), "Replacement minimap camera must explicitly opt out.");
        var portrait = Class(Source("Assets/Script/Player/PlayerSave/CharacterPortraitUploadService.cs"), "CharacterPortraitUploadService");
        var portraitFactory = portrait.Members.OfType<MethodDeclarationSyntax>().Single(method => method.ToFullString().Contains("AddComponent<Camera>()"));
        Require(portraitFactory.ToFullString().Contains("portraitCamera.useOcclusionCulling = false"), "Runtime portrait capture must explicitly opt out.");
        var recording = Class(Source("Assets/Script/UI/RecordingCameraShortcut.cs"), "RecordingCameraShortcut");
        string begin = Method(recording, "BeginFreeCamera");
        Require(begin.IndexOf("SetSuspended(occlusionSuspendedCamera, this, true)", StringComparison.Ordinal) <
            begin.IndexOf("look.ReleaseOrbitForRecording()", StringComparison.Ordinal), "Suspend before recording can move/reparent the player camera.");
        string release = Method(recording, "ReleaseOcclusionSuspension");
        Require(release.Contains("SetSuspended(occlusionSuspendedCamera, this, false)"), "Recording releases the originally owned camera, not the current replacement.");
        string returnBody = Method(recording, "ReturnToPlayer");
        Require(returnBody.Contains("finally") && returnBody.Contains("ReleaseOcclusionSuspension()"), "Recording return must release even after an exception.");
        Require(Method(recording, "OnDisable").Contains("ReturnToPlayer(false)") && Method(recording, "OnDestroy").Contains("ReturnToPlayer(false)"),
            "Recording disable/teardown must release an active shot.");
        var cinema = Class(Source("Assets/Script/Scene/CinematicDirector.cs"), "CinematicDirector");
        string routine = Method(cinema, "CinematicRoutine");
        Require(routine.Contains("SetSuspended(occlusionSuspendedCamera, this, true)") && routine.Contains("finally") &&
            routine.Contains("suspensionVersion == occlusionSuspensionVersion"), "Cinematic lifetime must acquire early and generation-guard the final release.");
        Require(Method(cinema, "RefreshOcclusionSuspension").Contains("suspensionVersion != occlusionSuspensionVersion"),
            "An old cinematic callback cannot take over the newer sequence's suspension.");
        Require(Method(cinema, "OnDisable").Contains("ReleaseOcclusionSuspension()") && Method(cinema, "OnDestroy").Contains("ReleaseOcclusionSuspension()"),
            "Cinematic cancellation and teardown must release ownership.");
        string disableCinematic = Method(cinema, "OnDisable");
        Require(disableCinematic.Contains("StopAllCoroutines()") &&
            disableCinematic.IndexOf("StopAllCoroutines()", StringComparison.Ordinal) < disableCinematic.IndexOf("ReleaseOcclusionSuspension()", StringComparison.Ordinal),
            "Disabled directors must stop camera movement before releasing their culling suspension.");
        Require(disableCinematic.IndexOf("RestoreCancelledShot()", StringComparison.Ordinal) < disableCinematic.IndexOf("ReleaseOcclusionSuspension()", StringComparison.Ordinal),
            "Cancelled camera pose must be restored before releasing its suspension.");
        string cancellation = Method(cinema, "RestoreCancelledShot");
        Require(cancellation.Contains("shotMovementBefore") && cancellation.Contains("shotLookBefore") && cancellation.Contains("shotMotorBefore") &&
            cancellation.Contains("PauseManager.Instance.isPaused") && cancellation.Contains("DialogueManager.IsAnyDialogueActive") && cancellation.Contains("QuizUIManager.IsAnyQuizActive"),
            "Cancellation must restore captured control state only when other input owners allow it.");
        var setup = Class(Source("Assets/Editor/CanyonOcclusionSetup.cs"), "CanyonOcclusionSetup");
        string setupBody = Method(setup, "Setup");
        Require(setupBody.Contains("original | OcclusionBits") && setupBody.Contains("original & ~OcclusionBits"),
            "Setup may alter only Occluder/Occludee bits, preserving navigation/lighting/batching.");
        Require(setupBody.Contains("IsGeneratedProp(go.transform)") && setupBody.Contains("continue"), "Generated props must be left to existing distance culling.");
        Require(!setupBody.Contains("SaveScene(") && !setupBody.Contains("SaveOpenScenes("), "Setup cannot automatically save the user's scene edits.");
        Require(Method(setup, "Inspect").Contains("SceneManager.sceneCount == 1") && Method(setup, "Inspect").Contains("!scene.isDirty"),
            "Setup/bake must reject other loaded scenes and pre-existing unsaved work.");
        string backup = Method(setup, "BackupCurrentBake");
        Require(backup.Contains("LocalBackups") && backup.Contains("!destination.StartsWith(Path.GetFullPath(\"Assets\")") && backup.Contains("File.Copy"),
            "Recoverable pre-bake data must stay outside imported Assets.");
        string bakeCompletion = Method(setup, "PollBake");
        Require(bakeCompletion.Contains("elapsedSinceFinish < 30d") && bakeCompletion.Contains("ReadPvsDataSize(ReadBakedData()) <= 0") &&
            bakeCompletion.IndexOf("elapsedSinceFinish < 30d", StringComparison.Ordinal) < bakeCompletion.IndexOf("bake = null", StringComparison.Ordinal),
            "Background generation must wait for imported native PVS readiness with a bounded timeout.");
        Require(bakeCompletion.Contains("Check(freshlyWritten") && bakeCompletion.Contains("Check(!job.nativeFailureObserved") &&
            bakeCompletion.IndexOf("Check(freshlyWritten", StringComparison.Ordinal) < bakeCompletion.IndexOf("SaveScene", StringComparison.Ordinal),
            "A retained old bake or observed native failure cannot be accepted as successful output.");
        Require(bakeCompletion.IndexOf("HashSceneContent", StringComparison.Ordinal) < bakeCompletion.IndexOf("SaveScene", StringComparison.Ordinal) &&
            bakeCompletion.IndexOf("ReadBakedData", StringComparison.Ordinal) < bakeCompletion.IndexOf("SaveScene", StringComparison.Ordinal),
            "Only unchanged initially saved Canyon content and verified linked data can be saved after bake.");
    }

    private static int Execute(bool development, params string[] source)
    {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("CameraOcclusion_" + Guid.NewGuid().ToString("N"),
            source.Select(text => CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(preprocessorSymbols: development ? new[] { "DEVELOPMENT_BUILD" } : Array.Empty<string>()))), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        Assembly assembly = Assembly.Load(stream.ToArray());
        try { return (int)assembly.GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw new InvalidOperationException("Actual camera occlusion regression failed.", exception.InnerException); }
    }

    private static void CheckSourceBoundaries(string profileText, string policyText)
    {
        var profile = Class(profileText, "CanyonOcclusionProfile");
        var policy = Class(policyText, "CanyonCameraOcclusionPolicy");
        VariableDeclaratorSyntax master = profile.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables).Single(field => field.Identifier.ValueText == "enableGameplayOcclusion");
        Require(master.Initializer == null || master.Initializer.Value.IsKind(SyntaxKind.FalseLiteralExpression),
            "Gameplay occlusion must default off until on-device A/B approval.");
        var roles = policy.Members.OfType<EnumDeclarationSyntax>().Single(type => type.Identifier.ValueText == "CameraRole");
        Require(roles.Members.Single(value => value.Identifier.ValueText == "Excluded").EqualsValue?.Value.ToString() == "0",
            "Existing/default camera roles must fail open as Excluded.");
        Require(Method(policy, "LateUpdate").Contains("role == CameraRole.Gameplay"),
            "Only Gameplay cameras require per-frame state/coverage checks.");
        Require(Method(policy, "RefreshPolicy").Contains("obsoleteOwners.Clear()") && Method(policy, "RefreshPolicy").Contains("owner == null"),
            "Destroyed Unity owners must be removed without leaving a permanent suspension.");
        Require(Method(policy, "RefreshPolicy").Contains("profile.gameplayCamera == ownedCamera") &&
            Method(policy, "RefreshPolicy").Contains("manager.MainCamera == ownedCamera"),
            "A role label alone cannot enable another camera.");
        Require(Method(policy, "RefreshPolicy").Contains("CanyonOcclusionProfile.TargetSceneName") &&
            Method(policy, "RefreshPolicy").Contains("scene.isLoaded"), "Policy cannot enable in another or unloaded scene.");
        Require(Method(policy, "OnDisable").Contains("useOcclusionCulling = false"), "Disabled policy must fail open immediately.");
        Require(Method(policy, "OnDestroy").Contains("useOcclusionCulling = false"), "Removing the policy must not turn an unguarded native switch on.");
        Require(Method(policy, "ResetForNewSession").Contains("suspensionOwners.Clear()") &&
            Method(policy, "ResetForNewSession").Contains("UnbindCallbacks()") &&
            Method(policy, "ResetForNewSession").Contains("BindCallbacks()"),
            "Reload-disabled sessions must clear old owners and rebind without duplicated callbacks.");
        Require(policyText.Contains("RenderPipelineManager.beginCameraRendering") && policyText.Contains("Camera.onPreCull"),
            "Both SRP and built-in/manual camera paths must apply the explicit camera policy.");
        foreach (string name in new[] { "FindObject", "FindObjects", "Camera.allCameras", "GetComponentsInChildren", "SetActive(", "PlayerPrefs", "PlayerDataManager", "Fusion", "PlayFab" })
            Require(!policyText.Contains(name) && !profileText.Contains(name), "Camera/profile code must not search globally or touch world state, accounts or player saves: " + name);
        foreach (AssignmentExpressionSyntax assignment in policy.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax access) continue;
            if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression))
            {
                Require(access.ToString() == "RenderPipelineManager.beginCameraRendering" || access.ToString() == "Camera.onPreCull",
                    "The only external subscription writes are the two render callbacks.");
                continue;
            }
            Require(access.Name.Identifier.ValueText == "useOcclusionCulling", "The policy may mutate only Camera.useOcclusionCulling externally: " + assignment.Left);
        }
        string contains = Method(profile, "ContainsCameraPosition");
        Require(contains.Contains("InverseTransformPoint") && contains.Contains("IsFinite"),
            "Coverage must support authored rotated/scaled areas and reject nonfinite values.");
        Require(contains.Contains("activeInHierarchy") && contains.Contains("area.gameObject.scene != gameObject.scene"),
            "Hidden or foreign view volumes must not authorize culling.");
    }

    private static ClassDeclarationSyntax Class(string source, string name) => CSharpSyntaxTree.ParseText(source,
        new CSharpParseOptions(preprocessorSymbols: new[] { "UNITY_EDITOR" })).GetRoot()
        .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(type => type.Identifier.ValueText == name);
    private static string Method(ClassDeclarationSyntax type, string name) => type.Members.OfType<MethodDeclarationSyntax>()
        .Single(method => method.Identifier.ValueText == name).ToFullString();
    private static void Require(bool condition, string message)
    {
        guards++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static string FindRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Script/Performance/CanyonOcclusionProfile.cs"))) return directory.FullName;
        throw new InvalidOperationException("Unity project root not found.");
    }
}
