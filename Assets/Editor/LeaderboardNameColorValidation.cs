using System;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in TMP checks in a temporary preview scene; no networking, saves or authored scene changes.</summary>
public static class LeaderboardNameColorValidation
{
    private const string Request = "Temp/LeaderboardNameColorValidation.request";
    private const string Report = "Temp/LeaderboardNameColorValidation.txt";
    private const string Menu = "Tools/Civil Craft/Validate Leaderboard Name Colors";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Bekind Sans SDF.asset";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Color32 Brown = new Color32(75, 50, 36, 255);
    private static readonly Color32 Purple = new Color32(191, 64, 191, 255);
    private static readonly Color32 Green = new Color32(64, 191, 64, 255);
    private static double nextCheck;

    [InitializeOnLoadMethod]
    private static void Watch()
    {
        EditorApplication.update -= CheckRequest;
        EditorApplication.update += CheckRequest;
    }

    private static bool Busy => EditorApplication.isPlayingOrWillChangePlaymode ||
        EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer;

    private static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request) || Busy) return;
        File.Delete(Request);
        WriteReport();
    }

    [MenuItem(Menu)]
    private static void RunFromMenu() => WriteReport();

    [MenuItem(Menu, true)]
    private static bool CanRunFromMenu() => !Busy;

    private static void WriteReport()
    {
        Directory.CreateDirectory("Temp");
        try
        {
            File.WriteAllText(Report, Run());
            Debug.Log("[Leaderboard name colors] PASS. Report: " + Report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Report, "FAIL: " + error);
            Debug.LogException(error);
        }
    }

    /// <summary>Runs actual production row binding and TMP parsing; all fixture objects are removed before returning.</summary>
    public static string Run()
    {
        Check(!Busy, "Wait until Play Mode, compilation, imports and player builds have stopped.");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath) ?? TMP_Settings.defaultFontAsset;
        if (font == null)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null) break;
            }
        }
        Check(font != null, "No configured TMP font is available for glyph validation.");

        var report = new StringBuilder();
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject fixture = null;
        try
        {
            fixture = new GameObject("Leaderboard Name Validation (Temporary)", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(fixture, scene);
            fixture.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rowObject = new GameObject("Row", typeof(RectTransform));
            rowObject.transform.SetParent(fixture.transform, false);
            var row = rowObject.AddComponent<MultiplayerLeaderboardRowUI>();
            TMP_Text builder = Label("Builder", rowObject.transform, font);
            Set(row, "builderText", builder);
            Set(row, "rankText", Label("Rank", rowObject.transform, font));
            Set(row, "winRateText", Label("Win Rate", rowObject.transform, font));
            Set(row, "winsText", Label("Wins", rowObject.transform, font));
            Set(row, "lossesText", Label("Losses", rowObject.transform, font));

            const string displayName = "<#BF40BF>.dev_hyakkimaru";
            Bind(row, builder, displayName, false, ".dev_hyakkimaru");
            Colors(builder, 0, builder.textInfo.characterCount, Purple);
            Check((builder.textInfo.characterInfo[0].style & FontStyles.Bold) != 0, "The name lost the row's bold style.");
            Bind(row, builder, displayName, true, ".dev_hyakkimaru (YOU)");
            Colors(builder, 0, ".dev_hyakkimaru".Length, Purple);
            Colors(builder, ".dev_hyakkimaru".Length, " (YOU)".Length, Brown);
            report.AppendLine("PASS: <#BF40BF>.dev_hyakkimaru renders as .dev_hyakkimaru in opaque BF40BF; the trusted (YOU) suffix retains the default brown.");

            Bind(row, builder, "Engineer", false, "Engineer");
            Colors(builder, 0, builder.textInfo.characterCount, Brown);
            foreach (string empty in new[] { null, "", " \t\r\n", "<#BF40BF>", "<color=#BF40BF></color>" })
            {
                Bind(row, builder, empty, false, "Engineer");
                Colors(builder, 0, builder.textInfo.characterCount, Brown);
                Bind(row, builder, empty, true, "Engineer (YOU)");
                Colors(builder, 0, builder.textInfo.characterCount, Brown);
            }
            report.AppendLine("PASS: Null, blank and color-only names use Engineer; rebinding a colored row to a plain or fallback name clears its color.");

            Bind(row, builder, "<#BF40BF>A<color=#40BF40>B</color>C", true, "ABC (YOU)");
            Colors(builder, 0, 1, Purple);
            Colors(builder, 1, 1, Green);
            Colors(builder, 2, 1, Purple);
            Colors(builder, 3, " (YOU)".Length, Brown);
            Bind(row, builder, "<COLOR=#bf40bf>A</COLOR>B", false, "AB");
            Colors(builder, 0, 1, Purple);
            Colors(builder, 1, 1, Brown);
            report.AppendLine("PASS: Nested, explicitly closed and case-insensitive six-digit RGB colors parse correctly; unclosed colors are closed before the suffix.");

            foreach (string literal in new[] {
                "<size=999>Huge</size>",
                "<sprite=0>Sprite</sprite>",
                "</noparse><size=999>Huge</size><noparse>Name",
                "</color>Name",
                "<color=red>Name</color>",
                "<#BF40BF00>Transparent",
                "<#B4B>Short",
                "<#BF40BG>Invalid",
                "<link=external>Name</link>"
            })
            {
                Bind(row, builder, literal, true, literal + " (YOU)");
                Colors(builder, 0, builder.textInfo.characterCount, Brown);
                SafeGlyphs(builder);
            }
            const string hostile = "</noparse><size=999>X</size><sprite=0></color></color>";
            const string hostileVisible = "</noparse><size=999>X</size><sprite=0></color>";
            Bind(row, builder, "<#BF40BF>" + hostile, true, hostileVisible + " (YOU)");
            Colors(builder, 0, hostileVisible.Length - "</color>".Length, Purple);
            Colors(builder, hostileVisible.Length - "</color>".Length, "</color> (YOU)".Length, Brown);
            SafeGlyphs(builder);
            Bind(row, builder, "Alice\nBob\tTest\0", true, "Alice Bob Test  (YOU)");
            Colors(builder, 0, builder.textInfo.characterCount, Brown);
            report.AppendLine("PASS: Size, sprite, noparse, link, invalid/short/alpha colors and unmatched closing-color tags remain literal; control characters become spaces, glyph size stays fixed and no sprites are created.");
            report.AppendLine("Completed UTC: " + DateTime.UtcNow.ToString("O"));
            return report.ToString();
        }
        finally
        {
            if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = 28;
        text.enableAutoSizing = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.color = Brown;
        text.rectTransform.sizeDelta = new Vector2(12000, 120);
        return text;
    }

    private static void Set(MultiplayerLeaderboardRowUI row, string field, TMP_Text value)
    {
        FieldInfo reference = typeof(MultiplayerLeaderboardRowUI).GetField(field, PrivateInstance);
        Check(reference != null, "The production row field is missing: " + field);
        reference.SetValue(row, value);
    }

    private static void Bind(MultiplayerLeaderboardRowUI row, TMP_Text builder, string name, bool isYou, string expected)
    {
        row.Bind(1, name, 3, 1, isYou);
        builder.ForceMeshUpdate(true, true);
        Check(builder.richText, "Production row binding leaves TMP name colors disabled.");
        Check(builder.GetParsedText() == expected,
            "Parsed name mismatch. Expected: " + expected + "; actual: " + builder.GetParsedText());
        Check(builder.textInfo.characterCount == expected.Length, "Markup introduced or removed unexpected glyphs.");
        SafeGlyphs(builder);
    }

    private static void Colors(TMP_Text text, int start, int count, Color32 color)
    {
        for (int index = start; index < start + count; index++)
            Check(text.textInfo.characterInfo[index].color.Equals(color),
                "Unexpected name color at glyph " + index + ". Expected " + color + ", actual " + text.textInfo.characterInfo[index].color);
    }

    private static void SafeGlyphs(TMP_Text text)
    {
        Check(text.textInfo.spriteCount == 0, "A typed sprite tag created a sprite.");
        for (int index = 0; index < text.textInfo.characterCount; index++)
            Check(Math.Abs(text.textInfo.characterInfo[index].pointSize - text.fontSize) < .01f,
                "Typed markup changed glyph size at index " + index);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
