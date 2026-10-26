using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// A private, invisible copy of Demo1 computes the original animation. The
// visible placed module receives its exact per-mesh poses in module coordinates.
// There is no substitute timeline, generic IK, or free-moving workpiece here.
[DefaultExecutionOrder(300)]
public sealed class Demo1ReplaySession : MonoBehaviour
{
    public struct FinishedSection { public string name; public Mesh mesh; public Matrix4x4 pose; }
    public static Demo1ReplaySession CreateFinishedTransport(FactoryWorkpiece workpiece, Matrix4x4 space)
    {
        var sections = new List<FinishedSection>();
        var previousRenderers = new List<Renderer>();
        foreach (var filter in workpiece.GetComponentsInChildren<MeshFilter>())
            if (filter.sharedMesh != null && filter.GetComponent<Renderer>() != null && filter.GetComponent<Renderer>().enabled)
            {
                sections.Add(new FinishedSection { name = filter.name, mesh = filter.sharedMesh, pose = space.inverse * filter.transform.localToWorldMatrix });
                previousRenderers.Add(filter.GetComponent<Renderer>());
            }
        if (sections.Count == 0) throw new InvalidOperationException("传送带成品没有可搬运的模型");
        // Bare finished models can also enter this handoff (e.g. saved/imported
        // products). Hide their old renderers as well as any frozen replay group.
        foreach (var renderer in previousRenderers) renderer.enabled = false;
        var old = workpiece.Replay;
        if (old != null)
        {
            if (old.visual != null) { old.visual.gameObject.SetActive(false); Destroy(old.visual.gameObject); }
            old.enabled = false; Destroy(old);
        }
        var replay = Create(workpiece); workpiece.Replay = replay; replay.Space = () => space;
        replay.Source.PrepareFinishedTransport(sections, replay.Definition.docks[2]);
        replay.ApplyWorkpiece(); return replay;
    }
    public LoadingProcessController Source { get; private set; }
    public Demo1RigDefinition Definition { get; private set; }
    public readonly Dictionary<string, List<Transform>> NamedMeshes = new Dictionary<string, List<Transform>>();
    public readonly Dictionary<int, List<Transform>> IdMeshes = new Dictionary<int, List<Transform>>();
    public Func<Matrix4x4> Space;
    public FactoryArm Holder;
    public bool Failed => Source == null || !string.IsNullOrEmpty(Source.ReplayError);
    public string Error => Source != null ? Source.ReplayError : "Demo1 动画实例已关闭";
    GameObject rig;
    FactoryWorkpiece part;
    Transform previousSheet;
    readonly List<Transform> sourcePieces = new List<Transform>();
    readonly List<Transform> visiblePieces = new List<Transform>();
    Transform visual;

    public static Demo1ReplaySession Create(FactoryWorkpiece workpiece)
    {
        var replay = workpiece.gameObject.AddComponent<Demo1ReplaySession>();
        replay.part = workpiece; replay.Definition = Demo1RigDefinition.Load();
        replay.rig = Instantiate(replay.Definition.rigPrefab, Vector3.zero, Quaternion.identity);
        replay.rig.name = "Demo1计算实例_" + workpiece.GetInstanceID();
        replay.rig.AddComponent<FactoryVisual>();
        replay.Source = replay.rig.GetComponentInChildren<LoadingProcessController>();
        replay.Source.ReplayMode = true; replay.Source.ReplayScope = replay.rig.transform;
        replay.Source.CaptureReplayNodes();
        foreach (Transform node in replay.Source.ReplayOriginalNodes)
        {
            if (node.GetComponent<MeshFilter>() == null) continue;
            if (!replay.NamedMeshes.TryGetValue(node.name, out var list))
                replay.NamedMeshes[node.name] = list = new List<Transform>();
            list.Add(node);
            int id = MeshId(node.name);
            if (id >= 0)
            {
                if (!replay.IdMeshes.TryGetValue(id, out var byId)) replay.IdMeshes[id] = byId = new List<Transform>();
                byId.Add(node);
            }
        }
        replay.HideRig();
        replay.Source.InitializeDemo1(); replay.Source.OwnReplayRigs(); replay.HideRig();
        if (!replay.Source.ReplayReady) throw new InvalidOperationException("Demo1 动画初始化失败：" + replay.Source.ReplayError);
        var tuning = FactoryProduction.Instance;
        if (tuning != null)
        {
            replay.Source.armBaseMoveSpeed *= tuning.armTravelMultiplier;
            replay.Source.armPickupSpeed *= tuning.armPickupMultiplier;
            replay.Source.armRotationSpeed *= tuning.armRotationMultiplier;
        }
        FactoryAnimationTiming.Apply(replay.Source);
        FactorySimulation.Register(replay);
        return replay;
    }
    public static int MeshId(string name)
    {
        var match = Regex.Match(name, @"(?:^|_)r(\d+)(?:_|$)");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }
    void HideRig()
    {
        foreach (Renderer renderer in rig.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
        foreach (Collider collider in rig.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Light light in rig.GetComponentsInChildren<Light>(true)) light.enabled = false;
    }
    public IEnumerator WaitFor(string checkpoint)
    {
        while (!Failed && Source.ReplayCheckpoint != checkpoint) yield return null;
        if (Failed) throw new InvalidOperationException("Demo1 " + checkpoint + " 动作失败：" + Error);
    }
    public IEnumerator Advance(string checkpoint)
    {
        Source.ContinueReplay(); yield return WaitFor(checkpoint);
    }
    void LateUpdate()
    {
        if (FactorySimulation.Running) return;
        if (Source == null || Space == null || Source.ReplaySheet == null) return;
        ApplyWorkpiece();
    }
    public void ApplyWorkpiece()
    {
        if (Source == null || Source.ReplayArmPlanning) return;
        Transform sheet = Source.ReplaySheet;
        if (previousSheet != sheet)
        {
            if (visual != null) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); }
            part.HideLegacyVisual();
            visual = new GameObject("Demo1原工件姿态").transform; visual.SetParent(transform, false);
            sourcePieces.Clear(); visiblePieces.Clear();
            foreach (MeshFilter source in sheet.GetComponentsInChildren<MeshFilter>())
            {
                var target = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
                target.transform.SetParent(visual, false);
                target.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                part.PaintRenderer(target.GetComponent<MeshRenderer>());
                sourcePieces.Add(source.transform); visiblePieces.Add(target.transform);
                source.GetComponent<Renderer>().forceRenderingOff = true;
            }
            previousSheet = sheet;
        }
        Matrix4x4 mapping = Space();
        // The workpiece root is only a container. Each section follows Demo1's
        // actual hinge and wrist transforms, including the downward H-clamp fit.
        transform.position = mapping.MultiplyPoint3x4(Source.workpieceRoot.position);
        for (int i = 0; i < sourcePieces.Count; i++) Demo1MeshMirror.SetPose(visiblePieces[i], mapping * sourcePieces[i].localToWorldMatrix);
    }
    public void Freeze()
    {
        ApplyWorkpiece(); enabled = false; Space = null;
        if (Source != null) FactorySimulation.Stop(Source);
        if (rig != null) Destroy(rig); rig = null;
    }
    void OnDestroy() { FactorySimulation.Unregister(this); if (Source != null) FactorySimulation.Stop(Source); if (rig != null) Destroy(rig); }
}

public sealed class Demo1MeshMirror
{
    readonly List<Transform> targets = new List<Transform>();
    readonly List<Transform> sources = new List<Transform>();
    readonly List<Matrix4x4> displayedPoses = new List<Matrix4x4>();
    readonly Func<Matrix4x4> mapping;
    readonly Demo1ReplaySession session;
    public int Count => targets.Count;
    public bool IsPlanning => session != null && session.Source != null && session.Source.ReplayArmPlanning;
    public readonly List<string> Missing = new List<string>();
    public Demo1MeshMirror(Transform target, Demo1ReplaySession session, Func<Matrix4x4> space)
    {
        mapping = space;
        this.session = session;
        var filters = new List<MeshFilter>(target.GetComponentsInChildren<MeshFilter>());
        filters.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));
        foreach (var filter in filters)
        {
            if (filter.GetComponentInParent<FactoryVisual>() != null || filter.name == "GridBasePlate") continue;
            if (!session.NamedMeshes.TryGetValue(filter.name, out var matches) &&
                !session.IdMeshes.TryGetValue(Demo1ReplaySession.MeshId(filter.name), out matches))
            {
                if (Demo1ReplaySession.MeshId(filter.name) >= 0) Missing.Add(filter.name);
                continue;
            }
            Transform chosen = matches[0];
            if (matches.Count > 1)
            {
                float best = float.PositiveInfinity;
                foreach (var candidate in matches)
                {
                    Matrix4x4 initial = session.Source.ReplayRestMatrices[candidate];
                    float distance = Vector3.Distance(space().MultiplyPoint3x4(initial.GetColumn(3)), filter.transform.position);
                    if (distance < best) { best = distance; chosen = candidate; }
                }
            }
            targets.Add(filter.transform); sources.Add(chosen);
            displayedPoses.Add(filter.transform.localToWorldMatrix);
        }
    }
    static int Depth(Transform t) { int depth = 0; while (t.parent != null) { depth++; t = t.parent; } return depth; }
    public void Apply()
    {
        if (session == null || session.Source == null || session.Source.ReplayArmPlanning) return;
        Matrix4x4 matrix = mapping();
        for (int i = 0; i < targets.Count; i++)
            if (targets[i] != null && sources[i] != null)
            {
                Matrix4x4 pose = matrix * sources[i].localToWorldMatrix;
                SetPose(targets[i], pose); displayedPoses[i] = pose;
            }
    }
    public float MaximumError()
    {
        float error = 0; Matrix4x4 matrix = mapping();
        for (int i = 0; i < targets.Count; i++)
        {
            // Apply intentionally holds the last visible pose during hidden IK
            // trials. Verify that held pose too, rather than comparing against
            // a speculative source pose that must never be displayed.
            Matrix4x4 expected = IsPlanning ? displayedPoses[i] : matrix * sources[i].localToWorldMatrix;
            Matrix4x4 actual = targets[i].localToWorldMatrix;
            for (int k = 0; k < 16; k++) error = Mathf.Max(error, Mathf.Abs(expected[k] - actual[k]));
        }
        return error;
    }
    public static void SetPose(Transform target, Matrix4x4 pose)
    {
        target.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        Vector3 scale = pose.lossyScale;
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
    }
}
