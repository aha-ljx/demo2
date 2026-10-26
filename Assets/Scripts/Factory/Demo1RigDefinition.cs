using UnityEngine;

// Generated from the actual Demo1 scene by Tools/Factory/Import Demo1 Reference.
public sealed class Demo1RigDefinition : ScriptableObject
{
    public GameObject rigPrefab;
    public Matrix4x4[] anchors = new Matrix4x4[3];
    public Vector3[] inputs = new Vector3[3];
    public Vector3[] outputs = new Vector3[3];
    public Vector3[] docks = new Vector3[3];
    public bool calibrated;
    public string sourceHash;
    static Demo1RigDefinition cached;
    public static Demo1RigDefinition Load()
    {
        var definition = cached != null ? cached : Resources.Load<Demo1RigDefinition>("Demo1Reference/Definition");
        if (definition == null || !definition.calibrated)
            throw new System.InvalidOperationException("Demo1 动画模板尚未导入/校准");
        cached = definition;
        return definition;
    }
    public Matrix4x4 Mapping(Transform target, int kind)
    {
        var ports = target.GetComponent<FactoryPorts>();
        if (ports != null && ports.MappingReady && ports.Kind == kind) return ports.SourceMapping;
        int[] ids = { 1472, 268, 822 };
        Transform anchor = FactoryMotion.Node(target, ids[kind]);
        return anchor.localToWorldMatrix * anchors[kind].inverse;
    }
}
