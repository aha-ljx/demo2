using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Check the actual runtime fallback and rendered glyphs, rather than just slot names.
[InitializeOnLoad]
public static class FactoryFontAudit
{
    const string Pending = "FactoryFontAudit.Pending";
    static double verifyAt;

    static FactoryFontAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            verifyAt = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += VerifyWhenReady;
        };
    }

    [MenuItem("Tools/Factory/Run Chinese Font Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    static void VerifyWhenReady()
    {
        if (EditorApplication.timeSinceStartup < verifyAt) return;
        EditorApplication.update -= VerifyWhenReady;
        SessionState.SetBool(Pending, false);
        var report = new StringBuilder();
        bool passed = false;
        try
        {
            var characters = new SortedSet<char>();
            foreach (string path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
                foreach (char c in File.ReadAllText(path))
                    if (c >= '\u4e00' && c <= '\u9fff') characters.Add(c);
            // Include punctuation from the HUD and the reported missing characters.
            string corpus = new string(characters.ToArray()) + "（） ：，；、。×·→_0123456789FBXRZ";
            var font = FactoryFonts.Ensure(TMP_Settings.defaultFontAsset);
            if (font == null || !font.HasCharacters(corpus, out uint[] missing, true, true))
                throw new Exception("Runtime font chain cannot resolve the project character corpus.");
            report.AppendLine("PASS runtime font chain covers all " + characters.Count + " Chinese source characters and UI punctuation");

            Canvas.ForceUpdateCanvases();
            var labels = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
            foreach (var label in labels) CheckRendered(label, report);

            // Exercise both rendering paths with characters absent from the old subset.
            const string regression = "左键放置 · 输入口 · 成品（0） 已入库：0\n库存已取完 · 圆环 · 队列已满 · 传送带放件停靠";
            foreach (bool worldSpace in new[] { false, true })
            {
                var probe = new GameObject("Chinese font regression probe");
                var label = worldSpace ? (TMP_Text)probe.AddComponent<TextMeshPro>() : probe.AddComponent<TextMeshProUGUI>();
                if (!worldSpace) probe.transform.SetParent(UnityEngine.Object.FindObjectOfType<Canvas>().transform, false);
                label.font = font;
                label.rectTransform.sizeDelta = new Vector2(1600, 300);
                label.text = regression;
                Canvas.ForceUpdateCanvases();
                CheckRendered(label, report);
                if (label.textInfo.characterCount < 50) throw new Exception("Regression probe did not generate its text mesh.");
                UnityEngine.Object.Destroy(probe);
            }
            report.AppendLine("PASS ALL");
            passed = true;
        }
        catch (Exception error) { report.AppendLine("FAIL " + error); }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/factory-font-audit.txt", report.ToString());
        Debug.Log(report.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    static void CheckRendered(TMP_Text label, StringBuilder report)
    {
        label.ForceMeshUpdate(true, true);
        for (int i = 0; i < label.textInfo.characterCount; i++)
        {
            var character = label.textInfo.characterInfo[i];
            if (char.IsWhiteSpace(character.character)) continue;
            if (character.textElement == null || character.textElement.unicode != character.character)
                throw new Exception(label.name + " substituted U+" + ((int)character.character).ToString("X4"));
        }
        report.AppendLine("PASS rendered glyphs: " + label.name + " (" + label.textInfo.characterCount + " characters)");
    }
}
