using UnityEngine;

public partial class PlacementSystem
{
    public float AssistantScale => equipmentScale;
    public static int EquipmentPrice(int kind) => kind >= 0 && kind < equipmentPrices.Length ? equipmentPrices[kind] : 0;
    public bool ApplyAssistantPlan(FactoryAssistantProfile profile, out string error)
    {
        error = null;
        if (profile == null || profile.plan == null || profile.feed == null) { error = "没有可摆放的已验证方案"; return false; }
        if (!FactoryPlanStorage.Validate(profile.plan, out error)) return false;
        int punches = 0, total = 0;
        foreach (var device in profile.plan.devices) if (device.kind == 0) punches++;
        foreach (int count in profile.feed)
        {
            if (count < 0 || count > FactoryStation.RawInputCapacity) { error = "方案的上料数量不合法"; return false; }
            total += count;
        }
        if (punches != profile.feed.Length || total > rawSupply.Remaining)
        { error = "请保持原料堆库存完整，并在空白场景中摆放该方案"; return false; }
        if (!TryLoadPlan(profile.plan, out error)) return false;
        if (rawSupply.QueueAssistantInput(profile.feed, out error)) return true;
        // Loading is only allowed into a blank scene; roll back this entire new layout if input checks fail.
        foreach (var item in new System.Collections.Generic.List<GameObject>(placed))
        {
            var station = item.GetComponent<FactoryStation>();
            if (station != null) rawSupply.RestoreQueuedRaw(station);
            item.SetActive(false); Destroy(item);
        }
        placed.Clear(); placedTypes.Clear();
        production.Stations.Clear(); production.Arms.Clear(); production.Rails.Clear();
        production.Statistics.ResetPlanningModules(); production.Rebuild(); RefreshCostDisplay(); Physics.SyncTransforms();
        return false;
    }
}

public sealed partial class RawMaterialSupply
{
    internal bool QueueAssistantInput(int[] amounts, out string error)
    {
        error = null;
        if (FactoryLevelSession.Active == null || FactoryLevelSession.Active.IsRunning || FactoryLevelSession.HasEnded || amounts == null)
        { error = "只能在规划阶段分配原料"; return false; }
        var punches = production.Stations.FindAll(s => s != null && s.Kind == 0);
        if (punches.Count != amounts.Length) { error = "冲床数量与上料计划不一致"; return false; }
        int total = 0;
        for (int i = 0; i < amounts.Length; i++)
        {
            if (amounts[i] < 0 || amounts[i] > FeedLimit(punches[i]) || !AllowsPlacement(punches[i].Ports.InputPort.position))
            { error = "上料计划超过冲床容量，或冲床进料口跨越原料边界"; return false; }
            total += amounts[i];
        }
        if (total > Remaining) { error = "原料堆库存不足"; return false; }
        for (int i = 0; i < amounts.Length; i++)
            for (int n = 0; n < amounts[i]; n++)
            {
                var part = stock[stock.Count - 1]; stock.RemoveAt(stock.Count - 1);
                part.SetGroundCollision(false); part.transform.rotation = Quaternion.identity; punches[i].Enqueue(part);
            }
        RefreshStock(); Physics.SyncTransforms(); return true;
    }
}
