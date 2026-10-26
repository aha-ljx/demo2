using TMPro;
using UnityEngine;

public sealed class FactoryPorts : MonoBehaviour
{
    public Transform InputPort { get; private set; }
    public Transform OutputPort { get; private set; }
    public Transform DockPort { get; private set; }
    public Transform ConveyorDockPort { get; private set; }
    public FactoryDockArea DockArea => DockPort.GetComponent<FactoryDockArea>();
    public FactoryDockArea ConveyorDockArea => ConveyorDockPort != null ? ConveyorDockPort.GetComponent<FactoryDockArea>() : null;
    public FactoryDockArea PickupDockArea { get; private set; }
    public Vector3 BeltPickupDockPosition => transform.TransformPoint(beltPickupDockLocal);
    Vector3 beltPickupDockLocal;
    public Vector3 FinishedPickupDock => BeltPickupDockPosition;
    Vector3 conveyorDockLocal;
    public Vector3 ConveyorDockPosition => transform.TransformPoint(conveyorDockLocal);
    Matrix4x4 sourceMappingLocal;
    public bool MappingReady { get; private set; }
    public Matrix4x4 SourceMapping => transform.localToWorldMatrix * sourceMappingLocal;
    public Vector3 DockPosition => Demo1RigDefinition.Load().Mapping(transform, Kind).MultiplyPoint3x4(Demo1RigDefinition.Load().docks[Kind]);
    public Vector3 WorkPoint => transform.TransformPoint(workLocal);
    public int Kind { get; private set; }
    Vector3 workLocal;
    public float PartWidth { get; private set; }
    public Vector3 InternalOutput => transform.TransformPoint(internalOutputLocal);
    Vector3 internalOutputLocal;

    public void Initialize(int kind, TMP_FontAsset font)
    {
        font = FactoryFonts.Ensure(font);
        Kind = kind;
        PartWidth = Mathf.Max(0.15f, transform.lossyScale.x * 0.8f);
        var definition = Demo1RigDefinition.Load();
        Matrix4x4 mapping = definition.Mapping(transform, kind);
        sourceMappingLocal = transform.worldToLocalMatrix * mapping;
        MappingReady = true;
        Vector3 input = mapping.MultiplyPoint3x4(definition.inputs[kind]);
        Vector3 output = mapping.MultiplyPoint3x4(definition.outputs[kind]);
        workLocal = transform.InverseTransformPoint(input);
        internalOutputLocal = transform.InverseTransformPoint(output);
        InputPort = Marker("输入", input, FactoryGuideUI.Ink, font);
        OutputPort = Marker(kind == 2 ? "成品输出" : "输出", output, FactoryGuideUI.Ink, font);
        Vector3 dock = DockPosition; dock.y = 0.1f;
        // Punch and bending stations accept separate rail approaches on either side.
        // Keep its calibrated working pose; enlarge both the visible and routable area.
        DockPort = Marker("机械臂停靠", dock, FactoryGuideUI.Mint, font, true,
            kind <= 1 ? 2f : 1f);
        if (kind == 2)
        {
            // The original output is the far end of the belt. Reflect it about
            // the real plate center to recover its near placement endpoint.
            Transform plate = FactoryMotion.Node(transform, 491);
            Vector3 near = 2 * mapping.inverse.MultiplyPoint3x4(FactoryMotion.Bounds(plate).center) - definition.outputs[2];
            Vector3 shift = Vector3.ProjectOnPlane(near - definition.inputs[2], Vector3.up);
            Vector3 conveyorDock = mapping.MultiplyPoint3x4(definition.docks[2] + shift);
            conveyorDockLocal = transform.InverseTransformPoint(conveyorDock);
            Vector3 marker = conveyorDock; marker.y = 0.1f;
            ConveyorDockPort = Marker("传送带放件停靠", marker, FactoryGuideUI.Mint, font, true);
            Vector3 pickupShift = Vector3.ProjectOnPlane(definition.outputs[2] - definition.inputs[2], Vector3.up);
            Vector3 pickupDock = mapping.MultiplyPoint3x4(definition.docks[2] + pickupShift);
            beltPickupDockLocal = transform.InverseTransformPoint(pickupDock);
            marker = BeltPickupDockPosition; marker.y = 0.1f;
            PickupDockArea = Marker("成品取件停靠", marker, FactoryGuideUI.Mint, font, true).GetComponent<FactoryDockArea>();
        }
    }

    Transform Marker(string title, Vector3 position, Color color, TMP_FontAsset font, bool docking = false,
        float dockSizeMultiplier = 1f)
    {
        Transform root;
        if (docking) root = FactoryDockArea.Create(transform, title + "区域", position, PartWidth * 1.4f * dockSizeMultiplier).transform;
        else
        {
            root = new GameObject(title + "位置", typeof(FactoryVisual)).transform;
            root.SetParent(transform, true); root.position = position;
            var line = root.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.loop = true; line.positionCount = 32;
            line.startWidth = line.endWidth = PartWidth * 0.035f;
            var mat = new Material(Shader.Find("Sprites/Default")); mat.color = color;
            line.sharedMaterial = mat;
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI * 2 / 32;
                line.SetPosition(i, root.InverseTransformVector(new Vector3(Mathf.Cos(a), 0.02f, Mathf.Sin(a)) * PartWidth * 0.7f));
            }
        }
        var label = new GameObject(title, typeof(TextMeshPro), typeof(FactoryPortLabel)).GetComponent<TextMeshPro>();
        label.transform.SetParent(root, false);
        label.transform.position = position + Vector3.up * PartWidth * 0.8f;
        label.font = font; label.text = title; label.fontSize = 4;
        label.color = FactoryGuideUI.Ink; label.gameObject.SetActive(!docking); label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(4, 1);
        label.transform.localScale = Vector3.one * PartWidth * 0.55f / transform.lossyScale.x;
        return root;
    }

    void OnDestroy()
    {
        foreach (LineRenderer line in GetComponentsInChildren<LineRenderer>())
            if (line.GetComponent<FactoryVisual>() != null && line.GetComponent<FactoryDockArea>() == null) Destroy(line.sharedMaterial);
    }
}

public sealed class FactoryPortLabel : MonoBehaviour
{
    void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera != null) transform.rotation = camera.transform.rotation;
    }
}

