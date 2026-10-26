using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FactorySmoothnessAudit
{
    static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    // Regression for the actual travel sampler, including a long frame that
    // crosses multiple junctions. These checks run without changing a scene.
    [MenuItem("Tools/Factory/Check Continuous Rail Motion")]
    public static void CheckPaths()
    {
        var points = new List<Vector3> { Vector3.zero, Vector3.zero,
            Vector3.right, Vector3.right, new Vector3(1, 0, 1), new Vector3(2, 0, 1) };
        foreach (float frame in new[] { 1f / 30, 1f / 60, 1f / 120, 4f / 30 })
        {
            var travel = new FactoryTravelPath(points, Vector3.zero, 0);
            float distance = 0;
            for (int i = 0; !travel.Complete && i < 1000; i++)
            {
                distance = Mathf.Min(3, distance + frame);
                Vector3 expected = distance <= 1 ? new Vector3(distance, 0, 0) :
                    distance <= 2 ? new Vector3(1, 0, distance - 1) : new Vector3(distance - 1, 0, 1);
                Check(Vector3.Distance(travel.Advance(frame), expected) < .0001f,
                    "Rail distance lost or corner cut at frame interval " + frame);
            }
            Check(travel.Complete && travel.Position == new Vector3(2, 0, 1), "Travel must finish exactly at the dock");
        }
        var longFrame = new FactoryTravelPath(points, Vector3.zero, 0);
        Check(Vector3.Distance(longFrame.Advance(2.5f), new Vector3(1.5f, 0, 1)) < .0001f,
            "A long frame must spend its remainder after multiple corners");
        Vector3 paused = longFrame.Position;
        Check(longFrame.Advance(0) == paused, "Pause must preserve the position");
        var empty = new FactoryTravelPath(new List<Vector3>(), Vector3.one, 0);
        Check(empty.Complete && empty.Advance(10) == Vector3.one, "Empty paths must preserve the base");
        // Exercise hot bounds queries after the reusable buffers have warmed up.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        long allocated;
        try
        {
            FactoryMotion.Bounds(cube.transform);
            FactoryWorkpiece.GeometryBounds(cube.transform);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                FactoryMotion.Bounds(cube.transform);
                FactoryWorkpiece.GeometryBounds(cube.transform);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated < 1024, "Bounds queries allocate temporary storage: " + allocated);
            cube.transform.position = new Vector3(3, 4, 5);
            Check(Vector3.Distance(FactoryMotion.Bounds(cube.transform).center, cube.transform.position) < .0001f,
                "Bounds must reflect moved models, not cached world poses");
        }
        finally { UnityEngine.Object.DestroyImmediate(cube); }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/factory-smoothness-path-audit.txt",
            "PASS 30/60/120 Hz and 4x frame intervals\nPASS duplicate nodes, exact docks, no corner cutting\nPASS multi-edge frame remainder, paused and empty paths\n" +
            "PASS 2000 warm bounds queries allocated bytes=" + allocated + "\nPASS moved model bounds refresh correctly\nPASS ALL\n");
        Debug.Log("Continuous rail motion: PASS ALL");
    }

    public static void RunBatch()
    {
        try { CheckPaths(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    public static void RunWorkflow()
    {
        CheckPaths();
        FactoryRailTurnAudit.AssertTopology();
        FactoryProductionAudit.RunGameplay();
    }
    public static void RunStorage()
    {
        CheckPaths();
        FactoryProductionAudit.RunStorage();
    }
}
