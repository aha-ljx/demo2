using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FactoryArmPlacementAudit
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    const string Pending = "FactoryArmPlacementAudit.Pending";
    static double verifyAt;
    static FactoryArmPlacementAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            verifyAt = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += Verify;
        };
    }

    [MenuItem("Tools/Factory/Run Arm Placement Audit")]
    public static void Validate()
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
        Action<bool, string> require = (ok, message) => { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); };
        try
        {
            var placement = UnityEngine.Object.FindObjectOfType<PlacementSystem>();
            var system = FactoryProduction.Instance;
            var placed = (List<GameObject>)typeof(PlacementSystem).GetField("placed", Private).GetValue(placement);
            var kinds = (Dictionary<GameObject, int>)typeof(PlacementSystem).GetField("placedTypes", Private).GetValue(placement);
            var align = typeof(PlacementSystem).GetMethod("AlignVisualToGround", Private);
            var check = typeof(PlacementSystem).GetMethod("CanPlaceHere", Private);
            typeof(PlacementSystem).GetField("selected", Private).SetValue(placement, 3);
            for (int kind = -1; kind < 3; kind++)
            for (int yaw = 0; yaw < 360; yaw += 90)
            {
                string context = "kind=" + kind + " yaw=" + yaw;
                var station = kind < 0 ? null : FactoryProductionAuditRunner.Model(kind, new Vector3(100, 0, 100), yaw);
                Vector3 target = station == null ? new Vector3(100, 0, 100) : station.GetComponent<FactoryPorts>().DockPosition;
                var railObject = FactoryProductionAuditRunner.Model(4, target, yaw);
                var rail = railObject.GetComponent<FactoryRail>();
                system.Rails.Add(rail); system.Graph.Build(system.Rails);
                placed.Add(railObject); kinds[railObject] = 4;
                if (station != null) { placed.Add(station); kinds[station] = kind; }
                var preview = FactoryProductionAuditRunner.Model(3, target, 0);
                typeof(PlacementSystem).GetField("groundPreview", Private).SetValue(placement, preview);
                align.Invoke(placement, new object[] { preview, target });
                require((bool)check.Invoke(placement, new object[] { preview, null }), "rail preview can be placed: " + context);
                require(Vector3.Distance(FactoryArm.BaseCenter(preview.transform), rail.Closest(target) + Vector3.up * FactoryArm.BaseCenter(preview.transform).y) < .005f,
                    "preview base sits on the rail centerline: " + context);
                var foot = FactoryMotion.Node(preview.transform, 872);
                require(Mathf.Abs(FactoryMotion.Bounds(foot).min.y - rail.SurfaceY) < .005f, "preview foot rests on the rail surface: " + context);
                Vector3 before = preview.transform.position;
                typeof(PlacementSystem).GetMethod("PlaceItem", Private).Invoke(placement, null);
                var committed = placed[placed.Count - 1];
                require(committed.GetComponent<FactoryArm>() != null && system.Arms.Count == 1, "click registers a working arm: " + context);
                require(Vector3.Distance(before, committed.transform.position) < .005f, "committed pose matches the preview: " + context);
                require(!(bool)check.Invoke(placement, new object[] { preview, null }), "second arm on the same rail is rejected: " + context);
                placed.Remove(committed); kinds.Remove(committed); UnityEngine.Object.DestroyImmediate(committed);

                if (kind == -1)
                {
                    var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obstruction.transform.position = FactoryArm.BaseCenter(preview.transform);
                    obstruction.transform.localScale = new Vector3(1, 2, 1);
                    obstruction.AddComponent<EquipmentGrid>().Initialize(1);
                    placed.Add(obstruction); kinds[obstruction] = 0;
                    require(!(bool)check.Invoke(placement, new object[] { preview, null }), "actual hardware still blocks placement: " + context);
                    placed.Remove(obstruction); kinds.Remove(obstruction); UnityEngine.Object.DestroyImmediate(obstruction);
                    align.Invoke(placement, new object[] { preview, target + Vector3.right * 50 });
                    require(!(bool)check.Invoke(placement, new object[] { preview, null }), "off-rail placement is rejected: " + context);
                }
                typeof(PlacementSystem).GetField("groundPreview", Private).SetValue(placement, null);
                UnityEngine.Object.DestroyImmediate(preview);
                placed.Clear(); kinds.Clear(); system.Rails.Clear(); system.Graph.Build(system.Rails);
                UnityEngine.Object.DestroyImmediate(railObject);
                if (station != null) UnityEngine.Object.DestroyImmediate(station);
            }
            FactoryRailTurnAudit.AssertTopology();
            report.AppendLine("PASS rotated L/T junction topology regression");
            report.AppendLine("PASS ALL"); passed = true;
        }
        catch (Exception error) { report.AppendLine("FAIL " + error); }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-arm-placement-audit.txt", report.ToString());
        Debug.Log(report.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    public static void Diagnose()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        var placement = UnityEngine.Object.FindObjectOfType<PlacementSystem>();
        var system = new GameObject("Placement audit").AddComponent<FactoryProduction>();
        typeof(PlacementSystem).GetField("production", Private).SetValue(placement, system);
        typeof(PlacementSystem).GetField("selected", Private).SetValue(placement, 3);
        var placed = (List<GameObject>)typeof(PlacementSystem).GetField("placed", Private).GetValue(placement);
        var kinds = (Dictionary<GameObject, int>)typeof(PlacementSystem).GetField("placedTypes", Private).GetValue(placement);
        var check = typeof(PlacementSystem).GetMethod("CanPlaceHere", Private);
        var report = new StringBuilder();
        for (int kind = -1; kind < 3; kind++)
        for (int yaw = 0; yaw < 360; yaw += 90)
        {
            GameObject station = kind < 0 ? null : FactoryProductionAuditRunner.Model(kind, new Vector3(100, 0, 100), yaw);
            Vector3 target = station == null ? new Vector3(100, 0, 100) : station.GetComponent<FactoryPorts>().DockPosition;
            var railObject = FactoryProductionAuditRunner.Model(4, target, yaw);
            var rail = railObject.GetComponent<FactoryRail>();
            system.Rails.Clear(); system.Rails.Add(rail); system.Graph.Build(system.Rails);
            placed.Clear(); kinds.Clear(); placed.Add(railObject); kinds[railObject] = 4;
            if (station != null) { placed.Add(station); kinds[station] = kind; }
            var arm = FactoryProductionAuditRunner.Model(3, target, 0);
            bool grounded = (bool)check.Invoke(placement, new object[] { arm, null });
            bool groundedOverlap = station != null && FactoryProduction.HardwareOverlap(arm, station);
            var node = FactoryMotion.Node(arm.transform, 869);
            Bounds bounds = FactoryMotion.Bounds(node);
            var foot = FactoryMotion.Node(arm.transform, 872);
            float bottom = foot != null ? FactoryMotion.Bounds(foot).min.y : bounds.min.y;
            Vector3 projected = rail.Closest(bounds.center);
            arm.transform.position += new Vector3(projected.x - bounds.center.x, rail.SurfaceY - bottom, projected.z - bounds.center.z);
            bool seated = (bool)check.Invoke(placement, new object[] { arm, null });
            report.AppendLine($"kind={kind} yaw={yaw} target={target} grounded={grounded} groundedOverlap={groundedOverlap} seated={seated} seatedOverlap={(station != null && FactoryProduction.HardwareOverlap(arm, station))}");
            UnityEngine.Object.DestroyImmediate(arm); UnityEngine.Object.DestroyImmediate(railObject);
            if (station != null) UnityEngine.Object.DestroyImmediate(station);
        }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-arm-placement-diagnostic.txt", report.ToString());
        Debug.Log(report.ToString());
    }
}
