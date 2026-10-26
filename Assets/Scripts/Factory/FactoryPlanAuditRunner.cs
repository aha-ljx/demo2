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
using UnityEngine.EventSystems;

public sealed class FactoryPlanAuditRunner : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly StringBuilder report = new StringBuilder();
    bool done, failed;
    float began;
    void OnEnable() { began = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; FactoryPlanStorage.DirectoryOverride = null; }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(message + "\n" + stack); } }
    void Update() { if (!done && (failed || Time.realtimeSinceStartup - began > 240)) Finish(false); }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
    void VerifyPunchBoundary(PlacementSystem builder)
    {
        var layout = builder.GetComponent<FactoryLevelLayout>();
        var supply = FindObjectOfType<RawMaterialSupply>();
        var selectedField = typeof(PlacementSystem).GetField("selected", Private);
        var check = typeof(PlacementSystem).GetMethod("CanPlaceHere", Private);
        var create = typeof(PlacementSystem).GetMethod("CreateEquipment", Private);
        var types = (Dictionary<GameObject, int>)typeof(PlacementSystem).GetField("placedTypes", Private).GetValue(builder);
        int previous = (int)selectedField.GetValue(builder);
        try
        {
            foreach (int kind in new[] { 0, 1, 2, 4 })
            {
                var probe = (GameObject)create.Invoke(builder, new object[] { kind });
                try
                {
                    selectedField.SetValue(builder, kind);
                    var grid = probe.GetComponent<EquipmentGrid>();
                    for (int yaw = 0; yaw < 360; yaw += 90)
                    {
                        probe.transform.rotation = Quaternion.Euler(0, yaw, 0);
                        grid.Snap(new Vector3(layout.BoundaryX - 40, 0, supply.transform.position.z - 120));
                        Check((bool)check.Invoke(builder, new object[] { probe, null }), "far-side placement allowed for kind " + kind + ", yaw " + yaw);
                        float edge = layout.BoundaryX - FactoryLevelLayout.BoundaryWidth * .5f;
                        probe.transform.position += Vector3.right * (edge - grid.Footprint.max.x + .01f);
                        Check((bool)check.Invoke(builder, new object[] { probe, null }) == (kind != 0),
                            "whole punch footprint rejects a 1cm crossing beyond visible line ends; kind " + kind + ", yaw " + yaw);
                        if (kind == 0)
                        {
                            string reason = (string)typeof(PlacementSystem).GetField("placementBlockReason", Private).GetValue(builder);
                            Check(reason.Contains("无法摆放") && reason.Contains("黄线"), "crossing explains why the punch cannot be placed");
                            types.Add(probe, 0); selectedField.SetValue(builder, 2);
                            Check(!(bool)check.Invoke(builder, new object[] { probe, probe }), "moving/rotating an existing punch uses its own kind");
                            types.Remove(probe); selectedField.SetValue(builder, 0);
                        }
                        probe.transform.position += Vector3.right * 80;
                        Check((bool)check.Invoke(builder, new object[] { probe, null }) == (kind != 0), "finished-area-side placement checked for kind " + kind);
                    }
                    if (kind == 0)
                    {
                        var plan = new FactorySavedPlan { name = "越线方案验证", equipmentScale = builder.AssistantScale,
                            devices = new[] { new FactoryPlanDevice { kind = 0, name = "冲床9", rotation = probe.transform.rotation,
                                offset = probe.transform.position - supply.transform.position } } };
                        Check(!builder.TryLoadPlan(plan, out string error) && error.Contains("黄线") && builder.PlacedCount == 0 &&
                            builder.CurrentCost == 0 && FactoryProduction.Instance.Stations.Count == 0 && FactoryProduction.Instance.Statistics.Modules.Count == 0,
                            "saved plan with a forbidden punch is rejected without partial geometry or cost");
                    }
                }
                finally { types.Remove(probe); probe.SetActive(false); Destroy(probe); }
            }
        }
        finally { selectedField.SetValue(builder, previous); }
    }
    GameObject Place(PlacementSystem builder, int kind, Vector3 position, float yaw = 0)
    {
        typeof(PlacementSystem).GetMethod("SelectItem", Private).Invoke(builder, new object[] { kind });
        var preview = (GameObject)typeof(PlacementSystem).GetField("groundPreview", Private).GetValue(builder);
        preview.transform.rotation = Quaternion.Euler(0, yaw, 0); preview.GetComponent<EquipmentGrid>().Snap(position);
        if (kind == 3)
        {
            var factory = FactoryProduction.Instance;
            Check(factory.FindRailAt(position, null, out var rail), "arm fixture has a supporting empty rail");
            FactoryArm.AlignToRail(preview.transform, rail, position);
        }
        // Reproduce placing directly over the character, including low rails.
        var body = builder.playerCollider;
        Bounds footprint = preview.GetComponent<EquipmentGrid>().Footprint;
        body.enabled = false;
        body.transform.position = footprint.center + Vector3.up * (body.height * .5f - body.center.y);
        body.enabled = true; Physics.SyncTransforms();
        Vector3 before = body.transform.position;
        Check((bool)typeof(PlacementSystem).GetMethod("CanPlaceHere", Private).Invoke(builder, new object[] { preview, null }),
            "saved-layout fixture uses a legal placement for kind " + kind);
        var placed = (List<GameObject>)typeof(PlacementSystem).GetField("placed", Private).GetValue(builder);
        typeof(PlacementSystem).GetMethod("PlaceItem", Private).Invoke(builder, null);
        Check(body.transform.position.y > before.y && Mathf.Abs(body.transform.position.x - before.x) < .001f &&
            Mathf.Abs(body.transform.position.z - before.z) < .001f && body.enabled && body.detectCollisions,
            "placing kind " + kind + " over the player lifts it vertically and keeps collision enabled");
        var item = placed.Last(); typeof(PlacementSystem).GetMethod("CancelSelection", Private).Invoke(builder, null); return item;
    }
    IEnumerator Start()
    {
        FactoryPlanStorage.DirectoryOverride = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/plan-save-test-" + Guid.NewGuid().ToString("N")));
        yield return null; yield return null;
        var menu = FindObjectOfType<MainMenuController>(); menu.StartGame(); menu.FirstLevelButton.onClick.Invoke();
        yield return null; yield return null; yield return null;
        FindObjectOfType<FactoryTutorial>().Close(); yield return null; yield return null; yield return null;
        var builder = FindObjectOfType<PlacementSystem>(); builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        var level = FactoryLevelSession.Active; var factory = FactoryProduction.Instance; var supply = FindObjectOfType<RawMaterialSupply>();
        VerifyPunchBoundary(builder);
        builder.Plans.OpenList();
        Check(builder.Plans.IsListOpen && FactoryPlanLibrary.BlocksGameplay, "planning offers a modal local plan library even before any plans exist");
        level.BeginProduction(); Check(!level.IsRunning, "plan library blocks starting production through its overlay");
        builder.Plans.Close(); yield return null; yield return null; yield return null;
        var removed = Place(builder, 0, new Vector3(-130, 0, 30));
        var punch = Place(builder, 0, new Vector3(-80, 0, 30), 90);
        typeof(PlacementSystem).GetMethod("RemovePlaced", Private).Invoke(builder, new object[] { removed });
        yield return null;
        Place(builder, 1, new Vector3(-40, 0, -60), 180);
        Place(builder, 2, new Vector3(40, 0, -60), 270);
        var railRoot = Place(builder, 4, new Vector3(90, 0, 90));
        var railBounds = railRoot.GetComponent<EquipmentGrid>().Footprint;
        Place(builder, 4, railBounds.center + Vector3.right * railBounds.size.x);
        var armRoot = Place(builder, 3, railBounds.center); var arm = armRoot.GetComponent<FactoryArm>();
        Vector3 originalArm = arm.PlacementPosition;
        Check(factory.Graph.Same(factory.Rails[0], factory.Rails[1]) && punch.name == "冲床2", "fixture includes connected rails and a non-reused machine serial");
        supply.OpenFeedDialog(punch.GetComponent<FactoryStation>()); supply.FeedSlider.value = 2; supply.ConfirmFeed();
        yield return null; yield return null; yield return null;
        level.BeginProduction();
        for (int i = 0; i < 2; i++)
        {
            int slot = factory.FinishedArea.Reserve(); var part = FactoryWorkpiece.Create(1, factory.FinishedArea.Target(slot));
            part.SetStage(3); factory.FinishedArea.Commit(slot, part);
        }
        armRoot.transform.position += Vector3.forward;
        level.Advance(600);
        Check(level.IsEnded && level.ResultCount == 2 && level.ResultCard.Find("SavePlan") != null, "result screen exposes saving the finished run's layout");
        Capture(level.Results, level.ResultCard, new Vector2(1000, 760), "result-save", 1920, 1080);
        Capture(level.Results, level.ResultCard, new Vector2(1000, 760), "result-save-small", 1280, 720);
        Capture(level.Results, level.ResultCard, new Vector2(1000, 760), "result-save-4x3", 1024, 768);
        level.ResultCard.Find("SavePlan").GetComponent<Button>().onClick.Invoke();
        Check(builder.Plans.IsSaveOpen && Cursor.visible, "result save button opens a named save dialog");
        var saveOverlay = (GameObject)typeof(FactoryPlanLibrary).GetField("saveOverlay", Private).GetValue(builder.Plans);
        var saveCard = (RectTransform)typeof(FactoryPlanLibrary).GetField("saveCard", Private).GetValue(builder.Plans);
        builder.Plans.NameInput.text = " "; builder.Plans.ConfirmSaveButton.onClick.Invoke();
        Check(FactoryPlanStorage.ReadAll(out _).Count == 0, "blank names cannot create an incomplete save");
        builder.Plans.NameInput.text = "双轨产线方案";
        Capture(saveOverlay, saveCard, new Vector2(820, 520), "save-dialog", 1920, 1080);
        Capture(saveOverlay, saveCard, new Vector2(820, 520), "save-dialog-small", 1280, 720);
        Capture(saveOverlay, saveCard, new Vector2(820, 520), "save-dialog-4x3", 1024, 768);
        builder.Plans.ConfirmSaveButton.onClick.Invoke(); builder.Plans.ConfirmSaveButton.onClick.Invoke();
        var saved = FactoryPlanStorage.ReadAll(out _);
        Check(saved.Count == 1 && saved[0].name == "双轨产线方案" && saved[0].devices.Length == 6 && saved[0].resultCount == 2 && saved[0].cost == builder.CurrentCost,
            "named layout is persisted once with all five equipment types and run metadata");
        var plan = saved[0]; var armRecord = plan.devices.Single(d => d.kind == 3);
        Check(Vector3.Distance(armRecord.offset + supply.transform.position, originalArm) < .001f &&
            Vector3.Distance(armRecord.offset + supply.transform.position, armRoot.transform.position) > .9f,
            "saving captures the arm's placed home rather than its in-flight end-of-run position");
        Check(File.Exists(Path.Combine(FactoryPlanStorage.DirectoryPath, plan.id + ".json")), "plan exists as a durable local file");
        for (int i = 0; i < 5; i++)
        {
            var extra = JsonUtility.FromJson<FactorySavedPlan>(JsonUtility.ToJson(plan)); extra.name = "方案对比 " + (i + 1);
            Check(FactoryPlanStorage.Save(extra, out _), "additional plan can be stored without overwriting previous plans");
        }
        File.WriteAllText(Path.Combine(FactoryPlanStorage.DirectoryPath, "broken.json"), "{invalid");
        Check(FactoryPlanStorage.ReadAll(out string skipped).Count == 6 && !string.IsNullOrEmpty(skipped), "corrupt files are skipped without losing valid saves");
        builder.Plans.Close(); level.Retry(); yield return null; yield return null; yield return null;
        builder = FindObjectOfType<PlacementSystem>(); builder.enabled = false; builder.worldCamera.GetComponent<GameController>().enabled = false;
        level = FactoryLevelSession.Active; factory = FactoryProduction.Instance; supply = FindObjectOfType<RawMaterialSupply>();
        Check(builder.PlacedCount == 0 && builder.CurrentCost == 0 && supply.Remaining == 10 && FactoryPlanStorage.ReadAll(out _).Count == 6,
            "new planning session is blank with fresh stock while saved plans survive scene reload");
        var collision = JsonUtility.FromJson<FactorySavedPlan>(JsonUtility.ToJson(plan));
        var duplicate = JsonUtility.FromJson<FactoryPlanDevice>(JsonUtility.ToJson(collision.devices.First(d => d.kind == 0)));
        duplicate.name = "冲床999"; collision.devices = collision.devices.Concat(new[] { duplicate }).ToArray();
        var playerBody = builder.playerCollider;
        var planProbe = (GameObject)typeof(PlacementSystem).GetMethod("CreateEquipment", Private).Invoke(builder, new object[] { duplicate.kind });
        planProbe.transform.SetPositionAndRotation(supply.transform.position + duplicate.offset, duplicate.rotation);
        Vector3 planCenter = planProbe.GetComponent<EquipmentGrid>().Footprint.center;
        planProbe.SetActive(false); Destroy(planProbe);
        playerBody.enabled = false;
        playerBody.transform.position = planCenter + Vector3.up *
            (playerBody.height * .5f - playerBody.center.y);
        playerBody.enabled = true; Physics.SyncTransforms();
        Vector3 beforePlan = playerBody.transform.position;
        Check(!builder.TryLoadPlan(collision, out _) && builder.PlacedCount == 0 && builder.CurrentCost == 0 && factory.Stations.Count == 0 &&
            factory.Rails.Count == 0 && factory.Arms.Count == 0 && factory.Statistics.Modules.Count == 0 && supply.Remaining == 10,
            "overlapping saved geometry is rejected atomically without partial equipment, cost or statistics");
        Check(playerBody.transform.position == beforePlan, "rejected plans do not lift the player");
        var incompatible = JsonUtility.FromJson<FactorySavedPlan>(JsonUtility.ToJson(plan)); incompatible.version = 999;
        Check(!builder.TryLoadPlan(incompatible, out _) && builder.PlacedCount == 0, "unsupported save versions leave the blank scene unchanged");
        yield return null; yield return null;
        builder.Plans.PlanningButton.onClick.Invoke();
        var listOverlay = (GameObject)typeof(FactoryPlanLibrary).GetField("listOverlay", Private).GetValue(builder.Plans);
        var listCard = (RectTransform)typeof(FactoryPlanLibrary).GetField("listCard", Private).GetValue(builder.Plans);
        Check(builder.Plans.IsListOpen, "new scene reopens the persistent plan list through its planning button");
        Capture(listOverlay, listCard, new Vector2(960, 800), "plan-list", 1920, 1080);
        Capture(listOverlay, listCard, new Vector2(960, 800), "plan-list-small", 1280, 720);
        Capture(listOverlay, listCard, new Vector2(960, 800), "plan-list-4x3", 1024, 768);
        listCard.Find("Next").GetComponent<Button>().onClick.Invoke();
        Check(listCard.Find("Page").GetComponent<TMP_Text>().text.Contains("2 / 2"), "saved plans paginate beyond four entries");
        var delete = listCard.Find("PlanRow0/DeletePlan").GetComponent<Button>();
        var deleteTarget = FactoryPlanStorage.ReadAll(out _)[4];
        Check(deleteTarget.name != "双轨产线方案", "delete fixture targets an independent comparison plan");
        Canvas.ForceUpdateCanvases();
        var deleteRect = delete.GetComponent<RectTransform>();
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(null, deleteRect.TransformPoint(deleteRect.rect.center)) };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(delete.transform), "delete button receives pointer hits independently of the load row");
        ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(FactoryPlanStorage.ReadAll(out _).Count == 5 && !File.Exists(deleteTarget.sourceFile) && builder.PlacedCount == 0 && builder.Plans.IsListOpen,
            "clicking delete removes only the selected local save and never loads its layout");
        delete.onClick.Invoke();
        Check(FactoryPlanStorage.ReadAll(out _).Count == 5, "double clicking delete cannot erase the next plan when rows shift");
        bool clicked = false;
        for (int i = 0; i < 4; i++)
        {
            var row = listCard.Find("PlanRow" + i);
            if (row.gameObject.activeSelf && row.Find("PlanName").GetComponent<TMP_Text>().text == "双轨产线方案")
            { row.GetComponent<Button>().onClick.Invoke(); clicked = true; break; }
        }
        Check(clicked && !builder.Plans.IsListOpen && builder.PlacedCount == 6 && builder.CurrentCost == plan.cost,
            "clicking the saved plan restores the full layout and recalculates its equipment cost");
        Check(playerBody.transform.position.y > beforePlan.y && Mathf.Abs(playerBody.transform.position.x - beforePlan.x) < .001f &&
            Mathf.Abs(playerBody.transform.position.z - beforePlan.z) < .001f && playerBody.enabled && playerBody.detectCollisions,
            "loading a valid plan over the player lifts it vertically after the full plan commits");
        Check(factory.Stations.Count == 3 && factory.Rails.Count == 2 && factory.Arms.Count == 1 && factory.Graph.Same(factory.Rails[0], factory.Rails[1]),
            "restore builds rails before arms and reconstructs rail connectivity");
        var restored = (List<GameObject>)typeof(PlacementSystem).GetField("placed", Private).GetValue(builder);
        var kinds = (Dictionary<GameObject, int>)typeof(PlacementSystem).GetField("placedTypes", Private).GetValue(builder);
        foreach (var device in plan.devices)
            Check(restored.Any(item => kinds[item] == device.kind && Vector3.Distance(item.transform.position, supply.transform.position + device.offset) < .01f &&
                Quaternion.Angle(item.transform.rotation, device.rotation) < .01f && (device.kind == 4 || item.name == device.name)),
                "saved position, orientation and module name restore for kind " + device.kind);
        Check(supply.Remaining == 10 && factory.Stored == 0 && factory.Statistics.ProductCount == 0 && factory.Statistics.Modules.All(m => m.Completed == 0) &&
            factory.Stations.All(s => s.Input.Count == 0 && s.Output == null) && !level.IsRunning && level.RemainingSeconds == 600,
            "loading a layout does not import old materials, production state, telemetry or elapsed time");
        Check(!builder.TryLoadPlan(plan, out _) && builder.PlacedCount == 6 && builder.CurrentCost == plan.cost,
            "loading into a nonempty scene cannot duplicate equipment or overwrite a live layout");
        yield return null; yield return null; yield return null;
        builder.Plans.OpenList();
        Check(builder.Plans.IsListOpen && listCard.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("PlanRow")).All(b => !b.interactable),
            "nonempty scenes can inspect saved plans but their load actions are disabled");
        yield return new WaitForSecondsRealtime(.45f);
        Check(listCard.Find("PlanRow0/DeletePlan").GetComponent<Button>().interactable, "deleting saves remains available when loading is disabled in a nonempty scene");
        listCard.Find("Next").GetComponent<Button>().onClick.Invoke();
        listCard.Find("PlanRow0/DeletePlan").GetComponent<Button>().onClick.Invoke();
        Check(FactoryPlanStorage.ReadAll(out _).Count == 4 && listCard.Find("Page").GetComponent<TMP_Text>().text == "1 / 1 页" &&
            builder.PlacedCount == 6 && builder.CurrentCost == plan.cost,
            "deleting the final entry on a page returns to the previous page and preserves the loaded factory");
        while (FactoryPlanStorage.ReadAll(out _).Count > 0)
        {
            yield return new WaitForSecondsRealtime(.45f);
            listCard.Find("PlanRow0/DeletePlan").GetComponent<Button>().onClick.Invoke();
        }
        Check(!listCard.Find("PlanRow0").gameObject.activeSelf && listCard.Find("Message").GetComponent<TMP_Text>().text.Contains("暂无") && builder.PlacedCount == 6,
            "deleting the last saved plan displays the empty library without removing scene equipment");
        Capture(listOverlay, listCard, new Vector2(960, 800), "plan-list-empty", 1280, 720);
        builder.Plans.Close(); yield return null; yield return null; yield return null;
        var nextPunch = Place(builder, 0, new Vector3(-160, 0, 30));
        Check(nextPunch.name == "冲床3", "new placements continue numbering after restored saved names");
        var loadedPunch = factory.Stations.Single(s => s.name == "冲床2");
        supply.OpenFeedDialog(loadedPunch); supply.FeedSlider.value = 1; supply.ConfirmFeed();
        yield return null; yield return null; yield return null;
        level.BeginProduction(); Time.captureDeltaTime = .1f; Time.timeScale = 4;
        while (loadedPunch.Completed == 0 && factory.Statistics.Elapsed < 180) yield return null;
        Check(loadedPunch.Completed == 1 && loadedPunch.OutputCount == 1 && factory.Statistics.Find(loadedPunch.gameObject).Completed == 1,
            "restored machine accepts fresh input and completes real animated production with fresh statistics");
        builder.Plans.OpenList();
        Check(!builder.Plans.IsListOpen && !builder.TryLoadPlan(plan, out _), "production phase rejects loading saved layouts");
        Finish(true);
    }
    void Capture(GameObject overlay, RectTransform card, Vector2 preferred, string name, int width, int height)
    {
        var currentLevel = FactoryLevelSession.Active;
        if (currentLevel != null) typeof(FactoryLevelSession).GetMethod("Refresh", Private).Invoke(currentLevel, null);
        var library = FindObjectOfType<FactoryPlanLibrary>();
        if (library != null) typeof(FactoryPlanLibrary).GetMethod("Update", Private).Invoke(library, null);
        var canvas = overlay.GetComponentInParent<Canvas>(); var mode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        var root = new GameObject("Plan capture camera"); var camera = root.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = FactoryGuideUI.Background; camera.cullingMask = 1 << 5;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        Canvas.ForceUpdateCanvases(); FactoryGuideUI.Fit(card, canvas, preferred, 32); Canvas.ForceUpdateCanvases();
        foreach (var label in overlay.GetComponentsInChildren<TMP_Text>())
        {
            if (label.GetComponentInParent<TMP_InputField>() != null && label.text.Length == 0) continue;
            label.ForceMeshUpdate();
            Check(!label.isTextTruncated && !label.isTextOverflowing, "plan UI text fits: " + label.name + " at " + width + "x" + height + " / " + label.text);
            foreach (var glyph in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
                if (!char.IsWhiteSpace(glyph.character) && (glyph.textElement == null || glyph.textElement.unicode != glyph.character))
                    throw new Exception("Missing plan glyph: " + glyph.character);
        }
        var corners = new Vector3[4]; card.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            var point = camera.WorldToViewportPoint(corner);
            Check(point.x >= -.001f && point.x <= 1.001f && point.y >= -.001f && point.y <= 1.001f, "plan panel remains in viewport at " + width + "x" + height);
        }
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs/plans"); File.WriteAllBytes("Logs/plans/" + name + ".png", picture.EncodeToPNG());
        RenderTexture.active = previous; canvas.renderMode = mode; canvas.worldCamera = oldCamera;
        camera.targetTexture = null; DestroyImmediate(picture); DestroyImmediate(target); DestroyImmediate(root); Canvas.ForceUpdateCanvases();
    }
    void Finish(bool ok)
    {
        if (done) return; done = true; Time.captureDeltaTime = 0;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-plan-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        FactoryPlanStorage.DirectoryOverride = null;
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
#endif
