#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class FactoryPlaybackAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    bool failed, finished;
    float started, dwellStart, dwellEnd, realStart, realEnd;
    FactoryPlaybackControl control;
    public bool SnapshotOnly;
    void OnEnable() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string text, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(text + "\n" + stack); }
        if (text.StartsWith("上料状态: Punching")) { dwellStart = Time.time; realStart = Time.realtimeSinceStartup; }
        if (text.StartsWith("上料状态: ReplaceWithFinishedPart")) { dwellEnd = Time.time; realEnd = Time.realtimeSinceStartup; }
    }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); Debug.Log("PLAYBACK AUDIT " + message); }
    void Update() { if (!finished && (failed || Time.realtimeSinceStartup - started > 300)) Finish(false); }
    void Click()
    {
        ExecuteEvents.Execute(control.FastForwardButton.gameObject,
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    void CaptureUI()
    {
        var camera = FindObjectOfType<PlacementSystem>().worldCamera;
        var canvas = control.FastForwardButton.GetComponentInParent<Canvas>();
        var mode = canvas.renderMode; var canvasCamera = canvas.worldCamera;
        float planeDistance = canvas.planeDistance; var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var render = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = render; canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = Mathf.Max(camera.nearClipPlane + .2f, 1f);
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            camera.Render(); RenderTexture.active = render;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/factory-playback-ui.png", texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            canvas.renderMode = mode; canvas.worldCamera = canvasCamera; canvas.planeDistance = planeDistance;
            Destroy(render); Destroy(texture);
        }
    }
    IEnumerator Start()
    {
        yield return null; yield return null;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
        var placement = FindObjectOfType<PlacementSystem>(); placement.enabled = false;
        placement.worldCamera.GetComponent<GameController>().enabled = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        var system = FactoryProduction.Instance; system.enabled = false;
        control = FindObjectOfType<FactoryPlaybackControl>();
        if (SnapshotOnly)
        {
            Click(); Click(); CaptureUI(); Check(File.Exists("Logs/factory-playback-ui.png"), "rendered playback UI snapshot"); Finish(true); yield break;
        }
        Check(control != null && control.FastForwardButton != null && control.Speed == 1 && Time.timeScale == 1, "scene creates a normal-speed playback button");
        Canvas.ForceUpdateCanvases();
        var rect = control.FastForwardButton.GetComponent<RectTransform>();
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        Check(hits.Count > 0 && hits[0].gameObject == control.FastForwardButton.gameObject, "button is the topmost clickable UI target");
        Check(FactoryPlaybackControl.PointerOverButton(point) && !FactoryPlaybackControl.PointerOverButton(Vector2.zero), "button pointer guard protects third-person cursor capture");
        foreach (int speed in new[] { 2, 4, 1 })
        {
            Click(); Check(control.Speed == speed && Time.timeScale == speed &&
                control.FastForwardButton.GetComponentInChildren<TMP_Text>().text.Contains("×" + speed), "UI pointer click switches to " + speed + "x and updates its label");
        }
        var machine = FactoryProductionAuditRunner.Model(0, new Vector3(100, 0, 100), 0);
        system.Register(machine, 0); var punch = machine.GetComponent<FactoryStation>();
        for (int index = 0; index < 3; index++)
        {
            int speed = index == 0 ? 1 : index == 1 ? 2 : 4;
            while (control.Speed != speed) Click();
            dwellStart = dwellEnd = realStart = realEnd = -1;
            system.AddRaw(punch, 1); float begin = Time.time;
            float deadline = Time.realtimeSinceStartup + 110;
            Vector3 heldPosition = Vector3.zero; bool sampled = false, cycled = false; float drift = 0;
            while (punch.Completed < index + 1 && !failed && Time.realtimeSinceStartup < deadline)
            {
                if (punch.Input.Count == 0 && punch.Busy)
                {
                    var replay = machine.GetComponentInChildren<Demo1ReplaySession>();
                    // Select the currently punching replay, not a buffered output.
                    foreach (var candidate in machine.GetComponentsInChildren<Demo1ReplaySession>())
                        if (candidate.Source.CurrentState == LoadingProcessController.LoadingState.Punching) replay = candidate;
                    if (replay != null && replay.Source.CurrentState == LoadingProcessController.LoadingState.Punching)
                    {
                        if (!sampled) { sampled = true; heldPosition = replay.Source.ReplayBottom; }
                        drift = Mathf.Max(drift, Vector3.Distance(heldPosition, replay.Source.ReplayBottom));
                        if (index == 1 && !cycled && Time.time - dwellStart > 5)
                        {
                            Click(); Click(); Click(); cycled = true;
                            Check(control.Speed == 2 && replay.Source.CurrentState == LoadingProcessController.LoadingState.Punching, "cycling playback during punching preserves the active task");
                        }
                    }
                }
                yield return null;
            }
            Check(punch.Completed == index + 1 && sampled && dwellEnd > dwellStart, "real punch cycle completes at " + speed + "x");
            Check(Mathf.Abs((dwellEnd - dwellStart) - 30) < .25f && drift < .001f,
                "turret dwell is 30 simulated seconds with stationary stock: " + (dwellEnd - dwellStart).ToString("F3") + "s, drift=" + drift);
            Check(Mathf.Abs((realEnd - realStart) - 30f / speed) < .75f,
                "real dwell at " + speed + "x is " + (realEnd - realStart).ToString("F3") + "s; full cycle=" + (Time.time - begin).ToString("F3") + " simulated seconds");
        }
        CaptureUI();
        control.enabled = false;
        Check(Time.timeScale == 1, "disabling playback restores normal time");
        control.enabled = true;
        Check(control.Speed == 1 && Time.timeScale == 1 && control.FastForwardButton.GetComponentInChildren<TMP_Text>().text.Contains("×1"), "reenabling starts at normal speed");
        Finish(true);
    }
    void Finish(bool success)
    {
        if (finished) return; finished = true; Time.timeScale = 1;
        Directory.CreateDirectory("Logs"); File.WriteAllText(SnapshotOnly ? "Logs/factory-playback-snapshot-audit.txt" : "Logs/factory-playback-audit.txt", report + (success ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
#endif
