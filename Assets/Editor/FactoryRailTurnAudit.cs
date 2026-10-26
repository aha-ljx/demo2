using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public static class FactoryRailTurnAudit
{
    public static void AssertTopology()
    {
        var report = new StringBuilder();
        Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); report.AppendLine("PASS " + message); };
        for (int yaw = 0; yaw < 360; yaw += 90)
        {
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            var a = Make(Vector3.zero, yaw);
            var b = Make(rotation * new Vector3(5, 0, 4), yaw + 90);
            try
            {
                var graph = new FactoryRailGraph(); graph.Build(new List<FactoryRail> { a, b });
                check(graph.Same(a, b), "offset L joint with a one-unit shared edge connects at yaw " + yaw);
                check(graph.Route(a, a.A, b.Closest(b.transform.position), 0.02f, out var path), "route traverses the rotated L joint");
                AssertInside(path, a, b, check);
                check(graph.Route(b, b.Closest(b.transform.position), a.A, 0.02f, out path), "rotated L joint is traversable in reverse");
                AssertInside(path, a, b, check);
                Move(b, rotation * new Vector3(0, 0, 5)); graph.Build(new List<FactoryRail> { a, b });
                check(graph.Same(a, b) && graph.Route(a, a.A, b.B, .02f, out path), "end-to-side T junction connects at yaw " + yaw);
                AssertInside(path, a, b, check);
                Move(b, rotation * new Vector3(5, 0, 5)); graph.Build(new List<FactoryRail> { a, b });
                check(!graph.Same(a, b), "corner-only contact does not count as a connected track");
                Move(b, rotation * new Vector3(6, 0, 4)); graph.Build(new List<FactoryRail> { a, b });
                check(!graph.Same(a, b) && !graph.Route(a, a.A, b.B, .02f, out _), "a missing strip cannot be crossed");
            }
            finally { UnityEngine.Object.DestroyImmediate(a.gameObject); UnityEngine.Object.DestroyImmediate(b.gameObject); }
        }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/factory-rail-turns-audit.txt", report + "PASS ALL\n");
    }
    static FactoryRail Make(Vector3 position, float yaw)
    {
        var root = new GameObject("Turn audit rail");
        var hardware = GameObject.CreatePrimitive(PrimitiveType.Cube); hardware.transform.SetParent(root.transform, false);
        hardware.transform.localScale = new Vector3(8, .2f, 2);
        root.AddComponent<EquipmentGrid>().Initialize(1);
        root.transform.rotation = Quaternion.Euler(0, yaw, 0);
        root.GetComponent<EquipmentGrid>().Snap(position);
        var rail = root.AddComponent<FactoryRail>(); rail.Measure(); return rail;
    }
    static void Move(FactoryRail rail, Vector3 point) { rail.GetComponent<EquipmentGrid>().Snap(point); rail.Measure(); }
    static void AssertInside(List<Vector3> path, FactoryRail a, FactoryRail b, Action<bool, string> check)
    {
        bool inside = true;
        for (int i = 1; i < path.Count; i++)
            for (int step = 0; step <= 32; step++)
            {
                Vector3 p = Vector3.Lerp(path[i - 1], path[i], step / 32f);
                inside &= Contains(a.GetComponent<EquipmentGrid>().Footprint, p) || Contains(b.GetComponent<EquipmentGrid>().Footprint, p);
            }
        check(inside, "junction waypoints stay inside the physical footprint union");
    }
    static bool Contains(Bounds bounds, Vector3 p) => p.x >= bounds.min.x - .031f && p.x <= bounds.max.x + .031f &&
        p.z >= bounds.min.z - .031f && p.z <= bounds.max.z + .031f;
}
