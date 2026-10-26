using UnityEngine;

public sealed class FactoryWorkpiece : MonoBehaviour
{
    static readonly System.Collections.Generic.List<Renderer> geometryRenderers = new System.Collections.Generic.List<Renderer>();
    public int Stage { get; private set; }
    public Demo1ReplaySession Replay { get; set; }
    public float Width { get; private set; }
    Transform visual;
    Material metal;

    public static FactoryWorkpiece Create(float width, Vector3 position)
    {
        var root = new GameObject("原材料");
        root.AddComponent<FactoryVisual>();
        var part = root.AddComponent<FactoryWorkpiece>();
        part.Width = width; part.ChangeModel("加工零件");
        part.transform.position = position;
        return part;
    }

    public void ChangeModel(string resource)
    {
        if (visual != null) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); }
        var prefab = Resources.Load<GameObject>("FactoryParts/" + resource);
        if (prefab == null) throw new System.InvalidOperationException("缺少工件模型：" + resource);
        visual = Instantiate(prefab, transform, false).transform;
        FlattenAndSize();
        Paint();
    }

    void FlattenAndSize()
    {
        Bounds b = GeometryBounds(visual);
        // These FBX files use different up axes. Find the thin axis from geometry.
        Vector3 axis = b.size.x < b.size.y && b.size.x < b.size.z ? Vector3.right :
            b.size.z < b.size.y ? Vector3.forward : Vector3.up;
        visual.rotation = Quaternion.FromToRotation(axis, Vector3.up);
        b = GeometryBounds(visual);
        visual.localScale *= Width / Mathf.Max(b.size.x, b.size.z, 0.0001f);
        b = GeometryBounds(visual);
        visual.position += transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
    }

    // Workpieces carry FactoryVisual, so use raw renderer bounds here.
    public static Bounds GeometryBounds(Transform root)
    {
        root.GetComponentsInChildren<Renderer>(false, geometryRenderers);
        Bounds b = new Bounds(root.position, Vector3.zero);
        bool first = true;
        foreach (var r in geometryRenderers) { if (!r.enabled) continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        geometryRenderers.Clear();
        return b;
    }

    void Paint()
    {
        if (metal == null)
        {
            metal = new Material(Shader.Find("Standard"));
            metal.color = new Color(59f / 255, 66f / 255, 74f / 255);
            metal.SetFloat("_Metallic", 0.15f); metal.SetFloat("_Glossiness", 0.35f);
        }
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>()) PaintRenderer(renderer);
    }
    public void PaintRenderer(Renderer renderer)
    {
        if (metal == null) { metal = new Material(Shader.Find("Standard")); metal.color = new Color(59f/255, 66f/255, 74f/255); metal.SetFloat("_Metallic", 0.15f); metal.SetFloat("_Glossiness", 0.35f); }
        int count = Mathf.Max(1, renderer.sharedMaterials.Length); var materials = new Material[count];
        for (int i = 0; i < count; i++) materials[i] = metal;
        renderer.sharedMaterials = materials;
    }
    public void HideLegacyVisual() { if (visual != null) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); visual = null; } }
    public void SetStage(int stage) { Stage = stage; name = new[] { "原材料", "冲压件", "折弯件", "焊接成品" }[stage]; }
    public void SetGroundCollision(bool enabled)
    {
        foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = enabled;
        if (!enabled) return;
        Bounds world = GeometryBounds(transform);
        Bounds local = new Bounds(transform.InverseTransformPoint(world.center), Vector3.zero);
        for (int i = 0; i < 8; i++) local.Encapsulate(transform.InverseTransformPoint(world.center + Vector3.Scale(world.extents,
            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        var solid = GetComponent<BoxCollider>(); if (solid == null) solid = gameObject.AddComponent<BoxCollider>();
        solid.center = local.center; solid.size = local.size; solid.enabled = true;
    }
    void OnDestroy() { if (metal != null) Destroy(metal); }
}
