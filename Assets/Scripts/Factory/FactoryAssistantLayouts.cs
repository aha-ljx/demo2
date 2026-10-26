#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

// Generates legal candidates for verification, not synthetic throughput predictions.
public partial class PlacementSystem
{
    internal FactorySavedPlan GenerateAssistantLayout(int lanes, int punchesPerLane, bool splitOutput, out int[] feed, out string error, int finishGroupSize = 2, int sharedMode = 0, int pitch = 34, int compact = 0, int offset = 24)
    {
        var generator = new AssistantLayoutBuilder(this);
        try { return generator.Build(lanes, punchesPerLane, splitOutput, out feed, out error, finishGroupSize, sharedMode, pitch, compact, offset); }
        finally { generator.Cleanup(); }
    }
    sealed class AssistantLayoutBuilder
    {
        readonly PlacementSystem owner;
        readonly List<GameObject> probes = new List<GameObject>();
        readonly List<GameObject> machines = new List<GameObject>();
        readonly List<FactoryPlanDevice> devices = new List<FactoryPlanDevice>();
        readonly HashSet<Vector2Int> foreign = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> reservedApproach = new HashSet<Vector2Int>();
        readonly HashSet<FactoryDockArea> lateralApproach = new HashSet<FactoryDockArea>();
        readonly Dictionary<Vector2Int, bool> blocked = new Dictionary<Vector2Int, bool>();
        readonly int[] serial = new int[4];
        GameObject railProbe;
        Vector3 anchor;
        public AssistantLayoutBuilder(PlacementSystem builder) { owner = builder; anchor = builder.rawSupply.transform.position; }
        GameObject Probe(int kind)
        {
            var item = owner.CreateEquipment(kind); probes.Add(item); return item;
        }
        void Record(GameObject item, int kind)
        {
            string[] prefixes = { "冲床", "折弯机", "焊接机", "机械臂" };
            devices.Add(new FactoryPlanDevice { kind = kind, offset = item.transform.position - anchor,
                rotation = item.transform.rotation, name = kind < 4 ? prefixes[kind] + (++serial[kind]) : null });
        }
        GameObject Station(int kind, Vector3 center)
        {
            var item = Probe(kind); item.GetComponent<EquipmentGrid>().Snap(center); machines.Add(item); Record(item, kind); return item;
        }
        Vector3 Point(Vector2Int cell) => new Vector3(cell.x * 3 + 1.5f, 0, cell.y * 3 + 1.5f);
        Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 3), Mathf.FloorToInt(p.z / 3));
        bool Blocked(Vector2Int cell)
        {
            if (reservedApproach.Contains(cell)) return true;
            if (foreign.Contains(cell) || foreign.Contains(cell + Vector2Int.left) || foreign.Contains(cell + Vector2Int.right) ||
                foreign.Contains(cell + Vector2Int.up) || foreign.Contains(cell + Vector2Int.down)) return true;
            if (Mathf.Abs(Point(cell).x - anchor.x) > 180 || Mathf.Abs(Point(cell).z - anchor.z) > 200) return true;
            if (blocked.TryGetValue(cell, out bool value)) return value;
            railProbe.GetComponent<EquipmentGrid>().Snap(Point(cell));
            Bounds footprint = railProbe.GetComponent<EquipmentGrid>().Footprint;
            Bounds full = EquipmentGrid.VisualBounds(railProbe);
            Vector3 center = Point(cell); center.y = owner.production.FinishedArea.Footprint.center.y;
            value = owner.production.FinishedArea.Footprint.Contains(center) || owner.rawSupply.Overlaps(full) || full.Intersects(owner.playerCollider.bounds);
            foreach (var machine in machines) if (!value && FactoryProduction.HardwareOverlap(railProbe, machine)) value = true;
            blocked[cell] = value; return value;
        }
        Vector2Int Terminal(FactoryDockArea area, Vector3 dock)
        {
            Vector2Int center = Cell(dock), best = center; float score = float.PositiveInfinity;
            for (int x = center.x - 4; x <= center.x + 4; x++)
                for (int y = center.y - 4; y <= center.y + 4; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (Blocked(cell)) continue;
                    railProbe.GetComponent<EquipmentGrid>().Snap(Point(cell));
                    if (!area.TryConnection(railProbe.GetComponent<EquipmentGrid>().Footprint, out _)) continue;
                    float distance = (Point(cell) - new Vector3(dock.x, 0, dock.z)).sqrMagnitude;
                    if (distance < score) { score = distance; best = cell; }
                }
            if (float.IsInfinity(score)) throw new InvalidOperationException("停靠区域没有可用滑槽接入：" + area.name + " kind=" + area.GetComponentInParent<FactoryPorts>()?.Kind + " dock=" + area.Footprint.center + " foreign=" + foreign.Count);
            return best;
        }
        List<Vector2Int> Route(Vector2Int start, Vector2Int end)
        {
            var open = new List<Vector2Int> { start }; var cost = new Dictionary<Vector2Int, int> { [start] = 0 };
            var previous = new Dictionary<Vector2Int, Vector2Int>();
            var directions = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
            for (int iteration = 0; open.Count > 0 && iteration < 12000; iteration++)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (cost[open[i]] + Distance(open[i], end) < cost[open[best]] + Distance(open[best], end)) best = i;
                var node = open[best]; open.RemoveAt(best);
                if (node == end)
                {
                    var path = new List<Vector2Int> { node };
                    while (previous.TryGetValue(node, out var parent)) { node = parent; path.Add(node); }
                    return path;
                }
                foreach (var direction in directions)
                {
                    var next = node + direction;
                    if (Blocked(next)) continue;
                    int nextCost = cost[node] + 1;
                    if (cost.TryGetValue(next, out int old) && old <= nextCost) continue;
                    cost[next] = nextCost; previous[next] = node;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            throw new InvalidOperationException("未找到独立且无碰撞的滑槽路径");
        }
        int Distance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        void Network(List<FactoryDockArea> areas, List<Vector3> docks, int approach = 0)
        {
            blocked.Clear(); var cells = new HashSet<Vector2Int>();
            Vector2Int first = Terminal(areas[0], docks[0] + Approach(areas[0], approach)); cells.Add(first);
            for (int i = 1; i < areas.Count; i++)
            {
                var end = Terminal(areas[i], docks[i] + Approach(areas[i], approach)); var start = first; int nearest = int.MaxValue;
                foreach (var cell in cells) if (Distance(cell, end) < nearest) { nearest = Distance(cell, end); start = cell; }
                foreach (var cell in Route(start, end)) cells.Add(cell);
            }
            foreach (var cell in cells)
            {
                railProbe.GetComponent<EquipmentGrid>().Snap(Point(cell)); Record(railProbe, 4);
            }
            var arm = Probe(3); bool seated = false;
            foreach (var cell in cells)
            {
                railProbe.GetComponent<EquipmentGrid>().Snap(Point(cell)); railProbe.GetComponent<FactoryRail>().Measure();
                arm.GetComponent<EquipmentGrid>().Snap(Point(cell));
                FactoryArm.AlignToRail(arm.transform, railProbe.GetComponent<FactoryRail>(), Point(cell));
                bool overlap = false;
                foreach (var machine in machines)
                    if (FactoryProduction.HardwareOverlap(arm, machine)) { overlap = true; break; }
                if (!overlap) { seated = true; break; }
            }
            if (!seated) throw new InvalidOperationException("没有安全的机械臂初始位置");
            Record(arm, 3);
            foreach (var cell in cells) foreign.Add(cell);
        }
        Vector3 Approach(FactoryDockArea area, int side) => (lateralApproach.Contains(area) ? Vector3.left : Vector3.forward) * side * 7;
        void ReserveOtherApproach(GameObject station)
        {
            if (station == null) return;
            var ports = station.GetComponent<FactoryPorts>();
            var cell = Terminal(ports.DockArea, ports.DockPosition + Approach(ports.DockArea, -1));
            reservedApproach.Add(cell); reservedApproach.Add(cell + Vector2Int.left); reservedApproach.Add(cell + Vector2Int.right);
            reservedApproach.Add(cell + Vector2Int.up); reservedApproach.Add(cell + Vector2Int.down);
        }
        Vector3 ReceivingDock(int edge)
        {
            var bounds = owner.production.FinishedArea.Footprint;
            Vector3[] directions = { Vector3.left, Vector3.back, Vector3.right, Vector3.forward };
            return bounds.center + directions[edge % 4] * (bounds.extents.x + .1f);
        }
        public FactorySavedPlan Build(int lanes, int punchesPerLane, bool splitOutput, out int[] feed, out string error, int finishGroupSize, int sharedMode, int pitch, int compact, int offset)
        {
            feed = null; error = null; railProbe = Probe(4);
            var stations = new List<List<GameObject>>();
            float boundary = owner.GetComponent<FactoryLevelLayout>().BoundaryX;
            try
            {
                if (sharedMode == 8)
                {
                    Station(0, new Vector3(boundary - 13, 0, anchor.z - 24)); feed = new[] { 10 };
                    return new FactorySavedPlan { name = "冲床无下游阻塞节拍测量", equipmentScale = owner.equipmentScale,
                        cost = equipmentPrices[0], devices = devices.ToArray() };
                }
                float middle = anchor.z - offset - (lanes - 1) * pitch * .5f;
                GameObject sharedPunch = (sharedMode & 1) != 0 ? Station(0, new Vector3(boundary - 13, 0, middle)) : null;
                GameObject sharedBend = null;
                for (int lane = 0; lane < lanes; lane++)
                {
                    float z = anchor.z - offset - lane * pitch;
                    var line = new List<GameObject>();
                    if (sharedPunch != null) line.Add(sharedPunch);
                    else for (int p = 0; p < punchesPerLane; p++)
                        line.Add(Station(0, new Vector3(boundary - 13, 0, z - p * 18)));
                    var punchBounds = line[0].GetComponent<EquipmentGrid>().Footprint;
                    if ((sharedMode & 2) != 0 && sharedBend == null)
                    {
                        sharedBend = Station(1, new Vector3(punchBounds.max.x + (compact > 0 ? 5 : 7), 0, middle));
                        lateralApproach.Add(sharedBend.GetComponent<FactoryPorts>().DockArea);
                    }
                    var bend = sharedBend != null ? sharedBend : Station(1, new Vector3(punchBounds.max.x + (compact > 0 ? 5 : 7), 0, z)); line.Add(bend);
                    var bendBounds = bend.GetComponent<EquipmentGrid>().Footprint;
                    line.Add(Station(2, new Vector3(bendBounds.max.x + (compact > 0 ? 8 : 10), 0, z)));
                    stations.Add(line);
                }
                ReserveOtherApproach(sharedPunch); ReserveOtherApproach(sharedBend);
                var finishAreas = new List<FactoryDockArea>(); var finishDocks = new List<Vector3>();
                for (int lane = 0; lane < lanes; lane++)
                {
                    if (lane > 0) reservedApproach.Clear();
                    var areas = new List<FactoryDockArea>(); var docks = new List<Vector3>();
                    foreach (var station in stations[lane]) { var ports = station.GetComponent<FactoryPorts>(); areas.Add(ports.DockArea); docks.Add(ports.DockPosition); }
                    var weld = stations[lane][stations[lane].Count - 1].GetComponent<FactoryPorts>();
                    areas.Add(weld.ConveyorDockArea); docks.Add(weld.ConveyorDockPosition);
                    if (splitOutput) { finishAreas.Add(weld.PickupDockArea); finishDocks.Add(weld.FinishedPickupDock); }
                    else
                    {
                        areas.Add(weld.PickupDockArea); docks.Add(weld.FinishedPickupDock);
                        areas.Add(owner.production.FinishedArea.DockArea); docks.Add(ReceivingDock(lane));
                    }
                    Network(areas, docks, sharedMode == 0 ? 0 : lane == 0 ? 1 : -1);
                }
                if (splitOutput)
                {
                    finishGroupSize = Mathf.Clamp(finishGroupSize, 1, 2);
                    for (int group = 0; group * finishGroupSize < finishAreas.Count; group++)
                    {
                        var areas = new List<FactoryDockArea>(); var docks = new List<Vector3>();
                        for (int i = group * finishGroupSize; i < Mathf.Min(group * finishGroupSize + finishGroupSize, finishAreas.Count); i++)
                        { areas.Add(finishAreas[i]); docks.Add(finishDocks[i]); }
                        areas.Add(owner.production.FinishedArea.DockArea); docks.Add(ReceivingDock(group));
                        Network(areas, docks);
                    }
                }
                feed = new int[sharedPunch != null ? 1 : lanes * punchesPerLane];
                for (int i = 0; i < feed.Length; i++) feed[i] = 10 / feed.Length + (i < 10 % feed.Length ? 1 : 0);
                long cost = 0; foreach (var device in devices) cost += equipmentPrices[device.kind];
                return new FactorySavedPlan { name = lanes + "线" + (sharedMode != 0 ? "共用" + (sharedPunch != null ? "冲床" : "") + (sharedBend != null ? "折弯" : "") : "") + (splitOutput ? "分工" : "单臂") + "方案", equipmentScale = owner.equipmentScale,
                    cost = cost, devices = devices.ToArray() };
            }
            catch (InvalidOperationException e)
            {
                error = e.Message;
                foreach (var m in machines) { var p = m.GetComponent<FactoryPorts>(); error += "\nkind=" + p.Kind + " body=" + m.GetComponent<EquipmentGrid>().Footprint + " dock=" + p.DockArea.Footprint; }
                return null;
            }
        }
        public void Cleanup() { foreach (var probe in probes) { probe.SetActive(false); UnityEngine.Object.Destroy(probe); } }
    }
}
#endif
