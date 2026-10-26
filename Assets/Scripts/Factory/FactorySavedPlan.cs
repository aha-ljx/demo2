using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class FactoryPlanDevice
{
    public int kind;
    public Vector3 offset;
    public Quaternion rotation;
    public string name;
}

[Serializable]
public sealed class FactorySavedPlan
{
    [NonSerialized] internal string sourceFile;
    public int version = 1;
    public string level = "first-level";
    public string id;
    public string name;
    public string createdUtc;
    public float equipmentScale;
    public int resultCount;
    public long resultNet;
    public long cost;
    public FactoryPlanDevice[] devices;
}

public static class FactoryPlanStorage
{
    // Audits use an isolated project-local directory, never the player's saves.
    internal static string DirectoryOverride;
    public static string DirectoryPath => string.IsNullOrEmpty(DirectoryOverride)
        ? Path.Combine(Application.persistentDataPath, "FactoryPlansV1") : DirectoryOverride;

    public static bool Validate(FactorySavedPlan plan, out string error)
    {
        error = "方案文件不完整或版本不兼容";
        if (plan == null || plan.version != 1 || plan.level != "first-level" || string.IsNullOrWhiteSpace(plan.name) ||
            plan.name.Length > 48 || !Finite(plan.equipmentScale) || plan.equipmentScale <= 0 ||
            plan.devices == null || plan.devices.Length == 0 || plan.devices.Length > 512) return false;
        var names = new HashSet<string>();
        foreach (var device in plan.devices)
        {
            if (device == null || device.kind < 0 || device.kind > 4 || !Finite(device.offset.x) || !Finite(device.offset.y) ||
                !Finite(device.offset.z) || device.offset.sqrMagnitude > 100000000 ||
                !Finite(device.rotation.x) || !Finite(device.rotation.y) || !Finite(device.rotation.z) || !Finite(device.rotation.w)) return false;
            float norm = device.rotation.x * device.rotation.x + device.rotation.y * device.rotation.y +
                device.rotation.z * device.rotation.z + device.rotation.w * device.rotation.w;
            if (Mathf.Abs(norm - 1) > .01f) return false;
            if (device.kind != 4 && (!FactoryStatistics.ValidSavedName(device.kind, device.name, out _) || !names.Add(device.name))) return false;
        }
        error = null; return true;
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool Save(FactorySavedPlan plan, out string error)
    {
        if (!Validate(plan, out error)) return false;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            plan.id = Guid.NewGuid().ToString("N");
            plan.createdUtc = DateTime.UtcNow.ToString("O");
            string destination = Path.Combine(DirectoryPath, plan.id + ".json");
            string temporary = destination + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(plan, true), new UTF8Encoding(false));
            File.Move(temporary, destination);
            plan.sourceFile = Path.GetFullPath(destination);
            error = null; return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
        { error = "方案保存失败，请检查本机存储空间或文件权限"; return false; }
    }
    public static bool Delete(FactorySavedPlan plan, out string error)
    {
        error = "无法删除该方案，请重新打开方案列表后再试";
        if (plan == null || string.IsNullOrEmpty(plan.sourceFile)) return false;
        try
        {
            string root = Path.GetFullPath(DirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string file = Path.GetFullPath(plan.sourceFile);
            if (!string.Equals(Path.GetDirectoryName(file), root, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(file)) { error = "该方案已不存在，列表已刷新"; return false; }
            File.Delete(file);
            error = null; return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
        { error = "删除失败，请检查该方案文件的访问权限"; return false; }
    }
    public static List<FactorySavedPlan> ReadAll(out string message)
    {
        var plans = new List<FactorySavedPlan>(); message = null;
        try
        {
            if (!Directory.Exists(DirectoryPath)) return plans;
            foreach (string file in Directory.GetFiles(DirectoryPath, "*.json"))
            {
                try
                {
                    if (new FileInfo(file).Length > 2097152) { message = "部分方案无法读取，已跳过"; continue; }
                    var plan = JsonUtility.FromJson<FactorySavedPlan>(File.ReadAllText(file));
                    if (Validate(plan, out _)) { plan.sourceFile = Path.GetFullPath(file); plans.Add(plan); }
                    else message = "部分方案无法读取，已跳过";
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
                { message = "部分方案无法读取，已跳过"; }
            }
            plans.Sort((a, b) => string.CompareOrdinal(b.createdUtc, a.createdUtc));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        { message = "无法读取本机方案，请检查文件权限"; }
        return plans;
    }
}
