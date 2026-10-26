using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// All manufacturing owns one clock and one ordered coroutine queue. Rendering
// may consume zero, one or many steps; unfinished computation never consumes
// simulated time. Keep UI/player input on Unity's unscaled render clock.
[DefaultExecutionOrder(1000)]
public sealed class FactorySimulation : MonoBehaviour
{
    public const float Step = 1f / 60f;
    public static FactorySimulation Active { get; private set; }
    public static bool Running => Active != null;
    public static float Delta => Running ? Step : Time.deltaTime;
    public static float Now => Running ? (float)(Active.Tick / 60d) : Time.time;
    public static readonly object PlanningPause = new object();
    public long Tick { get; private set; }
    public readonly List<float> ReceivedAt = new List<float>();
    readonly List<FactoryRoutine> routines = new List<FactoryRoutine>();
    readonly List<Demo1ReplaySession> replays = new List<Demo1ReplaySession>();
    FactoryProduction factory;
    double accumulator;
    bool pending;
    int cursor, count;

    public void Initialize(FactoryProduction production) { factory = production; Active = this; }
    public static FactoryRoutine Start(MonoBehaviour owner, IEnumerator routine)
    {
        if (!Running) return FactoryRoutine.Native(owner, routine);
        var task = new FactoryRoutine(owner, routine);
        Active.routines.Add(task);
        return task;
    }
    public static void Stop(FactoryRoutine routine) { routine?.Cancel(); }
    public static void Stop(MonoBehaviour owner)
    {
        if (!Running || ReferenceEquals(owner, null)) return;
        for (int i = 0; i < Active.routines.Count; i++)
            if (ReferenceEquals(Active.routines[i].Owner, owner)) Active.routines[i].Cancel();
    }
    public static void Register(Demo1ReplaySession replay) { if (Running) Active.replays.Add(replay); }
    public static void Unregister(Demo1ReplaySession replay) { if (Running) Active.replays.Remove(replay); }
    public static object Wait(float seconds) => Running ? (object)new Delay(seconds) : new WaitForSeconds(seconds);
    public sealed class Delay
    {
        public readonly float Seconds;
        public Delay(float seconds) { Seconds = Mathf.Max(0, seconds); }
    }
    void Update()
    {
        if (factory == null || !FactoryLevelSession.CanSimulate || Time.timeScale <= 0) return;
        // Never discard backlog or advance the level past unfinished work.
        accumulator += (double)Time.unscaledDeltaTime * Time.timeScale;
        double deadline = Time.realtimeSinceStartupAsDouble + .008;
        while ((pending || accumulator + 1e-9 >= Step) && FactoryLevelSession.CanSimulate)
        {
            if (!pending)
            {
                pending = true; Tick++; cursor = 0;
                factory.SimulationTick();
                for (int i = 0; i < factory.Stations.Count; i++)
                    if (factory.Stations[i] != null) factory.Stations[i].SimulationTick();
                count = routines.Count;
            }
            while (cursor < count)
            {
                FactoryRoutine task = routines[cursor];
                if (!task.Advance(Tick, deadline)) return;
                cursor++;
                if (Time.realtimeSinceStartupAsDouble >= deadline) return;
            }
            // Geometry queried by the next step must be committed, not the
            // last render frame's poses (especially when fast-forwarding).
            for (int i = 0; i < factory.Stations.Count; i++)
                if (factory.Stations[i] != null) factory.Stations[i].SimulationPresent();
            for (int i = 0; i < factory.Arms.Count; i++)
                if (factory.Arms[i] != null) factory.Arms[i].SimulationPresent();
            for (int i = 0; i < replays.Count; i++)
                if (replays[i] != null && replays[i].enabled && replays[i].Space != null) replays[i].ApplyWorkpiece();
            routines.RemoveAll(task => task.Done);
            accumulator -= Step; pending = false;
            factory.Statistics.Advance(Step);
            // Count arrivals at the same tick before deadline settlement.
            while (ReceivedAt.Count < factory.Stored) ReceivedAt.Add(Now);
            if (FactoryLevelSession.Active != null) FactoryLevelSession.Active.SimulationAdvance(Tick);
            if (Time.realtimeSinceStartupAsDouble >= deadline) return;
        }
    }
    void OnDestroy()
    {
        foreach (var task in routines) task.Cancel();
        routines.Clear(); replays.Clear();
        if (Active == this) Active = null;
    }
}

public sealed class FactoryRoutine
{
    public MonoBehaviour Owner { get; }
    public bool Done { get; private set; }
    readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    Coroutine native;
    bool executing;
    long wake;
    FactoryRoutine waiting;
    public FactoryRoutine(MonoBehaviour owner, IEnumerator body) { Owner = owner; stack.Push(body); }
    public static FactoryRoutine Native(MonoBehaviour owner, IEnumerator body)
    {
        var result = new FactoryRoutine(owner, body);
        result.native = owner.StartCoroutine(body); return result;
    }
    public void Cancel()
    {
        if (Done) return;
        Done = true;
        if (native != null && Owner != null) Owner.StopCoroutine(native);
        if (!executing) DisposeStack();
    }
    void DisposeStack()
    {
        while (stack.Count > 0)
            try { (stack.Pop() as IDisposable)?.Dispose(); }
            catch (Exception error) { Debug.LogException(error, Owner); }
    }
    // False suspends computation inside this step; true advances the queue.
    public bool Advance(long tick, double deadline)
    {
        if (Done || wake > tick || waiting != null && !waiting.Done) return true;
        if (Owner == null || !Owner.gameObject.activeInHierarchy) { Cancel(); return true; }
        waiting = null; executing = true;
        try
        {
            while (stack.Count > 0 && !Done)
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); (current as IDisposable)?.Dispose(); continue; }
                object yielded = current.Current;
                if (ReferenceEquals(yielded, FactorySimulation.PlanningPause))
                {
                    if (Time.realtimeSinceStartupAsDouble >= deadline) return false;
                    continue;
                }
                if (yielded is IEnumerator nested) { stack.Push(nested); continue; }
                if (yielded is FactorySimulation.Delay delay)
                {
                    wake = tick + Math.Max(1, (long)Math.Ceiling(delay.Seconds / (double)FactorySimulation.Step - 1e-5));
                    return true;
                }
                if (yielded is FactoryRoutine dependency) { waiting = dependency; return true; }
                if (yielded != null) throw new InvalidOperationException("Unsupported manufacturing yield: " + yielded.GetType().Name);
                wake = tick + 1; return true;
            }
            Done = true; return true;
        }
        catch (Exception error) { Done = true; Debug.LogException(error, Owner); return true; }
        finally { executing = false; if (Done) DisposeStack(); }
    }
}
