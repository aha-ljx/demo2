#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class FactorySharedBendingAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    FactoryProduction system;
    bool done, failed;
    float started;
    void OnEnable() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        failed = true; report.AppendLine(message + "\n" + stack);
    }
    void Update() { if (!done && (failed || Time.realtimeSinceStartup - started > 300)) Finish(false); }
    void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        report.AppendLine("PASS " + message);
        Debug.Log("SHARED BENDING " + message);
    }
    FactoryStation Station(int kind, Vector3 dock, float yaw = 0)
    {
        var model = FactoryProductionAuditRunner.Model(kind, new Vector3(100, 0, 100), yaw);
        var ports = model.GetComponent<FactoryPorts>();
        Vector3 offset = dock - ports.DockPosition; offset.y = 0;
        model.transform.position += offset;
        model.AddComponent<FactoryStation>().Initialize();
        return model.GetComponent<FactoryStation>();
    }
    FactoryRail Rail(Vector3 center)
    {
        var model = FactoryProductionAuditRunner.Model(4, center, 0);
        var grid = model.GetComponent<EquipmentGrid>();
        model.transform.position += center - grid.Footprint.center + Vector3.up * grid.Footprint.center.y;
        var rail = model.GetComponent<FactoryRail>();
        rail.Measure(); system.Rails.Add(rail);
        return rail;
    }
    FactoryArm Arm(FactoryRail rail)
    {
        var model = FactoryProductionAuditRunner.Model(3, rail.GetComponent<EquipmentGrid>().Footprint.center, 0);
        var arm = model.AddComponent<FactoryArm>(); arm.Initialize(rail);
        return arm;
    }
    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 30;
        system = new GameObject("Shared bending production").AddComponent<FactoryProduction>();
        system.enabled = false;
        system.armTravelMultiplier = 12;
        foreach (int sharedKind in new[] { 0, 1 })
        for (int yaw = 0; yaw < 360; yaw += 90)
        {
            var bend = Station(sharedKind, Vector3.zero, yaw);
            var ports = bend.Ports;
            Check(Mathf.Abs(ports.DockArea.Footprint.size.x - ports.PartWidth * 2.8f) < .001f,
                "shared dock kind " + sharedKind + " has twice the original side length at yaw " + yaw);
            float edge = ports.DockArea.Footprint.extents.x;
            var left = Rail(new Vector3(-edge - 1.5f, 0, 0));
            var right = Rail(new Vector3(edge + 1.5f, 0, 0));
            system.Graph.Build(system.Rails);
            var a = Arm(left); var b = Arm(right);
            Check(!system.Graph.Same(left, right), "shared area keeps the two rail networks independent");
            Check(a.CanReach(ports.DockPosition, out _) && b.CanReach(ports.DockPosition, out _),
                "both independent arms reach the same calibrated bending pose");
            right.transform.position += Vector3.right * .05f; right.Measure(); system.Graph.Build(system.Rails);
            Check(!b.CanReach(ports.DockPosition, out _), "a visible gap still blocks the second arm");
            DestroyImmediate(a.gameObject); DestroyImmediate(b.gameObject);
            DestroyImmediate(left.gameObject); DestroyImmediate(right.gameObject);
            DestroyImmediate(bend.gameObject);
            yield return null;
        }
        foreach (int kind in new[] { 2 })
        {
            var other = Station(kind, new Vector3(100, 0, 100));
            Check(Mathf.Abs(other.Ports.DockArea.Footprint.size.x - other.Ports.PartWidth * 1.4f) < .001f,
                "other module docking sizes are preserved");
            DestroyImmediate(other.gameObject);
        }
        var shared = Station(1, Vector3.zero);
        float half = shared.Ports.DockArea.Footprint.extents.x;
        var arms = new FactoryArm[2];
        var punches = new FactoryStation[2];
        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1 : 1;
            FactoryRail outer = null;
            for (int tile = 0; tile < 22; tile++) outer = Rail(new Vector3(sign * (half + 1.5f + tile * 3), 0, 0));
            Vector3 punchDock = new Vector3(sign * (half + 1.5f + 10 * 3), 0, 0);
            Vector3 weldDock = new Vector3(sign * (half + 1.5f + 20 * 3), 0, 0);
            punches[side] = Station(0, punchDock, side * 180);
            Station(2, weldDock, side * 180);
            system.Graph.Build(system.Rails);
            arms[side] = Arm(outer);
            system.AddRaw(punches[side], 1);
        }
        Check(!system.Graph.Same(arms[0].Rail, arms[1].Rail), "production uses two separate rail networks");
        system.enabled = true;
        var bent = new HashSet<FactoryArm>();
        var parts = new HashSet<FactoryWorkpiece>();
        bool departureLocked = false, releasedWhileTravelling = false;
        FactoryArm departing = null;
        float deadline = Time.time + 900;
        while (bent.Count < 2 && Time.time < deadline && !failed)
        {
            if (shared.Incoming > 1) throw new Exception("two arms were assigned to the bending machine simultaneously");
            if (shared.Output != null)
            {
                var holder = shared.Output.Replay.Holder;
                Check(holder != null && holder.Holding, "completed bending part remains held by its original arm");
                parts.Add(shared.Output); bent.Add(holder);
            }
            if (shared.ReservedBy != null && shared.Output == null)
            {
                departing = shared.ReservedBy;
                if (shared.CanAccept) throw new Exception("departing arm released the shared bending pose too early");
                departureLocked = true;
            }
            if (departing != null && shared.ReservedBy == null && departing.Busy) releasedWhileTravelling = true;
            yield return null;
        }
        Check(bent.Contains(arms[0]) && bent.Contains(arms[1]) && parts.Count == 2,
            "both independent arms complete real folding on one shared machine with their own workpieces");
        Check(departureLocked, "bending machine stays reserved while the first arm leaves its dock");
        Check(releasedWhileTravelling, "dock becomes available after departure while the first arm continues downstream");
        Finish(true);
    }
    void Finish(bool ok)
    {
        if (done) return; done = true;
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/factory-shared-bending-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        Time.captureDeltaTime = 0;
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
#endif
