using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(210)]
public sealed partial class FactoryArm : MonoBehaviour
{
    public FactoryRail Rail { get; private set; }
    public bool Busy { get; private set; }
    public bool Holding => held != null;
    public bool WaitingForWeld { get; private set; }
    public bool WaitingForConveyor { get; private set; }
    public float RailSpeed => 0.7f * scale * FactoryProduction.Instance.armTravelMultiplier;
    public float Reach { get; private set; }
    public string Status { get; private set; } = "等待设备完成";
    public Demo1MeshMirror Mirror { get; private set; }
    FactoryStation source, destination;
    FactoryWorkpiece held;
    Demo1ReplaySession replay;
    Vector3 baseOffset;
    Quaternion facing = Quaternion.identity;
    float scale;
    float baseClearance;
    bool incoming;
    Vector3 homePosition;
    FactoryRail homeRail;
    bool groundFinishedBase;
    float finishedBaseHeight;
    Transform carrierPlate;
    struct PlacedPose
    {
        public Vector3 position, scale;
        public Quaternion rotation;
    }
    readonly Dictionary<Transform, PlacedPose> placedPose = new Dictionary<Transform, PlacedPose>();
    public Vector3 TrackPosition => transform.position - baseOffset;
    public Vector3 PlacementPosition => homePosition + baseOffset;
    public Matrix4x4 ArmSpace => Matrix4x4.TRS(TrackPosition, facing, Vector3.one * scale) * Matrix4x4.Translate(-replay.Source.ReplayBasePosition);

    public static Vector3 BaseCenter(Transform root) => FactoryMotion.Bounds(FactoryMotion.Node(root, 869)).center;

    // Preview and committed arms must use the same physical base and rail height.
    public static void AlignToRail(Transform root, FactoryRail rail, Vector3 target)
    {
        Bounds bounds = FactoryMotion.Bounds(FactoryMotion.Node(root, 869));
        var foot = FactoryMotion.Node(root, 872);
        float bottom = foot != null ? FactoryMotion.Bounds(foot).min.y : bounds.min.y;
        Vector3 projected = rail.Closest(target);
        root.position += new Vector3(projected.x - bounds.center.x, rail.SurfaceY - bottom, projected.z - bounds.center.z);
    }

    public void Initialize(FactoryRail rail)
    {
        Rail = rail;
        Transform node = FactoryMotion.Node(transform, 869);
        Bounds bounds = FactoryMotion.Bounds(node);
        baseClearance = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
        baseOffset = transform.position - bounds.center;
        AlignToRail(transform, rail, bounds.center);
        scale = transform.lossyScale.x; Reach = 2 * scale;
        homePosition = TrackPosition; homeRail = Rail;
        carrierPlate = transform.Find("GridBasePlate");
        foreach (Transform nodeInArm in GetComponentsInChildren<Transform>())
            if (nodeInArm != transform)
                placedPose[nodeInArm] = new PlacedPose { position = nodeInArm.localPosition,
                    rotation = nodeInArm.localRotation, scale = nodeInArm.localScale };
        FactoryProduction.Instance.Arms.Add(this);
    }
    public bool AvailableFor(FactoryStation station) => !Busy && (held == null || station.Output == held) &&
        (station.Kind != 2 || FinishedRetryReady);
    public bool CanReach(Vector3 dock, out List<Vector3> path)
        => CanReach(dock, FactoryProduction.Instance.DockAreaAt(dock), out path);
    bool CanReach(Vector3 dock, FactoryDockArea area, out List<Vector3> path)
    {
        path = null; var system = FactoryProduction.Instance;
        FactoryRail end = null; float nearest = float.PositiveInfinity;
        Vector3 contact = Vector3.zero;
        foreach (var rail in system.Rails)
        {
            if (!system.Graph.Same(Rail, rail)) continue;
            Bounds b = rail.GetComponent<EquipmentGrid>().Footprint;
            Vector3 portal = new Vector3(dock.x, 0, dock.z);
            if (area != null)
            {
                if (!area.TryConnection(b, out portal)) continue;
            }
            else if (dock.x < b.min.x - 0.02f || dock.x > b.max.x + 0.02f || dock.z < b.min.z - 0.02f || dock.z > b.max.z + 0.02f) continue;
            float d = (rail.Closest(portal) - new Vector3(dock.x, 0, dock.z)).sqrMagnitude;
            if (d < nearest) { end = rail; nearest = d; contact = portal; }
        }
        if (end == null) return false;
        Vector3 routeStart = TrackPosition;
        var storage = system.FinishedArea;
        if (!system.Graph.Route(Rail, routeStart, end.Closest(contact), 0.02f, out path)) return false;
        // Storage is a drop-off boundary. Keep the carrier on its own rail.
        if (area != null && storage != null && area == storage.DockArea)
        {
            TrimReceivingApproach(path, storage.Footprint);
            return true;
        }
        path.Add(contact);
        path.Add(new Vector3(dock.x, 0, dock.z));
        return true;
    }
    static void TrimReceivingApproach(List<Vector3> path, Bounds receiving)
    {
        if (path.Count == 0) return;
        if (ContainsXZ(receiving, path[0]))
        { if (path.Count > 1) path.RemoveRange(1, path.Count - 1); return; }
        for (int i = 1; i < path.Count; i++)
        {
            Vector3 start = path[i - 1], delta = path[i] - start;
            if (delta.sqrMagnitude < .000001f ||
                !receiving.IntersectRay(new Ray(start, delta.normalized), out float entry) || entry > delta.magnitude + .001f) continue;
            Vector3 edge = start + delta.normalized * Mathf.Max(0, entry - .02f);
            path.RemoveRange(i, path.Count - i); path.Add(edge); return;
        }
    }
    static bool ContainsXZ(Bounds bounds, Vector3 p) => p.x >= bounds.min.x - .001f && p.x <= bounds.max.x + .001f &&
        p.z >= bounds.min.z - .001f && p.z <= bounds.max.z + .001f;
    public void Assign(FactoryStation from, FactoryStation to)
    {
        source = from; destination = to; Busy = true;
        from.ReservedBy = this; to.Incoming++; incoming = true;
        FactorySimulation.Start(this, Transfer());
    }
    IEnumerator Travel(FactoryStation station)
    {
        yield return TravelTo(station.Ports.DockPosition, station.Mapping.rotation);
    }
    IEnumerator TravelTo(Vector3 dock, Quaternion targetFacing, FactoryDockArea serviceArea = null)
    {
        if (!CanReach(dock, serviceArea != null ? serviceArea : FactoryProduction.Instance.DockAreaAt(dock), out var path))
            throw new System.InvalidOperationException("滑槽未接触机械臂停靠区域");
        Status = "沿玩家滑槽移动到原演示停靠点";
        yield return TravelPath(path, dock.y, targetFacing);
    }
    IEnumerator TravelPath(List<Vector3> path, float baseHeight, Quaternion targetFacing)
    {
        // Spend one frame's distance across as many rail edges as necessary.
        // Restarting a timer on every (often duplicate) graph node discarded
        // the remainder of the frame and introduced a pause at every tile.
        float speed = Mathf.Max(0.1f, RailSpeed);
        var travel = new FactoryTravelPath(path, transform.position - baseOffset, baseHeight);
        Quaternion startFacing = facing;
        float turnDuration = Quaternion.Angle(startFacing, targetFacing) /
            Mathf.Max(1f, replay != null && replay.Source != null ? replay.Source.armRotationSpeed : 90f);
        float elapsed = 0;
        while (!travel.Complete || elapsed < turnDuration)
        {
            elapsed += FactorySimulation.Delta;
            transform.position = travel.Advance(speed * FactorySimulation.Delta) + baseOffset;
            facing = Quaternion.Slerp(startFacing, targetFacing,
                turnDuration > 0 ? Mathf.SmoothStep(0, 1, elapsed / turnDuration) : 1);
            // Update even when a long frame crosses several short rail edges.
            Rail = FactoryProduction.Instance.Graph.RailAt(travel.Position, Rail);
            ReleaseSharedDockAfterDeparture();
            if (groundFinishedBase) GroundFinishedBase();
            yield return null;
        }
    }
    void ReleaseSharedDockAfterDeparture()
    {
        if (source == null || source.Kind > 1 || source.ReservedBy != this || (source.Kind == 0 && held == null) || (source.Kind == 1 && source.Output != null)) return;
        // Taking the part does not immediately free the common docking pose.
        // Wait until this arm's base has fully cleared the enlarged service area.
        Bounds clearance = source.Ports.DockArea.Footprint;
        clearance.Expand(new Vector3(baseClearance * 2, 0, baseClearance * 2));
        if (!ContainsXZ(clearance, TrackPosition)) source.ReservedBy = null;
    }
    void LateUpdate()
    {
        if (!FactorySimulation.Running) SimulationPresent();
    }
    internal void SimulationPresent()
    {
        if (groundFinishedBase) GroundFinishedBase();
        Mirror?.Apply();
        if (WaitingForConveyor) Status = destination != null && !destination.HasOutputSpace ? "传送带缓存已满（5/5），暂停送入，等待取走成品" : "焊接完成，等待连接传送带放件停靠点的滑槽";
        else if (Busy && replay != null && replay.Source.ReplayCheckpoint == null)
            Status = (WaitingForWeld ? "驻留焊接工位 · " : "") + replay.Source.ReplayMessage;
    }
    IEnumerator Transfer()
    {
        bool success = false;
        try
        {
            FactoryStation receivingStation = destination;
            var part = source.Output;
            replay = part.Replay;
            replay.Source.ReplayUseCarrierForWeld = true;
            if (source.Kind == 0)
            {
                Mirror = new Demo1MeshMirror(transform, replay, () => ArmSpace);
                Mirror.Apply();
                yield return Travel(source);
                held = source.TakeOutput(); replay.Holder = this;
                held.transform.SetParent(transform, true); replay.Space = () => ArmSpace;
                // Original base yaw, safe-height approach, suction contact,
                // wrist leveling, pickup lift and restoration run verbatim.
                yield return replay.Advance("Carry");
                // Keep the common pickup pose reserved until the base clears it.
                yield return replay.Advance("BendDock");
                yield return Travel(destination);
                destination.Incoming--; incoming = false;
                yield return destination.BendWithArm(held, this);
                Status = "折弯完成，持件等待通往焊接停靠点的滑槽和空闲焊接工位";
            }
            else
            {
                if (held != part) throw new System.InvalidOperationException("折弯件必须由原持件机械臂继续搬运");
                // Demo1 first raises and inverts the bent part, then travels.
                yield return replay.Advance("HTravelReady");
                yield return replay.Advance("HDock");
                source.TakeOutput();
                yield return Travel(destination);
                // Original exact gap fitting and downward insertion of all
                // four flaps precede the handoff to the H turntable.
                yield return replay.Advance("WeldInput");
                replay.Space = () => receivingStation.Mapping;
                destination.Incoming--; incoming = false;
                destination.Enqueue(held);
                held = null; WaitingForWeld = true;
                Status = "在焊接工位驻留，等待本件焊接完成";
                yield return replay.WaitFor("WeldComplete");
                WaitingForWeld = false;
                Vector3 conveyorDock = receivingStation.Mapping.MultiplyPoint3x4(replay.Source.ReplayConveyorBaseDestination);
                WaitingForConveyor = true;
                while (!receivingStation.HasOutputSpace || !CanReach(conveyorDock, out _)) yield return null;
                WaitingForConveyor = false;
                yield return replay.Advance("WeldProductLifted");
                held = part; held.transform.SetParent(transform, true);
                replay.Space = () => ArmSpace;
                yield return TravelTo(conveyorDock, destination.Mapping.rotation);
                yield return replay.Advance("ConveyorPlaced");
                replay.Space = () => receivingStation.Mapping;
                held.transform.SetParent(destination.transform, true);
                Mirror?.Apply(); Mirror = null; replay.Holder = null; held = null;
                replay.Source.ContinueReplay();
                Status = "焊接成品已放上传送带，等待下一项搬运任务";
            }
            success = true;
        }
        finally
        {
            if (source != null && source.ReservedBy == this) source.ReservedBy = null;
            if (incoming && destination != null) destination.Incoming = Mathf.Max(0, destination.Incoming - 1);
            incoming = false; Busy = false; WaitingForWeld = false; WaitingForConveyor = false;
            if (!success) Status = "原演示动作未完成：" + (replay != null ? replay.Error : "缺少工件");
            source = destination = null;
        }
    }
    public void Cancel()
    {
        FactorySimulation.Stop(this); StopAllCoroutines(); Mirror = null;
        if (storageDestination != null && storageSlot >= 0) storageDestination.Release(storageSlot);
        storageDestination = null; storageSlot = -1;
        if (held != null && replay != null && replay.Holder == this)
        {
            replay.Holder = null;
            if (source == null || !source.ReturnOutput(held))
            {
                if (replay != null && replay.Source != null && replay.Space != null) replay.Freeze();
                held.transform.SetParent(FactoryProduction.Instance != null ? FactoryProduction.Instance.Products : null, true);
            }
        }
        held = null; Busy = false;
    }
    void OnDestroy()
    {
        Cancel();
        if (FactoryProduction.Instance != null) FactoryProduction.Instance.Arms.Remove(this);
    }
}
