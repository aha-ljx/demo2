#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class FactoryWorkflowInputAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    FactoryProduction system;
    PlacementSystem placement;
    RawMaterialSupply supply;
    bool failed, finished, finishedPickupAudit;
    float started;
    void OnEnable() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(message + "\n" + stack); }
    }
    void Update() { if (!finished && (failed || Time.realtimeSinceStartup - started > 600)) Finish(false); }
    void Check(bool ok, string text)
    {
        if (!ok) throw new Exception(text); report.AppendLine("PASS " + text);
        if (text.StartsWith("automatic real delivery")) Debug.Log(text);
    }
    static Ray Down(Vector3 p) => new Ray(p + Vector3.up * 100, Vector3.down);
    void ClickStock()
    {
        Vector3 point = supply.transform.position;
        Check(placement.HandleSupplyPointer(Down(point), point, true, true, true, false, false), "stock press consumes the pointer event");
        Check(placement.HandleSupplyPointer(Down(point), point, true, false, false, true, false), "stock release consumes the pointer event");
        Check(supply.Held == null && !supply.IsFeedDialogOpen, "stock click only shows inventory; feeding starts from the punch");
        supply.TryPickUpStock(); // Explicit legacy single-plate setup for boundary and floor-drop checks.
    }
    void DragStock(Vector3 destination)
    {
        Vector3 start = supply.transform.position;
        placement.HandleSupplyPointer(Down(start), start, true, true, true, false, false);
        placement.HandleSupplyPointer(Down(destination), destination, true, false, true, false, false);
        placement.HandleSupplyPointer(Down(destination), destination, true, false, false, true, false);
    }
    IEnumerator Start()
    {
        Time.captureDeltaTime = 1f / 60;
        yield return null; yield return null;
        placement = FindObjectOfType<PlacementSystem>(); placement.enabled = false;
        placement.worldCamera.GetComponent<GameController>().enabled = false;
        system = FactoryProduction.Instance; system.enabled = false;
        system.armTravelMultiplier = 12; system.armPickupMultiplier = 12; system.armRotationMultiplier = 8;
        supply = FindObjectOfType<RawMaterialSupply>();
        VerifyFixedLayout();
        var placed = (List<GameObject>)typeof(PlacementSystem).GetField("placed", Private).GetValue(placement);
        var kinds = (Dictionary<GameObject, int>)typeof(PlacementSystem).GetField("placedTypes", Private).GetValue(placement);
        placement.player.position = new Vector3(-50, 2, -50); Physics.SyncTransforms();
        int count = supply.Remaining;
        Vector3 destination = supply.transform.position;
        DragStock(destination + new Vector3(12, 0, 12));
        Check(Vector3.Distance(supply.transform.position, destination) < .005f && supply.Held == null && supply.Remaining == count,
            "fixed stock rejects bird dragging without picking a plate or changing inventory");
        Check(!supply.TryPickUp(), "ordinary F pickup still requires the player to be nearby");
        ClickStock(); var identity = supply.Held;
        Check(identity != null && supply.Remaining == count - 1, "remote bird click picks exactly one existing plate");
        Check(!supply.TryMove(destination + Vector3.right * 4, placed, system.FinishedArea), "held stock cannot be moved during pickup");
        Check(supply.CancelPickUp() && supply.Held == null && supply.Remaining == count && identity.transform.parent == supply.transform,
            "cancel returns the exact plate and restores stock inventory");
        Check(!supply.CancelPickUp() && supply.Remaining == count, "repeated cancel cannot duplicate a plate");

        // Releasing away from the pile must not trigger an accidental pickup.
        placement.HandleSupplyPointer(Down(destination), destination, true, true, true, false, false);
        placement.HandleSupplyPointer(Down(Vector3.zero), Vector3.zero, false, false, false, true, false);
        Check(supply.Held == null && supply.Remaining == count, "release over the interface does not pick a plate");

        // The fixed pile and loose floor plates both remain in place after a drag attempt.
        ClickStock(); var loosePlate = supply.Held;
        var layout = placement.GetComponent<FactoryLevelLayout>();
        float plateWidth = loosePlate.Width;
        Vector3 boundaryPoint = new Vector3(layout.BoundaryX + 1, 0, supply.transform.position.z + 5);
        placement.player.position = boundaryPoint + new Vector3(-2, 1, 0); Physics.SyncTransforms();
        Check(!supply.TryDrop(boundaryPoint, placed) && supply.Held == loosePlate && supply.Remaining == count - 1,
            "raw ground placement across the yellow line is rejected without losing the held plate or inventory");
        boundaryPoint.x = layout.BoundaryX - plateWidth * .25f;
        placement.player.position = boundaryPoint + new Vector3(-2, 1, 0); Physics.SyncTransforms();
        Check(!supply.TryDrop(boundaryPoint, placed) && supply.Held == loosePlate,
            "raw plate centered outside cannot straddle the yellow line");
        boundaryPoint.x = layout.BoundaryX - plateWidth * .5f;
        placement.player.position = boundaryPoint + new Vector3(-2, 1, 0); Physics.SyncTransforms();
        Check(!supply.TryDrop(boundaryPoint, placed) && supply.Held == loosePlate,
            "raw plate cannot overlap the painted width of the yellow line");
        boundaryPoint = new Vector3(layout.BoundaryX + 1, 0, layout.Boundary.GetPosition(1).z + 5);
        placement.player.position = boundaryPoint + new Vector3(-2, 1, 0); Physics.SyncTransforms();
        Check(!supply.TryDrop(boundaryPoint, placed) && supply.Held == loosePlate,
            "raw ground placement cannot bypass the boundary around a visible line endpoint");
        Check(supply.CancelPickUp() && supply.Remaining == count && loosePlate.transform.parent == supply.transform,
            "cancel after rejected ground placements returns the original plate and restores stock");
        ClickStock(); loosePlate = supply.Held;
        Vector3 loosePosition = new Vector3(layout.BoundaryX - plateWidth * .5f - FactoryLevelLayout.BoundaryWidth * .5f - .1f,
            0, supply.transform.position.z + 5);
        placement.player.position = loosePosition + new Vector3(-2, 1, 0); Physics.SyncTransforms();
        Check(supply.TryDrop(loosePosition, placed), "ordinary ground placement still accepts a picked raw plate");
        Check(FactoryWorkpiece.GeometryBounds(loosePlate.transform).max.x < layout.BoundaryX - FactoryLevelLayout.BoundaryWidth * .5f,
            "successful raw placement keeps the entire visible plate outside the yellow line");
        Check(!supply.TryMove(supply.transform.position + Vector3.right * 3, placed, system.FinishedArea), "fixed stock cannot move even with a loose plate nearby");
        Check(Vector3.Distance(loosePlate.transform.position, loosePosition) < .005f, "dragging stock does not move loose floor plates");
        placement.player.position = loosePosition + new Vector3(-2, 1.25f, 0); Physics.SyncTransforms();
        int looseCount = supply.Remaining;
        Check(supply.TryPickUp() && supply.Held == loosePlate, "F can pick the loose floor plate");
        Check(supply.CancelPickUp() && supply.Remaining == looseCount && Vector3.Distance(loosePlate.transform.position, loosePosition) < .005f,
            "canceling a loose pickup restores its position without creating stock");
        placement.player.position = new Vector3(-50, 2, -50); Physics.SyncTransforms();

        var punchObject = FactoryProductionAuditRunner.Model(0, new Vector3(100, 0, 100), 90);
        system.Register(punchObject, 0); placed.Add(punchObject); kinds[punchObject] = 0;
        var punch = punchObject.GetComponent<FactoryStation>(); punch.enabled = false;
        Vector3 originalStock = supply.transform.position;
        DragStock(punchObject.GetComponent<EquipmentGrid>().Footprint.center);
        Check(Vector3.Distance(originalStock, supply.transform.position) < .005f && supply.Held == null,
            "invalid dragging is blocked by equipment and does not become a pickup");
        Check(!supply.TryMove(system.FinishedArea.transform.position, placed, system.FinishedArea), "stock cannot occupy the final storage area");
        ClickStock(); identity = supply.Held;
        Ray punchClick = Down(punchObject.GetComponent<EquipmentGrid>().Footprint.center);
        for (int turn = 0; turn < 4; turn++)
        {
            punchObject.transform.rotation = Quaternion.Euler(0, turn * 90, 0);
            punchObject.transform.position += Vector3.right * (layout.BoundaryX + 2 - punch.Ports.InputPort.position.x);
            Physics.SyncTransforms();
            punchClick = Down(punchObject.GetComponent<EquipmentGrid>().Footprint.center);
            Check(placement.FeedHeldFromRay(punchClick, true) && supply.Held == identity && punch.Input.Count == 0 && supply.Remaining == count - 2,
                "bird click cannot feed raw across the yellow line at punch rotation " + turn * 90);
            placement.player.position = punch.Ports.InputPort.position + Vector3.up;
            Physics.SyncTransforms();
            Check(!supply.TryFeed(punch) && supply.Held == identity && punch.Input.Count == 0,
                "nearby third-person feed cannot bypass the yellow line at punch rotation " + turn * 90);
        }
        punchObject.transform.position += Vector3.right * (layout.BoundaryX - plateWidth * .25f - punch.Ports.InputPort.position.x);
        Physics.SyncTransforms();
        Check(!supply.TryFeed(punch, true) && supply.Held == identity && punch.Input.Count == 0,
            "punch input centered on the source side still rejects a raw plate straddling the yellow line");
        Check(supply.CancelPickUp() && supply.Remaining == count - 1,
            "cancel after rejected punch feeds restores stock without duplicating a plate");
        ClickStock(); identity = supply.Held;
        punchObject.transform.position += Vector3.right * (layout.BoundaryX - plateWidth - 1 - punch.Ports.InputPort.position.x);
        placement.player.position = new Vector3(-50, 2, -50); Physics.SyncTransforms();
        punchClick = Down(punchObject.GetComponent<EquipmentGrid>().Footprint.center);
        punch.Incoming = 32;
        Check(placement.FeedHeldFromRay(punchClick, true) && supply.Held == identity && punch.Input.Count == 0,
            "clicking a full punch preserves the picked plate");
        punch.Incoming = 0;
        Check(!supply.TryFeed(punch) && supply.Held == identity, "third-person feed still enforces distance");
        Check(placement.FeedHeldFromRay(punchClick, true) && supply.Held == null && punch.Input.Peek() == identity,
            "bird click on the punch module transfers the exact picked plate remotely");
        Check(Vector3.Distance(identity.transform.position, punch.Ports.InputPort.position) < .005f,
            "clicked plate is placed at the punch pickup region before processing");
        punch.enabled = true; yield return null; yield return null;
        Check(punch.Busy || punch.Completed > 0, "punch processing starts automatically after bird feed");
        float deadline = Time.time + 120;
        while (punch.Completed == 0 && Time.time < deadline && !failed) yield return null;
        Check(punch.Completed == 1 && punch.Output == identity && identity.Stage == 1, "the clicked raw plate completes actual punching");

        // Finished items are dropped from all four outer edges onto the open receiving floor.
        Destroy(system.FinishedArea.gameObject); yield return null;
        system.CreateFinishedArea(new Vector3(200, 0, 200), 2.1224f);
        var area = system.FinishedArea;
        Check((area.DockArea.Footprint.size - new Vector3(area.Footprint.size.x, 0, area.Footprint.size.z)).magnitude < .005f,
            "the whole visible outer storage boundary accepts rail contact");
        var beltObject = FactoryProductionAuditRunner.Model(2, new Vector3(240, 0, 200), 0);
        system.Register(beltObject, 2); var belt = beltObject.GetComponent<FactoryStation>(); belt.enabled = false;
        Check(punch.TakeOutput() == identity, "the clicked plate continues into the original finishing sequence");
        var originalReplay = identity.Replay; originalReplay.Space = () => belt.Mapping;
        deadline = Time.time + 240;
        while (!originalReplay.Failed && originalReplay.Source.ReplayCheckpoint != "Complete" && Time.time < deadline)
        {
            if (originalReplay.Source.ReplayCheckpoint != null) originalReplay.Source.ContinueReplay();
            yield return null;
        }
        Check(!originalReplay.Failed && originalReplay.Source.ReplayCheckpoint == "Complete", "real bending and welding create the storage test product");
        originalReplay.Freeze(); identity.SetStage(3);
        if (SessionState.GetBool("FactoryFinishedPickupAudit.Pending", false))
        {
            finishedPickupAudit = true;
            SessionState.SetBool("FactoryFinishedPickupAudit.Pending", false);
            yield return VerifyBufferedFinishedPickup(identity, belt);
            Finish(true); yield break;
        }
        Check(area.GetComponentsInChildren<BoxCollider>().Length == 0,
            "receiving area has no platforms, columns or physical barriers");
        var manual = CopyFinished(identity);
        Bounds manualBounds = FactoryWorkpiece.GeometryBounds(manual.transform);
        manual.transform.position += belt.Ports.OutputPort.position - manualBounds.center;
        Check(belt.ReturnOutput(manual), "manual test product enters the conveyor output");
        belt.enabled = true;
        deadline = Time.time + 30;
        while (belt.OutputChanging && Time.time < deadline && !failed) yield return null;
        Check(!belt.OutputChanging, "manual pickup waits for the actual conveyor queue to settle");
        placement.player.position = new Vector3(-100, 1, -100);
        Check(!placement.TryPickUpFinishedProduct(belt) && belt.Output == manual, "distant F pickup cannot remove the conveyor product");
        placement.player.position = FactoryWorkpiece.GeometryBounds(manual.transform).center + Vector3.right;
        placement.worldCamera.transform.position = placement.player.position + Vector3.up * 2;
        belt.ReservedBy = new GameObject("Manual handoff guard").AddComponent<FactoryArm>();
        Check(placement.TryPickUpFinishedProduct(belt) && belt.Output == manual,
            "manual pickup cannot steal a product reserved by an automatic carrier");
        DestroyImmediate(belt.ReservedBy.gameObject); belt.ReservedBy = null;
        Check(placement.TryPickUpFinishedProduct(belt) && belt.Output == null,
            "nearby F pickup takes the exact conveyor product and frees its output");
        Check(!placement.TryStoreCarriedProduct() && area.StoredCount == 0, "distant left-click cannot sell a held product");
        placement.player.position = area.transform.position + Vector3.up;
        placement.worldCamera.transform.position = placement.player.position + Vector3.up * 2;
        Check(placement.TryStoreCarriedProduct(), "nearby left-click releases the held product into the receiving area");
        Check(!placement.TryStoreCarriedProduct(), "repeated left-click cannot submit the same product twice");
        deadline = Time.time + 10;
        while (area.StoredCount == 0 && Time.time < deadline && !failed) yield return null;
        Check(area.StoredCount == 1 && area.Revenue == 2000 && manual.transform.parent == area.Products,
            "manual product lands visibly and awards exactly 2000 yuan");
        Check(!area.BeginManualDrop(manual) && area.Revenue == 2000, "already sold product cannot be counted again");
        Check(area.GetComponentInChildren<TMPro.TMP_Text>().text.Contains("2,000"), "world label shows earned revenue");
        DestroyImmediate(area.gameObject);
        system.CreateFinishedArea(new Vector3(200, 0, 200), 2.1224f); area = system.FinishedArea;
        system.enabled = true;
        Vector3[] sides = { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };
        GameObject railObject = null, homeRailObject = null, armObject = null;
        FactoryRail rail = null, returnRail = null;
        FactoryArm arm = null;
        for (int index = 0; index < FinishedGoodsArea.Capacity && !failed; index++)
        {
            Vector3 side = sides[index / 5];
            if (index % 5 == 0)
            {
                railObject = FactoryProductionAuditRunner.Model(4, area.transform.position, 0);
                rail = railObject.GetComponent<FactoryRail>();
                Bounds a = area.DockArea.Footprint, b = railObject.GetComponent<EquipmentGrid>().Footprint;
                Vector3 center = a.center + Vector3.Scale(side, a.extents + b.extents);
                railObject.transform.position += new Vector3(center.x - b.center.x, 0, center.z - b.center.z);
                rail.Measure(); system.Rails.Add(rail);
                homeRailObject = FactoryProductionAuditRunner.Model(4, center + side * 3, 0);
                returnRail = homeRailObject.GetComponent<FactoryRail>();
                system.Rails.Add(returnRail); system.Graph.Build(system.Rails);
                beltObject.transform.position += new Vector3(center.x - belt.Ports.FinishedPickupDock.x, 0, center.z - belt.Ports.FinishedPickupDock.z);
                armObject = FactoryProductionAuditRunner.Model(3, center + side * 3, 0);
                arm = armObject.AddComponent<FactoryArm>(); arm.Initialize(returnRail);
            }
            Check(arm.CanReach(area.DockPosition(belt.Ports.FinishedPickupDock.y), out _), "outer edge is reachable from " + side);
            Check(arm.CanReach(area.DockPosition(belt.Ports.FinishedPickupDock.y), out var deliveryPath) &&
                deliveryPath.TrueForAll(p => p.x >= rail.GetComponent<EquipmentGrid>().Footprint.min.x - .01f &&
                    p.x <= rail.GetComponent<EquipmentGrid>().Footprint.max.x + .01f &&
                    p.z >= rail.GetComponent<EquipmentGrid>().Footprint.min.z - .01f &&
                    p.z <= rail.GetComponent<EquipmentGrid>().Footprint.max.z + .01f ||
                    p.x >= returnRail.GetComponent<EquipmentGrid>().Footprint.min.x - .01f &&
                    p.x <= returnRail.GetComponent<EquipmentGrid>().Footprint.max.x + .01f &&
                    p.z >= returnRail.GetComponent<EquipmentGrid>().Footprint.min.z - .01f &&
                    p.z <= returnRail.GetComponent<EquipmentGrid>().Footprint.max.z + .01f),
                "delivery route ends on the player's rail without entering the receiving area");
            Vector3 home = arm.TrackPosition;
            var part = CopyFinished(identity);
            Bounds geometry = FactoryWorkpiece.GeometryBounds(part.transform);
            part.transform.position += belt.Ports.OutputPort.position - new Vector3(geometry.center.x, geometry.min.y, geometry.center.z);
            belt.ReturnOutput(part);
            deadline = Time.time + 45;
            float maxFootGap = 0, maxPlateGap = 0;
            while ((area.StoredCount < index + 1 || arm.Busy) && Time.time < deadline && !failed)
            {
                if (arm.Busy)
                {
                    Vector3 point = arm.TrackPosition; Bounds track = railObject.GetComponent<EquipmentGrid>().Footprint;
                    float support = 0;
                    foreach (var trackRail in new[] { rail, returnRail })
                    {
                        track = trackRail.GetComponent<EquipmentGrid>().Footprint;
                        if (point.x >= track.min.x - .001f && point.x <= track.max.x + .001f &&
                            point.z >= track.min.z - .001f && point.z <= track.max.z + .001f)
                            support = Mathf.Max(support, trackRail.SurfaceY);
                    }
                    float bottom = FactoryMotion.Bounds(FactoryMotion.Node(arm.transform, 872)).min.y;
                    maxFootGap = Mathf.Max(maxFootGap, Mathf.Abs(bottom - support));
                    float plateBottom = armObject.transform.Find("GridBasePlate").GetComponent<Renderer>().bounds.min.y;
                    maxPlateGap = Mathf.Max(maxPlateGap, Mathf.Abs(plateBottom - support));
                }
                yield return null;
            }
            Check(!arm.Busy && Vector3.Distance(arm.TrackPosition, home) < .005f,
                "carrier returns to its exact placed home through edge " + side);
            Check(maxFootGap < .015f && maxPlateGap < .015f,
                "carrier foot and base plate remain supported: foot " + maxFootGap + ", plate " + maxPlateGap);
            Check(area.StoredCount == index + 1 && belt.Output == null && arm.FinishedDeliveries == index % 5 + 1,
                "automatic real delivery through edge " + side + ", stored " + (index + 1) + "/20");
            if (index % 5 == 4)
            {
                Destroy(armObject); system.Rails.Remove(rail); system.Rails.Remove(returnRail);
                Destroy(railObject); Destroy(homeRailObject); system.Graph.Build(system.Rails); yield return null;
            }
        }
        Check(area.StoredCount == 20 && area.Products.childCount == 20 && !area.HasSpace && area.Reserve() < 0,
            "four-sided storage retains all twenty visible products and rejects overflow");
        Check(area.Revenue == 40000, "twenty automatic edge drops award exactly 40000 yuan");
        var overflow = CopyFinished(identity);
        Bounds overflowBounds = FactoryWorkpiece.GeometryBounds(overflow.transform);
        overflow.transform.position += belt.Ports.OutputPort.position - overflowBounds.center;
        Check(belt.ReturnOutput(overflow), "overflow test product remains available on the conveyor");
        deadline = Time.time + 30;
        while (belt.OutputChanging && Time.time < deadline && !failed) yield return null;
        placement.player.position = FactoryWorkpiece.GeometryBounds(overflow.transform).center + Vector3.right;
        placement.worldCamera.transform.position = placement.player.position + Vector3.up * 2;
        Check(placement.TryPickUpFinishedProduct(belt), "full receiving area does not prevent manual conveyor pickup");
        placement.player.position = area.transform.position + Vector3.up;
        placement.worldCamera.transform.position = placement.player.position + Vector3.up * 2;
        Check(!placement.TryStoreCarriedProduct() &&
            (FactoryWorkpiece)typeof(PlacementSystem).GetField("carriedProduct", Private).GetValue(placement) == overflow &&
            area.Revenue == 40000, "full area preserves the held product without awarding extra revenue");
        foreach (Transform first in area.Products)
        foreach (Transform second in area.Products)
        {
            if (first == second) continue;
            Bounds bounds = FactoryWorkpiece.GeometryBounds(first); bounds.Expand(-.003f);
            Check(!bounds.Intersects(FactoryWorkpiece.GeometryBounds(second)), "stored products do not overlap");
        }
        Finish(true);
    }
    void VerifyFixedLayout()
    {
        var layout = placement.GetComponent<FactoryLevelLayout>();
        var area = system.FinishedArea;
        Check(layout != null && supply.IsFixed, "actual game starts with a fixed raw-material source");
        Check(supply.NearStock, "player starts within F pickup reach of the fixed source");
        Check(supply.StockBounds.max.x < layout.BoundaryX && area.Footprint.min.x > layout.BoundaryX,
            "the entire raw pile is outside the boundary and finished area is on the planning side");
        Check(layout.Boundary.positionCount == 2 && layout.Boundary.startWidth >= .2f &&
            Mathf.Abs(layout.Boundary.GetPosition(0).x - layout.BoundaryX) < .001f &&
            Mathf.Abs(layout.Boundary.GetPosition(1).x - layout.BoundaryX) < .001f,
            "a visible continuous ground line marks the raw-material boundary");
        float edge = layout.BoundaryX + 1;
        float measuredWidth = 0;
        for (int kind = 0; kind < 3; kind++)
        {
            var probe = Instantiate(placement.prefabs[kind]);
            var grid = probe.AddComponent<EquipmentGrid>(); grid.Initialize(layout.Scale);
            grid.Snap(new Vector3(edge + grid.Cells.x * .5f, 0, supply.StockBounds.center.z));
            Check(grid.Footprint.min.x >= edge - .001f && grid.Footprint.max.x + FactoryLevelLayout.EndClearance <= area.Footprint.min.x + .001f,
                "real grid-snapped module " + kind + " fits between the boundary and finished area with clearance");
            measuredWidth += grid.Cells.x;
            edge = grid.Footprint.max.x + FactoryLevelLayout.ModuleSpacing;
            probe.SetActive(false); Destroy(probe);
        }
        Check(Mathf.Abs(layout.RequiredModulesWidth - measuredWidth) < .001f, "layout distance derives from all three actual module footprints");
        float netDistance = area.Footprint.min.x - supply.StockBounds.max.x;
        Check(netDistance > measuredWidth + FactoryLevelLayout.ModuleSpacing * 2 && netDistance < measuredWidth + 20,
            "source-to-storage net gap is large enough but bounded: " + netDistance.ToString("F2") + " for " + measuredWidth + " units of modules");
        SnapshotLayout();
    }
    void SnapshotLayout()
    {
        var obj = new GameObject("Fixed layout preview camera"); var camera = obj.AddComponent<Camera>();
        Vector3 center = (supply.transform.position + system.FinishedArea.transform.position) * .5f;
        camera.transform.SetPositionAndRotation(center + Vector3.up * 85, Quaternion.Euler(90, 0, 0));
        camera.orthographic = true; camera.orthographicSize = 18;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .14f, .16f);
        // The floor normally follows the gameplay camera; expand it for this wider preview too.
        var floor = FindObjectOfType<InfiniteFloor>();
        Transform originalCamera = floor != null ? floor.gameCamera : null;
        if (floor != null)
        {
            floor.gameCamera = camera.transform;
            typeof(InfiniteFloor).GetMethod("RefreshTiles", Private).Invoke(floor, null);
        }
        var rotations = new Dictionary<Transform, Quaternion>();
        foreach (var label in FindObjectsOfType<TMPro.TextMeshPro>())
        { rotations[label.transform] = label.transform.rotation; label.transform.rotation = camera.transform.rotation; }
        var texture = new RenderTexture(1920, 800, 24); camera.targetTexture = texture; camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var picture = new Texture2D(1920, 800, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1920, 800), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/factory-start-layout.png", picture.EncodeToPNG());
        foreach (var pair in rotations) pair.Key.rotation = pair.Value;
        if (floor != null)
        {
            floor.gameCamera = originalCamera;
            typeof(InfiniteFloor).GetMethod("RefreshTiles", Private).Invoke(floor, null);
        }
        RenderTexture.active = previous; camera.targetTexture = null;
        Destroy(picture); Destroy(texture); Destroy(obj);
    }
    IEnumerator VerifyBufferedFinishedPickup(FactoryWorkpiece template, FactoryStation belt)
    {
        system.armTravelMultiplier = 3; system.armPickupMultiplier = 3; system.armRotationMultiplier = 2;
        for (int turn = 0; turn < 4; turn++)
        {
            system.enabled = false;
            var area = system.FinishedArea;
            var railObject = FactoryProductionAuditRunner.Model(4, area.transform.position, 0);
            var rail = railObject.GetComponent<FactoryRail>();
            Bounds receiving = area.Footprint, track = railObject.GetComponent<EquipmentGrid>().Footprint;
            Vector3 center = receiving.center + Vector3.left * (receiving.extents.x + track.extents.x);
            railObject.transform.position += new Vector3(center.x - track.center.x, 0, center.z - track.center.z);
            rail.Measure(); system.Rails.Add(rail); system.Graph.Build(system.Rails);
            belt.transform.rotation = Quaternion.Euler(0, turn * 90, 0);
            belt.transform.position += new Vector3(center.x - belt.Ports.FinishedPickupDock.x, 0, center.z - belt.Ports.FinishedPickupDock.z);
            // Legal edge-only rail contact leaves the calibrated dock center
            // outside the physical rail, as happens in player-built layouts.
            belt.transform.position += Vector3.forward * (rail.GetComponent<EquipmentGrid>().Footprint.extents.z + belt.Ports.PickupDockArea.Footprint.extents.z - .02f);
            belt.enabled = true;
            var parts = new List<FactoryWorkpiece>();
            for (int i = 0; i < 5; i++)
            {
                var part = CopyFinished(template);
                var geometry = FactoryWorkpiece.GeometryBounds(part.transform);
                part.transform.RotateAround(geometry.center, Vector3.up, turn * 90);
                geometry = FactoryWorkpiece.GeometryBounds(part.transform);
                part.transform.position += belt.Ports.OutputPort.position - new Vector3(geometry.center.x, geometry.min.y, geometry.center.z);
                Check(belt.ReturnOutput(part), "buffered pickup accepts actual finished geometry " + turn + "/" + i);
                parts.Add(part);
            }
            float deadline = Time.time + 60;
            while (belt.OutputChanging && Time.time < deadline) yield return null;
            Check(!belt.OutputChanging, "five products settle before pickup at rotation " + turn * 90);
            report.AppendLine("PICKUP geometry=" + FactoryWorkpiece.GeometryBounds(belt.Output.transform) + " port=" + belt.Ports.OutputPort.position + " dock=" + belt.Ports.FinishedPickupDock);
            var armObject = FactoryProductionAuditRunner.Model(3, center, 0);
            var arm = armObject.AddComponent<FactoryArm>(); arm.Initialize(rail);
            if (turn == 0)
            {
                var first = belt.Output;
                arm.AssignFinished(belt, area, area.Reserve());
                typeof(LoadingProcessController).GetProperty("ReplayError").SetValue(first.Replay.Source, "模拟取件失败以验证预约回滚");
                for (int i = 0; i < 8 && arm.Busy; i++) yield return null;
                Check(!arm.Busy && belt.ReservedBy == null && belt.Output == first && belt.OutputCount == 5 && area.StoredCount == 0,
                    "failed pickup releases the arm and belt while preserving the exact FIFO product and count");
                var reservations = (HashSet<int>)typeof(FinishedGoodsArea).GetField("reserved", Private).GetValue(area);
                Check(reservations.Count == 0 && first.GetComponent<BoxCollider>().enabled,
                    "failed pickup releases the receiving slot and restores product collision");
            }
            system.enabled = true;
            int expected = (turn + 1) * 5;
            deadline = Time.time + 240;
            while ((area.StoredCount < expected || arm.Busy) && Time.time < deadline && !failed) yield return null;
            Check(!arm.Busy && area.StoredCount == expected && belt.OutputCount == 0 && belt.ReservedBy == null && arm.FinishedDeliveries == 5,
                "default-speed pickup drains five FIFO products and releases reservations at rotation " + turn * 90 + "; state=" + arm.Status);
            Destroy(armObject); system.Rails.Remove(rail); system.Graph.Build(system.Rails); Destroy(railObject);
            yield return null;
        }
    }

    FactoryWorkpiece CopyFinished(FactoryWorkpiece template)
    {
        var root = new GameObject("成品测试样件", typeof(FactoryVisual));
        var part = root.AddComponent<FactoryWorkpiece>(); part.SetStage(3);
        foreach (var source in template.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
            mesh.transform.SetParent(root.transform, false); mesh.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            part.PaintRenderer(mesh.GetComponent<MeshRenderer>());
            Demo1MeshMirror.SetPose(mesh.transform, source.transform.localToWorldMatrix);
        }
        return part;
    }
    void Finish(bool ok)
    {
        if (finished) return; finished = true;
        Time.captureDeltaTime = 0;
        Directory.CreateDirectory("Logs"); File.WriteAllText(finishedPickupAudit ? "Logs/factory-finished-pickup-audit.txt" : "Logs/factory-workflow-input-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
#endif
