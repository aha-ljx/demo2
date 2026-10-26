using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class FactoryUIStyleAudit
{
    static FactoryUIStyleAudit()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("FactoryUIStyleAudit.Pending", false)) return;
            SessionState.SetBool("FactoryUIStyleAudit.Pending", false);
            var root = new GameObject("UI style audit"); UnityEngine.Object.DontDestroyOnLoad(root); root.AddComponent<FactoryUIStyleAuditRunner>();
        };
    }
    [MenuItem("Tools/Factory/Run UI Style Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity"); SessionState.SetBool("FactoryUIStyleAudit.Pending", true); EditorApplication.isPlaying = true;
    }
}
public sealed class FactoryUIStyleAuditRunner : MonoBehaviour
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly StringBuilder report = new StringBuilder(); bool done; float began;
    void OnEnable() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Update() { if (!done && Time.realtimeSinceStartup - began > 180) { report.AppendLine("FAIL timeout"); Finish(false); } }
    void Log(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) { report.AppendLine(message + stack); Finish(false); } }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
    T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Hidden).GetValue(o);
    IEnumerator Start()
    {
        yield return null; yield return null;
        var menu = FindObjectOfType<MainMenuController>();
        Capture("menu", 1920, 1080); Capture("menu-small", 1280, 720);
        menu.StartGame(); yield return null; Capture("levels", 1280, 720);
        menu.FirstLevelButton.onClick.Invoke(); yield return null; yield return null; yield return null;
        var guide = FindObjectOfType<FactoryTutorial>(); Capture("tutorial", 1280, 720);
        guide.NextButton.onClick.Invoke(); Capture("tutorial-placement", 1024, 768); guide.Close(); yield return null; yield return null;
        var builder = FindObjectOfType<PlacementSystem>(); builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        var level = FactoryLevelSession.Active; var stats = FactoryProduction.Instance.Statistics;
        Capture("planning", 1280, 720); Capture("planning-4x3", 1024, 768);
        builder.Plans.OpenList(); Capture("plans", 1280, 720); builder.Plans.Close(); yield return null; yield return null;
        var assistant = builder.Assistant; assistant.Open(); Capture("assistant", 1280, 720);
        assistant.GetType().GetMethod("OpenSettings", Hidden).Invoke(assistant, null); Capture("connection", 1280, 720); assistant.Close(); yield return null; yield return null;
        var data = JsonUtility.FromJson<FactoryAssistantCatalog>(Resources.Load<TextAsset>("FactoryAssistant/VerifiedPlans").text);
        var best = FactoryAssistantSolver.Solve(10, 600, data, builder.AssistantScale, 3, 3, 2);
        var empty = JsonUtility.FromJson<FactoryAssistantProfile>(JsonUtility.ToJson(best.best)); Array.Clear(empty.feed, 0, empty.feed.Length);
        Check(builder.ApplyAssistantPlan(empty, out string error), "layout loads for UI interaction checks: " + error);
        var punch = FactoryProduction.Instance.Stations.Find(x => x.Kind == 0);
        Check(stats.OpenDetails(punch.gameObject), "equipment details remain accessible"); Capture("equipment", 1280, 720);
        Field<Button>(stats, "feedAction").onClick.Invoke(); yield return null;
        var supply = FindObjectOfType<RawMaterialSupply>(); Check(supply.IsFeedDialogOpen && supply.FeedSlider.maxValue == 10, "feeding slider preserves stock and capacity bounds");
        supply.FeedSlider.value = 5; Capture("feeding", 1280, 720); supply.ConfirmFeedButton.onClick.Invoke();
        Check(supply.Remaining == 5 && punch.Input.Count == 5, "the restyled feed action transfers the selected real raw materials");
        yield return null; yield return null;
        var arm = FactoryProduction.Instance.Arms[0]; stats.OpenDetails(arm.gameObject); Capture("arm", 1024, 768); stats.CloseStatistics(); yield return null; yield return null;
        stats.OpenCurves(); Capture("statistics", 1280, 720); Capture("statistics-4x3", 1024, 768); stats.CloseStatistics(); yield return null; yield return null;
        level.BeginProduction(); Check(level.IsRunning, "start production button behavior is preserved"); yield return null;
        level.GetType().GetMethod("Advance", Hidden).Invoke(level, new object[] { 600f }); yield return null;
        Capture("results", 1280, 720); Check(level.ResultCost == best.cost && level.ResultNet == -best.cost, "settlement retains actual costs and net result after removing duplicate metrics");
        builder.Plans.OpenSave(); Capture("save-plan", 1024, 768); builder.Plans.Close();
        Finish(true);
    }
    void Capture(string name, int width, int height)
    {
        var canvas = FindObjectOfType<Canvas>().rootCanvas;
        var root = new GameObject("UI capture"); var camera = root.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.7f, .71f, .69f); camera.cullingMask = 1 << 5;
        var texture = new RenderTexture(width, height, 24); camera.targetTexture = texture;
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; float oldPlane = canvas.planeDistance;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10; Canvas.ForceUpdateCanvases();
        foreach (var rect in canvas.GetComponentsInChildren<RectTransform>())
            if (rect.name.EndsWith("Card") || rect.name == "HomeContent" || rect.name == "LevelOneHUD") FactoryGuideUI.Fit(rect, canvas, rect.sizeDelta, 32);
        Canvas.ForceUpdateCanvases();
        foreach (var label in canvas.GetComponentsInChildren<TMP_Text>())
        {
            if (label.name == "Transcript") continue;
            label.ForceMeshUpdate();
            Check(!label.isTextTruncated && !label.isTextOverflowing, name + ": text fits " + label.name);
            Check(label.color != FactoryGuideUI.Mint, name + ": no yellow body text on light surfaces");
            if (label.name != "Wordmark" && !string.IsNullOrWhiteSpace(label.text)) Check(label.color.grayscale < .6f, name + ": readable dark text " + label.name);
        }
        camera.Render(); var oldTarget = RenderTexture.active; RenderTexture.active = texture;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs/ui-style"); File.WriteAllBytes("Logs/ui-style/" + name + ".png", picture.EncodeToPNG());
        RenderTexture.active = oldTarget; camera.targetTexture = null; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
        Destroy(picture); Destroy(texture); Destroy(root); Canvas.ForceUpdateCanvases();
    }
    void Finish(bool ok)
    {
        if (done) return; done = true; Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-ui-style-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
