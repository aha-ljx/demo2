using System.Collections.Generic;
using UnityEngine;

public sealed class FactoryProduction : MonoBehaviour
{
    public static FactoryProduction Instance { get; private set; }
    public readonly List<FactoryStation> Stations = new List<FactoryStation>();
    public readonly List<FactoryRail> Rails = new List<FactoryRail>();
    public readonly List<FactoryArm> Arms = new List<FactoryArm>();
    public readonly FactoryRailGraph Graph = new FactoryRailGraph();
    public FactoryStatistics Statistics { get; private set; }
    public Transform Products { get; private set; }
    public FinishedGoodsArea FinishedArea { get; private set; }
    public int Stored => FinishedArea != null ? FinishedArea.StoredCount : 0;
    public void CreateFinishedArea(Vector3 position, float scale)
    {
        if (FinishedArea != null) return;
        FinishedArea = new GameObject("最终产物放置区").AddComponent<FinishedGoodsArea>();
        FinishedArea.Initialize(position, scale);
    }
    public int Finished;
    [Header("产线机械臂速度倍率")]
    [Min(0.1f)] public float armTravelMultiplier = 3f;
    [Min(0.1f)] public float armPickupMultiplier = 3f;
    [Min(0.1f)] public float armRotationMultiplier = 2f;
    public string Hint { get; private set; } = "冲床须放在黄线远离最终产物区的一侧；原料堆靠近按 F 或鸟瞰点击拾取";
    float nextDispatch;
    void Awake()
    {
        Instance = this; Products = new GameObject("已完成成品").transform;
        Statistics = gameObject.AddComponent<FactoryStatistics>();
        gameObject.AddComponent<FactorySimulation>().Initialize(this);
    }
    public void Message(string message) { Hint = message; }
    public static bool HardwareOverlap(GameObject a, GameObject b)
    {
        foreach (var x in a.GetComponentsInChildren<MeshRenderer>())
        {
            if (!Solid(x)) continue;
            Bounds bounds = x.bounds; bounds.Expand(-0.025f);
            foreach (var y in b.GetComponentsInChildren<MeshRenderer>())
                if (Solid(y) && bounds.Intersects(y.bounds) && MeshIntersectsBox(y, bounds)) return true;
        }
        return false;
    }
    static readonly Dictionary<Mesh, Vector3[]> collisionVertices = new Dictionary<Mesh, Vector3[]>();
    static readonly Dictionary<Mesh, int[]> collisionTriangles = new Dictionary<Mesh, int[]>();
    static bool MeshIntersectsBox(Renderer renderer, Bounds box)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return true;
        Mesh mesh = filter.sharedMesh;
        if (!collisionVertices.TryGetValue(mesh, out var vertices)) collisionVertices[mesh] = vertices = mesh.vertices;
        if (!collisionTriangles.TryGetValue(mesh, out var triangles)) collisionTriangles[mesh] = triangles = mesh.triangles;
        Matrix4x4 matrix = filter.transform.localToWorldMatrix;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
            Vector3 b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]);
            Vector3 c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
            if (LoadingProcessController.ReplayTriangleBox(a, b, c, box.center, box.extents)) return true;
        }
        return false;
    }
    static bool Solid(Renderer renderer) => renderer.enabled && !renderer.name.Contains("地板") &&
        renderer.name != "GridBasePlate" && renderer.GetComponentInParent<FactoryVisual>() == null;
    public void Register(GameObject item, int kind)
    {
        EquipmentCollision.Install(item);
        if (kind < 3) item.AddComponent<FactoryStation>().Initialize();
        else if (kind == 4)
        {
            var rail = item.GetComponent<FactoryRail>(); Rails.Add(rail); Rebuild();
        }
        else
        {
            if (FindRail(item, null, out FactoryRail rail)) item.AddComponent<FactoryArm>().Initialize(rail);
        }
        if (kind < 3 || kind == 3 && item.GetComponent<FactoryArm>() != null) Statistics.Register(item, kind);
    }
    Transform routeVisual;
    Material routeMaterial;
    public void Rebuild()
    {
        Graph.Build(Rails);
        if (routeVisual != null) Destroy(routeVisual.gameObject);
        routeVisual = new GameObject("滑槽连通路径", typeof(FactoryVisual)).transform;
        routeVisual.SetParent(transform, false);
        if (routeMaterial == null)
        {
            routeMaterial = new Material(Shader.Find("Sprites/Default"));
            routeMaterial.color = new Color(.1f, .12f, .08f, .45f);
        }
        foreach (var segment in Graph.Segments())
        {
            var line = new GameObject("轨道路径").AddComponent<LineRenderer>();
            line.transform.SetParent(routeVisual, false); line.sharedMaterial = routeMaterial;
            line.positionCount = 2; line.startWidth = line.endWidth = 0.07f;
            line.SetPosition(0, segment[0]); line.SetPosition(1, segment[1]);
        }
    }
    public bool FindRail(GameObject arm, GameObject ignored, out FactoryRail rail)
    {
        return FindRailAt(FactoryArm.BaseCenter(arm.transform), ignored, out rail);
    }
    public bool FindRailAt(Vector3 p, GameObject ignored, out FactoryRail rail, bool allowOccupied = false)
    {
        rail = null;
        float nearest = float.PositiveInfinity;
        foreach (var r in Rails)
        {
            Bounds b = r.GetComponent<EquipmentGrid>().Footprint;
            if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
            bool occupied = false;
            foreach (var other in Arms)
                if (other != null && other.gameObject != ignored && Graph.Same(r, other.Rail)) occupied = true;
            float d = Vector3.Distance(r.Closest(p), new Vector3(p.x, 0, p.z));
            if ((!occupied || allowOccupied) && d < nearest) { rail = r; nearest = d; }
        }
        return rail != null;
    }
    public FactoryDockArea DockAreaAt(Vector3 position)
    {
        foreach (var station in Stations)
        {
            if (station == null) continue;
            var ports = station.Ports;
            if (ports.DockArea.Matches(position)) return ports.DockArea;
            if (ports.ConveyorDockArea != null && ports.ConveyorDockArea.Matches(position)) return ports.ConveyorDockArea;
            if (ports.PickupDockArea != null && ports.PickupDockArea.Matches(position)) return ports.PickupDockArea;
        }
        if (FinishedArea != null && FinishedArea.DockArea.Matches(position)) return FinishedArea.DockArea;
        return null;
    }
    public bool ValidRailChange(GameObject candidate)
    {
        var list = new List<FactoryRail>(Rails);
        var rail = candidate.GetComponent<FactoryRail>();
        if (!list.Contains(rail)) list.Add(rail);
        var graph = new FactoryRailGraph(); graph.Build(list);
        for (int i = 0; i < Arms.Count; i++)
        for (int j = i + 1; j < Arms.Count; j++)
            if (graph.Same(Arms[i].Rail, Arms[j].Rail)) return false;
        return true;
    }
    public bool CanEdit(GameObject item)
    {
        var station = item.GetComponent<FactoryStation>();
        if (station != null && station.Locked) { Message("设备正在加工或交接，完成后可调整"); return false; }
        var arm = item.GetComponent<FactoryArm>();
        if (arm != null && (arm.Busy || arm.Holding)) { Message("机械臂正在搬运或持件，交接完成后可调整"); return false; }
        var rail = item.GetComponent<FactoryRail>();
        if (rail != null)
            foreach (var a in Arms)
                if (a != null && Graph.Same(a.Rail, rail)) { Message("请先撤回这条轨道上的机械臂，再调整滑槽"); return false; }
        return true;
    }
    public void AddRaw(FactoryStation station, int count)
    {
        if (station == null || station.Kind != 0) { Message("请点击冲床输入口或先选中冲床"); return; }
        for (int i = 0; i < count && station.CanAccept; i++)
            station.Enqueue(FactoryWorkpiece.Create(station.Ports.PartWidth, station.Ports.InputPort.position));
        Message("已放入原料，冲床按顺序加工；队列 " + station.Input.Count + "/32");
    }
    void Update()
    {
        if (!FactorySimulation.Running) SimulationTick();
    }
    internal void SimulationTick()
    {
        if (!FactoryLevelSession.CanSimulate) return;
        if (FactorySimulation.Now < nextDispatch) return; nextDispatch = FactorySimulation.Now + 0.3f;
        DispatchFinishedProducts();
        foreach (var source in Stations)
        {
            if (source == null || source.Output == null || source.OutputChanging || source.ReservedBy != null || source.Kind >= 2) continue;
            FactoryArm bestArm = null; FactoryStation bestTarget = null; float best = float.PositiveInfinity;
            foreach (var arm in Arms)
            {
                if (arm == null || !arm.AvailableFor(source) ||
                    (source.Output.Replay != null && source.Output.Replay.Holder != null && source.Output.Replay.Holder != arm) ||
                    !arm.CanReach(source.Ports.DockPosition, out var pickup)) continue;
                if (source.Kind == 0 && ShouldWaitForFinishedProduct(arm)) continue;
                foreach (var target in Stations)
                {
                    if (target.Kind != source.Kind + 1 || !target.CanAccept ||
                        !arm.CanReach(target.Ports.DockPosition, out var delivery)) continue;
                    float cost = target.Input.Count * 20 + target.Incoming * 20 + Length(pickup) + Length(delivery);
                    if (cost < best) { best = cost; bestArm = arm; bestTarget = target; }
                }
            }
            if (bestArm != null) bestArm.Assign(source, bestTarget);
        }
    }
    void DispatchFinishedProducts()
    {
        if (FinishedArea == null || !FinishedArea.HasSpace) return;
        foreach (var station in Stations)
        {
            if (station == null || station.Kind != 2 || station.OutputChanging || station.Output == null || station.ReservedBy != null) continue;
            FactoryArm best = null; float cost = float.PositiveInfinity;
            foreach (var arm in Arms)
            {
                if (arm == null || !arm.AvailableFor(station) || arm.Holding ||
                    !arm.CanReach(station.Ports.FinishedPickupDock, out var pickup) ||
                    !arm.CanReach(FinishedArea.DockPosition(station.Ports.FinishedPickupDock.y), out var delivery)) continue;
                float distance = Length(pickup) + Length(delivery);
                if (distance < cost) { cost = distance; best = arm; }
            }
            if (best != null)
            {
                int slot = FinishedArea.Reserve();
                if (slot >= 0) best.AssignFinished(station, FinishedArea, slot);
            }
        }
    }
    bool ShouldWaitForFinishedProduct(FactoryArm arm)
    {
        if (FinishedArea == null || !FinishedArea.HasSpace) return false;
        foreach (var station in Stations)
            if (station != null && station.Kind == 2 && (station.Busy || station.Output != null) &&
                arm.CanReach(station.Ports.FinishedPickupDock, out _) &&
                arm.CanReach(FinishedArea.DockPosition(station.Ports.FinishedPickupDock.y), out _)) return true;
        return false;
    }
    static float Length(List<Vector3> path)
    {
        float sum = 0; for (int i = 1; i < path.Count; i++) sum += Vector3.Distance(path[i - 1], path[i]); return sum;
    }
    public void ForgetRail(FactoryRail rail) { if (Rails.Remove(rail)) Rebuild(); }
    public void ForgetStation(FactoryStation station)
    {
        // Placement prevents removing an active station. This also handles scene teardown.
        Stations.Remove(station);
    }
    public string Describe(GameObject item)
    {
        var station = item.GetComponent<FactoryStation>();
        if (station != null) return "\n" + station.Status + " · 排队 " + station.Input.Count +
            " · 待搬运 " + station.OutputCount + "/" + station.OutputCapacity + " · 预约 " + station.Incoming;
        var arm = item.GetComponent<FactoryArm>(); if (arm != null) return "\n" + arm.Status;
        var rail = item.GetComponent<FactoryRail>();
        if (rail != null)
        {
            bool occupied = Arms.Exists(a => a != null && Graph.Same(rail, a.Rail));
            return "\n连通轨道：" + (occupied ? "已有1个机械臂" : "可放1个机械臂");
        }
        return "";
    }
    void OnDestroy() { if (FinishedArea != null) Destroy(FinishedArea.gameObject); FactoryFonts.Release(); if (routeMaterial != null) Destroy(routeMaterial); if (Instance == this) Instance = null; }
}

