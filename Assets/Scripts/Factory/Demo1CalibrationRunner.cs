#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class Demo1CalibrationRunner : MonoBehaviour
{
    readonly StringBuilder log = new StringBuilder();
    LoadingProcessController source;
    bool failed;
    bool finished;
    void Update() { if (failed && !finished) Finish(false); }
    void OnEnable() { Application.logMessageReceived += OnLog; }
    void OnDisable() { Application.logMessageReceived -= OnLog; }
    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) { failed = true; log.AppendLine(message + "\n" + stack); }
    }
    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 60;
        var definition = AssetDatabase.LoadAssetAtPath<Demo1RigDefinition>("Assets/Resources/Demo1Reference/Definition.asset");
        var root = Instantiate(definition.rigPrefab);
        source = root.GetComponentInChildren<LoadingProcessController>();
        source.ReplayMode = true; source.ReplayScope = root.transform; source.CaptureReplayNodes();
        source.InitializeDemo1(); source.OwnReplayRigs();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
        if (!source.ReplayReady) { Finish(false); yield break; }
        source.BeginReplay(); definition.inputs[0] = source.ReplayBottom;
        float start = Time.realtimeSinceStartup;
        string last = null;
        while (!failed && Time.realtimeSinceStartup - start < 500)
        {
            string checkpoint = source.ReplayCheckpoint;
            if (checkpoint != null && checkpoint != last)
            {
                log.AppendLine(checkpoint + " part=" + source.ReplayBottom.ToString("F5") + " base=" + source.ReplayBasePosition.ToString("F5"));
                if (checkpoint == "PunchOutput") definition.outputs[0] = source.ReplayBottom;
                if (checkpoint == "PickupDock") definition.docks[0] = source.ReplayBasePosition;
                if (checkpoint == "BendDock") { definition.docks[1] = source.ReplayBasePosition; definition.inputs[1] = source.ReplayBottom; }
                if (checkpoint == "BendOutput") definition.outputs[1] = source.ReplayBottom;
                if (checkpoint == "HDock") definition.docks[2] = source.ReplayBasePosition;
                if (checkpoint == "WeldInput") definition.inputs[2] = source.ReplayBottom;
                if (checkpoint == "Complete")
                {
                    definition.outputs[2] = source.ReplayBottom; definition.calibrated = true;
                    EditorUtility.SetDirty(definition); AssetDatabase.SaveAssets(); Finish(true); yield break;
                }
                last = checkpoint; source.ContinueReplay();
            }
            yield return null;
        }
        Finish(false);
    }
    void Finish(bool success)
    {
        finished = true;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/demo1-calibration.txt", log + (success ? "PASS\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        else { Time.captureDeltaTime = 0; EditorApplication.isPlaying = false; }
    }
}
#endif
