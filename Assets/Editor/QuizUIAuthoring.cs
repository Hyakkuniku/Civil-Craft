#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in new assets and detached UI checks; no live scene, NPC, input or save mutations.</summary>
public static class QuizUIAuthoring
{
    private const string Request = "Temp/npc-quiz-authoring-v6.request";
    private const string Report = "Temp/NPCQuizAuthoringValidation.txt";
    private const string PrefabPath = "Assets/Resources/UI/QuizUI.prefab";
    private const string ExamplePath = "Assets/Data/Quizzes/NPC Quiz Example.asset";
    private static double nextCheck;
    private static bool running;
    private static TMP_FontAsset font;
    private static Sprite rounded;
    private static bool Busy => running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
        EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer || PrefabStageUtility.GetCurrentPrefabStage() != null;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (Busy || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        CreateAndValidate();
    }

    [MenuItem("Tools/Civil Craft/Quizzes/Create and Validate Quiz UI")]
    public static void CreateAndValidate()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            Check(!Busy, "Leave Play/Prefab Mode and finish imports before creating Quiz UI.");
            running = true;
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset");
            rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/LevelResultRounded.png");
            Check(font != null && rounded != null, "Existing gameplay font or rounded UI sprite is missing.");
            var sceneStates = new Dictionary<Scene, Tuple<bool, GameObject[]>>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                sceneStates[scene] = Tuple.Create(scene.isDirty, scene.GetRootGameObjects());
            }
            Scene active = SceneManager.GetActiveScene();
            Object[] selection = Selection.objects;
            Object activeSelection = Selection.activeObject;
            var report = new StringBuilder();
            try
            {
                QuizData example = CreateExample();
                CreatePrefab();
                Validate(example, report);
                ValidateLocalAvatarVisibility(report);
                ValidateAlmanacQuizTab(report);
                foreach (var pair in sceneStates)
                    Check(pair.Key.isLoaded && pair.Key.isDirty == pair.Value.Item1 &&
                        pair.Key.GetRootGameObjects().SequenceEqual(pair.Value.Item2), "A source scene changed during detached quiz authoring.");
                Check(SceneManager.GetActiveScene() == active, "The source active scene changed.");
                report.Insert(0, "PASS: reusable QuizUI prefab and three-question example are ready. No released story or scene changed.\n");
                report.AppendLine("NOTE: Detached presentation only. No network, preferences, player saves, live input or NPC callbacks ran.");
                File.WriteAllText(Report, report.ToString());
                Debug.Log("[NPC Quiz] Created/validated prefab and example; phone/tablet/desktop layouts and click blockade passed.");
            }
            finally { Selection.objects = selection; Selection.activeObject = activeSelection; }
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Debug.LogException(error); }
        finally { running = false; }
    }

    private static QuizData CreateExample()
    {
        QuizData existing = AssetDatabase.LoadAssetAtPath<QuizData>(ExamplePath);
        if (existing != null) { Check(existing.TryValidate(out string error), error); return existing; }
        EnsureFolder("Assets/Data"); EnsureFolder("Assets/Data/Quizzes");
        Sprite beam = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/bm_ui/WoodBeam_mat_icon.png");
        Sprite road = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Elements/UI/bm_ui/WoodRoad_mat_icon.png");
        Check(beam != null && road != null, "Existing beam/road icons are missing.");
        QuizData quiz = ScriptableObject.CreateInstance<QuizData>();
        quiz.quizId = "civil-craft-npc-quiz-example-v1";
        quiz.title = "Bridge Builder Practice";
        quiz.questions = new List<QuizQuestion>
        {
            new QuizQuestion { questionId = "stable-shape", prompt = "Which shape is commonly used to keep a bridge frame stable?", correctChoiceIndex = 0,
                feedback = "Triangles help a frame keep its shape.", choices = new List<QuizChoice> { TextChoice("Triangle"), TextChoice("Square"), TextChoice("Circle") } },
            new QuizQuestion { questionId = "identify-beam", prompt = "Which picture shows a wood beam?", correctChoiceIndex = 0,
                feedback = "A beam supports the bridge structure.", choices = new List<QuizChoice> { ImageChoice(beam), ImageChoice(road) } },
            new QuizQuestion { questionId = "driving-surface", prompt = "Which choice represents the part that vehicles drive across?", correctChoiceIndex = 1,
                feedback = "The road deck provides a continuous driving surface.", choices = new List<QuizChoice> { TextChoice("A support beam"), ImageChoice(road, "Wood road deck"), ImageChoice(beam) } }
        };
        Check(quiz.TryValidate(out string validation), validation);
        AssetDatabase.CreateAsset(quiz, ExamplePath);
        EditorUtility.SetDirty(quiz); AssetDatabase.SaveAssetIfDirty(quiz);
        return quiz;
    }

    private static QuizChoice TextChoice(string text) => new QuizChoice { text = text };
    private static QuizChoice ImageChoice(Sprite image, string text = "")
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image, out string guid, out long localId);
        return new QuizChoice { image = image, text = text, imageFingerprint = guid + ":" + localId + ":" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(image)) };
    }

    private static void CreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            // Upgrade in place: keep the prefab GUID, authored controls and all existing references.
            QuizUILayout authored = existing.GetComponent<QuizUILayout>();
            if (authored != null && authored.presentationVersion >= 3 && authored.paperFrame != null && authored.paperFace != null &&
                authored.paperGroup != null && authored.npcNameplate != null && authored.npcName != null &&
                existing.GetComponent<QuizView>().pauseButton == null && authored.header.Find("Pause") == null) return;
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                QuizView view = contents.GetComponent<QuizView>();
                Check(view != null && view.IsReady, "Existing QuizUI has incomplete references; repair it before upgrading.");
                Button oldPause = view.pauseButton;
                if (oldPause == null && view.layout.header != null)
                {
                    Transform legacy = view.layout.header.Find("Pause");
                    if (legacy != null) oldPause = legacy.GetComponent<Button>();
                }
                view.pauseButton = null;
                if (oldPause != null) Object.DestroyImmediate(oldPause.gameObject);
                view.layout.EnsurePresentationElements();
                view.SetNpcPresentation(false, string.Empty);
                view.layout.ApplyLayout();
                Check(PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath) != null, "Could not upgrade the existing QuizUI prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return;
        }
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject root = Build(preview);
            root.SetActive(true); root.GetComponent<QuizView>().Panel.SetActive(false);
            ResetCanvasRoot(root.transform as RectTransform, new Vector2(1600, 900));
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Check(asset != null, "Could not save the new QuizUI prefab.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static GameObject Build(Scene scene)
    {
        GameObject root = new GameObject("QuizUI", typeof(RectTransform));
        root.SetActive(false); root.layer = 5; SceneManager.MoveGameObjectToScene(root, scene);
        Canvas canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = 100;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
        root.AddComponent<GraphicRaycaster>();
        QuizView view = root.AddComponent<QuizView>();
        QuizUILayout layout = root.AddComponent<QuizUILayout>();
        QuizUIManager manager = root.AddComponent<QuizUIManager>(); manager.view = view;
        view.layout = layout; layout.view = view;
        Image panel = Surface(root.transform, "NPCQuizPanel", Color.clear, true, false);
        QuizUILayout.Stretch(panel.rectTransform, 0, 0, 0, 0); view.panel = panel.gameObject;
        RectTransform safe = Child(panel.transform, "Safe Content").transform as RectTransform;
        QuizUILayout.Stretch(safe, 0, 0, 0, 0); layout.safeContent = safe;
        RectTransform header = Child(safe, "Header").transform as RectTransform; layout.header = header;
        view.title = Label(header, "Quiz Title", "Bridge Builder Practice", 38);
        view.progress = Label(header, "Question Progress", "Question 1 of 3", 26);
        view.questionScroll = Scroll(safe, "Question Scroll", out RectTransform questionViewport, out RectTransform questionContent);
        layout.questionViewport = questionViewport; layout.questionContent = questionContent;
        view.question = Label(questionContent, "Question", "Choose the best answer.", 31);
        view.choiceScroll = Scroll(safe, "Choice Scroll", out RectTransform choiceViewport, out RectTransform choiceContent);
        layout.choiceViewport = choiceViewport; view.choiceContent = choiceContent;
        view.choiceTemplate = Choice(choiceContent); view.choiceTemplate.gameObject.SetActive(false);
        RectTransform footer = Child(safe, "Footer").transform as RectTransform; layout.footer = footer;
        layout.feedbackScroll = Scroll(footer, "Feedback Scroll", out RectTransform feedbackViewport, out RectTransform feedbackContent);
        layout.feedbackViewport = feedbackViewport; layout.feedbackContent = feedbackContent;
        view.feedback = Label(feedbackContent, "Feedback", "Choose one answer, then press Submit.", 25);
        view.leaveButton = ActionButton(footer, "Leave Quiz", "Leave Quiz", QuizUILayout.Sand);
        view.submitButton = ActionButton(footer, "Submit", "Submit", QuizUILayout.Gold);
        view.nextButton = ActionButton(footer, "Next", "Next", QuizUILayout.Gold);
        view.finishButton = ActionButton(footer, "Finish", "Finish", QuizUILayout.Gold);
        view.retrySaveButton = ActionButton(footer, "Retry Save", "Retry Save", QuizUILayout.Gold);
        view.nextButton.gameObject.SetActive(false); view.finishButton.gameObject.SetActive(false); view.retrySaveButton.gameObject.SetActive(false);
        layout.EnsurePresentationElements();
        view.SetNpcPresentation(false, string.Empty);
        return root;
    }

    private static QuizChoiceView Choice(Transform parent)
    {
        Button button = ActionButton(parent, "Choice Template", "", QuizUILayout.Cream);
        Object.DestroyImmediate(button.GetComponentInChildren<TMP_Text>(true).gameObject);
        QuizChoiceView choice = button.gameObject.AddComponent<QuizChoiceView>();
        choice.button = button; choice.face = button.targetGraphic as Image;
        choice.number = Label(button.transform, "Choice Number", "A", 25);
        choice.label = Label(button.transform, "Choice Text", "Answer", 29);
        choice.artwork = Surface(button.transform, "Choice Image", Color.white, false, false);
        choice.artwork.preserveAspect = true;
        return choice;
    }

    private static ScrollRect Scroll(Transform parent, string name, out RectTransform viewport, out RectTransform content)
    {
        Image image = Surface(parent, name, QuizUILayout.Sand, true, false);
        viewport = image.rectTransform; image.gameObject.AddComponent<RectMask2D>();
        ScrollRect scroll = image.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport;
        content = Child(viewport, "Content").transform as RectTransform;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
        scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40; scroll.decelerationRate = .12f;
        return scroll;
    }

    private static void Validate(QuizData example, StringBuilder report)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab != null, "Quiz UI prefab is missing.");
        QuizView authored = prefab.GetComponent<QuizView>();
        Check(authored != null && authored.IsReady && prefab.GetComponent<QuizUIManager>()?.view == authored, "Quiz view/manager references are incomplete.");
        Check(!authored.Panel.activeSelf && prefab.activeSelf, "Quiz panel must start hidden with its session owner active.");
        Check(!prefab.GetComponent<QuizUIManager>().transform.IsChildOf(authored.Panel.transform), "Session owner must stay outside the hideable panel.");
        Check(authored.Panel.GetComponent<Image>().raycastTarget && authored.Panel.GetComponent<Image>().color.a == 0f,
            "Quiz backdrop must block clicks while leaving the NPC/world visible.");
        Check(authored.layout.presentationVersion == 3 && authored.layout.paperFrame != null && authored.layout.paperFace != null &&
            authored.layout.npcName != null && authored.layout.paperGroup != null, "The paper-card/NPC presentation upgrade is incomplete.");
        Check(authored.pauseButton == null && authored.layout.header.Find("Pause") == null, "The authored quiz still contains its removed Pause button.");
        Check(prefab.transform.localScale == Vector3.one && (prefab.transform as RectTransform).rect.width > 0f,
            "The authored quiz Canvas has invalid root scale/size.");
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        QuizData longQuiz = null;
        Material npcMaterial = null;
        var previewMeshes = new List<Mesh>();
        try
        {
            GameObject root = Object.Instantiate(prefab); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.GetComponent<QuizUIManager>().enabled = false;
            root.GetComponent<CanvasScaler>().enabled = false;
            QuizView view = root.GetComponent<QuizView>(); QuizUILayout layout = root.GetComponent<QuizUILayout>();
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            GameObject cameraObject = new GameObject("Detached Quiz Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false;
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -1000); camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(130, 178, 188, 255); canvas.worldCamera = camera;
            GameObject npcProxy = BuildNpcPreview(preview, previewMeshes, out npcMaterial);
            report.AppendLine("NPC rendering fixture: " + npcProxy.name + ". No NPC scripts or story state were cloned.");
            longQuiz = Object.Instantiate(example);
            longQuiz.questions[0].prompt = string.Join("\n\n", Enumerable.Repeat("Read this bridge-building question carefully. Longer prompts stay readable and scroll independently above the choices.", 18));
            longQuiz.questions[0].choices = Enumerable.Range(0, 23).Select(i => i == 1 ? ImageChoice(example.questions[2].choices[1].image,
                "Image caption: " + string.Join(" ", Enumerable.Repeat("A long mixed-choice explanation stays inside its card.", 6))) :
                TextChoice("Choice " + (i + 1) + ": " + string.Join(" ", Enumerable.Repeat("Bridge-building answer", 8)))).ToList();
            Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1024, 768), new Vector2Int(1920, 1080), new Vector2Int(720, 1280) };
            string[] names = { "phone", "tablet", "desktop", "portrait" };
            root.SetActive(true); view.Panel.SetActive(true);
            for (int sample = 0; sample < sizes.Length * 2; sample++)
            {
                int i = sample % sizes.Length;
                bool npcMode = sample >= sizes.Length;
                string name = names[i] + (npcMode ? "-npc" : "-fallback");
                Vector2Int size = sizes[i]; target = new RenderTexture(size.x, size.y, 24); target.Create();
                // Match the actual prefab CanvasScaler instead of pretending
                // every device uses one logical unit per physical pixel.
                float physicalScale = Mathf.Sqrt((size.x / 1600f) * (size.y / 900f));
                Vector2 logicalSize = new Vector2(size.x, size.y) / physicalScale;
                canvas.scaleFactor = physicalScale;
                camera.targetTexture = target; camera.aspect = (float)size.x / size.y; camera.orthographicSize = size.y * .5f;
                camera.orthographicSize = logicalSize.y * .5f;
                ResetCanvasRoot(root.transform as RectTransform, logicalSize);
                view.SetNpcPresentation(npcMode, "Bhan");
                Rect slot = layout.NpcScreenViewport;
                npcProxy.SetActive(npcMode);
                npcProxy.transform.localPosition = new Vector3((slot.center.x - .5f) * logicalSize.x, (slot.center.y - .5f) * logicalSize.y, 20f);
                npcProxy.transform.localScale = Vector3.one * Mathf.Min(slot.height * logicalSize.y * .72f, slot.width * logicalSize.x * 1.5f);
                Canvas.ForceUpdateCanvases(); view.ShowQuestion(example, 2, new string[0]); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                Check(view.title.textInfo.characterCount > 0 && view.title.textInfo.characterInfo.Take(view.title.textInfo.characterCount).Any(character => character.isVisible) &&
                    view.progress.textInfo.characterCount > 0 && view.progress.textInfo.characterInfo.Take(view.progress.textInfo.characterCount).Any(character => character.isVisible),
                    "Quiz title/progress became invisible at " + name);
                Check(layout.ColumnCount == (layout.safeContent.rect.width * physicalScale >= 760f && size.x > size.y ? 2 : 1), "Wrong choice column count for " + name);
                Check(layout.npcNameplate.gameObject.activeSelf == npcMode && !layout.npcName.richText, "NPC nameplate state/plain-text regression.");
                if (npcMode)
                {
                    Rect card = Bounds(layout.safeContent, root.transform as RectTransform);
                    Rect reserved = new Rect((slot.xMin - .5f) * logicalSize.x, (slot.yMin - .5f) * logicalSize.y,
                        slot.width * logicalSize.x, slot.height * logicalSize.y);
                    Check(!card.Overlaps(reserved), "Quiz paper covers the reserved NPC camera viewport at " + name);
                    Check(slot.xMin >= 0f && slot.yMin >= 0f && slot.xMax <= 1.001f && slot.yMax <= 1.001f, "NPC viewport leaves screen.");
                    ValidateNpcFraming(preview, slot, (float)size.x / size.y, name);
                }
                foreach (RectTransform rect in new[] { layout.header, layout.footer, layout.questionViewport, layout.choiceViewport })
                    Inside(rect, layout.safeContent, "Quiz region leaves safe content: " + rect.name);
                Check(!Bounds(layout.questionViewport, layout.safeContent).Overlaps(Bounds(layout.choiceViewport, layout.safeContent)), "Question/choice regions overlap.");
                Check(!Bounds(layout.choiceViewport, layout.safeContent).Overlaps(Bounds(layout.footer, layout.safeContent)), "Choices cover footer controls.");
                Check(!Bounds(layout.feedbackViewport, layout.footer).Overlaps(Bounds(view.leaveButton.transform as RectTransform, layout.footer)),
                    "Feedback overlaps the physical-size Leave Quiz button.");
                Check(!Bounds(layout.feedbackViewport, layout.footer).Overlaps(Bounds(view.submitButton.transform as RectTransform, layout.footer)),
                    "Feedback overlaps the physical-size Submit button.");
                Check(view.VisibleChoices.Count == example.questions[2].choices.Count, "Choices were hardcoded or lost.");
                Check((view.pauseButton == null || !view.pauseButton.gameObject.activeSelf) && layout.header.Find("Pause") == null,
                    "A Pause button is visible in the quiz at " + name);
                Check(Mathf.Abs(view.title.rectTransform.rect.width - layout.header.rect.width) < .5f &&
                    Mathf.Abs(view.progress.rectTransform.rect.width - layout.header.rect.width) < .5f,
                    "Removing Pause did not restore full-width title/progress at " + name);
                foreach (Button button in new[] { view.leaveButton, view.submitButton })
                    Check((button.transform as RectTransform).rect.height * physicalScale >= 47.9f, "Quiz touch target is shorter than 48 physical pixels: " + button.name);
                Check(view.VisibleChoices[1].artwork.preserveAspect && view.VisibleChoices[2].artwork.preserveAspect, "Choice images must preserve aspect ratio.");
                ValidateControls(view);
                PrepareGraphics(root, camera, target);
                Capture(camera, target, "Temp/npc-quiz-" + name + ".png");
                ValidateClickBlockade(root, view, camera, preview);
                ValidateAssessment(view, example, physicalScale);
                Capture(camera, target, "Temp/npc-quiz-assessment-" + name + ".png");
                view.ShowQuestion(longQuiz, 0, new string[0]); layout.ApplyLayout(); Canvas.ForceUpdateCanvases();
                Check(layout.questionContent.rect.height > layout.questionViewport.rect.height, "Long prompt is not scrollable.");
                Check(view.choiceContent.rect.height > layout.choiceViewport.rect.height, "Long/dynamic choices are not scrollable on " + names[i]);
                Check(view.VisibleChoices.Count == 23 && !view.question.richText, "Dynamic/plain-text content regression.");
                Check(view.question.fontSize * physicalScale >= 19.9f && view.VisibleChoices[0].label.fontSize * physicalScale >= 19.9f,
                    "Question or choice text is too small in physical pixels.");
                report.AppendLine("PASS: " + name + " " + size.x + "x" + size.y + ": no quiz Pause button, full-width header, safe paper/NPC regions, adaptive columns, text/image/mixed choices, Submit/retry/Next/Finish, independent long-content scrolling and transparent full-screen click blockade.");
                camera.targetTexture = null; Release(target); target = null;
            }
        }
        finally
        {
            if (longQuiz != null) Object.DestroyImmediate(longQuiz);
            Release(target); EditorSceneManager.ClosePreviewScene(preview);
            if (npcMaterial != null) Object.DestroyImmediate(npcMaterial);
            foreach (Mesh mesh in previewMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }

    private static GameObject BuildNpcPreview(Scene preview, List<Mesh> previewMeshes, out Material material)
    {
        material = null;
        NPCProgressionManager source = Resources.FindObjectsOfTypeAll<NPCProgressionManager>().FirstOrDefault(candidate =>
            candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded &&
            candidate.name.IndexOf("bhan", StringComparison.OrdinalIgnoreCase) >= 0);
        if (source != null)
        {
            // Copy only static mesh/material presentation. Never instantiate NPC
            // scripts, enable the original NPC or run progression/Animator lifecycle.
            GameObject real = new GameObject("Detached Bhan Mesh Preview"); SceneManager.MoveGameObjectToScene(real, preview);
            GameObject model = new GameObject("Render Meshes Only"); SceneManager.MoveGameObjectToScene(model, preview); model.transform.SetParent(real.transform, false);
            bool found = false; Bounds bounds = default;
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer.GetComponent<TMP_Text>() != null) continue;
                bool visiblePart = true;
                for (Transform cursor = renderer.transform; cursor != null && cursor != source.transform; cursor = cursor.parent)
                    if (!cursor.gameObject.activeSelf) { visiblePart = false; break; }
                if (!visiblePart) continue;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    mesh = new Mesh(); skinned.BakeMesh(mesh); previewMeshes.Add(mesh);
                }
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                if (mesh == null) continue;
                GameObject part = new GameObject(renderer.name, typeof(MeshFilter), typeof(MeshRenderer));
                SceneManager.MoveGameObjectToScene(part, preview); part.layer = 30; part.transform.SetParent(model.transform, false);
                part.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation); part.transform.localScale = renderer.transform.lossyScale;
                part.GetComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer copy = part.GetComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = ShadowCastingMode.Off; copy.receiveShadows = false;
                if (!found) { bounds = copy.bounds; found = true; } else bounds.Encapsulate(copy.bounds);
            }
            if (found && bounds.size.y > .01f)
            {
                model.transform.localPosition = -bounds.center;
                real.transform.localScale = Vector3.one / bounds.size.y;
                // Normalize in an inner transform so the outer proxy can be
                // positioned/scaled using the same screen-slot fixture as fallback.
                model.transform.localPosition /= bounds.size.y; model.transform.localScale /= bounds.size.y;
                real.transform.localScale = Vector3.one;
                Vector3 facing = Vector3.ProjectOnPlane(source.transform.forward, Vector3.up);
                if (facing.sqrMagnitude > .001f) real.transform.rotation = Quaternion.FromToRotation(facing.normalized, Vector3.back);
                GameObject lightObject = new GameObject("Detached NPC Preview Light", typeof(Light)); SceneManager.MoveGameObjectToScene(lightObject, preview);
                Light light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f; light.cullingMask = 1 << 30;
                lightObject.transform.rotation = Quaternion.Euler(30f, -25f, 0f);
                return real;
            }
            Object.DestroyImmediate(real);
        }
        // A standalone prefab check may run with no story scene loaded.
        GameObject proxy = new GameObject("Detached NPC Camera Placeholder"); SceneManager.MoveGameObjectToScene(proxy, preview);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        material = new Material(shader); material.color = new Color32(104, 77, 53, 255);
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        SceneManager.MoveGameObjectToScene(body, preview); body.transform.SetParent(proxy.transform, false); body.layer = 30;
        body.transform.localScale = new Vector3(.3f, .29f, .2f); body.transform.localPosition = new Vector3(0f, -.13f, 0f);
        body.GetComponent<MeshRenderer>().sharedMaterial = material; Object.DestroyImmediate(body.GetComponent<Collider>());
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        SceneManager.MoveGameObjectToScene(head, preview); head.transform.SetParent(proxy.transform, false); head.layer = 30;
        head.transform.localScale = Vector3.one * .32f; head.transform.localPosition = new Vector3(0f, .3f, 0f);
        head.GetComponent<MeshRenderer>().sharedMaterial = material; Object.DestroyImmediate(head.GetComponent<Collider>());
        return proxy;
    }

    private static void ValidateNpcFraming(Scene scene, Rect slot, float aspect, string name)
    {
        MethodInfo framing = typeof(QuizNPCPresentation).GetMethod("TryFramingPose", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Check(framing != null, "NPC presentation is missing the pure framing calculation.");
        GameObject cameraObject = new GameObject("Detached NPC Projection Check", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.aspect = aspect; camera.fieldOfView = 42f;
        try
        {
            Bounds bounds = new Bounds(new Vector3(2f, 1f, 3f), new Vector3(.7f, 1.8f, .55f));
            MethodInfo focusMethod = typeof(QuizNPCPresentation).GetMethod("SpeakerFocusBounds", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Check(focusMethod != null, "NPC presentation is missing the closer speaker focus calculation.");
            Bounds focused = (Bounds)focusMethod.Invoke(null, new object[] { bounds });
            Check(Mathf.Approximately(focused.max.y, bounds.max.y) && focused.min.y > bounds.min.y &&
                Mathf.Approximately(focused.size.x, bounds.size.x) && Mathf.Approximately(focused.size.z, bounds.size.z),
                "Closer NPC composition must retain the head and body width without cropping the top.");
            foreach (Bounds framedBounds in new[] { bounds, focused })
            foreach (Vector3 direction in new[] { Vector3.forward, Vector3.right, Vector3.back })
            {
                object[] arguments = { framedBounds, direction, aspect, 42f, new Rect(0, 0, 1, 1), slot, Vector3.zero, Quaternion.identity };
                Check((bool)framing.Invoke(null, arguments), "NPC framing is unavailable for a valid " + name + " viewport.");
                camera.transform.SetPositionAndRotation((Vector3)arguments[6], (Quaternion)arguments[7]);
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = framedBounds.center + Vector3.Scale(framedBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 projected = camera.WorldToViewportPoint(point);
                    Check(projected.z > 0f && projected.x >= slot.xMin - .001f && projected.x <= slot.xMax + .001f &&
                        projected.y >= slot.yMin - .001f && projected.y <= slot.yMax + .001f,
                        "NPC body leaves the reserved viewport: " + name + " / " + direction + " / " + projected + " vs " + slot);
                }
            }
            object[] invalid = { new Bounds(Vector3.zero, Vector3.zero), Vector3.forward, aspect, 42f,
                new Rect(0, 0, 1, 1), slot, Vector3.zero, Quaternion.identity };
            Check(!(bool)framing.Invoke(null, invalid), "Missing NPC geometry must use the full-screen fallback.");
        }
        finally { Object.DestroyImmediate(cameraObject); }
    }

    private static void ResetCanvasRoot(RectTransform rect, Vector2 size)
    {
        // A new overlay Canvas in a preview scene has no display and Unity can
        // serialize zero scale/size. Author usable root values explicitly.
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition3D = Vector3.zero;
        rect.sizeDelta = size;
    }

    private static void ValidateLocalAvatarVisibility(StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Material avatarMaterial = null, speakerMaterial = null;
        var visibility = new QuizLocalAvatarVisibility();
        try
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Check(shader != null, "Unlit shader missing from detached avatar camera check.");
            avatarMaterial = new Material(shader); avatarMaterial.color = new Color(.95f, .025f, .025f, 1f);
            speakerMaterial = new Material(shader); speakerMaterial.color = new Color(.025f, .95f, .025f, 1f);
            GameObject avatar = new GameObject("Detached Local Avatar Visuals"); SceneManager.MoveGameObjectToScene(avatar, preview);
            Renderer avatarRenderer = PreviewCube(preview, avatar.transform, "Local Avatar Occluder", new Vector3(0f, 0f, 0f), Vector3.one * 1.6f, avatarMaterial);
            Renderer initiallyHidden = PreviewCube(preview, avatar.transform, "Initially Force-Off Accessory", new Vector3(1.25f, 0f, -.1f), Vector3.one * .5f, avatarMaterial);
            initiallyHidden.forceRenderingOff = true;
            Renderer initiallyDisabled = PreviewCube(preview, avatar.transform, "Initially Disabled Accessory", new Vector3(-1.25f, 0f, -.1f), Vector3.one * .5f, avatarMaterial);
            initiallyDisabled.enabled = false;
            Renderer speaker = PreviewCube(preview, null, "NPC Behind Local Avatar", new Vector3(0f, 0f, 1f), Vector3.one * 1.6f, speakerMaterial);
            Camera owned = PreviewAvatarCamera(preview, "Quiz Gameplay Camera");
            Camera secondary = PreviewAvatarCamera(preview, "Secondary Map Camera");
            int maskBefore = owned.cullingMask;
            target = new RenderTexture(256, 256, 24); target.Create();
            ColorCount before = RenderAvatarColors(owned, target, "Temp/npc-quiz-avatar-before.png");
            Check(before.red > 1000 && before.green < 100, "Detached local avatar must initially occlude the NPC.");
            Check(visibility.Begin(owned, avatar.transform), "Could not begin detached local-avatar camera exclusion.");
            ColorCount mapBefore = RenderAvatarColors(secondary, target, "Temp/npc-quiz-avatar-secondary-before.png");
            Check(mapBefore.red > 1000 && mapBefore.green < 100, "Quiz exclusion hid the local avatar from the secondary camera.");
            Check(!avatarRenderer.forceRenderingOff && initiallyHidden.forceRenderingOff && !initiallyDisabled.enabled,
                "Beginning/secondary rendering changed persistent avatar visibility.");
            ColorCount quiz = RenderAvatarColors(owned, target, "Temp/npc-quiz-avatar-owned.png");
            Check(quiz.green > 1000 && quiz.red < 100, "Quiz camera still renders the foreground local avatar over its NPC.");
            Check(!avatarRenderer.forceRenderingOff && initiallyHidden.forceRenderingOff && !initiallyDisabled.enabled && speaker.enabled &&
                !speaker.forceRenderingOff && owned.cullingMask == maskBefore && avatar.activeSelf,
                "Quiz render did not restore original renderer flags or changed shared objects/layers.");
            ColorCount mapAfter = RenderAvatarColors(secondary, target, "Temp/npc-quiz-avatar-secondary-after.png");
            Check(mapAfter.red > 1000 && mapAfter.green < 100, "Local avatar did not return for the secondary camera after quiz rendering.");
            visibility.End(); visibility.End();
            ColorCount restored = RenderAvatarColors(owned, target, "Temp/npc-quiz-avatar-restored.png");
            Check(restored.red > 1000 && restored.green < 100 && !avatarRenderer.forceRenderingOff && initiallyHidden.forceRenderingOff,
                "Ending quiz camera exclusion failed to restore the local avatar exactly.");

            // Teardown may occur during a render callback (scene transition or
            // cancellation). The already-entered scope must restore immediately.
            Check(visibility.Begin(owned, avatar.transform), "Could not reopen detached avatar exclusion.");
            Action<ScriptableRenderContext, Camera> cancelSrp = (context, camera) => { if (camera == owned) visibility.End(); };
            Camera.CameraCallback cancelBuiltin = camera => { if (camera == owned) visibility.End(); };
            try
            {
                RenderPipelineManager.beginCameraRendering += cancelSrp;
                Camera.onPreCull += cancelBuiltin;
                ColorCount cancelled = RenderAvatarColors(owned, target, "Temp/npc-quiz-avatar-cancelled.png");
                Check(cancelled.red > 1000 && !avatarRenderer.forceRenderingOff && initiallyHidden.forceRenderingOff,
                    "Mid-render cancellation retained a hidden-avatar renderer flag.");
            }
            finally { RenderPipelineManager.beginCameraRendering -= cancelSrp; Camera.onPreCull -= cancelBuiltin; }
            visibility.PrepareForNewFrame(); visibility.End();
            Check(!avatarRenderer.forceRenderingOff && initiallyHidden.forceRenderingOff && !initiallyDisabled.enabled,
                "Visibility recovery/teardown failed to preserve initially hidden or disabled accessories.");
            report.AppendLine("PASS: actual detached camera renders hide only the local avatar in the owned quiz shot; secondary map camera retains it before/after; NPC visible; original force-off/enabled flags, cancellation, double End and teardown preserved.");
        }
        finally
        {
            visibility.End(); Release(target); EditorSceneManager.ClosePreviewScene(preview);
            if (avatarMaterial != null) Object.DestroyImmediate(avatarMaterial);
            if (speakerMaterial != null) Object.DestroyImmediate(speakerMaterial);
        }
    }

    private struct ColorCount { public int red, green; }
    private static ColorCount RenderAvatarColors(Camera camera, RenderTexture target, string path)
    {
        camera.targetTexture = target; Render(camera, target);
        RenderTexture previous = RenderTexture.active;
        Texture2D texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            ColorCount count = default;
            foreach (Color32 pixel in texture.GetPixels32())
            {
                if (pixel.r > 120 && pixel.r > pixel.g * 2 && pixel.r > pixel.b * 2) count.red++;
                if (pixel.g > 120 && pixel.g > pixel.r * 2 && pixel.g > pixel.b * 2) count.green++;
            }
            return count;
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }

    private static Camera PreviewAvatarCamera(Scene preview, string name)
    {
        GameObject item = new GameObject(name, typeof(Camera)); SceneManager.MoveGameObjectToScene(item, preview);
        Camera camera = item.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false;
        camera.transform.position = new Vector3(0f, 0f, -5f); camera.orthographic = true; camera.orthographicSize = 2f;
        camera.nearClipPlane = .1f; camera.farClipPlane = 20f; camera.aspect = 1f; camera.cullingMask = 1 << 30;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .025f, .95f, 1f);
        return camera;
    }

    private static Renderer PreviewCube(Scene preview, Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = name; item.layer = 30;
        SceneManager.MoveGameObjectToScene(item, preview); if (parent != null) item.transform.SetParent(parent, false);
        item.transform.position = position; item.transform.localScale = scale;
        Renderer renderer = item.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        Object.DestroyImmediate(item.GetComponent<Collider>()); return renderer;
    }

    private static void ValidateControls(QuizView view)
    {
        Check(!view.submitButton.interactable, "Submit must be disabled before selection.");
        int submitted = 0, selected = -1, next = 0, finished = 0;
        Action submitAction = () => submitted++;
        Action<int> choiceAction = index => selected = index;
        Action nextAction = () => next++;
        Action finishAction = () => finished++;
        view.SubmitRequested += submitAction; view.ChoiceSelected += choiceAction;
        view.NextRequested += nextAction; view.FinishRequested += finishAction;
        var click = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
        try
        {
        ExecuteEvents.Execute(view.submitButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(submitted == 0, "Disabled Submit accepted a native UI click.");
        ExecuteEvents.Execute(view.VisibleChoices[1].button.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(selected == 1 && view.submitButton.interactable, "Choice click did not select and enable Submit.");
        ExecuteEvents.Execute(view.submitButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(submitted == 1, "Submit click did not dispatch its event exactly once.");
        view.ShowFeedback(false, "Must not reveal the correct choice.");
        Check(view.feedback.text == "Try again." && view.VisibleChoices.All(card => card.button.interactable), "Wrong answer revealed content or locked retry.");
        view.SetSelection(0); Check(view.feedback.text != "Try again.", "New selection retained stale wrong feedback.");
        view.ShowFeedback(true, "An explanation.");
        Check(view.nextButton.gameObject.activeSelf && view.VisibleChoices.All(card => !card.button.interactable), "Correct answer should lock choices until Next.");
        ExecuteEvents.Execute(view.VisibleChoices[1].button.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(selected == 1, "Locked correct-question choices still dispatched input.");
        ExecuteEvents.Execute(view.nextButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(next == 1, "Next click did not dispatch exactly once.");
        view.ShowSaveError("Save test error");
        Check(view.retrySaveButton.gameObject.activeSelf && view.leaveButton.interactable && !view.nextButton.gameObject.activeSelf, "Save failure must allow retry/exit without advancing.");
        // Return to the mixed question before the screenshot/click checks.
        QuizData example = AssetDatabase.LoadAssetAtPath<QuizData>(ExamplePath);
        view.ShowCompletion(example); Check(view.finishButton.gameObject.activeSelf, "Completion should offer Finish.");
        ExecuteEvents.Execute(view.finishButton.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(finished == 1, "Finish click did not dispatch exactly once.");
        view.ShowQuestion(example, 2, new string[0]);
        }
        finally
        {
            view.SubmitRequested -= submitAction; view.ChoiceSelected -= choiceAction;
            view.NextRequested -= nextAction; view.FinishRequested -= finishAction;
        }
    }

    private static void ValidateAssessment(QuizView view, QuizData data, float physicalScale)
    {
        view.ShowAssessmentQuestion(data, 0, -1, 0);
        Canvas.ForceUpdateCanvases();
        Check(!view.leaveButton.gameObject.activeSelf && !view.submitButton.interactable, "Assessment must hide Leave and disable early Submit.");
        Check(view.previousButton != null && !view.previousButton.interactable && view.nextButton.interactable,
            "Assessment navigation at the first question is incorrect.");
        int submitted = 0, previous = 0;
        Action submittedAction = () => submitted++;
        Action previousAction = () => previous++;
        view.SubmitRequested += submittedAction;
        view.PreviousRequested += previousAction;
        var click = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
        try
        {
            ExecuteEvents.Execute(view.submitButton.gameObject, click, ExecuteEvents.pointerClickHandler);
            Check(submitted == 0, "Early native Submit click must be ignored.");
            view.ShowAssessmentQuestion(data, data.QuestionCount - 1, 1, data.QuestionCount);
            Canvas.ForceUpdateCanvases();
            Check(view.submitButton.interactable && !view.nextButton.interactable && view.previousButton.interactable,
                "Answered assessment must allow Submit and Previous, not past-end Next.");
            foreach (Button button in new[] { view.previousButton, view.nextButton, view.submitButton })
            {
                Inside(button.transform as RectTransform, view.layout.footer, "Assessment button outside footer: " + button.name);
                Check((button.transform as RectTransform).rect.height * physicalScale >= 47.9f,
                    "Assessment action target is too short: " + button.name);
            }
            Check(!Bounds(view.previousButton.transform as RectTransform, view.layout.footer).Overlaps(Bounds(view.nextButton.transform as RectTransform, view.layout.footer)) &&
                !Bounds(view.nextButton.transform as RectTransform, view.layout.footer).Overlaps(Bounds(view.submitButton.transform as RectTransform, view.layout.footer)),
                "Assessment navigation/action buttons overlap.");
            // The control now exists before this repeated ShowQuestion, exercising
            // the authored/reused-button binding path, not only first-time cloning.
            view.ShowAssessmentQuestion(data, data.QuestionCount - 1, 1, data.QuestionCount);
            ExecuteEvents.Execute(view.previousButton.gameObject, click, ExecuteEvents.pointerClickHandler);
            Check(previous == 1, "Authored/reused Previous must dispatch exactly once.");
            ExecuteEvents.Execute(view.submitButton.gameObject, click, ExecuteEvents.pointerClickHandler);
            Check(submitted == 1, "Native assessment Submit dispatch failed.");
            view.ShowAssessmentResult(data, 0);
            Check(view.finishButton.gameObject.activeSelf && view.question.text.Contains("0 / " + data.QuestionCount) &&
                !view.previousButton.gameObject.activeSelf && !view.leaveButton.gameObject.activeSelf, "Zero-score result must offer Continue, not fail/retry gating.");
            Check(view.finishButton.GetComponentInChildren<TMP_Text>(true).text == "Continue", "Assessment needs Continue label.");
            view.ShowAssessmentQuestion(data, 0, 1, 1);
        }
        finally { view.SubmitRequested -= submittedAction; view.PreviousRequested -= previousAction; }
    }

    private static void ValidateAlmanacQuizTab(StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        try
        {
            GameObject root = new GameObject("Detached Almanac Quiz", typeof(RectTransform), typeof(Canvas));
            root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            AlmanacManager book = root.AddComponent<AlmanacManager>(); book.almanacCanvas = root;
            RectTransform paper = Child(root.transform, "Open Book").transform as RectTransform;
            paper.anchorMin = new Vector2(.06f, .04f); paper.anchorMax = new Vector2(.94f, .85f); paper.offsetMin = paper.offsetMax = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                Button button = ActionButton(root.transform, "Existing Tab " + i, "Tab " + i, QuizUILayout.Sand);
                RectTransform tabRect = button.transform as RectTransform;
                tabRect.anchorMin = new Vector2(.05f + i * .09f, .925f); tabRect.anchorMax = new Vector2(.125f + i * .09f, .925f);
                tabRect.sizeDelta = new Vector2(0, 100); tabRect.anchoredPosition = new Vector2(0, 4);
                Image left = Surface(paper, "Left " + i, QuizUILayout.Cream, false, false);
                Image right = Surface(paper, "Right " + i, QuizUILayout.Cream, false, false);
                left.rectTransform.anchorMin = new Vector2(.02f, .04f); left.rectTransform.anchorMax = new Vector2(.498f, .96f);
                right.rectTransform.anchorMin = new Vector2(.502f, .04f); right.rectTransform.anchorMax = new Vector2(.98f, .96f);
                left.rectTransform.offsetMin = left.rectTransform.offsetMax = right.rectTransform.offsetMin = right.rectTransform.offsetMax = Vector2.zero;
                book.categories.Add(new AlmanacCategory { categoryName = "Existing " + i, tabType = (AlmanacTabType)i, tabButton = button, leftPageZone = left.transform, rightPageZone = right.transform });
            }
            Vector2[] oldMin = book.categories.Select(category => ((RectTransform)category.tabButton.transform).anchorMin).ToArray();
            AlmanacQuizAccess.Install(book); AlmanacQuizAccess.Install(book);
            Check(book.categories.Count == 5, "Quiz category install is not idempotent.");
            AlmanacCategory quizCategory = book.categories[4];
            Check(quizCategory.tabType == AlmanacTabType.Quiz && !quizCategory.tabButton.interactable, "Quiz tab must exist but be disabled without an NPC offer.");
            TMP_Text quizTabLabel = quizCategory.tabButton.GetComponentInChildren<TMP_Text>(true);
            Check(quizTabLabel.color.r > .7f && quizTabLabel.color.g > .65f, "Disabled Quiz tab label must contrast with the dark tab face.");
            for (int i = 0; i < 4; i++)
            {
                Check(((RectTransform)book.categories[i].tabButton.transform).anchorMin == oldMin[i], "Adding Quiz moved an existing tab.");
                book.categories[i].leftPageZone.gameObject.SetActive(false); book.categories[i].rightPageZone.gameObject.SetActive(false);
            }
            Check(quizCategory.leftPageZone.GetComponent<Image>() != null && quizCategory.rightPageZone.GetComponent<Image>() != null,
                "Quiz category lost the authored paper backgrounds.");
            Transform rightPage = quizCategory.rightPageZone.Find("Quiz Instructions");
            Button start = rightPage.Find("Start Quiz").GetComponent<Button>();
            Check(!start.gameObject.activeSelf, "No NPC offer must not provide a playable quiz.");
            // Presentation fixture only: simulate offered text, never call TryOffer or touch player data.
            start.gameObject.SetActive(true); start.interactable = true;
            quizCategory.tabButton.interactable = true;
            book.OnCategoryChanged?.Invoke(4);
            Check(quizTabLabel.color == QuizUILayout.Gold, "Real selected Quiz state must use the high-contrast gold label.");
            book.OnCategoryChanged?.Invoke(0);
            Check(quizTabLabel.color == QuizUILayout.Cream, "Unselected available Quiz state must use the high-contrast cream label.");
            book.OnCategoryChanged?.Invoke(4);
            TMP_Text title = quizCategory.leftPageZone.Find("Quiz Introduction/Quiz Title").GetComponent<TMP_Text>();
            TMP_Text description = quizCategory.leftPageZone.Find("Quiz Introduction/Quiz Guidance").GetComponent<TMP_Text>();
            title.text = "Beam Bridge Quiz";
            description.text = "Review your lessons before starting.\n\nAnswer all 5 questions, then press Submit. You can move between questions before submitting.\n\nOnce started, finish the quiz before returning to the Almanac. Any score lets you continue; no rewards are added.";
            GameObject cameraObject = new GameObject("Detached Almanac Quiz Camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.enabled = false; camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -1000); camera.nearClipPlane = .1f; camera.farClipPlane = 2000;
            camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(115, 96, 75, 255);
            canvas.worldCamera = camera;
            // Disable the copied behavior: this fixture must never run live book/save lifecycle methods.
            book.enabled = false;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            root.SetActive(true);
            Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1024, 768), new Vector2Int(1920, 1080) };
            foreach (Vector2Int size in sizes)
            {
                target = new RenderTexture(size.x, size.y, 24); target.Create();
                float scale = Mathf.Sqrt((size.x / 1600f) * (size.y / 900f));
                Vector2 logical = new Vector2(size.x, size.y) / scale;
                canvas.scaleFactor = scale; ResetCanvasRoot(root.transform as RectTransform, logical);
                camera.targetTexture = target; camera.aspect = (float)size.x / size.y; camera.orthographicSize = logical.y * .5f;
                PrepareGraphics(root, camera, target);
                foreach (TMP_Text text in new[] { title, description })
                    Check(text.GetPreferredValues(text.text, text.rectTransform.rect.width, 0).y <= text.rectTransform.rect.height + 1f,
                        "Almanac Quiz guidance clips at " + size + ": " + text.name);
                foreach (Button button in rightPage.GetComponentsInChildren<Button>(true))
                    Check(((RectTransform)button.transform).rect.height * scale >= 47.9f, "Almanac Quiz action is not a 48px touch target at " + size);
                Check(!Bounds(quizCategory.tabButton.transform as RectTransform, root.transform as RectTransform).Overlaps(
                    Bounds(book.categories[3].tabButton.transform as RectTransform, root.transform as RectTransform)), "Quiz tab overlaps Materials tab.");
                Capture(camera, target, "Temp/npc-quiz-almanac-" + size.x + "x" + size.y + ".png");
                camera.targetTexture = null; Release(target); target = null;
            }
            report.AppendLine("PASS: Almanac Quiz tab added once without moving existing tabs; inactive without NPC offer; paper backgrounds, Start/Back, readable phone/tablet/desktop introduction and separate touch targets.");
        }
        finally { Release(target); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void ValidateClickBlockade(GameObject root, QuizView view, Camera camera, Scene scene)
    {
        GameObject eventObject = new GameObject("Detached Quiz Event System"); eventObject.SetActive(false); SceneManager.MoveGameObjectToScene(eventObject, scene);
        EventSystem events = eventObject.AddComponent<EventSystem>();
        try
        {
            var pointer = new PointerEventData(events) { position = new Vector2(camera.pixelWidth * .01f, camera.pixelHeight * .01f) };
            var hits = new List<RaycastResult>(); root.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Check(hits.Any(hit => hit.gameObject == view.Panel), "Opaque quiz backdrop does not receive empty-space clicks. " +
                NativeGeometry(view.Panel, camera, pointer.position, hits));
            pointer.position = camera.WorldToScreenPoint(view.leaveButton.transform.position); hits.Clear();
            root.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Check(hits.Count > 0 && hits.OrderByDescending(hit => hit.depth).First().gameObject.transform.IsChildOf(view.leaveButton.transform), "Leave Quiz hit target is blocked.");
            foreach (Button button in new[] { view.submitButton, view.VisibleChoices[0].button })
            {
                RectTransform target = button.transform as RectTransform;
                Vector3 clickPoint = target.TransformPoint(target.rect.center);
                if (button == view.VisibleChoices[0].button)
                {
                    // Choice RectTransforms use a top-left pivot; its transform
                    // position is not the touch center and can sit on a mask edge.
                    Rect item = Bounds(target, view.layout.choiceViewport), area = view.layout.choiceViewport.rect;
                    Rect visible = Rect.MinMaxRect(Mathf.Max(item.xMin, area.xMin), Mathf.Max(item.yMin, area.yMin),
                        Mathf.Min(item.xMax, area.xMax), Mathf.Min(item.yMax, area.yMax));
                    float scale = root.GetComponent<Canvas>().scaleFactor;
                    Check(visible.width * scale >= 48f && visible.height * scale >= 48f,
                        "First choice has no 48px visible touch area: " + item + " in " + area);
                    clickPoint = view.layout.choiceViewport.TransformPoint(visible.center);
                }
                pointer.position = camera.WorldToScreenPoint(clickPoint); hits.Clear();
                root.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Check(hits.Count > 0 && (hits.OrderByDescending(hit => hit.depth).First().gameObject.transform == button.transform ||
                    hits.OrderByDescending(hit => hit.depth).First().gameObject.transform.IsChildOf(button.transform)),
                    "Quiz action/choice hit target is blocked: " + button.name + "; " +
                    NativeGeometry(button.gameObject, camera, pointer.position, hits) + "; choices=" + view.layout.choiceViewport.rect +
                    "; content=" + view.choiceContent.anchoredPosition + "/" + view.choiceContent.rect);
            }
        }
        finally { Object.DestroyImmediate(eventObject); }
    }

    private static string NativeGeometry(GameObject panel, Camera camera, Vector2 point, IEnumerable<RaycastResult> hits)
    {
        Image image = panel.GetComponent<Image>();
        Vector3[] corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
        return "camera=" + camera.pixelRect + "; point=" + point + "; panel=" + image.rectTransform.rect +
            "; corners=" + corners[0] + "/" + corners[2] + "; active=" + image.isActiveAndEnabled +
            "; depth=" + image.depth + "; cull=" + image.canvasRenderer.cull + "; contains=" +
            RectTransformUtility.RectangleContainsScreenPoint(image.rectTransform, point, camera) +
            "; registered=" + GraphicRegistry.GetRaycastableGraphicsForCanvas(image.canvas).Contains(image) +
            "; hits=" + string.Join(",", hits.Select(hit => hit.gameObject.name + "/" + hit.depth));
    }

    private static void PrepareGraphics(GameObject root, Camera camera, RenderTexture target)
    {
        Canvas.ForceUpdateCanvases();
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>()) { graphic.SetAllDirty(); graphic.Rebuild(CanvasUpdate.PreRender); }
        Canvas.ForceUpdateCanvases(); Render(camera, target); Canvas.ForceUpdateCanvases();
    }
    private static void Capture(Camera camera, RenderTexture target, string path)
    {
        Render(camera, target); RenderTexture previous = RenderTexture.active;
        Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    private static void Render(Camera camera, RenderTexture target)
    {
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        else camera.Render();
    }
    private static void Release(RenderTexture target) { if (target == null) return; target.Release(); Object.DestroyImmediate(target); }
    private static Rect Bounds(RectTransform target, RectTransform parent)
    {
        Vector3[] corners = new Vector3[4]; target.GetWorldCorners(corners); Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (Vector3 corner in corners) { Vector2 point = parent.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static void Inside(RectTransform target, RectTransform parent, string message)
    {
        Rect bounds = Bounds(target, parent), area = parent.rect;
        Check(bounds.xMin >= area.xMin - .5f && bounds.yMin >= area.yMin - .5f && bounds.xMax <= area.xMax + .5f && bounds.yMax <= area.yMax + .5f, message);
    }
    private static GameObject Child(Transform parent, string name)
    {
        GameObject child = new GameObject(name, typeof(RectTransform)); child.layer = parent.gameObject.layer;
        SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene); child.transform.SetParent(parent, false); return child;
    }
    private static Image Surface(Transform parent, string name, Color color, bool raycast, bool roundedShape = true)
    {
        Image image = Child(parent, name).AddComponent<Image>(); image.color = color; image.raycastTarget = raycast;
        if (roundedShape) { image.sprite = rounded; image.type = Image.Type.Sliced; }
        return image;
    }
    private static TMP_Text Label(Transform parent, string name, string value, float size)
    {
        TMP_Text text = Child(parent, name).AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSharedMaterial = font.material;
        text.text = value; text.fontSize = size; text.color = QuizUILayout.Ink; text.richText = false; text.raycastTarget = false;
        text.enableAutoSizing = false; text.enableWordWrapping = true; text.alignment = TextAlignmentOptions.TopLeft;
        text.overflowMode = TextOverflowModes.Overflow; text.margin = Vector4.zero; return text;
    }
    private static Button ActionButton(Transform parent, string name, string value, Color fill)
    {
        Image frame = Surface(parent, name, QuizUILayout.Ink, true); Image face = Surface(frame.transform, "Face", fill, false);
        QuizUILayout.Stretch(face.rectTransform, 3, 3, 3, 3);
        Button button = frame.gameObject.AddComponent<Button>(); button.targetGraphic = face;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors; colors.highlightedColor = colors.selectedColor = new Color(1, .95f, .82f);
        colors.pressedColor = new Color(.85f, .76f, .61f); colors.disabledColor = new Color(.65f, .62f, .58f); button.colors = colors;
        TMP_Text text = Label(frame.transform, "Label", value, 27); text.alignment = TextAlignmentOptions.Center;
        QuizUILayout.Stretch(text.rectTransform, 12, 6, 12, 6); return button;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

[CustomEditor(typeof(QuizData))]
public sealed class QuizDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Questions run in this list's order. Each choice may use text, an image, or both. Correct Choice Index is zero-based (0 = first choice). IDs remain stable for saved progress; editing quiz content resets only this quiz's saved answers.", MessageType.Info);
        DrawDefaultInspector();
        QuizData quiz = (QuizData)target;
        if (quiz.assessmentMode)
            EditorGUILayout.HelpBox("Assessment mode: the NPC offers this quiz in the Almanac's Quiz tab. All questions are answered before one Submit; any score can Continue. Leave is locked after Start. No rewards are granted.", MessageType.Info);
        if (!quiz.TryValidate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        else EditorGUILayout.HelpBox("Ready: " + quiz.questions.Count + " questions. This asset is not added to any NPC until you assign it to a Quiz sequence step.", MessageType.Info);
    }
}
#endif
