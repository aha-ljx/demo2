#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Unlike the previous completion-only audit, this checks the actual visible
// mesh matrices against Demo1 on every sampled animation frame.
[DefaultExecutionOrder(500)]
public sealed class FactoryProductionAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    readonly List<Demo1MeshMirror> mirrors = new List<Demo1MeshMirror>();
    readonly HashSet<LoadingProcessController.LoadingState> states = new HashSet<LoadingProcessController.LoadingState>();
    FactoryProduction system;
    Demo1ReplaySession replay;
    bool failed;
    bool compare;
    float error;
    float started;
    int samples;
    readonly List<System.Func<Matrix4x4>> fixedSpaces = new List<System.Func<Matrix4x4>>();
    readonly List<Matrix4x4> initialSpaces = new List<Matrix4x4>();
    float mappingDrift;
    void OnEnable() { Application.logMessageReceived += Log; started = Time.realtimeSinceStartup; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) { failed = true; report.AppendLine(message + "\n" + stack); }
    }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
    void Update() { if (failed || Time.realtimeSinceStartup - started > 600) Finish(false); }
    void LateUpdate()
    {
        if (!compare) return;
        foreach (var mirror in mirrors) { mirror.Apply(); error = Mathf.Max(error, mirror.MaximumError()); }
        for (int i = 0; i < fixedSpaces.Count; i++)
        {
            Matrix4x4 current = fixedSpaces[i]();
            for (int k = 0; k < 16; k++) mappingDrift = Mathf.Max(mappingDrift, Mathf.Abs(current[k] - initialSpaces[i][k]));
        }
        samples++;
        if (replay != null) states.Add(replay.Source.CurrentState);
    }
    public static GameObject Model(int kind, Vector3 position, float yaw)
    {
        string[] names = { "Placeable_冲床", "Placeable_折弯", "Placeables_焊接", "Placeables_机械臂", "Placeable_滑槽" };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Placeables/" + names[kind] + ".prefab");
        var item = Instantiate(prefab); item.name = names[kind];
        item.AddComponent<EquipmentGrid>().Initialize(2.1224f);
        item.transform.rotation = Quaternion.Euler(0, yaw, 0);
        item.GetComponent<EquipmentGrid>().Snap(position);
        if (kind < 3) item.AddComponent<FactoryPorts>().Initialize(kind, TMPro.TMP_Settings.defaultFontAsset);
        if (kind == 4) item.AddComponent<FactoryRail>().Measure();
        return item;
    }
    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 60;
        system = new GameObject("Production").AddComponent<FactoryProduction>();
        var part = FactoryWorkpiece.Create(1.69792f, Vector3.zero);
        replay = Demo1ReplaySession.Create(part); part.Replay = replay;
        var definition = Demo1RigDefinition.Load();
        var modules = new GameObject[3]; var copies = new GameObject[3];
        for (int kind = 0; kind < 3; kind++)
        {
            int index = kind;
            modules[kind] = Model(kind, new Vector3(70 + 40 * kind, 0, 80), kind * 90);
            copies[kind] = Model(kind, new Vector3(70 + 40 * kind, 0, 120), 270 - kind * 90);
            var mirror = new Demo1MeshMirror(modules[kind].transform, replay, () => definition.Mapping(modules[index].transform, index));
            var duplicate = new Demo1MeshMirror(copies[kind].transform, replay, () => definition.Mapping(copies[index].transform, index));
            Check(mirror.Count > 10 && duplicate.Count == mirror.Count, "matching Demo1 meshes for module " + kind + ": " + mirror.Count);
            Check(mirror.Missing.Count == 0, "all original module mesh IDs are bound: " + string.Join(",", mirror.Missing));
            mirrors.Add(mirror); mirrors.Add(duplicate);
            fixedSpaces.Add(() => definition.Mapping(modules[index].transform, index)); initialSpaces.Add(fixedSpaces[fixedSpaces.Count - 1]());
            fixedSpaces.Add(() => definition.Mapping(copies[index].transform, index)); initialSpaces.Add(fixedSpaces[fixedSpaces.Count - 1]());
        }
        Matrix4x4 space = definition.Mapping(modules[0].transform, 0);
        replay.Space = () => space;
        var arm = Model(3, new Vector3(30, 0, 80), 0);
        Matrix4x4 armSpace = Matrix4x4.identity;
        var armMirror = new Demo1MeshMirror(arm.transform, replay, () => armSpace);
        Check(armMirror.Count >= 10, "original suction, wrist, links and base are matched");
        mirrors.Add(armMirror);
        compare = true;
        replay.Source.BeginReplay();
        string previous = null;
        while (!replay.Failed)
        {
            string checkpoint = replay.Source.ReplayCheckpoint;
            if (checkpoint != null && checkpoint != previous)
            {
                previous = checkpoint;
                report.AppendLine("CHECKPOINT " + checkpoint);
                int kind = checkpoint == "BendDock" || checkpoint == "BendOutput" || checkpoint == "HTravelReady" ? 1 :
                    checkpoint == "HDock" || checkpoint == "WeldInput" || checkpoint == "Complete" ? 2 : 0;
                space = definition.Mapping(modules[kind].transform, kind);
                armSpace = space;
                if (checkpoint == "Complete") break;
                replay.Source.ContinueReplay();
            }
            if (replay.Source.CurrentState >= LoadingProcessController.LoadingState.ArmRotateToPart && previous == "PickupDock") armSpace = space;
            yield return null;
        }
        yield return null;
        Check(!replay.Failed, "original Demo1 sequence reaches completion");
        Check(error < 0.003f, "per-frame transformed mesh matrix error < 0.003; actual=" + error.ToString("G8"));
        Check(samples > 1000, "compared " + samples + " animation frames");
        Check(mappingDrift < 0.00001f, "module mapping uses a fixed rest reference without cumulative drift; actual=" + mappingDrift);
        Check(states.Contains(LoadingProcessController.LoadingState.ClampMaterial) && states.Contains(LoadingProcessController.LoadingState.PunchClampAcquire), "both original clamp handoffs executed");
        Check(states.Contains(LoadingProcessController.LoadingState.PositionBendEdge) && states.Contains(LoadingProcessController.LoadingState.CloseBendingDie), "original four-edge positioning and die strokes executed");
        Check(states.Contains(LoadingProcessController.LoadingState.PlaceBentPartInHClamps), "original downward H-clamp insertion executed");
        Check(states.Contains(LoadingProcessController.LoadingState.IndexHTurntable) && states.Contains(LoadingProcessController.LoadingState.ConveyorRunning), "original welding index and rolling conveyor executed");
        compare = false; replay.Freeze();
        Finish(true);
    }
    void Finish(bool success)
    {
        enabled = false; Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/demo1-animation-parity.txt", report.ToString() + (success ? "PASS ALL\n" : "FAIL\n"));
        if (success && SessionState.GetBool("Demo1AuditAll", false)) { Time.captureDeltaTime = 0; EditorApplication.isPlaying = false; }
        else if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        else { Time.captureDeltaTime = 0; EditorApplication.isPlaying = false; }
    }
}
#endif
