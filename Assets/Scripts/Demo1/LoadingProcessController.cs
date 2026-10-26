// Demo1 source SHA256: 2e49b01621b5be695955935242c47c8e3e8d36b1639dc07d34c6d921579e395a
// Motion routines retained; integration edits are reproducible with .animation-tools/import_demo1.py.
using System.Collections;
using UnityEngine;

/// <summary>原料台 -> 吸盘 -> 拾取机床夹爪 -> 冲床夹爪的单块板材上料演示。</summary>
public sealed partial class LoadingProcessController : MonoBehaviour
{
    public enum LoadingState
    {
        Idle, MoveSuctionToMaterial, LowerSuction, AttachMaterial,
        RaiseMaterial, MoveToTransferPoint, LowerToTransferPoint,
        ClampMaterial, ReleaseSuction, RaiseSuctionClearance,
        ClearClampFromSuction, RaiseSuction, ReturnSuctionHome,
        PickupFeed, PickupRailEndReached, PlaceSheetOnPunchTable,
        SlideSheetToPunchClamp, PunchClampMoveToAcquire,
        PunchClampAcquire, PickupClampRelease, TransferToPunchClamp,
        PickupClampReturn, PunchFeed, PunchStartPosition,
        PunchClampRelease, SlideSheetIntoPunchGuard, PunchClampReturn,
        Punching, ReplaceWithFinishedPart, RetrieveFinishedPart,
        PunchClampPullBack, PunchClampMoveToRailEnd, FinishedPartDelivered,
        SlideFinishedPartToTableEdge, ArmBaseTravel, ArmRotateToPart,
        ArmSuctionPickup, ArmLiftPart, ArmRestorePosture,
        ArmBaseToBendingMold, ArmApproachBendingMold,
        PositionBendEdge, CloseBendingDie,
        OpenBendingDie, BendingComplete, ArmToHClamps,
        PlaceBentPartInHClamps, ArmReturnToRailEnd,
        WeldingGunApproach, WeldingSeam, IndexHTurntable,
        WeldingComplete, TransferArmApproach, TransferArmPickup,
        TransferArmCarry, PlaceOnConveyor, ConveyorRunning,
        TransferArmReturn, TransferComplete,
        ReadyForPunching
    }

    [Header("工件与设备")]
    public GameObject rawSheetPrefab;
    public GameObject finishedPartPrefab;
    public GameObject[] bendingPiecePrefabs;
    public Transform workpieceRoot;
    public Renderer materialTable;
    public Renderer controlConsole;
    public Transform suctionMover;
    public Transform clampMover;
    public Transform[] suctionVisuals;
    public Transform suctionHousing;
    public Transform suctionVerticalGuide;
    public Transform suctionRack;
    public Transform suctionGear;
    public Transform[] clampVisuals;
    public Transform pickupClampBeam;
    public Transform clampJawA;
    public Transform clampJawB;
    [Header("冲床内部送料夹爪")]
    public Transform punchClampMover;
    public Transform[] punchClampVisuals;
    public Transform[] punchClampBrackets;
    public Transform[] punchJawsA;
    public Transform[] punchJawsB;
    public Renderer punchTable;

    [Header("静止设备，仅用于检查场景绑定")]
    public Transform suctionRail;
    public Transform clampRail;
    public Transform punchClampRail;
    public Transform punchRailFrame;
    public Transform connector;
    public Transform punchMachine;

    [Header("LoadingPoints：在场景中调整这些空节点")]
    public Transform suctionHome;
    public Transform materialPickupPoint;
    public Transform materialLiftPoint;
    public Transform transferPoint;
    public Transform clampPickupPoint;
    public Transform connectorPassPoint;
    public Transform punchEntryPoint;
    public Transform punchStartPoint;
    public Transform pickupClampHomePoint;
    public Transform pickupRailStartPoint;
    public Transform pickupRailEndPoint;
    public Transform punchClampHomePoint;
    public Transform punchClampAcquirePoint;
    public Transform punchRailStartPoint;
    public Transform punchRailEndPoint;
    public Transform transferToPunchPoint;

    [Header("运动速度，单位：Unity 世界单位/秒")]
    [Min(0.01f)] public float suctionMoveSpeed = 1.2f;
    [Min(0.01f)] public float suctionVerticalSpeed = 0.7f;
    [Min(0.01f)] public float clampMoveSpeed = 0.8f;
    [Min(0.01f)] public float feedSpeed = 0.55f;
    [Min(0.01f)] public float punchClampMoveSpeed = 0.8f;
    [Min(0.01f)] public float punchFeedSpeed = 0.55f;
    [Min(0.05f)] public float suctionReleaseClearance = 0.08f;
    [Min(0.1f)] public float clampClearanceDistance = 0.95f;
    [Min(0.001f)] public float jawCloseSpeed = 0.08f;
    [Min(0f)] public float jawCloseDistance = 0.035f;
    [Min(0.0001f)] public float positionTolerance = 0.002f;
    [Min(0.1f)] public float rawSheetWorldWidth = 0.8f;
    [ColorUsage(false)] public Color rawSheetColor = new Color(59f / 255f, 66f / 255f, 74f / 255f, 1f);
    [Min(0f)] public float punchTableGrooveInset = 0.01f;
    [Min(0.001f)] public float punchRailOutsideClearance = 0.01f;
    [Min(0.1f)] public float consoleInteractionDistance = 2.5f;
    [Header("玩家取料与放料")]
    public Transform materialPilePoint;
    [Min(1)] public int initialMaterialCount = 8;
    [Min(0.5f)] public float materialInteractionDistance = 3.5f;
    [Min(0f)] public float materialPileSpacing = 0.001f;
    [Header("成品拾取与放置")]
    public Transform finishedPlacementZonePoint;
    public Vector2 finishedPlacementZoneSize = new Vector2(2.4f, 2.4f);
    [Min(0.5f)] public float finishedPickupDistance = 2.5f;
    [Min(0.01f)] public float armBaseMoveSpeed = 0.7f;
    [Min(1f)] public float armRotationSpeed = 45f;
    [Min(0.01f)] public float armPickupSpeed = 0.35f;
    [Min(0.01f)] public float armLiftHeight = 0.15f;
    [Min(0f)] public float armPickupRailInset = 0.15f;
    [Min(0.01f)] public float bendingPressSpeed = 0.25f;
    [Min(0f)] public float bendingDieClearance = 0.004f;
    [Min(0f)] public float bendingPressHold = 0.25f;
    [Min(0.01f)] public float hClampApproachClearance = 0.18f;
    [Min(0.01f)] public float weldingGunMoveSpeed = 0.45f;
    [Min(0.01f)] public float weldingSeamSpeed = 0.12f;
    [Min(1f)] public float weldingTurntableSpeed = 35f;
    [Min(0f)] public float weldingTipClearance = 0.006f;
    [Min(0.01f)] public float weldingApproachClearance = 0.12f;
    [Min(0.01f)] public float transferArmMoveSpeed = 0.6f;
    [Min(0.01f)] public float conveyorFeedSpeed = 0.35f;
    [Min(0.01f)] public float transferLiftClearance = 0.2f;

    public LoadingState CurrentState { get; private set; } = LoadingState.Idle;
    public bool SuctionActive { get; private set; }
    public bool PickupClampActive { get; private set; }
    public bool PickupClampReturning { get; private set; }
    public bool PunchClampActive { get; private set; }
    public bool ClampActive => PickupClampActive; // 兼容已有读取端
    public Transform Workpiece => workpieceRoot;

    private FactoryRoutine sequence;
    private FactoryRoutine pickupReturnSequence;
    private FactoryRoutine armReturnSequence;
    private bool armReturning;
    private FactoryRoutine transferReturnSequence;
    private bool transferArmReturning;
    private Transform sheet;
    private Transform rawSheetInstance;
    private Transform materialPileRoot;
    private readonly System.Collections.Generic.List<GameObject> materialPileVisuals =
        new System.Collections.Generic.List<GameObject>();
    private int materialsRemaining;
    private bool carryingMaterial, materialPlaced, materialCommitted;
    private Vector3 pickupPointHome, liftPointHome;
    private Vector3 placedSheetCenter;
    private Vector3 rawSheetFootprint;
    private Transform placementPreview;
    private Material placementPreviewMaterial;
    private bool placementPreviewValid, placementPreviewHasPoint;
    private Vector3 placementPreviewCenter, placementPreviewPickup;
    private string placementPreviewReason = "靠近拾取机床原料台并看向台面";
    private Transform finishedPlacementZone, finishedPlacementPreview;
    private Transform finishedProductsRoot;
    private Material finishedZoneMaterial, finishedZoneBorderMaterial;
    private bool finishedProductCarried, finishedProductPlaced;
    private bool finishedPreviewValid, finishedPreviewHasPoint;
    private Vector3 finishedPreviewBottom;
    private string finishedPreviewReason = "靠近传送带旁的蓝色放置区域";
    private Quaternion finishedProductPlacementRotation;
    private float finishedZoneFloorY;
    private Quaternion workpieceHomeLocalRotation, rawSheetHomeLocalRotation;
    private Vector3 workpieceHomeLocalScale, rawSheetHomeLocalPosition;
    private Vector3 rawSheetHomeLocalScale;
    private Material rawSheetMaterial;
    private Transform workpieceHomeParent;
    private Vector3[] suctionVisualHome;
    private Vector3[] clampVisualHome;
    private Vector3[] punchVisualHome;
    private Vector3[] punchJawAHome, punchJawBHome;
    private Vector3 jawAHome, jawBHome;
    private Vector3 suctionMoverHome, clampMoverHome;
    private Vector3 suctionGuideHome;
    private Vector3 suctionRailAxis;
    private float suctionMinTravel, suctionMaxTravel, suctionGearRadius;
    private float suctionGearRollSign = 1f;
    private float suctionCarryHeight;
    private Vector3 suctionGearLocalAxis;
    private Quaternion suctionGearHomeRotation;
    private Vector3 punchMoverHome;
    private Transform punchRightGuard;
    private Transform punchRailShell;
    private Transform armLongSlot, armBaseAssembly, armBaseJoint;
    private Transform armSuctionAssembly, bendingProbe;
    private Transform bendingBody, bendingRail;
    private Transform bendingUpper, bendingUpperMold, bendingLowerMold;
    private Transform[] bendingUpperVisuals;
    private Transform[] bendingUpperMovingRoots;
    private static readonly int[] BendingUpperClampIds =
    {
        151, 154, 157, 160, 175, 178, 180, 184,
        185, 188, 190, 194, 195, 198, 200, 204,
        205, 208, 210, 214, 215, 218, 220, 224,
        225, 228, 230, 234, 235, 238, 240, 244,
        245, 248, 250, 254, 255, 256, 258, 260
    };
    private Vector3[] bendingUpperHomePositions;
    private Transform[] armSegments, armMovingVisuals, armSliders;
    private Transform[] armOriginalParents;
    private Transform armRig;
    private Transform armYawJoint, armThirdJoint, armFourthJoint, armSixthJoint;
    private Quaternion armYawHomeRotation;
    private float armPickupPlannedYaw;
    private struct PickupArmPose
    {
        public Quaternion yaw, third, fourth, sixth;
    }
    private PickupArmPose fixedArmSolvedPose;
    private bool fixedArmSolved, fixedArmPathClear;
    private Transform armThirdMeshTransform;
    private Vector3[] armThirdMeshVertices;
    private readonly System.Collections.Generic.Dictionary<Mesh, Vector3[]>
        collisionMeshVertices = new System.Collections.Generic.Dictionary<Mesh, Vector3[]>();
    private readonly System.Collections.Generic.Dictionary<Mesh, int[]>
        collisionMeshTriangles = new System.Collections.Generic.Dictionary<Mesh, int[]>();
    private readonly System.Collections.Generic.Dictionary<Mesh, Vector3[]>
        collisionLocalVertexBuffers = new System.Collections.Generic.Dictionary<Mesh, Vector3[]>();
    private Transform collisionPartRoot;
    private MeshFilter[] collisionPartMeshes;
    private Quaternion armThirdHomeRotation, armFourthHomeRotation,
        armSixthHomeRotation;
    private Transform armWristRig;
    private Vector3 armWristHomeLocalScale;
    private Transform[] armWristVisuals, armWristOriginalParents;
    private Quaternion armWristHomeLocalRotation;
    private Transform armContactAnchor;
    private Transform armWristStem;
    private Transform[] bentFlaps;
    private Bounds bentPanelBoundsLocal;
    private struct HClampGap
    {
        public Vector3 center;
        public Vector3 direction;
        public float near, far;
        public Vector3 tangent;
        public float tangentMin, tangentMax;
    }
    private Vector3[] cupTipLocalPoints;
    private Vector3 finishedTopLocalNormal = Vector3.up;
    private Vector3[] armVisualHomePositions;
    private Quaternion[] armVisualHomeRotations;
    private Transform armBaseMover;
    private Vector3 armBaseHome;
    private Transform hTurntableAssembly, hTurntable, hTurntableRig;
    private Transform[] hClampHeads, turntableOriginalParents;
    private Transform weldingBase, weldingFirstLink, weldingGunAssembly,
        weldingGunNozzle, weldingArmRig;
    private Transform[] weldingMovingParts, weldingOriginalParents;
    private Vector3 weldingHomeTipVector, weldingNozzleTipLocal;
    private GameObject weldingArc;
    private Material weldingArcMaterial;
    private Transform transferBase, transferFirstLink, transferSuction,
        transferHead, transferArmRig, transferWristRig, transferContactAnchor,
        transferBaseMover;
    private Transform transferYawJoint, transferShoulderJoint,
        transferElbowJoint, transferForearmJoint;
    private struct TransferArmPose
    {
        public Quaternion yaw, shoulder, elbow, forearm;
    }
    private TransferArmPose transferHomePose;
    private bool transferRailStopFound;
    private float transferRailStopTravel;
    private Transform[] transferSegments, transferMovingParts, transferOriginalParents,
        transferWristParts, transferWristParents, transferSliders, conveyorRollers;
    private Vector3 transferHomeBase;
    private Vector3 transferHomeArmRigPosition;
    private Vector3[] transferHomePositions;
    private Vector3[] conveyorHomePositions;
    private Quaternion[] transferHomeRotations, conveyorHomeRotations;
    private Quaternion transferHomeWristRotation, transferDownWristRotation;
    private Transform conveyorAssembly, conveyorPlate, conveyorShaft;
    private Vector3 conveyorShaftAxis;
    private float conveyorRollRadius;
    private bool initialized;
    private string message = "准备上料";
    private Font uiFont;

    public void InitializeDemo1()
    {
        if (!ReplayMode) uiFont = Resources.Load<Font>("NotoSansSC-UI");
        if (!ReplayMode && uiFont == null)
            Debug.LogWarning("任务提示字体 NotoSansSC-UI 未找到，中文可能无法显示。", this);
        if (controlConsole == null) controlConsole = FindControlConsole();
        if (suctionHousing == null && suctionVisuals != null && suctionVisuals.Length > 1)
            suctionHousing = suctionVisuals[1];
        if (suctionVerticalGuide == null)
            suctionVerticalGuide = FindModelNode(1207);
        if (suctionRack == null) suctionRack = FindModelNode(1120);
        if (suctionGear == null) suctionGear = FindModelNode(1138);
        if (pickupClampBeam == null) pickupClampBeam = FindModelNode(1017);
        if (punchTable == null) punchTable = FindModelNode(1414)?.GetComponent<Renderer>();
        if (punchRailFrame == null) punchRailFrame = FindModelNode(889);
        punchRightGuard = FindModelNode(1472);
        punchRailShell = FindModelNode(1490);
        BindTransferArm();
        BindWeldingEquipment();
        BindPostWeldEquipment();
        bendingBody = FindModelNode(268);
        bendingRail = FindModelNode(411);
        bendingUpper = FindModelNode(121);
        bendingUpperMold = FindModelNode(1565);
        bendingLowerMold = FindModelNode(1547);
        if (!BindBendingUpperVisuals()) return;
        if (punchClampBrackets == null || punchClampBrackets.Length != 2)
            punchClampBrackets = new[] { FindModelNode(1513), FindModelNode(1499) };
        if (punchClampVisuals != null && punchClampVisuals.Length == 2
            && (punchJawsA == null || punchJawsA.Length != 4
                || punchJawsB == null || punchJawsB.Length != 4))
            BindPunchJawPairs();
        if (pickupRailStartPoint == null && clampRail != null
            && pickupClampHomePoint != null && pickupRailEndPoint != null)
            pickupRailStartPoint = CreateRuntimeRailStart(clampRail,
                pickupClampHomePoint, pickupRailEndPoint, "PickupRailStartPoint (runtime)");
        if (punchRailStartPoint == null && punchClampRail != null
            && punchClampHomePoint != null && punchRailEndPoint != null)
            punchRailStartPoint = CreateRuntimeRailStart(punchClampRail,
                punchClampHomePoint, punchRailEndPoint, "PunchRailStartPoint (runtime)");
#if UNITY_EDITOR
        EnsureBendingPieceReferencesForPlay();
#endif
        if (!ValidateSetup()) return;
        SyncPunchClampPointsToPlacedAssemblies();
        // 控制空节点不属于 FBX。旧场景中它可能留在世界原点，模型本身仍在导轨上。
        suctionMover.position = suctionHome.position;
        clampMover.position = pickupClampHomePoint.position;
        punchClampMover.position = punchClampHomePoint.position;
        if (!CalibratePickupRailFromMesh()) return;
        PlaceTransferOnClampLeft();
        if (!CalibrateSuctionRailFromMesh()) return;
        suctionCarryHeight = CalculateSuctionCarryHeight();
        workpieceHomeParent = workpieceRoot.parent;
        suctionMoverHome = suctionMover.position;
        suctionGuideHome = suctionVerticalGuide.position;
        suctionGearHomeRotation = suctionGear.localRotation;
        clampMoverHome = clampMover.position;
        punchMoverHome = punchClampMover.position;
        suctionVisualHome = SavePositions(suctionVisuals);
        clampVisualHome = SavePositions(clampVisuals);
        punchVisualHome = SavePositions(punchClampVisuals);
        armRig = new GameObject("TransferArmRig (runtime)").transform;
        armRig.position = GetBounds(armBaseJoint).center;
        armOriginalParents = new Transform[armSegments.Length + 1];
        for (int i = 0; i < armSegments.Length; i++)
        {
            armOriginalParents[i] = armSegments[i].parent;
            armSegments[i].SetParent(armRig, true);
        }
        armOriginalParents[armSegments.Length] = armSuctionAssembly.parent;
        armSuctionAssembly.SetParent(armRig, true);
        armYawJoint = CreateArmJoint("Arm2 r851 horizontal yaw",
            armSegments[4].position, armRig);
        armThirdJoint = CreateArmJoint("Arm3 r848 pivot at Arm2 r851",
            armSegments[3].position, armYawJoint);
        armFourthJoint = CreateArmJoint("Arm4 r845 lower pivot",
            armSegments[2].position, armThirdJoint);
        armSixthJoint = CreateArmJoint("Arm6 r839 pivot",
            armSegments[0].position, armFourthJoint);
        armSegments[3].SetParent(armThirdJoint, true);
        armSegments[4].SetParent(armYawJoint, true);
        MeshFilter armThirdMesh = armSegments[3].GetComponent<MeshFilter>();
        if (armThirdMesh != null && armThirdMesh.sharedMesh != null
            && armThirdMesh.sharedMesh.isReadable)
        {
            armThirdMeshTransform = armThirdMesh.transform;
            armThirdMeshVertices = armThirdMesh.sharedMesh.vertices;
        }
        armSegments[2].SetParent(armFourthJoint, true);
        armSegments[1].SetParent(armFourthJoint, true);
        armSegments[0].SetParent(armSixthJoint, true);
        armSuctionAssembly.SetParent(armSixthJoint, true);
        armYawHomeRotation = armYawJoint.localRotation;
        armThirdHomeRotation = armThirdJoint.localRotation;
        armFourthHomeRotation = armFourthJoint.localRotation;
        armSixthHomeRotation = armSixthJoint.localRotation;
        armWristStem = FindModelNode(armSuctionAssembly, 857);
        armWristVisuals = new[] { armWristStem,
            FindModelNode(armSuctionAssembly, 863),
            FindModelNode(armSuctionAssembly, 860), FindModelNode(armSuctionAssembly, 866),
            FindModelNode(armSuctionAssembly, 867), FindModelNode(armSuctionAssembly, 868) };
        armWristRig = new GameObject("BendingWrist (runtime)").transform;
        armWristRig.position = armWristStem.position;
        armWristRig.SetParent(armSuctionAssembly, true);
        armWristOriginalParents = new Transform[armWristVisuals.Length];
        for (int i = 0; i < armWristVisuals.Length; i++)
        {
            armWristOriginalParents[i] = armWristVisuals[i].parent;
            armWristVisuals[i].SetParent(armWristRig, true);
        }
        armWristHomeLocalScale = armWristRig.localScale;
        armWristHomeLocalRotation = armWristRig.localRotation;
        if (!CacheCupTipPlane()) return;
        Vector3 cupContact, cupNormal;
        GetCupTipPlane(out cupContact, out cupNormal);
        armContactAnchor = new GameObject("SuctionContact (runtime)").transform;
        armContactAnchor.SetParent(armWristRig, false);
        armContactAnchor.position = cupContact;
        armMovingVisuals = new[] { armBaseAssembly, armRig };
        armBaseMover = new GameObject("ArmBaseMover (runtime)").transform;
        armBaseHome = GetBounds(armBaseAssembly).center;
        if (!SetupWeldingRigs()) return;
        if (!SetupPostWeldRigs()) return;
        armBaseMover.position = armBaseHome;
        armVisualHomePositions = SavePositions(armMovingVisuals);
        bendingUpperHomePositions = SavePositions(bendingUpperVisuals);
        armVisualHomeRotations = new Quaternion[armMovingVisuals.Length];
        for (int i = 0; i < armMovingVisuals.Length; i++)
            armVisualHomeRotations[i] = armMovingVisuals[i].rotation;
        punchJawAHome = SaveLocalPositions(punchJawsA);
        punchJawBHome = SaveLocalPositions(punchJawsB);
        if (clampJawA != null) jawAHome = clampJawA.localPosition;
        if (clampJawB != null) jawBHome = clampJawB.localPosition;

        if (workpieceRoot.childCount == 0)
        {
            GameObject raw = Instantiate(rawSheetPrefab, workpieceRoot);
            raw.name = "RawSheet";
            OrientSheetFlat(raw.transform);
            Bounds b = GetBounds(raw.transform);
            float width = Mathf.Max(b.size.x, b.size.z);
            if (width <= 0.0001f)
            {
                Debug.LogError("LoadingProcessController: 原料板没有有效的 Renderer bounds。", this);
                Destroy(raw);
                return;
            }
            raw.transform.localScale *= rawSheetWorldWidth / width;
        }
        sheet = workpieceRoot.GetChild(0);
        rawSheetInstance = sheet;
        workpieceHomeLocalRotation = workpieceRoot.localRotation;
        workpieceHomeLocalScale = workpieceRoot.localScale;
        rawSheetHomeLocalPosition = rawSheetInstance.localPosition;
        rawSheetHomeLocalRotation = rawSheetInstance.localRotation;
        rawSheetHomeLocalScale = rawSheetInstance.localScale;
        rawSheetFootprint = GetBounds(rawSheetInstance).size;
        ApplyWorkpieceColor(sheet);
        pickupPointHome = materialPickupPoint.position;
        liftPointHome = materialLiftPoint.position;
        materialsRemaining = initialMaterialCount;
        if (!ReplayMode)
        {
            CreateMaterialPile();
            CreatePlacementPreview();
            CreateFinishedPlacementZone();
        }
        initialized = true;
        ResetLoadingProcess();
    }

    private void ApplyWorkpieceColor(Transform target)
    {
        if (rawSheetMaterial == null)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("LoadingProcessController: Standard shader not found for workpiece.", this);
                return;
            }

            rawSheetMaterial = new Material(shader)
            {
                name = "Workpiece Dark Gray (runtime)",
                color = rawSheetColor
            };
            rawSheetMaterial.SetFloat("_Metallic", 0.15f);
            rawSheetMaterial.SetFloat("_Glossiness", 0.35f);
        }

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 0)
            {
                renderer.sharedMaterial = rawSheetMaterial;
                continue;
            }

            for (int i = 0; i < materials.Length; i++) materials[i] = rawSheetMaterial;
            renderer.sharedMaterials = materials;
        }
    }

    private void OnDestroy()
    {
        if (ReplayMode) { ReleaseReplayResources(); return; }
        if (finishedPlacementPreview != null) Destroy(finishedPlacementPreview.gameObject);
        if (finishedPlacementZone != null) Destroy(finishedPlacementZone.gameObject);
        if (finishedZoneMaterial != null) Destroy(finishedZoneMaterial);
        if (finishedZoneBorderMaterial != null) Destroy(finishedZoneBorderMaterial);
        if (placementPreview != null) Destroy(placementPreview.gameObject);
        if (placementPreviewMaterial != null) Destroy(placementPreviewMaterial);
        if (materialPileRoot != null) Destroy(materialPileRoot.gameObject);
        if (rawSheetMaterial != null) Destroy(rawSheetMaterial);
        if (weldingArcMaterial != null) Destroy(weldingArcMaterial);
        if (weldingArc != null) Destroy(weldingArc);
        if (weldingArmRig != null)
        {
            weldingArmRig.rotation = Quaternion.identity;
            weldingArmRig.localScale = Vector3.one;
            for (int i = 0; weldingOriginalParents != null
                && i < weldingMovingParts.Length; i++)
                if (weldingMovingParts[i] != null)
                    weldingMovingParts[i].SetParent(weldingOriginalParents[i], true);
            Destroy(weldingArmRig.gameObject);
        }
        if (transferArmRig != null)
        {
            if (workpieceRoot != null && workpieceRoot.parent == transferWristRig)
                workpieceRoot.SetParent(workpieceHomeParent, true);
            if (transferYawJoint != null && transferShoulderJoint != null
                && transferElbowJoint != null && transferForearmJoint != null
                && transferWristRig != null)
                ApplyTransferArmPose(transferHomePose, transferHomePose, 1f,
                    transferHomeWristRotation);
            transferArmRig.rotation = Quaternion.identity;
            transferArmRig.localScale = Vector3.one;
            if (transferWristRig != null)
            {
                if (transferContactAnchor != null)
                    Destroy(transferContactAnchor.gameObject);
                for (int i = 0; i < transferWristParts.Length; i++)
                    if (transferWristParts[i] != null)
                        transferWristParts[i].SetParent(transferWristParents[i], true);
                Destroy(transferWristRig.gameObject);
            }
            for (int i = 0; i < transferMovingParts.Length; i++)
                if (transferMovingParts[i] != null)
                    transferMovingParts[i].SetParent(transferOriginalParents[i], true);
            Destroy(transferArmRig.gameObject);
        }
        if (transferBaseMover != null) Destroy(transferBaseMover.gameObject);
        if (hTurntableRig != null)
        {
            if (workpieceRoot != null && workpieceRoot.parent == hTurntableRig)
                workpieceRoot.SetParent(workpieceHomeParent, true);
            hTurntableRig.rotation = Quaternion.identity;
            if (turntableOriginalParents != null)
            {
                if (hTurntableAssembly != null)
                    hTurntableAssembly.SetParent(turntableOriginalParents[0], true);
                for (int i = 0; i < hClampHeads.Length; i++)
                    if (hClampHeads[i] != null)
                        hClampHeads[i].SetParent(turntableOriginalParents[i + 1], true);
            }
            Destroy(hTurntableRig.gameObject);
        }
        if (bendingUpperHomePositions != null)
            RestorePositions(bendingUpperVisuals, bendingUpperHomePositions);
        if (armBaseMover != null) Destroy(armBaseMover.gameObject);
        if (armRig != null)
        {
            if (armContactAnchor != null) Destroy(armContactAnchor.gameObject);
            if (armWristRig != null)
            {
                for (int i = 0; i < armWristVisuals.Length; i++)
                    if (armWristVisuals[i] != null)
                        armWristVisuals[i].SetParent(armWristOriginalParents[i], true);
                Destroy(armWristRig.gameObject);
            }
            for (int i = 0; i < armSegments.Length; i++)
                if (armSegments[i] != null)
                    armSegments[i].SetParent(armOriginalParents[i], true);
            if (armSuctionAssembly != null)
                armSuctionAssembly.SetParent(armOriginalParents[armSegments.Length], true);
            Destroy(armRig.gameObject);
        }
    }

    private void Update()
    {
        if (ReplayMode) return;
        if (!initialized) return;
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetLoadingProcess();
            return;
        }
        UpdatePlacementPreview();
        UpdateFinishedPlacementPreview();
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (CurrentState == LoadingState.ReadyForPunching
                || CurrentState == LoadingState.TransferArmReturn)
                HandleFinishedProductInteraction();
            else HandleMaterialInteraction();
        }
        if (!Input.GetKeyDown(KeyCode.E) || !IsNearConsole()) return;
        if (CurrentState == LoadingState.ReadyForPunching)
        {
            if (!finishedProductPlaced)
            {
                message = "请先到传送带末端拾取成品并放入蓝色区域";
                return;
            }
            ResetLoadingProcess();
            return;
        }
        if (CurrentState == LoadingState.Idle) StartLoadingProcess();
    }

    /// <summary>供 UI Button、控制台交互或其他脚本调用。</summary>
    [ContextMenu("Start Loading Process")]
    public void StartLoadingProcess()
    {
        if (!initialized || sequence != null || CurrentState != LoadingState.Idle) return;
        if (!materialPlaced)
        {
            message = "请先从原料堆取板，并放到拾取机床原料台";
            return;
        }
        if (!ConfigurePickupFromPlacedSheet()) return;
        materialCommitted = true;
        sequence = StartMotion(RunLoadingProcess());
    }

    /// <summary>恢复同一块板材、吸盘、夹爪和全部状态，允许再次测试。</summary>
    [ContextMenu("Reset Loading Process")]
    public void ResetLoadingProcess()
    {
        if (!initialized) return;
        if (!materialCommitted && (carryingMaterial || materialPlaced))
        {
            materialsRemaining = Mathf.Min(initialMaterialCount, materialsRemaining + 1);
            RefreshMaterialPile();
        }
        carryingMaterial = false;
        materialPlaced = false;
        materialCommitted = false;
        finishedProductCarried = false;
        finishedProductPlaced = false;
        finishedPreviewValid = finishedPreviewHasPoint = false;
        if (finishedPlacementPreview != null)
        {
            Destroy(finishedPlacementPreview.gameObject);
            finishedPlacementPreview = null;
        }
        placementPreviewValid = placementPreviewHasPoint = false;
        placementPreviewReason = "靠近拾取机床原料台并看向台面";
        if (placementPreview != null) placementPreview.gameObject.SetActive(false);
        if (sequence != null) StopMotion(sequence);
        sequence = null;
        if (pickupReturnSequence != null) StopMotion(pickupReturnSequence);
        pickupReturnSequence = null;
        if (armReturnSequence != null) StopMotion(armReturnSequence);
        armReturnSequence = null;
        armReturning = false;
        if (transferReturnSequence != null) StopMotion(transferReturnSequence);
        transferReturnSequence = null;
        transferArmReturning = false;
        collisionPartRoot = null;
        collisionPartMeshes = null;
        collisionMeshVertices.Clear();
        collisionMeshTriangles.Clear();
        collisionLocalVertexBuffers.Clear();
        PickupClampReturning = false;
        bentFlaps = null;
        if (weldingArc != null) weldingArc.SetActive(false);
        if (sheet != rawSheetInstance)
        {
            sheet.SetParent(null, true);
            Destroy(sheet.gameObject);
            sheet = rawSheetInstance;
            rawSheetInstance.gameObject.SetActive(true);
        }
        workpieceRoot.SetParent(workpieceHomeParent, true);
        workpieceRoot.localRotation = workpieceHomeLocalRotation;
        workpieceRoot.localScale = workpieceHomeLocalScale;
        rawSheetInstance.localPosition = rawSheetHomeLocalPosition;
        rawSheetInstance.localRotation = rawSheetHomeLocalRotation;
        rawSheetInstance.localScale = rawSheetHomeLocalScale;
        rawSheetInstance.gameObject.SetActive(false);
        materialPickupPoint.position = pickupPointHome;
        materialLiftPoint.position = liftPointHome;
        suctionMover.position = suctionMoverHome;
        clampMover.position = clampMoverHome;
        punchClampMover.position = punchMoverHome;
        RestorePositions(suctionVisuals, suctionVisualHome);
        suctionVerticalGuide.position = suctionGuideHome;
        suctionGear.localRotation = suctionGearHomeRotation;
        RestorePositions(clampVisuals, clampVisualHome);
        RestorePositions(punchClampVisuals, punchVisualHome);
        armBaseMover.position = armBaseHome;
        RestorePositions(armMovingVisuals, armVisualHomePositions);
        for (int i = 0; i < armMovingVisuals.Length; i++)
            armMovingVisuals[i].rotation = armVisualHomeRotations[i];
        armRig.localScale = Vector3.one;
        armYawJoint.localRotation = armYawHomeRotation;
        armPickupPlannedYaw = 0f;
        armThirdJoint.localRotation = armThirdHomeRotation;
        armFourthJoint.localRotation = armFourthHomeRotation;
        armSixthJoint.localRotation = armSixthHomeRotation;
        armWristRig.localRotation = armWristHomeLocalRotation;
        armWristRig.localScale = armWristHomeLocalScale;
        weldingArmRig.localRotation = Quaternion.identity;
        weldingArmRig.localScale = Vector3.one;
        transferBaseMover.position = transferHomeBase;
        transferArmRig.position = transferHomeArmRigPosition;
        transferArmRig.rotation = Quaternion.identity;
        transferArmRig.localScale = Vector3.one;
        ApplyTransferArmPose(transferHomePose, transferHomePose, 1f,
            transferHomeWristRotation);
        RestorePositions(transferMovingParts, transferHomePositions);
        for (int i = 0; i < transferMovingParts.Length; i++)
            transferMovingParts[i].rotation = transferHomeRotations[i];
        transferWristRig.rotation = transferHomeWristRotation;
        for (int i = 0; i < conveyorRollers.Length; i++)
        {
            conveyorRollers[i].position = conveyorHomePositions[i];
            conveyorRollers[i].rotation = conveyorHomeRotations[i];
        }
        hTurntableRig.localRotation = Quaternion.identity;
        RestorePositions(bendingUpperVisuals, bendingUpperHomePositions);
        RestoreLocalPositions(punchJawsA, punchJawAHome);
        RestoreLocalPositions(punchJawsB, punchJawBHome);
        if (clampJawA != null) clampJawA.localPosition = jawAHome;
        if (clampJawB != null) clampJawB.localPosition = jawBHome;
        SuctionActive = false;
        PickupClampActive = false;
        PunchClampActive = false;
        SetState(LoadingState.Idle, materialsRemaining > 0
            ? "靠近原料堆按 F 取板，然后到拾取机床原料台按 F 放置"
            : "原料已用完");
    }

    private IEnumerator RunLoadingProcess()
    {
        SetState(LoadingState.MoveSuctionToMaterial, "吸盘沿导轨移动到原料上方");
        Vector3 aboveMaterial = new Vector3(materialPickupPoint.position.x,
            suctionMover.position.y, materialPickupPoint.position.z);
        yield return MoveSuctionHorizontally(aboveMaterial, suctionMoveSpeed);
        if (sequence == null) yield break;

        SetState(LoadingState.LowerSuction, "吸盘下降到板材表面");
        yield return MoveSuctionVertically(materialPickupPoint.position,
            suctionVerticalSpeed);

        SetState(LoadingState.AttachMaterial, "吸盘吸附板材");
        SuctionActive = true;
        ReparentWithoutJump(workpieceRoot, suctionMover);

        SetState(LoadingState.RaiseMaterial, "吸盘提升板材");
        yield return MoveSuctionVertically(new Vector3(materialLiftPoint.position.x,
                suctionCarryHeight, materialLiftPoint.position.z),
            suctionVerticalSpeed);

        SetState(LoadingState.MoveToTransferPoint, "吸盘沿导轨搬运到交接区");
        Vector3 aboveTransfer = new Vector3(transferPoint.position.x,
            suctionCarryHeight, transferPoint.position.z);
        yield return MoveSuctionHorizontally(aboveTransfer, suctionMoveSpeed);
        if (sequence == null) yield break;

        SetState(LoadingState.LowerToTransferPoint, "吸盘将板材降至送料支撑位置");
        yield return MoveSuctionVertically(transferPoint.position,
            suctionVerticalSpeed);

        SetState(LoadingState.ClampMaterial, "夹爪靠近并夹紧板材边缘");
        Vector3 rightEdgePickup = PickupTargetAtSheetRightEdge();
        if (!RailTargetValid(clampMover.position, rightEdgePickup,
            pickupClampHomePoint, pickupRailEndPoint, "拾取机床夹爪")) yield break;
        yield return MoveCarrier(clampMover, clampVisuals, rightEdgePickup,
            clampMoveSpeed);
        yield return CloseJaws();
        PickupClampActive = true;
        ReparentWithoutJump(workpieceRoot, clampMover);

        SetState(LoadingState.ReleaseSuction, "夹爪已接管，吸盘释放板材");
        SuctionActive = false;

        SetState(LoadingState.RaiseSuctionClearance, "吸盘架升到夹爪梁上方");
        Vector3 initialClearance = new Vector3(transferPoint.position.x,
            Mathf.Max(suctionMover.position.y, suctionCarryHeight), transferPoint.position.z);
        yield return MoveSuctionVertically(initialClearance, suctionVerticalSpeed);

        SetState(LoadingState.ClearClampFromSuction, "夹爪带板材退出吸盘架升降范围");
        Vector3 feedAxis = pickupRailEndPoint.position - pickupRailStartPoint.position;
        feedAxis.y = 0f;
        feedAxis.Normalize();
        Bounds suctionEnvelope = GetBounds(suctionVisuals[0]);
        Bounds jawEnvelope = GetBounds(clampJawA);
        jawEnvelope.Encapsulate(GetBounds(clampJawB));
        float suctionFront = Vector3.Dot(suctionEnvelope.center, feedAxis)
            + HorizontalRadius(suctionEnvelope, feedAxis);
        float jawRear = Vector3.Dot(jawEnvelope.center, feedAxis)
            - HorizontalRadius(jawEnvelope, feedAxis);
        float clearanceTravel = Mathf.Max(clampClearanceDistance,
            suctionFront - jawRear + 0.08f);
        Vector3 clampClearTarget = clampMover.position + feedAxis * clearanceTravel;
        if (!RailTargetValid(clampMover.position, clampClearTarget,
            pickupClampHomePoint, pickupRailEndPoint, "拾取机床夹爪")) yield break;
        yield return MoveCarrier(clampMover, clampVisuals, clampClearTarget, feedSpeed);

        SetState(LoadingState.RaiseSuction, "吸盘架保持避让高度");
        Vector3 transferLift = new Vector3(transferPoint.position.x,
            Mathf.Max(suctionMover.position.y, suctionCarryHeight), transferPoint.position.z);
        yield return MoveSuctionVertically(transferLift,
            suctionVerticalSpeed);

        SetState(LoadingState.ReturnSuctionHome, "吸盘返回待机位置");
        Vector3 homeAbove = new Vector3(suctionHome.position.x, transferLift.y,
            suctionHome.position.z);
        yield return MoveSuctionHorizontally(homeAbove,
            suctionMoveSpeed);
        if (sequence == null) yield break;
        yield return MoveSuctionVertically(suctionHome.position,
            suctionVerticalSpeed);

        SetState(LoadingState.PickupFeed, "拾取机床夹爪沿 3.6m 导轨送料");
        if (!RailTargetValid(clampMover.position, pickupRailEndPoint.position,
            pickupClampHomePoint, pickupRailEndPoint, "拾取机床夹爪")) yield break;
        yield return MoveCarrier(clampMover, clampVisuals, pickupRailEndPoint.position, feedSpeed);
        SetState(LoadingState.PickupRailEndReached, "拾取机床夹爪在自身导轨末端停止");
        // 交接标记跟随板材的实际到位位置；只移动标记，不移动工件。
        transferToPunchPoint.position = SheetBottomCenter();

        SetState(LoadingState.PickupClampRelease, "拾取机床夹爪松开板材");
        yield return MoveJawPair(clampJawA, clampJawB, false, jawAHome, jawBHome);
        PickupClampActive = false;
        ReparentWithoutJump(workpieceRoot, workpieceHomeParent);
        // The pickup machine has finished its handoff. Return it on its own
        // coroutine while the released workpiece continues toward the punch jaws.
        PickupClampReturning = true;
        pickupReturnSequence = StartMotion(ReturnPickupClampHome());

        SetState(LoadingState.PlaceSheetOnPunchTable, "板材落到冲床左侧桌面");
        Vector3 tablePosition = workpieceRoot.position;
        tablePosition.y += punchTable.bounds.max.y + 0.002f - SheetBottomCenter().y;
        yield return MoveWorkpiece(tablePosition, feedSpeed);

        SetState(LoadingState.SlideSheetToPunchClamp, "板材沿桌面平移到夹爪位置");
        Vector3 sheetTarget = SheetTargetAtPunchJaws();
        yield return MoveWorkpiece(sheetTarget, feedSpeed);
        transferToPunchPoint.position = SheetBottomCenter();

        SetState(LoadingState.PunchClampAcquire, "冲床两套夹爪闭合并接管板材");
        yield return MoveJawPairs(punchJawsA, punchJawsB, true);
        PunchClampActive = true;
        SetState(LoadingState.TransferToPunchClamp, "板材控制权切换到冲床夹爪");
        ReparentWithoutJump(workpieceRoot, punchClampMover);

        SetState(LoadingState.PunchFeed, "两套冲床夹爪沿模型 Y 轴将板材送入右侧保护罩");
        if (punchRightGuard == null)
        {
            Fail("未找到转塔冲床右侧保护罩 r1472");
            yield break;
        }
        Bounds guardBounds = GetBounds(punchRightGuard);
        // CAD/model Y is the horizontal Unity Z direction for this imported FBX.
        // Keep world Y (height) unchanged while both assemblies carry the sheet.
        float feedDistanceZ = guardBounds.center.z - SheetBottomCenter().z;
        if (Mathf.Abs(feedDistanceZ) < rawSheetWorldWidth * 0.5f)
        {
            Fail(string.Format("模型 Y 轴送料距离仅 {0:F3}m；请检查 r1472 与板材位置",
                Mathf.Abs(feedDistanceZ)));
            yield break;
        }
        Vector3 punchDestination = punchClampMover.position
            + Vector3.forward * SafePunchClampTravel(feedDistanceZ, guardBounds);
        float clampTravelZ = punchDestination.z - punchClampMover.position.z;
        punchRailStartPoint.position = punchClampMover.position;
        punchRailEndPoint.position = punchDestination;
        if (Mathf.Abs(clampTravelZ) > positionTolerance
            && !RailTargetValid(punchClampMover.position, punchDestination,
            punchRailStartPoint, punchRailEndPoint, "冲床夹爪")) yield break;
        Vector3 deliveryPoint = SheetBottomCenter() + Vector3.forward * feedDistanceZ;
        punchStartPoint.position = deliveryPoint;
        Debug.Log(string.Format("冲床送料：模型 Y/Unity Z，夹爪 Z {0:F3} -> {1:F3}，板材目标 Z {2:F3}，r1472 Z 范围 {3:F3}..{4:F3}，夹爪安全行程 {5:F3}m",
            punchClampMover.position.z, punchDestination.z,
            deliveryPoint.z, guardBounds.min.z, guardBounds.max.z,
            Mathf.Abs(clampTravelZ)), this);
        if (Mathf.Abs(clampTravelZ) > positionTolerance)
            yield return MoveCarrier(punchClampMover, punchClampVisuals,
                punchDestination, punchFeedSpeed);

        SetState(LoadingState.PunchClampRelease, "冲床夹爪在保护罩前松开板材");
        yield return MoveJawPairs(punchJawsA, punchJawsB, false);
        PunchClampActive = false;
        ReparentWithoutJump(workpieceRoot, workpieceHomeParent);

        float remainingZ = deliveryPoint.z - SheetBottomCenter().z;
        if (Mathf.Abs(remainingZ) > positionTolerance)
        {
            SetState(LoadingState.SlideSheetIntoPunchGuard,
                "板材沿桌面继续移动到保护罩中间，电线支板保持在罩外");
            yield return MoveWorkpiece(workpieceRoot.position
                + Vector3.forward * remainingZ, punchFeedSpeed);
        }
        SetState(LoadingState.PunchStartPosition, "板材到达转塔冲床右侧保护罩内");
        if (PickupClampActive || workpieceRoot.parent != workpieceHomeParent
            || Vector3.Distance(SheetBottomCenter(), punchStartPoint.position) > positionTolerance * 2f)
        {
            Fail("板材未到达 r1472 中间或工件控制权异常");
            yield break;
        }

        SetState(LoadingState.Punching, "板材在转塔下冲压，停留 " + punchHoldSeconds.ToString("0.#") + " 秒");
        yield return WaitMotion(punchHoldSeconds);
        if (!ReplaceSheetWithFinishedPart()) yield break;

        yield return ReplayGate("PunchUnload");
        SetState(LoadingState.RetrieveFinishedPart, "成品沿模型 Y 轴移出保护罩，交给冲床夹爪");
        Vector3 retrievalPoint = SheetBottomCenter();
        retrievalPoint.z -= remainingZ;
        yield return MoveWorkpiece(workpieceRoot.position
            + Vector3.forward * (retrievalPoint.z - SheetBottomCenter().z), punchFeedSpeed);
        yield return MoveJawPairs(punchJawsA, punchJawsB, true);
        PunchClampActive = true;
        ReparentWithoutJump(workpieceRoot, punchClampMover);

        SetState(LoadingState.PunchClampPullBack, "两套冲床夹爪沿模型 Y 轴拉回成品");
        if (Mathf.Abs(clampTravelZ) > positionTolerance
            && !RailTargetValid(punchClampMover.position, punchMoverHome,
            punchRailStartPoint, punchRailEndPoint, "冲床夹爪拉回")) yield break;
        if (Mathf.Abs(clampTravelZ) > positionTolerance)
            yield return MoveCarrier(punchClampMover, punchClampVisuals,
                punchMoverHome, punchClampMoveSpeed);

        Vector3 xRailEnd;
        if (!TryGetPunchXRailEnd(out xRailEnd)) yield break;
        SetState(LoadingState.PunchClampMoveToRailEnd, "两套冲床夹爪沿模型 X 轴导轨送出成品");
        yield return MoveCarrier(punchClampMover, punchClampVisuals,
            xRailEnd, punchClampMoveSpeed);
        SetState(LoadingState.FinishedPartDelivered, "成品已到达模型 X 轴导轨尽头");
        yield return MoveJawPairs(punchJawsA, punchJawsB, false);
        PunchClampActive = false;
        ReparentWithoutJump(workpieceRoot, workpieceHomeParent);
        SetState(LoadingState.PunchClampReturn, "成品留在 r1490 尽头，两套冲床夹爪沿 X 轴返回原位");
        yield return MoveCarrier(punchClampMover, punchClampVisuals,
            punchMoverHome, punchClampMoveSpeed);

        Vector3 tableEdgeBottom;
        if (!TryGetFinishedPartTableEdge(out tableEdgeBottom)) yield break;
        if (ReplayMode) tableEdgeBottom.y += ReplayOutputLift;
        SetState(LoadingState.SlideFinishedPartToTableEdge,
            "冲压成品平移到桌子左 r1414 边缘");
        yield return MoveWorkpiece(workpieceRoot.position
            + Vector3.up * (tableEdgeBottom.y - SheetBottomCenter().y), feedSpeed);
        yield return MoveWorkpiece(workpieceRoot.position
            + tableEdgeBottom - SheetBottomCenter(), feedSpeed);

        yield return ReplayGate("PunchOutput");
        Vector3 armDestination;
        if (!TryGetArmRailEnd(out armDestination)) yield break;
        SetState(LoadingState.ArmBaseTravel, "滑槽基座 r872 沿 r775 移至桌面侧端点前安全位置");
        yield return MoveCarrier(armBaseMover, armMovingVisuals,
            armDestination, armBaseMoveSpeed);
        yield return ReplayGate("PickupDock");
        Vector3 pivot = armYawJoint.position;
        SetState(LoadingState.ArmRotateToPart,
            "机械臂2 r851 绕基座水平回转，上臂探向冲床成品");
        yield return RotateArmAroundBase(pivot, armPickupPlannedYaw);

        SetState(LoadingState.ArmSuctionPickup, "机械臂吸盘接触并吸附冲压成品");
        Vector3 productTop = SheetTopCenter();
        float approachHeight = productTop.y
            + Mathf.Max(armLiftHeight, 0.25f) + 0.02f;
        Vector3 contact = SuctionContactPoint();
        yield return MovePickupArmTo(new Vector3(contact.x, approachHeight, contact.z));
        if (sequence == null) yield break;
        yield return LevelPickupWrist();
        if (sequence == null) yield break;
        yield return MovePickupArmTo(new Vector3(productTop.x, approachHeight, productTop.z));
        if (sequence == null) yield break;
        yield return MovePickupArmTo(productTop + Vector3.up * 0.005f);
        if (sequence == null) yield break;
        ReparentWithoutJump(workpieceRoot, armWristRig);
        if (!AlignFinishedPartWithCupTips()) yield break;
        SetState(LoadingState.ArmLiftPart, "机械臂吸盘抬起冲压成品");
        yield return MovePickupArmTo(SuctionContactPoint()
            + Vector3.up * Mathf.Max(armLiftHeight, 0.35f));
        if (sequence == null) yield break;

        SetState(LoadingState.ArmRestorePosture, "机械臂携成品恢复取件前姿态");
        yield return RestorePickupArmJoints();
        yield return ReplayGate("Carry");

        Vector3 probeRailDestination;
        if (!TryGetArmRailPositionForBendingMold(out probeRailDestination)) yield break;
        SetState(LoadingState.ArmBaseToBendingMold,
            "滑槽基座 r872 沿 r775 移到下模 r1547 方位");
        yield return MoveCarrier(armBaseMover, armMovingVisuals,
            probeRailDestination, armBaseMoveSpeed);
        yield return ReplayGate("BendDock");
        yield return RunBendingSequence();
        if (CurrentState == LoadingState.BendingComplete)
        {
            yield return ReplayGate("BendOutput");
            yield return DeliverBentPartToHClamps();
            if (sequence != null && CurrentState == LoadingState.ArmReturnToRailEnd)
            {
                yield return RunWeldingSequence();
                if (sequence != null && CurrentState == LoadingState.WeldingComplete)
                {
                    while (sequence != null && armReturning) yield return null;
                }
                if (sequence != null && CurrentState == LoadingState.WeldingComplete)
                {
                    if (ReplayMode && ReplayUseCarrierForWeld)
                    {
                        if (!PlanReplayConveyorDestination()) yield break;
                        yield return ReplayGate("WeldComplete");
                        yield return ReplayCarrierToConveyor();
                    }
                    else yield return RunPostWeldTransfer();
                }
                if (CurrentState == LoadingState.TransferComplete)
                    SetState(LoadingState.ReadyForPunching,
                        finishedProductPlaced
                            ? "成品已放入蓝色区域；回控制台按 E 准备下一件"
                            : finishedProductCarried
                                ? "携带成品前往蓝色区域，按虚影位置放置"
                                : "成品已到传送带末端；靠近成品按 F 拾取");
            }
        }
        yield return ReplayGate("Complete");
        sequence = null;
    }

    private IEnumerator ReturnPickupClampHome()
    {
        yield return MoveCarrier(clampMover, clampVisuals,
            pickupClampHomePoint.position, clampMoveSpeed);
        PickupClampReturning = false;
        pickupReturnSequence = null;
    }

    private void BindTransferArm()
    {
        armLongSlot = FindModelNode(775);
        Transform bendingBeam = FindModelNode(37);
        bendingProbe = bendingBeam != null ? FindModelNode(bendingBeam, 43) : null;
        armBaseAssembly = FindModelNode(869);
        armSuctionAssembly = FindModelNode(854);
        armBaseJoint = armBaseAssembly != null ? FindModelNode(armBaseAssembly, 878) : null;
        armSegments = new[] { FindModelNode(839), FindModelNode(842),
            FindModelNode(845), FindModelNode(848), FindModelNode(851) };
        armMovingVisuals = new[] { armBaseAssembly, armSegments[0], armSegments[1],
            armSegments[2], armSegments[3], armSegments[4], armSuctionAssembly };
        armSliders = armBaseAssembly != null
            ? new[] { FindModelNode(armBaseAssembly, 875),
                FindModelNode(armBaseAssembly, 879),
                FindModelNode(armBaseAssembly, 880),
                FindModelNode(armBaseAssembly, 881) }
            : null;
    }

    private bool BindBendingUpperVisuals()
    {
        if (bendingUpper == null || bendingUpperMold == null)
            return Fail("折弯机上模 r121/r1565 缺失");
        Transform upperAssembly = FindModelNode(118);
        if (upperAssembly == null)
            return Fail("折弯机上模夹总成 r118 缺失");
        bendingUpperVisuals = new Transform[BendingUpperClampIds.Length + 2];
        bendingUpperVisuals[0] = bendingUpper;
        bendingUpperVisuals[1] = bendingUpperMold;
        for (int i = 0; i < BendingUpperClampIds.Length; i++)
        {
            Transform clamp = FindModelNode(upperAssembly, BendingUpperClampIds[i]);
            if (clamp == null)
                return Fail("折弯机上模夹缺少 r" + BendingUpperClampIds[i]);
            bendingUpperVisuals[i + 2] = clamp;
        }
        var roots = new System.Collections.Generic.List<Transform>();
        foreach (Transform candidate in bendingUpperVisuals)
        {
            bool movedByParent = false;
            foreach (Transform other in bendingUpperVisuals)
            {
                if (candidate == other || !candidate.IsChildOf(other)) continue;
                movedByParent = true;
                break;
            }
            if (!movedByParent) roots.Add(candidate);
        }
        bendingUpperMovingRoots = roots.ToArray();
        return true;
    }

    private void BindWeldingEquipment()
    {
        hTurntableAssembly = FindModelNode(723);
        hTurntable = FindModelNode(726);
        hClampHeads = new[] { FindModelNode(1563), FindModelNode(1564),
            FindModelNode(1560), FindModelNode(1553),
            FindModelNode(1556), FindModelNode(1562),
            FindModelNode(1559), FindModelNode(1561) };
        weldingBase = FindModelNode(822);
        weldingFirstLink = weldingBase != null
            ? FindModelNode(weldingBase, 828) : null;
        weldingGunAssembly = FindModelNode(1531);
        weldingGunNozzle = weldingGunAssembly != null
            ? FindModelNode(weldingGunAssembly, 1537) : null;
        // r828 is the pedestal connection inside r822. It must not be pitched
        // or uniformly scaled with the upper arm, or it cuts through r825.
        weldingMovingParts = new[] { FindModelNode(1528), FindModelNode(1529),
            FindModelNode(1527), FindModelNode(1530),
            FindModelNode(1526), weldingGunAssembly };
    }

    private void BindPostWeldEquipment()
    {
        transferBase = FindModelNode(1524);
        transferSuction = FindModelNode(1523);
        transferFirstLink = transferBase != null ? FindModelNode(transferBase, 878) : null;
        transferSegments = new[] { FindModelNode(1519), FindModelNode(1520),
            FindModelNode(1521), FindModelNode(1522), FindModelNode(1525) };
        transferMovingParts = new[] { transferSegments[0], transferSegments[1],
            transferSegments[2], transferSegments[3], transferSegments[4], transferSuction };
        transferSliders = transferBase != null ? new[] {
            FindModelNode(transferBase, 875), FindModelNode(transferBase, 879),
            FindModelNode(transferBase, 880), FindModelNode(transferBase, 881) } : null;
        transferHead = transferSuction != null ? FindModelNode(transferSuction, 863) : null;
        transferWristParts = transferSuction != null ? new[] {
            FindModelNode(transferSuction, 857), transferHead,
            FindModelNode(transferSuction, 860), FindModelNode(transferSuction, 866),
            FindModelNode(transferSuction, 867), FindModelNode(transferSuction, 868) } : null;
        conveyorAssembly = FindModelNode(485);
        conveyorPlate = conveyorAssembly != null ? FindModelNode(conveyorAssembly, 491) : null;
        conveyorShaft = conveyorAssembly != null ? FindModelNode(conveyorAssembly, 500) : null;
        if (conveyorAssembly == null) return;
        var rollers = new System.Collections.Generic.List<Transform>();
        foreach (Transform part in conveyorAssembly.GetComponentsInChildren<Transform>(true))
            if (part.name.Contains("传送带卷筒") || part.name.Contains("传送带皮带轮")
                || part.name.Contains("传送带卷筒轴"))
                rollers.Add(part);
        conveyorRollers = rollers.ToArray();
    }

    private bool SetupPostWeldRigs()
    {
        if (transferBase == null || transferFirstLink == null || transferSuction == null
            || transferHead == null || conveyorPlate == null || conveyorShaft == null
            || transferSliders == null || System.Array.Exists(transferSliders, p => p == null)
            || System.Array.Exists(transferMovingParts, p => p == null)
            || System.Array.Exists(transferWristParts, p => p == null)
            || conveyorRollers == null || conveyorRollers.Length == 0)
            return Fail("缺少焊后搬运机械臂吸盘或传送带卷筒模型");

        // The pedestal and first link stay fixed to the sliding base, as on the welding arm.
        transferArmRig = new GameObject("Post Weld Transfer Shoulder (runtime)").transform;
        transferArmRig.position = GetBounds(transferFirstLink).center;
        transferOriginalParents = new Transform[transferMovingParts.Length];
        for (int i = 0; i < transferMovingParts.Length; i++)
        {
            transferOriginalParents[i] = transferMovingParts[i].parent;
            transferMovingParts[i].SetParent(transferArmRig, true);
        }
        // r1522 is the first moving link attached to the fixed r878 base.
        // r1525 extends from r1522; rotating both about r1525 detached r1522
        // from the pedestal in the previous runtime hierarchy.
        transferYawJoint = CreateArmJoint("Transfer Arm Base Yaw r878",
            transferSegments[3].position, transferArmRig);
        transferShoulderJoint = CreateArmJoint("Transfer Arm Shoulder r1522",
            transferSegments[3].position, transferYawJoint);
        transferElbowJoint = CreateArmJoint("Transfer Arm Forward r1525",
            transferSegments[4].position, transferShoulderJoint);
        transferForearmJoint = CreateArmJoint("Transfer Arm Forearm r1521",
            transferSegments[2].position, transferElbowJoint);
        transferSegments[3].SetParent(transferShoulderJoint, true);
        transferSegments[4].SetParent(transferElbowJoint, true);
        transferSegments[2].SetParent(transferForearmJoint, true);
        transferSegments[1].SetParent(transferForearmJoint, true);
        transferSegments[0].SetParent(transferForearmJoint, true);
        transferSuction.SetParent(transferForearmJoint, true);
        transferHomePose = CurrentTransferArmPose();
        Vector3 cupDirection = Vector3.zero;
        Vector3 contact = Vector3.zero;
        for (int i = 2; i < transferWristParts.Length; i++)
            cupDirection += GetBounds(transferWristParts[i]).center - GetBounds(transferHead).center;
        cupDirection.Normalize();
        if (cupDirection.sqrMagnitude < 0.5f)
            return Fail("无法确定第二搬运机械臂吸盘朝向");
        for (int i = 2; i < transferWristParts.Length; i++)
        {
            Vector3 tip;
            if (!TryFindCupTip(transferWristParts[i], cupDirection, out tip))
                return Fail("第二搬运机械臂吸盘网格不可读");
            contact += tip;
        }
        contact /= transferWristParts.Length - 2;
        transferWristRig = new GameObject("Post Weld Wrist Joint (runtime)").transform;
        transferWristRig.position = transferWristParts[0].position;
        transferWristRig.SetParent(transferSuction, true);
        transferWristParents = new Transform[transferWristParts.Length];
        for (int i = 0; i < transferWristParts.Length; i++)
        {
            transferWristParents[i] = transferWristParts[i].parent;
            transferWristParts[i].SetParent(transferWristRig, true);
        }
        transferHomeWristRotation = transferWristRig.rotation;
        transferDownWristRotation = Quaternion.FromToRotation(cupDirection, Vector3.down)
            * transferHomeWristRotation;
        transferContactAnchor = new GameObject("Post Weld Cup Contact (runtime)").transform;
        transferContactAnchor.position = contact;
        transferContactAnchor.SetParent(transferWristRig, true);
        transferHomeArmRigPosition = transferArmRig.position;
        if (Vector3.Distance(transferArmRig.position, transferWristRig.position) < 0.1f)
            return Fail("第二搬运机械臂的肩部到吸盘距离无效");
        transferBaseMover = new GameObject("Post Weld Base Mover (runtime)").transform;
        transferHomeBase = GetBounds(transferBase).center;
        transferBaseMover.position = transferHomeBase;
        transferHomePositions = SavePositions(transferMovingParts);
        transferHomeRotations = SaveRotations(transferMovingParts);
        conveyorHomeRotations = SaveRotations(conveyorRollers);
        conveyorHomePositions = SavePositions(conveyorRollers);
        Vector3 near, far;
        if (!TryRailMeshEnds(conveyorShaft, out near, out far))
            return Fail("传送带卷筒轴 r500 缺少网格长轴");
        conveyorShaftAxis = (far - near).normalized;
        Transform firstDrum = FindModelNode(conveyorAssembly, 497);
        if (firstDrum == null) return Fail("传送带缺少首端卷筒 r497");
        Bounds drum = GetBounds(firstDrum);
        conveyorRollRadius = Mathf.Max(0.01f,
            Mathf.Min(drum.extents.x, Mathf.Min(drum.extents.y, drum.extents.z)));
        return true;
    }

    private bool SetupWeldingRigs()
    {
        if (hTurntableAssembly == null || hTurntable == null
            || weldingBase == null || weldingFirstLink == null
            || weldingGunNozzle == null
            || System.Array.Exists(hClampHeads, part => part == null)
            || System.Array.Exists(weldingMovingParts, part => part == null))
            return Fail("缺少 H 转台、夹头或焊接机械臂/焊枪 r1537 模型");
        Vector3 planePoint, planeNormal;
        if (!TryGetHTurntablePlane(out planePoint, out planeNormal)) return false;

        hTurntableRig = new GameObject("H Turntable Rotation (runtime)").transform;
        hTurntableRig.position = planePoint;
        turntableOriginalParents = new Transform[hClampHeads.Length + 1];
        turntableOriginalParents[0] = hTurntableAssembly.parent;
        hTurntableAssembly.SetParent(hTurntableRig, true);
        for (int i = 0; i < hClampHeads.Length; i++)
        {
            turntableOriginalParents[i + 1] = hClampHeads[i].parent;
            hClampHeads[i].SetParent(hTurntableRig, true);
        }

        MeshFilter nozzleMesh = weldingGunNozzle.GetComponent<MeshFilter>();
        if (nozzleMesh == null || nozzleMesh.sharedMesh == null
            || !nozzleMesh.sharedMesh.isReadable)
            return Fail("焊枪2 r1537 缺少可读取的枪头网格");
        Vector3[] vertices = nozzleMesh.sharedMesh.vertices;
        if (vertices.Length == 0) return Fail("焊枪2 r1537 枪头网格为空");
        float nearest = float.PositiveInfinity;
        foreach (Vector3 vertex in vertices)
        {
            Vector3 world = weldingGunNozzle.TransformPoint(vertex);
            float distance = (world - planePoint).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            weldingNozzleTipLocal = vertex;
        }

        weldingArmRig = new GameObject("Welding Shoulder Pivot (runtime)").transform;
        weldingArmRig.position = weldingMovingParts[0].position;
        weldingOriginalParents = new Transform[weldingMovingParts.Length];
        for (int i = 0; i < weldingMovingParts.Length; i++)
        {
            weldingOriginalParents[i] = weldingMovingParts[i].parent;
            weldingMovingParts[i].SetParent(weldingArmRig, true);
        }
        weldingHomeTipVector = weldingArmRig.InverseTransformPoint(
            weldingGunNozzle.TransformPoint(weldingNozzleTipLocal));
        if (weldingHomeTipVector.magnitude < 0.1f)
            return Fail("焊枪2 r1537 与机械臂基座过近，无法计算焊接轨迹");

        weldingArc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        weldingArc.name = "Welding Arc (runtime)";
        weldingArc.transform.SetParent(weldingGunNozzle, false);
        weldingArc.transform.localPosition = weldingNozzleTipLocal;
        Vector3 scale = weldingGunNozzle.lossyScale;
        weldingArc.transform.localScale = new Vector3(
            0.025f / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            0.025f / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            0.025f / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        Collider arcCollider = weldingArc.GetComponent<Collider>();
        if (arcCollider != null) Destroy(arcCollider);
        Shader shader = Shader.Find("Standard");
        if (shader != null)
        {
            weldingArcMaterial = new Material(shader);
            weldingArcMaterial.color = new Color(1f, 0.65f, 0.1f);
            weldingArcMaterial.EnableKeyword("_EMISSION");
            weldingArcMaterial.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.02f) * 4f);
            weldingArc.GetComponent<Renderer>().sharedMaterial = weldingArcMaterial;
        }
        Light light = weldingArc.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.7f, 0.35f);
        light.range = 0.35f;
        light.intensity = 2f;
        weldingArc.SetActive(false);
        return true;
    }

    private static Transform CreateArmJoint(string name, Vector3 pivot,
        Transform parent)
    {
        Transform joint = new GameObject(name + " (runtime)").transform;
        joint.position = pivot;
        joint.SetParent(parent, true);
        return joint;
    }

    private IEnumerator RunBendingSequence()
    {
        Bounds lower = GetBounds(bendingLowerMold);
        Bounds upper = GetBounds(bendingUpperMold);
        float openGap = upper.min.y - lower.max.y;
        if (openGap <= 0.02f)
        {
            Fail("折弯机上下模没有足够的初始开口");
            yield break;
        }
        Vector3 railNear, railFar;
        if (!TryRailMeshEnds(bendingRail, out railNear, out railFar))
        {
            Fail("无法计算折弯机的进料方向");
            yield break;
        }
        Vector3 outside = railFar - railNear;
        outside.y = 0f;
        if (outside.sqrMagnitude < 0.0001f)
        {
            Fail("折弯机导轨没有水平进料方向");
            yield break;
        }
        outside.Normalize();
        if (Vector3.Dot(armBaseMover.position - lower.center, outside) < 0f)
            outside = -outside;

        SetState(LoadingState.ArmApproachBendingMold,
            "机械臂在折弯机外侧抬起成品并朝模具弯曲");
        Bounds body = GetBounds(bendingBody);
        float outsideLimit = Vector3.Dot(body.center, outside)
            + HorizontalRadius(body, outside)
            + HorizontalRadius(GetBounds(sheet), outside) + 0.12f;
        float outsideGap = outsideLimit
            - Vector3.Dot(GetBounds(sheet).center, outside);
        if (outsideGap > 0f)
            yield return MoveArmSuctionTo(SuctionContactPoint() + outside * outsideGap);
        if (sequence == null) yield break;
        float lift = lower.max.y + Mathf.Min(openGap * 0.5f, armLiftHeight)
            - GetBounds(sheet).min.y;
        if (lift > 0f)
            yield return MoveArmSuctionTo(SuctionContactPoint() + Vector3.up * lift);
        if (sequence == null) yield break;

        Quaternion straightYaw = armYawJoint.localRotation;
        Quaternion straightThird = armThirdJoint.localRotation;
        Quaternion straightFourth = armFourthJoint.localRotation;
        Quaternion straightSixth = armSixthJoint.localRotation;
        ReparentWithoutJump(workpieceRoot, armWristRig);
        if (!ReplaceWithBendingParts()) yield break;

        Vector3[] edgeOffsets;
        Transform[] bendFlaps;
        Bounds centerPanel;
        if (!TryFindBendingFlaps(out bendFlaps, out centerPanel,
            out edgeOffsets))
        {
            Fail("折弯分件模型缺少中间板或四块独立边缘");
            yield break;
        }
        SetState(LoadingState.ArmApproachBendingMold,
            "固定尺寸关节带吸盘入模，产物保持水平");
        Vector3 cupCenter, cupNormal;
        Vector3 panelSize = centerPanel.size;
        Vector3 panelNormalLocal = panelSize.x <= panelSize.y
            ? (panelSize.x <= panelSize.z ? Vector3.right : Vector3.forward)
            : (panelSize.y <= panelSize.z ? Vector3.up : Vector3.forward);
        if (Vector3.Dot(sheet.TransformDirection(panelNormalLocal), Vector3.up) < 0f)
            panelNormalLocal = -panelNormalLocal;
        finishedTopLocalNormal = panelNormalLocal;
        Vector3 panelNormal = sheet.TransformDirection(panelNormalLocal);
        yield return RotateWristTo(Quaternion.FromToRotation(panelNormal,
            Vector3.up) * armWristRig.rotation);
        if (sequence == null) yield break;
        GetCupTipPlane(out cupCenter, out cupNormal);
        if (Vector3.Dot(sheet.TransformDirection(panelNormalLocal),
            Vector3.up) < 0.999f
            || Mathf.Abs(Vector3.Dot(cupNormal, Vector3.up)) < 0.995f)
        {
            Fail("折弯中心底板或吸盘未能保持水平");
            yield break;
        }
        Quaternion levelWrist = armWristRig.rotation;
        bentFlaps = bendFlaps;
        bentPanelBoundsLocal = centerPanel;
        float partRadius = Mathf.Max(GetBounds(sheet).size.x,
            GetBounds(sheet).size.z);
        float retractDistance = partRadius + 0.15f;
        float dieMinX = Mathf.Max(lower.min.x, upper.min.x);
        float dieMaxX = Mathf.Min(lower.max.x, upper.max.x);
        float dieMinZ = Mathf.Max(lower.min.z, upper.min.z);
        float dieMaxZ = Mathf.Min(lower.max.z, upper.max.z);
        if (dieMinX >= dieMaxX || dieMinZ >= dieMaxZ)
        {
            Fail("折弯机上下模在水平面没有重叠的压制区域");
            yield break;
        }
        Vector3 diePoint = new Vector3((dieMinX + dieMaxX) * 0.5f,
            lower.center.y, (dieMinZ + dieMaxZ) * 0.5f);
        diePoint.y = lower.max.y + bendingDieClearance;
        float approachY = lower.max.y + Mathf.Min(openGap * 0.5f, 0.1f);
        float sheetHeight = Mathf.Min(GetBounds(sheet).size.y, 0.03f);
        Vector3 finalHingeLocal = Vector3.zero;
        for (int edge = 0; edge < edgeOffsets.Length; edge++)
        {
            Vector3 localOutward = edgeOffsets[edge].normalized;
            MeshFilter flapMesh = bendFlaps[edge].GetComponent<MeshFilter>();
            if (flapMesh == null)
            {
                Fail("成品第 " + (edge + 1) + " 边缺少折弯网格");
                yield break;
            }
            Bounds flapLocalBounds = FilterBoundsInRoot(sheet, flapMesh);
            Vector3 hingeLocal = flapLocalBounds.center
                - Vector3.Scale(localOutward, flapLocalBounds.extents);
            finalHingeLocal = hingeLocal;
            SetState(LoadingState.PositionBendEdge,
                "吸盘头旋转，将成品第 " + (edge + 1) + "/4 边对准上下模");
            if (edge == 0)
            {
                Vector3 edgeDirection = sheet.TransformDirection(edgeOffsets[edge]);
                edgeDirection.y = 0f;
                if (edgeDirection.sqrMagnitude < 0.0001f)
                {
                    Fail("成品边缘方向异常，无法定位第 1 边");
                    yield break;
                }
                float yaw = Vector3.SignedAngle(edgeDirection, -outside,
                    Vector3.up);
                yield return RotateWristTo(Quaternion.AngleAxis(yaw, Vector3.up)
                    * armWristRig.rotation);
                if (sequence == null) yield break;
            }
            else
            {
                Vector3 fromHinge = sheet.TransformPoint(hingeLocal)
                    - armWristRig.position;
                Vector3 toDie = diePoint - armWristRig.position;
                fromHinge.y = toDie.y = 0f;
                if (fromHinge.sqrMagnitude < 0.0001f
                    || toDie.sqrMagnitude < 0.0001f)
                {
                    Fail("吸盘旋转中心与第 " + (edge + 1) + " 边铰线重合");
                    yield break;
                }
                float yaw = Vector3.SignedAngle(fromHinge, toDie, Vector3.up);
                yield return RotateWristTo(Quaternion.AngleAxis(yaw, Vector3.up)
                    * armWristRig.rotation);
                if (sequence == null) yield break;
            }
            Quaternion wristOrientation = armWristRig.rotation;
            if (edge == 0)
            {
                Vector3 approach = diePoint;
                approach.y = approachY;
                yield return MoveArmEdgeTo(hingeLocal, approach, wristOrientation);
                if (sequence == null) yield break;
            }
            yield return MoveArmEdgeTo(hingeLocal, diePoint, wristOrientation);
            if (sequence == null) yield break;
            GetCupTipPlane(out cupCenter, out cupNormal);
            if (Vector3.Dot(sheet.TransformDirection(panelNormalLocal),
                Vector3.up) < 0.999f
                || Mathf.Abs(Vector3.Dot(cupNormal, Vector3.up)) < 0.995f)
            {
                Fail("成品第 " + (edge + 1)
                    + " 边对模时底板或吸盘倾斜");
                yield break;
            }
            if (Vector3.Distance(sheet.TransformPoint(hingeLocal), diePoint)
                > positionTolerance * 5f)
            {
                Fail("成品第 " + (edge + 1) + " 边的铰线未对准折弯模具");
                yield break;
            }

            float stroke = upper.min.y
                - (lower.max.y + sheetHeight + bendingDieClearance);
            if (stroke <= positionTolerance)
            {
                Fail("上模无法对第 " + (edge + 1) + " 边完成闭合");
                yield break;
            }
            SetState(LoadingState.CloseBendingDie,
                "r121 带上模 r1565 下压第 " + (edge + 1) + "/4 边");
            Vector3 hinge = sheet.TransformPoint(hingeLocal);
            Vector3 foldAxis = Vector3.Cross(
                Vector3.up, sheet.TransformDirection(localOutward)).normalized;
            Vector3 outerLocal = flapLocalBounds.center
                + Vector3.Scale(localOutward, flapLocalBounds.extents);
            Vector3 outerPointOnFlap = bendFlaps[edge].InverseTransformPoint(
                sheet.TransformPoint(outerLocal));
            if (ArmBodyIntersectsFoldPath(bendFlaps[edge], hinge, foldAxis))
            {
                Fail("成品第 " + (edge + 1)
                    + " 边的折弯轨迹会与机械臂穿模");
                yield break;
            }
            yield return MoveBendingUpper(stroke, bendFlaps[edge], hinge, foldAxis);
            Vector3 foldedOutward = bendFlaps[edge].TransformPoint(
                outerPointOnFlap) - hinge;
            if (foldedOutward.sqrMagnitude < 0.000001f
                || Vector3.Dot(foldedOutward.normalized, Vector3.down) < 0.995f)
            {
                Fail("成品第 " + (edge + 1) + " 边未与水平底板竖直相交");
                yield break;
            }
            if (bendingPressHold > 0f)
                yield return WaitMotion(bendingPressHold);
            SetState(LoadingState.OpenBendingDie,
                "折弯机上部抬起，上下模打开");
            yield return MoveBendingUpper(0f);
        }
        Vector3 retract = sheet.TransformPoint(finalHingeLocal)
            + outside * retractDistance;
        retract.y = approachY;
        yield return MoveArmEdgeTo(finalHingeLocal, retract,
            armWristRig.rotation);
        if (sequence == null) yield break;
        yield return RestoreBendingJoints(straightYaw, straightThird, straightFourth,
            straightSixth, levelWrist);
        if (sequence == null) yield break;
        SetState(LoadingState.BendingComplete, "四个突出边缘已依次完成上下模闭合");
    }

    private IEnumerator DeliverBentPartToHClamps()
    {
        if (bentFlaps == null || bentFlaps.Length != 4)
        {
            Fail("四边折弯成品缺少立边分件，无法放入 H 夹头");
            yield break;
        }
        Vector3 turntablePoint, turntableNormal;
        if (!TryGetHTurntablePlane(out turntablePoint, out turntableNormal))
            yield break;
        Vector3[] gaps;
        HClampGap[] clampGaps;
        float clampTop;
        if (!TryGetHClampGaps(turntableNormal, out gaps,
            out clampGaps, out clampTop)) yield break;
        Vector3 flapCenter = Vector3.zero;
        foreach (Transform flap in bentFlaps)
            flapCenter += GetBounds(flap).center;
        flapCenter *= 0.25f;
        float flapSide = Vector3.Dot(
            sheet.InverseTransformPoint(flapCenter) - bentPanelBoundsLocal.center,
            finishedTopLocalNormal);
        if (Mathf.Abs(flapSide) < 0.001f)
        {
            Fail("四条折弯立边没有离开中心板平面，无法确定朝下方向");
            yield break;
        }
        Vector3 downwardLocalNormal = finishedTopLocalNormal
            * Mathf.Sign(flapSide);
        Vector3 panelUnderLocal = bentPanelBoundsLocal.center
            + Vector3.Scale(downwardLocalNormal,
                bentPanelBoundsLocal.extents);
        Vector3 centerLocal = sheet.InverseTransformPoint(flapCenter);
        float clearance = Mathf.Max(0.01f, hClampApproachClearance);
        float safeCenterY = Mathf.Max(clampTop, turntablePoint.y)
            + GetBounds(sheet).extents.magnitude + clearance;
        if (flapCenter.y < safeCenterY)
        {
            Vector3 raised = flapCenter;
            raised.y = safeCenterY;
            yield return MoveArmEdgeTo(centerLocal, raised, armWristRig.rotation);
            if (sequence == null) yield break;
        }
        Vector3 productNormal = sheet.TransformDirection(downwardLocalNormal);
        yield return RotateWristTo(Quaternion.FromToRotation(productNormal,
            -turntableNormal) * armWristRig.rotation);
        if (sequence == null) yield break;
        if (Vector3.Dot(sheet.TransformDirection(downwardLocalNormal),
            -turntableNormal) < 0.999f)
        {
            Fail("四条折弯立边未能朝下指向 H 转台 r726 台面");
            yield break;
        }

        Vector3 targetCenter = Vector3.zero;
        foreach (Vector3 gap in gaps) targetCenter += gap;
        targetCenter *= 0.25f;
        Vector3 railAxis;
        float minimumTravel, maximumTravel;
        if (!TryGetArmRailLimits(out railAxis, out minimumTravel,
            out maximumTravel)) yield break;
        float projectedTravel = Vector3.Dot(targetCenter
            - sheet.TransformPoint(centerLocal), railAxis);
        float travel = Mathf.Clamp(projectedTravel, minimumTravel, maximumTravel);
        ReplayHTravelDestination = armBaseMover.position + railAxis * travel;
        yield return ReplayGate("HTravelReady");
        SetState(LoadingState.ArmToHClamps,
            "折弯成品抬高，滑槽基座移向四组 H 夹头");
        yield return MoveCarrier(armBaseMover, armMovingVisuals,
            armBaseMover.position + railAxis * travel, armBaseMoveSpeed);
        yield return ReplayGate("HDock");

        float yaw;
        float fitError;
        if (!TryFitBentFlapsToGaps(gaps, turntableNormal,
            out yaw, out fitError)) yield break;
        yield return RotateWristTo(Quaternion.AngleAxis(yaw, turntableNormal)
            * armWristRig.rotation);
        if (sequence == null) yield break;

        PickupArmPose parkPose = CurrentArmPose();
        Vector3 approach;
        if (!TryBentFlapPlacementTarget(targetCenter, turntablePoint,
                turntableNormal, centerLocal, out approach)) yield break;
        approach += turntableNormal * clearance;
        approach.y = Mathf.Max(approach.y,
            clampTop + sheet.TransformPoint(centerLocal).y
            - GetBounds(sheet).min.y + clearance);
        Quaternion orientation = armWristRig.rotation;
        yield return MoveArmEdgeTo(centerLocal, approach, orientation);
        if (sequence == null) yield break;
        if (!TryBentFlapPlacementTarget(targetCenter, turntablePoint,
                turntableNormal, centerLocal, out targetCenter)) yield break;
        SetState(LoadingState.PlaceBentPartInHClamps,
            "四条立边朝下落在 H 转台台面并进入夹头间隙");
        yield return MoveArmEdgeTo(centerLocal, targetCenter, orientation);
        if (sequence == null) yield break;
        for (int correction = 0; correction < 3; correction++)
        {
            float lowest;
            if (!TryGetLowestBentFlapProjection(turntableNormal,
                    out lowest)) yield break;
            float gap = lowest - Vector3.Dot(turntablePoint,
                turntableNormal);
            if (Mathf.Abs(gap - 0.002f) <= 0.004f) break;
            targetCenter = sheet.TransformPoint(centerLocal)
                + turntableNormal * (0.002f - gap);
            yield return MoveArmEdgeTo(centerLocal, targetCenter, orientation);
            if (sequence == null) yield break;
        }
        float lowestFlap;
        if (!TryGetLowestBentFlapProjection(turntableNormal,
                out lowestFlap)) yield break;
        float bottomGap = lowestFlap - Vector3.Dot(turntablePoint,
            turntableNormal);
        float protrusion;
        if (!AreBentFlapsInsideGaps(clampGaps, out protrusion))
        {
            Fail("成品立边未全部进入四组 H 夹头间隙（最大越界 "
                + protrusion.ToString("F3") + "m），已保留在吸盘上");
            yield break;
        }
        if (Mathf.Abs(bottomGap - 0.002f) > 0.01f
            || Vector3.Dot(sheet.TransformDirection(downwardLocalNormal),
                -turntableNormal) < 0.999f)
        {
            Fail("折弯立边末端未落在 H 转台 r726 台面，已保留在吸盘上");
            yield break;
        }
        float panelUnderHeight = Vector3.Dot(
            sheet.TransformPoint(panelUnderLocal) - turntablePoint,
            turntableNormal);
        if (panelUnderHeight < 0.01f)
        {
            Fail("中心板没有被四条向下的立边撑在台面上方，已保留在吸盘上");
            yield break;
        }
        for (int i = 0; i < bentFlaps.Length; i++)
        {
            float lowest, highest;
            if (!TryGetProjectedMeshInterval(bentFlaps[i], turntableNormal,
                    out lowest, out highest)
                || Mathf.Abs(lowest - Vector3.Dot(turntablePoint,
                    turntableNormal) - 0.002f) > 0.015f
                || highest < lowest + 0.01f)
            {
                Fail("第 " + (i + 1)
                    + " 条折弯立边没有向下立在 H 转台台面，已保留在吸盘上");
                yield break;
            }
        }
        ReparentWithoutJump(workpieceRoot, hTurntableRig);
        SetState(LoadingState.ArmReturnToRailEnd,
            "成品留在 H 夹头内；机械臂返回右端，同时开始四条接缝焊接");
        yield return ReplayGate("WeldInput");
        if (ReplayMode && ReplayUseCarrierForWeld)
        {
            yield return ReplayParkAtWeld(parkPose, orientation, GetBounds(sheet).size.y + clearance);
            yield break;
        }
        armReturning = true;
        armReturnSequence = StartMotion(ReturnArmToRailEnd(parkPose,
            orientation, GetBounds(sheet).size.y + clearance));
    }

    private IEnumerator ReturnArmToRailEnd(PickupArmPose parkPose,
        Quaternion orientation, float lift)
    {
        try
        {
            yield return MoveArmSuctionTo(SuctionContactPoint()
                + Vector3.up * lift);
            if (sequence == null) yield break;
            yield return RestoreBendingJoints(parkPose.yaw, parkPose.third,
                parkPose.fourth, parkPose.sixth, orientation);
            if (sequence == null) yield break;

            Vector3 rightEnd;
            if (!TryGetArmRailRightEnd(out rightEnd)) yield break;
            yield return MoveCarrier(armBaseMover, armMovingVisuals,
                rightEnd, armBaseMoveSpeed);
        }
        finally
        {
            armReturning = false;
            armReturnSequence = null;
        }
    }

    private IEnumerator RunPostWeldTransfer()
    {
        if (workpieceRoot.parent != hTurntableRig)
        {
            Fail("焊接结束后成品未停留在 H 转台上");
            yield break;
        }
        Transform basePanel = sheet.Find("FinishedPartSection1");
        if (basePanel == null)
        {
            Fail("无法定位成品底座中心面");
            yield break;
        }
        Bounds panel = GetBounds(basePanel);
        Vector3 pickup = new Vector3(panel.center.x, panel.max.y + 0.003f,
            panel.center.z);
        float lift = Mathf.Max(transferLiftClearance,
            GetBounds(sheet).size.y + transferLiftClearance);
        Vector3 abovePickup = pickup + Vector3.up *
            Mathf.Min(lift, Mathf.Max(0.08f, transferLiftClearance));
        Vector3 raisedPickup = pickup + Vector3.up * lift;
        Vector3 railAxis;
        float minTravel, maxTravel;
        if (!TryGetPostWeldRailLimits(out railAxis, out minTravel, out maxTravel))
            yield break;
        yield return FindTransferRailStopAsync(abovePickup, pickup,
            raisedPickup, railAxis, minTravel, maxTravel);
        if (sequence == null) yield break;
        if (!transferRailStopFound)
        {
            Fail("导轨行程内没有可从上方吸取并抬起成品的固定尺寸关节姿态");
            yield break;
        }
        float pickupTravel = transferRailStopTravel;
        SetState(LoadingState.TransferArmApproach,
            "第二搬运机械臂沿导轨移动，吸盘对准焊接成品底座");
        yield return MoveCarrier(transferBaseMover,
            new[] { transferBase, transferArmRig },
            transferBaseMover.position + railAxis * pickupTravel, transferArmMoveSpeed);
        yield return MovePostWeldTipTo(abovePickup, transferArmMoveSpeed);
        if (sequence == null || CurrentState != LoadingState.TransferArmApproach) yield break;
        yield return MovePostWeldTipTo(pickup, transferArmMoveSpeed * 0.65f);
        if (sequence == null || CurrentState != LoadingState.TransferArmApproach) yield break;
        SetState(LoadingState.TransferArmPickup,
            "机械臂吸盘头 r863__2 吸取成品底座");
        ReparentWithoutJump(workpieceRoot, transferWristRig);
        yield return MovePostWeldTipTo(raisedPickup, transferArmMoveSpeed);
        if (sequence == null || CurrentState != LoadingState.TransferArmPickup) yield break;

        Vector3 beltNear, beltFar;
        float beltHeight;
        if (!TryGetConveyorEndpoints(out beltNear, out beltFar, out beltHeight))
            yield break;
        Vector3 beltDirection = (beltFar - beltNear).normalized;
        Vector3 beltContact = beltNear;
        beltContact.y = beltHeight + (PostWeldCupTipPosition().y
            - GetBounds(sheet).min.y) + 0.005f;
        Vector3 beltApproach = beltContact + Vector3.up * lift;
        Vector3 beltRetract = beltApproach;
        yield return FindTransferRailStopAsync(beltApproach, beltContact,
            beltRetract, railAxis, minTravel - pickupTravel,
            maxTravel - pickupTravel);
        if (sequence == null) yield break;
        if (!transferRailStopFound)
        {
            Fail("导轨行程内没有可放件并退出传送带的固定尺寸关节姿态");
            yield break;
        }
        float placementTravel = transferRailStopTravel;
        SetState(LoadingState.TransferArmCarry,
            "第二搬运机械臂携带成品移向传送带起点");
        yield return MoveCarrier(transferBaseMover,
            new[] { transferBase, transferArmRig },
            transferBaseMover.position + railAxis * placementTravel, transferArmMoveSpeed);
        yield return MovePostWeldTipTo(beltApproach, transferArmMoveSpeed);
        if (sequence == null || CurrentState != LoadingState.TransferArmCarry) yield break;
        SetState(LoadingState.PlaceOnConveyor,
            "机械臂在传送带底板 r491 起点放下成品");
        yield return MovePostWeldTipTo(beltContact, transferArmMoveSpeed * 0.65f);
        if (sequence == null || CurrentState != LoadingState.PlaceOnConveyor) yield break;
        ReparentWithoutJump(workpieceRoot, workpieceHomeParent);
        float rollAlignment = Vector3.Dot(
            Vector3.Cross(conveyorShaftAxis, Vector3.up), beltDirection);
        if (Mathf.Abs(rollAlignment) < 0.5f)
        {
            Fail("传送带卷筒轴与送料方向不垂直");
            yield break;
        }
        transferArmReturning = true;
        transferReturnSequence = StartMotion(ReturnPostWeldArm(beltRetract));
        SetState(LoadingState.ConveyorRunning,
            "传送带送料；第二搬运机械臂同步退避并回正");
        float rollSign = Mathf.Sign(rollAlignment);
        float distance = Vector3.Distance(beltNear, beltFar);
        float travelled = 0f;
        while (travelled < distance && sequence != null)
        {
            float step = Mathf.Min(distance - travelled,
                Mathf.Max(0.01f, conveyorFeedSpeed) * MotionDelta);
            workpieceRoot.position += beltDirection * step;
            float degrees = rollSign * step / conveyorRollRadius * Mathf.Rad2Deg;
            foreach (Transform roller in conveyorRollers)
                roller.RotateAround(GetBounds(roller).center, conveyorShaftAxis, degrees);
            travelled += step;
            yield return null;
        }
        if (sequence == null) yield break;
        if (transferArmReturning)
        {
            SetState(LoadingState.TransferArmReturn,
                "成品已到传送带末端，等待机械臂完成回正");
            while (sequence != null && transferArmReturning) yield return null;
        }
        if (sequence == null) yield break;
        SetState(LoadingState.TransferComplete,
            "焊后吸取、传送带送料和搬运机械臂归位完成");
    }

    private IEnumerator ReturnPostWeldArm(Vector3 retract)
    {
        try
        {
            yield return MovePostWeldTipTo(retract, transferArmMoveSpeed);
            if (sequence == null) yield break;
            yield return RestorePostWeldArmPose();
            if (sequence == null) yield break;
            yield return MoveCarrier(transferBaseMover,
                new[] { transferBase, transferArmRig }, transferHomeBase,
                transferArmMoveSpeed);
        }
        finally
        {
            transferArmReturning = false;
            transferReturnSequence = null;
        }
    }

    private bool TryGetPostWeldRailLimits(out Vector3 axis,
        out float minTravel, out float maxTravel)
    {
        axis = Vector3.zero;
        minTravel = float.NegativeInfinity;
        maxTravel = float.PositiveInfinity;
        Vector3 near, far;
        if (!TryRailMeshEnds(armLongSlot, out near, out far))
            return Fail("无法读取第二搬运机械臂导轨");
        axis = far - near;
        axis.y = 0f;
        axis.Normalize();
        if (axis.sqrMagnitude < 0.5f) return Fail("第二搬运机械臂导轨方向无效");
        Bounds rail = GetBounds(armLongSlot);
        float center = Vector3.Dot(rail.center, axis);
        float radius = HorizontalRadius(rail, axis);
        foreach (Transform slider in transferSliders)
        {
            Bounds b = GetBounds(slider);
            float pos = Vector3.Dot(b.center, axis);
            float half = HorizontalRadius(b, axis);
            minTravel = Mathf.Max(minTravel, center - radius + 0.03f - pos + half);
            maxTravel = Mathf.Min(maxTravel, center + radius - 0.03f - pos - half);
        }
        return minTravel <= maxTravel || Fail("第二搬运机械臂滑块超出导轨");
    }

    private IEnumerator FindTransferRailStopAsync(Vector3 approach,
        Vector3 contact, Vector3 retreat, Vector3 railAxis,
        float minTravel, float maxTravel)
    {
        transferRailStopFound = false;
        Vector3 originalPosition = transferArmRig.position;
        TransferArmPose originalPose = CurrentTransferArmPose();
        Quaternion originalWrist = transferWristRig.rotation;
        float preferred = Mathf.Clamp(Vector3.Dot(contact
            - PostWeldCupTipPosition(), railAxis), minTravel, maxTravel);
        float bestScore = float.PositiveInfinity;
        try
        {
            for (int sample = 0; sample <= 16; sample++)
            {
                float travel = sample == 0 ? preferred
                    : Mathf.Lerp(minTravel, maxTravel, (sample - 1) / 15f);
                transferArmRig.position = originalPosition + railAxis * travel;
                ApplyTransferArmPose(originalPose, originalPose, 1f,
                    transferDownWristRotation);
                TransferArmPose approachPose, contactPose, retreatPose;
                bool reachable = TrySolvePostWeldArmPose(approach,
                    transferDownWristRotation, out approachPose);
                if (reachable)
                {
                    ApplyTransferArmPose(approachPose, approachPose, 1f,
                        transferDownWristRotation);
                    reachable = TrySolvePostWeldArmPose(contact,
                        transferDownWristRotation, out contactPose);
                }
                else contactPose = new TransferArmPose();
                if (reachable)
                {
                    ApplyTransferArmPose(contactPose, contactPose, 1f,
                        transferDownWristRotation);
                    reachable = TrySolvePostWeldArmPose(retreat,
                        transferDownWristRotation, out retreatPose);
                }
                else retreatPose = new TransferArmPose();
                if (reachable)
                {
                    float score = Mathf.Abs(travel - preferred);
                    if (score < bestScore)
                    {
                        transferRailStopFound = true;
                        transferRailStopTravel = travel;
                        bestScore = score;
                    }
                }
                transferArmRig.position = originalPosition;
                ApplyTransferArmPose(originalPose, originalPose, 1f,
                    originalWrist);
                if (sample == 0 && reachable) yield break;
                yield return null;
            }
        }
        finally
        {
            transferArmRig.position = originalPosition;
            ApplyTransferArmPose(originalPose, originalPose, 1f,
                originalWrist);
        }
    }

    private bool TryGetConveyorEndpoints(out Vector3 near, out Vector3 far,
        out float surface)
    {
        near = far = Vector3.zero;
        surface = 0f;
        if (!TryRailMeshEnds(conveyorPlate, out near, out far))
            return Fail("无法读取传送带底板 r491 的输送方向");
        Vector3 axis = far - near;
        axis.y = 0f;
        float length = axis.magnitude;
        if (length < 0.2f) return Fail("传送带底板 r491 长度无效");
        axis /= length;
        Vector3 center = GetBounds(conveyorPlate).center;
        float inset = Mathf.Min(length * 0.35f,
            HorizontalRadius(GetBounds(sheet), axis) + 0.04f);
        near = center - axis * (length * 0.5f - inset);
        far = center + axis * (length * 0.5f - inset);
        if ((far - sheet.position).sqrMagnitude < (near - sheet.position).sqrMagnitude)
        { Vector3 swap = near; near = far; far = swap; }
        surface = Mathf.Max(GetBounds(conveyorPlate).max.y,
            GetBounds(FindModelNode(conveyorAssembly, 497)).max.y);
        return true;
    }

    private TransferArmPose CurrentTransferArmPose()
    {
        return new TransferArmPose {
            yaw = transferYawJoint.localRotation,
            shoulder = transferShoulderJoint.localRotation,
            elbow = transferElbowJoint.localRotation,
            forearm = transferForearmJoint.localRotation };
    }

    private void ApplyTransferArmPose(TransferArmPose start,
        TransferArmPose goal, float fraction, Quaternion wristWorld)
    {
        transferYawJoint.localRotation = Quaternion.Slerp(start.yaw,
            goal.yaw, fraction);
        transferShoulderJoint.localRotation = Quaternion.Slerp(start.shoulder,
            goal.shoulder, fraction);
        transferElbowJoint.localRotation = Quaternion.Slerp(start.elbow,
            goal.elbow, fraction);
        transferForearmJoint.localRotation = Quaternion.Slerp(start.forearm,
            goal.forearm, fraction);
        transferWristRig.rotation = wristWorld;
    }

    private void RotateTransferPitchJoint(Transform joint, Vector3 target,
        Vector3 axis, ref float travel, float limit)
    {
        Vector3 from = Vector3.ProjectOnPlane(PostWeldCupTipPosition()
            - joint.position, axis);
        Vector3 to = Vector3.ProjectOnPlane(target - joint.position, axis);
        if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f)
            return;
        float step = Mathf.Clamp(Vector3.SignedAngle(from, to, axis), -10f, 10f);
        float next = Mathf.Clamp(travel + step, -limit, limit);
        joint.RotateAround(joint.position, axis, next - travel);
        travel = next;
    }

    private bool TrySolvePostWeldArmPose(Vector3 target,
        Quaternion wristWorld, out TransferArmPose result)
    {
        result = new TransferArmPose();
        TransferArmPose start = CurrentTransferArmPose();
        Quaternion savedWrist = transferWristRig.rotation;
        float best = 0.025f;
        try
        {
            for (int seed = 0; seed < 5; seed++)
            {
                ApplyTransferArmPose(start, start, 1f, wristWorld);
                float yawTravel = 0f, shoulderTravel = 0f;
                float elbowTravel = 0f, forearmTravel = 0f;
                Vector3 initialFrom = Vector3.ProjectOnPlane(
                    PostWeldCupTipPosition() - transferYawJoint.position,
                    Vector3.up);
                Vector3 initialTo = Vector3.ProjectOnPlane(
                    target - transferYawJoint.position, Vector3.up);
                if (initialFrom.sqrMagnitude > 0.0001f
                    && initialTo.sqrMagnitude > 0.0001f)
                {
                    yawTravel = Mathf.Clamp(Vector3.SignedAngle(initialFrom,
                        initialTo, Vector3.up), -170f, 170f);
                    transferYawJoint.RotateAround(transferYawJoint.position,
                        Vector3.up, yawTravel);
                    transferWristRig.rotation = wristWorld;
                }
                if (seed != 0)
                {
                    Vector3 forward = Vector3.ProjectOnPlane(
                        PostWeldCupTipPosition() - transferYawJoint.position,
                        Vector3.up);
                    if (forward.sqrMagnitude < 0.0001f) continue;
                    Vector3 axis = Vector3.Cross(forward.normalized,
                        Vector3.up).normalized;
                    elbowTravel = (seed % 2 == 0 ? 1f : -1f)
                        * (seed <= 2 ? 30f : 60f);
                    transferElbowJoint.RotateAround(
                        transferElbowJoint.position, axis, elbowTravel);
                    transferWristRig.rotation = wristWorld;
                }
                for (int iteration = 0; iteration < 120; iteration++)
                {
                    if (Vector3.Distance(PostWeldCupTipPosition(), target)
                        < 0.008f) break;
                    Vector3 forward = Vector3.ProjectOnPlane(
                        PostWeldCupTipPosition() - transferYawJoint.position,
                        Vector3.up);
                    if (forward.sqrMagnitude < 0.0001f) break;
                    Vector3 pitchAxis = Vector3.Cross(forward.normalized,
                        Vector3.up).normalized;
                    RotateTransferPitchJoint(transferForearmJoint, target,
                        pitchAxis, ref forearmTravel, 150f);
                    transferWristRig.rotation = wristWorld;
                    RotateTransferPitchJoint(transferElbowJoint, target,
                        pitchAxis, ref elbowTravel, 150f);
                    transferWristRig.rotation = wristWorld;
                    RotateTransferPitchJoint(transferShoulderJoint, target,
                        pitchAxis, ref shoulderTravel, 120f);
                    transferWristRig.rotation = wristWorld;
                    Vector3 from = Vector3.ProjectOnPlane(
                        PostWeldCupTipPosition() - transferYawJoint.position,
                        Vector3.up);
                    Vector3 to = Vector3.ProjectOnPlane(
                        target - transferYawJoint.position, Vector3.up);
                    if (from.sqrMagnitude < 0.0001f
                        || to.sqrMagnitude < 0.0001f) break;
                    float step = Mathf.Clamp(Vector3.SignedAngle(from, to,
                        Vector3.up), -10f, 10f);
                    float next = Mathf.Clamp(yawTravel + step, -170f, 170f);
                    transferYawJoint.RotateAround(transferYawJoint.position,
                        Vector3.up, next - yawTravel);
                    yawTravel = next;
                    transferWristRig.rotation = wristWorld;
                }
                float distance = Vector3.Distance(PostWeldCupTipPosition(), target);
                if (distance >= best) continue;
                result = CurrentTransferArmPose();
                best = distance;
                if (best < 0.008f) break;
            }
            return best < 0.025f;
        }
        finally
        {
            ApplyTransferArmPose(start, start, 1f, savedWrist);
        }
    }

    private Vector3 PostWeldCupTipPosition()
    {
        return transferContactAnchor.position;
    }

    private IEnumerator MovePostWeldTipTo(Vector3 target, float speed)
    {
        TransferArmPose startPose = CurrentTransferArmPose();
        Quaternion wristStart = transferWristRig.rotation;
        TransferArmPose goal;
        if (!TrySolvePostWeldArmPose(target, transferDownWristRotation,
            out goal))
        {
            Fail("固定尺寸焊后搬运机械臂无法到达吸盘目标");
            yield break;
        }
        Vector3 start = PostWeldCupTipPosition();
        float length = Vector3.Distance(start, target);
        float travelled = 0f;
        while (travelled < length)
        {
            travelled = Mathf.Min(length, travelled + Mathf.Max(0.01f, speed)
                * MotionDelta);
            float fraction = length < 0.0001f ? 1f : travelled / length;
            ApplyTransferArmPose(startPose, goal,
                fraction, Quaternion.Slerp(wristStart,
                    transferDownWristRotation, fraction));
            yield return null;
        }
        ApplyTransferArmPose(startPose, goal, 1f, transferDownWristRotation);
    }

    private IEnumerator RestorePostWeldArmPose()
    {
        TransferArmPose start = CurrentTransferArmPose();
        Quaternion wristStart = transferWristRig.rotation;
        float duration = Mathf.Max(
            Quaternion.Angle(start.yaw, transferHomePose.yaw),
            Quaternion.Angle(start.shoulder, transferHomePose.shoulder),
            Quaternion.Angle(start.elbow, transferHomePose.elbow),
            Quaternion.Angle(start.forearm, transferHomePose.forearm),
            Quaternion.Angle(wristStart, transferHomeWristRotation))
            / Mathf.Max(1f, armRotationSpeed);
        duration = Mathf.Max(duration, 0.01f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Mathf.Min(duration, elapsed + MotionDelta);
            float fraction = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            ApplyTransferArmPose(start, transferHomePose, fraction,
                Quaternion.Slerp(wristStart, transferHomeWristRotation,
                    fraction));
            yield return null;
        }
        ApplyTransferArmPose(transferHomePose, transferHomePose, 1f,
            transferHomeWristRotation);
    }

    private IEnumerator RunWeldingSequence()
    {
        if (bentFlaps == null || bentFlaps.Length != 4
            || workpieceRoot.parent != hTurntableRig)
        {
            Fail("成品没有固定在 H 转台上，无法开始四条接缝焊接");
            yield break;
        }
        Vector3 planePoint, planeNormal;
        if (!TryGetHTurntablePlane(out planePoint, out planeNormal)) yield break;
        Vector3[] corners = GetWeldingCornersLocal();
        Vector3 panelCenter = sheet.TransformPoint(bentPanelBoundsLocal.center);
        Vector3 station = Vector3.zero;
        float nearest = float.PositiveInfinity;
        foreach (Vector3 corner in corners)
        {
            Vector3 world = sheet.TransformPoint(corner);
            float distance = (world - WeldingTipPosition()).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            station = world;
        }

        bool[] welded = new bool[4];
        for (int pass = 0; pass < 4; pass++)
        {
            int selected = -1;
            nearest = float.PositiveInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                if (welded[i]) continue;
                Vector3 world = sheet.TransformPoint(corners[i]);
                float distance = (world - station).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                selected = i;
            }
            if (selected < 0)
            {
                Fail("未找到下一条待焊接的立边接缝");
                yield break;
            }
            welded[selected] = true;
            Vector3 top = sheet.TransformPoint(corners[selected]);
            float height = Vector3.Dot(top - planePoint, planeNormal);
            if (height < 0.04f)
            {
                Fail("立边接缝高度不足，无法沿缝隙移动焊枪");
                yield break;
            }
            Vector3 bottom = top - planeNormal * (height - 0.015f);
            Vector3 outward = Vector3.ProjectOnPlane(top - panelCenter,
                planeNormal).normalized;
            if (outward.sqrMagnitude < 0.5f)
            {
                Fail("无法确定立边交接处的焊枪接近方向");
                yield break;
            }
            Vector3 clearance = outward * weldingTipClearance;
            Vector3 approach = top + clearance
                + outward * weldingApproachClearance
                + planeNormal * weldingApproachClearance;
            SetState(LoadingState.WeldingGunApproach,
                "焊接机械臂将焊枪2移到第 " + (pass + 1) + "/4 条立边接缝上方");
            yield return MoveWeldingTipTo(approach, weldingGunMoveSpeed);
            if (sequence == null) yield break;
            yield return MoveWeldingTipTo(top + clearance, weldingGunMoveSpeed);
            if (sequence == null) yield break;

            SetState(LoadingState.WeldingSeam,
                "焊枪2沿第 " + (pass + 1) + "/4 条立边交接缝焊接");
            weldingArc.SetActive(true);
            yield return MoveWeldingTipTo(bottom + clearance, weldingSeamSpeed);
            weldingArc.SetActive(false);
            if (sequence == null) yield break;
            yield return MoveWeldingTipTo(approach, weldingGunMoveSpeed);
            if (sequence == null) yield break;

            if (pass == 3) break;
            SetState(LoadingState.IndexHTurntable,
                "H 转台带夹头和成品旋转 90°，将下一条接缝转到焊枪位置");
            yield return RotateWeldingTurntable(planeNormal, 90f);
            planePoint = hTurntableRig.position;
            panelCenter = sheet.TransformPoint(bentPanelBoundsLocal.center);
        }
        yield return MoveWeldingTipTo(weldingArmRig.position
            + weldingHomeTipVector, weldingGunMoveSpeed);
        if (sequence == null) yield break;
        weldingArmRig.rotation = Quaternion.identity;
        weldingArmRig.localScale = Vector3.one;
        bentFlaps = null;
        SetState(LoadingState.WeldingComplete, "四条立边交接缝已完成模拟焊接");
    }

    private Vector3[] GetWeldingCornersLocal()
    {
        Vector3 face = finishedTopLocalNormal;
        float flapSide = Vector3.Dot(sheet.InverseTransformPoint(
            (GetBounds(bentFlaps[0]).center + GetBounds(bentFlaps[1]).center
             + GetBounds(bentFlaps[2]).center + GetBounds(bentFlaps[3]).center)
            * 0.25f) - bentPanelBoundsLocal.center, face);
        face *= Mathf.Sign(flapSide);
        Vector3 a = Mathf.Abs(face.x) > 0.5f ? Vector3.up : Vector3.right;
        Vector3 b = Vector3.Cross(face, a).normalized;
        Vector3 e = bentPanelBoundsLocal.extents;
        Vector3 center = bentPanelBoundsLocal.center + Vector3.Scale(face, e);
        float ea = Vector3.Dot(e, Abs(a));
        float eb = Vector3.Dot(e, Abs(b));
        return new[] { center + a * ea + b * eb,
            center - a * ea + b * eb,
            center - a * ea - b * eb,
            center + a * ea - b * eb };
    }

    private Vector3 WeldingTipPosition()
    {
        return weldingGunNozzle.TransformPoint(weldingNozzleTipLocal);
    }

    private bool AimWeldingArmAt(Vector3 target)
    {
        Vector3 offset = target - weldingArmRig.position;
        float scale = offset.magnitude / weldingHomeTipVector.magnitude;
        if (scale < 0.4f || scale > 1.8f)
            return Fail("焊枪目标超出焊接机械臂可用范围");
        weldingArmRig.rotation = Quaternion.FromToRotation(
            weldingHomeTipVector, offset);
        weldingArmRig.localScale = Vector3.one * scale;
        return true;
    }

    private IEnumerator MoveWeldingTipTo(Vector3 target, float speed)
    {
        Vector3 start = WeldingTipPosition();
        float distance = Vector3.Distance(start, target);
        float progress = 0f;
        while (progress < distance)
        {
            progress = Mathf.Min(distance, progress
                + Mathf.Max(0.01f, speed) * MotionDelta);
            if (!AimWeldingArmAt(Vector3.Lerp(start, target,
                    distance < 0.0001f ? 1f : progress / distance)))
                yield break;
            yield return null;
        }
        if (!AimWeldingArmAt(target)) yield break;
    }

    private IEnumerator RotateWeldingTurntable(Vector3 normal, float angle)
    {
        Quaternion destination = Quaternion.AngleAxis(angle, normal)
            * hTurntableRig.rotation;
        while (Quaternion.Angle(hTurntableRig.rotation, destination) > 0.05f)
        {
            hTurntableRig.rotation = Quaternion.RotateTowards(hTurntableRig.rotation,
                destination, Mathf.Max(1f, weldingTurntableSpeed) * MotionDelta);
            yield return null;
        }
        hTurntableRig.rotation = destination;
    }

    private bool TryGetHClampGaps(Vector3 surfaceNormal,
        out Vector3[] gaps, out HClampGap[] spaces, out float top)
    {
        int[,] ids = { { 1563, 1564 }, { 1560, 1553 },
            { 1556, 1562 }, { 1559, 1561 } };
        gaps = new Vector3[4];
        spaces = new HClampGap[4];
        top = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            Transform a = FindModelNode(ids[i, 0]);
            Transform b = FindModelNode(ids[i, 1]);
            if (a == null || b == null)
                return Fail("缺少第 " + (i + 1) + " 组 H 夹头模型");
            if (a.GetComponentInChildren<Renderer>() == null
                || b.GetComponentInChildren<Renderer>() == null)
                return Fail("第 " + (i + 1) + " 组 H 夹头缺少可测量的网格");
            Bounds first = GetBounds(a);
            Bounds second = GetBounds(b);
            Vector3 direction = second.center - first.center;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
                return Fail("第 " + (i + 1) + " 组 H 夹头没有水平间隙");
            direction.Normalize();
            Vector3 faceA = first.center
                + direction * HorizontalRadius(first, direction);
            Vector3 faceB = second.center
                - direction * HorizontalRadius(second, direction);
            if (Vector3.Dot(faceB - faceA, direction) <= 0f)
                return Fail("第 " + (i + 1) + " 组 H 夹头相向表面重叠");
            gaps[i] = (faceA + faceB) * 0.5f;
            Vector3 tangent = Vector3.Cross(surfaceNormal, direction).normalized;
            spaces[i].direction = direction;
            spaces[i].near = Vector3.Dot(faceA, direction);
            spaces[i].far = Vector3.Dot(faceB, direction);
            spaces[i].tangent = tangent;
            spaces[i].tangentMin = Mathf.Max(
                Vector3.Dot(first.center, tangent)
                    - HorizontalRadius(first, tangent),
                Vector3.Dot(second.center, tangent)
                    - HorizontalRadius(second, tangent));
            spaces[i].tangentMax = Mathf.Min(
                Vector3.Dot(first.center, tangent)
                    + HorizontalRadius(first, tangent),
                Vector3.Dot(second.center, tangent)
                    + HorizontalRadius(second, tangent));
            float sharedBottom = Mathf.Max(first.min.y, second.min.y);
            float sharedTop = Mathf.Min(first.max.y, second.max.y);
            gaps[i].y = sharedBottom <= sharedTop
                ? (sharedBottom + sharedTop) * 0.5f
                : (first.center.y + second.center.y) * 0.5f;
            spaces[i].center = gaps[i];
            top = Mathf.Max(top, first.max.y, second.max.y);
        }
        return true;
    }

    private bool AreBentFlapsInsideGaps(HClampGap[] spaces,
        out float protrusion)
    {
        protrusion = float.PositiveInfinity;
        if (bentFlaps == null || bentFlaps.Length != 4
            || spaces == null || spaces.Length != 4) return false;
        for (int a = 0; a < 4; a++)
        for (int b = 0; b < 4; b++)
        for (int c = 0; c < 4; c++)
        for (int d = 0; d < 4; d++)
        {
            if (a == b || a == c || a == d || b == c || b == d || c == d)
                continue;
            int[] assignment = { a, b, c, d };
            float worst = 0f;
            for (int i = 0; i < 4; i++)
            {
                HClampGap space = spaces[assignment[i]];
                float low, high, sideLow, sideHigh;
                if (!TryGetProjectedMeshInterval(bentFlaps[i],
                        space.direction, out low, out high)
                    || !TryGetProjectedMeshInterval(bentFlaps[i],
                        space.tangent, out sideLow, out sideHigh))
                    return false;
                worst = Mathf.Max(worst, space.near - low, high - space.far);
                if (space.tangentMin <= space.tangentMax)
                    worst = Mathf.Max(worst,
                        space.tangentMin - sideHigh,
                        sideLow - space.tangentMax);
            }
            protrusion = Mathf.Min(protrusion, worst);
        }
        return protrusion <= 0.005f;
    }

    private static bool TryGetProjectedMeshInterval(Transform root,
        Vector3 axis, out float minimum, out float maximum)
    {
        minimum = float.PositiveInfinity;
        maximum = float.NegativeInfinity;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3(x, y, z));
                float position = Vector3.Dot(
                    filter.transform.TransformPoint(corner), axis);
                minimum = Mathf.Min(minimum, position);
                maximum = Mathf.Max(maximum, position);
            }
        }
        return !float.IsInfinity(minimum);
    }

    private bool TryGetHTurntablePlane(out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;
        Transform turntable = FindModelNode(726);
        if (turntable == null)
            return Fail("缺少 H 转台 r726，无法确定成品底面姿态");
        MeshFilter filter = turntable.GetComponent<MeshFilter>();
        if (filter == null)
            filter = turntable.GetComponentInChildren<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return Fail("H 转台 r726 缺少可测量的台面网格");
        Bounds mesh = filter.sharedMesh.bounds;
        Vector3 localNormal = mesh.size.x < mesh.size.y
            ? (mesh.size.x < mesh.size.z ? Vector3.right : Vector3.forward)
            : (mesh.size.y < mesh.size.z ? Vector3.up : Vector3.forward);
        normal = filter.transform.TransformDirection(localNormal).normalized;
        if (Vector3.Dot(normal, Vector3.up) < 0f)
        {
            normal = -normal;
            localNormal = -localNormal;
        }
        if (Vector3.Dot(normal, Vector3.up) < 0.5f)
            return Fail("H 转台 r726 的台面法线不是朝上的平面");
        point = filter.transform.TransformPoint(mesh.center
            + Vector3.Scale(localNormal, mesh.extents));
        return true;
    }

    private bool TryBentFlapPlacementTarget(Vector3 gapCenter,
        Vector3 surfacePoint, Vector3 surfaceNormal, Vector3 centerLocal,
        out Vector3 target)
    {
        target = gapCenter;
        float lowest;
        if (!TryGetLowestBentFlapProjection(surfaceNormal, out lowest))
            return Fail("无法读取四条折弯立边的最低点");
        Vector3 currentCenter = sheet.TransformPoint(centerLocal);
        float correction = Vector3.Dot(surfacePoint, surfaceNormal)
            + 0.002f - lowest
            - Vector3.Dot(gapCenter - currentCenter, surfaceNormal);
        target = gapCenter + surfaceNormal * correction;
        return true;
    }

    private bool TryGetLowestBentFlapProjection(Vector3 axis,
        out float lowest)
    {
        lowest = float.PositiveInfinity;
        foreach (Transform flap in bentFlaps)
        {
            float minimum, maximum;
            if (!TryGetProjectedMeshInterval(flap, axis,
                out minimum, out maximum)) return false;
            lowest = Mathf.Min(lowest, minimum);
        }
        return !float.IsInfinity(lowest);
    }

    private bool TryFitBentFlapsToGaps(Vector3[] gaps, Vector3 planeNormal,
        out float yaw,
        out float error, bool allowRotation = true)
    {
        yaw = 0f;
        error = float.PositiveInfinity;
        if (bentFlaps == null || bentFlaps.Length != 4) return false;
        Vector3[] source = new Vector3[4];
        Vector3 sourceCenter = Vector3.zero;
        Vector3 gapCenter = Vector3.zero;
        for (int i = 0; i < 4; i++)
        {
            source[i] = GetBounds(bentFlaps[i]).center;
            sourceCenter += source[i];
            gapCenter += gaps[i];
        }
        sourceCenter *= 0.25f;
        gapCenter *= 0.25f;
        for (int a = 0; a < 4; a++)
        for (int b = 0; b < 4; b++)
        for (int c = 0; c < 4; c++)
        for (int d = 0; d < 4; d++)
        {
            if (a == b || a == c || a == d || b == c || b == d || c == d)
                continue;
            int[] assignment = { a, b, c, d };
            float dot = 0f, cross = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 from = Vector3.ProjectOnPlane(
                    source[i] - sourceCenter, planeNormal);
                Vector3 to = Vector3.ProjectOnPlane(
                    gaps[assignment[i]] - gapCenter, planeNormal);
                dot += Vector3.Dot(from, to);
                cross += Vector3.Dot(Vector3.Cross(from, to), planeNormal);
            }
            float angle = allowRotation
                ? Mathf.Atan2(cross, dot) * Mathf.Rad2Deg : 0f;
            Quaternion rotation = Quaternion.AngleAxis(angle, planeNormal);
            float squaredError = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 difference = Vector3.ProjectOnPlane(
                    rotation * (source[i] - sourceCenter)
                    - (gaps[assignment[i]] - gapCenter), planeNormal);
                squaredError += difference.sqrMagnitude;
            }
            float candidate = Mathf.Sqrt(squaredError * 0.25f);
            if (candidate >= error) continue;
            error = candidate;
            yaw = angle;
        }
        return !float.IsInfinity(error);
    }

    private bool TryGetArmRailRightEnd(out Vector3 destination)
    {
        destination = armBaseMover.position;
        Vector3 axis;
        float minimumTravel, maximumTravel;
        if (!TryGetArmRailLimits(out axis, out minimumTravel,
            out maximumTravel)) return false;
        Vector3 first = destination + axis * minimumTravel;
        Vector3 second = destination + axis * maximumTravel;
        if (Mathf.Abs(first.x - second.x) > 0.05f)
            destination = first.x < second.x ? first : second;
        else
        {
            Vector3 table = punchTable.bounds.center;
            destination = Vector3.Distance(first, table)
                > Vector3.Distance(second, table) ? first : second;
        }
        return true;
    }

    private IEnumerator RotateWristTo(Quaternion target)
    {
        Quaternion start = armWristRig.rotation;
        bool stationary = Quaternion.Angle(start, target) <= .01f;
        if (stationary) ReplaySkippedStationaryWristChecks += 36;
        double sliceStart = Time.realtimeSinceStartupAsDouble;
        armPlanningDepth++;
        try
        {
            // An already-level wrist has no swept arc. Check the current
            // geometry once, including any newly replaced bending panels.
            int samples = stationary ? 0 : 36;
            for (int step = 0; step <= samples; step++)
            {
                armWristRig.rotation = Quaternion.Slerp(start, target, samples > 0 ? (float)step / samples : 0);
                yield return CheckArmCollisionAsync(CurrentArmPose(), start);
                if (armCollisionFound)
                {
                    armWristRig.rotation = start;
                    Fail("吸盘旋转轨迹会使产物与机械臂穿模");
                    yield break;
                }
                if (Time.realtimeSinceStartupAsDouble - sliceStart >= ArmPlanningSliceSeconds)
                {
                    armWristRig.rotation = start;
                    yield return PlanningYield;
                    sliceStart = Time.realtimeSinceStartupAsDouble;
                }
            }
            armWristRig.rotation = start;
        }
        finally
        {
            armWristRig.rotation = start;
            armPlanningDepth--;
        }
        while (Quaternion.Angle(armWristRig.rotation, target) > 0.01f)
        {
            armWristRig.rotation = Quaternion.RotateTowards(armWristRig.rotation,
                target, armRotationSpeed * MotionDelta);
            yield return null;
        }
        armWristRig.rotation = target;
    }

    private IEnumerator RestoreBendingJoints(Quaternion yaw, Quaternion third,
        Quaternion fourth, Quaternion sixth, Quaternion wristWorld)
    {
        PickupArmPose start = CurrentArmPose();
        PickupArmPose goal = new PickupArmPose { yaw = yaw, third = third,
            fourth = fourth, sixth = sixth };
        yield return PreviewFixedArmPathAsync(start, goal, wristWorld,
            armWristRig.rotation);
        if (!fixedArmPathClear)
        {
            Fail("固定尺寸机械臂回位轨迹会与产物穿模");
            yield break;
        }
        float duration = Mathf.Max(
            Quaternion.Angle(start.yaw, yaw),
            Quaternion.Angle(start.third, third),
            Quaternion.Angle(start.fourth, fourth),
            Quaternion.Angle(start.sixth, sixth))
            / Mathf.Max(1f, armRotationSpeed);
        duration = Mathf.Max(duration, 0.01f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Mathf.Min(duration, elapsed + MotionDelta);
            ApplyArmPose(start, goal, Mathf.SmoothStep(0f, 1f, elapsed / duration), wristWorld);
            yield return null;
        }
        ApplyArmPose(start, goal, 1f, wristWorld);
    }

    private bool CacheCupTipPlane()
    {
        cupTipLocalPoints = new Vector3[4];
        Vector3 cupCenter = Vector3.zero;
        for (int i = 0; i < 4; i++)
            cupCenter += GetBounds(armWristVisuals[i + 2]).center;
        cupCenter *= 0.25f;
        Vector3 outward = cupCenter - GetBounds(armWristVisuals[1]).center;
        if (outward.sqrMagnitude < 0.000001f)
            outward = cupCenter - GetBounds(armWristStem).center;
        if (outward.sqrMagnitude < 0.000001f)
            return Fail("无法确定四个机械臂吸盘的末端方向");
        outward.Normalize();
        for (int i = 0; i < 4; i++)
        {
            Vector3 tip;
            if (!TryFindCupTip(armWristVisuals[i + 2], outward, out tip))
                return Fail("无法读取机械臂吸盘末端网格 r"
                    + new[] { 860, 866, 867, 868 }[i]);
            cupTipLocalPoints[i] = armWristRig.InverseTransformPoint(tip);
        }
        Vector3 center, normal;
        GetCupTipPlane(out center, out normal);
        return normal.sqrMagnitude > 0.5f
            || Fail("四个机械臂吸盘末端无法确定一个平面");
    }

    private static bool TryFindCupTip(Transform cup, Vector3 outward,
        out Vector3 tip)
    {
        tip = Vector3.zero;
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        MeshFilter[] filters = cup.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            foreach (Vector3 vertex in mesh.vertices)
            {
                float projection = Vector3.Dot(filter.transform.TransformPoint(vertex),
                    outward);
                min = Mathf.Min(min, projection);
                max = Mathf.Max(max, projection);
            }
        }
        if (float.IsInfinity(max)) return false;
        float threshold = max - Mathf.Max((max - min) * 0.01f, 0.0001f);
        int count = 0;
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 point = filter.transform.TransformPoint(vertex);
                if (Vector3.Dot(point, outward) < threshold) continue;
                tip += point;
                count++;
            }
        }
        if (count == 0) return false;
        tip /= count;
        return true;
    }

    private void GetCupTipPlane(out Vector3 center, out Vector3 normal)
    {
        Vector3[] tips = new Vector3[4];
        center = Vector3.zero;
        for (int i = 0; i < 4; i++)
        {
            tips[i] = armWristRig.TransformPoint(cupTipLocalPoints[i]);
            center += tips[i];
        }
        center *= 0.25f;
        normal = Vector3.Cross(tips[2] - tips[3], tips[1] - tips[0]);
        if (normal.sqrMagnitude < 0.000001f)
            normal = Vector3.Cross(tips[2] - tips[0], tips[3] - tips[0]);
        if (normal.sqrMagnitude < 0.000001f) return;
        normal.Normalize();
        Vector3 outward = center - GetBounds(armWristVisuals[1]).center;
        if (Vector3.Dot(normal, outward) < 0f) normal = -normal;
    }

    private bool AlignFinishedPartWithCupTips()
    {
        Bounds local;
        if (!TryGetLocalMeshBounds(sheet, out local))
            return Fail("冲压成品缺少可对齐吸盘的板面网格");
        Vector3 center, cupNormal;
        GetCupTipPlane(out center, out cupNormal);
        if (cupNormal.sqrMagnitude < 0.5f)
            return Fail("四个机械臂吸盘末端平面无效");
        if (Vector3.Dot(cupNormal, Vector3.down) < 0.995f)
            return Fail("吸取冲床产物时，吸盘末端未水平朝下");
        Vector3 faceLocal = local.center
            + Vector3.Scale(finishedTopLocalNormal, local.extents);
        Vector3 faceNormal = sheet.TransformDirection(finishedTopLocalNormal);
        if (Vector3.Dot(faceNormal, Vector3.up) < 0.995f)
            return Fail("冲床产物在吸取前未保持水平");
        sheet.rotation = Quaternion.FromToRotation(faceNormal, -cupNormal)
            * sheet.rotation;
        sheet.position += center + cupNormal * 0.001f
            - sheet.TransformPoint(faceLocal);
        if (Vector3.Dot(sheet.TransformDirection(finishedTopLocalNormal),
            Vector3.up) < 0.995f)
            return Fail("吸附后的冲床产物未保持水平");
        return true;
    }

    private Quaternion HorizontalPickupWristRotation()
    {
        Vector3 center, normal;
        GetCupTipPlane(out center, out normal);
        return Quaternion.FromToRotation(normal, Vector3.down)
            * armWristRig.rotation;
    }

    private IEnumerator LevelPickupWrist()
    {
        Vector3 center, normal;
        GetCupTipPlane(out center, out normal);
        if (normal.sqrMagnitude < 0.5f)
        {
            Fail("无法确定取件吸盘末端平面");
            yield break;
        }
        Quaternion target = HorizontalPickupWristRotation();
        while (Quaternion.Angle(armWristRig.rotation, target) > 0.01f)
        {
            armWristRig.rotation = Quaternion.RotateTowards(
                armWristRig.rotation, target, armRotationSpeed * MotionDelta);
            yield return null;
        }
        armWristRig.rotation = target;
    }

    private IEnumerator MoveArmEdgeTo(Vector3 localEdge, Vector3 targetEdge,
        Quaternion wristOrientation)
    {
        Vector3 currentEdge = sheet.TransformPoint(localEdge);
        if (Vector3.Distance(currentEdge, targetEdge) <= positionTolerance * 2f)
            yield break;
        Vector3 edgeOffset = currentEdge - armWristRig.position;
        yield return MoveFixedArmWristTo(targetEdge - edgeOffset,
            wristOrientation);
        if (sequence == null) yield break;
        if (Vector3.Distance(sheet.TransformPoint(localEdge), targetEdge)
            > positionTolerance * 5f)
            Fail("固定尺寸机械臂未能将产物边缘对准目标");
    }

    private IEnumerator MoveFixedArmWristTo(Vector3 target,
        Quaternion wristOrientation)
    {
        yield return SolveFixedArmPoseAsync(target, wristOrientation);
        if (sequence == null) yield break;
        if (!fixedArmSolved)
        {
            Fail("固定尺寸机械臂无法在不碰撞产物的情况下到达折弯目标");
            yield break;
        }
        PickupArmPose goal = fixedArmSolvedPose;
        yield return MoveFixedArmPoseTo(goal, target, wristOrientation);
    }

    // Execute a previously checked path without repeating its IK and swept
    // collision search. Used only for the same part, start pose and carrier frame.
    private IEnumerator MoveFixedArmPoseTo(PickupArmPose goal, Vector3 target,
        Quaternion wristOrientation)
    {
        PickupArmPose start = CurrentArmPose();
        float duration = Mathf.Max(
            Vector3.Distance(armWristRig.position, target)
                / Mathf.Max(0.01f, armPickupSpeed),
            Quaternion.Angle(start.yaw, goal.yaw)
                / Mathf.Max(1f, armRotationSpeed),
            Quaternion.Angle(start.third, goal.third)
                / Mathf.Max(1f, armRotationSpeed),
            Quaternion.Angle(start.fourth, goal.fourth)
                / Mathf.Max(1f, armRotationSpeed),
            Quaternion.Angle(start.sixth, goal.sixth)
                / Mathf.Max(1f, armRotationSpeed), 0.01f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Mathf.Min(duration, elapsed + MotionDelta);
            ApplyArmPose(start, goal, Mathf.SmoothStep(0f, 1f, elapsed / duration), wristOrientation);
            yield return null;
        }
        ApplyArmPose(start, goal, 1f, wristOrientation);
    }

    private PickupArmPose CurrentArmPose()
    {
        return new PickupArmPose { yaw = armYawJoint.localRotation,
            third = armThirdJoint.localRotation,
            fourth = armFourthJoint.localRotation,
            sixth = armSixthJoint.localRotation };
    }

    private void ApplyArmPose(PickupArmPose start, PickupArmPose goal,
        float fraction, Quaternion wristOrientation)
    {
        armYawJoint.localRotation = Quaternion.Slerp(start.yaw, goal.yaw, fraction);
        armThirdJoint.localRotation = Quaternion.Slerp(start.third, goal.third,
            fraction);
        armFourthJoint.localRotation = Quaternion.Slerp(start.fourth, goal.fourth,
            fraction);
        armSixthJoint.localRotation = Quaternion.Slerp(start.sixth, goal.sixth,
            fraction);
        armWristRig.rotation = wristOrientation;
    }

    private IEnumerator SolveFixedArmPoseAsync(Vector3 target,
        Quaternion wristOrientation)
    {
        armPlanningDepth++;
        try
        {
            fixedArmSolved = false;
            PickupArmPose start = CurrentArmPose();
            Quaternion savedWrist = armWristRig.rotation;
            float bestDistance = float.PositiveInfinity;
            double sliceStart = Time.realtimeSinceStartupAsDouble;
            try
            {
                for (int seed = 0; seed < 5; seed++)
                {
                    ApplyArmPose(start, start, 1f, wristOrientation);
                    float thirdTravel = 0f, fourthTravel = 0f;
                    float sixthTravel = 0f, yawTravel = 0f;
                    if (seed != 0)
                    {
                        Vector3 forward = Vector3.ProjectOnPlane(
                            armWristRig.position - armYawJoint.position, Vector3.up);
                        if (forward.sqrMagnitude < 0.0001f) continue;
                        Vector3 bendAxis = Vector3.Cross(forward.normalized,
                            Vector3.up).normalized;
                        fourthTravel = (seed % 2 == 0 ? 1f : -1f)
                            * (seed <= 2 ? 30f : 60f);
                        armFourthJoint.RotateAround(armFourthJoint.position,
                            bendAxis, fourthTravel);
                    }
                    for (int iteration = 0; iteration < 120; iteration++)
                    {
                        if (Time.realtimeSinceStartupAsDouble - sliceStart >= ArmPlanningSliceSeconds)
                        {
                            PickupArmPose pending = CurrentArmPose();
                            ApplyArmPose(start, start, 1f, savedWrist);
                            yield return PlanningYield;
                            ApplyArmPose(pending, pending, 1f, wristOrientation);
                            sliceStart = Time.realtimeSinceStartupAsDouble;
                        }
                        if (Vector3.Distance(armWristRig.position, target) < 0.008f)
                            break;
                        Vector3 forward = Vector3.ProjectOnPlane(
                            armWristRig.position - armYawJoint.position, Vector3.up);
                        if (forward.sqrMagnitude < 0.0001f) break;
                        Vector3 pitchAxis = Vector3.Cross(forward.normalized,
                            Vector3.up).normalized;
                        RotateFixedPitchJoint(armSixthJoint, target, pitchAxis,
                            ref sixthTravel, 150f);
                        RotateFixedPitchJoint(armFourthJoint, target, pitchAxis,
                            ref fourthTravel, 150f);
                        RotateFixedPitchJoint(armThirdJoint, target, pitchAxis,
                            ref thirdTravel, 120f);
                        Vector3 from = Vector3.ProjectOnPlane(
                            armWristRig.position - armYawJoint.position, Vector3.up);
                        Vector3 to = Vector3.ProjectOnPlane(
                            target - armYawJoint.position, Vector3.up);
                        if (from.sqrMagnitude < 0.0001f
                            || to.sqrMagnitude < 0.0001f) break;
                        float step = Mathf.Clamp(Vector3.SignedAngle(from, to,
                            Vector3.up), -10f, 10f);
                        float nextYaw = Mathf.Clamp(yawTravel + step, -170f, 170f);
                        armYawJoint.RotateAround(armYawJoint.position, Vector3.up,
                            nextYaw - yawTravel);
                        yawTravel = nextYaw;
                        armWristRig.rotation = wristOrientation;
                    }
                    float distance = Vector3.Distance(armWristRig.position, target);
                    if (distance < 0.009f && distance < bestDistance)
                    {
                        PickupArmPose candidate = CurrentArmPose();
                        yield return PreviewFixedArmPathAsync(start, candidate,
                            wristOrientation, savedWrist);
                        if (fixedArmPathClear)
                        {
                            fixedArmSolvedPose = candidate;
                            bestDistance = distance;
                            fixedArmSolved = true;
                            if (bestDistance < 0.008f) yield break;
                        }
                    }
                    ApplyArmPose(start, start, 1f, savedWrist);
                    if (Time.realtimeSinceStartupAsDouble - sliceStart >= ArmPlanningSliceSeconds)
                    {
                        yield return PlanningYield;
                        sliceStart = Time.realtimeSinceStartupAsDouble;
                    }
                }
            }
            finally
            {
                ApplyArmPose(start, start, 1f, savedWrist);
            }
        }
        finally { armPlanningDepth--; }
    }

    private void RotateFixedPitchJoint(Transform joint, Vector3 target,
        Vector3 axis, ref float travel, float limit)
    {
        Vector3 from = Vector3.ProjectOnPlane(armWristRig.position
            - joint.position, axis);
        Vector3 to = Vector3.ProjectOnPlane(target - joint.position, axis);
        if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f) return;
        float step = Mathf.Clamp(Vector3.SignedAngle(from, to, axis),
            -10f, 10f);
        float next = Mathf.Clamp(travel + step, -limit, limit);
        joint.RotateAround(joint.position, axis, next - travel);
        travel = next;
    }

    private IEnumerator PreviewFixedArmPathAsync(PickupArmPose start,
        PickupArmPose goal, Quaternion wristOrientation,
        Quaternion displayWrist)
    {
        armPlanningDepth++;
        try
        {
            fixedArmPathClear = true;
            double sliceStart = Time.realtimeSinceStartupAsDouble;
            PickupArmPose saved = CurrentArmPose();
            Quaternion savedWrist = armWristRig.rotation;
            try
            {
                for (int step = 0; step <= 24; step++)
                {
                    ApplyArmPose(start, goal, step / 24f, wristOrientation);
                    yield return CheckArmCollisionAsync(start, displayWrist);
                    if (armCollisionFound)
                    {
                        fixedArmPathClear = false;
                        yield break;
                    }
                    if (Time.realtimeSinceStartupAsDouble - sliceStart < ArmPlanningSliceSeconds) continue;
                    ApplyArmPose(start, start, 1f, displayWrist);
                    yield return PlanningYield;
                    sliceStart = Time.realtimeSinceStartupAsDouble;
                }
            }
            finally
            {
                ApplyArmPose(saved, saved, 1f, savedWrist);
            }
        }
        finally { armPlanningDepth--; }
    }

    // Use real time: fast-forward must not increase the planning work per frame.
    private const double ArmPlanningSliceSeconds = .0015;
    private bool armCollisionFound;

    private IEnumerator CheckArmCollisionAsync(PickupArmPose display, Quaternion displayWrist)
    {
        PickupArmPose sample = CurrentArmPose();
        Quaternion sampleWrist = armWristRig.rotation;
        var check = CheckArmCollisionGeometry();
        try
        {
            while (check.MoveNext())
            {
                // Hidden trial poses must never reach the visible replay mirror.
                ApplyArmPose(display, display, 1f, displayWrist);
                yield return PlanningYield;
                ApplyArmPose(sample, sample, 1f, sampleWrist);
            }
        }
        finally
        {
            (check as System.IDisposable)?.Dispose();
            ApplyArmPose(sample, sample, 1f, sampleWrist);
        }
    }

    private bool ArmBodyIntersectsWorkpiece()
    {
        var check = CheckArmCollisionGeometry();
        try { while (check.MoveNext()) { } }
        finally { (check as System.IDisposable)?.Dispose(); }
        return armCollisionFound;
    }

    private IEnumerator CheckArmCollisionGeometry()
    {
        armCollisionFound = false;
        double sliceStart = Time.realtimeSinceStartupAsDouble;
        Bounds partBounds = GetBounds(sheet);
        if (collisionPartRoot != sheet)
        {
            collisionPartRoot = sheet;
            collisionPartMeshes = sheet.GetComponentsInChildren<MeshFilter>(true);
        }
        for (int armIndex = 0; armIndex <= 3; armIndex++)
        {
            Transform link = armSegments[armIndex];
            Bounds linkBounds = GetBounds(link);
            if (!linkBounds.Intersects(partBounds)) continue;
            MeshFilter linkMesh = link.GetComponent<MeshFilter>();
            if (linkMesh == null || linkMesh.sharedMesh == null) continue;
            Bounds linkLocal = linkMesh.sharedMesh.bounds;
            linkLocal.Expand(0.005f);
            foreach (MeshFilter part in collisionPartMeshes)
            {
                if (part.sharedMesh == null) continue;
                if (!GetBounds(part.transform).Intersects(linkBounds)) continue;
                if (!part.sharedMesh.isReadable)
                {
                    armCollisionFound = true; yield break;
                }
                Vector3[] vertices;
                if (!collisionMeshVertices.TryGetValue(part.sharedMesh,
                    out vertices))
                {
                    vertices = part.sharedMesh.vertices;
                    collisionMeshVertices.Add(part.sharedMesh, vertices);
                }
                Vector3[] localVertices;
                if (!collisionLocalVertexBuffers.TryGetValue(part.sharedMesh,
                    out localVertices))
                {
                    localVertices = new Vector3[vertices.Length];
                    collisionLocalVertexBuffers.Add(part.sharedMesh, localVertices);
                }
                Matrix4x4 toLink = linkMesh.transform.worldToLocalMatrix
                    * part.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if ((i & 255) == 0 && Time.realtimeSinceStartupAsDouble - sliceStart >= ArmPlanningSliceSeconds)
                    {
                        yield return PlanningYield;
                        sliceStart = Time.realtimeSinceStartupAsDouble;
                    }
                    localVertices[i] = toLink.MultiplyPoint3x4(vertices[i]);
                    if (linkLocal.Contains(localVertices[i])) { armCollisionFound = true; yield break; }
                }
                int[] triangles;
                if (!collisionMeshTriangles.TryGetValue(part.sharedMesh,
                    out triangles))
                {
                    triangles = part.sharedMesh.triangles;
                    collisionMeshTriangles.Add(part.sharedMesh, triangles);
                }
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    if ((i & 255) == 0 && Time.realtimeSinceStartupAsDouble - sliceStart >= ArmPlanningSliceSeconds)
                    {
                        yield return PlanningYield;
                        sliceStart = Time.realtimeSinceStartupAsDouble;
                    }
                    Vector3 a = localVertices[triangles[i]];
                    Vector3 b = localVertices[triangles[i + 1]];
                    Vector3 c = localVertices[triangles[i + 2]];
                    if (TriangleIntersectsBox(a, b, c, linkLocal)) { armCollisionFound = true; yield break; }
                }
            }
        }

    }

    private static bool TriangleIntersectsBox(Vector3 a, Vector3 b, Vector3 c,
        Bounds box)
    {
        a -= box.center;
        b -= box.center;
        c -= box.center;
        Vector3 e = box.extents;
        if (Mathf.Min(a.x, b.x, c.x) > e.x
            || Mathf.Max(a.x, b.x, c.x) < -e.x
            || Mathf.Min(a.y, b.y, c.y) > e.y
            || Mathf.Max(a.y, b.y, c.y) < -e.y
            || Mathf.Min(a.z, b.z, c.z) > e.z
            || Mathf.Max(a.z, b.z, c.z) < -e.z) return false;
        Vector3 ab = b - a, bc = c - b, ca = a - c;
        if (TriangleSeparatedOnAxis(a, b, c, Vector3.Cross(ab, bc), e))
            return false;
        return !TriangleSeparatedOnEdgeAxes(a, b, c, ab, e)
            && !TriangleSeparatedOnEdgeAxes(a, b, c, bc, e)
            && !TriangleSeparatedOnEdgeAxes(a, b, c, ca, e);
    }

    private static bool TriangleSeparatedOnEdgeAxes(Vector3 a, Vector3 b,
        Vector3 c, Vector3 edge, Vector3 extents)
    {
        return TriangleSeparatedOnAxis(a, b, c,
            Vector3.Cross(edge, Vector3.right), extents)
            || TriangleSeparatedOnAxis(a, b, c,
                Vector3.Cross(edge, Vector3.up), extents)
            || TriangleSeparatedOnAxis(a, b, c,
                Vector3.Cross(edge, Vector3.forward), extents);
    }

    private static bool TriangleSeparatedOnAxis(Vector3 a, Vector3 b, Vector3 c,
        Vector3 axis, Vector3 extents)
    {
        if (axis.sqrMagnitude < 0.0000000001f) return false;
        float pa = Vector3.Dot(a, axis), pb = Vector3.Dot(b, axis);
        float pc = Vector3.Dot(c, axis);
        float radius = Mathf.Abs(axis.x) * extents.x
            + Mathf.Abs(axis.y) * extents.y
            + Mathf.Abs(axis.z) * extents.z;
        return Mathf.Min(pa, pb, pc) > radius
            || Mathf.Max(pa, pb, pc) < -radius;
    }

    private bool ArmBodyIntersectsFoldPath(Transform flap, Vector3 hinge,
        Vector3 foldAxis)
    {
        Vector3 position = flap.position;
        Quaternion rotation = flap.rotation;
        try
        {
            for (int angle = 0; angle <= 90; angle += 5)
            {
                flap.position = position;
                flap.rotation = rotation;
                if (angle != 0) flap.RotateAround(hinge, foldAxis, angle);
                if (ArmBodyIntersectsWorkpiece()) return true;
            }
            return false;
        }
        finally
        {
            flap.position = position;
            flap.rotation = rotation;
        }
    }

    private IEnumerator MoveBendingUpper(float travel, Transform flap = null,
        Vector3 hinge = default(Vector3), Vector3 foldAxis = default(Vector3))
    {
        Vector3 destination = bendingUpperHomePositions[0] + Vector3.down * travel;
        float totalTravel = Vector3.Distance(bendingUpper.position, destination);
        float folded = 0f;
        while (Vector3.Distance(bendingUpper.position, destination) > positionTolerance)
        {
            Vector3 next = Vector3.MoveTowards(bendingUpper.position, destination,
                bendingPressSpeed * MotionDelta);
            Vector3 delta = next - bendingUpper.position;
            TranslateBendingUpper(delta);
            if (flap != null && totalTravel > 0.0001f)
            {
                float nextFold = 90f * (1f - Vector3.Distance(next, destination)
                    / totalTravel);
                flap.RotateAround(hinge, foldAxis, nextFold - folded);
                folded = nextFold;
            }
            yield return null;
        }
        Vector3 remainder = destination - bendingUpper.position;
        TranslateBendingUpper(remainder);
        if (flap != null) flap.RotateAround(hinge, foldAxis, 90f - folded);
    }

    private void TranslateBendingUpper(Vector3 delta)
    {
        foreach (Transform root in bendingUpperMovingRoots)
            root.position += delta;
    }

    private bool ReplaceWithBendingParts()
    {
        if (bendingPiecePrefabs == null || bendingPiecePrefabs.Length != 5
            || System.Array.Exists(bendingPiecePrefabs, prefab => prefab == null))
            return Fail("缺少加工零件 冲压完毕1–5.fbx 五块折弯分件模型");
        Transform previous = sheet;
        Bounds previousLocal;
        if (!TryGetLocalMeshBounds(previous, out previousLocal))
            return Fail("冲床成品缺少可用于匹配折弯分件尺寸的网格");
        Vector3 previousFaceNormal = previous.TransformDirection(finishedTopLocalNormal);
        GameObject split = new GameObject("BendingPart");
        split.transform.SetParent(workpieceRoot, false);
        Transform[] sections = new Transform[bendingPiecePrefabs.Length];
        for (int i = 0; i < bendingPiecePrefabs.Length; i++)
        {
            GameObject piece = Instantiate(bendingPiecePrefabs[i], split.transform, false);
            piece.name = "FinishedPartSection" + (i + 1);
            sections[i] = piece.transform;
        }
        if (!AlignThirdBendingSection(split.transform, sections[0], sections[2]))
        {
            Destroy(split);
            return Fail("冲压完毕3.fbx 无法与中心板边界对齐");
        }
        split.transform.position = previous.position;
        split.transform.rotation = previous.rotation;
        Bounds splitLocal;
        if (!TryGetLocalMeshBounds(split.transform, out splitLocal))
        {
            Destroy(split);
            return Fail("折弯分件模型没有有效的网格边界");
        }
        float oldWidth = PlanarWorldWidth(previous, previousLocal);
        float newWidth = PlanarWorldWidth(split.transform, splitLocal);
        if (newWidth < 0.0001f)
        {
            Destroy(split);
            return Fail("折弯分件模型没有有效的网格边界");
        }
        split.transform.localScale *= oldWidth / newWidth;
        split.transform.position += GetBounds(previous).center
            - GetBounds(split.transform).center;
        sheet = split.transform;
        Bounds localSplit;
        if (TryGetLocalMeshBounds(sheet, out localSplit))
        {
            Vector3 e = localSplit.extents;
            finishedTopLocalNormal = e.x < e.y
                ? (e.x < e.z ? Vector3.right : Vector3.forward)
                : (e.y < e.z ? Vector3.up : Vector3.forward);
            if (Vector3.Dot(sheet.TransformDirection(finishedTopLocalNormal),
                previousFaceNormal) < 0f)
                finishedTopLocalNormal = -finishedTopLocalNormal;
        }
        if (!AlignFinishedPartWithCupTips())
        {
            sheet = previous;
            Destroy(split);
            return false;
        }
        previous.gameObject.SetActive(false);
        ApplyWorkpieceColor(sheet);
        Destroy(previous.gameObject);
        return true;
    }

    private static float PlanarWorldWidth(Transform root, Bounds local)
    {
        Vector3 size = local.size;
        if (size.x <= size.y && size.x <= size.z)
            return Mathf.Max(root.TransformVector(Vector3.up * size.y).magnitude,
                root.TransformVector(Vector3.forward * size.z).magnitude);
        if (size.y <= size.z)
            return Mathf.Max(root.TransformVector(Vector3.right * size.x).magnitude,
                root.TransformVector(Vector3.forward * size.z).magnitude);
        return Mathf.Max(root.TransformVector(Vector3.right * size.x).magnitude,
            root.TransformVector(Vector3.up * size.y).magnitude);
    }

    private static bool AlignThirdBendingSection(Transform assembly,
        Transform panel, Transform thirdSection)
    {
        MeshFilter panelMesh = panel.GetComponentInChildren<MeshFilter>(true);
        MeshFilter sectionMesh = thirdSection.GetComponentInChildren<MeshFilter>(true);
        if (panelMesh == null || sectionMesh == null
            || panelMesh.sharedMesh == null || sectionMesh.sharedMesh == null)
            return false;
        Bounds panelBounds = FilterBoundsInRoot(assembly, panelMesh);
        Bounds sectionBounds = FilterBoundsInRoot(assembly, sectionMesh);
        Vector3 separation = sectionBounds.center - panelBounds.center;
        float x = Mathf.Abs(separation.x);
        float y = Mathf.Abs(separation.y);
        float z = Mathf.Abs(separation.z);
        Vector3 outward = x >= y && x >= z ? Vector3.right
            : y >= z ? Vector3.up : Vector3.forward;
        if (Vector3.Dot(separation, outward) < 0f) outward = -outward;
        float panelEdge = Vector3.Dot(panelBounds.center, outward)
            + Vector3.Dot(panelBounds.extents, Abs(outward));
        float sectionInnerEdge = Vector3.Dot(sectionBounds.center, outward)
            - Vector3.Dot(sectionBounds.extents, Abs(outward));
        float correction = panelEdge - sectionInnerEdge;
        if (Mathf.Abs(correction) > panelBounds.size.magnitude * 0.01f)
            thirdSection.localPosition += outward * correction;
        return true;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y),
            Mathf.Abs(value.z));
    }

    private bool TryFindBendingFlaps(out Transform[] flaps,
        out Bounds centerPanel, out Vector3[] directions)
    {
        flaps = null;
        centerPanel = new Bounds();
        directions = null;
        MeshFilter[] filters = sheet.GetComponentsInChildren<MeshFilter>(true);
        MeshFilter central = null;
        float greatestArea = 0f;
        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null || !filter.gameObject.activeInHierarchy) continue;
            Bounds b = FilterBoundsInRoot(sheet, filter);
            Vector3 size = b.size;
            float[] dimensions = { size.x, size.y, size.z };
            System.Array.Sort(dimensions);
            float area = dimensions[1] * dimensions[2];
            if (area <= greatestArea) continue;
            greatestArea = area;
            central = filter;
            centerPanel = b;
        }
        if (central == null) return false;
        Vector3 panelSize = centerPanel.size;
        int thinAxis = panelSize.x <= panelSize.y
            ? (panelSize.x <= panelSize.z ? 0 : 2)
            : (panelSize.y <= panelSize.z ? 1 : 2);
        Vector3 firstAxis = thinAxis == 0 ? Vector3.up : Vector3.right;
        Vector3 secondAxis = thinAxis == 2 ? Vector3.up : Vector3.forward;
        directions = new[] { firstAxis, secondAxis,
            -firstAxis, -secondAxis };
        flaps = new Transform[directions.Length];
        System.Collections.Generic.HashSet<MeshFilter> used =
            new System.Collections.Generic.HashSet<MeshFilter> { central };
        for (int edge = 0; edge < directions.Length; edge++)
        {
            MeshFilter best = null;
            float bestProjection = 0f;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null || used.Contains(filter)
                    || !filter.gameObject.activeInHierarchy) continue;
                float projection = Vector3.Dot(
                    FilterBoundsInRoot(sheet, filter).center - centerPanel.center,
                    directions[edge].normalized);
                if (projection <= bestProjection) continue;
                bestProjection = projection;
                best = filter;
            }
            if (best == null) return false;
            flaps[edge] = best.transform;
            used.Add(best);
        }
        return true;
    }

    private static Bounds FilterBoundsInRoot(Transform root, MeshFilter filter)
    {
        Bounds mesh = filter.sharedMesh.bounds;
        Bounds result = new Bounds();
        bool found = false;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 local = root.InverseTransformPoint(
                        filter.transform.TransformPoint(mesh.center
                            + Vector3.Scale(mesh.extents, new Vector3(x, y, z))));
                    if (!found) { result = new Bounds(local, Vector3.zero); found = true; }
                    else result.Encapsulate(local);
                }
        return result;
    }

    private static bool TryGetLocalMeshBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.gameObject.activeInHierarchy) continue;
            Bounds part = FilterBoundsInRoot(root, filter);
            if (!found) { bounds = part; found = true; }
            else bounds.Encapsulate(part);
        }
        return found;
    }

    private bool TryGetArmRailEnd(out Vector3 destination)
    {
        destination = armBaseMover.position;
        Vector3 axis;
        float minimumTravel, maximumTravel;
        if (!TryGetArmRailLimits(out axis, out minimumTravel, out maximumTravel))
            return false;
        Vector3 lowerEnd = destination + axis * minimumTravel;
        Vector3 upperEnd = destination + axis * maximumTravel;
        Vector3 product = SheetTopCenter();
        product.y = lowerEnd.y = upperEnd.y = 0f;
        bool upperNearTable = (upperEnd - product).sqrMagnitude
            < (lowerEnd - product).sqrMagnitude;
        float inset = Mathf.Min(armPickupRailInset,
            (maximumTravel - minimumTravel) * 0.25f);
        float edgeTravel = upperNearTable
            ? maximumTravel - inset : minimumTravel + inset;
        float opposite = upperNearTable ? minimumTravel : maximumTravel;
        float travel = edgeTravel;
        bool clear = false;
        const int samples = 12;
        float[] yawOffsets = { 0f, 30f, -30f, 60f, -60f,
            90f, -90f };
        for (int i = 0; i <= samples; i++)
        {
            float candidate = Mathf.Lerp(edgeTravel, opposite,
                (float)i / samples);
            foreach (float yawOffset in yawOffsets)
            {
                float yaw;
                if (!ArmPickupPathClearsTable(axis * candidate,
                    yawOffset, out yaw)) continue;
                travel = candidate;
                armPickupPlannedYaw = yaw;
                clear = true;
                break;
            }
            if (clear) break;
        }
        if (!clear)
            return Fail("机械臂3 r848 在取件轨迹中无法避开桌子左 r1414；请检查导轨和桌面位置");
        if (Mathf.Abs(travel) <= positionTolerance)
        {
            Debug.Log("滑槽基座已位于靠近成品的 r775 导轨尽头。", this);
            return true;
        }
        destination = armBaseMover.position + axis * travel;
        Debug.Log(string.Format("机械臂滑槽送料：r872 沿 r775 移动 {0:F3}m，距桌面侧端点内收 {1:F3}m，终点 {2}",
            travel, inset, destination), this);
        return true;
    }

    private bool ArmPickupPathClearsTable(Vector3 railTranslation,
        float yawOffset, out float plannedYaw)
    {
        plannedYaw = 0f;
        Vector3 originalPosition = armRig.position;
        Quaternion originalRotation = armRig.rotation;
        Vector3 originalScale = armRig.localScale;
        Quaternion originalYaw = armYawJoint.localRotation;
        Quaternion originalThird = armThirdJoint.localRotation;
        Quaternion originalFourth = armFourthJoint.localRotation;
        Quaternion originalSixth = armSixthJoint.localRotation;
        Quaternion originalWrist = armWristRig.rotation;
        float originalPlannedYaw = armPickupPlannedYaw;
        bool safe = true;
        try
        {
            for (int step = 1; step <= 12; step++)
            {
                armRig.position = originalPosition + railTranslation * (step / 12f);
                if (!ArmThirdLinkClearsTable()) return false;
            }
            Vector3 pivot = armYawJoint.position;
            Vector3 from = SuctionContactPoint() - pivot;
            Vector3 to = SheetTopCenter() - pivot;
            from.y = to.y = 0f;
            if (from.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f)
                return false;
            float yaw = Vector3.SignedAngle(from, to, Vector3.up) + yawOffset;
            if (Mathf.Abs(yaw) > 170f) return false;
            plannedYaw = yaw;
            armPickupPlannedYaw = yaw;
            for (int step = 0; safe && step < 24; step++)
            {
                armYawJoint.RotateAround(pivot, Vector3.up, yaw / 24f);
                safe = ArmThirdLinkClearsTable();
            }
            if (!safe) return false;
            Vector3 top = SheetTopCenter();
            float high = top.y + Mathf.Max(armLiftHeight, 0.25f) + 0.02f;
            Vector3 contact = SuctionContactPoint();
            if (!PreviewPickupArmMoveClearsTable(
                new Vector3(contact.x, high, contact.z))) return false;
            armWristRig.rotation = HorizontalPickupWristRotation();
            Vector3[] levelWaypoints = {
                new Vector3(top.x, high, top.z),
                top + Vector3.up * 0.005f,
                top + Vector3.up * (Mathf.Max(armLiftHeight, 0.35f) + 0.005f) };
            foreach (Vector3 targetPoint in levelWaypoints)
                if (!PreviewPickupArmMoveClearsTable(targetPoint)) return false;
            Quaternion pickupYaw = armYawJoint.localRotation;
            Quaternion pickupThird = armThirdJoint.localRotation;
            Quaternion pickupFourth = armFourthJoint.localRotation;
            Quaternion pickupSixth = armSixthJoint.localRotation;
            Quaternion levelWrist = armWristRig.rotation;
            for (int step = 1; step <= 24; step++)
            {
                float fraction = step / 24f;
                armYawJoint.localRotation = Quaternion.Slerp(pickupYaw,
                    armYawHomeRotation, fraction);
                armThirdJoint.localRotation = Quaternion.Slerp(pickupThird,
                    armThirdHomeRotation, fraction);
                armFourthJoint.localRotation = Quaternion.Slerp(pickupFourth,
                    armFourthHomeRotation, fraction);
                armSixthJoint.localRotation = Quaternion.Slerp(pickupSixth,
                    armSixthHomeRotation, fraction);
                armWristRig.rotation = levelWrist;
                if (!ArmThirdLinkClearsTable()) return false;
            }
            return true;
        }
        finally
        {
            armYawJoint.localRotation = originalYaw;
            armThirdJoint.localRotation = originalThird;
            armFourthJoint.localRotation = originalFourth;
            armSixthJoint.localRotation = originalSixth;
            armPickupPlannedYaw = originalPlannedYaw;
            armRig.position = originalPosition;
            armRig.rotation = originalRotation;
            armRig.localScale = originalScale;
            armWristRig.rotation = originalWrist;
        }
    }

    private bool PreviewPickupArmMoveClearsTable(Vector3 destination)
    {
        PickupArmPose pose;
        if (!TrySolvePickupArmPose(destination, out pose)) return false;
        Quaternion wristWorld = armWristRig.rotation;
        Quaternion startYaw = armYawJoint.localRotation;
        Quaternion startThird = armThirdJoint.localRotation;
        Quaternion startFourth = armFourthJoint.localRotation;
        Quaternion startSixth = armSixthJoint.localRotation;
        for (int i = 1; i <= 24; i++)
        {
            float fraction = i / 24f;
            armYawJoint.localRotation = Quaternion.Slerp(startYaw, pose.yaw, fraction);
            armThirdJoint.localRotation = Quaternion.Slerp(startThird,
                pose.third, fraction);
            armFourthJoint.localRotation = Quaternion.Slerp(startFourth,
                pose.fourth, fraction);
            armSixthJoint.localRotation = Quaternion.Slerp(startSixth,
                pose.sixth, fraction);
            armWristRig.rotation = wristWorld;
            if (!ArmThirdLinkClearsTable()) return false;
        }
        return true;
    }

    private bool ArmThirdLinkClearsTable()
    {
        Bounds arm = GetBounds(armSegments[3]);
        Bounds table = punchTable.bounds;
        const float clearance = 0.025f;
        bool aboveFootprint = arm.max.x > table.min.x - clearance
            && arm.min.x < table.max.x + clearance
            && arm.max.z > table.min.z - clearance
            && arm.min.z < table.max.z + clearance;
        if (!aboveFootprint || arm.min.y >= table.max.y + clearance)
            return true;
        if (armThirdMeshVertices == null) return false;
        // The imported arm is diagonal. Its world AABB can overlap the table
        // while the actual mesh remains outside it, so inspect mesh points in
        // the tabletop slab before rejecting a pickup pose.
        float slabBottom = table.max.y - 0.12f;
        foreach (Vector3 vertex in armThirdMeshVertices)
        {
            Vector3 point = armThirdMeshTransform.TransformPoint(vertex);
            if (point.x > table.min.x - clearance
                && point.x < table.max.x + clearance
                && point.z > table.min.z - clearance
                && point.z < table.max.z + clearance
                && point.y > slabBottom
                && point.y < table.max.y + clearance)
                return false;
        }
        return true;
    }

    private bool TryGetArmRailPositionForBendingMold(out Vector3 destination)
    {
        destination = armBaseMover.position;
        Vector3 axis;
        float minimumTravel, maximumTravel;
        if (!TryGetArmRailLimits(out axis, out minimumTravel, out maximumTravel))
            return false;
        float projectedTravel = Vector3.Dot(GetBounds(bendingLowerMold).center
            - SuctionContactPoint(), axis);
        float travel = Mathf.Clamp(projectedTravel, minimumTravel, maximumTravel);
        destination += axis * travel;
        if (Mathf.Abs(travel - projectedTravel) > 0.05f)
            Debug.LogWarning("折弯机下模 r1547 的投影超出 r775 行程，基座停在可达端点。", this);
        Debug.Log(string.Format("机械臂对准 r1547：沿 r775 移动 {0:F3}m，下模投影行程 {1:F3}m",
            travel, projectedTravel), this);
        return true;
    }

    private bool TryGetArmRailLimits(out Vector3 axis,
        out float minimumTravel, out float maximumTravel)
    {
        axis = Vector3.zero;
        minimumTravel = float.NegativeInfinity;
        maximumTravel = float.PositiveInfinity;
        Vector3 near, far;
        if (!TryRailMeshEnds(armLongSlot, out near, out far))
            return Fail("无法读取长滑槽 r775 的网格长轴");
        axis = far - near;
        axis.y = 0f;
        if (axis.magnitude < 0.5f)
            return Fail("长滑槽 r775 的水平导轨长度异常");
        axis.Normalize();
        Bounds rail = GetBounds(armLongSlot);
        float railCenter = Vector3.Dot(rail.center, axis);
        float railRadius = HorizontalRadius(rail, axis);
        const float clearance = 0.03f;
        foreach (Transform slider in armSliders)
        {
            Bounds bounds = GetBounds(slider);
            float center = Vector3.Dot(bounds.center, axis);
            float radius = HorizontalRadius(bounds, axis);
            minimumTravel = Mathf.Max(minimumTravel,
                railCenter - railRadius + clearance - (center - radius));
            maximumTravel = Mathf.Min(maximumTravel,
                railCenter + railRadius - clearance - (center + radius));
        }
        if (minimumTravel > maximumTravel)
            return Fail("滑槽基座的四个滑块无法同时处于 r775 导轨范围内");
        return true;
    }

    private bool TryGetFinishedPartTableEdge(out Vector3 bottomCenter)
    {
        bottomCenter = SheetBottomCenter();
        Bounds table = punchTable.bounds;
        Bounds part = GetBounds(sheet);
        const float inset = 0.02f;
        float minimumZ = table.min.z + part.extents.z + inset;
        float maximumZ = table.max.z - part.extents.z - inset;
        if (table.size.x < part.size.x + 2f * inset || minimumZ > maximumZ)
            return Fail("成品尺寸超出桌子左 r1414 的可放置范围");
        bottomCenter = new Vector3(table.max.x - part.extents.x - inset,
            table.max.y + 0.002f,
            Mathf.Clamp(part.center.z, minimumZ, maximumZ));
        return true;
    }

    private IEnumerator RotateArmAroundBase(Vector3 pivot, float degrees)
    {
        yield return RotateArmAroundAxis(pivot, Vector3.up, degrees);
    }

    private IEnumerator RotateArmAroundAxis(Vector3 pivot, Vector3 axis,
        float degrees)
    {
        float rotated = 0f;
        while (Mathf.Abs(degrees - rotated) > 0.01f)
        {
            float next = Mathf.MoveTowards(rotated, degrees,
                armRotationSpeed * MotionDelta);
            float step = next - rotated;
            armYawJoint.RotateAround(pivot, axis, step);
            rotated = next;
            yield return null;
        }
    }

    private bool TrySolvePickupArmPose(Vector3 target, out PickupArmPose pose)
    {
        pose = new PickupArmPose();
        Quaternion savedYaw = armYawJoint.localRotation;
        Quaternion savedThird = armThirdJoint.localRotation;
        Quaternion savedFourth = armFourthJoint.localRotation;
        Quaternion savedSixth = armSixthJoint.localRotation;
        Quaternion wristWorld = armWristRig.rotation;
        try
        {
            armYawJoint.localRotation = armYawHomeRotation;
            armThirdJoint.localRotation = armThirdHomeRotation;
            armFourthJoint.localRotation = armFourthHomeRotation;
            armSixthJoint.localRotation = armSixthHomeRotation;
            armYawJoint.RotateAround(armYawJoint.position, Vector3.up,
                armPickupPlannedYaw);
            armWristRig.rotation = wristWorld;
            float yawTravel = armPickupPlannedYaw;
            float thirdTravel = 0f, fourthTravel = 0f, sixthTravel = 0f;
            for (int iteration = 0; iteration < 90; iteration++)
            {
                if (Vector3.Distance(SuctionContactPoint(), target) < 0.012f)
                    break;
                Vector3 forward = Vector3.ProjectOnPlane(
                    armThirdJoint.position - armYawJoint.position, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.ProjectOnPlane(SuctionContactPoint()
                        - armThirdJoint.position, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f) return false;
                Vector3 pitchAxis = Vector3.Cross(forward.normalized,
                    Vector3.up).normalized;
                RotatePickupPitchJoint(armThirdJoint, target, pitchAxis,
                    ref thirdTravel, 100f);
                armWristRig.rotation = wristWorld;
                RotatePickupPitchJoint(armFourthJoint, target, pitchAxis,
                    ref fourthTravel, 120f);
                armWristRig.rotation = wristWorld;
                RotatePickupPitchJoint(armSixthJoint, target, pitchAxis,
                    ref sixthTravel, 150f);
                armWristRig.rotation = wristWorld;
                Vector3 from = Vector3.ProjectOnPlane(SuctionContactPoint()
                    - armYawJoint.position, Vector3.up);
                Vector3 to = Vector3.ProjectOnPlane(target
                    - armYawJoint.position, Vector3.up);
                if (from.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f)
                    return false;
                float yawStep = Mathf.Clamp(Vector3.SignedAngle(from, to,
                    Vector3.up), -12f, 12f);
                float nextYaw = Mathf.Clamp(yawTravel + yawStep, -170f, 170f);
                armYawJoint.RotateAround(armYawJoint.position, Vector3.up,
                    nextYaw - yawTravel);
                yawTravel = nextYaw;
                armWristRig.rotation = wristWorld;
            }
            if (Vector3.Distance(SuctionContactPoint(), target) > 0.025f)
                return false;
            pose.yaw = armYawJoint.localRotation;
            pose.third = armThirdJoint.localRotation;
            pose.fourth = armFourthJoint.localRotation;
            pose.sixth = armSixthJoint.localRotation;
            return true;
        }
        finally
        {
            armYawJoint.localRotation = savedYaw;
            armThirdJoint.localRotation = savedThird;
            armFourthJoint.localRotation = savedFourth;
            armSixthJoint.localRotation = savedSixth;
            armWristRig.rotation = wristWorld;
        }
    }

    private void RotatePickupPitchJoint(Transform joint, Vector3 target,
        Vector3 axis, ref float travel, float limit)
    {
        Vector3 from = Vector3.ProjectOnPlane(SuctionContactPoint()
            - joint.position, axis);
        Vector3 to = Vector3.ProjectOnPlane(target - joint.position, axis);
        if (from.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f) return;
        float step = Mathf.Clamp(Vector3.SignedAngle(from, to, axis),
            -12f, 12f);
        float next = Mathf.Clamp(travel + step, -limit, limit);
        joint.RotateAround(joint.position, axis, next - travel);
        travel = next;
    }

    private IEnumerator MovePickupArmTo(Vector3 destination)
    {
        Quaternion wristWorld = armWristRig.rotation;
        PickupArmPose pose;
        if (!TrySolvePickupArmPose(destination, out pose))
        {
            Fail("r851 水平回转与 r848 前俯无法到达冲床成品取件位置");
            yield break;
        }
        Quaternion fromYaw = armYawJoint.localRotation;
        Quaternion fromThird = armThirdJoint.localRotation;
        Quaternion fromFourth = armFourthJoint.localRotation;
        Quaternion fromSixth = armSixthJoint.localRotation;
        float duration = Mathf.Max(
            Vector3.Distance(SuctionContactPoint(), destination)
                / Mathf.Max(0.01f, armPickupSpeed),
            Quaternion.Angle(fromYaw, pose.yaw) / Mathf.Max(1f, armRotationSpeed),
            Quaternion.Angle(fromThird, pose.third) / Mathf.Max(1f, armRotationSpeed),
            Quaternion.Angle(fromFourth, pose.fourth) / Mathf.Max(1f, armRotationSpeed),
            Quaternion.Angle(fromSixth, pose.sixth) / Mathf.Max(1f, armRotationSpeed),
            0.01f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Mathf.Min(duration, elapsed + MotionDelta);
            float fraction = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            armYawJoint.localRotation = Quaternion.Slerp(fromYaw, pose.yaw, fraction);
            armThirdJoint.localRotation = Quaternion.Slerp(fromThird,
                pose.third, fraction);
            armFourthJoint.localRotation = Quaternion.Slerp(fromFourth,
                pose.fourth, fraction);
            armSixthJoint.localRotation = Quaternion.Slerp(fromSixth,
                pose.sixth, fraction);
            armWristRig.rotation = wristWorld;
            yield return null;
        }
        armYawJoint.localRotation = pose.yaw;
        armThirdJoint.localRotation = pose.third;
        armFourthJoint.localRotation = pose.fourth;
        armSixthJoint.localRotation = pose.sixth;
        armWristRig.rotation = wristWorld;
    }

    private IEnumerator RestorePickupArmJoints()
    {
        Quaternion wristWorld = armWristRig.rotation;
        Quaternion fromYaw = armYawJoint.localRotation;
        Quaternion fromThird = armThirdJoint.localRotation;
        Quaternion fromFourth = armFourthJoint.localRotation;
        Quaternion fromSixth = armSixthJoint.localRotation;
        float duration = Mathf.Max(
            Quaternion.Angle(fromYaw, armYawHomeRotation),
            Quaternion.Angle(fromThird, armThirdHomeRotation),
            Quaternion.Angle(fromFourth, armFourthHomeRotation),
            Quaternion.Angle(fromSixth, armSixthHomeRotation))
            / Mathf.Max(1f, armRotationSpeed);
        duration = Mathf.Max(duration, 0.01f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Mathf.Min(duration, elapsed + MotionDelta);
            float fraction = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            armYawJoint.localRotation = Quaternion.Slerp(fromYaw,
                armYawHomeRotation, fraction);
            armThirdJoint.localRotation = Quaternion.Slerp(fromThird,
                armThirdHomeRotation, fraction);
            armFourthJoint.localRotation = Quaternion.Slerp(fromFourth,
                armFourthHomeRotation, fraction);
            armSixthJoint.localRotation = Quaternion.Slerp(fromSixth,
                armSixthHomeRotation, fraction);
            armWristRig.rotation = wristWorld;
            yield return null;
        }
        armYawJoint.localRotation = armYawHomeRotation;
        armThirdJoint.localRotation = armThirdHomeRotation;
        armFourthJoint.localRotation = armFourthHomeRotation;
        armSixthJoint.localRotation = armSixthHomeRotation;
        armWristRig.rotation = wristWorld;
    }

    private Vector3 SuctionContactPoint()
    {
        return armContactAnchor.position;
    }

    private Vector3 SheetTopCenter()
    {
        Bounds bounds = GetBounds(sheet);
        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }

    private IEnumerator MoveArmSuctionTo(Vector3 contactDestination)
    {
        Vector3 contactOffset = SuctionContactPoint() - armWristRig.position;
        yield return MoveFixedArmWristTo(contactDestination - contactOffset,
            armWristRig.rotation);
        if (sequence == null) yield break;
        if (Vector3.Distance(SuctionContactPoint(), contactDestination)
            > positionTolerance * 5f)
            Fail("固定尺寸机械臂未能将吸盘移到目标位置");
    }

    private bool ReplaceSheetWithFinishedPart()
    {
        if (finishedPartPrefab == null) return Fail("缺少加工零件 冲压完毕.fbx 引用");
        Vector3 oldBottom = SheetBottomCenter();
        GameObject finished = Instantiate(finishedPartPrefab, workpieceRoot);
        finished.name = "FinishedPart";
        OrientSheetFlat(finished.transform);
        Bounds bounds = GetBounds(finished.transform);
        float width = Mathf.Max(bounds.size.x, bounds.size.z);
        if (width <= 0.0001f)
        {
            Destroy(finished);
            return Fail("成品模型没有有效的 Renderer bounds");
        }
        finished.transform.localScale *= rawSheetWorldWidth / width;
        Bounds resized = GetBounds(finished.transform);
        finished.transform.position += oldBottom
            - new Vector3(resized.center.x, resized.min.y, resized.center.z);
        rawSheetInstance.gameObject.SetActive(false);
        sheet = finished.transform;
        Bounds localFinished;
        if (TryGetLocalMeshBounds(sheet, out localFinished))
        {
            Vector3 e = localFinished.extents;
            finishedTopLocalNormal = e.x < e.y
                ? (e.x < e.z ? Vector3.right : Vector3.forward)
                : (e.y < e.z ? Vector3.up : Vector3.forward);
            if (Vector3.Dot(sheet.TransformDirection(finishedTopLocalNormal),
                Vector3.up) < 0f)
                finishedTopLocalNormal = -finishedTopLocalNormal;
        }
        ApplyWorkpieceColor(sheet);
        SetState(LoadingState.ReplaceWithFinishedPart, "原料已替换为冲压完毕成品");
        return true;
    }

    private bool TryGetPunchXRailEnd(out Vector3 destination)
    {
        destination = punchClampMover.position;
        if (punchRailShell == null)
            return Fail("未找到转塔冲床导轨壳 r1490，无法确定出料终点");
        Bounds shell = GetBounds(punchRailShell);
        if (shell.size.x <= positionTolerance)
            return Fail("转塔冲床导轨壳 r1490 没有有效的 X 轴范围");
        const float clearance = 0.03f;
        float minimumTravel = float.NegativeInfinity;
        float maximumTravel = float.PositiveInfinity;
        foreach (Transform assembly in punchClampVisuals)
        {
            Transform slider = FindModelNode(assembly, 1420);
            if (slider == null) return Fail("冲床夹爪缺少 r1420 滑块，无法计算 X 轴行程");
            Bounds bounds = GetBounds(slider);
            minimumTravel = Mathf.Max(minimumTravel, shell.min.x + clearance - bounds.min.x);
            maximumTravel = Mathf.Min(maximumTravel, shell.max.x - clearance - bounds.max.x);
        }
        if (minimumTravel > maximumTravel)
            return Fail("两套冲床夹爪的滑块无法同时处于 r1490 导轨壳范围内");
        // 场景镜头中向左对应 Unity 世界 X 正方向。
        float travel = maximumTravel;
        if (travel <= positionTolerance)
            return Fail("冲床夹爪无法沿 X 轴正方向移动到 r1490 尽头");
        destination.x += travel;
        Debug.Log(string.Format("冲床成品出料：夹爪沿 Unity 世界 X 正方向移动 {0:F3}m，到达 r1490 尽头 X={1:F3}",
            travel, shell.max.x), this);
        return true;
    }

    private IEnumerator MoveCarrier(Transform carrier, Transform[] visuals,
        Vector3 destination, float speed, Transform[] verticallyFixed = null,
        bool rollSuctionGear = false)
    {
        if (ReplayMode && carrier == armBaseMover)
        {
            Vector3 shift = destination - carrier.position;
            carrier.position = destination;
            foreach (Transform visual in visuals)
                if (visual != null && !visual.IsChildOf(carrier)) visual.position += shift;
            yield break;
        }
        while (Vector3.Distance(carrier.position, destination) > positionTolerance)
        {
            Vector3 next = Vector3.MoveTowards(carrier.position, destination,
                Mathf.Max(0.01f, speed) * MotionDelta);
            Vector3 delta = next - carrier.position;
            carrier.position = next;
            if (visuals != null)
                foreach (Transform visual in visuals)
                    if (visual != null && !visual.IsChildOf(carrier)) visual.position += delta;
            if (verticallyFixed != null)
                foreach (Transform visual in verticallyFixed)
                    if (visual != null) visual.position -= Vector3.up * delta.y;
            if (rollSuctionGear) UpdateSuctionGearRotation();
            yield return null;
        }
        Vector3 remainder = destination - carrier.position;
        carrier.position = destination;
        if (visuals != null)
            foreach (Transform visual in visuals)
                if (visual != null && !visual.IsChildOf(carrier)) visual.position += remainder;
        if (verticallyFixed != null)
            foreach (Transform visual in verticallyFixed)
                if (visual != null) visual.position -= Vector3.up * remainder.y;
        if (rollSuctionGear) UpdateSuctionGearRotation();
    }

    private IEnumerator MoveWorkpiece(Vector3 destination, float speed)
    {
        while (Vector3.Distance(workpieceRoot.position, destination) > positionTolerance)
        {
            workpieceRoot.position = Vector3.MoveTowards(workpieceRoot.position,
                destination, Mathf.Max(0.01f, speed) * MotionDelta);
            yield return null;
        }
        workpieceRoot.position = destination;
    }

    private Vector3 SheetTargetAtPunchJaws()
    {
        Vector3 jawCenter = Vector3.zero;
        for (int i = 0; i < punchJawsA.Length; i++)
            jawCenter += (GetBounds(punchJawsA[i]).center
                + GetBounds(punchJawsB[i]).center) * 0.5f;
        jawCenter /= punchJawsA.Length;

        Vector3 towardTable = punchTable.bounds.center - jawCenter;
        towardTable.y = 0f;
        if (towardTable.sqrMagnitude < 0.0001f)
            towardTable = punchTable.bounds.center - SheetBottomCenter();
        towardTable.y = 0f;
        towardTable.Normalize();
        Bounds board = GetBounds(sheet);
        float halfWidth = HorizontalRadius(board, towardTable);
        Vector3 targetCenter = jawCenter + towardTable * (halfWidth - 0.005f);
        Vector3 move = targetCenter - board.center;
        move.y = 0f;
        return workpieceRoot.position + move;
    }

    private IEnumerator MoveSuctionHorizontally(Vector3 destination, float speed)
    {
        Vector3 movement = destination - suctionMover.position;
        movement.y = 0f;
        float along = Vector3.Dot(movement, suctionRailAxis);
        float crossTrack = (movement - suctionRailAxis * along).magnitude;
        float travel = Vector3.Dot(destination - suctionMoverHome, suctionRailAxis);
        if (crossTrack > 0.01f || travel < suctionMinTravel - 0.01f
            || travel > suctionMaxTravel + 0.01f)
        {
            Fail(string.Format("吸盘小车偏离 r1090 导轨：横向 {0:F3}m，行程 {1:F3}m，允许 {2:F3}..{3:F3}m",
                crossTrack, travel, suctionMinTravel, suctionMaxTravel));
            yield break;
        }
        yield return MoveCarrier(suctionMover, suctionVisuals, destination, speed,
            null, true);
    }

    private void UpdateSuctionGearRotation()
    {
        float travel = Vector3.Dot(suctionMover.position - suctionMoverHome, suctionRailAxis);
        float angle = suctionGearRollSign * travel / suctionGearRadius * Mathf.Rad2Deg;
        suctionGear.localRotation = suctionGearHomeRotation
            * Quaternion.AngleAxis(angle, suctionGearLocalAxis);
    }

    private IEnumerator MoveSuctionVertically(Vector3 destination, float speed)
    {
        // 机箱保护壳与竖向导轨随横移小车走，但不随长杆和吸盘架升降。
        yield return MoveCarrier(suctionMover, suctionVisuals, destination, speed,
            new[] { suctionHousing, suctionVerticalGuide });
    }

    private IEnumerator CloseJaws()
    {
        yield return MoveJawPair(clampJawA, clampJawB, true, jawAHome, jawBHome);
    }

    private IEnumerator MoveJawPairs(Transform[] jawsA, Transform[] jawsB, bool close)
    {
        Vector3[] targetsA = new Vector3[jawsA.Length];
        Vector3[] targetsB = new Vector3[jawsB.Length];
        for (int i = 0; i < jawsA.Length; i++)
        {
            Vector3 direction = (jawsB[i].position - jawsA[i].position).normalized;
            targetsA[i] = close ? jawsA[i].position + direction * (jawCloseDistance * 0.5f)
                : jawsA[i].parent.TransformPoint(punchJawAHome[i]);
            targetsB[i] = close ? jawsB[i].position - direction * (jawCloseDistance * 0.5f)
                : jawsB[i].parent.TransformPoint(punchJawBHome[i]);
        }
        bool moving;
        do
        {
            moving = false;
            for (int i = 0; i < jawsA.Length; i++)
            {
                jawsA[i].position = Vector3.MoveTowards(jawsA[i].position,
                    targetsA[i], jawCloseSpeed * MotionDelta);
                jawsB[i].position = Vector3.MoveTowards(jawsB[i].position,
                    targetsB[i], jawCloseSpeed * MotionDelta);
                moving |= Vector3.Distance(jawsA[i].position, targetsA[i]) > positionTolerance
                    || Vector3.Distance(jawsB[i].position, targetsB[i]) > positionTolerance;
            }
            if (moving) yield return null;
        } while (moving);
        for (int i = 0; i < jawsA.Length; i++)
        {
            jawsA[i].position = targetsA[i];
            jawsB[i].position = targetsB[i];
        }
    }

    private void BindPunchJawPairs()
    {
        Transform first = punchClampVisuals[0];
        Transform second = punchClampVisuals[1];
        punchJawsA = new[] { FindModelNode(first, 1460), FindModelNode(first, 1467),
            FindModelNode(second, 1460), FindModelNode(second, 1467) };
        punchJawsB = new[] { FindModelNode(first, 1463), FindModelNode(first, 1466),
            FindModelNode(second, 1463), FindModelNode(second, 1466) };
    }

    private void SyncPunchClampPointsToPlacedAssemblies()
    {
        // Use the positions saved in the scene; entering Play must not seat the
        // assemblies or shift their individual jaw meshes again.
        Bounds combined = GetBounds(punchClampVisuals[0]);
        combined.Encapsulate(GetBounds(punchClampVisuals[1]));
        Vector3 home = combined.center;
        punchClampHomePoint.position = home;
        punchClampAcquirePoint.position = home;
        punchRailStartPoint.position = home;
        if (punchRightGuard != null)
            punchRailEndPoint.position = new Vector3(home.x, home.y,
                GetBounds(punchRightGuard).center.z);
    }

    private float SafePunchClampTravel(float requestedZ, Bounds guard)
    {
        float direction = Mathf.Sign(requestedZ);
        float distance = Mathf.Abs(requestedZ);
        const float clearance = 0.03f;
        foreach (Transform assembly in punchClampVisuals)
        {
            foreach (int sourceId in new[] { 1427, 1430 })
            {
                Transform support = FindModelNode(assembly, sourceId);
                if (support == null)
                {
                    Debug.LogWarning("LoadingProcessController: 未找到夹爪电线支板 r"
                        + sourceId + "，冲床夹爪停止在原位以避免穿模。", assembly);
                    return 0f;
                }
                Bounds bounds = GetBounds(support);
                // The guard and support can meet only when their X/Y ranges overlap.
                if (bounds.max.x <= guard.min.x || bounds.min.x >= guard.max.x
                    || bounds.max.y <= guard.min.y || bounds.min.y >= guard.max.y)
                    continue;
                float available = direction > 0f
                    ? guard.min.z - clearance - bounds.max.z
                    : bounds.min.z - guard.max.z - clearance;
                if (available < distance)
                    Debug.Log(string.Format("冲床夹爪避让：{0} 限制模型 Y 行程为 {1:F3}m，保护罩前保留 {2:F3}m 间隙。",
                        support.name, Mathf.Max(0f, available), clearance), support);
                distance = Mathf.Min(distance, Mathf.Max(0f, available));
            }
        }
        return direction * distance;
    }

    public static bool SeatPunchAssembly(Transform assembly, Transform bracket,
        Transform frame, Transform rail, Renderer table, Transform jawA,
        Transform fingerA, Transform jawB, Transform fingerB,
        float grooveInset, float outsideClearance)
    {
        Transform slider = FindModelNode(assembly, 1420);
        Vector3 near, far;
        if (slider == null || !TryRailMeshEnds(rail, out near, out far)) return false;
        Vector3 axis = far - near;
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.01f) return false;
        axis.Normalize();
        Vector3 side = table.bounds.center - GetBounds(frame).center;
        side.y = 0f;
        side -= axis * Vector3.Dot(side, axis);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(Vector3.up, axis);
        side.Normalize();

        Vector3 before = assembly.position;
        Vector3 sliderCenter = GetBounds(slider).center;
        Vector3 bracketCenter = GetBounds(bracket).center;
        assembly.position += axis * Vector3.Dot(bracketCenter - sliderCenter, axis);
        float frameOuter = MeshProjectionExtreme(frame, side, true);
        float sliderInner = MeshProjectionExtreme(slider, side, false);
        float bracketSide = Vector3.Dot(bracketCenter, side);
        float sliderSide = Vector3.Dot(GetBounds(slider).center, side);
        assembly.position += side * Mathf.Max(frameOuter + outsideClearance - sliderInner,
            bracketSide - sliderSide);

        float minimumOffset = frameOuter + outsideClearance
            - MeshProjectionExtreme(slider, side, false);
        bool foundA, foundB;
        float offsetA = FindNearestGrooveOffset(table, fingerA, side, grooveInset,
            out foundA);
        float offsetB = FindNearestGrooveOffset(table, fingerB, side, grooveInset,
            out foundB);
        if (!foundA || !foundB)
            Debug.LogWarning("LoadingProcessController: 冲床夹指未找到匹配的桌面窄槽："
                + (!foundA ? fingerA.name : fingerB.name), assembly);
        float assemblyOffset = Mathf.Max(minimumOffset, (offsetA + offsetB) * 0.5f);
        assembly.position += side * assemblyOffset;
        // Each jaw pair has its own slot. Move both halves together so closing
        // still acts across the sheet and the slider stays on the rail exterior.
        Vector3 correctionA = side * (offsetA - assemblyOffset);
        Vector3 correctionB = side * (offsetB - assemblyOffset);
        jawA.position += correctionA;
        fingerA.position += correctionA;
        jawB.position += correctionB;
        fingerB.position += correctionB;
        Bounds fingers = GetBounds(fingerA);
        fingers.Encapsulate(GetBounds(fingerB));
        float grooveFloor = (SampleTableHeight(table, FingerContactCenter(fingerA), grooveInset)
            + SampleTableHeight(table, FingerContactCenter(fingerB), grooveInset)) * 0.5f;
        assembly.position += Vector3.up * (grooveFloor - fingers.min.y);
        return Vector3.Distance(before, assembly.position) > 0.0001f
            || correctionA.sqrMagnitude > 0.00000001f
            || correctionB.sqrMagnitude > 0.00000001f;
    }

    private static float MeshProjectionExtreme(Transform part, Vector3 axis, bool maximum)
    {
        MeshFilter filter = part.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            try
            {
                float extreme = maximum ? float.NegativeInfinity : float.PositiveInfinity;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    float projection = Vector3.Dot(filter.transform.TransformPoint(vertex), axis);
                    extreme = maximum ? Mathf.Max(extreme, projection)
                        : Mathf.Min(extreme, projection);
                }
                if (!float.IsInfinity(extreme)) return extreme;
            }
            catch (UnityException) { }
        }
        Bounds bounds = GetBounds(part);
        float center = Vector3.Dot(bounds.center, axis);
        return center + (maximum ? 1f : -1f) * HorizontalRadius(bounds, axis);
    }

    private static Vector3 FingerContactCenter(Transform finger)
    {
        MeshFilter filter = finger.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return GetBounds(finger).center;
        try
        {
            Vector3[] vertices = filter.sharedMesh.vertices;
            float bottom = GetBounds(finger).min.y;
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Vector3 vertex in vertices)
            {
                Vector3 point = finger.TransformPoint(vertex);
                if (point.y > bottom + 0.004f) continue;
                sum += point;
                count++;
            }
            return count > 0 ? sum / count : GetBounds(finger).center;
        }
        catch (UnityException) { return GetBounds(finger).center; }
    }

    private static float FindNearestGrooveOffset(Renderer table, Transform finger,
        Vector3 side, float fallbackInset, out bool found)
    {
        found = false;
        const int count = 121;
        const float step = 0.005f;
        float[] heights = new float[count];
        bool[] inside = new bool[count];
        float highest = float.NegativeInfinity;
        Vector3 contact = FingerContactCenter(finger);
        Bounds tableBounds = table.bounds;
        for (int i = 0; i < count; i++)
        {
            float offset = (i - count / 2) * step;
            Vector3 point = contact + side * offset;
            inside[i] = tableBounds.Contains(new Vector3(point.x,
                tableBounds.center.y, point.z));
            if (!inside[i]) continue;
            heights[i] = SampleTableHeight(table, point, fallbackInset);
            highest = Mathf.Max(highest, heights[i]);
        }
        float best = 0f;
        float bestDistance = float.PositiveInfinity;
        double sliceStart = Time.realtimeSinceStartupAsDouble;
        for (int start = 0; start < count;)
        {
            if (!inside[start] || heights[start] > highest - 0.004f)
            {
                start++;
                continue;
            }
            int end = start;
            while (end + 1 < count && inside[end + 1]
                && heights[end + 1] <= highest - 0.004f) end++;
            float width = (end - start + 1) * step;
            if (width >= 0.01f && width <= 0.12f)
            {
                float center = ((start + end) * 0.5f - count / 2) * step;
                if (Mathf.Abs(center) < bestDistance)
                {
                    best = center;
                    bestDistance = Mathf.Abs(center);
                    found = true;
                }
            }
            start = end + 1;
        }
        return best;
    }

    private IEnumerator MoveJawPair(Transform jawA, Transform jawB, bool close,
        Vector3 homeA, Vector3 homeB)
    {
        if (jawA == null || jawB == null) yield break;
        Vector3 direction = jawB.position - jawA.position;
        if (direction.sqrMagnitude < 0.000001f) yield break;
        direction.Normalize();
        Vector3 targetA = close ? jawA.position + direction * (jawCloseDistance * 0.5f)
            : jawA.parent.TransformPoint(homeA);
        Vector3 targetB = close ? jawB.position - direction * (jawCloseDistance * 0.5f)
            : jawB.parent.TransformPoint(homeB);
        while (Vector3.Distance(jawA.position, targetA) > positionTolerance
            || Vector3.Distance(jawB.position, targetB) > positionTolerance)
        {
            jawA.position = Vector3.MoveTowards(jawA.position, targetA,
                jawCloseSpeed * MotionDelta);
            jawB.position = Vector3.MoveTowards(jawB.position, targetB,
                jawCloseSpeed * MotionDelta);
            yield return null;
        }
        jawA.position = targetA;
        jawB.position = targetB;
    }

    private bool RailTargetValid(Vector3 origin, Vector3 target, Transform home,
        Transform end, string mechanism)
    {
        Vector3 axis = end.position - home.position;
        axis.y = 0f;
        float length = axis.magnitude;
        if (length < 0.01f) return Fail(mechanism + "导轨端点重合");
        axis /= length;
        Vector3 relative = target - home.position;
        relative.y = 0f;
        float travel = Vector3.Dot(relative, axis);
        float crossTrack = (relative - axis * travel).magnitude;
        Vector3 move = target - origin;
        move.y = 0f;
        float moveCrossTrack = (move - axis * Vector3.Dot(move, axis)).magnitude;
        if (travel < -0.015f || travel > length + 0.015f
            || crossTrack > 0.12f || moveCrossTrack > 0.12f
            || Mathf.Abs(target.y - home.position.y) > 0.05f)
            return Fail(string.Format(
                "{0}目标不在导轨内：行程 {1:F3}/{2:F3}m，横向偏差 {3:F3}m，移动横向偏差 {4:F3}m，高差 {5:F3}m；请校准 LoadingPoints",
                mechanism, travel, length, crossTrack, moveCrossTrack,
                target.y - home.position.y));
        return true;
    }

    private void PlaceTransferOnClampLeft()
    {
        Vector3 axis = pickupRailEndPoint.position - pickupRailStartPoint.position;
        axis.y = 0f;
        axis.Normalize();
        Vector3 relative = transferPoint.position - pickupClampHomePoint.position;
        relative.y = 0f;
        float along = Vector3.Dot(relative, axis);
        float lateral = (relative - axis * along).magnitude;
        if (along < 0.05f || lateral > 0.04f)
        {
            Vector3 target = pickupClampHomePoint.position
                + axis * (rawSheetWorldWidth * 0.7f);
            transferPoint.position = new Vector3(target.x, transferPoint.position.y, target.z);
        }
    }

    private bool CalibratePickupRailFromMesh()
    {
        MeshFilter railFilter = clampRail.GetComponent<MeshFilter>();
        Transform sliderA = FindModelNode(1007);
        Transform sliderB = FindModelNode(1010);
        if (railFilter == null || railFilter.sharedMesh == null
            || sliderA == null || sliderB == null)
            return Fail("找不到 r1087 导轨网格或 r1007/r1010 夹爪架滑块，无法计算真实行程");

        Bounds local = railFilter.sharedMesh.bounds;
        Vector3 localAxis = Vector3.right;
        float halfLength = local.extents.x;
        if (local.extents.y > halfLength)
        {
            localAxis = Vector3.up;
            halfLength = local.extents.y;
        }
        if (local.extents.z > halfLength)
        {
            localAxis = Vector3.forward;
            halfLength = local.extents.z;
        }
        Vector3 near = railFilter.transform.TransformPoint(local.center - localAxis * halfLength);
        Vector3 far = railFilter.transform.TransformPoint(local.center + localAxis * halfLength);
        Vector3 towardPunch = GetBounds(punchMachine).center - pickupClampHomePoint.position;
        towardPunch.y = 0f;
        if (Vector3.Dot(far - near, towardPunch) < 0f)
        {
            Vector3 swap = near;
            near = far;
            far = swap;
        }
        Vector3 axis = far - near;
        axis.y = 0f;
        float railLength = axis.magnitude;
        if (railLength < 0.5f) return Fail("r1087 导轨网格长轴异常");
        axis /= railLength;

        Bounds sliders = GetBounds(sliderA);
        sliders.Encapsulate(GetBounds(sliderB));
        float sliderMin = Vector3.Dot(sliders.center, axis) - HorizontalRadius(sliders, axis);
        float sliderMax = Vector3.Dot(sliders.center, axis) + HorizontalRadius(sliders, axis);
        float startTravel = Vector3.Dot(near, axis) + 0.05f - sliderMin;
        float endTravel = Vector3.Dot(far, axis) - 0.05f - sliderMax;
        if (endTravel < 0.2f)
            return Fail(string.Format("夹爪架滑块与 r1087 导轨不对齐：后限 {0:F3}m，前限 {1:F3}m",
                startTravel, endTravel));
        pickupRailStartPoint.position = pickupClampHomePoint.position + axis * startTravel;
        pickupRailEndPoint.position = pickupClampHomePoint.position + axis * endTravel;
        Debug.Log(string.Format("LoadingProcessController: r1087 实际导轨方向 {0}，夹爪架安全行程 0..{1:F3}m。",
            axis, endTravel), this);
        return true;
    }

    private bool CalibrateSuctionRailFromMesh()
    {
        Vector3 near, far, rackNear, rackFar;
        if (!TryRailMeshEnds(suctionRail, out near, out far)
            || !TryRailMeshEnds(suctionRack, out rackNear, out rackFar))
            return Fail("找不到 r1090 导轨或 r1120 齿条的网格长轴");
        Vector3 towardTransfer = pickupClampHomePoint.position - materialTable.bounds.center;
        towardTransfer.y = 0f;
        if (Vector3.Dot(far - near, towardTransfer) < 0f)
        {
            Vector3 swap = near;
            near = far;
            far = swap;
        }
        suctionRailAxis = far - near;
        suctionRailAxis.y = 0f;
        if (suctionRailAxis.magnitude < 1f) return Fail("r1090 吸盘导轨长度异常");
        suctionRailAxis.Normalize();
        Vector3 rackAxis = rackFar - rackNear;
        rackAxis.y = 0f;
        if (rackAxis.magnitude < 1f
            || Mathf.Abs(Vector3.Dot(suctionRailAxis, rackAxis.normalized)) < 0.98f)
            return Fail("r1120 齿条与 r1090 导轨不平行");

        int[] sliderIds = { 1139, 1142, 1143, 1144 };
        Bounds sliders = new Bounds();
        for (int i = 0; i < sliderIds.Length; i++)
        {
            Transform slider = FindModelNode(sliderIds[i]);
            if (slider == null) return Fail("吸盘机箱滑块模型缺失：r" + sliderIds[i]);
            if (i == 0) sliders = GetBounds(slider);
            else sliders.Encapsulate(GetBounds(slider));
        }
        float sliderMin = Vector3.Dot(sliders.center, suctionRailAxis)
            - HorizontalRadius(sliders, suctionRailAxis);
        float sliderMax = Vector3.Dot(sliders.center, suctionRailAxis)
            + HorizontalRadius(sliders, suctionRailAxis);
        float measuredMinTravel = Vector3.Dot(near, suctionRailAxis) + 0.05f - sliderMin;
        suctionMinTravel = Mathf.Min(0f, measuredMinTravel);
        suctionMaxTravel = Vector3.Dot(far, suctionRailAxis) - 0.05f - sliderMax;
        if (measuredMinTravel > 0.1f || suctionMaxTravel < 0.1f)
            return Fail(string.Format("吸盘滑块未落在 r1090 导轨内：允许行程 {0:F3}..{1:F3}m",
                suctionMinTravel, suctionMaxTravel));

        MeshFilter gearFilter = suctionGear.GetComponent<MeshFilter>();
        if (gearFilter == null || gearFilter.sharedMesh == null)
            return Fail("找不到 r1138 齿轮网格");
        Vector3 extents = gearFilter.sharedMesh.bounds.extents;
        suctionGearLocalAxis = Vector3.right;
        if (extents.y < extents.x && extents.y <= extents.z)
            suctionGearLocalAxis = Vector3.up;
        else if (extents.z < extents.x && extents.z < extents.y)
            suctionGearLocalAxis = Vector3.forward;
        Vector3 radialA = suctionGearLocalAxis == Vector3.right ? Vector3.up : Vector3.right;
        Vector3 radialB = suctionGearLocalAxis == Vector3.forward ? Vector3.up : Vector3.forward;
        float extentA = Vector3.Scale(extents, radialA).magnitude;
        float extentB = Vector3.Scale(extents, radialB).magnitude;
        suctionGearRadius = Mathf.Max(0.01f,
            Mathf.Max(gearFilter.transform.TransformVector(radialA * extentA).magnitude,
                gearFilter.transform.TransformVector(radialB * extentB).magnitude));
        Vector3 gearCenter = GetBounds(suctionGear).center;
        float rackA = Vector3.Dot(rackNear, suctionRailAxis);
        float rackB = Vector3.Dot(rackFar, suctionRailAxis);
        float gearAtHome = Vector3.Dot(gearCenter, suctionRailAxis);
        suctionMinTravel = Mathf.Max(suctionMinTravel,
            Mathf.Min(rackA, rackB) + 0.02f - gearAtHome);
        suctionMaxTravel = Mathf.Min(suctionMaxTravel,
            Mathf.Max(rackA, rackB) - 0.02f - gearAtHome);
        if (suctionMinTravel > 0.03f || suctionMaxTravel < 0.1f)
            return Fail("r1138 齿轮与 r1120 齿条的可用行程不足");
        suctionMinTravel = Mathf.Min(0f, suctionMinTravel);
        Vector3 towardRack = GetBounds(suctionRack).center - gearCenter;
        towardRack -= suctionRailAxis * Vector3.Dot(towardRack, suctionRailAxis);
        Vector3 worldAxle = suctionGear.TransformDirection(suctionGearLocalAxis);
        float tangent = Vector3.Dot(Vector3.Cross(worldAxle, towardRack), suctionRailAxis);
        suctionGearRollSign = tangent > 0f ? -1f : 1f;

        if (!AlignSuctionPoint(materialPickupPoint) || !AlignSuctionPoint(transferPoint))
            return false;
        Bounds table = materialTable.bounds;
        if (Mathf.Abs(materialPickupPoint.position.x - table.center.x) > table.extents.x + 0.02f
            || Mathf.Abs(materialPickupPoint.position.z - table.center.z) > table.extents.z + 0.02f)
            return Fail("吸盘导轨投影后的取料点不在原料台上");
        Vector3 pickup = materialPickupPoint.position;
        materialLiftPoint.position = new Vector3(pickup.x, materialLiftPoint.position.y, pickup.z);
        Vector3 relative = transferPoint.position - pickupClampHomePoint.position;
        relative.y = 0f;
        Vector3 clampAxis = pickupRailEndPoint.position - pickupClampHomePoint.position;
        clampAxis.y = 0f;
        clampAxis.Normalize();
        float sideOffset = (relative - clampAxis * Vector3.Dot(relative, clampAxis)).magnitude;
        if (sideOffset > rawSheetWorldWidth * 0.5f)
            return Fail(string.Format("吸盘导轨与夹爪交接线相距 {0:F3}m，板材宽度不足以交接", sideOffset));
        Debug.Log(string.Format("LoadingProcessController: r1090 实际导轨方向 {0}，吸盘滑块行程 {1:F3}..{2:F3}m。",
            suctionRailAxis, suctionMinTravel, suctionMaxTravel), this);
        return true;
    }

    private bool AlignSuctionPoint(Transform point)
    {
        Vector3 offset = point.position - suctionHome.position;
        offset.y = 0f;
        float travel = Vector3.Dot(offset, suctionRailAxis);
        if (travel < suctionMinTravel - 0.02f || travel > suctionMaxTravel + 0.02f)
            return Fail(string.Format("{0} 超出吸盘导轨行程：{1:F3}m，允许 {2:F3}..{3:F3}m",
                point.name, travel, suctionMinTravel, suctionMaxTravel));
        Vector3 projected = suctionHome.position + suctionRailAxis * travel;
        point.position = new Vector3(projected.x, point.position.y, projected.z);
        return true;
    }

    private static bool TryRailMeshEnds(Transform rail, out Vector3 near, out Vector3 far)
    {
        near = far = Vector3.zero;
        MeshFilter filter = rail.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;
        Bounds local = filter.sharedMesh.bounds;
        Vector3 axis = Vector3.right;
        float halfLength = local.extents.x;
        if (local.extents.y > halfLength)
        {
            axis = Vector3.up;
            halfLength = local.extents.y;
        }
        if (local.extents.z > halfLength)
        {
            axis = Vector3.forward;
            halfLength = local.extents.z;
        }
        near = filter.transform.TransformPoint(local.center - axis * halfLength);
        far = filter.transform.TransformPoint(local.center + axis * halfLength);
        return true;
    }

    private Vector3 PickupTargetAtSheetRightEdge()
    {
        Vector3 axis = pickupRailEndPoint.position - pickupRailStartPoint.position;
        axis.y = 0f;
        axis.Normalize();
        Bounds board = GetBounds(sheet);
        float halfWidth = Mathf.Abs(axis.x) * board.extents.x
            + Mathf.Abs(axis.z) * board.extents.z;
        // 图中板材应在黄色夹爪左侧；夹爪抓取它靠近自身的右边缘。
        Vector3 rightEdge = board.center - axis * halfWidth;
        Vector3 jaws = (clampJawA.position + clampJawB.position) * 0.5f;
        float travel = Vector3.Dot(rightEdge - jaws, axis);
        Vector3 target = clampMover.position + axis * travel;
        target.y = clampPickupPoint.position.y;
        return target;
    }

    private static float HorizontalRadius(Bounds bounds, Vector3 axis)
    {
        return Mathf.Abs(axis.x) * bounds.extents.x
            + Mathf.Abs(axis.z) * bounds.extents.z;
    }

    private static Transform CreateRuntimeRailStart(Transform rail, Transform home,
        Transform end, string name)
    {
        Vector3 direction = end.position - home.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return null;
        direction.Normalize();
        Bounds bounds = GetBounds(rail);
        float radius = Mathf.Abs(direction.x) * bounds.extents.x
            + Mathf.Abs(direction.z) * bounds.extents.z;
        float minimum = Vector3.Dot(bounds.center, direction) - radius + 0.08f;
        float travel = Vector3.Dot(home.position, direction) - minimum;
        GameObject point = new GameObject(name);
        point.transform.position = home.position - direction * Mathf.Max(0f, travel);
        return point.transform;
    }

    private bool Fail(string reason)
    {
        ReplayError = reason;
        Debug.LogError("LoadingProcessController: " + reason, this);
        message = reason;
        sequence = null;
        return false;
    }

    private void CreateMaterialPile()
    {
        Bounds table = materialTable.bounds;
        Transform floor = FindModelNode(4);
        float floorY = floor != null ? GetBounds(floor).max.y : table.min.y - 0.8f;
        Vector3 pilePosition = materialPilePoint != null
            ? materialPilePoint.position : FindFreeMaterialPilePosition(table, floor, floorY);
        pilePosition.y = floorY;
        materialPileRoot = new GameObject("Material Pile (runtime)").transform;
        materialPileRoot.position = pilePosition;
        float nextBottom = floorY + 0.001f;
        for (int i = 0; i < initialMaterialCount; i++)
        {
            // rawSheetPrefab is Assets/Moudles/加工零件.fbx in SampleScene.
            GameObject visual = Instantiate(rawSheetPrefab, materialPileRoot);
            visual.name = "Stock Sheet " + (i + 1);
            OrientSheetFlat(visual.transform);
            Bounds bounds = GetBounds(visual.transform);
            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            if (width > 0.0001f)
                visual.transform.localScale *= rawSheetWorldWidth / width;
            ApplyWorkpieceColor(visual.transform);
            bounds = GetBounds(visual.transform);
            visual.transform.position += new Vector3(pilePosition.x, nextBottom, pilePosition.z)
                - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            nextBottom = GetBounds(visual.transform).max.y + materialPileSpacing;
            materialPileVisuals.Add(visual);
        }
        RefreshMaterialPile();
    }

    private Vector3 FindFreeMaterialPilePosition(Bounds table, Transform floor,
        float floorY)
    {
        // Search the open floor around the raw table, starting on the side away
        // from the control console. A mesh bounds check keeps the stack out of
        // consoles, machine bases and other equipment without requiring colliders.
        Vector3 away = table.center - controlConsole.bounds.center;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
        away.Normalize();
        Bounds floorBounds = floor != null ? GetBounds(floor) : new Bounds();
        Renderer[] obstacles = FindObjectsOfType<Renderer>();
        for (int ring = 0; ring < 10; ring++)
        {
            for (int directionIndex = 0; directionIndex < 13; directionIndex++)
            {
                float angle = directionIndex == 0 ? 0f
                    : (directionIndex % 2 == 1 ? 1f : -1f)
                        * ((directionIndex + 1) / 2) * 30f;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * away;
                float distance = HorizontalRadius(table, direction)
                    + rawSheetWorldWidth * 0.5f + 1.1f + ring * 0.55f;
                Vector3 candidate = table.center + direction * distance;
                candidate.y = floorY;
                if (IsMaterialPileAreaClear(candidate, floor, floorBounds, obstacles, floorY))
                {
                    Debug.Log("LoadingProcessController: 原料堆空地位置 " + candidate, this);
                    return candidate;
                }
            }
        }
        Vector3 fallback = table.center + away
            * (HorizontalRadius(table, away) + rawSheetWorldWidth * 0.5f + 2.5f);
        Debug.LogWarning("LoadingProcessController: 未找到完全空旷的原料堆位置，请指定 materialPilePoint。", this);
        return fallback;
    }

    private bool IsMaterialPileAreaClear(Vector3 center, Transform floor,
        Bounds floorBounds, Renderer[] obstacles, float floorY)
    {
        float halfX = rawSheetFootprint.x * 0.5f + 0.35f;
        float halfZ = rawSheetFootprint.z * 0.5f + 0.35f;
        return IsGroundAreaClear(center, halfX, halfZ, floor,
            floorBounds, obstacles, floorY);
    }

    private bool IsGroundAreaClear(Vector3 center, float halfX,
        float halfZ, Transform floor, Bounds floorBounds,
        Renderer[] obstacles, float floorY)
    {
        if (floor != null && (center.x - halfX < floorBounds.min.x
            || center.x + halfX > floorBounds.max.x
            || center.z - halfZ < floorBounds.min.z
            || center.z + halfZ > floorBounds.max.z)) return false;
        foreach (Renderer obstacle in obstacles)
        {
            if (obstacle == null || obstacle.transform.IsChildOf(workpieceRoot)
                || (floor != null && obstacle.transform.IsChildOf(floor))) continue;
            Bounds bounds = obstacle.bounds;
            if (bounds.max.y < floorY + 0.02f || bounds.min.y > floorY + 1.75f)
                continue;
            if (center.x + halfX > bounds.min.x && center.x - halfX < bounds.max.x
                && center.z + halfZ > bounds.min.z && center.z - halfZ < bounds.max.z)
                return false;
        }
        return true;
    }

    private void CreateFinishedPlacementZone()
    {
        Transform floor = FindModelNode(4);
        Bounds floorBounds = floor != null ? GetBounds(floor) : new Bounds();
        finishedZoneFloorY = floor != null ? floorBounds.max.y
            : GetBounds(conveyorPlate).min.y - 0.8f;
        float width = Mathf.Max(1.2f, finishedPlacementZoneSize.x);
        float depth = Mathf.Max(1.2f, finishedPlacementZoneSize.y);
        Vector3 position = finishedPlacementZonePoint != null
            ? finishedPlacementZonePoint.position
            : FindFreeFinishedZonePosition(floor, floorBounds,
                finishedZoneFloorY, width, depth);
        position.y = finishedZoneFloorY;
        finishedPlacementZone = new GameObject("Finished Placement Zone (runtime)").transform;
        finishedPlacementZone.position = position;
        finishedProductsRoot = new GameObject("Finished Products (runtime)").transform;
        finishedProductsRoot.SetParent(transform, false);

        Shader shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        finishedZoneMaterial = new Material(shader);
        finishedZoneMaterial.color = new Color(0.05f, 0.65f, 1f, 0.22f);
        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surface.name = "Blue Finished Product Area";
        surface.transform.SetParent(finishedPlacementZone, false);
        surface.transform.localPosition = Vector3.up * 0.012f;
        surface.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        surface.transform.localScale = new Vector3(width, depth, 1f);
        surface.GetComponent<Renderer>().sharedMaterial = finishedZoneMaterial;
        Collider surfaceCollider = surface.GetComponent<Collider>();
        if (surfaceCollider != null) Destroy(surfaceCollider);

        finishedZoneBorderMaterial = new Material(shader);
        finishedZoneBorderMaterial.color = new Color(0.05f, 0.9f, 1f, 0.9f);
        GameObject borderObject = new GameObject("Finished Area Border");
        borderObject.transform.SetParent(finishedPlacementZone, false);
        LineRenderer border = borderObject.AddComponent<LineRenderer>();
        border.sharedMaterial = finishedZoneBorderMaterial;
        border.useWorldSpace = false;
        border.loop = true;
        border.positionCount = 4;
        border.startWidth = border.endWidth = 0.035f;
        border.SetPosition(0, new Vector3(-width * 0.5f, 0.025f, -depth * 0.5f));
        border.SetPosition(1, new Vector3(width * 0.5f, 0.025f, -depth * 0.5f));
        border.SetPosition(2, new Vector3(width * 0.5f, 0.025f, depth * 0.5f));
        border.SetPosition(3, new Vector3(-width * 0.5f, 0.025f, depth * 0.5f));
    }

    private Vector3 FindFreeFinishedZonePosition(Transform floor,
        Bounds floorBounds, float floorY, float width, float depth)
    {
        Vector3 endA, endB;
        if (!TryRailMeshEnds(conveyorPlate, out endA, out endB))
            return GetBounds(conveyorPlate).center + Vector3.right * 2f;
        Vector3 turntable = GetBounds(hTurntable).center;
        Vector3 far = (endA - turntable).sqrMagnitude > (endB - turntable).sqrMagnitude
            ? endA : endB;
        Vector3 near = far == endA ? endB : endA;
        Vector3 axis = far - near;
        axis.y = 0f;
        axis.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, axis).normalized;
        Bounds conveyor = GetBounds(conveyorPlate);
        Renderer[] obstacles = FindObjectsOfType<Renderer>();
        float halfX = width * 0.5f + 0.3f;
        float halfZ = depth * 0.5f + 0.3f;
        float sideExtent = Mathf.Abs(side.x) * halfX + Mathf.Abs(side.z) * halfZ;
        float baseline = HorizontalRadius(conveyor, side) + sideExtent + 0.6f;
        for (int ring = 0; ring < 10; ring++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                for (int along = 0; along < 3; along++)
                {
                    Vector3 candidate = far - axis * (along * 0.55f)
                        + side * sign * (baseline + ring * 0.45f);
                    candidate.y = floorY;
                    if (!IsGroundAreaClear(candidate, halfX, halfZ, floor,
                        floorBounds, obstacles, floorY)) continue;
                    Debug.Log("LoadingProcessController: 成品放置区域 " + candidate, this);
                    return candidate;
                }
            }
        }
        Debug.LogWarning("LoadingProcessController: 未找到传送带旁的完整空地，请指定 finishedPlacementZonePoint。", this);
        return far + side * baseline;
    }

    private bool IsNearFinishedProduct()
    {
        Camera camera = Camera.main;
        if (camera == null || sheet == null || finishedProductPlaced) return false;
        Bounds bounds = GetBounds(sheet);
        Vector3 eye = camera.transform.position;
        Vector3 nearest = bounds.ClosestPoint(new Vector3(eye.x, bounds.center.y, eye.z));
        Vector3 delta = eye - nearest;
        delta.y = 0f;
        return delta.magnitude <= finishedPickupDistance;
    }

    private bool IsNearFinishedZone()
    {
        Camera camera = Camera.main;
        if (camera == null || finishedPlacementZone == null) return false;
        Vector3 center = finishedPlacementZone.position;
        Vector3 eye = camera.transform.position;
        float halfX = Mathf.Max(1.2f, finishedPlacementZoneSize.x) * 0.5f;
        float halfZ = Mathf.Max(1.2f, finishedPlacementZoneSize.y) * 0.5f;
        float dx = eye.x - Mathf.Clamp(eye.x, center.x - halfX, center.x + halfX);
        float dz = eye.z - Mathf.Clamp(eye.z, center.z - halfZ, center.z + halfZ);
        return new Vector2(dx, dz).magnitude <= materialInteractionDistance;
    }

    private void UpdateFinishedPlacementPreview()
    {
        finishedPreviewValid = finishedPreviewHasPoint = false;
        finishedPreviewReason = "靠近传送带旁的蓝色放置区域";
        if (!finishedProductCarried || finishedPlacementPreview == null
            || !IsNearFinishedZone())
        {
            if (finishedPlacementPreview != null)
                finishedPlacementPreview.gameObject.SetActive(false);
            return;
        }
        Camera camera = Camera.main;
        Ray ray = new Ray(camera.transform.position, camera.transform.forward);
        Plane floorPlane = new Plane(Vector3.up,
            new Vector3(0f, finishedZoneFloorY, 0f));
        float distance;
        if (!floorPlane.Raycast(ray, out distance)
            || distance > materialInteractionDistance * 2f)
        {
            finishedPreviewReason = "请看向蓝色放置区域内的地面";
            finishedPlacementPreview.gameObject.SetActive(false);
            return;
        }
        Vector3 hit = ray.GetPoint(distance);
        finishedPreviewHasPoint = true;
        finishedPreviewBottom = new Vector3(hit.x, finishedZoneFloorY + 0.003f, hit.z);
        finishedPlacementPreview.gameObject.SetActive(true);
        Bounds bounds = GetBounds(finishedPlacementPreview);
        finishedPlacementPreview.position += finishedPreviewBottom
            - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Vector3 zone = finishedPlacementZone.position;
        float halfZoneX = Mathf.Max(1.2f, finishedPlacementZoneSize.x) * 0.5f;
        float halfZoneZ = Mathf.Max(1.2f, finishedPlacementZoneSize.y) * 0.5f;
        finishedPreviewValid = hit.x - bounds.extents.x >= zone.x - halfZoneX + 0.03f
            && hit.x + bounds.extents.x <= zone.x + halfZoneX - 0.03f
            && hit.z - bounds.extents.z >= zone.z - halfZoneZ + 0.03f
            && hit.z + bounds.extents.z <= zone.z + halfZoneZ - 0.03f;
        finishedPreviewReason = finishedPreviewValid
            ? "绿色成品虚影：按 F 在此处放置"
            : "成品虚影必须完整位于蓝色区域内";
        if (finishedPreviewValid)
        {
            foreach (Transform existing in finishedProductsRoot)
            {
                Bounds occupied = GetBounds(existing);
                if (bounds.max.x <= occupied.min.x + 0.02f
                    || bounds.min.x >= occupied.max.x - 0.02f
                    || bounds.max.z <= occupied.min.z + 0.02f
                    || bounds.min.z >= occupied.max.z - 0.02f) continue;
                finishedPreviewValid = false;
                finishedPreviewReason = "该位置已有成品，请选择蓝色区域内的其他位置";
                break;
            }
        }
        if (placementPreviewMaterial != null)
            placementPreviewMaterial.color = finishedPreviewValid
                ? new Color(0.2f, 1f, 0.45f, 0.38f)
                : new Color(1f, 0.25f, 0.2f, 0.38f);
    }

    private void HandleFinishedProductInteraction()
    {
        if (finishedProductPlaced) return;
        if (!finishedProductCarried)
        {
            if (!IsNearFinishedProduct())
            {
                message = "靠近传送带末端的成品后按 F 拾取";
                return;
            }
            Camera camera = Camera.main;
            if (camera == null) return;
            finishedProductPlacementRotation = workpieceRoot.rotation;
            GameObject ghost = Instantiate(sheet.gameObject);
            ghost.name = "Finished Product Placement Preview (runtime)";
            finishedPlacementPreview = ghost.transform;
            foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                if (slots.Length == 0) slots = new Material[1];
                for (int i = 0; i < slots.Length; i++) slots[i] = placementPreviewMaterial;
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            ghost.SetActive(false);
            workpieceRoot.SetParent(camera.transform, false);
            workpieceRoot.localPosition = new Vector3(0.45f, -0.45f, 1.1f);
            workpieceRoot.localRotation = Quaternion.identity;
            finishedProductCarried = true;
            message = "已拾取成品；前往传送带旁的蓝色区域，按虚影位置放置";
            return;
        }
        if (!finishedPreviewValid)
        {
            message = finishedPreviewReason;
            return;
        }
        workpieceRoot.SetParent(workpieceHomeParent, true);
        workpieceRoot.rotation = finishedProductPlacementRotation;
        workpieceRoot.position += finishedPreviewBottom - SheetBottomCenter();
        sheet.SetParent(finishedProductsRoot, true);
        sheet = rawSheetInstance;
        finishedProductCarried = false;
        finishedProductPlaced = true;
        finishedPreviewValid = finishedPreviewHasPoint = false;
        Destroy(finishedPlacementPreview.gameObject);
        finishedPlacementPreview = null;
        message = "成品已放入蓝色区域；回控制台按 E 可准备下一件";
    }

    private void RefreshMaterialPile()
    {
        for (int i = 0; i < materialPileVisuals.Count; i++)
            materialPileVisuals[i].SetActive(i < materialsRemaining);
    }

    private bool IsNearMaterialPile()
    {
        Camera camera = Camera.main;
        if (camera == null || materialPileRoot == null) return false;
        Vector3 delta = camera.transform.position - materialPileRoot.position;
        delta.y = 0f;
        return delta.magnitude <= materialInteractionDistance;
    }

    private bool IsNearMaterialTable()
    {
        Camera camera = Camera.main;
        if (camera == null) return false;
        Bounds table = materialTable.bounds;
        Vector3 eye = camera.transform.position;
        Vector3 nearest = table.ClosestPoint(new Vector3(eye.x, table.center.y, eye.z));
        Vector3 delta = eye - nearest;
        delta.y = 0f;
        return delta.magnitude <= materialInteractionDistance;
    }

    private void CreatePlacementPreview()
    {
        GameObject ghost = Instantiate(rawSheetPrefab);
        ghost.name = "Material Placement Preview (runtime)";
        placementPreview = ghost.transform;
        OrientSheetFlat(placementPreview);
        Bounds bounds = GetBounds(placementPreview);
        float width = Mathf.Max(bounds.size.x, bounds.size.z);
        if (width > 0.0001f)
            placementPreview.localScale *= rawSheetWorldWidth / width;
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader != null)
        {
            placementPreviewMaterial = new Material(shader);
            placementPreviewMaterial.name = "Material Placement Ghost (runtime)";
            placementPreviewMaterial.color = new Color(0.2f, 1f, 0.45f, 0.38f);
            if (shader.name == "Standard")
            {
                placementPreviewMaterial.SetFloat("_Mode", 3f);
                placementPreviewMaterial.SetInt("_SrcBlend",
                    (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                placementPreviewMaterial.SetInt("_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                placementPreviewMaterial.SetInt("_ZWrite", 0);
                placementPreviewMaterial.DisableKeyword("_ALPHATEST_ON");
                placementPreviewMaterial.EnableKeyword("_ALPHABLEND_ON");
                placementPreviewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                placementPreviewMaterial.renderQueue =
                    (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                if (slots.Length == 0) slots = new Material[1];
                for (int i = 0; i < slots.Length; i++) slots[i] = placementPreviewMaterial;
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
        foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        ghost.SetActive(false);
    }

    private void UpdatePlacementPreview()
    {
        placementPreviewValid = placementPreviewHasPoint = false;
        placementPreviewReason = "靠近拾取机床原料台并看向台面";
        if (placementPreview == null || !carryingMaterial
            || CurrentState != LoadingState.Idle || !IsNearMaterialTable())
        {
            if (placementPreview != null) placementPreview.gameObject.SetActive(false);
            return;
        }
        placementPreviewValid = TryGetMaterialPlacement(out placementPreviewCenter,
            out placementPreviewPickup, out placementPreviewReason,
            out placementPreviewHasPoint);
        placementPreview.gameObject.SetActive(placementPreviewHasPoint);
        if (!placementPreviewHasPoint) return;
        Bounds bounds = GetBounds(placementPreview);
        placementPreview.position += placementPreviewCenter
            - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        if (placementPreviewMaterial != null)
            placementPreviewMaterial.color = placementPreviewValid
                ? new Color(0.2f, 1f, 0.45f, 0.38f)
                : new Color(1f, 0.25f, 0.2f, 0.38f);
    }

    private bool TryGetMaterialPlacement(out Vector3 center, out Vector3 pickup,
        out string reason, out bool hasPoint)
    {
        center = pickup = Vector3.zero;
        hasPoint = false;
        reason = "请看向原料台台面";
        Camera camera = Camera.main;
        if (camera == null || !IsNearMaterialTable()) return false;
        Bounds table = materialTable.bounds;
        Ray ray = new Ray(camera.transform.position, camera.transform.forward);
        Plane plane = new Plane(Vector3.up, new Vector3(0f, table.max.y, 0f));
        float distance;
        if (!plane.Raycast(ray, out distance) || distance > materialInteractionDistance * 2f)
            return false;
        Vector3 hit = ray.GetPoint(distance);
        center = new Vector3(hit.x, table.max.y + 0.002f, hit.z);
        hasPoint = true;
        if (hit.x < table.min.x || hit.x > table.max.x
            || hit.z < table.min.z || hit.z > table.max.z)
        {
            reason = "请将视线对准原料台台面";
            return false;
        }
        // The suction carriage has one horizontal axis. Snap the visible ghost
        // to the nearest reachable point on that axis so F places the sheet
        // exactly where the cups will later pick it up.
        float minTravel = suctionMinTravel;
        float maxTravel = suctionMaxTravel;
        if (!IntersectRailAxisWithTable(suctionHome.position.x, suctionRailAxis.x,
                table.min.x - 0.02f, table.max.x + 0.02f,
                ref minTravel, ref maxTravel)
            || !IntersectRailAxisWithTable(suctionHome.position.z, suctionRailAxis.z,
                table.min.z - 0.02f, table.max.z + 0.02f,
                ref minTravel, ref maxTravel)
            || minTravel > maxTravel)
        {
            reason = "吸盘导轨没有覆盖原料台面";
            return false;
        }
        Vector3 offset = hit - suctionHome.position;
        offset.y = 0f;
        float travel = Mathf.Clamp(Vector3.Dot(offset, suctionRailAxis),
            minTravel, maxTravel);
        pickup = suctionHome.position + suctionRailAxis * travel;
        center = new Vector3(pickup.x, table.max.y + 0.002f, pickup.z);
        pickup.y = pickupPointHome.y;
        reason = null;
        return true;
    }

    private static bool IntersectRailAxisWithTable(float origin, float direction,
        float minimum, float maximum, ref float minTravel, ref float maxTravel)
    {
        if (Mathf.Abs(direction) < 0.0001f)
            return origin >= minimum && origin <= maximum;
        float a = (minimum - origin) / direction;
        float b = (maximum - origin) / direction;
        minTravel = Mathf.Max(minTravel, Mathf.Min(a, b));
        maxTravel = Mathf.Min(maxTravel, Mathf.Max(a, b));
        return minTravel <= maxTravel;
    }

    private void HandleMaterialInteraction()
    {
        if (CurrentState != LoadingState.Idle || sequence != null) return;
        if (!carryingMaterial && !materialPlaced && IsNearMaterialPile())
        {
            if (materialsRemaining <= 0)
            {
                message = "原料堆已空";
                return;
            }
            Camera camera = Camera.main;
            if (camera == null) return;
            materialsRemaining--;
            RefreshMaterialPile();
            rawSheetInstance.gameObject.SetActive(true);
            workpieceRoot.SetParent(camera.transform, false);
            workpieceRoot.localPosition = new Vector3(0.4f, -0.45f, 0.95f);
            workpieceRoot.localRotation = Quaternion.identity;
            carryingMaterial = true;
            message = "已取原料；靠近拾取机床原料台，瞄准可吸取位置按 F 放置";
            return;
        }
        if (!carryingMaterial) return;
        if (!placementPreviewValid)
        {
            message = placementPreviewReason;
            return;
        }
        workpieceRoot.SetParent(workpieceHomeParent, false);
        workpieceRoot.localRotation = workpieceHomeLocalRotation;
        workpieceRoot.localScale = workpieceHomeLocalScale;
        rawSheetInstance.localPosition = rawSheetHomeLocalPosition;
        rawSheetInstance.localRotation = rawSheetHomeLocalRotation;
        rawSheetInstance.localScale = rawSheetHomeLocalScale;
        workpieceRoot.position += placementPreviewCenter - SheetBottomCenter();
        placedSheetCenter = placementPreviewCenter;
        materialPickupPoint.position = placementPreviewPickup;
        materialLiftPoint.position = new Vector3(placementPreviewPickup.x,
            liftPointHome.y, placementPreviewPickup.z);
        carryingMaterial = false;
        materialPlaced = true;
        placementPreviewValid = placementPreviewHasPoint = false;
        placementPreview.gameObject.SetActive(false);
        message = "原料已放在台面；吸盘将按实际位置取料，靠近控制台按 E 启动";
    }

    private bool ConfigurePickupFromPlacedSheet()
    {
        Bounds board = GetBounds(sheet);
        if (Vector3.Distance(placedSheetCenter,
            new Vector3(board.center.x, board.min.y, board.center.z)) > 0.02f)
            return Fail("原料位置已变化，请重新放料");
        return true;
    }

    private Vector3 SheetBottomCenter()
    {
        Bounds b = GetBounds(sheet);
        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    private static readonly System.Collections.Generic.List<Renderer> boundsRenderers = new System.Collections.Generic.List<Renderer>();
    private static Bounds GetBounds(Transform root)
    {
        // Bounds move with the joints; cache storage, never the world bounds.
        root.GetComponentsInChildren<Renderer>(true, boundsRenderers);
        if (boundsRenderers.Count == 0) return new Bounds(root.position, Vector3.zero);
        Bounds b = boundsRenderers[0].bounds;
        for (int i = 1; i < boundsRenderers.Count; i++) b.Encapsulate(boundsRenderers[i].bounds);
        boundsRenderers.Clear();
        return b;
    }

    public static float SampleTableHeight(Renderer table, Vector3 position, float fallbackInset)
    {
        float fallback = table.bounds.max.y - fallbackInset;
        MeshFilter filter = table.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return fallback;
        try
        {
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            Transform t = filter.transform;
            float surface = float.NegativeInfinity;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = t.TransformPoint(vertices[triangles[i]]);
                Vector3 b = t.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 c = t.TransformPoint(vertices[triangles[i + 2]]);
                float denominator = (b.z - c.z) * (a.x - c.x)
                    + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < 0.0000001f) continue;
                float u = ((b.z - c.z) * (position.x - c.x)
                    + (c.x - b.x) * (position.z - c.z)) / denominator;
                float v = ((c.z - a.z) * (position.x - c.x)
                    + (a.x - c.x) * (position.z - c.z)) / denominator;
                if (u < -0.0001f || v < -0.0001f || u + v > 1.0001f) continue;
                surface = Mathf.Max(surface, u * a.y + v * b.y + (1f - u - v) * c.y);
            }
            return float.IsNegativeInfinity(surface) ? fallback : surface;
        }
        catch (UnityException)
        {
            return fallback;
        }
    }

    private float CalculateSuctionCarryHeight()
    {
        // The housing and vertical guide stay at a fixed height while the frame rises.
        Renderer[] renderers = suctionVisuals[0].GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds frame = new Bounds(suctionMover.position, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            Transform part = renderer.transform;
            if (part == suctionVerticalGuide || part.IsChildOf(suctionVerticalGuide)
                || part == suctionHousing || part.IsChildOf(suctionHousing)) continue;
            if (!found) { frame = renderer.bounds; found = true; }
            else frame.Encapsulate(renderer.bounds);
        }
        if (!found) frame = GetBounds(suctionVisuals[0]);
        float beamTop = GetBounds(pickupClampBeam).max.y;
        float frameBelowMover = suctionMover.position.y - frame.min.y;
        float safeHeight = Mathf.Max(materialLiftPoint.position.y,
            transferPoint.position.y + suctionReleaseClearance,
            beamTop + suctionReleaseClearance + frameBelowMover);
        Debug.Log(string.Format(
            "LoadingProcessController: suction clearance Y={0:F3}m, clamp beam top={1:F3}m, frame bottom offset={2:F3}m.",
            safeHeight, beamTop, -frameBelowMover), this);
        return safeHeight;
    }

    private static void OrientSheetFlat(Transform raw)
    {
        Bounds b = GetBounds(raw);
        float faceSize = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (b.size.y <= faceSize * 0.1f) return;
        Vector3 thinAxis = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
        raw.rotation = Quaternion.FromToRotation(thinAxis, Vector3.up) * raw.rotation;
    }

    private bool IsNearConsole()
    {
        Camera camera = Camera.main;
        if (camera == null || controlConsole == null) return false;
        Bounds bounds = controlConsole.bounds;
        Vector3 eye = camera.transform.position;
        Vector3 closest = bounds.ClosestPoint(new Vector3(eye.x, bounds.center.y, eye.z));
        Vector3 delta = eye - closest;
        delta.y = 0f;
        return delta.magnitude <= consoleInteractionDistance;
    }

    private Renderer FindControlConsole()
    {
        foreach (Renderer renderer in ReplayRenderers())
        {
            string name = renderer.name;
            int index = name.IndexOf("r646", System.StringComparison.Ordinal);
            if (index >= 0 && (index == 0 || name[index - 1] == '_')
                && (index + 4 == name.Length || name[index + 4] == '_'))
                return renderer;
        }
        return null;
    }

    private Transform FindModelNode(int sourceId)
    {
        string marker = "r" + sourceId;
        foreach (Transform candidate in ReplayNodes())
        {
            string name = candidate.name;
            int index = name.IndexOf(marker, System.StringComparison.Ordinal);
            if (index >= 0 && (index == 0 || name[index - 1] == '_')
                && (index + marker.Length == name.Length || name[index + marker.Length] == '_'))
                return candidate;
        }
        return null;
    }

    private static Transform FindModelNode(Transform root, int sourceId)
    {
        string marker = "r" + sourceId;
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            string name = candidate.name;
            int index = name.IndexOf(marker, System.StringComparison.Ordinal);
            if (index >= 0 && (index == 0 || name[index - 1] == '_')
                && (index + marker.Length == name.Length || name[index + marker.Length] == '_'))
                return candidate;
        }
        return null;
    }

    private static void ReparentWithoutJump(Transform child, Transform parent)
    {
        Vector3 before = child.position;
        Quaternion rotation = child.rotation;
        child.SetParent(parent, true);
        if (Vector3.Distance(before, child.position) > 0.0001f)
            Debug.LogError("LoadingProcessController: 工件控制权切换时发生位置跳变。");
        child.position = before;
        child.rotation = rotation;
    }

    private static Vector3[] SavePositions(Transform[] visuals)
    {
        if (visuals == null) return new Vector3[0];
        Vector3[] positions = new Vector3[visuals.Length];
        for (int i = 0; i < visuals.Length; i++)
            if (visuals[i] != null) positions[i] = visuals[i].position;
        return positions;
    }

    private static Quaternion[] SaveRotations(Transform[] visuals)
    {
        Quaternion[] rotations = new Quaternion[visuals.Length];
        for (int i = 0; i < visuals.Length; i++)
            rotations[i] = visuals[i].rotation;
        return rotations;
    }

    private static Vector3[] SaveLocalPositions(Transform[] parts)
    {
        if (parts == null) return new Vector3[0];
        Vector3[] positions = new Vector3[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null) positions[i] = parts[i].localPosition;
        return positions;
    }

    private static void RestoreLocalPositions(Transform[] parts, Vector3[] positions)
    {
        if (parts == null || positions == null) return;
        for (int i = 0; i < Mathf.Min(parts.Length, positions.Length); i++)
            if (parts[i] != null) parts[i].localPosition = positions[i];
    }

    private static void RestorePositions(Transform[] visuals, Vector3[] positions)
    {
        if (visuals == null || positions == null) return;
        for (int i = 0; i < Mathf.Min(visuals.Length, positions.Length); i++)
            if (visuals[i] != null) visuals[i].position = positions[i];
    }

#if UNITY_EDITOR
    private void EnsureBendingPieceReferencesForPlay()
    {
        if (bendingPiecePrefabs != null && bendingPiecePrefabs.Length == 5
            && System.Array.TrueForAll(bendingPiecePrefabs, prefab => prefab != null))
            return;
        GameObject[] pieces = new GameObject[5];
        for (int i = 0; i < pieces.Length; i++)
        {
            pieces[i] = bendingPiecePrefabs != null && i < bendingPiecePrefabs.Length
                ? bendingPiecePrefabs[i] : null;
            if (pieces[i] == null)
                pieces[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Moudles/加工零件 冲压完毕" + (i + 1) + ".fbx");
        }
        bendingPiecePrefabs = pieces;
    }
#endif

    private bool ValidateSetup()
    {
        if (bendingPiecePrefabs == null || bendingPiecePrefabs.Length != 5
            || System.Array.Exists(bendingPiecePrefabs, prefab => prefab == null))
        {
            message = "缺少冲压成品的五个折弯分件引用";
            Debug.LogError("LoadingProcessController: 缺少加工零件 冲压完毕1–5.fbx 引用。", this);
            return false;
        }
        bool valid = rawSheetPrefab != null && finishedPartPrefab != null
            && workpieceRoot != null && materialTable != null
            && armLongSlot != null && armBaseAssembly != null && bendingProbe != null
            && armBaseJoint != null && armSuctionAssembly != null
            && armSegments != null && System.Array.TrueForAll(armSegments, part => part != null)
            && armSliders != null && System.Array.TrueForAll(armSliders, slider => slider != null)
            && bendingBody != null && bendingRail != null
            && bendingUpper != null && bendingUpperMold != null
            && bendingLowerMold != null
            && FindModelNode(armSuctionAssembly, 863) != null
            && FindModelNode(armSuctionAssembly, 857) != null
            && FindModelNode(armSuctionAssembly, 860) != null
            && FindModelNode(armSuctionAssembly, 866) != null
            && FindModelNode(armSuctionAssembly, 867) != null
            && FindModelNode(armSuctionAssembly, 868) != null
            && controlConsole != null
            && suctionMover != null && clampMover != null && suctionHome != null
            && suctionRail != null && suctionRack != null && suctionGear != null
            && suctionHousing != null && suctionVerticalGuide != null
            && pickupClampBeam != null
            && punchClampMover != null && punchClampRail != null
            && punchRailFrame != null && punchRailShell != null
            && punchTable != null && punchClampBrackets != null
            && punchClampBrackets.Length == 2 && punchClampBrackets[0] != null
            && punchClampBrackets[1] != null
            && punchClampVisuals != null && punchClampVisuals.Length == 2
            && punchClampVisuals[0] != null && punchClampVisuals[1] != null
            && punchJawsA != null && punchJawsB != null
            && punchJawsA.Length == 4 && punchJawsB.Length == 4
            && System.Array.TrueForAll(punchJawsA, jaw => jaw != null)
            && System.Array.TrueForAll(punchJawsB, jaw => jaw != null)
            && materialPickupPoint != null && materialLiftPoint != null
            && transferPoint != null && clampPickupPoint != null
            && pickupClampHomePoint != null && pickupRailStartPoint != null
            && pickupRailEndPoint != null
            && punchClampHomePoint != null && punchClampAcquirePoint != null
            && punchRailStartPoint != null && punchRailEndPoint != null
            && transferToPunchPoint != null
            && punchEntryPoint != null && punchStartPoint != null;
        if (!valid)
        {
            message = "上料配置不完整，请检查 LoadingProcessController Inspector";
            Debug.LogError("LoadingProcessController: 缺少工件、设备或 LoadingPoints 引用。", this);
        }
        return valid;
    }

    private void SetState(LoadingState state, string description)
    {
        CurrentState = state;
        message = description;
        Debug.Log("上料状态: " + state + " — " + description, this);
    }

    private string GetTaskHint()
    {
        if (!initialized) return "正在准备场景";
        if (CurrentState == LoadingState.Idle)
        {
            if (materialPlaced) return "按 E 开始演示";
            if (carryingMaterial) return "按 F 放置原料";
            return materialsRemaining > 0 ? "按 F 拾取原料" : "原料已用完";
        }
        if (CurrentState == LoadingState.ReadyForPunching
            || CurrentState == LoadingState.TransferArmReturn)
        {
            if (finishedProductPlaced) return "按 E 开始下一轮";
            return finishedProductCarried ? "按 F 放置产物" : "按 F 拾取产物";
        }
        return "演示进行中，请等待产物";
    }

    private void OnGUI()
    {
        if (ReplayMode) return;
        if (uiFont != null) GUI.skin.font = uiFont;
        float panelWidth = Mathf.Min(530f, Screen.width - 24f);
        GUI.Box(new Rect(12, 12, panelWidth, 150), "任务提示");
        GUIStyle taskStyle = new GUIStyle(GUI.skin.label) { fontSize = 22 };
        GUI.Label(new Rect(24, 36, panelWidth - 24f, 32), GetTaskHint(), taskStyle);
        GUIStyle detailStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
        GUI.Label(new Rect(24, 72, panelWidth - 24f, 43), message, detailStyle);
        GUI.Label(new Rect(24, 120, panelWidth - 24f, 24),
            "剩余原料: " + materialsRemaining + "    R 重置当前工件");
        if (CurrentState == LoadingState.Idle)
        {
            string prompt = null;
            if (carryingMaterial)
                prompt = placementPreviewValid
                    ? "绿色虚影：按 F 在此位置放置原料" : placementPreviewReason;
            else if (!carryingMaterial && !materialPlaced && IsNearMaterialPile())
                prompt = materialsRemaining > 0 ? "按 F 从原料堆取板" : "原料堆已空";
            if (prompt != null)
                GUI.Box(new Rect(Screen.width * 0.5f - 175f,
                    Screen.height * 0.6f - 48f, 350f, 38f), prompt);
        }
        if (CurrentState == LoadingState.ReadyForPunching
            || CurrentState == LoadingState.TransferArmReturn)
        {
            string prompt = finishedProductCarried
                ? finishedPreviewReason
                : finishedProductPlaced ? null
                    : IsNearFinishedProduct() ? "按 F 拾取传送带末端的成品"
                        : "前往传送带末端，靠近成品按 F 拾取";
            if (prompt != null)
                GUI.Box(new Rect(Screen.width * 0.5f - 185f,
                    Screen.height * 0.6f - 48f, 370f, 38f), prompt);
        }
        if (initialized && (CurrentState == LoadingState.Idle || CurrentState == LoadingState.ReadyForPunching)
            && IsNearConsole())
            GUI.Box(new Rect(Screen.width * 0.5f - 125f, Screen.height * 0.6f, 250f, 38f),
                CurrentState == LoadingState.ReadyForPunching
                    ? finishedProductPlaced ? "按 E 准备下一件原料"
                        : "先拾取并放置成品" : materialPlaced
                        ? "按 E 操作控制台：开始上料" : "先取料并放到原料台");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        DrawSegment(suctionHome, materialPickupPoint);
        DrawSegment(materialPickupPoint, materialLiftPoint);
        DrawSegment(materialLiftPoint, transferPoint);
        Gizmos.color = Color.yellow;
        DrawSegment(pickupRailStartPoint, pickupClampHomePoint);
        DrawSegment(pickupClampHomePoint, pickupRailEndPoint);
        Gizmos.color = Color.green;
        DrawSegment(punchClampHomePoint, punchClampAcquirePoint);
        DrawSegment(transferToPunchPoint, punchStartPoint);
        DrawSegment(punchRailStartPoint, punchRailEndPoint);
    }

    private static void DrawSegment(Transform from, Transform to)
    {
        if (from == null || to == null) return;
        Gizmos.DrawSphere(from.position, 0.035f);
        Gizmos.DrawLine(from.position, to.position);
        Gizmos.DrawSphere(to.position, 0.035f);
    }
}
