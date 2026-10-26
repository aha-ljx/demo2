using UnityEngine;
using UnityEngine.Rendering;

// The visible square is also the connection region used by the rail planner.
// It is a service area, not a physical obstacle or an extra rail connection.
public sealed class FactoryDockArea : MonoBehaviour
{
    const float ContactTolerance = 0.001f;
    float halfSize;
    Mesh surface;
    Material fill, border;

    public Bounds Footprint
    {
        get
        {
            Vector3 center = transform.position; center.y = 0;
            var bounds = new Bounds(center, Vector3.zero);
            foreach (Vector3 corner in Corners())
            {
                Vector3 point = transform.TransformPoint(corner); point.y = 0;
                bounds.Encapsulate(point);
            }
            return bounds;
        }
    }

    Vector3[] Corners() => new[] { new Vector3(-halfSize, 0, -halfSize), new Vector3(halfSize, 0, -halfSize),
        new Vector3(halfSize, 0, halfSize), new Vector3(-halfSize, 0, halfSize) };

    public static FactoryDockArea Create(Transform owner, string title, Vector3 position, float side)
    {
        var root = new GameObject(title, typeof(FactoryVisual));
        root.transform.SetParent(owner, false); root.transform.position = position;
        var area = root.AddComponent<FactoryDockArea>();
        area.halfSize = side * .5f / Mathf.Abs(root.transform.lossyScale.x);
        area.fill = new Material(Shader.Find("Sprites/Default")) { color = new Color(.94f, .98f, .03f, .16f) };
        area.border = new Material(Shader.Find("Sprites/Default")) { color = FactoryGuideUI.Mint };
        area.surface = new Mesh { name = "Dock square" };
        area.surface.vertices = area.Corners(); area.surface.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        area.surface.RecalculateNormals(); area.surface.RecalculateBounds();
        root.AddComponent<MeshFilter>().sharedMesh = area.surface;
        var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = area.fill;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        var line = root.AddComponent<LineRenderer>();
        line.useWorldSpace = false; line.loop = true; line.positionCount = 4;
        line.startWidth = line.endWidth = side * .025f; line.sharedMaterial = area.border;
        line.SetPositions(area.Corners());
        return area;
    }

    public bool Matches(Vector3 dock) => (new Vector2(dock.x - transform.position.x, dock.z - transform.position.z)).sqrMagnitude < .0004f;
    public bool ContainsXZ(Vector3 point)
    {
        Bounds bounds = Footprint;
        return point.x >= bounds.min.x - ContactTolerance && point.x <= bounds.max.x + ContactTolerance &&
            point.z >= bounds.min.z - ContactTolerance && point.z <= bounds.max.z + ContactTolerance;
    }

    public bool TryConnection(Bounds rail, out Vector3 contact)
    {
        Bounds area = Footprint;
        float left = Mathf.Max(rail.min.x, area.min.x), right = Mathf.Min(rail.max.x, area.max.x);
        float back = Mathf.Max(rail.min.z, area.min.z), front = Mathf.Min(rail.max.z, area.max.z);
        contact = new Vector3((left + right) * .5f, 0, (back + front) * .5f);
        return right >= left - ContactTolerance && front >= back - ContactTolerance;
    }

    void OnDestroy()
    {
        if (surface != null) Destroy(surface);
        if (fill != null) Destroy(fill);
        if (border != null) Destroy(border);
    }
}

