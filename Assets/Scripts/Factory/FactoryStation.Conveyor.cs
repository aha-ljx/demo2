using UnityEngine;

public sealed partial class FactoryStation
{
    float conveyorFeedSpeed = .35f, conveyorPitch, conveyorItemLength;
    readonly Transform[] conveyorEndSupports = new Transform[2];
    Transform conveyorPlate;
    Transform ConveyorPlate => conveyorPlate != null ? conveyorPlate : (conveyorPlate = FactoryMotion.Node(transform, 491));
    Vector3 ConveyorNear => 2 * FactoryMotion.Bounds(ConveyorPlate).center - Ports.OutputPort.position;
    Vector3 ConveyorDirection => Vector3.ProjectOnPlane(Ports.OutputPort.position - ConveyorNear, Vector3.up).normalized;
    float ConveyorSpan => Vector3.ProjectOnPlane(Ports.OutputPort.position - ConveyorNear, Vector3.up).magnitude;
    float ConveyorSpacing => conveyorPitch;
    float ConveyorEndExtension => Mathf.Max(0, (ConveyorSpacing * (ConveyorOutputCapacity - 1) - ConveyorSpan) * .5f);
    Vector3 ConveyorHead => Ports.OutputPort.position + ConveyorDirection * ConveyorEndExtension;

    void ConfigureConveyor(FactoryWorkpiece part)
    {
        Bounds product = FactoryWorkpiece.GeometryBounds(part.transform);
        Vector3 axis = ConveyorDirection;
        float length = Mathf.Abs(axis.x) * product.size.x + Mathf.Abs(axis.z) * product.size.z;
        conveyorItemLength = Mathf.Max(conveyorItemLength, length);
        conveyorPitch = Mathf.Max(conveyorPitch, length + .01f);
        // The original conveyor endpoints include approach margins. Use them
        // for buffering too, with small end supports for five full-size items.
        Bounds plate = FactoryMotion.Bounds(ConveyorPlate);
        float plateLength = Mathf.Abs(axis.x) * plate.size.x + Mathf.Abs(axis.z) * plate.size.z;
        float extra = Mathf.Max(0, (conveyorPitch * (ConveyorOutputCapacity - 1) + conveyorItemLength - plateLength) * .5f) + .02f;
        Vector3 across = Vector3.Cross(Vector3.up, axis);
        float width = Mathf.Abs(across.x) * plate.size.x + Mathf.Abs(across.z) * plate.size.z;
        for (int i = 0; i < 2; i++)
        {
            if (conveyorEndSupports[i] == null)
            {
                var support = GameObject.CreatePrimitive(PrimitiveType.Cube); support.name = "ConveyorBufferEndSupport";
                support.AddComponent<FactoryVisual>(); support.transform.SetParent(transform, true);
                support.GetComponent<Renderer>().sharedMaterial = ConveyorPlate.GetComponentInChildren<Renderer>().sharedMaterial;
                conveyorEndSupports[i] = support.transform;
            }
            Vector3 center = plate.center + axis * ((i == 0 ? -1 : 1) * (plateLength * .5f + extra * .5f - .01f));
            center.y = Ports.OutputPort.position.y - .03f;
            Demo1MeshMirror.SetPose(conveyorEndSupports[i], Matrix4x4.TRS(center, Quaternion.LookRotation(axis), new Vector3(width, .05f, extra + .02f)));
        }
    }
    Vector3 QueueDelta(FactoryWorkpiece part, int index)
    {
        Bounds bounds = FactoryWorkpiece.GeometryBounds(part.transform);
        Vector3 seat = ConveyorHead - ConveyorDirection * (ConveyorSpacing * index);
        return seat - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }
    bool ConveyorQueueNeedsAdvance()
    {
        for (int i = 0; i < conveyorOutputs.Count; i++)
            if (QueueDelta(conveyorOutputs[i], i).sqrMagnitude > .000004f) return true;
        return false;
    }
    void AdvanceConveyorQueue()
    {
        if (ReservedBy != null || conveyorOutputs.Count == 0) return;
        OutputChanging = false;
        float speed = conveyorFeedSpeed * Mapping.lossyScale.x;
        Vector3 direction = ConveyorDirection;
        Vector3 head = Ports.OutputPort.position + direction * ConveyorEndExtension;
        for (int i = 0; i < conveyorOutputs.Count; i++)
        {
            Bounds bounds = FactoryWorkpiece.GeometryBounds(conveyorOutputs[i].transform);
            Vector3 delta = head - direction * (ConveyorSpacing * i) - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            if (delta.sqrMagnitude <= .000004f) continue;
            Vector3 step = Vector3.ClampMagnitude(delta, speed * FactorySimulation.Delta);
            conveyorOutputs[i].transform.position += step;
            if ((delta - step).sqrMagnitude > .000004f) OutputChanging = true;
        }
    }
}
