using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Detached tape checks and real book previews; never saves scenes, prefabs or font assets.</summary>
public static class AlmanacPhotoMountValidation
{
    private const string Request = "Temp/almanac-photo-mount-v1.request";
    private const string Report = "Temp/AlmanacPhotoMountValidation.txt";
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch() { EditorApplication.update -= Check; EditorApplication.update += Check; }

    private static void Check()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer ||
            PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/Civil Craft/Almanac/Check Taped Pictures (No Scene Save)")]
    public static void Run()
    {
        var report = new StringBuilder();
        GameObject prefab = null;
        try
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Leave Play Mode before this check.");
            CheckDecoration(report);
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/Resources/Fonts/Internet Friends SDF.asset");
            Require(font != null, "Existing Almanac font has not imported.");
            AlmanacManager source = Resources.FindObjectsOfTypeAll<AlmanacManager>().FirstOrDefault(book =>
                book != null && book.gameObject.scene.IsValid() && book.gameObject.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(book.gameObject.scene) && book.Panel != null &&
                book.categories.Count > 0);
            if (source == null)
            {
                prefab = PrefabUtility.LoadPrefabContents("Assets/Prefabs/BuildingMode/MANAGERS AND CANVASES.prefab");
                source = prefab.GetComponentInChildren<AlmanacManager>(true);
            }
            Require(source != null, "No authored book is available for a detached preview.");
            MethodInfo capture = typeof(AlmanacTypographyAuthoring).GetMethod("CaptureBook",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(capture != null, "Book preview entry point is missing.");
            capture.Invoke(null, new object[] { source, font, report });
            File.WriteAllText(Report, "PASS: taped-picture checks and phone/tablet book previews.\n" + report);
            Debug.Log("[Almanac pictures] " + report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error + "\n" + report);
            Debug.LogException(error);
        }
        finally { if (prefab != null) PrefabUtility.UnloadPrefabContents(prefab); }
    }

    private static void CheckDecoration(StringBuilder report)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Detached Photo Fixture", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, preview);
            RectTransform frame = root.GetComponent<RectTransform>();
            frame.sizeDelta = new Vector2(410f, 190f);
            frame.anchoredPosition = new Vector2(37f, -82f);
            Image paper = root.AddComponent<Image>();
            var picture = new GameObject("Photo", typeof(RectTransform), typeof(RawImage));
            picture.transform.SetParent(frame, false);
            RawImage image = picture.GetComponent<RawImage>();
            image.uvRect = new Rect(.12f, .07f, .76f, .86f);
            string pictureBefore = EditorJsonUtility.ToJson(image);
            Vector2 anchorMin = frame.anchorMin, anchorMax = frame.anchorMax, pivot = frame.pivot;
            Vector2 size = frame.sizeDelta, position = frame.anchoredPosition;
            Vector3 scale = frame.localScale;
            Quaternion rotation = frame.localRotation;
            AlmanacPhotoMount first = AlmanacPhotoMount.Ensure(frame);
            AlmanacPhotoMount second = AlmanacPhotoMount.Ensure(frame);
            Require(first == second && frame.GetComponentsInChildren<AlmanacPhotoMount>(true).Length == 1,
                "Repeated setup duplicated tape.");
            Require(!first.raycastTarget && first.transform.GetSiblingIndex() == frame.childCount - 1,
                "Tape blocks input or renders behind the picture.");
            Require(pictureBefore == EditorJsonUtility.ToJson(image) && frame.anchorMin == anchorMin &&
                frame.anchorMax == anchorMax && frame.pivot == pivot && frame.sizeDelta == size &&
                frame.anchoredPosition == position && frame.localScale == scale && frame.localRotation == rotation,
                "Tape changed photo texture/crop or existing frame geometry.");
            MethodInfo populate = typeof(AlmanacPhotoMount).GetMethod("OnPopulateMesh",
                BindingFlags.Instance | BindingFlags.NonPublic);
            using (var vertices = new VertexHelper())
            {
                populate.Invoke(first, new object[] { vertices });
                Require(vertices.currentVertCount > 0 && vertices.currentVertCount < 100,
                    "Tape mesh is empty or unnecessarily heavy.");
                var vertex = new UIVertex();
                for (int i = 0; i < vertices.currentVertCount; i++)
                {
                    vertices.PopulateUIVertex(ref vertex, i);
                    Require(IsFinite(vertex.position.x) && IsFinite(vertex.position.y) && IsFinite(vertex.position.z),
                        "Invalid tape vertex.");
                    Require(vertex.position.y <= first.rectTransform.rect.yMax + 8.1f &&
                        vertex.position.y >= first.rectTransform.rect.yMin - 10.1f,
                        "Tape overlaps the reserved title/caption space.");
                }
                report.AppendLine("TAPE: two torn-end translucent strips, " + vertices.currentVertCount +
                    " vertices, default UI material; no texture allocation, no per-frame updater.");
            }
            frame.sizeDelta = new Vector2(285f, 390f);
            Require(first.rectTransform.rect.size == frame.rect.size, "Tape does not follow resized frames.");
            // Wardrobe moves the shared RawImage, not its stable book card.
            var wardrobe = new GameObject("Wardrobe", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(wardrobe, preview);
            picture.transform.SetParent(wardrobe.transform, false);
            Require(first.transform.parent == frame && picture.GetComponentInChildren<AlmanacPhotoMount>() == null,
                "Book tape followed the portrait into the wardrobe.");
            frame.gameObject.SetActive(false);
            Require(!first.gameObject.activeInHierarchy, "Tape stayed visible after photo frame was hidden.");
            report.AppendLine("SAFETY: idempotent setup, frontmost/non-raycasting, resize-safe; photo and frame layout untouched; tape stays in book during wardrobe reparenting and hides with missing pictures.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
