using System.Collections.Generic;
using UnityEngine;

public class InfiniteFloor : MonoBehaviour
{
    public Transform player;
    public Transform gameCamera;
    public Material floorMaterial;

    public float tileSize = 20f;
    public int radius = 3;

    private readonly Dictionary<Vector2Int, GameObject> tiles =
        new Dictionary<Vector2Int, GameObject>();

    private readonly HashSet<Vector2Int> required =
        new HashSet<Vector2Int>();

    private readonly List<Vector2Int> toRecycle =
        new List<Vector2Int>();

    private readonly Stack<GameObject> pool =
        new Stack<GameObject>();

    private Material gridMaterial;
    private Texture2D gridTexture;

    private void Start()
    {
        // 大块地面仍复用，纹理每个世界单位重复一次，避免创建成千上万个方块。
        tileSize = Mathf.Max(2f, Mathf.Round(tileSize * 0.5f) * 2f);
        gridTexture = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        gridTexture.wrapMode = TextureWrapMode.Repeat;
        gridTexture.filterMode = FilterMode.Trilinear;
        gridTexture.anisoLevel = 8;
        Color background = floorMaterial != null ? floorMaterial.color : new Color(0.72f, 0.74f, 0.76f);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                gridTexture.SetPixel(x, y, x < 2 || y < 2
                    ? new Color(0.28f, 0.31f, 0.34f) : background);
        gridTexture.Apply();
        gridMaterial = new Material(Shader.Find("Standard"));
        gridMaterial.mainTexture = gridTexture;
        gridMaterial.mainTextureScale = Vector2.one * tileSize;
        gridMaterial.SetFloat("_Glossiness", 0f);
        RefreshTiles();
    }

    private void Update()
    {
        RefreshTiles();
    }

    private void RefreshTiles()
    {
        required.Clear();
        AddArea(player.position);
        AddArea(gameCamera.position);

        toRecycle.Clear();

        foreach (var pair in tiles)
        {
            if (!required.Contains(pair.Key))
                toRecycle.Add(pair.Key);
        }

        foreach (Vector2Int cell in toRecycle)
        {
            GameObject tile = tiles[cell];
            tiles.Remove(cell);
            tile.SetActive(false);
            pool.Push(tile);
        }

        foreach (Vector2Int cell in required)
        {
            if (tiles.ContainsKey(cell))
                continue;

            GameObject tile = pool.Count > 0
                ? pool.Pop()
                : CreateTile();

            tile.transform.position = new Vector3(
                cell.x * tileSize,
                -0.1f,
                cell.y * tileSize
            );

            tile.SetActive(true);
            tiles.Add(cell, tile);
        }
    }

    private void AddArea(Vector3 position)
    {
        int centerX = Mathf.RoundToInt(position.x / tileSize);
        int centerZ = Mathf.RoundToInt(position.z / tileSize);

        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
                required.Add(new Vector2Int(centerX + x, centerZ + z));
        }
    }

    private GameObject CreateTile()
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = "Floor Tile";
        tile.transform.SetParent(transform);
        tile.transform.localScale = new Vector3(tileSize, 0.2f, tileSize);

        tile.GetComponent<Renderer>().sharedMaterial = gridMaterial;

        return tile;
    }

    private void OnDestroy()
    {
        if (gridMaterial != null) Destroy(gridMaterial);
        if (gridTexture != null) Destroy(gridTexture);
    }
}
