using System.Collections.Generic;
using UnityEngine;

public sealed class FactoryModuleStatistics
{
    public GameObject Root;
    public int Kind;
    public string Name;
    public bool Removed;
    public int Completed;
    public float CreatedAt;
    public float Rate;
    public float MovementRate;
    internal Vector3 LastPosition;
    internal float Distance;
    internal float MovementSeconds;
    internal readonly Queue<float> Completions = new Queue<float>();
    public readonly List<Vector2> History = new List<Vector2>();
    public string Unit => Kind == 3 ? "单位/秒" : "件/分钟";
    public string State
    {
        get
        {
            if (Removed || Root == null) return "已撤回";
            if (FactoryLevelSession.HasEnded) return "生产已结束";
            if (FactoryLevelSession.Active != null && !FactoryLevelSession.Active.IsRunning) return "规划中 · 等待开始生产";
            var station = Root.GetComponent<FactoryStation>();
            if (station != null) return station.Status;
            var arm = Root.GetComponent<FactoryArm>();
            return arm != null ? arm.Status : "等待任务";
        }
    }
}

// Measurements are simulation seconds, independent of wall-clock playback speed.
public sealed partial class FactoryStatistics : MonoBehaviour
{
    public static FactoryStatistics Active { get; private set; }
    public readonly List<FactoryModuleStatistics> Modules = new List<FactoryModuleStatistics>();
    public readonly List<Vector2> ProductHistory = new List<Vector2>();
    readonly Dictionary<int, FactoryModuleStatistics> lookup = new Dictionary<int, FactoryModuleStatistics>();
    readonly int[] serials = new int[4];
    readonly Queue<float> products = new Queue<float>();
    public float Elapsed { get; private set; }
    public float ProductRate { get; private set; }
    public int ProductCount { get; private set; }
    float nextSample = 1;
    void Awake() { Active = this; ProductHistory.Add(Vector2.zero); }

    public FactoryModuleStatistics Register(GameObject item, int kind)
    {
        if (kind < 0 || kind > 3 || item == null) return null;
        if (lookup.TryGetValue(item.GetInstanceID(), out var existing)) return existing;
        string[] names = { "冲床", "折弯机", "焊接机", "机械臂" };
        var module = new FactoryModuleStatistics { Root = item, Kind = kind,
            Name = names[kind] + (++serials[kind]), CreatedAt = Elapsed };
        var arm = item.GetComponent<FactoryArm>();
        if (arm != null) module.LastPosition = arm.TrackPosition;
        module.History.Add(new Vector2(Elapsed, 0));
        lookup.Add(item.GetInstanceID(), module); Modules.Add(module); item.name = module.Name;
        return module;
    }
    public FactoryModuleStatistics Find(GameObject item)
        => item != null && lookup.TryGetValue(item.GetInstanceID(), out var module) ? module : null;
    public static bool ValidSavedName(int kind, string name, out int number)
    {
        number = 0;
        string[] prefixes = { "冲床", "折弯机", "焊接机", "机械臂" };
        if (kind < 0 || kind > 3 || string.IsNullOrEmpty(name) || !name.StartsWith(prefixes[kind], System.StringComparison.Ordinal)) return false;
        string suffix = name.Substring(prefixes[kind].Length);
        return int.TryParse(suffix, out number) && number > 0 && number < 1000000 && suffix == number.ToString();
    }
    public void RestoreName(GameObject item, string name)
    {
        var module = Find(item);
        if (module == null || !ValidSavedName(module.Kind, name, out int number)) return;
        module.Name = item.name = name; serials[module.Kind] = Mathf.Max(serials[module.Kind], number);
    }
    public void ResetPlanningModules()
    {
        Modules.Clear(); lookup.Clear(); System.Array.Clear(serials, 0, serials.Length);
    }
    public void Withdraw(GameObject item)
    {
        var module = Find(item);
        if (module == null || module.Removed) return;
        module.Removed = true; module.Rate = module.MovementRate = 0;
        module.History.Add(new Vector2(Elapsed, 0));
    }
    public void RecordCompletion(GameObject item)
    {
        var module = Find(item);
        if (module == null || module.Removed || !FactoryLevelSession.CanSimulate) return;
        module.Completed++; module.Completions.Enqueue(Elapsed);
        RefreshRates();
    }
    public void RecordProduct()
    {
        if (!FactoryLevelSession.CanSimulate) return;
        ProductCount++; products.Enqueue(Elapsed); RefreshRates();
    }
    internal void Advance(float seconds)
    {
        if (seconds <= 0) return;
        Elapsed += seconds;
        foreach (var module in Modules)
        {
            if (module.Removed || module.Root == null || module.Kind != 3) continue;
            var arm = module.Root.GetComponent<FactoryArm>();
            Vector3 position = arm.TrackPosition;
            // Idle editing/teleports do not count as robot transport.
            if (arm.Busy) module.Distance += Vector3.Distance(position, module.LastPosition);
            module.MovementSeconds += seconds; module.LastPosition = position;
        }
        RefreshRates();
        if (Elapsed >= nextSample)
        {
            Append(ProductHistory, new Vector2(Elapsed, ProductRate));
            foreach (var module in Modules)
            {
                if (module.Removed || module.Root == null) continue;
                if (module.Kind == 3)
                {
                    module.MovementRate = module.Distance / Mathf.Max(.0001f, module.MovementSeconds);
                    module.Distance = module.MovementSeconds = 0;
                }
                Append(module.History, new Vector2(Elapsed, module.Kind == 3 ? module.MovementRate : module.Rate));
            }
            nextSample = Mathf.Floor(Elapsed) + 1;
        }
    }
    static void Append(List<Vector2> history, Vector2 point)
    {
        history.Add(point);
        if (history.Count > 3601) history.RemoveRange(0, history.Count - 3601);
    }
    void RefreshRates()
    {
        ProductRate = WindowRate(products, 0);
        foreach (var module in Modules)
            module.Rate = module.Removed || module.Root == null ? 0 : WindowRate(module.Completions, module.CreatedAt);
    }
    float WindowRate(Queue<float> events, float createdAt)
    {
        while (events.Count > 0 && Elapsed - events.Peek() >= 60) events.Dequeue();
        float duration = Mathf.Min(60, Elapsed - createdAt);
        return duration > .0001f ? events.Count * 60f / duration : 0;
    }
    void Update()
    {
        if (FactoryLevelSession.CanSimulate && !FactorySimulation.Running) Advance(Time.deltaTime);
        else if (!FactoryLevelSession.CanSimulate) foreach (var module in Modules)
            if (module.Root != null && module.Kind == 3) module.LastPosition = module.Root.GetComponent<FactoryArm>().TrackPosition;
        UpdateInterface();
    }
    void OnDestroy() { DestroyInterface(); if (Active == this) Active = null; }
}

