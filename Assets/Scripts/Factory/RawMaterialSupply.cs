using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RawMaterialSupply : MonoBehaviour
{
    public int Remaining => stock.Count;
    public FactoryWorkpiece Held { get; private set; }
    public bool IsFixed { get; private set; }
    public Bounds StockBounds => new Bounds(transform.TransformPoint(stockCollider.center), Vector3.Scale(stockCollider.size, transform.lossyScale));
    readonly List<FactoryWorkpiece> stock = new List<FactoryWorkpiece>();
    readonly List<FactoryWorkpiece> loose = new List<FactoryWorkpiece>();
    Transform actor;
    CharacterController actorCollider;
    FactoryProduction production;
    BoxCollider stockCollider;
    TextMeshPro label;
    float width;
    bool heldFromStock;
    Vector3 heldOrigin;
    Quaternion heldRotation;
    public const float Reach = 3.5f;

    public void Initialize(Transform player, CharacterController body, FactoryProduction factory, float partWidth, int count = 20)
    {
        actor = player; actorCollider = body; production = factory; width = partWidth;
        Vector3 offset = Vector3.ProjectOnPlane(player.right + player.forward, Vector3.up).normalized;
        transform.position = new Vector3(player.position.x, 0, player.position.z) + offset * (width + 2f);
        stockCollider = gameObject.AddComponent<BoxCollider>();
        float height = 0;
        for (int i = 0; i < count; i++)
        {
            var part = FactoryWorkpiece.Create(width, transform.position + Vector3.up * height);
            part.transform.SetParent(transform, true);
            stock.Add(part);
            height += Mathf.Max(0.025f, FactoryWorkpiece.GeometryBounds(part.transform).size.y + 0.006f);
        }
        label = new GameObject("原料拾取提示", typeof(FactoryVisual)).AddComponent<TextMeshPro>();
        label.transform.SetParent(transform, false);
        label.font = FactoryFonts.Ensure(TMP_Settings.defaultFontAsset);
        label.fontSize = 3; label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(5, 1.5f);
        RefreshStock();
    }

    bool InReach(Bounds bounds) => Vector3.Distance(actorCollider.bounds.center,
        bounds.ClosestPoint(actorCollider.bounds.center)) <= Reach;
    public void FixAt(Vector3 position)
    {
        if (IsFixed) throw new System.InvalidOperationException("原料堆的关卡位置已经固定");
        position.y = 0; transform.position = position;
        IsFixed = true; RefreshStock(); Physics.SyncTransforms();
    }
    public bool NearStock => Remaining > 0 && InReach(StockBounds);
    public bool HitsStock(Ray ray) => StockBounds.IntersectRay(ray);
    public bool Overlaps(Bounds area)
    {
        if (Remaining > 0 && area.Intersects(StockBounds)) return true;
        foreach (var part in loose)
            if (part != null && area.Intersects(FactoryWorkpiece.GeometryBounds(part.transform))) return true;
        return false;
    }

    public bool TryPickUp()
    {
        if (Held != null) return false;
        FactoryWorkpiece part = null;
        float best = Reach;
        foreach (var candidate in loose)
        {
            if (candidate == null) continue;
            float distance = Vector3.Distance(actorCollider.bounds.center,
                FactoryWorkpiece.GeometryBounds(candidate.transform).ClosestPoint(actorCollider.bounds.center));
            if (distance <= best) { best = distance; part = candidate; }
        }
        bool fromStock = part == null;
        if (part != null) loose.Remove(part);
        else if (NearStock)
        {
            part = stock[stock.Count - 1]; stock.RemoveAt(stock.Count - 1);
            RefreshStock();
        }
        if (part == null) return false;
        BeginCarry(part, fromStock);
        return true;
    }

    public bool TryPickUpStock()
    {
        if (Held != null || stock.Count == 0) return false;
        var part = stock[stock.Count - 1]; stock.RemoveAt(stock.Count - 1);
        RefreshStock(); BeginCarry(part, true); return true;
    }

    void BeginCarry(FactoryWorkpiece part, bool fromStock)
    {
        heldFromStock = fromStock; heldOrigin = part.transform.position; heldRotation = part.transform.rotation;
        foreach (var collider in part.GetComponentsInChildren<Collider>()) collider.enabled = false;
        Held = part; part.transform.SetParent(actor, true);
        production.Message("已拾取原料板；左键点击冲床上料，右键取消拾取");
    }

    public bool CancelPickUp()
    {
        if (Held == null) return false;
        var part = Held; Held = null;
        if (heldFromStock)
        {
            part.transform.SetParent(transform, true);
            stock.Add(part); RestackStock();
            RefreshStock();
        }
        else
        {
            part.transform.SetParent(production.Products, true);
            part.transform.SetPositionAndRotation(heldOrigin, heldRotation);
            part.SetGroundCollision(true); loose.Add(part);
        }
        production.Message("已取消拾取，原料已归还原处");
        Physics.SyncTransforms(); return true;
    }

    // Reparent queued plates before their withdrawn machine is destroyed.
    // Only untouched input is refunded; finished parts are never recreated as raw stock.
    public int RestoreQueuedRaw(FactoryStation station)
    {
        if (station == null || station.Kind != 0 || station.Locked || FactoryLevelSession.HasEnded ||
            FactoryLevelSession.Active != null && FactoryLevelSession.Active.IsRunning) return 0;
        int returned = 0;
        while (station.Input.Count > 0)
        {
            var part = station.Input.Dequeue();
            if (part == null) continue;
            if (part.Stage != 0)
            { part.transform.SetParent(production.Products, true); continue; }
            part.transform.SetParent(transform, true);
            part.SetGroundCollision(true);
            if (!stock.Contains(part)) { stock.Add(part); returned++; }
        }
        RestackStock(); RefreshStock(); Physics.SyncTransforms();
        return returned;
    }

    void RestackStock()
    {
        float height = 0;
        foreach (var part in stock)
        {
            part.transform.SetPositionAndRotation(transform.position + Vector3.up * height, Quaternion.identity);
            height += Mathf.Max(.025f, FactoryWorkpiece.GeometryBounds(part.transform).size.y + .006f);
        }
    }

    public bool TryMove(Vector3 position, IList<GameObject> equipment, FinishedGoodsArea finishedArea)
    {
        if (Held != null || IsFixed) return false;
        position.y = 0;
        Bounds moved = StockBounds; moved.center += position - transform.position;
        if (moved.Intersects(actorCollider.bounds)) return false;
        foreach (var item in equipment)
            if (item != null && OverlapXZ(moved, item.GetComponent<EquipmentGrid>().Footprint)) return false;
        if (finishedArea != null && OverlapXZ(moved, finishedArea.Footprint)) return false;
        foreach (var part in loose)
            if (part != null && OverlapXZ(moved, FactoryWorkpiece.GeometryBounds(part.transform))) return false;
        transform.position = position; Physics.SyncTransforms(); return true;
    }
    static bool OverlapXZ(Bounds a, Bounds b) => a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;

    public bool TryFeed(FactoryStation station, bool birdMode = false)
    {
        if (Held == null || station == null || station.Kind != 0) return false;
        if (!birdMode && Vector3.Distance(actorCollider.bounds.center, station.Ports.InputPort.position) > Reach + width)
        { production.Message("请靠近冲床输入口后再放置"); return false; }
        if (!station.CanAccept) { production.Message("冲床队列已满，请稍后再放置"); return false; }
        var part = Held; Held = null;
        part.transform.SetParent(null, true); part.transform.rotation = Quaternion.identity;
        station.Enqueue(part);
        production.Message("原料已入队，冲床将顺次处理；剩余库存 " + Remaining);
        return true;
    }

    public bool TryDrop(Vector3 point, IList<GameObject> equipment)
    {
        if (Held == null) return false;
        if (Vector3.Distance(new Vector3(actor.position.x, 0, actor.position.z), point) > Reach + width)
        { production.Message("放置位置太远，请走近后再放置"); return false; }
        Bounds area = new Bounds(point + Vector3.up * 0.03f, new Vector3(width, 0.06f, width));
        if (area.Intersects(actorCollider.bounds) || Overlaps(area))
        { production.Message("放置位置被占用，请选择附近空地"); return false; }
        foreach (var item in equipment)
            if (item != null && area.Intersects(item.GetComponent<EquipmentGrid>().Footprint))
            { production.Message("请对准冲床输入口，或选择没有设备的空地"); return false; }
        var part = Held; Held = null;
        part.transform.SetParent(production.Products, true); part.transform.SetPositionAndRotation(point, Quaternion.identity);
        var box = part.GetComponent<BoxCollider>();
        if (box == null) box = part.gameObject.AddComponent<BoxCollider>();
        Bounds bounds = FactoryWorkpiece.GeometryBounds(part.transform);
        box.center = part.transform.InverseTransformPoint(bounds.center);
        box.size = new Vector3(bounds.size.x, Mathf.Max(0.025f, bounds.size.y), bounds.size.z);
        box.enabled = true; loose.Add(part);
        production.Message("原料板已放下；靠近按 F 可重新拾取");
        Physics.SyncTransforms(); return true;
    }

    public void UpdateHeld(Camera camera, bool birdMode = false)
    {
        if (Held != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = actor.forward;
            Held.transform.SetPositionAndRotation(actorCollider.bounds.center +
                forward * (actorCollider.radius * actor.lossyScale.x + width * 0.55f), Quaternion.identity);
            if (birdMode)
            {
                Ray ray = camera.ScreenPointToRay(Input.mousePosition);
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
                    Held.transform.position = ray.GetPoint(distance) + Vector3.up * .6f;
            }
        }
        if (label != null) label.transform.rotation = camera.transform.rotation;
    }

    void RefreshStock()
    {
        stockCollider.enabled = stock.Count > 0;
        if (stock.Count > 0)
        {
            Bounds bounds = FactoryWorkpiece.GeometryBounds(stock[0].transform);
            foreach (var part in stock) bounds.Encapsulate(FactoryWorkpiece.GeometryBounds(part.transform));
            stockCollider.center = transform.InverseTransformPoint(bounds.center);
            stockCollider.size = bounds.size;
            label.transform.position = bounds.center + Vector3.up * (bounds.extents.y + 0.65f);
        }
        label.text = stock.Count > 0 ? "原料" : "原料已用完";
        label.color = FactoryGuideUI.Ink;
    }
}
