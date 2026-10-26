using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Observes the actual editor game; never starts a scene, changes speed, feeds
// material or advances a clock. Trace files survive scene retries for comparison.
[InitializeOnLoad]
public static class FactorySimulationAudit
{
    [Serializable] public sealed class Trace
    {
        public string rules;
        public float startingSpeed;
        public int count;
        public long cost;
        public float[] receivedAt;
        public int[] stages;
    }
    static FactoryLevelSession observed;
    static float startingSpeed;
    static bool saved;
    static FactorySimulationAudit() { EditorApplication.update += Observe; }
    static void Observe()
    {
        if (!EditorApplication.isPlaying) { observed = null; return; }
        var level = FactoryLevelSession.Active;
        var simulation = FactorySimulation.Active;
        if (level == null || simulation == null) return;
        if (observed != level)
        {
            observed = level; saved = false; startingSpeed = 0;
        }
        if (level.IsRunning && startingSpeed == 0) startingSpeed = Time.timeScale;
        if (saved || !level.IsEnded) return;
        saved = true;
        var stages = new int[4];
        foreach (var part in UnityEngine.Object.FindObjectsOfType<FactoryWorkpiece>()) stages[part.Stage]++;
        var trace = new Trace { rules = FactoryAssistantSolver.Rules, startingSpeed = startingSpeed,
            count = level.ResultCount, cost = level.ResultCost, receivedAt = simulation.ReceivedAt.ToArray(), stages = stages };
        Directory.CreateDirectory("Logs");
        string path = "Logs/live-playback-" + startingSpeed.ToString("0") + "x.json";
        File.WriteAllText(path, JsonUtility.ToJson(trace, true));
        Debug.Log("LIVE PLAYBACK TRACE " + path + " count=" + trace.count + " arrivals=" + string.Join(",", trace.receivedAt));
    }

    [MenuItem("Tools/Factory/Check Fixed Simulation Scheduler")]
    public static void CheckScheduler()
    {
        var ownerObject = new GameObject("Scheduler test owner");
        var owner = ownerObject.AddComponent<FactoryVisual>();
        try
        {
            var expected = Drive(owner, 1);
            foreach (int group in new[] { 2, 4, 17 })
                if (Drive(owner, group) != expected) throw new Exception("Render grouping changes simulated event ticks");
            bool unwound = false;
            var cancelled = new FactoryRoutine(owner, Cleanup(() => unwound = true));
            cancelled.Advance(1, double.PositiveInfinity); cancelled.Cancel();
            if (!unwound || !cancelled.Done) throw new Exception("Cancellation must unwind reservations");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/fixed-simulation-scheduler-audit.txt",
                "PASS identical event ticks for render groups 1/2/4/17\nPASS nested routines and exact simulation delay\nPASS planning pauses do not advance ticks\nPASS cancellation unwinds finally\nPASS ALL\n");
            Debug.Log("Fixed simulation scheduler: PASS ALL");
        }
        finally { UnityEngine.Object.DestroyImmediate(ownerObject); }
    }
    static string Drive(MonoBehaviour owner, int grouping)
    {
        var events = new List<string>();
        long now = 0;
        var routine = new FactoryRoutine(owner, Example(() => now, events));
        for (long frame = 0; !routine.Done && frame < 100; frame++)
            for (int step = 0; step < grouping && !routine.Done; step++)
            {
                now++;
                // A real-time planning budget may interrupt inside a tick.
                while (!routine.Advance(now, double.NegativeInfinity)) { }
            }
        if (!routine.Done || string.Join(",", events) != "start:1,child:1,end:4")
            throw new Exception("Incorrect tick schedule: " + string.Join(",", events));
        return string.Join(",", events);
    }
    static IEnumerator Example(Func<long> now, List<string> events)
    {
        events.Add("start:" + now());
        yield return Child(now, events);
        yield return new FactorySimulation.Delay(FactorySimulation.Step * 2);
        events.Add("end:" + now());
    }
    static IEnumerator Child(Func<long> now, List<string> events)
    {
        yield return FactorySimulation.PlanningPause;
        events.Add("child:" + now());
        yield return null;
    }
    static IEnumerator Cleanup(Action complete)
    {
        try { while (true) yield return null; }
        finally { complete(); }
    }
}
