using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A canvas-native plot: no world cameras, textures or external chart packages.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FactoryRateChart : MaskableGraphic
{
    List<Vector2> points;
    float start, end, maximum = 1;
    TMP_Text topLabel, startLabel, endLabel;
    public void Initialize()
    {
        raycastTarget = false;
        Vector2 size = rectTransform.sizeDelta;
        topLabel = FactoryGuideUI.Text(transform, "Maximum", "1", new Vector2(-size.x * .5f + 22, size.y * .5f - 13), new Vector2(44, 24), 15, FactoryGuideUI.Muted, TextAlignmentOptions.Center);
        startLabel = FactoryGuideUI.Text(transform, "StartTime", "0秒", new Vector2(-size.x * .5f + 65, -size.y * .5f + 10), new Vector2(100, 22), 15, FactoryGuideUI.Muted);
        endLabel = FactoryGuideUI.Text(transform, "EndTime", "60秒", new Vector2(size.x * .5f - 60, -size.y * .5f + 10), new Vector2(100, 22), 15, FactoryGuideUI.Muted, TextAlignmentOptions.Right);
    }
    public void Show(List<Vector2> values, float from, float to)
    {
        points = values; start = from; end = Mathf.Max(from + 1, to); maximum = 1;
        if (points != null) foreach (var point in points)
            if (point.x >= start && point.x <= end) maximum = Mathf.Max(maximum, point.y * 1.15f);
        topLabel.text = maximum.ToString("0.#");
        startLabel.text = start.ToString("0") + "秒"; endLabel.text = end.ToString("0") + "秒";
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); Rect r = rectTransform.rect;
        Rect plot = Rect.MinMaxRect(r.xMin + 48, r.yMin + 28, r.xMax - 12, r.yMax - 12);
        for (int i = 0; i <= 4; i++)
        {
            float y = Mathf.Lerp(plot.yMin, plot.yMax, i / 4f);
            Line(mesh, new Vector2(plot.xMin, y), new Vector2(plot.xMax, y), 1, new Color(.12f, .14f, .11f, .2f));
            float x = Mathf.Lerp(plot.xMin, plot.xMax, i / 4f);
            Line(mesh, new Vector2(x, plot.yMin), new Vector2(x, plot.yMax), 1, new Color(.12f, .14f, .11f, .12f));
        }
        if (points == null) return;
        bool previousValid = false; Vector2 previous = Vector2.zero;
        foreach (var point in points)
        {
            if (point.x < start || point.x > end) continue;
            var p = new Vector2(Mathf.Lerp(plot.xMin, plot.xMax, (point.x - start) / (end - start)),
                Mathf.Lerp(plot.yMin, plot.yMax, Mathf.Clamp01(point.y / maximum)));
            if (previousValid) Line(mesh, previous, p, 2.5f, color);
            else Line(mesh, p - Vector2.right * 2, p + Vector2.right * 2, 3, color);
            previous = p; previousValid = true;
        }
    }
    static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        if (normal.sqrMagnitude < .00001f) return;
        int first = mesh.currentVertCount;
        mesh.AddVert(a - normal, tint, Vector2.zero); mesh.AddVert(a + normal, tint, Vector2.zero);
        mesh.AddVert(b + normal, tint, Vector2.zero); mesh.AddVert(b - normal, tint, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first + 2, first + 3, first);
    }
}

