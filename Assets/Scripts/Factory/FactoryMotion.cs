using UnityEngine;

// Model IDs are the original Demo1 assembly IDs. Always resolve inside a module:
// duplicate placed machines must never animate one another's parts.
public static class FactoryMotion
{
    static readonly System.Collections.Generic.List<Transform> nodes = new System.Collections.Generic.List<Transform>();
    static readonly System.Collections.Generic.List<Renderer> renderers = new System.Collections.Generic.List<Renderer>();
    public static Transform Node(Transform root, int id)
    {
        string marker = "r" + id;
        root.GetComponentsInChildren<Transform>(true, nodes);
        Transform result = null;
        foreach (Transform t in nodes)
        {
            int i = t.name.IndexOf(marker, System.StringComparison.Ordinal);
            if (i >= 0 && (i == 0 || t.name[i - 1] == '_') &&
                (i + marker.Length == t.name.Length || t.name[i + marker.Length] == '_')) { result = t; break; }
        }
        nodes.Clear();
        return result;
    }

    public static Bounds Bounds(Transform root)
    {
        Bounds b = new Bounds(root.position, Vector3.zero);
        bool first = true;
        root.GetComponentsInChildren<Renderer>(false, renderers);
        foreach (Renderer r in renderers)
        {
            if (!r.enabled || r.GetComponentInParent<FactoryVisual>() != null) continue;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        renderers.Clear();
        return b;
    }

    public static Vector3 Top(Transform t) { Bounds b = Bounds(t); return new Vector3(b.center.x, b.max.y, b.center.z); }
}

// Exclude labels, port rings and temporary workpieces from placement bounds.
public sealed class FactoryVisual : MonoBehaviour { }
