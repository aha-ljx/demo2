using System.Collections;
using UnityEngine;

public sealed partial class FactoryArm
{
    FinishedGoodsArea storageDestination;
    int storageSlot = -1;
    float finishedRetryAfter;
    bool FinishedRetryReady => FactorySimulation.Now >= finishedRetryAfter;
    public int FinishedDeliveries { get; private set; }

    public void AssignFinished(FactoryStation from, FinishedGoodsArea area, int slot)
    {
        source = from; destination = null; storageDestination = area; storageSlot = slot;
        Busy = true; from.ReservedBy = this;
        FactorySimulation.Start(this, TransferFinished());
    }

    float FinishedSupportHeight(Vector3 point)
    {
        float height = 0;
        foreach (var rail in FactoryProduction.Instance.Rails)
            if (FactoryProduction.Instance.Graph.Same(Rail, rail) &&
                ContainsXZ(rail.GetComponent<EquipmentGrid>().Footprint, point))
                height = Mathf.Max(height, rail.SurfaceY);
        return height;
    }

    void GroundFinishedBase()
    {
        Vector3 position = TrackPosition;
        position.y = FinishedSupportHeight(position) + finishedBaseHeight;
        transform.position = position + baseOffset;
        // The grid plate is a separate visual, excluded from the replay mirror.
        // Its underside follows the support rather than carrying an airborne slab.
        if (carrierPlate != null)
        {
            var bounds = carrierPlate.GetComponent<Renderer>().bounds;
            carrierPlate.position += Vector3.up * (FinishedSupportHeight(position) - bounds.min.y);
        }
    }

    IEnumerator TransferFinished()
    {
        bool delivered = false;
        FactoryStation belt = source;
        FinishedGoodsArea area = storageDestination;
        int slot = storageSlot;
        FactoryWorkpiece part = belt.Output;
        string failureReason = null;
        try
        {
            Vector3 pickupDock = belt.Ports.FinishedPickupDock;
            var definition = Demo1RigDefinition.Load();
            Matrix4x4 pickupSpace = Matrix4x4.TRS(pickupDock, belt.Mapping.rotation, Vector3.one * scale)
                * Matrix4x4.Translate(-definition.docks[2]);
            part.SetGroundCollision(false);
            Matrix4x4 originalPickupSpace = pickupSpace;
            replay = Demo1ReplaySession.CreateFinishedTransport(part, pickupSpace);
            // The replay dock's height belongs to the original machine, not
            // the floor beneath this carrier. Preserve the fixed arm geometry
            // while deriving its world base height from actual support.
            var foot = replay.Source.ReplayNode(872).GetComponentInChildren<Renderer>();
            finishedBaseHeight = (replay.Source.ReplayBasePosition.y - foot.bounds.min.y) * scale;
            groundFinishedBase = true;
            // Capture the product in the same grounded pickup frame used by
            // the visible arm, so changing base height cannot shift the part.
            pickupDock.y = FinishedSupportHeight(pickupDock) + finishedBaseHeight;
            pickupSpace = Matrix4x4.TRS(pickupDock, belt.Mapping.rotation, Vector3.one * scale)
                * Matrix4x4.Translate(-definition.docks[2]);
            replay.Space = () => pickupSpace;
            // Re-express the captured source sections without moving the real
            // belt product when the grounded frame differs from the old dock.
            replay.Source.OffsetFinishedTransport(pickupSpace.inverse * originalPickupSpace);
            // Establish the grounded rest pose before asynchronous reach checks.
            // Keep the visible arm still while the hidden rig tries candidates.
            GroundFinishedBase();
            new Demo1MeshMirror(transform, replay, () => ArmSpace).Apply();
            yield return WaitForFinishedCheckpoint("FinishedPickup");
            if (replay.Failed) yield break;
            Vector3 calibratedDock = pickupDock;
            replay.enabled = false;
            Bounds productBounds = FactoryWorkpiece.GeometryBounds(part.transform);
            Vector3 towardPart = Vector3.ProjectOnPlane(productBounds.center - pickupDock, Vector3.up).normalized;
            bool pickupFits = false;
            // Edge-connected rails can leave the calibrated dock on the lower
            // floor. Find a grounded pose inside the same service area that
            // can complete approach, suction contact and lift with fixed links.
            for (int attempt = 0; attempt <= 8; attempt++)
            {
                Vector3 candidate = calibratedDock + towardPart * (attempt * scale * .1f);
                if (!belt.Ports.PickupDockArea.ContainsXZ(candidate) ||
                    !CanReach(candidate, belt.Ports.PickupDockArea, out _)) continue;
                candidate.y = FinishedSupportHeight(candidate) + finishedBaseHeight;
                Matrix4x4 nextSpace = Matrix4x4.TRS(candidate, belt.Mapping.rotation, Vector3.one * scale)
                    * Matrix4x4.Translate(-definition.docks[2]);
                replay.Source.OffsetFinishedTransport(nextSpace.inverse * pickupSpace);
                pickupSpace = nextSpace;
                yield return replay.Source.CheckFinishedPickup();
                if (!replay.Source.ReplayFinishedPickupFits) continue;
                pickupDock = candidate; pickupFits = true; break;
            }
            if (!pickupFits) { failureReason = "当前停靠区没有可完成吸取和抬升的姿态，请调整滑槽接入位置"; yield break; }
            replay.enabled = true; replay.ApplyWorkpiece();
            GroundFinishedBase();
            Mirror = new Demo1MeshMirror(transform, replay, () => ArmSpace);
            Mirror.Apply();
            Status = "前往传送带末端取最终产物";
            yield return TravelTo(pickupDock, belt.Mapping.rotation, belt.Ports.PickupDockArea);
            yield return WaitForFinishedCheckpoint("FinishedPickup");
            if (replay.Failed) yield break;
            replay.Source.ContinueReplay();
            yield return WaitForFinishedCheckpoint("FinishedLifted");
            if (replay.Failed) yield break;
            if (belt.TakeOutput() != part) throw new System.InvalidOperationException("传送带成品交接被改变");
            belt.ReservedBy = null;
            held = part; part.transform.SetParent(transform, true); replay.Holder = this;
            replay.Space = () => ArmSpace;
            Status = "沿玩家滑槽运送最终产物到存放区";
            yield return TravelTo(area.DockPosition(pickupDock.y), Quaternion.identity);
            // Stop on the contacting rail and release suction without entering the area.
            replay.ApplyWorkpiece(); Mirror?.Apply(); Mirror = null;
            replay.Holder = null; replay.Freeze();
            held = null;
            Status = "到达产物区边缘，松开吸盘让成品落入区域";
            yield return area.Drop(slot, part);
            if (part.transform.parent != area.Products)
                throw new System.InvalidOperationException("最终产物未落入收货区");
            delivered = true; held = null; FinishedDeliveries++;
            Status = "最终产物已入库，返回原放置位置";
            yield return TravelTo(homePosition, Quaternion.identity);
            // End at the exact placed position, rather than the storage dock
            // or the nearest rail endpoint. Stay busy throughout the return.
            Rail = homeRail;
            groundFinishedBase = false;
            transform.position = homePosition + baseOffset;
            facing = Quaternion.identity;
            foreach (var pose in placedPose)
                if (pose.Key != null)
                {
                    pose.Key.localPosition = pose.Value.position;
                    pose.Key.localRotation = pose.Value.rotation;
                    pose.Key.localScale = pose.Value.scale;
                }
            Status = "已返回原放置位置，等待下一件成品";
        }
        finally
        {
            if (belt != null && belt.ReservedBy == this) belt.ReservedBy = null;
            if (!delivered && area != null) area.Release(slot);
            storageDestination = null; storageSlot = -1;
            Busy = false; source = null;
            if (!delivered)
            {
                string error = failureReason ?? (replay != null ? replay.Error : "缺少成品");
                // A failed nested replay must release the task instead of leaving
                // this arm and the conveyor permanently reserved by one another.
                Mirror = null;
                if (replay != null && replay.Source != null) { replay.Holder = null; replay.Freeze(); }
                if (part != null && belt != null)
                {
                    held = null;
                    if (belt.Output == part) { part.transform.SetParent(belt.transform, true); part.SetGroundCollision(true); }
                    else if (part.transform.parent != area.Products) belt.ReturnOutput(part);
                }
                finishedRetryAfter = FactorySimulation.Now + 2;
                Status = "成品取件未完成，保留产物后重试：" + error;
            }
        }
    }

    IEnumerator WaitForFinishedCheckpoint(string checkpoint)
    {
        // Unity stops a throwing child coroutine without unwinding the parent.
        // Let this transaction observe failure and exit through its own finally.
        while (!replay.Failed && replay.Source.ReplayCheckpoint != checkpoint) yield return null;
    }
}
