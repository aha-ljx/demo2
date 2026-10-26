using System.Collections.Generic;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class FactoryFinishedPlacementAudit
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const string Pending = "FactoryFinishedPlacementAudit.Pending";
    static double verifyAt;
    static FactoryFinishedPlacementAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            verifyAt = EditorApplication.timeSinceStartup + 2; EditorApplication.update += Verify;
        };
    }
    [MenuItem("Tools/Factory/Run Finished Arm Placement Audit")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
    static void Verify()
    {
        if (EditorApplication.timeSinceStartup < verifyAt) return;
        EditorApplication.update -= Verify; SessionState.SetBool(Pending, false);
        var report = new StringBuilder(); bool passed = false;
        Action<bool, string> require = (ok, text) => { if (!ok) throw new Exception(text); report.AppendLine("PASS " + text); };
        try
        {
            var placement = UnityEngine.Object.FindObjectOfType<PlacementSystem>(); placement.enabled = false;
            placement.worldCamera.GetComponent<GameController>().enabled = false;
            placement.player.position = new Vector3(-100, 2, -100); Physics.SyncTransforms();
            var system = FactoryProduction.Instance; system.enabled = false;
            UnityEngine.Object.DestroyImmediate(system.FinishedArea.gameObject);
            system.CreateFinishedArea(new Vector3(100, 0, 100), 2.1224f);
            var area = system.FinishedArea;
            require(Mathf.Abs(area.transform.position.x % 1 - .5f) < .001f && Mathf.Abs(area.transform.position.z % 1 - .5f) < .001f,
                "storage cross aisle is aligned with rail tile centers");
            var placed = (List<GameObject>)typeof(PlacementSystem).GetField("placed", Private).GetValue(placement);
            var kinds = (Dictionary<GameObject, int>)typeof(PlacementSystem).GetField("placedTypes", Private).GetValue(placement);
            var mainRailObject = FactoryProductionAuditRunner.Model(4, new Vector3(50, 0, 50), 0);
            system.Register(mainRailObject, 4); placed.Add(mainRailObject); kinds[mainRailObject] = 4;
            var mainArmObject = FactoryProductionAuditRunner.Model(3, mainRailObject.GetComponent<EquipmentGrid>().Footprint.center, 0);
            system.Register(mainArmObject, 3); placed.Add(mainArmObject); kinds[mainArmObject] = 3;
            var endRailObject = FactoryProductionAuditRunner.Model(4, area.transform.position, 0);
            system.Register(endRailObject, 4); placed.Add(endRailObject); kinds[endRailObject] = 4;
            var endRail = endRailObject.GetComponent<FactoryRail>();
            require(!system.Graph.Same(mainRailObject.GetComponent<FactoryRail>(), endRail), "storage rail is independent of the occupied production track");
            typeof(PlacementSystem).GetMethod("SelectItem", Private).Invoke(placement, new object[] { 3 });
            var preview = (GameObject)typeof(PlacementSystem).GetField("groundPreview", Private).GetValue(placement);
            var align = typeof(PlacementSystem).GetMethod("AlignVisualToGround", Private);
            var check = typeof(PlacementSystem).GetMethod("CanPlaceHere", Private);
            align.Invoke(placement, new object[] { preview, area.transform.position });
            require((bool)check.Invoke(placement, new object[] { preview, null }), "actual translucent arm preview can be placed on the storage aisle rail");
            require(!FactoryProduction.HardwareOverlap(preview, area.gameObject), "storage platforms do not obstruct the aligned arm base");
            typeof(PlacementSystem).GetMethod("PlaceItem", Private).Invoke(placement, null);
            var endArmObject = placed[placed.Count - 1]; var endArm = endArmObject.GetComponent<FactoryArm>();
            require(endArm != null && endArm.Rail == endRail && system.Arms.Count == 2, "actual click registers an independent finished-product arm alongside the production arm");
            require(!(bool)check.Invoke(placement, new object[] { preview, null }), "a duplicate arm on the same connected storage rail is still rejected");
            require(((string)typeof(PlacementSystem).GetField("placementBlockReason", Private).GetValue(placement)).Contains("已有机械臂"),
                "occupied-track rejection explains the real reason");
            for (int i = 1; i <= 5; i++)
            {
                var tile = FactoryProductionAuditRunner.Model(4, area.transform.position + Vector3.right * (3 * i), 0);
                system.Register(tile, 4); placed.Add(tile); kinds[tile] = 4;
            }
            var beltObject = FactoryProductionAuditRunner.Model(2, area.transform.position + Vector3.right * 20, 0);
            var ports = beltObject.GetComponent<FactoryPorts>();
            Vector3 pickup = area.transform.position + Vector3.right * 15;
            beltObject.transform.position += new Vector3(pickup.x - ports.FinishedPickupDock.x, 0, pickup.z - ports.FinishedPickupDock.z);
            system.Register(beltObject, 2); placed.Add(beltObject); kinds[beltObject] = 2;
            require(endArm.CanReach(ports.FinishedPickupDock, out _), "placed arm reaches the belt-end pickup area through its actual rail path");
            require(endArm.CanReach(area.DockPosition(ports.FinishedPickupDock.y), out _), "placed arm reaches final storage through the aligned cross aisle");
            require(!mainArmObject.GetComponent<FactoryArm>().CanReach(ports.FinishedPickupDock, out _), "the production arm cannot borrow the disconnected storage track");
            report.AppendLine("PASS ALL"); passed = true;
        }
        catch (Exception error) { report.AppendLine("FAIL " + error); }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-finished-placement-audit.txt", report.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
    public static void Diagnose()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        var placement = Object.FindObjectOfType<PlacementSystem>();
        var system = new GameObject("Finished placement diagnosis").AddComponent<FactoryProduction>();
        typeof(PlacementSystem).GetField("production", Private).SetValue(placement, system);
        typeof(PlacementSystem).GetField("selected", Private).SetValue(placement, 3);
        var placed = (List<GameObject>)typeof(PlacementSystem).GetField("placed", Private).GetValue(placement);
        var kinds = (Dictionary<GameObject, int>)typeof(PlacementSystem).GetField("placedTypes", Private).GetValue(placement);
        var align = typeof(PlacementSystem).GetMethod("AlignVisualToGround", Private);
        var check = typeof(PlacementSystem).GetMethod("CanPlaceHere", Private);
        var report = new StringBuilder();
        for (int yaw = 0; yaw < 360; yaw += 90)
        {
            var station = FactoryProductionAuditRunner.Model(2, new Vector3(100, 0, 100), yaw);
            var ports = station.GetComponent<FactoryPorts>();
            foreach (Vector3 target in new[] { ports.DockPosition, ports.ConveyorDockPosition, ports.FinishedPickupDock })
            {
                var railObject = FactoryProductionAuditRunner.Model(4, target, yaw);
                var rail = railObject.GetComponent<FactoryRail>();
                system.Rails.Clear(); system.Rails.Add(rail); system.Graph.Build(system.Rails);
                placed.Clear(); kinds.Clear(); placed.Add(station); kinds[station] = 2; placed.Add(railObject); kinds[railObject] = 4;
                var arm = FactoryProductionAuditRunner.Model(3, target, 0);
                typeof(PlacementSystem).GetField("groundPreview", Private).SetValue(placement, arm);
                align.Invoke(placement, new object[] { arm, target });
                for (int armYaw = 0; armYaw < 360; armYaw += 90)
                {
                    report.AppendLine($"moduleYaw={yaw} dock={target:F3} armYaw={armYaw} allowed={check.Invoke(placement, new object[] { arm, null })} hardwareOverlap={FactoryProduction.HardwareOverlap(arm, station)} base={FactoryArm.BaseCenter(arm.transform):F3}");
                    arm.transform.RotateAround(FactoryArm.BaseCenter(arm.transform), Vector3.up, 90);
                    FactoryArm.AlignToRail(arm.transform, rail, target);
                }
                Object.DestroyImmediate(arm); Object.DestroyImmediate(railObject);
            }
            for (int dx = -3; dx <= 3; dx++)
            for (int dz = -3; dz <= 3; dz++)
            {
                var railObject = FactoryProductionAuditRunner.Model(4, ports.FinishedPickupDock + new Vector3(dx, 0, dz), 0);
                var rail = railObject.GetComponent<FactoryRail>();
                if (!ports.PickupDockArea.TryConnection(railObject.GetComponent<EquipmentGrid>().Footprint, out _))
                { Object.DestroyImmediate(railObject); continue; }
                placed.Clear(); kinds.Clear(); placed.Add(station); kinds[station] = 2;
                system.Rails.Clear(); system.Graph.Build(system.Rails);
                typeof(PlacementSystem).GetField("selected", Private).SetValue(placement, 4);
                bool railAllowed = (bool)check.Invoke(placement, new object[] { railObject, null });
                if (!railAllowed) { Object.DestroyImmediate(railObject); continue; }
                placed.Add(railObject); kinds[railObject] = 4; system.Rails.Add(rail); system.Graph.Build(system.Rails);
                typeof(PlacementSystem).GetField("selected", Private).SetValue(placement, 3);
                Vector3 target = railObject.GetComponent<EquipmentGrid>().Footprint.center;
                var arm = FactoryProductionAuditRunner.Model(3, target, 0);
                typeof(PlacementSystem).GetField("groundPreview", Private).SetValue(placement, arm);
                align.Invoke(placement, new object[] { arm, target });
                string states = "";
                for (int armYaw = 0; armYaw < 360; armYaw += 90)
                {
                    states += $" {armYaw}:{check.Invoke(placement, new object[] { arm, null })}";
                    arm.transform.RotateAround(FactoryArm.BaseCenter(arm.transform), Vector3.up, 90);
                    FactoryArm.AlignToRail(arm.transform, rail, target);
                }
                report.AppendLine($"EDGE moduleYaw={yaw} railCenter={target:F3} armAllowed={states}");
                Object.DestroyImmediate(arm); Object.DestroyImmediate(railObject);
            }
            typeof(PlacementSystem).GetField("selected", Private).SetValue(placement, 3);
            Object.DestroyImmediate(station);
        }
        system.Rails.Clear(); placed.Clear(); kinds.Clear();
        system.CreateFinishedArea(new Vector3(100, 0, 100), 2.1224f);
        foreach (Vector3 side in new[] { Vector3.left, Vector3.right, Vector3.back, Vector3.forward })
        {
            var railObject = FactoryProductionAuditRunner.Model(4, new Vector3(100, 0, 100), 0);
            Bounds b = railObject.GetComponent<EquipmentGrid>().Footprint, a = system.FinishedArea.DockArea.Footprint;
            Vector3 target = a.center + Vector3.Scale(side, a.extents + b.extents);
            railObject.transform.position += new Vector3(target.x - b.center.x, 0, target.z - b.center.z);
            var rail = railObject.GetComponent<FactoryRail>(); rail.Measure(); system.Rails.Add(rail); system.Graph.Build(system.Rails);
            placed.Add(railObject); kinds[railObject] = 4;
            var arm = FactoryProductionAuditRunner.Model(3, target, 0);
            typeof(PlacementSystem).GetField("groundPreview", Private).SetValue(placement, arm);
            align.Invoke(placement, new object[] { arm, target });
            report.AppendLine($"storageSide={side} allowed={check.Invoke(placement, new object[] { arm, null })} hardwareOverlap={FactoryProduction.HardwareOverlap(arm, system.FinishedArea.gameObject)}");
            Object.DestroyImmediate(arm); system.Rails.Clear(); placed.Clear(); kinds.Clear(); Object.DestroyImmediate(railObject);
        }
        for (int dx = -5; dx <= 5; dx++)
        for (int dz = -5; dz <= 5; dz++)
        {
            var railObject = FactoryProductionAuditRunner.Model(4, new Vector3(100 + dx, 0, 100 + dz), 0);
            var rail = railObject.GetComponent<FactoryRail>();
            if (!system.FinishedArea.DockArea.TryConnection(railObject.GetComponent<EquipmentGrid>().Footprint, out _))
            { Object.DestroyImmediate(railObject); continue; }
            system.Rails.Clear(); system.Graph.Build(system.Rails); placed.Clear(); kinds.Clear();
            typeof(PlacementSystem).GetField("selected", Private).SetValue(placement, 4);
            bool railAllowed = (bool)check.Invoke(placement, new object[] { railObject, null });
            if (!railAllowed) { Object.DestroyImmediate(railObject); continue; }
            system.Rails.Add(rail); system.Graph.Build(system.Rails); placed.Add(railObject); kinds[railObject] = 4;
            typeof(PlacementSystem).GetField("selected", Private).SetValue(placement, 3);
            Vector3 target = railObject.GetComponent<EquipmentGrid>().Footprint.center;
            var arm = FactoryProductionAuditRunner.Model(3, target, 0);
            typeof(PlacementSystem).GetField("groundPreview", Private).SetValue(placement, arm);
            align.Invoke(placement, new object[] { arm, target });
            string states = "";
            for (int armYaw = 0; armYaw < 360; armYaw += 90)
            {
                states += $" {armYaw}:{check.Invoke(placement, new object[] { arm, null })}";
                arm.transform.RotateAround(FactoryArm.BaseCenter(arm.transform), Vector3.up, 90);
                FactoryArm.AlignToRail(arm.transform, rail, target);
            }
            report.AppendLine($"STORAGE railCenter={target:F3} armAllowed={states}");
            Object.DestroyImmediate(arm); Object.DestroyImmediate(railObject);
        }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-finished-placement-diagnostic.txt", report.ToString());
    }
}
