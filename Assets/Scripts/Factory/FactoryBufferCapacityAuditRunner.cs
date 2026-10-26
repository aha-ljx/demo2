#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
public sealed class FactoryBufferCapacityAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder(); bool failed, done;
    float started;
    void OnEnable() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(message + "\n" + stack); } }
    void Check(bool ok, string text) { if (!ok) throw new Exception(text); report.AppendLine("PASS " + text); Debug.Log("BUFFER AUDIT " + text); }
    void Update() { if (!done && (failed || Time.realtimeSinceStartup - started > 300)) Finish(false); }
    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 60; Time.timeScale = 1;
        var production = new GameObject("Punch buffer test").AddComponent<FactoryProduction>(); production.enabled = false;
        var machine = FactoryProductionAuditRunner.Model(0, new Vector3(100, 0, 100), 0);
        production.Register(machine, 0); var punch = machine.GetComponent<FactoryStation>();
        for (int i = 0; i < 9; i++)
        {
            var part = FactoryWorkpiece.Create(punch.Ports.PartWidth, punch.Ports.OutputPort.position + Vector3.up * i * .01f);
            part.SetStage(1); Check(punch.ReturnOutput(part), "seed punch buffer item " + (i + 1));
        }
        production.AddRaw(punch, 2); float limit = Time.time + 200;
        while ((punch.Completed < 1 || punch.Busy) && Time.time < limit && !failed) yield return null;
        Check(punch.Completed == 1 && punch.OutputCount == 10 && punch.Input.Count == 1 && !punch.Busy, "real tenth output fills punch buffer and prevents starting the eleventh");
        var pending = punch.Input.Peek();
        for (int i = 0; i < 90; i++) yield return null;
        Check(pending.Replay == null && punch.Completed == 1 && punch.OutputCount == 10, "full punch buffer leaves the next raw sheet untouched");
        var rejected = FactoryWorkpiece.Create(punch.Ports.PartWidth, Vector3.zero); rejected.SetStage(1);
        Check(!punch.ReturnOutput(rejected) && punch.OutputCount == 10 && rejected.transform.parent == null, "external return cannot overflow or lose the eleventh output");
        var removed = punch.TakeOutput(); removed.transform.SetParent(production.Products, true);
        limit = Time.time + 200;
        while ((punch.Completed < 2 || punch.Busy) && Time.time < limit && !failed) yield return null;
        Check(punch.Completed == 2 && punch.OutputCount == 10 && punch.Input.Count == 0, "taking one punch output automatically resumes exactly one queued task");
        Destroy(rejected.gameObject); Destroy(machine); Destroy(production.Products.gameObject); Destroy(production.gameObject);
        yield return null; yield return null;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-punch-capacity-audit.txt", report + "PASS ALL\n");
        done = true; enabled = false;
        SessionState.SetBool("FactoryGameplayBuffers", true);
        new GameObject("Conveyor buffer workflow audit").AddComponent<Demo1GameplayAuditRunner>();
    }
    void Finish(bool success)
    {
        done = true; Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-punch-capacity-audit.txt", report + "FAIL\n");
        if (Application.isBatchMode) EditorApplication.Exit(1); else EditorApplication.isPlaying = false;
    }
}
#endif
