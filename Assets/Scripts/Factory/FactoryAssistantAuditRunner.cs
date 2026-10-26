#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FactoryAssistantAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    bool done, candidateFailed;
    public bool ReverifyBest;
    public bool SharedPunchOnly;
    public bool RefreshMotionCatalog;
    float began;
    int originalFrameRate, originalVSync;
    void OnEnable() { began = Time.realtimeSinceStartup; originalFrameRate = Application.targetFrameRate; originalVSync = QualitySettings.vSyncCount; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; Application.targetFrameRate = originalFrameRate; QualitySettings.vSyncCount = originalVSync; }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) { candidateFailed = true; report.AppendLine("CANDIDATE ERROR " + message); } }
    void Update() { if (!done && Time.realtimeSinceStartup - began > 1800) Finish(false); }
    void Note(string message)
    {
        report.AppendLine(message); Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-assistant-progress.txt", report.ToString());
    }
    IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        var profiles = new List<FactoryAssistantProfile>();
        int[][] configurations = { new[] { 1, 1, 0 }, new[] { 1, 1, 1 }, new[] { 1, 2, 1 }, new[] { 2, 1, 0 }, new[] { 2, 1, 1 }, new[] { 3, 1, 1 }, new[] { 4, 1, 1 }, new[] { 2, 1, 1, 60 }, new[] { 3, 1, 1, 60 }, new[] { 2, 1, 1, 60, 1 },
            new[] { 2, 1, 1, 60, 1, 3, 18 }, new[] { 2, 1, 1, 60, 2, 3, 18 }, new[] { 2, 1, 1, 60, 1, 1, 18 }, new[] { 2, 1, 1, 60, 1, 2, 18 },
            new[] { 2, 1, 1, 60, 1, 3, 24 }, new[] { 2, 1, 1, 60, 2, 3, 24 }, new[] { 2, 1, 1, 60, 1, 1, 24 }, new[] { 2, 1, 1, 60, 1, 2, 24 }, new[] { 1, 1, 1, 60, 1 },
            new[] { 2, 1, 1, 60, 2, 2, 18 }, new[] { 2, 1, 1, 60, 2, 2, 24 },
            new[] { 2, 1, 1, 60, 2, 2, 15, 1, 12 }, new[] { 2, 1, 1, 60, 1, 2, 15, 1, 12 },
            new[] { 2, 1, 1, 60, 2, 2, 18, 1, 12 }, new[] { 2, 1, 1, 60, 1, 2, 18, 1, 12 },
            new[] { 2, 1, 1, 60, 2, 2, 15, 0, 12 }, new[] { 2, 1, 1, 60, 1, 2, 15, 0, 12 },
            new[] { 1, 1, 1, 60, 1, 8, 18 },
            new[] { 2, 1, 1, 60, 2, 3, 18, 0, 12 }, new[] { 2, 1, 1, 60, 1, 3, 18, 0, 12 },
            new[] { 2, 1, 1, 60, 2, 3, 15, 0, 12 }, new[] { 2, 1, 1, 60, 1, 3, 15, 0, 12 },
            new[] { 2, 1, 1, 60, 2, 1, 15, 0, 12 }, new[] { 2, 1, 1, 60, 1, 1, 15, 0, 12 },
            new[] { 2, 1, 1, 60, 2, 3, 15, 0, 9 }, new[] { 2, 1, 1, 60, 2, 1, 15, 0, 9 },
            new[] { 2, 1, 1, 60, 2, 1, 18, 0, 12 }, new[] { 2, 1, 1, 60, 1, 1, 18, 0, 12 },
            new[] { 2, 1, 1, 60, 2, 1, 18, 0, 9 }, new[] { 2, 1, 1, 60, 2, 3, 18, 0, 3 }, new[] { 2, 1, 1, 60, 2, 3, 18, 0, 0 } };
        float scale = 0;
        const string cacheFile = "Logs/assistant-profile-cache.json";
        HashSet<string> motionCandidates = null;
        if (RefreshMotionCatalog)
        {
            var previous = JsonUtility.FromJson<FactoryAssistantCatalog>(Resources.Load<TextAsset>("FactoryAssistant/VerifiedPlans").text);
            // A clock change can change the frontier. Re-run every catalogued
            // layout, including formerly dominated ones; never relabel old times.
            motionCandidates = new HashSet<string>(previous.profiles.Select(p => p.key));
            Note("MOTION REVERIFY all previous profiles: " + string.Join(", ", motionCandidates));
        }
        if (!RefreshMotionCatalog && File.Exists(cacheFile))
        {
            var cache = JsonUtility.FromJson<FactoryAssistantCatalog>(File.ReadAllText(cacheFile));
            if (cache != null && cache.rules == FactoryAssistantSolver.Rules && cache.profiles != null) profiles.AddRange(cache.profiles);
        }
        string reverifyKey = null;
        if (ReverifyBest)
        {
            var existing = JsonUtility.FromJson<FactoryAssistantCatalog>(File.ReadAllText(cacheFile)); scale = existing.scale;
            var best = SharedPunchOnly ? FactoryAssistantSolver.SolveBestSharedPunch(600, existing, scale, 3, 3, 2) : FactoryAssistantSolver.Solve(10, 600, existing, scale, 3, 3, 2);
            if (best.best == null) { Note("FAIL no best candidate to reverify"); Finish(false); yield break; }
            reverifyKey = best.best.key; profiles.RemoveAll(p => p.key == reverifyKey);
            Note("REVERIFY " + reverifyKey + " quantity=" + best.quantity + " cost=" + best.cost);
        }
        for (int candidate = 0; candidate < configurations.Length; candidate++)
        {
            var config = configurations[candidate]; string key = string.Join("-", config) + (config.Length > 5 ? "-v3" : "-v2");
            if (motionCandidates != null && !motionCandidates.Contains(key)) continue;
            if (reverifyKey != null && key != reverifyKey) continue;
            Time.captureDeltaTime = 0; Time.timeScale = 1; candidateFailed = false;
            MainMenuController.RequestFirstLevel(); SceneManager.LoadScene("GameScene");
            yield return null; yield return null; yield return null;
            var builder = FindObjectOfType<PlacementSystem>(); builder.enabled = false;
            builder.worldCamera.GetComponent<GameController>().enabled = false;
            if (Application.isBatchMode)
                foreach (var camera in FindObjectsOfType<Camera>()) camera.enabled = false;
            var level = FactoryLevelSession.Active; var factory = FactoryProduction.Instance; scale = builder.AssistantScale;
            if (profiles.Exists(p => p.key == key)) { Note("CACHE candidate " + key); continue; }
            Note("BUILD candidate " + candidate + " lanes=" + config[0] + " punches=" + config[1] + " split=" + config[2]);
            var plan = builder.GenerateAssistantLayout(config[0], config[1], config[2] == 1, out var feed, out string error,
                config.Length > 4 ? config[4] : 2, config.Length > 5 ? config[5] : 0, config.Length > 6 ? config[6] : 34,
                config.Length > 7 ? config[7] : 0, config.Length > 8 ? config[8] : 24);
            yield return null;
            if (plan == null) { Note("SKIP geometry: " + error); continue; }
            float simulationStep = FactorySimulation.Step;
            var profile = new FactoryAssistantProfile { plan = plan, feed = feed, key = key, simulationStep = simulationStep };
            if (!builder.ApplyAssistantPlan(profile, out error)) { Note("SKIP placement: " + error); continue; }
            Note("RUN candidate " + candidate + " cost=" + builder.CurrentCost + " devices=" + builder.PlacedCount);
            // Observe the same simulation as the player. Do not manufacture a
            // render delta using captureDeltaTime. Keep the current desktop visible.
            bool lowFrameRate = RefreshMotionCatalog && key == "2-1-1-60-2-2-15-0-12-v3";
            Application.targetFrameRate = lowFrameRate ? 30 : -1;
            if (!Application.isBatchMode)
            {
                var camera = builder.worldCamera;
                Bounds view = factory.FinishedArea.Footprint;
                foreach (var station in factory.Stations) view.Encapsulate(FactoryMotion.Bounds(station.transform));
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(view.extents.z, view.extents.x / camera.aspect) * 1.25f;
                camera.transform.SetPositionAndRotation(view.center + Vector3.up * 100, Quaternion.Euler(90, 0, 0));
                camera.farClipPlane = 300;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            Time.timeScale = lowFrameRate ? 4 : 32;
            Note("PLAYBACK requested=" + Time.timeScale + " renderLimit=" + Application.targetFrameRate + " step=" + simulationStep);
            level.BeginProduction();
            var arrivals = new List<float>(); int last = 0;
            var punchTimes = new List<float>();
            var punches = factory.Stations.FindAll(s => s.Kind == 0);
            bool pulseOnly = config.Length > 5 && config[5] == 8;
            var serviced = new Dictionary<FactoryStation, HashSet<string>>();
            while (level.IsRunning && !candidateFailed)
            {
                if (punches.Count == 1 && punches[0].Completed > punchTimes.Count)
                {
                    while (punchTimes.Count < punches[0].Completed) punchTimes.Add(600 - level.RemainingSeconds);
                    Note("PUNCH " + key + " completed=" + punchTimes.Count + " time=" + punchTimes.Last().ToString("F2") + " output-buffer=" + punches[0].OutputCount);
                    if (pulseOnly)
                    {
                        // Measurement sink only: remove output to eliminate stack and pickup blocking.
                        // This never credits a finished product or enters the playable catalog.
                        var sample = punches[0].TakeOutput(); if (sample != null) { sample.gameObject.SetActive(false); Destroy(sample.gameObject); }
                    }
                    if (pulseOnly && punchTimes.Count >= 10) break;
                }
                foreach (var station in factory.Stations)
                    if (station.Kind <= 1 && station.Output != null)
                    {
                        var user = station.Kind == 0 ? station.ReservedBy : station.Output.Replay?.Holder;
                        if (user == null) continue;
                        if (!serviced.TryGetValue(station, out var users)) serviced[station] = users = new HashSet<string>();
                        users.Add(user.name);
                    }
                if (FactorySimulation.Active.ReceivedAt.Count > last)
                {
                    while (last < FactorySimulation.Active.ReceivedAt.Count) { arrivals.Add(FactorySimulation.Active.ReceivedAt[last]); last++; }
                    Note("ARRIVAL candidate " + candidate + " count=" + last + " time=" + arrivals.Last().ToString("F2"));
                }
                yield return null;
            }
            int stored = level.IsEnded ? level.ResultCount : factory.Stored;
            while (last < stored) { arrivals.Add(FactorySimulation.Active.ReceivedAt[last]); last++; }
            if (!level.IsEnded) level.Advance(level.RemainingSeconds);
            if (pulseOnly) { Note("PUNCH BASELINE output-count=" + punchTimes.Count + " times=" + string.Join(",", punchTimes.Select(x => x.ToString("F2")))); continue; }
            if (candidateFailed || arrivals.Count == 0) { Note("SKIP production failed or no receipts"); continue; }
            if (lowFrameRate && File.Exists("Logs/live-playback-1x.json"))
            {
                var reference = JsonUtility.FromJson<PlaybackReference>(File.ReadAllText("Logs/live-playback-1x.json"));
                if (reference.cost != plan.cost || reference.count != stored || !arrivals.SequenceEqual(reference.receivedAt))
                { Note("FAIL 30 FPS / 4x differs from desktop 1x receipts"); Finish(false); yield break; }
                Note("PASS 30 FPS / 4x: every receipt exactly matches desktop 1x");
            }
            profile.receivedAt = arrivals.ToArray(); profile.plan = builder.CapturePlan(plan.name, level);
            profile.punchCompletedAt = punchTimes.ToArray();
            profile.sharedUsage = serviced.Where(p => p.Value.Count > 1).Select(p => p.Key.name + "：" + string.Join("、", p.Value.OrderBy(x => x))).ToArray();
            foreach (string usage in profile.sharedUsage) Note("SHARED " + key + " " + usage);
            profiles.Add(profile);
            File.WriteAllText(cacheFile, JsonUtility.ToJson(new FactoryAssistantCatalog { scale = scale, profiles = profiles.ToArray() }, true));
            Note("VERIFIED candidate " + candidate + " count=" + arrivals.Count + " last=" + arrivals.Last().ToString("F2") + " cost=" + profile.plan.cost);
        }
        var catalog = new FactoryAssistantCatalog { scale = scale, profiles = profiles.ToArray() };
        var answer = FactoryAssistantSolver.Solve(10, 600, catalog, scale, 3, 3, 2);
        if (answer.best == null) { Note("FAIL no verified 10-product layout within 600 seconds plus margin"); Finish(false); yield break; }
        Note("PASS 10 products / 600 seconds: cost=" + answer.best.plan.cost + " completion including margin=" + answer.completion.ToString("F2"));
        bool parser = FactoryAssistantSolver.Parse("能否十分钟内得到十件产物，成本最低", out int count, out float seconds, out _) && count == 10 && seconds == 600;
        bool impossible = FactoryAssistantSolver.Solve(11, 600, catalog, scale, 3, 3, 2).provenImpossible &&
            FactoryAssistantSolver.Solve(1, 20, catalog, scale, 3, 3, 2).provenImpossible;
        bool minimum = !profiles.Any(p => p.receivedAt.Length >= 10 && p.receivedAt[9] + catalog.margin <= 600 && p.plan.cost < answer.best.plan.cost);
        if (!parser || !impossible || !minimum) { Note("FAIL parser, resource proof, or minimum-cost candidate selection"); Finish(false); yield break; }
        Directory.CreateDirectory("Assets/Resources/FactoryAssistant");
        File.WriteAllText("Assets/Resources/FactoryAssistant/VerifiedPlans.json", JsonUtility.ToJson(catalog, true));
        Note("PASS catalog exported with " + profiles.Count + " real-simulation profiles"); Finish(true);
    }
    void Finish(bool ok)
    {
        if (done) return; done = true; Time.captureDeltaTime = 0;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-assistant-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); else EditorApplication.isPlaying = false;
    }
    [Serializable] sealed class PlaybackReference { public long cost; public int count; public float[] receivedAt; }
}
#endif
