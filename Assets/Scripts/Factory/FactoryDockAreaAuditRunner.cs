#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class FactoryDockAreaAuditRunner : MonoBehaviour
{
    readonly StringBuilder report = new StringBuilder();
    FactoryProduction system;
    bool finished, failed;
    float started;
    void OnEnable() { started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) { failed = true; report.AppendLine(message + "\n" + stack); }
    }
    void Update() { if (!finished && (failed || Time.realtimeSinceStartup - started > 180)) Finish(false); }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); }
    IEnumerator Start()
    {
        system = new GameObject("Dock audit production").AddComponent<FactoryProduction>();
        system.enabled = false; system.armTravelMultiplier = 12;
        for (int yaw = 0; yaw < 360; yaw += 90)
        for (int kind = 0; kind < 3; kind++)
        {
            var model = FactoryProductionAuditRunner.Model(kind, new Vector3(100, 0, 100), yaw);
            model.AddComponent<FactoryStation>().Initialize();
            var ports = model.GetComponent<FactoryPorts>();
            yield return Test(ports.DockArea, ports.DockPosition, yaw == 0);
            if (kind == 2)
            {
                yield return Test(ports.ConveyorDockArea, ports.ConveyorDockPosition, yaw == 0);
                yield return Test(ports.PickupDockArea, ports.FinishedPickupDock, yaw == 0);
                if (yaw == 0) Snapshot(model);
            }
            // All regions must follow a module that is moved after creation.
            model.transform.position += new Vector3(3, 0, -4);
            Check(ports.DockArea.Matches(ports.DockPosition), "dock region follows module movement at yaw " + yaw);
            model.transform.RotateAround(model.transform.position, Vector3.up, 90);
            Check(ports.DockArea.Matches(ports.DockPosition), "dock region follows module rotation at yaw " + yaw);
            DestroyImmediate(model);
        }
        system.CreateFinishedArea(new Vector3(100, 0, 100), 2.1224f);
        yield return Test(system.FinishedArea.DockArea, system.FinishedArea.DockPosition(1.2f), true);
        Finish(true);
    }

    IEnumerator Test(FactoryDockArea area, Vector3 dock, bool travel)
    {
        Check(area != null && area.GetComponent<LineRenderer>().positionCount == 4 && area.GetComponent<MeshRenderer>() != null,
            "filled square marker: " + area.name);
        Check(system.DockAreaAt(dock) == area, "runtime resolves the displayed docking area: " + area.name);
        var railObject = FactoryProductionAuditRunner.Model(4, dock, 0);
        var rail = railObject.GetComponent<FactoryRail>(); system.Rails.Add(rail);
        var armObject = FactoryProductionAuditRunner.Model(3, dock, 0);
        MoveRail(rail, area, Vector3.right, 0);
        var arm = armObject.AddComponent<FactoryArm>(); arm.Initialize(rail);
        foreach (Vector3 side in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            MoveRail(rail, area, side, 0);
            FactoryArm.AlignToRail(arm.transform, rail, rail.GetComponent<EquipmentGrid>().Footprint.center);
            Bounds track = rail.GetComponent<EquipmentGrid>().Footprint;
            Check(!Contains(track, dock), "test rail touches the area without covering its center");
            Check(arm.CanReach(dock, out var path), "edge contact reaches " + area.name + " from " + side);
            CheckPath(path, track, area.Footprint);
            MoveRail(rail, area, side, .05f);
            Check(!arm.CanReach(dock, out _), "visible gap blocks " + area.name + " from " + side);
        }
        MoveRail(rail, area, new Vector3(1, 0, 1), 0);
        Check(arm.CanReach(dock, out var cornerPath), "corner contact reaches docking area");
        CheckPath(cornerPath, rail.GetComponent<EquipmentGrid>().Footprint, area.Footprint);

        MoveRail(rail, area, Vector3.right, 0);
        var remoteObject = FactoryProductionAuditRunner.Model(4, dock + Vector3.right * 40, 0);
        var remote = remoteObject.GetComponent<FactoryRail>(); system.Rails.Add(remote); system.Graph.Build(system.Rails);
        var remoteArmObject = FactoryProductionAuditRunner.Model(3, remote.GetComponent<EquipmentGrid>().Footprint.center, 0);
        var remoteArm = remoteArmObject.AddComponent<FactoryArm>(); remoteArm.Initialize(remote);
        Check(!remoteArm.CanReach(dock, out _), "a different disconnected rail cannot use the contact");
        DestroyImmediate(remoteArmObject); system.Rails.Remove(remote); DestroyImmediate(remoteObject); system.Graph.Build(system.Rails);

        if (travel)
        {
            FactoryArm.AlignToRail(arm.transform, rail, rail.GetComponent<EquipmentGrid>().Footprint.center);
            var movement = (IEnumerator)typeof(FactoryArm).GetMethod("TravelTo", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(arm, new object[] { dock, Quaternion.identity });
            yield return arm.StartCoroutine(movement);
            bool receiving = system.FinishedArea != null && area == system.FinishedArea.DockArea;
            Check(receiving ? Contains(rail.GetComponent<EquipmentGrid>().Footprint, arm.TrackPosition) : Vector3.Distance(arm.TrackPosition, dock) < .005f,
                "arm reaches its destination, remaining on the rail for final product drop-off: " + area.name);
        }
        if (system.FinishedArea != null && area == system.FinishedArea.DockArea)
        {
            MoveRail(rail, area, Vector3.right, 0);
            FactoryArm.AlignToRail(arm.transform, rail, rail.GetComponent<EquipmentGrid>().Footprint.center);
            var insideObject = FactoryProductionAuditRunner.Model(4, dock, 0);
            var insideRail = insideObject.GetComponent<FactoryRail>();
            Vector3 insideCenter = rail.GetComponent<EquipmentGrid>().Footprint.center - Vector3.right * 3;
            var insideBounds = insideObject.GetComponent<EquipmentGrid>().Footprint;
            insideObject.transform.position += new Vector3(insideCenter.x - insideBounds.center.x, 0, insideCenter.z - insideBounds.center.z);
            insideRail.Measure(); system.Rails.Add(insideRail); system.Graph.Build(system.Rails);
            bool reachesEdge = arm.CanReach(dock, out var edgePath);
            Check(reachesEdge && !area.ContainsXZ(edgePath[edgePath.Count - 1]),
                "rail extending inside the receiving zone still stops the carrier at the outer edge; reachable=" + reachesEdge +
                ", bounds=" + area.Footprint + ", path=" + (edgePath != null ? string.Join(" -> ", edgePath) : "none"));
            system.Rails.Remove(insideRail); DestroyImmediate(insideObject); system.Graph.Build(system.Rails);
            foreach (Vector3 side in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                MoveRail(rail, area, side, 0);
                Vector3 offset = Mathf.Abs(side.x) > .5f ? Vector3.forward : Vector3.right;
                rail.transform.position += offset * area.Footprint.extents.x * .75f;
                rail.Measure(); system.Graph.Build(system.Rails);
                FactoryArm.AlignToRail(arm.transform, rail, rail.GetComponent<EquipmentGrid>().Footprint.center);
                Vector3 pickup = arm.TrackPosition;
                Check(arm.CanReach(dock, out var arrival), "off-center outer-edge arrival reaches storage");
                CheckClearStorage(arrival);
                yield return arm.StartCoroutine((IEnumerator)typeof(FactoryArm).GetMethod("TravelTo", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(arm, new object[] { dock, Quaternion.identity }));
                Check(arm.CanReach(pickup, out var departure), "the same arm can return to its rail for the next product");
                CheckClearStorage(departure);
                yield return arm.StartCoroutine((IEnumerator)typeof(FactoryArm).GetMethod("TravelTo", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(arm, new object[] { pickup, Quaternion.identity }));
                Check(Vector3.Distance(arm.TrackPosition, pickup) < .005f, "off-center arrival and departure complete at side " + side);
            }
        }
        DestroyImmediate(armObject); system.Rails.Remove(rail); DestroyImmediate(railObject); system.Graph.Build(system.Rails);
    }
    void CheckClearStorage(List<Vector3> path)
    {
        bool clear = true;
        foreach (var collider in system.FinishedArea.GetComponentsInChildren<BoxCollider>())
        {
            Bounds bounds = collider.bounds; bounds.Expand(new Vector3(.6f, 0, .6f));
            for (int i = 1; i < path.Count; i++)
                for (int sample = 0; sample <= 32; sample++)
                    clear &= !Contains(bounds, Vector3.Lerp(path[i - 1], path[i], sample / 32f));
        }
        Check(clear, "storage approach and return leave clearance around the real platforms");
    }
    void MoveRail(FactoryRail rail, FactoryDockArea area, Vector3 side, float gap)
    {
        Bounds a = area.Footprint, b = rail.GetComponent<EquipmentGrid>().Footprint;
        Vector3 target = a.center + new Vector3(side.x * (a.extents.x + b.extents.x + gap), 0, side.z * (a.extents.z + b.extents.z + gap));
        rail.transform.position += new Vector3(target.x - b.center.x, 0, target.z - b.center.z);
        rail.Measure(); system.Graph.Build(system.Rails);
    }
    static bool Contains(Bounds bounds, Vector3 point) => point.x >= bounds.min.x - .002f && point.x <= bounds.max.x + .002f &&
        point.z >= bounds.min.z - .002f && point.z <= bounds.max.z + .002f;
    void CheckPath(List<Vector3> path, Bounds rail, Bounds area)
    {
        bool inside = true;
        for (int i = 1; i < path.Count; i++)
            for (int sample = 0; sample <= 32; sample++)
            {
                Vector3 p = Vector3.Lerp(path[i - 1], path[i], sample / 32f);
                inside &= Contains(rail, p) || Contains(area, p);
            }
        Check(inside, "all approach waypoints stay in the rail or green area");
    }
    void Snapshot(GameObject model)
    {
        var cameraObject = new GameObject("Dock preview camera"); var camera = cameraObject.AddComponent<Camera>();
        Bounds bounds = EquipmentGrid.VisualBounds(model);
        camera.transform.SetPositionAndRotation(bounds.center + Vector3.up * 40, Quaternion.Euler(90, 0, 0));
        camera.orthographic = true; camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) + 5;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .14f, .16f);
        RenderSettings.ambientLight = new Color(.7f, .7f, .7f);
        foreach (var label in model.GetComponentsInChildren<TMPro.TMP_Text>()) label.transform.rotation = camera.transform.rotation;
        var texture = new RenderTexture(1400, 1000, 24); camera.targetTexture = texture; camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var picture = new Texture2D(1400, 1000, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); picture.Apply();
        Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/factory-dock-areas.png", picture.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        Destroy(picture); Destroy(texture); Destroy(cameraObject);
    }
    void Finish(bool ok)
    {
        if (finished) return; finished = true;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-dock-area-audit.txt", report + (ok ? "PASS ALL\n" : "FAIL\n"));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
#endif
