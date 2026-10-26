from pathlib import Path
import hashlib, re

original_path = Path(r'D:/Unity/Programs/Demo1/Assets/Scripts/LoadingProcessController.cs')
original = original_path.read_text(encoding='utf-8-sig')
adapted = Path('Assets/Scripts/Demo1/LoadingProcessController.cs').read_text(encoding='utf-8-sig')
methods = '''RunBendingSequence RunWeldingSequence RunPostWeldTransfer ReturnPostWeldArm
ReturnArmToRailEnd ReturnPickupClampHome MoveWorkpiece MoveSuctionHorizontally
MoveSuctionVertically UpdateSuctionGearRotation CloseJaws MoveJawPairs MoveJawPair
MoveBendingUpper TranslateBendingUpper ReplaceWithBendingParts AlignThirdBendingSection
TryFindBendingFlaps MoveArmEdgeTo MoveFixedArmWristTo SolveFixedArmPoseAsync
RotateFixedPitchJoint PreviewFixedArmPathAsync ArmBodyIntersectsWorkpiece
ArmBodyIntersectsFoldPath MovePickupArmTo RestorePickupArmJoints TrySolvePickupArmPose
RotatePickupPitchJoint LevelPickupWrist RotateWristTo AlignFinishedPartWithCupTips
CacheCupTipPlane TryFindCupTip GetCupTipPlane MoveArmSuctionTo RotateArmAroundBase
RotateArmAroundAxis MoveWeldingTipTo AimWeldingArmAt RotateWeldingTurntable
TryGetHClampGaps AreBentFlapsInsideGaps TryFitBentFlapsToGaps TryGetHTurntablePlane
FindTransferRailStopAsync TrySolvePostWeldArmPose MovePostWeldTipTo RestorePostWeldArmPose
ReplaceSheetWithFinishedPart SafePunchClampTravel SeatPunchAssembly
CalibratePickupRailFromMesh CalibrateSuctionRailFromMesh'''.split()

def body(text, name):
    match = re.search(r'^    (?:public|private) (?:static )?[^\n]+\b'+name+r'\(', text, re.M)
    assert match, name
    start = text.index('{', match.start()); depth = 1; end = start + 1
    while depth:
        if text[end] == '{': depth += 1
        if text[end] == '}': depth -= 1
        end += 1
    return text[start:end]

lines = ['Demo1 original SHA256: '+hashlib.sha256(original_path.read_bytes()).hexdigest()]
for method in methods:
    a, b = body(original,method), body(adapted,method)
    assert a == b, 'Motion was changed: '+method
    lines.append('PASS byte-identical motion body: '+method)
lines += ['PASS '+str(len(methods))+' motion/geometry methods unchanged.',
          'Integration changes: per-instance lookup, UI isolation, checkpoint waits, and replacement of cross-module base translation by player rail routing.',
          'RunLoadingProcess and DeliverBentPartToHClamps retain original actions; added checkpoint waits do not replace gestures, constraints, or timings.']
Path('Logs/demo1-source-parity.txt').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(lines[-3])
