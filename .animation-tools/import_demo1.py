from pathlib import Path
import hashlib

source = Path(r'D:/Unity/Programs/Demo1/Assets/Scripts/LoadingProcessController.cs')
target = Path('Assets/Scripts/Demo1/LoadingProcessController.cs')
s = source.read_text(encoding='utf-8-sig')
original_hash = hashlib.sha256(source.read_bytes()).hexdigest()
def replace(a, b):
    global s
    assert s.count(a) == 1, (a[:100], s.count(a))
    s = s.replace(a,b)

replace('public sealed class LoadingProcessController', 'public sealed partial class LoadingProcessController')
replace('    private void Start()\n', '    public void InitializeDemo1()\n')
replace('        uiFont = Resources.Load<Font>("NotoSansSC-UI");', '        if (!ReplayMode) uiFont = Resources.Load<Font>("NotoSansSC-UI");')
replace('        if (uiFont == null)\n', '        if (!ReplayMode && uiFont == null)\n')
replace('        CreateMaterialPile();\n        CreatePlacementPreview();\n        CreateFinishedPlacementZone();',
        '        if (!ReplayMode)\n        {\n            CreateMaterialPile();\n            CreatePlacementPreview();\n            CreateFinishedPlacementZone();\n        }')
replace('    private void Update()\n    {', '    private void Update()\n    {\n        if (ReplayMode) return;')
replace('    private void OnGUI()\n    {', '    private void OnGUI()\n    {\n        if (ReplayMode) return;')
replace('    private void OnDestroy()\n    {', '    private void OnDestroy()\n    {\n        if (ReplayMode) { ReleaseReplayResources(); return; }')
replace('    private static Renderer FindControlConsole()', '    private Renderer FindControlConsole()')
replace('    private static Transform FindModelNode(int sourceId)', '    private Transform FindModelNode(int sourceId)')
replace('foreach (Renderer renderer in FindObjectsOfType<Renderer>())', 'foreach (Renderer renderer in ReplayRenderers())')
replace('foreach (Transform candidate in FindObjectsOfType<Transform>())', 'foreach (Transform candidate in ReplayNodes())')
replace('        Debug.LogError("LoadingProcessController: " + reason, this);',
        '        ReplayError = reason;\n        Debug.LogError("LoadingProcessController: " + reason, this);')

# Only module-to-module rail travel is replaced. All local mechanical actions,
# geometry, timing, pose solvers, contact and collision checks remain Demo1's.
replace('''        Vector3 armDestination;
        if (!TryGetArmRailEnd(out armDestination)) yield break;''',
'''        yield return ReplayGate("PunchOutput");
        Vector3 armDestination;
        if (!TryGetArmRailEnd(out armDestination)) yield break;''')
replace('''            armDestination, armBaseMoveSpeed);
        Vector3 pivot''', '''            armDestination, armBaseMoveSpeed);
        yield return ReplayGate("PickupDock");
        Vector3 pivot''')
replace('''        yield return RestorePickupArmJoints();

        Vector3 probeRailDestination;''', '''        yield return RestorePickupArmJoints();
        yield return ReplayGate("Carry");

        Vector3 probeRailDestination;''')
replace('''            probeRailDestination, armBaseMoveSpeed);
        yield return RunBendingSequence();''', '''            probeRailDestination, armBaseMoveSpeed);
        yield return ReplayGate("BendDock");
        yield return RunBendingSequence();''')
replace('''        if (CurrentState == LoadingState.BendingComplete)
        {
            yield return DeliverBentPartToHClamps();''', '''        if (CurrentState == LoadingState.BendingComplete)
        {
            yield return ReplayGate("BendOutput");
            yield return DeliverBentPartToHClamps();''')
replace('''        SetState(LoadingState.ArmToHClamps,
            "折弯成品抬高，滑槽基座移向四组 H 夹头");''', '''        ReplayHTravelDestination = armBaseMover.position + railAxis * travel;
        yield return ReplayGate("HTravelReady");
        SetState(LoadingState.ArmToHClamps,
            "折弯成品抬高，滑槽基座移向四组 H 夹头");''')
replace('''            armBaseMover.position + railAxis * travel, armBaseMoveSpeed);

        float yaw;''', '''            armBaseMover.position + railAxis * travel, armBaseMoveSpeed);
        yield return ReplayGate("HDock");

        float yaw;''')
replace('''        armReturning = true;
        armReturnSequence = StartCoroutine''', '''        yield return ReplayGate("WeldInput");
        armReturning = true;
        armReturnSequence = StartCoroutine''')
replace('''        sequence = null;
    }

    private IEnumerator ReturnPickupClampHome()''', '''        yield return ReplayGate("Complete");
        sequence = null;
    }

    private IEnumerator ReturnPickupClampHome()''')
replace('''        bool rollSuctionGear = false)
    {
        while''', '''        bool rollSuctionGear = false)
    {
        if (ReplayMode && carrier == armBaseMover)
        {
            Vector3 shift = destination - carrier.position;
            carrier.position = destination;
            foreach (Transform visual in visuals)
                if (visual != null && !visual.IsChildOf(carrier)) visual.position += shift;
            yield break;
        }
        while''')

target.write_text('// Demo1 source SHA256: '+original_hash+'\n// Motion routines retained; integration edits are reproducible with .animation-tools/import_demo1.py.\n'+s,encoding='utf-8')
print('Imported Demo1 controller',original_hash)
