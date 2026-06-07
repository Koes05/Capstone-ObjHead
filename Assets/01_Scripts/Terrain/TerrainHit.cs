using UnityEngine;

public struct TerrainHit
{
    public bool hit;
    public Vector2 point;
    public Vector2Int pixel;
    public TerrainType terrainType;

    public TerrainHit(bool hit, Vector2 point, Vector2Int pixel, TerrainType terrainType)
    {
        this.hit = hit;
        this.point = point;
        this.pixel = pixel;
        this.terrainType = terrainType;
    }

    public static TerrainHit Miss
    {
        get { return new TerrainHit(false, Vector2.zero, new Vector2Int(-1, -1), TerrainType.Empty); }
    }
}
