using System.Collections.Generic;
using UnityEngine;

public sealed class FactoryRail : MonoBehaviour
{
    public Vector3 A { get; private set; }
    public Vector3 B { get; private set; }
    public float HalfWidth { get; private set; }
    public float SurfaceY { get; private set; }
    public void Measure()
    {
        Bounds b = GetComponent<EquipmentGrid>().Footprint;
        Bounds visual = EquipmentGrid.VisualBounds(gameObject);
        Vector3 axis = b.size.x > b.size.z ? Vector3.right : Vector3.forward;
        float length = Mathf.Max(b.size.x, b.size.z);
        HalfWidth = Mathf.Min(b.size.x, b.size.z) * 0.5f;
        SurfaceY = visual.max.y;
        A = b.center - axis * Mathf.Max(0, length * 0.5f - HalfWidth);
        B = b.center + axis * Mathf.Max(0, length * 0.5f - HalfWidth);
        A = new Vector3(A.x, 0, A.z); B = new Vector3(B.x, 0, B.z);
    }
    public Vector3 Closest(Vector3 point)
    {
        point.y = 0; Vector3 delta = B - A;
        return A + delta * Mathf.Clamp01(Vector3.Dot(point - A, delta) / Mathf.Max(delta.sqrMagnitude, 0.0001f));
    }
    void OnDestroy() { if (FactoryProduction.Instance != null) FactoryProduction.Instance.ForgetRail(this); }
}

// A graph of center lines and junction projections. Every edge belongs to an
// actual rail, including the short link across a right-angle junction.
public sealed class FactoryRailGraph
{
    public readonly List<Vector3> Points = new List<Vector3>();
    readonly List<List<int>> edges = new List<List<int>>();
    readonly Dictionary<FactoryRail, List<int>> railNodes = new Dictionary<FactoryRail, List<int>>();
    readonly Dictionary<FactoryRail, List<int>> spineNodes = new Dictionary<FactoryRail, List<int>>();
    readonly Dictionary<int, FactoryRail> owners = new Dictionary<int, FactoryRail>();
    public readonly Dictionary<FactoryRail, int> Components = new Dictionary<FactoryRail, int>();

    int Add(FactoryRail rail, Vector3 p, bool spine = true)
    {
        foreach (int id in railNodes[rail]) if ((Points[id] - p).sqrMagnitude < 0.0001f)
        {
            if (spine && !spineNodes[rail].Contains(id)) spineNodes[rail].Add(id);
            return id;
        }
        int index = Points.Count; Points.Add(p); edges.Add(new List<int>());
        railNodes[rail].Add(index); if (spine) spineNodes[rail].Add(index);
        owners[index] = rail; return index;
    }
    void Link(int a, int b) { if (a == b) return; if (!edges[a].Contains(b)) edges[a].Add(b); if (!edges[b].Contains(a)) edges[b].Add(a); }

    public void Build(List<FactoryRail> rails)
    {
        Points.Clear(); edges.Clear(); railNodes.Clear(); spineNodes.Clear(); Components.Clear(); owners.Clear();
        foreach (var r in rails)
        {
            if (r == null) continue;
            r.Measure(); railNodes[r] = new List<int>(); spineNodes[r] = new List<int>(); Add(r, r.A); Add(r, r.B);
        }
        for (int i = 0; i < rails.Count; i++)
        for (int j = i + 1; j < rails.Count; j++)
        {
            var a = rails[i]; var b = rails[j]; if (a == null || b == null) continue;
            Join(a, b);
        }
        foreach (var pair in spineNodes)
        {
            var ids = pair.Value; Vector3 axis = pair.Key.B - pair.Key.A;
            ids.Sort((a, b) => Vector3.Dot(Points[a], axis).CompareTo(Vector3.Dot(Points[b], axis)));
            for (int i = 1; i < ids.Count; i++) Link(ids[i - 1], ids[i]);
        }
        int component = 0; var visited = new HashSet<int>();
        for (int i = 0; i < Points.Count; i++)
        {
            if (!visited.Add(i)) continue;
            var queue = new Queue<int>(); queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int n = queue.Dequeue(); Components[owners[n]] = component;
                foreach (int next in edges[n]) if (visited.Add(next)) queue.Enqueue(next);
            }
            component++;
        }
    }
    void Join(FactoryRail a, FactoryRail b)
    {
        Bounds x = a.GetComponent<EquipmentGrid>().Footprint, y = b.GetComponent<EquipmentGrid>().Footprint;
        float left = Mathf.Max(x.min.x, y.min.x), right = Mathf.Min(x.max.x, y.max.x);
        float back = Mathf.Max(x.min.z, y.min.z), front = Mathf.Min(x.max.z, y.max.z);
        float overlapX = right - left, overlapZ = front - back;
        // Join through the shared edge, including offset L/T junctions. Testing
        // endpoint distance against track widths misses valid partial edges.
        // A point-only corner or a missing tile still cannot connect tracks.
        if (overlapX < -0.03f || overlapZ < -0.03f || (overlapX <= 0.03f && overlapZ <= 0.03f)) return;
        Vector3 portal = new Vector3((left + right) * 0.5f, 0, (back + front) * 0.5f);
        int aSpine = Add(a, a.Closest(portal)), bSpine = Add(b, b.Closest(portal));
        int aEdge = Add(a, portal, false), bEdge = Add(b, portal, false);
        Link(aSpine, aEdge); Link(aEdge, bEdge); Link(bEdge, bSpine);
    }
    public bool Same(FactoryRail a, FactoryRail b) => a != null && b != null &&
        Components.TryGetValue(a, out int x) && Components.TryGetValue(b, out int y) && x == y;

    public IEnumerable<Vector3[]> Segments()
    {
        for (int i = 0; i < edges.Count; i++)
        foreach (int j in edges[i])
        {
            if (j <= i) continue;
            Vector3 a = Points[i], b = Points[j];
            a.y = owners[i].SurfaceY + 0.025f; b.y = owners[j].SurfaceY + 0.025f;
            yield return new[] { a, b };
        }
    }

    public bool Route(FactoryRail rail, Vector3 start, Vector3 port, float reach, out List<Vector3> path)
    {
        path = null;
        if (rail == null || !railNodes.ContainsKey(rail)) return false;
        // Dijkstra starts at the projected current position, not an endpoint.
        Vector3 projected = rail.Closest(start);
        var costs = new float[Points.Count]; var previous = new int[Points.Count]; var done = new bool[Points.Count];
        for (int i = 0; i < costs.Length; i++) { costs[i] = float.PositiveInfinity; previous[i] = -1; }
        foreach (int n in spineNodes[rail]) costs[n] = Vector3.Distance(projected, Points[n]);
        int best = -1; float bestCost = float.PositiveInfinity; Vector3 destination = Vector3.zero;
        // Include projections onto every rail, so ports midway along a long rail work.
        var targets = new Dictionary<FactoryRail, Vector3>();
        foreach (var r in railNodes.Keys)
        {
            if (!Same(rail, r)) continue;
            Vector3 p = r.Closest(port); Vector3 flatPort = new Vector3(port.x, 0, port.z);
            if (Vector3.Distance(p, flatPort) <= reach) targets[r] = p;
        }
        if (targets.ContainsKey(rail)) { bestCost = Vector3.Distance(projected, targets[rail]); destination = targets[rail]; best = -2; }
        for (int count = 0; count < Points.Count; count++)
        {
            int n = -1;
            for (int i = 0; i < Points.Count; i++) if (!done[i] && (n < 0 || costs[i] < costs[n])) n = i;
            if (n < 0 || float.IsInfinity(costs[n])) break;
            done[n] = true;
            if (spineNodes[owners[n]].Contains(n) && targets.TryGetValue(owners[n], out Vector3 p))
            {
                float score = costs[n] + Vector3.Distance(Points[n], p);
                if (score < bestCost) { bestCost = score; best = n; destination = p; }
            }
            foreach (int next in edges[n])
            {
                float cost = costs[n] + Vector3.Distance(Points[n], Points[next]);
                if (cost < costs[next]) { costs[next] = cost; previous[next] = n; }
            }
        }
        if (best == -1) return false;
        path = new List<Vector3>();
        if (best >= 0) { for (int n = best; n >= 0; n = previous[n]) path.Add(Points[n]); path.Reverse(); }
        path.Insert(0, projected); path.Add(destination); return true;
    }
    public FactoryRail RailAt(Vector3 point, FactoryRail connectedTo)
    {
        FactoryRail best = connectedTo; float distance = float.PositiveInfinity;
        foreach (var r in railNodes.Keys)
        {
            if (!Same(connectedTo, r)) continue;
            float d = (r.Closest(point) - new Vector3(point.x, 0, point.z)).sqrMagnitude;
            if (d < distance) { distance = d; best = r; }
        }
        return best;
    }
}
