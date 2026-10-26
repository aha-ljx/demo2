using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Integration only. All mechanical routines live in the imported Demo1 file.
public sealed partial class LoadingProcessController
{
    [HideInInspector] public bool ReplayMode;
    // Nested coroutine boundaries can leave trial transforms alive until the
    // next frame. Presentation must hold its last committed pose throughout.
    private int armPlanningDepth;
    public bool ReplayArmPlanning => armPlanningDepth > 0;
    float MotionDelta => ReplayMode ? FactorySimulation.Delta : Time.deltaTime;
    object PlanningYield => ReplayMode ? FactorySimulation.PlanningPause : null;
    object WaitMotion(float seconds) => ReplayMode ? FactorySimulation.Wait(seconds) : new WaitForSeconds(seconds);
    FactoryRoutine StartMotion(IEnumerator routine) => ReplayMode ? FactorySimulation.Start(this, routine) : FactoryRoutine.Native(this, routine);
    void StopMotion(FactoryRoutine routine) { FactorySimulation.Stop(routine); }
    [HideInInspector] public Transform ReplayScope;
    public string ReplayCheckpoint { get; private set; }
    // Raise only the original output destination to sit on earlier finished
    // plates. Pickup still uses Demo1's real surface/contact/IK calculations.
    public float ReplayOutputLift;
    [Min(0f)] public float punchHoldSeconds = 2f;
    public bool ReplayUseCarrierForWeld;
    public bool ReplayWeldParked { get; private set; }
    public Vector3 ReplayConveyorBaseDestination { get; private set; }
    public string ReplayError { get; private set; }
    public string ReplayMessage => message;
    public bool ReplayReady => initialized;
    public Transform ReplaySheet => sheet;
    public bool ReplayArmReturning => armReturning;
    public Vector3 ReplayHTravelDestination { get; private set; }
    public Vector3 ReplayBasePosition => armBaseMover.position;
    public GameObject ReplayArc => weldingArc;
    public Transform ReplayArmBase => armBaseAssembly;
    Transform[] replayNodes;
    public readonly Dictionary<Transform, Matrix4x4> ReplayRestMatrices = new Dictionary<Transform, Matrix4x4>();

    void Start() { if (!ReplayMode) InitializeDemo1(); }
    IEnumerable<Transform> ReplayNodes()
    {
        if (!ReplayMode) return FindObjectsOfType<Transform>();
        if (replayNodes == null) CaptureReplayNodes();
        return replayNodes;
    }
    IEnumerable<Renderer> ReplayRenderers()
    {
        if (!ReplayMode) return FindObjectsOfType<Renderer>();
        return ReplayScope.GetComponentsInChildren<Renderer>(true);
    }
    public void CaptureReplayNodes()
    {
        replayNodes = ReplayScope.GetComponentsInChildren<Transform>(true);
        ReplayRestMatrices.Clear();
        foreach (Transform node in replayNodes) ReplayRestMatrices[node] = node.localToWorldMatrix;
    }
    public Transform ReplayNode(int id) => FindModelNode(id);
    public IEnumerable<Transform> ReplayOriginalNodes => ReplayNodes();
    public Vector3 ReplayBottom => SheetBottomCenter();
    public void OwnReplayRigs()
    {
        Transform[] roots = { armRig, armBaseMover, weldingArmRig, hTurntableRig,
            transferArmRig, transferBaseMover };
        foreach (Transform root in roots)
            if (root != null && root.parent == null) root.SetParent(ReplayScope, true);
    }
    public void BeginReplay()
    {
        if (!initialized) throw new System.InvalidOperationException("Demo1 初始化失败：" + message);
        rawSheetInstance.gameObject.SetActive(true);
        Vector3 p = materialPickupPoint.position;
        p.y = materialTable.bounds.max.y + 0.002f;
        workpieceRoot.position += p - SheetBottomCenter();
        placedSheetCenter = p;
        carryingMaterial = false; materialPlaced = true;
        StartLoadingProcess();
    }
    IEnumerator ReplayGate(string checkpoint)
    {
        if (!ReplayMode) yield break;
        ReplayCheckpoint = checkpoint;
        while (ReplayCheckpoint == checkpoint && ReplayError == null) yield return null;
    }
    public void ContinueReplay() { ReplayCheckpoint = null; }
    public Vector3 ReplayFinishedDropTarget;
    public System.Func<float> ReplayConveyorStopOffset;
    public float ReplayConveyorEndExtension;
    public void PrepareFinishedTransport(List<Demo1ReplaySession.FinishedSection> sections, Vector3 basePosition)
    {
        rawSheetInstance.gameObject.SetActive(false);
        workpieceRoot.SetParent(workpieceHomeParent, true);
        workpieceRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity); workpieceRoot.localScale = Vector3.one;
        sheet = new GameObject("FinishedTransportSheet").transform; sheet.SetParent(workpieceRoot, false);
        foreach (var section in sections)
        {
            var mesh = new GameObject(section.name, typeof(MeshFilter), typeof(MeshRenderer));
            mesh.transform.SetParent(sheet, false); mesh.GetComponent<MeshFilter>().sharedMesh = section.mesh;
            mesh.GetComponent<Renderer>().sharedMaterial = rawSheetMaterial;
            mesh.GetComponent<Renderer>().forceRenderingOff = true;
            Demo1MeshMirror.SetPose(mesh.transform, section.pose);
        }
        Vector3 shift = basePosition - armBaseMover.position; armBaseMover.position = basePosition;
        foreach (var visual in armMovingVisuals) if (visual != null && !visual.IsChildOf(armBaseMover)) visual.position += shift;
        sequence = StartMotion(ReplayFinishedTransportSequence());
    }

    // Convert the captured belt geometry to a new carrier frame before pickup.
    public void OffsetFinishedTransport(Matrix4x4 frameChange)
    {
        finishedPickupPlanReady = false;
        Demo1MeshMirror.SetPose(workpieceRoot, frameChange * workpieceRoot.localToWorldMatrix);
    }

    public bool ReplayFinishedPlacementFits { get; private set; }
    public bool ReplayFinishedPickupFits { get; private set; }
    readonly PickupArmPose[] finishedPickupPoses = new PickupArmPose[3];
    readonly Vector3[] finishedPickupTargets = new Vector3[3];
    PickupArmPose finishedPickupStart;
    Quaternion finishedPickupWrist;
    Matrix4x4 finishedPickupPartPose;
    bool finishedPickupPlanReady;
    public int ReplayFinishedPickupPlanUses { get; private set; }
    public int ReplaySkippedStationaryWristChecks { get; private set; }
    public IEnumerator CheckFinishedPickup()
    {
        armPlanningDepth++;
        try
        {
            ReplayFinishedPickupFits = false;
            finishedPickupPlanReady = false;
            PickupArmPose saved = CurrentArmPose();
            Quaternion wrist = armWristRig.rotation;
            Transform parent = workpieceRoot.parent;
            Matrix4x4 partPose = workpieceRoot.localToWorldMatrix;
            try
            {
                armWristRig.rotation = HorizontalPickupWristRotation();
                Quaternion horizontal = armWristRig.rotation;
                Transform panel = sheet.Find("FinishedPartSection1");
                Bounds bounds = GetBounds(panel != null ? panel : sheet);
                Vector3 contact = new Vector3(bounds.center.x, bounds.max.y + .003f, bounds.center.z);
                float lift = Mathf.Max(transferLiftClearance, GetBounds(sheet).size.y + transferLiftClearance);
                Vector3[] targets = { contact + Vector3.up * lift, contact, contact + Vector3.up * lift };
                for (int i = 0; i < targets.Length; i++)
                {
                    Vector3 target = targets[i];
                    Vector3 offset = SuctionContactPoint() - armWristRig.position;
                    yield return SolveFixedArmPoseAsync(target - offset, horizontal);
                    if (!fixedArmSolved) yield break;
                    finishedPickupPoses[i] = fixedArmSolvedPose;
                    finishedPickupTargets[i] = target - offset;
                    ApplyArmPose(fixedArmSolvedPose, fixedArmSolvedPose, 1, horizontal);
                    if (i == 1) ReparentWithoutJump(workpieceRoot, armWristRig);
                }
                finishedPickupStart = saved; finishedPickupWrist = horizontal;
                finishedPickupPartPose = partPose; finishedPickupPlanReady = true;
                ReplayFinishedPickupFits = true;
            }
            finally
            {
                workpieceRoot.SetParent(parent, true);
                Demo1MeshMirror.SetPose(workpieceRoot, partPose);
                ApplyArmPose(saved, saved, 1, wrist);
            }
        }
        finally { armPlanningDepth--; }
    }

    public IEnumerator CheckFinishedPlacement(Vector3 target)
    {
        armPlanningDepth++;
        try
        {
            ReplayFinishedPlacementFits = false;
            PickupArmPose saved = CurrentArmPose();
            Quaternion wrist = armWristRig.rotation;
            Vector3 offset = SuctionContactPoint() - armWristRig.position;
            Vector3 contact = target + SuctionContactPoint() - SheetBottomCenter();
            float lift = Mathf.Max(transferLiftClearance, GetBounds(sheet).size.y + transferLiftClearance);
            try
            {
                yield return SolveFixedArmPoseAsync(contact + Vector3.up * Mathf.Min(lift, .06f) - offset, wrist);
                if (!fixedArmSolved) yield break;
                ApplyArmPose(fixedArmSolvedPose, fixedArmSolvedPose, 1, wrist);
                yield return SolveFixedArmPoseAsync(contact - offset, wrist);
                ReplayFinishedPlacementFits = fixedArmSolved;
            }
            finally { ApplyArmPose(saved, saved, 1, wrist); }
        }
        finally { armPlanningDepth--; }
    }

    IEnumerator ReplayFinishedTransportSequence()
    {
        yield return null;
        yield return ReplayGate("FinishedPickup");
        Transform panel = sheet.Find("FinishedPartSection1");
        Bounds bounds = GetBounds(panel != null ? panel : sheet);
        Vector3 contact = new Vector3(bounds.center.x, bounds.max.y + 0.003f, bounds.center.z);
        float lift = Mathf.Max(transferLiftClearance, GetBounds(sheet).size.y + transferLiftClearance);
        SetState(LoadingState.TransferArmApproach, "成品搬运臂对准传送带末端产物");
        yield return LevelPickupWrist();
        if (sequence == null) yield break;
        // The dock transaction locks the belt while this exact three-stage
        // motion is checked. Reuse it after arrival; discard on any pose change.
        bool usePlan = FinishedPickupPlanMatches();
        finishedPickupPlanReady = false;
        yield return usePlan ? MoveCheckedFinishedPickup(0) : MoveArmSuctionTo(contact + Vector3.up * lift);
        if (sequence == null) yield break;
        yield return usePlan ? MoveCheckedFinishedPickup(1) : MoveArmSuctionTo(contact);
        if (sequence == null) yield break;
        ReparentWithoutJump(workpieceRoot, armWristRig);
        SetState(LoadingState.TransferArmPickup, "吸取最终产物并抬高，准备沿玩家滑槽搬运");
        yield return usePlan ? MoveCheckedFinishedPickup(2) : MoveArmSuctionTo(contact + Vector3.up * lift);
        if (sequence == null) yield break;
        yield return ReplayGate("FinishedLifted");
        SetState(LoadingState.TransferArmCarry, "成品搬运臂已到达最终产物区");
        Vector3 bottom = SheetBottomCenter();
        Vector3 targetContact = ReplayFinishedDropTarget + SuctionContactPoint() - bottom;
        // The product is already clear of the belt. A short approach above the
        // storage surface avoids adding the full pickup lift to a tall stack.
        Vector3 above = targetContact + Vector3.up * Mathf.Min(lift, 0.06f);
        yield return MoveArmSuctionTo(above);
        if (sequence == null) yield break;
        yield return MoveArmSuctionTo(targetContact);
        if (sequence == null) yield break;
        ReparentWithoutJump(workpieceRoot, workpieceHomeParent);
        // The original fixed-joint solver has a small contact tolerance. Let
        // the released part settle those last millimetres onto its exact seat.
        yield return MoveWorkpiece(workpieceRoot.position + ReplayFinishedDropTarget - SheetBottomCenter(), armPickupSpeed);
        if (sequence == null) yield break;
        SetState(LoadingState.PlaceOnConveyor, "成品搬运臂将最终产物放到存放台");
        yield return MoveArmSuctionTo(above);
        if (sequence == null) yield break;
        yield return RestorePickupArmJoints();
        if (sequence == null) yield break;
        yield return ReplayGate("FinishedPlaced");
    }

    bool FinishedPickupPlanMatches()
    {
        if (!finishedPickupPlanReady || Quaternion.Angle(armWristRig.rotation, finishedPickupWrist) > .01f) return false;
        PickupArmPose current = CurrentArmPose();
        if (Quaternion.Angle(current.yaw, finishedPickupStart.yaw) > .01f ||
            Quaternion.Angle(current.third, finishedPickupStart.third) > .01f ||
            Quaternion.Angle(current.fourth, finishedPickupStart.fourth) > .01f ||
            Quaternion.Angle(current.sixth, finishedPickupStart.sixth) > .01f) return false;
        Matrix4x4 pose = workpieceRoot.localToWorldMatrix;
        for (int i = 0; i < 16; i++) if (Mathf.Abs(pose[i] - finishedPickupPartPose[i]) > .0001f) return false;
        return true;
    }

    IEnumerator MoveCheckedFinishedPickup(int stage)
    {
        ReplayFinishedPickupPlanUses++;
        yield return MoveFixedArmPoseTo(finishedPickupPoses[stage], finishedPickupTargets[stage], finishedPickupWrist);
    }

    IEnumerator ReplayParkAtWeld(PickupArmPose pose, Quaternion orientation, float clearance)
    {
        // Clear the turntable, then hold the same safe insertion pose. No base
        // return and no second transfer arm is involved in the factory game.
        yield return MoveArmSuctionTo(SuctionContactPoint() + Vector3.up * clearance);
        if (sequence == null) yield break;
        yield return RestoreBendingJoints(pose.yaw, pose.third, pose.fourth, pose.sixth, orientation);
        if (sequence == null) yield break;
        ReplayWeldParked = true;
        SetState(LoadingState.ArmReturnToRailEnd, "机械臂在焊接工位安全驻留，等待焊接完成");
    }

    IEnumerator ReplayCarrierToConveyor()
    {
        ReplayWeldParked = false;
        Transform panel = sheet.Find("FinishedPartSection1");
        if (panel == null) { Fail("无法定位焊接成品中心板"); yield break; }
        Bounds bounds = GetBounds(panel);
        Vector3 pickup = new Vector3(bounds.center.x, bounds.max.y + 0.003f, bounds.center.z);
        float lift = Mathf.Max(transferLiftClearance, GetBounds(sheet).size.y + transferLiftClearance);
        SetState(LoadingState.TransferArmApproach, "原搬运机械臂从焊接平台上方接近成品");
        yield return LevelPickupWrist();
        if (sequence == null) yield break;
        yield return MoveArmSuctionTo(pickup + Vector3.up * lift);
        if (sequence == null) yield break;
        yield return MoveArmSuctionTo(pickup);
        if (sequence == null) yield break;
        SetState(LoadingState.TransferArmPickup, "原搬运机械臂吸取焊接成品");
        ReparentWithoutJump(workpieceRoot, armWristRig);
        yield return MoveArmSuctionTo(pickup + Vector3.up * lift);
        if (sequence == null) yield break;

        Vector3 near, far; float surface;
        if (!TryGetConveyorEndpoints(out near, out far, out surface)) yield break;
        Vector3 shift = Vector3.ProjectOnPlane(near - pickup, Vector3.up);
        ReplayConveyorBaseDestination = armBaseMover.position + shift;
        yield return ReplayGate("WeldProductLifted");
        SetState(LoadingState.TransferArmCarry, "原搬运机械臂沿玩家滑槽携成品移到传送带");
        // The actual base travels on the player's connected rail before this
        // checkpoint resumes; this only advances the private source coordinate.
        yield return MoveCarrier(armBaseMover, armMovingVisuals, ReplayConveyorBaseDestination, armBaseMoveSpeed);
        Vector3 bufferAxis = (far - near).normalized;
        near -= bufferAxis * ReplayConveyorEndExtension;
        far += bufferAxis * ReplayConveyorEndExtension;
        Vector3 contact = near;
        contact.y = surface + SuctionContactPoint().y - GetBounds(sheet).min.y + 0.005f;
        Vector3 above = contact + Vector3.up * lift;
        yield return MoveArmSuctionTo(above);
        if (sequence == null) yield break;
        SetState(LoadingState.PlaceOnConveyor, "同一机械臂将焊接成品放到传送带起点");
        yield return MoveArmSuctionTo(contact);
        if (sequence == null) yield break;
        ReparentWithoutJump(workpieceRoot, workpieceHomeParent);
        yield return MoveArmSuctionTo(above);
        if (sequence == null) yield break;
        yield return RestorePickupArmJoints();
        if (sequence == null) yield break;
        yield return ReplayGate("ConveyorPlaced");

        Vector3 direction = (far - near).normalized;
        float alignment = Vector3.Dot(Vector3.Cross(conveyorShaftAxis, Vector3.up), direction);
        if (Mathf.Abs(alignment) < 0.5f) { Fail("传送带卷筒轴与送料方向不垂直"); yield break; }
        SetState(LoadingState.ConveyorRunning, "传送带送料；玩家机械臂已完成焊后搬运，可接下一项任务");
        float distance = Vector3.Distance(near, far), travelled = 0;
        while (sequence != null)
        {
            float destination = Mathf.Max(0, distance - (ReplayConveyorStopOffset != null ? ReplayConveyorStopOffset() : 0));
            if (travelled >= destination) break;
            float step = Mathf.Min(destination - travelled, Mathf.Max(0.01f, conveyorFeedSpeed) * MotionDelta);
            workpieceRoot.position += direction * step;
            float degrees = Mathf.Sign(alignment) * step / conveyorRollRadius * Mathf.Rad2Deg;
            foreach (Transform roller in conveyorRollers)
                roller.RotateAround(GetBounds(roller).center, conveyorShaftAxis, degrees);
            travelled += step; yield return null;
        }
        if (sequence != null) SetState(LoadingState.TransferComplete, "原搬运机械臂已放件，成品到达传送带末端");
    }
    bool PlanReplayConveyorDestination()
    {
        Transform panel = sheet.Find("FinishedPartSection1");
        if (panel == null) return Fail("无法定位焊接成品中心板");
        Vector3 near, far; float surface;
        if (!TryGetConveyorEndpoints(out near, out far, out surface)) return false;
        ReplayConveyorBaseDestination = armBaseMover.position + Vector3.ProjectOnPlane(near - GetBounds(panel).center, Vector3.up);
        return true;
    }
    void ReleaseReplayResources()
    {
        FactorySimulation.Stop(this);
        // Runtime pivots belong to ReplayScope. Destroying that scope removes
        // the complete private rig; restoring parents during teardown is invalid.
        if (rawSheetMaterial != null) Destroy(rawSheetMaterial);
        if (weldingArcMaterial != null) Destroy(weldingArcMaterial);
    }
    public static bool ReplayTriangleBox(Vector3 a, Vector3 b, Vector3 c, Vector3 center, Vector3 extents)
        => TriangleIntersectsBox(a, b, c, new Bounds(center, extents * 2));
}
