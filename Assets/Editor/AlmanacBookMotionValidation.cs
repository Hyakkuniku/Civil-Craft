using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in, isolated edit-mode coroutine checks. Does not open a game or touch saves.</summary>
[InitializeOnLoad]
public static class AlmanacBookMotionValidation
{
    private const string Request = "Temp/almanac-book-motion-validation-v4.request";
    private const string Report = "Temp/AlmanacBookMotionValidation.txt";
    private static Scene preview;
    private static AlmanacBookMotion motion;
    private static RectTransform[] pages;
    private static Vector3[][] originalCorners;
    private static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    private static int stage, swaps;
    private static double deadline;

    static AlmanacBookMotionValidation() { EditorApplication.update += Update; AssemblyReloadEvents.beforeAssemblyReload += Cleanup; }
    private static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            if (motion == null)
            {
                if (!File.Exists(Request)) return;
                File.Delete(Request); Begin();
            }
            // Scene imports/previews can block editor updates for several seconds.
            // This is a watchdog for the test runner, not an animation duration.
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Paper-turn validation stalled for thirty seconds.");
            int budget = 32;
            while (work.Count > 0 && budget-- > 0)
            {
                IEnumerator routine = work.Peek();
                if (!routine.MoveNext()) { work.Pop(); continue; }
                AssertBindingStationary();
                if (routine.Current is IEnumerator nested) { work.Push(nested); continue; }
                return;
            }
            if (work.Count != 0) return;
            AssertRestored();
            if (swaps != 1) throw new Exception("Content replacement count: " + swaps);
            ++stage; swaps = 0; deadline = EditorApplication.timeSinceStartup + 30;
            if (stage == 2)
            {
                // Start a real turn so it captures poses and blocks input, then
                // emulate the bent-sheet frame directly. An unfocused editor may
                // skip that transient frame entirely; don't wait for it to render.
                IEnumerator interrupted = motion.Turn(pages[0], pages[1], pages[2], pages[3], () => ++swaps, true);
                if (!interrupted.MoveNext() || !motion.IsTurning) throw new Exception("Interrupted turn did not start.");
                pages[1].localRotation = Quaternion.Euler(0, 45, 0);
                pages[1].GetComponent<Image>().color = Color.gray;
                motion.Cancel(); AssertRestored();
                (interrupted as IDisposable)?.Dispose();
                if (swaps != 0) throw new Exception("Cancelled turn replaced content before the midpoint.");
                File.WriteAllText(Report, "PASS: fixed/stretched sheets keep binding stationary during forward/backward turns; content replaced once; cancellation restores corners, pivots, colors and input. Chapter changes keep zone controllers active while only selected paper/furniture renders. No runtime UI added. Temporary objects only; live navigation still requires testing.\n");
                Cleanup(); return;
            }
            work.Push(motion.Turn(pages[0], pages[1], pages[2], pages[3], () => ++swaps, stage != 1));
        }
        catch (Exception error) { File.WriteAllText(Report, "FAIL: " + error); Cleanup(); Debug.LogException(error); }
    }

    private static void Begin()
    {
        preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("TemporaryBookMotionTest", typeof(RectTransform)); SceneManager.MoveGameObjectToScene(root, preview);
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1280, 800);
        motion = root.AddComponent<AlmanacBookMotion>();
        CheckChapterVisibility(root);
        pages = new RectTransform[4];
        originalCorners = new Vector3[4][];
        for (int i = 0; i < 4; i++)
        {
            var page = new GameObject("Sheet" + i, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            page.transform.SetParent(root.transform, false); pages[i] = page.GetComponent<RectTransform>();
            pages[i].sizeDelta = new Vector2(640, 800);
            pages[i].pivot = Vector2.one * .5f;
            pages[i].anchoredPosition = new Vector2(i % 2 == 0 ? -320 : 320, 0);
            if (i >= 2)
            {
                pages[i].anchorMin = new Vector2(i % 2 == 0 ? 0 : .5f, 0);
                pages[i].anchorMax = new Vector2(i % 2 == 0 ? .5f : 1, 1);
                pages[i].sizeDelta = pages[i].anchoredPosition = Vector2.zero;
            }
            originalCorners[i] = new Vector3[4]; pages[i].GetWorldCorners(originalCorners[i]);
            page.GetComponent<CanvasGroup>().interactable = i % 2 == 0;
            page.GetComponent<CanvasGroup>().blocksRaycasts = i % 2 == 1;
        }
        stage = swaps = 0; deadline = EditorApplication.timeSinceStartup + 30;
        work.Push(motion.Turn(pages[0], pages[1], pages[2], pages[3], () => ++swaps, true));
    }

    private static void CheckChapterVisibility(GameObject root)
    {
        var owner = root.AddComponent<AlmanacManager>();
        for (int i = 0; i < 2; i++)
        {
            var category = new AlmanacCategory { tabType = i == 0 ? AlmanacTabType.General : AlmanacTabType.Contracts };
            foreach (bool left in new[] { true, false })
            {
                var zone = new GameObject("Chapter" + i + (left ? "Left" : "Right"), typeof(RectTransform), typeof(Image));
                zone.transform.SetParent(root.transform, false);
                var furniture = new GameObject("AuthoredPaperFurniture", typeof(RectTransform));
                furniture.transform.SetParent(zone.transform, false);
                if (left) category.leftPageZone = zone.transform; else category.rightPageZone = zone.transform;
            }
            owner.categories.Add(category);
        }
        motion.Bind(owner);
        for (int selected = 0; selected < 2; selected++)
        {
            owner.OnCategoryChanged?.Invoke(selected);
            for (int i = 0; i < 2; i++)
                foreach (Transform zone in new[] { owner.categories[i].leftPageZone, owner.categories[i].rightPageZone })
                    if (!zone.gameObject.activeSelf || zone.GetComponent<Image>().enabled != (i == selected) ||
                        zone.Find("AuthoredPaperFurniture").gameObject.activeSelf != (i == selected))
                        throw new Exception("Chapter visibility disabled a controller or displayed another chapter's furniture.");
        }
    }

    private static void AssertRestored()
    {
        if (motion.IsTurning) throw new Exception("Turn input lock was not released.");
        for (int i = 0; i < 4; i++)
        {
            CanvasGroup group = pages[i].GetComponent<CanvasGroup>();
            if (pages[i].pivot != Vector2.one * .5f || pages[i].anchoredPosition != (i >= 2 ? Vector2.zero : new Vector2(i % 2 == 0 ? -320 : 320, 0)) ||
                Quaternion.Angle(pages[i].localRotation, Quaternion.identity) > .01f ||
                group.interactable != (i % 2 == 0) || group.blocksRaycasts != (i % 2 == 1) ||
                pages[i].GetComponent<Image>().color != Color.white || pages[i].childCount != 0)
                throw new Exception("Sheet pose/input was not restored: " + i);
            var corners = new Vector3[4]; pages[i].GetWorldCorners(corners);
            for (int corner = 0; corner < 4; corner++)
                if (Vector3.Distance(corners[corner], originalCorners[i][corner]) > .01f)
                    throw new Exception("Sheet corner moved after restoration: " + i);
        }
    }

    private static void AssertBindingStationary()
    {
        for (int i = 0; i < pages.Length; i++)
        {
            RectTransform page = pages[i];
            Vector3 binding = page.TransformPoint(new Vector3(i % 2 == 0 ? page.rect.xMax : page.rect.xMin, 0, 0));
            if (binding.magnitude > .01f) throw new Exception("Binding moved during turn: " + i + " " + binding);
        }
    }

    private static void Cleanup()
    {
        work.Clear();
        if (motion != null) { motion.Cancel(); Object.DestroyImmediate(motion.gameObject); }
        motion = null;
        if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        preview = default;
    }
}
