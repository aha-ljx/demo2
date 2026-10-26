using UnityEngine;

// 每台设备的底板占用整数个 1×1 单元；机器本体只做等比缩放。
public class EquipmentGrid : MonoBehaviour
{
    public Vector2Int Cells { get; private set; }
    private Transform plate;

    public void Initialize(float scale)
    {
        transform.localScale *= scale;
        Bounds bounds = VisualBounds(gameObject);
        Renderer sourcePlate = null;
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            if (renderer.name.Contains("地板") && renderer.enabled)
            {
                if (sourcePlate == null || renderer.bounds.size.x * renderer.bounds.size.z >
                    sourcePlate.bounds.size.x * sourcePlate.bounds.size.z)
                    sourcePlate = renderer;
            }
        }

        Cells = new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(bounds.size.x - 0.001f)),
            Mathf.Max(1, Mathf.CeilToInt(bounds.size.z - 0.001f)));
        var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseObject.name = "GridBasePlate";
        plate = baseObject.transform;
        plate.SetParent(transform, true);
        // Rails may legally overlap a station's decorative floor. Keep their
        // top faces at distinct heights to avoid coplanar depth flickering.
        // Both plates still start at the same ground level, preserving Snap
        // positions, saved layouts and the height of the original hardware.
        float plateThickness = sourcePlate != null ? 0.02f : 0.05f;
        plate.position = new Vector3(bounds.center.x, bounds.min.y + plateThickness * 0.5f, bounds.center.z);
        plate.localScale = new Vector3(Cells.x / transform.lossyScale.x,
            plateThickness / transform.lossyScale.y, Cells.y / transform.lossyScale.z);
        if (sourcePlate != null)
        {
            baseObject.GetComponent<Renderer>().sharedMaterial = sourcePlate.sharedMaterial;
            foreach (var renderer in GetComponentsInChildren<Renderer>())
                if (renderer != baseObject.GetComponent<Renderer>() && renderer.name.Contains("地板"))
                {
                    renderer.enabled = false;
                    foreach (var collider in renderer.GetComponents<Collider>())
                        collider.enabled = false;
                }
        }
    }

    public Bounds Footprint => plate.GetComponent<Renderer>().bounds;

    public static Bounds VisualBounds(GameObject obj)
    {
        Bounds bounds = new Bounds(obj.transform.position, Vector3.zero);
        bool found = false;
        foreach (var renderer in obj.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer.GetComponentInParent<FactoryVisual>() != null) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    public void Snap(Vector3 target)
    {
        Bounds footprint = Footprint;
        float width = Mathf.Round(footprint.size.x);
        float depth = Mathf.Round(footprint.size.z);
        Vector3 center = new Vector3(
            Mathf.Round(target.x - width * 0.5f) + width * 0.5f,
            0f,
            Mathf.Round(target.z - depth * 0.5f) + depth * 0.5f);
        transform.position += new Vector3(center.x - footprint.center.x,
            -footprint.min.y, center.z - footprint.center.z);
    }
}
