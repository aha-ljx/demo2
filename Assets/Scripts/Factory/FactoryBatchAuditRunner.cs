#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed class FactoryBatchAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    bool failed, finished;
    float started;
    void OnEnable() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error)
        { failed = true; report.AppendLine(message + "\n" + stack); }
    }
    void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        report.AppendLine("PASS " + message);
    }
    void Update() { if (!finished && (failed || Time.realtimeSinceStartup - started > 360)) Finish(false); }
    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 60;
        yield return null;
        var placement = FindObjectOfType<PlacementSystem>();
        Canvas.ForceUpdateCanvases();
        string[] names = { "冲床", "折弯", "焊接", "机械臂", "长滑槽" };
        for (int i = 0; i < names.Length; i++)
        {
            var label = placement.slotImages[i].GetComponentInChildren<TMP_Text>();
            label.ForceMeshUpdate();
            Check(label.text == (i + 1) + "\n" + names[i], "slot " + (i + 1) + " has its module name");
            Check(label.textInfo.lineCount == 2 && !label.isTextTruncated, "slot " + (i + 1) + " renders both complete lines");
            var rect = placement.slotImages[i].rectTransform;
            int chinese = 0;
            foreach (var ch in label.textInfo.characterInfo)
            {
                if (ch.character < 0x4e00 || ch.character > 0x9fff) continue;
                Check(ch.isVisible && ch.textElement.unicode == ch.character, "Chinese glyph exists and is visible: " + ch.character);
                Vector3 bottom = rect.InverseTransformPoint(label.transform.TransformPoint(ch.bottomLeft));
                Vector3 top = rect.InverseTransformPoint(label.transform.TransformPoint(ch.topRight));
                Check(rect.rect.Contains(bottom) && rect.rect.Contains(top), "Chinese glyph fits within slot: " + ch.character);
                chinese++;
            }
            Check(chinese == names[i].Length, "all module name characters are rendered");
        }
        var production = FactoryProduction.Instance;
        var model = FactoryProductionAuditRunner.Model(0, new Vector3(60, 0, 60), 90);
        production.Register(model, 0);
        var punch = model.GetComponent<FactoryStation>();
        var originals = new List<FactoryWorkpiece>();
        for (int i = 0; i < 3; i++)
        {
            var part = FactoryWorkpiece.Create(punch.Ports.PartWidth, punch.Ports.InputPort.position);
            originals.Add(part); punch.Enqueue(part);
        }
        var startedOrder = new List<FactoryWorkpiece>();
        bool resumedWithPendingOutput = false;
        float limit = Time.time + 420;
        while (punch.Completed < 3 && Time.time < limit && !failed)
        {
            foreach (var part in originals)
                if (part.Replay != null && !startedOrder.Contains(part)) startedOrder.Add(part);
            if (punch.OutputCount > 0 && punch.Busy && punch.Input.Count < 2) resumedWithPendingOutput = true;
            yield return null;
        }
        Check(punch.Completed == 3 && punch.OutputCount == 3 && punch.Input.Count == 0,
            "all three queued plates finish without a downstream arm");
        Check(resumedWithPendingOutput, "feeder restarts while earlier output is still waiting");
        Check(startedOrder.Count == 3 && startedOrder[0] == originals[0] && startedOrder[1] == originals[1] && startedOrder[2] == originals[2],
            "input plates are processed in FIFO order");
        for (int i = 1; i < 3; i++)
        {
            Bounds below = FactoryWorkpiece.GeometryBounds(originals[i - 1].transform);
            Bounds above = FactoryWorkpiece.GeometryBounds(originals[i].transform);
            Check(above.min.y >= below.max.y - 0.001f && above.min.y - below.max.y < 0.02f,
                "finished plates stack without overlap or floating");
        }
        for (int i = 2; i >= 0; i--)
        {
            Check(punch.TakeOutput() == originals[i], "arm takes the exposed top output plate");
            Check(punch.OutputCount == i, "taking output reveals the next stored plate");
        }
        Check(punch.Output == null, "all outputs can be drained without loss");
        Finish(true);
    }
    void Finish(bool ok)
    {
        if (finished) return;
        finished = true; Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/factory-batch-audit.txt", report.ToString() + (ok ? "PASS ALL\n" : "FAIL\n"));
        Time.captureDeltaTime = 0;
        if (ok && SessionState.GetBool("FactoryBatchAll", false)) EditorApplication.isPlaying = false;
        else if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
#endif
