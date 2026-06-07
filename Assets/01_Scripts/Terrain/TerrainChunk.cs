using UnityEngine;

public class TerrainChunk : MonoBehaviour
{
    private TerrainManager owner;
    private Vector2Int chunkCoord;
    private RectInt pixelRect;
    private int collisionCellSizePx;

    public Vector2Int ChunkCoord
    {
        get { return chunkCoord; }
    }

    public RectInt PixelRect
    {
        get { return pixelRect; }
    }

    public void Initialize(TerrainManager terrainOwner, Vector2Int coord, RectInt rect, int cellSizePx)
    {
        owner = terrainOwner;
        chunkCoord = coord;
        pixelRect = rect;
        collisionCellSizePx = Mathf.Max(1, cellSizePx);
    }

    public void RebuildColliders()
    {
        ClearColliders();

        if (owner == null || pixelRect.width <= 0 || pixelRect.height <= 0)
        {
            return;
        }

        int rowCount = Mathf.CeilToInt(pixelRect.height / (float)collisionCellSizePx);
        int columnCount = Mathf.CeilToInt(pixelRect.width / (float)collisionCellSizePx);

        for (int row = 0; row < rowCount; row++)
        {
            int y = pixelRect.yMin + row * collisionCellSizePx;
            int height = Mathf.Min(collisionCellSizePx, pixelRect.yMax - y);
            int runStart = -1;

            for (int column = 0; column <= columnCount; column++)
            {
                bool solid = false;

                if (column < columnCount)
                {
                    int x = pixelRect.xMin + column * collisionCellSizePx;
                    int width = Mathf.Min(collisionCellSizePx, pixelRect.xMax - x);
                    solid = owner.AnySolidInPixelRect(x, y, width, height);
                }

                if (solid && runStart < 0)
                {
                    runStart = column;
                }
                else if (!solid && runStart >= 0)
                {
                    CreateRunCollider(runStart, column, row, height);
                    runStart = -1;
                }
            }
        }
    }

    private void CreateRunCollider(int startColumn, int endColumn, int row, int height)
    {
        int x = pixelRect.xMin + startColumn * collisionCellSizePx;
        int y = pixelRect.yMin + row * collisionCellSizePx;
        int endX = Mathf.Min(pixelRect.xMin + endColumn * collisionCellSizePx, pixelRect.xMax);
        int width = endX - x;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
        box.offset = owner.PixelRectCenterToLocal(x, y, width, height);
        box.size = owner.PixelSizeToWorldSize(width, height);
    }

    private void ClearColliders()
    {
        BoxCollider2D[] colliders = GetComponents<BoxCollider2D>();
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
            {
                Destroy(colliders[i]);
            }
            else
            {
                DestroyImmediate(colliders[i]);
            }
        }
    }
}
