using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class GridPlacementAudit
{
    [MenuItem("Tools/Placement/Validate Grid Footprints")]
    public static void Validate()
    {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var placement = Object.FindObjectOfType<PlacementSystem>();
            if (placement == null) return;
            var report = new StringBuilder();
            var controller = Object.FindObjectOfType<GameController>();
            float playerHeight = EquipmentGrid.VisualBounds(controller.model.gameObject).size.y;
            var arm = Object.Instantiate(placement.prefabs[3]);
            float armHeight = EquipmentGrid.VisualBounds(arm).size.y;
            Object.DestroyImmediate(arm);
            float scale = Mathf.Max(1f, playerHeight * 1.5f / armHeight);
            report.AppendLine($"Player height={playerHeight:F4}, arm original height={armHeight:F4}, scale={scale:F4}");
            foreach (var prefab in placement.prefabs)
            {
                var obj = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
                try
                {
                    foreach (var r in obj.GetComponentsInChildren<Renderer>())
                        if (r.name.Contains("地板")) report.AppendLine($"Base {r.name}: {r.bounds.size}");
                    var grid = obj.AddComponent<EquipmentGrid>();
                    grid.Initialize(scale);
                    report.AppendLine($"{prefab.name}: {grid.Cells}, visual={EquipmentGrid.VisualBounds(obj).size}");
                    foreach (Vector3 target in new[] { Vector3.zero, new Vector3(-10.2f, 0, 7.7f) })
                    {
                        for (int rotation = 0; rotation < 4; rotation++)
                        {
                            obj.transform.rotation = Quaternion.Euler(0, rotation * 90, 0);
                            grid.Snap(target);
                            Bounds b = grid.Footprint;
                            Require(Mathf.Abs(b.min.x - Mathf.Round(b.min.x)) < 0.001f, "X alignment");
                            Require(Mathf.Abs(b.min.z - Mathf.Round(b.min.z)) < 0.001f, "Z alignment");
                            Require(Mathf.Abs(b.min.y) < 0.001f, "Ground alignment");
                            Require(Mathf.Abs(b.size.x - Mathf.Round(b.size.x)) < 0.001f, "Integer width");
                            Require(Mathf.Abs(b.size.z - Mathf.Round(b.size.z)) < 0.001f, "Integer depth");
                        }
                    }
                }
                finally { Object.DestroyImmediate(obj); }
            }
            report.AppendLine("PASS: All 5 equipment footprints align to integer grid at positive/negative positions and all four rotations.");
            File.WriteAllText("Logs/grid-measurements.txt", report.ToString());
            Debug.Log(report.ToString());
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new System.Exception(message);
    }
}
