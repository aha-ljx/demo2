using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class PlacementSystem
{
    public FactoryPlanLibrary Plans { get; private set; }
    public FactoryLocalAssistant Assistant { get; private set; }
    public int PlacedCount => placed.Count;
    public void InitializePlans(Canvas canvas, FactoryLevelSession level)
    {
        Plans = gameObject.AddComponent<FactoryPlanLibrary>(); Plans.Initialize(canvas, this, level);
        Assistant = gameObject.AddComponent<FactoryLocalAssistant>(); Assistant.Initialize(canvas, this, level);
    }
    public FactorySavedPlan CapturePlan(string name, FactoryLevelSession level)
    {
        if (level == null || !level.IsEnded || placed.Count == 0) return null;
        var devices = new List<FactoryPlanDevice>();
        foreach (var item in placed)
        {
            if (item == null || !placedTypes.TryGetValue(item, out int kind)) continue;
            var arm = item.GetComponent<FactoryArm>();
            devices.Add(new FactoryPlanDevice { kind = kind,
                offset = (arm != null ? arm.PlacementPosition : item.transform.position) - rawSupply.transform.position,
                rotation = item.transform.rotation, name = kind == 4 ? null : production.Statistics.Find(item)?.Name });
        }
        return new FactorySavedPlan { name = name, equipmentScale = equipmentScale, devices = devices.ToArray(),
            resultCount = level.ResultCount, resultNet = level.ResultNet, cost = CurrentCost };
    }
    public bool CanLoadPlan(out string error)
    {
        error = null;
        var level = FactoryLevelSession.Active;
        if (level == null || level.IsRunning || level.IsEnded) error = "仅能在开始生产前的规划阶段载入方案";
        else if (placed.Count != 0 || production.Stations.Count != 0 || production.Rails.Count != 0 || production.Arms.Count != 0)
            error = "请先撤回已摆放的设备，在空白场景中载入方案";
        else if (rawSupply.Held != null || carriedProduct != null) error = "请先归还或放下手中的物品，再载入方案";
        return error == null;
    }
    public bool TryLoadPlan(FactorySavedPlan plan, out string error)
    {
        if (!CanLoadPlan(out error) || !FactoryPlanStorage.Validate(plan, out error)) return false;
        if (Mathf.Abs(plan.equipmentScale - equipmentScale) > .001f)
        { error = "该方案的设备尺寸与当前场景不兼容，未载入任何设备"; return false; }
        CancelSelection(); inspectedItem = pressedItem = null; dragging = false;
        var staged = new List<GameObject>();
        bool committed = false;
        try
        {
            // Stage geometry first. No station, arm, telemetry or cost is committed until every item passes.
            foreach (var device in plan.devices)
            {
                if (device.kind >= prefabs.Length || prefabs[device.kind] == null)
                { error = "当前场景缺少方案所需的设备"; return false; }
                var item = CreateEquipment(device.kind); staged.Add(item);
                item.transform.SetPositionAndRotation(rawSupply.transform.position + device.offset, device.rotation);
                placed.Add(item); placedTypes.Add(item, device.kind);
                if (device.kind == 4) production.Rails.Add(item.GetComponent<FactoryRail>());
            }
            foreach (var rail in production.Rails) rail.Measure();
            production.Graph.Build(production.Rails);
            var occupied = new List<FactoryRail>();
            for (int i = 0; i < staged.Count; i++)
            {
                var item = staged[i]; int kind = plan.devices[i].kind;
                if (!CanPlaceHere(item, item))
                { error = "方案无法摆放：" + placementBlockReason + "；未载入任何设备"; return false; }
                if (kind == 3)
                {
                    if (!production.FindRail(item, null, out var rail))
                    { error = "方案中的机械臂没有对应滑槽，未载入任何设备"; return false; }
                    foreach (var other in occupied)
                        if (production.Graph.Same(rail, other))
                        { error = "方案中同一连通滑槽上存在多个机械臂，未载入任何设备"; return false; }
                    occupied.Add(rail);
                }
            }
            production.Rails.Clear(); production.Graph.Build(production.Rails);
            production.Statistics.ResetPlanningModules();
            // Rails must exist before registering arms. Names are restored independently of this order.
            foreach (int phase in new[] { 4, 0, 1, 2, 3 })
                for (int i = 0; i < staged.Count; i++)
                    if (plan.devices[i].kind == phase)
                    {
                        production.Register(staged[i], phase);
                        if (phase != 4) production.Statistics.RestoreName(staged[i], plan.devices[i].name);
                    }
            production.Rebuild(); RefreshCostDisplay(); Physics.SyncTransforms();
            LiftPlayerAboveEquipment();
            committed = true; error = null; return true;
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is NullReferenceException)
        { error = "方案无法恢复，未载入任何设备；请使用与当前版本兼容的方案"; return false; }
        finally
        {
            if (!committed)
            {
                foreach (var item in staged)
                {
                    placed.Remove(item); placedTypes.Remove(item);
                    production.Stations.RemoveAll(s => s != null && s.gameObject == item);
                    production.Arms.RemoveAll(a => a != null && a.gameObject == item);
                    item.SetActive(false); Destroy(item);
                }
                production.Rails.Clear(); production.Graph.Build(production.Rails);
                // A pristine scene remains pristine even if registration itself failed.
                if (staged.Exists(item => item.GetComponent<FactoryStation>() != null || item.GetComponent<FactoryArm>() != null))
                    production.Statistics.ResetPlanningModules();
                production.Rebuild(); RefreshCostDisplay(); Physics.SyncTransforms();
            }
        }
    }
}
