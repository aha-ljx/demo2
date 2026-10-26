#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class FactoryOnboardingAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    bool done, failed;
    float started;
    void OnEnable() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(message + "\n" + stack); } }
    void Update() { if (!done && (failed || Time.realtimeSinceStartup - started > 240)) Finish(false); }
    void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); Debug.Log("ONBOARDING " + message); }
    IEnumerator Start()
    {
        yield return null; yield return null;
        var menu = FindObjectOfType<MainMenuController>();
        Check(menu != null && SceneManager.GetActiveScene().name == "MainMenu", "audit begins at the real main menu");
        GameObject.Find("StartButton").GetComponent<Button>().onClick.Invoke();
        yield return null;
        Check(SceneManager.GetActiveScene().name == "MainMenu" && menu.LevelSelection.activeSelf,
            "enter game opens level selection before loading gameplay");
        Check(menu.FirstLevelButton.interactable && !menu.SecondLevelButton.interactable,
            "level one is available and level two is visibly unavailable");
        Inspect(menu.LevelSelection);
        Capture(menu.LevelSelection.GetComponentInParent<Canvas>(), menu.LevelSelection, "level-select", 1920, 1080);
        Capture(menu.LevelSelection.GetComponentInParent<Canvas>(), menu.LevelSelection, "level-select-small", 1280, 720);
        menu.LevelSelection.transform.Find("SelectionCard/BackToMainMenu").GetComponent<Button>().onClick.Invoke();
        Check(!menu.LevelSelection.activeSelf, "back button returns to the original main menu");
        GameObject.Find("StartButton").GetComponent<Button>().onClick.Invoke();
        menu.FirstLevelButton.onClick.Invoke();
        for (int i = 0; i < 10 && FindObjectOfType<FactoryTutorial>() == null; i++) yield return null;
        yield return null;
        var guide = FindObjectOfType<FactoryTutorial>();
        Check(SceneManager.GetActiveScene().name == "GameScene" && guide != null && guide.IsOpen && guide.PageIndex == 0,
            "selecting level one loads the factory with tutorial page one centered");
        Check(!MainMenuController.TutorialRequested, "scene entry consumes the tutorial request exactly once");
        Check(Time.timeScale == 0 && FactoryTutorial.BlocksGameplay && Cursor.visible,
            "tutorial pauses the simulation and makes the cursor available");
        var placement = FindObjectOfType<PlacementSystem>();
        Vector3 playerPosition = placement.player.position;
        for (int i = 0; i < 5; i++) yield return null;
        Check(Vector3.Distance(playerPosition, placement.player.position) < .001f && placement.CurrentCost == 0,
            "player and construction remain unchanged while the tutorial is open");
        var playback = FindObjectOfType<FactoryPlaybackControl>();
        playback.CycleSpeed();
        Check(Time.timeScale == 0, "fast-forward cannot override the tutorial pause");
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(4, 4) }, hits);
        Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(guide.Overlay.transform),
            "the full-screen scrim catches clicks outside the tutorial card");
        string[][] tokens = { new[] { "W", "A", "S", "D", "Shift", "CapsLk", "冲刺" },
            new[] { "1 - 5", "R", "鼠标左键", "鼠标右键" }, new[] { "CapsLk", "WASD", "视角", "数字键" },
            new[] { "冲床", "折弯", "焊接", "最终区域", "滑槽", "拐弯", "接触", "指定", "每条独立滑槽只能放置一个机械臂" },
            new[] { "成本", "时间", "产量优先", "净收益", "2000", "滑块" } };
        Check(FactoryTutorial.PageCount == tokens.Length, "all five tutorial topics have a matching page");
        for (int page = 0; page < FactoryTutorial.PageCount; page++)
        {
            Check(guide.PageIndex == page, "page index advances in order: " + page);
            Check(guide.Card.Find("PageCounter").GetComponent<TMP_Text>().text == (page + 1).ToString("00") + " / 05",
                "page counter shows the five-page total");
            string corpus = string.Join("\n", guide.Overlay.GetComponentsInChildren<TMP_Text>().Select(t => t.text));
            Check(tokens[page].All(corpus.Contains), "page contains all requested controls and instructions: " + page);
            Check(guide.PreviousButton.interactable == (page > 0), "previous-page availability matches the current page");
            Inspect(guide.Overlay);
            Capture(guide.Overlay.GetComponentInParent<Canvas>(), guide.Overlay, "tutorial-" + (page + 1), 1920, 1080);
            Capture(guide.Overlay.GetComponentInParent<Canvas>(), guide.Overlay, "tutorial-" + (page + 1) + "-small", 1280, 720);
            Capture(guide.Overlay.GetComponentInParent<Canvas>(), guide.Overlay, "tutorial-" + (page + 1) + "-4x3", 1024, 768);
            if (page == 1)
            {
                guide.PreviousButton.onClick.Invoke(); Check(guide.PageIndex == 0, "previous button revisits the prior page");
                guide.NextButton.onClick.Invoke();
            }
            guide.NextButton.onClick.Invoke();
        }
        Check(!guide.IsOpen && !guide.Overlay.activeSelf && Mathf.Approximately(Time.timeScale, 1),
            "start planning on the final page closes the tutorial and resumes gameplay");
        Check(FactoryTutorial.BlocksGameplay, "closing click is quarantined from placement input");
        yield return null; yield return null; yield return null;
        Check(!FactoryTutorial.BlocksGameplay, "normal gameplay input resumes after the closing frame");
        playback.CycleSpeed(); Check(Mathf.Approximately(Time.timeScale, 2), "existing fast-forward still works after completing the guide");
        guide.HelpButton.onClick.Invoke();
        Check(guide.IsOpen && guide.PageIndex == 0 && Time.timeScale == 0, "help button reopens the guide from page one");
        guide.Card.Find("SkipTutorial").GetComponent<Button>().onClick.Invoke();
        Check(!guide.IsOpen && Mathf.Approximately(Time.timeScale, 2), "skip restores the exact previous playback speed");
        SceneManager.LoadScene("MainMenu"); yield return null; yield return null;
        Check(!FactoryTutorial.BlocksGameplay && Mathf.Approximately(Time.timeScale, 1), "scene exit leaves no stale pause or input lock");
        Finish(true);
    }
    void Inspect(GameObject root)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var label in root.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            if (label.isTextTruncated || label.isTextOverflowing) throw new Exception("Guide text does not fit: " + label.name + " / " + label.text);
            foreach (var glyph in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
                if (!char.IsWhiteSpace(glyph.character) && (glyph.textElement == null || glyph.textElement.unicode != glyph.character))
                    throw new Exception("Missing guide glyph: " + glyph.character);
        }
        report.AppendLine("PASS visible guide text fits and renders its actual Chinese glyphs");
    }
    void Capture(Canvas canvas, GameObject root, string name, int width, int height)
    {
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        var cameraObject = new GameObject("Guide capture camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .04f, .055f);
        camera.cullingMask = 1 << 5;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        Canvas.ForceUpdateCanvases();
        var card = root.transform.Find(root.name == "LevelSelection" ? "SelectionCard" : "TutorialCard").GetComponent<RectTransform>();
        FactoryGuideUI.Fit(card, canvas, root.name == "LevelSelection" ? new Vector2(1160, 740) : new Vector2(1180, 760));
        Canvas.ForceUpdateCanvases(); Inspect(root);
        var corners = new Vector3[4]; card.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
        {
            Vector3 point = camera.WorldToViewportPoint(corner);
            if (point.x < -.001f || point.x > 1.001f || point.y < -.001f || point.y > 1.001f)
                throw new Exception("Guide panel extends outside viewport: " + name + " / " + point);
        }
        camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs/onboarding"); File.WriteAllBytes("Logs/onboarding/" + name + ".png", picture.EncodeToPNG());
        RenderTexture.active = previous;
        canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
        camera.targetTexture = null; DestroyImmediate(picture); DestroyImmediate(target); DestroyImmediate(cameraObject);
        Canvas.ForceUpdateCanvases();
    }
    void Finish(bool ok)
    {
        if (done) return; done = true;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-onboarding-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
#endif
