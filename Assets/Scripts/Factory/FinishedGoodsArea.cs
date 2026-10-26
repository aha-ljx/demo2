using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// An open receiving area. Products are released at the rail edge and settle inside.
public sealed class FinishedGoodsArea : MonoBehaviour
{
    public const int Capacity = 20;
    public const int PricePerProduct = 2000;
    public int StoredCount => stored.Count;
    public long Revenue => (long)StoredCount * PricePerProduct;
    public bool HasSpace => stored.Count + reserved.Count < Capacity;
    public Bounds Footprint { get; private set; }
    public Transform Products { get; private set; }
    public FactoryDockArea DockArea { get; private set; }
    readonly Dictionary<int, FactoryWorkpiece> stored = new Dictionary<int, FactoryWorkpiece>();
    readonly HashSet<int> reserved = new HashSet<int>();
    readonly HashSet<FactoryWorkpiece> received = new HashSet<FactoryWorkpiece>();
    TextMeshPro label;
    public static float SideLength(float scale) => scale * .8f * 4.4f;

    public void Initialize(Vector3 center, float scale)
    {
        center.x = Mathf.Floor(center.x) + .5f;
        center.z = Mathf.Floor(center.z) + .5f;
        center.y = 0; transform.position = center;
        float edge = SideLength(scale) * .5f;
        Footprint = new Bounds(center, new Vector3(edge * 2, .1f, edge * 2));
        Products = new GameObject("已入库成品", typeof(FactoryVisual)).transform;
        Products.SetParent(transform, false);
        DockArea = FactoryDockArea.Create(transform, "最终产物区收货边界", center + Vector3.up * .08f, edge * 2);
        var boundary = DockArea.GetComponent<LineRenderer>(); boundary.startWidth = boundary.endWidth = .08f;
        label = new GameObject("最终产物区提示", typeof(FactoryVisual), typeof(FactoryPortLabel)).AddComponent<TextMeshPro>();
        label.transform.SetParent(transform, true); label.transform.position = center + Vector3.up * (scale + 1.4f);
        label.font = FactoryFonts.Ensure(TMP_Settings.defaultFontAsset); label.fontSize = 3;
        label.alignment = TextAlignmentOptions.Center; label.rectTransform.sizeDelta = new Vector2(11, 3);
        Refresh();
    }

    // Used to identify this destination; the route ends on its contacting rail.
    public Vector3 DockPosition(float baseHeight) => new Vector3(transform.position.x, baseHeight, transform.position.z);
    public void AppendApproach(Vector3 contact, List<Vector3> path) { path.Add(contact); }
    public bool IsNear(Vector3 player, float distance)
    {
        player.y = 0;
        return Vector3.Distance(player, Footprint.ClosestPoint(player)) <= distance;
    }
    public int Reserve()
    {
        for (int i = 0; i < Capacity; i++)
            if (!stored.ContainsKey(i) && !reserved.Contains(i) && (i < 9 || stored.ContainsKey(i - 9)))
            { reserved.Add(i); return i; }
        return -1;
    }
    public void Release(int slot) { reserved.Remove(slot); }
    public Vector3 Target(int slot)
    {
        int cell = slot % 9;
        float spacing = Footprint.size.x / 3;
        Vector3 point = transform.position + new Vector3(cell % 3 - 1, 0, cell / 3 - 1) * spacing;
        point.y = slot < 9 ? .02f : FactoryWorkpiece.GeometryBounds(stored[slot - 9].transform).max.y + .015f;
        return point;
    }
    public bool BeginManualDrop(FactoryWorkpiece part)
    {
        if (FactoryLevelSession.HasEnded || part == null || part.Stage != 3 || received.Contains(part)) return false;
        int slot = Reserve();
        if (slot < 0) return false;
        FactorySimulation.Start(this, Drop(slot, part)); return true;
    }
    public IEnumerator Drop(int slot, FactoryWorkpiece part)
    {
        if (part == null || part.Stage != 3 || !reserved.Contains(slot) || !received.Add(part))
        { Release(slot); yield break; }
        bool complete = false;
        try
        {
            part.transform.SetParent(Products, true);
            part.SetGroundCollision(false);
            Vector3 target = Target(slot);
            Bounds geometry = FactoryWorkpiece.GeometryBounds(part.transform);
            Vector3 start = new Vector3(geometry.center.x, geometry.min.y, geometry.center.z);
            float height = Mathf.Max(.1f, start.y - target.y);
            float duration = Mathf.Max(.35f, Mathf.Sqrt(2 * height / 9.81f));
            for (float elapsed = 0; elapsed < duration; elapsed += FactorySimulation.Delta)
            {
                if (part == null) yield break;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 next = Vector3.Lerp(start, target, t);
                next.y = Mathf.Lerp(start.y, target.y, t * t);
                geometry = FactoryWorkpiece.GeometryBounds(part.transform);
                part.transform.position += next - new Vector3(geometry.center.x, geometry.min.y, geometry.center.z);
                yield return null;
            }
            geometry = FactoryWorkpiece.GeometryBounds(part.transform);
            part.transform.position += target - new Vector3(geometry.center.x, geometry.min.y, geometry.center.z);
            if (FactoryLevelSession.HasEnded) yield break;
            Commit(slot, part); complete = true;
        }
        finally
        {
            if (!complete) { Release(slot); received.Remove(part); }
        }
    }
    public void Commit(int slot, FactoryWorkpiece part)
    {
        if (part == null || part.Stage != 3 || stored.ContainsValue(part) || !reserved.Remove(slot))
            throw new System.InvalidOperationException("成品收货无效或已结算");
        received.Add(part); stored.Add(slot, part);
        FactoryProduction.Instance.Statistics.RecordProduct();
        part.transform.SetParent(Products, true); part.SetGroundCollision(true); Refresh();
    }
    void Refresh()
    {
        if (label != null) { label.text = HasSpace ? "入库区" : "入库区已满"; label.color = FactoryGuideUI.Ink; }
    }
    void OnDestroy() { FactorySimulation.Stop(this); }
}
