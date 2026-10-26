using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(200)]
public sealed partial class FactoryStation : MonoBehaviour
{
    public FactoryPorts Ports { get; private set; }
    public int Kind => Ports.Kind;
    public readonly Queue<FactoryWorkpiece> Input = new Queue<FactoryWorkpiece>();
    public FactoryWorkpiece Output { get; private set; }
    readonly List<FactoryWorkpiece> punchOutputs = new List<FactoryWorkpiece>();
    readonly List<FactoryWorkpiece> conveyorOutputs = new List<FactoryWorkpiece>();
    public int OutputCount => Kind == 0 ? punchOutputs.Count : Kind == 2 ? conveyorOutputs.Count : Output != null ? 1 : 0;
    public int OutputCapacity => Kind == 0 ? PunchOutputCapacity : Kind == 2 ? ConveyorOutputCapacity : 1;
    public bool HasOutputSpace => OutputCount < OutputCapacity;
    public bool OutputChanging { get; private set; }
    public const int PunchOutputCapacity = 10;
    public const int RawInputCapacity = 32;
    public const int ConveyorOutputCapacity = 5;
    public bool Busy { get; private set; }
    public FactoryArm ReservedBy { get; set; }
    public int Incoming { get; set; }
    public int Completed { get; private set; }
    public string Status { get; private set; } = "等待输入";
    public bool CanAccept => Kind == 0 ? Input.Count + Incoming < RawInputCapacity :
        !Busy && Input.Count + Incoming == 0 && (Kind == 2 ? HasOutputSpace : Output == null) &&
        (Kind != 1 || ReservedBy == null);
    public bool Locked => Busy || ReservedBy != null || Incoming > 0 ||
        (Output != null && Output.Replay != null && Output.Replay.Holder != null);
    public Matrix4x4 Mapping => Demo1RigDefinition.Load().Mapping(transform, Kind);
    public Demo1MeshMirror Mirror { get; private set; }
    Demo1ReplaySession active;
    GameObject arc;
    Material arcMaterial;

    public void Initialize()
    {
        Ports = GetComponent<FactoryPorts>();
        FactoryProduction.Instance.Stations.Add(this);
    }
    public void Enqueue(FactoryWorkpiece part)
    {
        part.transform.SetParent(transform, true); Input.Enqueue(part);
        if (Kind == 0) ArrangeRawQueue();
    }
    void ArrangeRawQueue()
    {
        int i = 0;
        foreach (var part in Input)
            part.transform.position = Ports.InputPort.position + Vector3.up * Ports.PartWidth * 0.006f * i++;
    }
    void Update()
    {
        if (!FactorySimulation.Running) SimulationTick();
    }
    internal void SimulationTick()
    {
        if (!FactoryLevelSession.CanSimulate) return;
        if (Kind == 2) AdvanceConveyorQueue();
        if (!Busy && Input.Count > 0 && Kind != 1 &&
            HasOutputSpace) FactorySimulation.Start(this, Process(Input.Dequeue()));
        if (!Busy && Kind != 1 && !HasOutputSpace) Status = Kind == 0 ? "冲床出料缓存已满（10/10），暂停加工" : "传送带缓存已满（5/5），等待取走成品";
        else if (!Busy && Output != null) Status = Kind == 1 ? "吸盘持件，等待焊接停靠轨道和空闲焊接工位" :
            Kind == 2 ? "传送带缓存 " + OutputCount + "/5，末端成品可取走" : "等待机械臂按 Demo1 姿态取件";
    }
    void LateUpdate()
    {
        if (!FactorySimulation.Running) SimulationPresent();
    }
    internal void SimulationPresent()
    {
        if (active == null) return;
        Status = active.Source.ReplayCheckpoint == "PunchUnload"
            ? (ReservedBy != null ? "机械臂正在取件，等待安全出料" : "输出暂存已满，等待机械臂取走冲压件")
            : active.Source.ReplayMessage;
        if (Kind == 2 && active.Source.ReplayCheckpoint == "WeldComplete" &&
            active.Holder != null && active.Holder.WaitingForConveyor)
            Status = !HasOutputSpace ? "传送带缓存已满（5/5），机械臂暂停送入" : "焊接完成，产物留在 H 平台，等待连接传送带放件轨道";
        Mirror?.Apply();
        if (Kind == 2) UpdateArc();
    }
    void Bind(Demo1ReplaySession replay)
    {
        active = replay; Mirror = new Demo1MeshMirror(transform, replay, () => Mapping);
        Mirror.Apply();
    }
    void Unbind()
    {
        Mirror?.Apply(); active = null; Mirror = null;
        if (arc != null) Destroy(arc);
        if (arcMaterial != null) Destroy(arcMaterial);
    }
    public FactoryWorkpiece TakeOutput()
    {
        var part = Output;
        if (part == null) return null;
        if (Kind == 0)
        {
            punchOutputs.Remove(part);
            Output = punchOutputs.Count > 0 ? punchOutputs[punchOutputs.Count - 1] : null;
        }
        else if (Kind == 2)
        {
            conveyorOutputs.Remove(part);
            Output = conveyorOutputs.Count > 0 ? conveyorOutputs[0] : null;
            OutputChanging = Output != null;
        }
        else Output = null;
        return part;
    }
    public bool ReturnOutput(FactoryWorkpiece part)
    {
        if (part == null) return false;
        var buffer = Kind == 0 ? punchOutputs : Kind == 2 ? conveyorOutputs : null;
        if (buffer != null && !buffer.Contains(part))
        {
            if (!HasOutputSpace) return false;
            buffer.Add(part);
        }
        else if (buffer == null && Output != null && Output != part) return false;
        Output = Kind == 2 ? conveyorOutputs[0] : part;
        part.transform.SetParent(transform, true);
        if (part.Replay != null && part.Replay.Holder == null && part.Replay.enabled) part.Replay.Space = () => Mapping;
        if (Kind == 2) { ConfigureConveyor(part); OutputChanging = ConveyorQueueNeedsAdvance(); }
        return true;
    }
    IEnumerator Process(FactoryWorkpiece part)
    {
        Busy = true;
        try
        {
            if (Kind == 0)
            {
                ArrangeRawQueue();
                part.Replay = Demo1ReplaySession.Create(part);
                part.Replay.Space = () => Mapping;
                Bind(part.Replay); part.Replay.Source.BeginReplay();
                yield return part.Replay.WaitFor("PunchUnload");
                // Keep accepting and processing input independently of downstream
                // availability. Only unloading waits for capacity or an arm's grip.
                while (OutputCount >= PunchOutputCapacity || ReservedBy != null) yield return null;
                OutputChanging = true;
                float stackHeight = 0;
                foreach (var pending in punchOutputs)
                    stackHeight += FactoryWorkpiece.GeometryBounds(pending.transform).size.y + Ports.PartWidth * 0.002f;
                part.Replay.Source.ReplayOutputLift = stackHeight / Mathf.Max(0.0001f, Mapping.lossyScale.y);
                yield return part.Replay.Advance("PunchOutput");
                part.SetStage(1);
                // Only original cross-module base travel is replaced by track routing.
                yield return part.Replay.Advance("PickupDock");
                part.Replay.ApplyWorkpiece(); punchOutputs.Add(part); Output = part; RecordCompleted();
            }
            else
            {
                Bind(part.Replay); part.Replay.Space = () => Mapping;
                // Read the tail position dynamically: removing a buffered item
                // while this one is travelling frees one more conveyor slot.
                ConfigureConveyor(part);
                conveyorFeedSpeed = part.Replay.Source.conveyorFeedSpeed;
                part.Replay.Source.ReplayConveyorEndExtension = ConveyorEndExtension / Mathf.Max(.0001f, Mapping.lossyScale.x);
                part.Replay.Source.ReplayConveyorStopOffset = () => ConveyorSpacing * OutputCount / Mathf.Max(.0001f, Mapping.lossyScale.x);
                yield return part.Replay.Advance("Complete");
                part.SetStage(3); part.Replay.Freeze();
                while (!HasOutputSpace) yield return null;
                if (!ReturnOutput(part)) throw new System.InvalidOperationException("传送带缓存已满");
                RecordCompleted(); FactoryProduction.Instance.Finished++;
            }
            Status = "等待输入";
        }
        finally { if (Kind != 2) OutputChanging = false; Unbind(); Busy = false; }
    }
    public IEnumerator BendWithArm(FactoryWorkpiece part, FactoryArm arm)
    {
        Busy = true;
        try
        {
            Bind(part.Replay);
            // Keep Demo1's suction grip through all approaches, edge indexing,
            // withdrawals and die strokes. The workpiece never moves on its own.
            part.Replay.Holder = arm;
            yield return part.Replay.Advance("BendOutput");
            part.SetStage(2); part.Replay.ApplyWorkpiece(); Output = part; RecordCompleted();
        }
        finally { Unbind(); Busy = false; }
    }
    void RecordCompleted()
    {
        Completed++;
        FactoryProduction.Instance.Statistics.RecordCompletion(gameObject);
    }
    void UpdateArc()
    {
        GameObject original = active.Source.ReplayArc;
        if (original == null) return;
        if (arc == null)
        {
            arc = new GameObject("Demo1焊接电弧", typeof(FactoryVisual), typeof(MeshFilter), typeof(MeshRenderer), typeof(Light));
            arc.transform.SetParent(transform, false);
            arc.GetComponent<MeshFilter>().sharedMesh = original.GetComponent<MeshFilter>().sharedMesh;
            arcMaterial = new Material(original.GetComponent<Renderer>().sharedMaterial);
            arc.GetComponent<Renderer>().sharedMaterial = arcMaterial;
            var a = arc.GetComponent<Light>(); var b = original.GetComponent<Light>();
            a.type = b.type; a.color = b.color; a.range = b.range * Mapping.lossyScale.x; a.intensity = b.intensity;
        }
        arc.SetActive(original.activeSelf);
        Demo1MeshMirror.SetPose(arc.transform, Mapping * original.transform.localToWorldMatrix);
    }
    void OnDestroy()
    {
        FactorySimulation.Stop(this);
        if (arcMaterial != null) Destroy(arcMaterial);
        if (FactoryProduction.Instance != null) FactoryProduction.Instance.ForgetStation(this);
    }
}
