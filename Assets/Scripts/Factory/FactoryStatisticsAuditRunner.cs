#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class FactoryStatisticsAuditRunner : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly StringBuilder report = new StringBuilder();
    bool done, failed;
    float began;
    void OnEnable() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(message + "\n" + stack); } }
    void Update() { if (!done && (failed || Time.realtimeSinceStartup - began > 240)) Finish(false); }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
    GameObject Place(PlacementSystem builder, int kind, Vector3 position)
    {
        typeof(PlacementSystem).GetMethod("SelectItem", Private).Invoke(builder, new object[] { kind });
        var preview = (GameObject)typeof(PlacementSystem).GetField("groundPreview", Private).GetValue(builder);
        preview.GetComponent<EquipmentGrid>().Snap(position);
        typeof(PlacementSystem).GetMethod("PlaceItem", Private).Invoke(builder, null);
        typeof(PlacementSystem).GetMethod("CancelSelection", Private).Invoke(builder, null);
        return FactoryProduction.Instance.Stations.LastOrDefault(s => s.Kind == kind)?.gameObject;
    }
    IEnumerator Start()
    {
        yield return null; yield return null;
        var menu = FindObjectOfType<MainMenuController>(); menu.StartGame(); menu.FirstLevelButton.onClick.Invoke();
        yield return null; yield return null; yield return null;
        var guide = FindObjectOfType<FactoryTutorial>(); guide.Close();
        yield return null; yield return null; yield return null;
        var builder = FindObjectOfType<PlacementSystem>(); builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        var level = FactoryLevelSession.Active; var factory = FactoryProduction.Instance; var stats = factory.Statistics;
        var supply = FindObjectOfType<RawMaterialSupply>();
        var punch = Place(builder, 0, new Vector3(-40, 0, 30));
        var bend = Place(builder, 1, new Vector3(0, 0, -70));
        var weld = Place(builder, 2, new Vector3(40, 0, -70));
        Place(builder, 4, new Vector3(-60, 0, 30));
        var rail = factory.Rails.Single();
        var armRoot = FactoryProductionAuditRunner.Model(3, rail.GetComponent<EquipmentGrid>().Footprint.center, 0);
        FactoryArm.AlignToRail(armRoot.transform, rail, rail.GetComponent<EquipmentGrid>().Footprint.center);
        factory.Register(armRoot, 3); var arm = armRoot.GetComponent<FactoryArm>();
        Check(arm != null && punch.name == "冲床1" && bend.name == "折弯机1" && weld.name == "焊接机1" && arm.name == "机械臂1",
            "four equipment types receive their own sequential readable names");
        Check(stats.Modules.Count == 4 && stats.Find(rail.gameObject) == null, "rails are excluded from names and statistics");
        Place(builder, 0, new Vector3(-90, 0, -70));
        Check(stats.Modules.Last().Name == "冲床2", "same-type serials increase without affecting other equipment types");
        yield return null; yield return null;
        Check(stats.Elapsed == 0 && stats.Modules.All(m => m.Rate == 0), "planning records no production time or false throughput");
        Check(stats.OpenDetails(punch) && FactoryStatistics.BlocksGameplay, "equipment details open and isolate world input");
        Capture(stats, false, "punch-details", 1920, 1080);
        Capture(stats, false, "punch-details-small", 1280, 720);
        Capture(stats, false, "punch-details-4x3", 1024, 768);
        var detail = (GameObject)typeof(FactoryStatistics).GetField("detailOverlay", Private).GetValue(stats);
        detail.transform.Find("EquipmentStatisticsCard/FeedPunch").GetComponent<Button>().onClick.Invoke();
        Check(supply.IsFeedDialogOpen && !stats.IsDetailOpen, "punch details retain the targeted quantity feeding action");
        supply.FeedSlider.value = 1; supply.ConfirmFeed();
        yield return null; yield return null; yield return null;
        Check(stats.OpenDetails(armRoot), "mechanical arm supports the same equipment inspector");
        Capture(stats, false, "arm-details-small", 1280, 720);
        Check(!detail.transform.Find("EquipmentStatisticsCard/FeedPunch").gameObject.activeSelf &&
            detail.transform.Find("EquipmentStatisticsCard/CurrentRate").GetComponent<TMP_Text>().text.Contains(arm.RailSpeed.ToString("0.00")),
            "arm inspector shows configured movement speed and hides the punch feeding action");
        stats.CloseStatistics(); yield return null; yield return null; yield return null;
        level.BeginProduction(); Time.captureDeltaTime = .1f; Time.timeScale = 4;
        var punchStation = punch.GetComponent<FactoryStation>();
        while (punchStation.Completed == 0 && stats.Elapsed < 180) yield return null;
        Check(punchStation.Completed == 1 && stats.Find(punch).Completed == 1 && stats.Find(punch).Rate > 0,
            "a real animated punch completion emits exactly one module throughput event");
        Check(stats.Elapsed > 40 && stats.ProductCount == 0, "simulation time advances with playback while unfinished products do not count as received");
        typeof(FactoryArm).GetField("<Busy>k__BackingField", Private).SetValue(arm, true);
        Vector3 center = rail.GetComponent<EquipmentGrid>().Footprint.center;
        var path = new List<Vector3>();
        for (int i = 0; i < 6; i++) path.Add(center + Vector3.right * (i % 2 == 0 ? -1 : 1));
        var movement = (IEnumerator)typeof(FactoryArm).GetMethod("TravelPath", Private).Invoke(arm,
            new object[] { path, arm.TrackPosition.y, Quaternion.identity });
        yield return arm.StartCoroutine(movement);
        typeof(FactoryArm).GetField("<Busy>k__BackingField", Private).SetValue(arm, false);
        Check(stats.Find(armRoot).History.Any(p => p.y > 0) && stats.Find(armRoot).History.All(p => p.y <= arm.RailSpeed + .1f),
            "actual rail travel produces movement-rate samples in simulation units per second");
        var area = factory.FinishedArea; int slot = area.Reserve();
        var received = FactoryWorkpiece.Create(1, area.Target(slot)); received.SetStage(3); area.Commit(slot, received);
        Check(stats.ProductCount == 1 && stats.ProductRate > 0, "only an actual final-area commit increments received-product throughput");
        float until = stats.Elapsed + 3;
        while (stats.Elapsed < until) yield return null;
        Check(stats.Find(armRoot).MovementRate == 0 && stats.ProductHistory.Any(p => p.y > 0),
            "idle arms return to zero speed while received products appear in history");
        stats.StatisticsButton.onClick.Invoke();
        Check(stats.IsCurvesOpen && Time.timeScale == 4, "statistics button opens live curves without pausing simulation");
        Capture(stats, true, "curves", 1920, 1080);
        Capture(stats, true, "curves-small", 1280, 720);
        Capture(stats, true, "curves-4x3", 1024, 768);
        var curves = (GameObject)typeof(FactoryStatistics).GetField("curvesOverlay", Private).GetValue(stats);
        curves.transform.Find("ProductionCurvesCard/Next").GetComponent<Button>().onClick.Invoke();
        Check(curves.transform.Find("ProductionCurvesCard/ModuleTitle0").GetComponent<TMP_Text>().text.Contains("冲床2"),
            "curve pagination exposes modules beyond the first four without adding rails");
        Capture(stats, true, "curves-page-two", 1280, 720);
        stats.CloseStatistics();
        float expireAt = stats.Elapsed + 61;
        while (stats.Elapsed < expireAt) yield return null;
        Check(stats.Find(punch).Rate == 0 && stats.ProductRate == 0 && stats.Find(punch).History.Any(p => p.y > 0),
            "rolling throughput expires after sixty simulation seconds while historical curves survive");
        guide.Open(); float paused = stats.Elapsed; yield return null; yield return null; yield return null;
        Check(stats.Elapsed == paused, "tutorial pauses statistics on the same simulation clock as production"); guide.Close();
        Finish(true);
    }
    void Capture(FactoryStatistics stats, bool curves, string name, int width, int height)
    {
        var overlay = (GameObject)typeof(FactoryStatistics).GetField(curves ? "curvesOverlay" : "detailOverlay", Private).GetValue(stats);
        var card = (RectTransform)typeof(FactoryStatistics).GetField(curves ? "curvesCard" : "detailCard", Private).GetValue(stats);
        var canvas = overlay.GetComponentInParent<Canvas>(); var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        var root = new GameObject("Statistics capture camera"); var camera = root.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = FactoryGuideUI.Background; camera.cullingMask = 1 << 5;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        Canvas.ForceUpdateCanvases(); typeof(FactoryStatistics).GetMethod(curves ? "RefreshCurves" : "RefreshDetails", Private).Invoke(stats, null);
        Canvas.ForceUpdateCanvases();
        foreach (var chart in overlay.GetComponentsInChildren<FactoryRateChart>())
        {
            var mesh = chart.canvasRenderer.GetMesh();
            Check(mesh != null && mesh.vertexCount > 40, "chart renderer contains grid and measured curve geometry: " + chart.name);
            Check(Vector2.Distance(chart.rectTransform.rect.size, chart.rectTransform.sizeDelta) < .01f,
                "chart uses its fixed plot size rather than stretching into adjacent plots");
        }
        foreach (var label in overlay.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            Check(!label.isTextTruncated && !label.isTextOverflowing, "statistics text fits: " + label.name + " at " + width + "x" + height);
            foreach (var glyph in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
                if (!char.IsWhiteSpace(glyph.character) && (glyph.textElement == null || glyph.textElement.unicode != glyph.character))
                    throw new Exception("Missing statistics glyph: " + glyph.character);
        }
        var corners = new Vector3[4]; card.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            var point = camera.WorldToViewportPoint(corner);
            Check(point.x >= -.001f && point.x <= 1.001f && point.y >= -.001f && point.y <= 1.001f,
                "statistics panel stays in viewport at " + width + "x" + height);
        }
        camera.Render(); var oldTarget = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs/statistics"); File.WriteAllBytes("Logs/statistics/" + name + ".png", picture.EncodeToPNG());
        RenderTexture.active = oldTarget; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
        camera.targetTexture = null; DestroyImmediate(picture); DestroyImmediate(target); DestroyImmediate(root); Canvas.ForceUpdateCanvases();
    }
    void Finish(bool ok)
    {
        if (done) return; done = true; Time.captureDeltaTime = 0;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-statistics-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
#endif
