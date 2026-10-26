#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class FactoryLocalAssistantAuditRunner : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly StringBuilder report = new StringBuilder();
    bool done, failed;
    float began;
    void OnEnable() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(message + "\n" + stack); } }
    void Update() { if (!done && (failed || Time.realtimeSinceStartup - began > 600)) Finish(false); }
    void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message); report.AppendLine("PASS " + message);
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-local-assistant-progress.txt", report.ToString());
    }
    IEnumerator Start()
    {
        yield return null; yield return null;
        var menu = FindObjectOfType<MainMenuController>(); menu.StartGame(); menu.FirstLevelButton.onClick.Invoke();
        yield return null; yield return null; yield return null;
        var guide = FindObjectOfType<FactoryTutorial>(); guide.Close(); yield return null; yield return null; yield return null;
        var builder = FindObjectOfType<PlacementSystem>(); builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        var level = FactoryLevelSession.Active; var assistant = builder.Assistant;
        var data = Resources.Load<TextAsset>("FactoryAssistant/VerifiedPlans");
        Check(data != null, "verified candidate catalog ships as a local game resource without network or API credentials");
        var catalog = JsonUtility.FromJson<FactoryAssistantCatalog>(data.text);
        Check(FactoryAssistantSolver.Parse("十分钟内得到十件产物", out int n, out float t, out _) && n == 10 && t == 600 &&
            FactoryAssistantSolver.Parse("1分30秒得到2件", out n, out t, out _) && n == 2 && t == 90 &&
            !FactoryAssistantSolver.Parse("-1件，600秒", out n, out t, out _) &&
            !FactoryAssistantSolver.Parse("1.5件，600秒", out n, out t, out _) &&
            !FactoryAssistantSolver.Parse("601秒10件", out n, out t, out _), "quantity/time parser supports Chinese and combined units and rejects invalid objectives");
        typeof(FactoryLocalAssistant).GetField("useDeepSeek", Private).SetValue(assistant, false);
        assistant.AssistantButton.onClick.Invoke();
        Check(assistant.IsOpen && FactoryLocalAssistant.BlocksGameplay && Cursor.visible, "planning button opens an isolated local-assistant interface");
        level.BeginProduction(); Check(!level.IsRunning && level.RemainingSeconds == 600, "assistant blocks starting production through its modal interface");
        guide.Open(); Check(!guide.IsOpen, "tutorial cannot cover an active assistant query");
        assistant.QuestionInput.text = "600秒11件"; assistant.CalculateButton.onClick.Invoke();
        Check(assistant.Answer.provenImpossible && assistant.Answer.best == null && !assistant.ApplyButton.interactable,
            "resource limit produces a real impossibility answer without an invented plan");
        assistant.QuestionInput.text = "20秒1件"; assistant.CalculateButton.onClick.Invoke();
        Check(assistant.Answer.provenImpossible, "mandatory punch dwell establishes the short-deadline impossibility bound");
        assistant.QuestionInput.text = "30秒10件"; assistant.CalculateButton.onClick.Invoke();
        Check(!assistant.Answer.provenImpossible && assistant.Answer.best == null, "an unsuccessful candidate search is not misrepresented as a global impossibility proof");
        Capture(assistant, "no-candidate", 1280, 720);
        assistant.QuestionInput.text = "能否在600秒内得到10件产物，并且成本最低？"; assistant.CalculateButton.onClick.Invoke();
        var answer = assistant.Answer;
        Check(answer.best != null && answer.completion <= 600 && assistant.ApplyButton.interactable,
            "ten-product query returns a real verified feasible candidate and enables placement");
        long minimum = catalog.profiles.Where(p => p.receivedAt.Length >= 10 && p.receivedAt[9] + catalog.margin <= 600)
            .Min(p => p.plan.devices.Sum(d => (long)PlacementSystem.EquipmentPrice(d.kind)));
        Check(answer.cost == minimum && answer.compared == catalog.profiles.Length,
            "selected candidate is the minimum current equipment cost among all verified layouts meeting the deadline");
        Check(FactoryAssistantSolver.Solve(10, 600, catalog, builder.AssistantScale, 4, 3, 2).best == null,
            "changed simulation parameters invalidate incompatible timing profiles");
        Capture(assistant, "feasible", 1920, 1080);
        Capture(assistant, "feasible-small", 1280, 720);
        Capture(assistant, "feasible-4x3", 1024, 768);
        var invalidFeed = JsonUtility.FromJson<FactoryAssistantProfile>(JsonUtility.ToJson(answer.best));
        foreach (var device in invalidFeed.plan.devices) device.offset += Vector3.right * 300;
        Check(!builder.ApplyAssistantPlan(invalidFeed, out _) && builder.PlacedCount == 0 && builder.CurrentCost == 0 &&
            FindObjectOfType<RawMaterialSupply>().Remaining == 10 && FactoryProduction.Instance.Statistics.Modules.Count == 0,
            "input-boundary failure rolls back the new assistant layout without consuming stock or leaving partial devices");
        yield return null; yield return null;
        assistant.ApplyButton.onClick.Invoke();
        var factory = FactoryProduction.Instance; var supply = FindObjectOfType<RawMaterialSupply>();
        Check(builder.PlacedCount == answer.best.plan.devices.Length && builder.CurrentCost == answer.cost && supply.Remaining == 0 &&
            factory.Stations.Where(s => s.Kind == 0).Sum(s => s.Input.Count) == 10 && !level.IsRunning && level.RemainingSeconds == 600,
            "placement restores the verified layout and assigns exactly ten real stock plates without starting the clock");
        int placed = builder.PlacedCount; assistant.ApplyButton.onClick.Invoke();
        Check(builder.PlacedCount == placed && supply.Remaining == 0, "repeated placement cannot duplicate devices or raw materials");
        yield return null; yield return null;
        Capture(assistant, "placed", 1280, 720);
        assistant.Close(); yield return null; yield return null; yield return null;
        level.BeginProduction(); Time.captureDeltaTime = 1f / 240; Time.timeScale = 4;
        int previous = 0;
        while (level.IsRunning && factory.Stored < 10)
        {
            if (factory.Stored > previous)
            { previous = factory.Stored; Check(true, "actual 60Hz production receipt " + previous + " at " + (600 - level.RemainingSeconds).ToString("F2") + " simulation seconds"); }
            yield return null;
        }
        Check(factory.Stored == 10 && 600 - level.RemainingSeconds <= answer.deadline,
            "the player-facing auto-placed plan really delivers ten products at " + (600 - level.RemainingSeconds).ToString("F2") + " simulation seconds by the requested deadline at 60Hz");
        if (!level.IsEnded) level.Advance(level.RemainingSeconds);
        Check(level.ResultCount == 10 && level.ResultCost == answer.cost && level.ResultRevenue == 20000,
            "assistant never fabricates rewards: normal settlement uses the ten physical received products and actual layout cost");
        Finish(true);
    }
    void Capture(FactoryLocalAssistant assistant, string name, int width, int height)
    {
        var level = FactoryLevelSession.Active;
        if (level != null) typeof(FactoryLevelSession).GetMethod("Refresh", Private).Invoke(level, null);
        var overlay = (GameObject)typeof(FactoryLocalAssistant).GetField("overlay", Private).GetValue(assistant);
        var card = (RectTransform)typeof(FactoryLocalAssistant).GetField("card", Private).GetValue(assistant);
        var canvas = overlay.GetComponentInParent<Canvas>(); var mode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        var root = new GameObject("Assistant capture camera"); var camera = root.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = FactoryGuideUI.Background; camera.cullingMask = 1 << 5;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        Canvas.ForceUpdateCanvases(); FactoryGuideUI.Fit(card, canvas, new Vector2(1080, 800), 32); Canvas.ForceUpdateCanvases();
        foreach (var label in overlay.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            Check(!label.isTextTruncated && !label.isTextOverflowing, "assistant text fits: " + label.name + " at " + width + "x" + height);
            foreach (var glyph in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
                if (!char.IsWhiteSpace(glyph.character) && (glyph.textElement == null || glyph.textElement.unicode != glyph.character))
                    throw new Exception("Missing assistant glyph: " + glyph.character);
        }
        var corners = new Vector3[4]; card.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            var point = camera.WorldToViewportPoint(corner);
            Check(point.x >= -.001f && point.x <= 1.001f && point.y >= -.001f && point.y <= 1.001f, "assistant panel stays in viewport at " + width + "x" + height);
        }
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs/assistant"); File.WriteAllBytes("Logs/assistant/" + name + ".png", picture.EncodeToPNG());
        RenderTexture.active = previous; canvas.renderMode = mode; canvas.worldCamera = oldCamera;
        camera.targetTexture = null; DestroyImmediate(picture); DestroyImmediate(target); DestroyImmediate(root); Canvas.ForceUpdateCanvases();
    }
    void Finish(bool ok)
    {
        if (done) return; done = true; Time.captureDeltaTime = 0;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-local-assistant-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
#endif
