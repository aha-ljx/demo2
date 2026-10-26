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
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public sealed class FactoryLevelAuditRunner : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly StringBuilder report = new StringBuilder();
    bool done, failed;
    float began;
    void OnEnable() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error) { failed = true; report.AppendLine(message + "\n" + stack); } }
    void Update() { if (!done && (failed || Time.realtimeSinceStartup - began > 240)) Finish(false); }
    void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }

    IEnumerator Start()
    {
        yield return null; yield return null;
        var menu = FindObjectOfType<MainMenuController>();
        menu.StartGame(); menu.FirstLevelButton.onClick.Invoke();
        yield return null; yield return null; yield return null;
        var level = FactoryLevelSession.Active;
        var supply = FindObjectOfType<RawMaterialSupply>();
        var builder = FindObjectOfType<PlacementSystem>();
        var factory = FactoryProduction.Instance;
        var tutorial = FindObjectOfType<FactoryTutorial>();
        var playback = FindObjectOfType<FactoryPlaybackControl>();
        Check(level != null && supply.Remaining == 10 && tutorial.IsOpen, "actual menu entry creates first level with ten raw plates and tutorial");
        level.StartButton.onClick.Invoke();
        Check(!level.IsRunning && level.RemainingSeconds == 600, "tutorial blocks starting the level clock");
        tutorial.Close(); yield return null; yield return null; yield return null;
        Canvas.ForceUpdateCanvases();
        var helpRect = tutorial.HelpButton.GetComponent<RectTransform>();
        Vector2 helpPoint = RectTransformUtility.WorldToScreenPoint(null, helpRect.TransformPoint(helpRect.rect.center));
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = helpPoint }, hits);
        Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(helpRect), "tutorial button stays visible and clickable above the level header");
        builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        Place(builder, 0, new Vector3(-40, 0, 30));
        Place(builder, 4, new Vector3(-60, 0, 30));
        Check(builder.CurrentCost == 3050 && !level.IsRunning, "planning counts actual placed module cost without starting the timer");
        var punch = factory.Stations.Single(s => s.Kind == 0);
        Place(builder, 0, new Vector3(-80, 0, 30));
        var otherPunch = factory.Stations.Single(s => s.Kind == 0 && s != punch);
        int stock = supply.Remaining;
        var targetCenter = otherPunch.GetComponent<EquipmentGrid>().Footprint.center;
        var statistics = factory.Statistics;
        Check(punch.name == "冲床1" && otherPunch.name == "冲床2" && statistics.Modules.Count == 2 && statistics.Find(factory.Rails.Single().gameObject) == null,
            "placed machines receive independent serial names while rails have no naming or statistics entry");
        Check(builder.OpenEquipmentFromRay(new Ray(targetCenter + Vector3.up * 100, Vector3.down)) && statistics.IsDetailOpen,
            "clicking equipment opens its named statistics details");
        var details = level.HUD.GetComponentInParent<Canvas>().transform.Find("EquipmentStatisticsOverlay/EquipmentStatisticsCard");
        Check(details.Find("EquipmentName").GetComponent<TMP_Text>().text == "冲床2" && details.Find("CurrentState").GetComponent<TMP_Text>().text.Contains("规划中"),
            "equipment details show the selected module name and current planning state");
        details.Find("FeedPunch").GetComponent<Button>().onClick.Invoke();
        Check(supply.IsFeedDialogOpen && !statistics.IsDetailOpen && supply.FeedSlider.minValue == 0 && supply.FeedSlider.maxValue == 10,
            "punch details provide the targeted quantity selector without a prior stock click");
        Check(supply.FeedSlider.wholeNumbers && RawMaterialSupply.BlocksGameplay && Cursor.visible,
            "dialog uses whole plate quantities, shows the cursor and blocks world interaction");
        level.BeginProduction(); Check(!level.IsRunning, "feed dialog blocks starting the clock underneath it");
        otherPunch.Incoming = FactoryStation.RawInputCapacity - 3;
        yield return null;
        Check(supply.FeedSlider.maxValue == 3, "dialog upper bound follows the punch's remaining queue capacity");
        CaptureFeed(supply, level, "feed-dialog", 1920, 1080);
        CaptureFeed(supply, level, "feed-dialog-small", 1280, 720);
        CaptureFeed(supply, level, "feed-dialog-4x3", 1024, 768);
        Canvas.ForceUpdateCanvases();
        var sliderRect = supply.FeedSlider.GetComponent<RectTransform>();
        var dragEvent = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(null, sliderRect.TransformPoint(new Vector3(sliderRect.rect.width * .24f, 0, 0))) };
        supply.FeedSlider.OnPointerDown(dragEvent); supply.FeedSlider.OnDrag(dragEvent); supply.FeedSlider.OnPointerUp(dragEvent);
        Check(supply.FeedSlider.value == 2, "dragging the actual slider selects a whole quantity within the displayed range");
        supply.FeedSlider.value = 3;
        supply.CancelFeedButton.onClick.Invoke();
        Check(supply.Remaining == stock && otherPunch.Input.Count == 0, "cancel leaves stock and target queue unchanged");
        yield return null; yield return null; yield return null;
        supply.OpenFeedDialog(otherPunch);
        supply.ConfirmFeedButton.onClick.Invoke();
        Check(supply.Remaining == stock && otherPunch.Input.Count == 0 && !supply.IsFeedDialogOpen,
            "confirming zero closes the dialog without consuming stock");
        yield return null; yield return null; yield return null;
        supply.OpenFeedDialog(otherPunch); supply.FeedSlider.value = 3;
        otherPunch.Incoming = FactoryStation.RawInputCapacity;
        Check(supply.ConfirmFeed() == 0 && supply.Remaining == stock,
            "confirmation rechecks a now-full queue and never exceeds capacity");
        otherPunch.Incoming = 0;
        yield return null; yield return null; yield return null;
        supply.OpenFeedDialog(otherPunch); supply.FeedSlider.value = 2;
        supply.ConfirmFeedButton.onClick.Invoke();
        Check(supply.Remaining == stock - 2 && otherPunch.Input.Count == 2 && punch.Input.Count == 0,
            "confirmation feeds exactly the selected number exclusively to the selected punch");
        Check(supply.ConfirmFeed() == 0 && supply.Remaining == stock - 2 && otherPunch.Input.Count == 2,
            "repeated confirmation cannot duplicate a batch or consume more inventory");
        var returnedPlates = otherPunch.Input.ToArray();
        yield return null; yield return null; yield return null;
        supply.OpenFeedDialog(punch); supply.FeedSlider.value = 1; supply.ConfirmFeed();
        yield return null; yield return null; yield return null;
        Check(supply.Remaining == 7 && punch.Input.Count == 1 && otherPunch.Input.Count == 2,
            "two punches retain their separate planned batches before withdrawal");
        var withdrawnObject = otherPunch.gameObject;
        typeof(PlacementSystem).GetMethod("RemovePlaced", Private).Invoke(builder, new object[] { withdrawnObject });
        Check(supply.Remaining == 9 && otherPunch.Input.Count == 0 && punch.Input.Count == 1,
            "withdrawing a loaded punch refunds only its queued raw plates and preserves the other punch queue");
        Check(returnedPlates.All(part => part != null && part.Stage == 0 && part.transform.parent == supply.transform &&
            part.GetComponentsInChildren<Collider>().Any(c => c.enabled)),
            "the original refunded plates leave the machine, regain collisions and rejoin the stock pile");
        Check(statistics.Find(withdrawnObject).Removed, "withdrawn machine history is retained and marked as removed");
        typeof(PlacementSystem).GetMethod("RemovePlaced", Private).Invoke(builder, new object[] { withdrawnObject });
        Check(supply.Remaining == 9 && builder.CurrentCost == 3050,
            "repeated withdrawal cannot refund inventory or equipment cost twice");
        yield return null; yield return null; yield return null;
        Check(builder.CurrentCost == 3050 && !factory.Stations.Contains(otherPunch) &&
            returnedPlates.All(part => part != null && part.transform.parent == supply.transform && supply.StockBounds.Contains(FactoryWorkpiece.GeometryBounds(part.transform).center)),
            "refunded plates survive machine destruction and are visibly restacked inside the original pile");
        Check(supply.Remaining == 9 && punch.Input.Count == 1, "planning keeps the other punch's queued plate intact");
        for (int i = 0; i < 8; i++) yield return null;
        Check(level.RemainingSeconds == 600 && !punch.Busy && punch.Input.Count == 1 && punch.Completed == 0,
            "queued production and countdown remain stopped during planning");
        Capture(level, "planning", 1920, 1080);
        Capture(level, "planning-small", 1280, 720);
        Capture(level, "planning-4x3", 1024, 768);
        level.StartButton.onClick.Invoke();
        Check(level.IsRunning && !level.StartButton.interactable && FactoryLevelSession.BlocksInput,
            "start button begins the run once and quarantines its click from placement");
        var rail = factory.Rails.Single();
        typeof(PlacementSystem).GetMethod("RemovePlaced", Private).Invoke(builder, new object[] { rail.gameObject });
        typeof(PlacementSystem).GetMethod("RemovePlaced", Private).Invoke(builder, new object[] { punch.gameObject });
        Check(builder.CurrentCost == 3050 && factory.Rails.Contains(rail) && factory.Stations.Contains(punch),
            "running level rejects withdrawal of idle rails and placed machines without reducing cost");
        Place(builder, 4, new Vector3(-90, 0, 30));
        var addedRail = factory.Rails.Single(r => r != rail);
        typeof(PlacementSystem).GetMethod("RemovePlaced", Private).Invoke(builder, new object[] { addedRail.gameObject });
        Check(builder.CurrentCost == 3100 && factory.Rails.Contains(addedRail), "equipment added after start is also protected from withdrawal");
        for (int speed = 0; speed < 3; speed++)
        {
            if (speed > 0) playback.FastForwardButton.onClick.Invoke();
            yield return null; yield return null; yield return null;
            float previous = level.RemainingSeconds, scaled = 0, real = 0;
            for (int i = 0; i < 12; i++) { yield return null; scaled += Time.deltaTime; real += Time.unscaledDeltaTime; }
            Check(Mathf.Abs(previous - level.RemainingSeconds - scaled) < .01f,
                "countdown consumes the exact simulation seconds used by production at speed " + playback.Speed);
            Check(Mathf.Abs(scaled - real * playback.Speed) < .05f + scaled * .1f,
                "simulation and countdown follow real time multiplied by fast-forward " + playback.Speed);
        }
        float remaining = level.RemainingSeconds;
        level.StartButton.onClick.Invoke();
        Check(level.RemainingSeconds == remaining, "repeated start cannot reset or extend the clock");
        Check(punch.Busy || punch.Completed > 0, "the queued real plate begins processing only after production starts");
        tutorial.Open(); remaining = level.RemainingSeconds;
        for (int i = 0; i < 5; i++) yield return null;
        playback.CycleSpeed();
        Check(level.RemainingSeconds == remaining && Time.timeScale == 0, "tutorial pauses both countdown and production and cannot be overridden by fast-forward");
        tutorial.Close();
        float resumeDeadline = Time.realtimeSinceStartup + 5;
        while (level.RemainingSeconds == remaining && Time.realtimeSinceStartup < resumeDeadline) yield return null;
        Check(Time.timeScale == 4 && level.RemainingSeconds < remaining, "closing the tutorial restores the prior simulation speed and clock");
        Fill(factory.FinishedArea, 6);
        Check(statistics.ProductCount == 6 && statistics.ProductRate > 0, "final-area receipts update the product throughput statistics exactly once per product");
        level.Advance(level.RemainingSeconds - 59);
        Capture(level, "last-minute", 1920, 1080);
        Check(level.IsRunning && !level.IsEnded, "reaching six products does not settle early before the deadline");
        level.Advance(59);
        Check(level.IsEnded && !level.IsRunning && level.RemainingSeconds == 0 && Time.timeScale == 0,
            "600 simulation seconds end the run and freeze production");
        float frozenStatisticsTime = statistics.Elapsed;
        statistics.StatisticsButton.onClick.Invoke();
        Check(statistics.IsCurvesOpen, "production curves remain available from the result screen");
        yield return null; yield return null;
        Check(statistics.Elapsed == frozenStatisticsTime, "viewing end-of-run curves does not advance frozen statistics");
        statistics.CloseStatistics();
        Check(level.ResultCount == 6 && level.ResultGrade == "生产完成" && level.ResultRevenue == 12000 && level.ResultCost == 3100 && level.ResultNet == 8900,
            "settlement snapshots six products at 2000 each and deducts all retained equipment costs");
        playback.CycleSpeed(); tutorial.Open(); level.Advance(10);
        Check(Time.timeScale == 0 && !tutorial.IsOpen && level.ResultNet == 8900 && FactoryLevelSession.BlocksInput,
            "results block gameplay, tutorial and speed changes without duplicate settlement");
        var late = FactoryWorkpiece.Create(1, Vector3.zero); late.SetStage(3);
        Check(!factory.FinishedArea.BeginManualDrop(late), "after-deadline manual deposit cannot change the result"); Destroy(late.gameObject);
        Capture(level, "excellent", 1920, 1080);
        Capture(level, "excellent-small", 1280, 720);
        Capture(level, "excellent-4x3", 1024, 768);
        Check(FactoryLevelSession.CompareResults(6, -10000, 5, 100000) > 0 &&
            FactoryLevelSession.CompareResults(5, 100000, 6, -10000) < 0 &&
            FactoryLevelSession.CompareResults(6, 1000, 6, 900) > 0 &&
            FactoryLevelSession.CompareResults(6, 900, 6, 1000) < 0 &&
            FactoryLevelSession.CompareResults(6, 1000, 6, 1000) == 0,
            "result comparison always prefers quantity first and uses net earnings only for ties");
        level.Results.transform.Find("ResultCard/Retry").GetComponent<Button>().onClick.Invoke();
        yield return null; yield return null; yield return null;
        level = FactoryLevelSession.Active; supply = FindObjectOfType<RawMaterialSupply>(); builder = FindObjectOfType<PlacementSystem>();
        Check(level != null && !level.IsRunning && !level.IsEnded && level.RemainingSeconds == 600 && supply.Remaining == 10 && builder.CurrentCost == 0,
            "retry restores a fresh planning phase, ten plates, zero cost and full time");
        foreach (bool fixedStep in new[] { false, true })
        {
            builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
            factory = FactoryProduction.Instance;
            level.BeginProduction(); Fill(factory.FinishedArea, 9);
            if (fixedStep) level.SimulationAdvance(120 * 60); else level.Advance(120);
            Check(level.IsRunning && !level.IsEnded && level.RemainingSeconds == 480,
                "nine receipts keep the run active with unfinished work; fixed step=" + fixedStep);
            Fill(factory.FinishedArea, 1);
            if (fixedStep) level.SimulationAdvance(120 * 60 + 1); else level.Advance(FactorySimulation.Step);
            Check(level.IsEnded && !level.IsRunning && level.RemainingSeconds > 479 && Time.timeScale == 0 &&
                level.ResultCount == 10 && level.ResultRevenue == 20000 && level.ResultNet == 20000,
                "tenth receipt settles immediately before the deadline with correct totals; fixed step=" + fixedStep);
            var earlyResults = level.Results;
            float earlyRemaining = level.RemainingSeconds;
            level.Advance(600); level.SimulationAdvance(36000);
            Check(level.Results == earlyResults && level.RemainingSeconds == earlyRemaining && level.ResultCount == 10,
                "early settlement is frozen and cannot be repeated; fixed step=" + fixedStep);
            level.Retry(); yield return null; yield return null; yield return null;
            level = FactoryLevelSession.Active; builder = FindObjectOfType<PlacementSystem>();
        }
        builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        level.BeginProduction(); Fill(FactoryProduction.Instance.FinishedArea, 4); level.Advance(600);
        Check(level.ResultGrade == "生产完成" && level.ResultNet == 8000, "four products settle at 2000 each without an obsolete pass threshold");
        Capture(level, "pass", 1920, 1080);
        level.Retry(); yield return null; yield return null; yield return null;
        level = FactoryLevelSession.Active; builder = FindObjectOfType<PlacementSystem>();
        builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        Place(builder, 0, new Vector3(-40, 0, 30)); Place(builder, 4, new Vector3(-60, 0, 30));
        Place(builder, 0, new Vector3(-80, 0, 30));
        level.BeginProduction(); Fill(FactoryProduction.Instance.FinishedArea, 3);
        yield return null; yield return null; yield return null;
        supply = FindObjectOfType<RawMaterialSupply>();
        supply.OpenFeedDialog(FactoryProduction.Instance.Stations.First(s => s.Kind == 0));
        Check(supply.IsFeedDialogOpen, "targeted feeding is available during production");
        level.Advance(600);
        Check(!supply.IsFeedDialogOpen && Cursor.visible,
            "deadline closes an open feed dialog and leaves the result cursor visible");
        Check(level.ResultGrade == "生产完成" && level.ResultNet == -50, "three products also settle normally and negative net earnings are preserved");
        Capture(level, "fail", 1920, 1080);
        level.Results.transform.Find("ResultCard/ReturnMenu").GetComponent<Button>().onClick.Invoke();
        yield return null; yield return null;
        Check(SceneManager.GetActiveScene().name == "MainMenu" && FactoryLevelSession.Active == null && Time.timeScale == 1,
            "return to menu clears the level and restores time without a stale input lock");
        Finish(true);
    }
    void Place(PlacementSystem builder, int kind, Vector3 point)
    {
        typeof(PlacementSystem).GetMethod("SelectItem", Private).Invoke(builder, new object[] { kind });
        var preview = (GameObject)typeof(PlacementSystem).GetField("groundPreview", Private).GetValue(builder);
        preview.GetComponent<EquipmentGrid>().Snap(point);
        typeof(PlacementSystem).GetMethod("PlaceItem", Private).Invoke(builder, null);
        typeof(PlacementSystem).GetMethod("CancelSelection", Private).Invoke(builder, null);
    }
    void Fill(FinishedGoodsArea area, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int slot = area.Reserve(); var part = FactoryWorkpiece.Create(1, area.Target(slot)); part.SetStage(3); area.Commit(slot, part);
        }
    }
    void Inspect(GameObject root)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var label in root.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            Check(!label.isTextTruncated && !label.isTextOverflowing, "level text fits: " + label.name);
            foreach (var glyph in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
                if (!char.IsWhiteSpace(glyph.character) && (glyph.textElement == null || glyph.textElement.unicode != glyph.character))
                    throw new Exception("Missing level glyph: " + glyph.character);
        }
    }
    void CaptureFeed(RawMaterialSupply supply, FactoryLevelSession level, string name, int width, int height)
    {
        var overlay = (GameObject)typeof(RawMaterialSupply).GetField("feedOverlay", Private).GetValue(supply);
        var card = (RectTransform)typeof(RawMaterialSupply).GetField("feedCard", Private).GetValue(supply);
        var canvas = level.HUD.GetComponentInParent<Canvas>(); var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        var cameraObject = new GameObject("Feed capture camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = FactoryGuideUI.Background; camera.cullingMask = 1 << 5;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        Canvas.ForceUpdateCanvases(); FactoryGuideUI.Fit(card, canvas, new Vector2(620, 380), 32); Canvas.ForceUpdateCanvases();
        Inspect(overlay);
        var corners = new Vector3[4]; card.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            Vector3 point = camera.WorldToViewportPoint(corner);
            Check(point.x >= -.001f && point.x <= 1.001f && point.y >= -.001f && point.y <= 1.001f,
                "feed dialog stays within viewport at " + width + "x" + height);
        }
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs/level-one"); File.WriteAllBytes("Logs/level-one/" + name + ".png", picture.EncodeToPNG());
        RenderTexture.active = previous; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
        camera.targetTexture = null; DestroyImmediate(picture); DestroyImmediate(target); DestroyImmediate(cameraObject); Canvas.ForceUpdateCanvases();
    }
    void Capture(FactoryLevelSession level, string name, int width, int height)
    {
        typeof(FactoryLevelSession).GetMethod("Refresh", Private).Invoke(level, null);
        var canvas = level.HUD.GetComponentInParent<Canvas>(); var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        var cameraObject = new GameObject("Level capture camera"); var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = FactoryGuideUI.Background; camera.cullingMask = 1 << 5;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        Canvas.ForceUpdateCanvases();
        FactoryGuideUI.Fit(level.HUD, canvas, new Vector2(1840, 176), 32);
        if (level.IsEnded) FactoryGuideUI.Fit(level.ResultCard, canvas, new Vector2(1000, 760));
        Canvas.ForceUpdateCanvases(); Inspect(level.HUD.gameObject);
        if (level.IsEnded) Inspect(level.Results);
        var rect = level.IsEnded ? level.ResultCard : level.HUD; var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            Vector3 point = camera.WorldToViewportPoint(corner);
            Check(point.x >= -.001f && point.x <= 1.001f && point.y >= -.001f && point.y <= 1.001f, "level panel stays within viewport at " + width + "x" + height);
        }
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs/level-one"); File.WriteAllBytes("Logs/level-one/" + name + ".png", picture.EncodeToPNG());
        RenderTexture.active = previous; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
        camera.targetTexture = null; DestroyImmediate(picture); DestroyImmediate(target); DestroyImmediate(cameraObject); Canvas.ForceUpdateCanvases();
    }
    void Finish(bool ok)
    {
        if (done) return; done = true;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-level-one-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
#endif
