using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public sealed class FactoryAssistantProfile
{
    public string key;
    public FactorySavedPlan plan;
    public int[] feed;
    public float[] receivedAt;
    public float simulationStep = .05f;
    public string[] sharedUsage;
    public float[] punchCompletedAt;
}

[Serializable]
public sealed class FactoryAssistantCatalog
{
    public int version = 1;
    public string rules = FactoryAssistantSolver.Rules;
    public float scale, travel = 3, pickup = 3, rotation = 2;
    public float margin = 10;
    public FactoryAssistantProfile[] profiles;
}

public sealed class FactoryAssistantAnswer
{
    public int quantity;
    public float deadline;
    public int compared;
    public string message;
    public FactoryAssistantProfile best;
    public float completion;
    public long cost;
    public bool provenImpossible;
    public bool sharedPunchRequired, maximizeQuantity;
}

// A finite, reproducible optimization over layouts validated by the real game.
// No language model is allowed to invent a layout, timing, or a global optimality proof.
public static class FactoryAssistantSolver
{
    public const string Rules = "level1-raw10-punchhold30-arm332-product2000-fixed60-v4";
    static string Digits(string value)
    {
        const string chinese = "零一二三四五六七八九";
        value = value.Replace('两', '二');
        if (int.TryParse(value, out _)) return value;
        if (value == "两") return "2";
        int hundred = value.IndexOf('百'), ten = value.IndexOf('十'), result = 0;
        if (hundred >= 0) { result += chinese.IndexOf(value[0]) * 100; value = value.Substring(hundred + 1); }
        ten = value.IndexOf('十');
        if (ten >= 0)
        {
            result += (ten == 0 ? 1 : Mathf.Max(0, chinese.IndexOf(value[ten - 1]))) * 10;
            if (ten + 1 < value.Length) result += Mathf.Max(0, chinese.IndexOf(value[value.Length - 1]));
        }
        else foreach (char c in value) if (chinese.IndexOf(c) > 0) result += chinese.IndexOf(c);
        return result.ToString(CultureInfo.InvariantCulture);
    }
    public static bool Parse(string question, out int quantity, out float seconds, out string error)
    {
        quantity = 0; seconds = FactoryLevelSession.Duration; error = null;
        if (string.IsNullOrWhiteSpace(question)) { error = "请输入目标，例如：600秒内得到10件产物，成本最低。"; return false; }
        if (Regex.IsMatch(question, @"-\s*\d") || question.Contains("半") || question.Contains("毫秒") || question.Contains("点"))
        { error = "请使用明确的正数时间和整数件数，例如“1分30秒内得到2件”。"; return false; }
        string text = Regex.Replace(question, "[零一二三四五六七八九十百两]+", m => Digits(m.Value));
        var count = Regex.Match(text, @"(?<![\d.\-])(?<n>\d+)\s*(件|个)");
        if (!count.Success || !int.TryParse(count.Groups["n"].Value, out quantity))
        { error = "请写明产物数量，例如“10件”；未写时间时按第一关600秒计算。"; return false; }
        var minutes = Regex.Match(text, @"(?<n>\d+(?:\.\d+)?)\s*(分钟|分|min)", RegexOptions.IgnoreCase);
        var second = Regex.Match(text, @"(?<n>\d+(?:\.\d+)?)\s*(秒|s)", RegexOptions.IgnoreCase);
        var hours = Regex.Match(text, @"(?<n>\d+(?:\.\d+)?)\s*(小时|h)", RegexOptions.IgnoreCase);
        if (minutes.Success || second.Success || hours.Success)
        {
            seconds = 0;
            float value;
            if (minutes.Success && float.TryParse(minutes.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) seconds += value * 60;
            if (second.Success && float.TryParse(second.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) seconds += value;
            if (hours.Success && float.TryParse(hours.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) seconds += value * 3600;
        }
        if (quantity <= 0 || seconds <= 0 || float.IsInfinity(seconds) || float.IsNaN(seconds))
        { error = "目标数量和时间必须大于0。"; return false; }
        if (seconds > FactoryLevelSession.Duration)
        { error = "第一关总时长为600秒，请设置不超过600秒的目标。"; return false; }
        return true;
    }
    public static FactoryAssistantAnswer SolveBestSharedPunch(float deadline, FactoryAssistantCatalog catalog, float scale, float travel, float pickup, float rotation)
    {
        FactoryAssistantAnswer last = null;
        for (int quantity = FactoryLevelSession.RawCount; quantity > 0; quantity--)
        {
            last = Solve(quantity, deadline, catalog, scale, travel, pickup, rotation, true); last.maximizeQuantity = true;
            if (last.best != null || last.provenImpossible) return last;
        }
        return last;
    }
    public static FactoryAssistantAnswer Solve(int quantity, float deadline, FactoryAssistantCatalog catalog, float scale,
        float travel, float pickup, float rotation, bool requireSharedPunch = false)
    {
        var answer = new FactoryAssistantAnswer { quantity = quantity, deadline = deadline, sharedPunchRequired = requireSharedPunch };
        if (quantity <= 0 || deadline <= 0 || float.IsNaN(deadline) || float.IsInfinity(deadline))
        { answer.message = "目标数量和时间必须是有效的正数。"; return answer; }
        if (quantity > FactoryLevelSession.RawCount)
        {
            answer.provenImpossible = true; answer.message = "无法完成：第一关只有10块原料，每块最多形成1件产物。"; return answer;
        }
        if (deadline < 30)
        {
            answer.provenImpossible = true; answer.message = "无法完成：仅冲压停留就需要30秒，尚未包括送料、折弯、焊接和入库。"; return answer;
        }
        if (catalog == null || catalog.version != 1 || catalog.rules != Rules || catalog.profiles == null ||
            float.IsNaN(catalog.scale) || float.IsInfinity(catalog.scale) || float.IsNaN(catalog.margin) ||
            catalog.margin < 0 || catalog.margin > 60 ||
            Mathf.Abs(catalog.scale - scale) > .001f || Mathf.Abs(catalog.travel - travel) > .001f ||
            Mathf.Abs(catalog.pickup - pickup) > .001f || Mathf.Abs(catalog.rotation - rotation) > .001f)
        { answer.message = "当前规则下暂无可用的已验证方案，不能据此断言目标无法完成。"; return answer; }
        foreach (var profile in catalog.profiles)
        {
            if (profile == null || !FactoryPlanStorage.Validate(profile.plan, out _) || profile.receivedAt == null ||
                profile.feed == null || profile.feed.Length == 0) continue;
            int total = 0, punches = 0; bool valid = profile.receivedAt.Length <= FactoryLevelSession.RawCount;
            foreach (int feed in profile.feed) { total += feed; if (feed < 0 || feed > FactoryStation.RawInputCapacity) valid = false; }
            foreach (var device in profile.plan.devices) if (device.kind == 0) punches++;
            if (requireSharedPunch && (punches != 1 || profile.sharedUsage == null ||
                !Array.Exists(profile.sharedUsage, x => x != null && x.StartsWith("冲床", StringComparison.Ordinal)))) continue;
            float previous = 0;
            foreach (float arrival in profile.receivedAt)
            {
                if (float.IsNaN(arrival) || float.IsInfinity(arrival) || arrival < 30 || arrival < previous || arrival > FactoryLevelSession.Duration) valid = false;
                previous = arrival;
            }
            if (!valid || total != FactoryLevelSession.RawCount || punches != profile.feed.Length || Mathf.Abs(profile.plan.equipmentScale - scale) > .001f) continue;
            answer.compared++;
            if (profile.receivedAt.Length < quantity) continue;
            float completion = profile.receivedAt[quantity - 1] + catalog.margin;
            if (completion > deadline) continue;
            long cost = 0; foreach (var device in profile.plan.devices) cost += PlacementSystem.EquipmentPrice(device.kind);
            if (answer.best == null || cost < answer.cost || cost == answer.cost && completion < answer.completion)
            { answer.best = profile; answer.completion = completion; answer.cost = cost; }
        }
        answer.message = answer.best != null
            ? "可以：已验证方案能在目标时间内完成入库。\n这是当前已验证候选中的最低成本方案，不代表所有可能布局的全局最优。"
            : "当前已验证候选中没有满足该时间目标的方案。\n这属于暂未找到，不能证明所有布局都无法完成；可放宽时间后重试。";
        if (requireSharedPunch && answer.best == null) answer.message = "单冲床双轨共用的已验证候选中，尚未找到满足该数量和期限的方案；没有改选双冲床布局。";
        return answer;
    }
}
