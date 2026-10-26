using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// One fixed source outside a visible boundary, with a compact module-sized run to storage.
public sealed class FactoryLevelLayout : MonoBehaviour
{
    public const float ModuleSpacing = 3f;
    public const float EndClearance = 3f;
    public const float BoundaryWidth = .22f;
    public float RequiredModulesWidth { get; private set; }
    public float BoundaryX { get; private set; }
    public float Scale { get; private set; }
    public LineRenderer Boundary { get; private set; }
    Material boundaryMaterial;

    public void Initialize(Transform player, CharacterController body, GameObject[] prefabs,
        float scale, RawMaterialSupply supply, FactoryProduction production)
    {
        Scale = scale;
        float sourceOffset = Mathf.Ceil(body.radius * player.lossyScale.x + scale * .4f + 1f);
        Vector3 source = new Vector3(Mathf.Floor(player.position.x) + .5f + sourceOffset, 0,
            Mathf.Floor(player.position.z) + .5f);
        supply.FixAt(source);
        float depth = 0;
        for (int kind = 0; kind < 3; kind++)
        {
            if (kind >= prefabs.Length || prefabs[kind] == null)
                throw new System.InvalidOperationException("开局布局缺少必要的加工模块");
            var probe = Instantiate(prefabs[kind]);
            var grid = probe.AddComponent<EquipmentGrid>(); grid.Initialize(scale);
            RequiredModulesWidth += grid.Cells.x;
            depth = Mathf.Max(depth, grid.Cells.y);
            probe.SetActive(false); Destroy(probe);
        }
        BoundaryX = Mathf.Ceil(supply.StockBounds.max.x + EndClearance);
        // One grid cell inside the boundary, two rail-sized gaps, and clearance before storage.
        float rowEnd = BoundaryX + 1 + RequiredModulesWidth + ModuleSpacing * 2;
        float destinationX = Mathf.Ceil(rowEnd + EndClearance + FinishedGoodsArea.SideLength(scale) * .5f - .5f) + .5f;
        production.CreateFinishedArea(new Vector3(destinationX, 0, source.z), scale);

        var boundaryRoot = new GameObject("原料区与产线布置边界", typeof(FactoryVisual));
        boundaryRoot.transform.SetParent(transform, false);
        Boundary = boundaryRoot.AddComponent<LineRenderer>();
        Boundary.useWorldSpace = true; Boundary.positionCount = 2;
        Boundary.SetPosition(0, new Vector3(BoundaryX, .085f, source.z - depth * .5f - EndClearance));
        Boundary.SetPosition(1, new Vector3(BoundaryX, .085f, source.z + depth * .5f + EndClearance));
        Boundary.startWidth = Boundary.endWidth = BoundaryWidth;
        boundaryMaterial = new Material(Shader.Find("Sprites/Default")) { color = FactoryGuideUI.Mint };
        Boundary.sharedMaterial = boundaryMaterial;
        Boundary.shadowCastingMode = ShadowCastingMode.Off; Boundary.receiveShadows = false;
        var label = new GameObject("原料区边界提示", typeof(FactoryVisual), typeof(FactoryPortLabel)).AddComponent<TextMeshPro>();
        label.transform.SetParent(boundaryRoot.transform, false);
        label.transform.position = new Vector3(BoundaryX, 1.2f, source.z - depth * .5f - EndClearance - 1.5f);
        label.font = FactoryFonts.Ensure(TMP_Settings.defaultFontAsset); label.fontSize = 3;
        label.color = FactoryGuideUI.Ink; label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(12, 2.5f);
        label.text = "原料边界";
    }
    // Check the entire footprint against the source-side edge, including beyond the line ends.
    public bool AllowsRawPlacement(Bounds footprint) => footprint.max.x <= BoundaryX - BoundaryWidth * .5f;

    void OnDestroy() { if (boundaryMaterial != null) Destroy(boundaryMaterial); }
}

